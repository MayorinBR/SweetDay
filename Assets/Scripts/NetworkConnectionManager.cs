using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PlayerType
{
    Runner,
    Catcher
}

public class NetworkConnectionManager : NetworkBehaviour
{
    public static NetworkConnectionManager Instance { get; private set; }

    // Limites de jogadores
    public const int MAX_PLAYERS = 4;
    public const int MAX_GUARDS = 2;

    public NetworkVariable<int> totalPlayers = new NetworkVariable<int>(0);
    public NetworkVariable<int> totalGuards = new NetworkVariable<int>(0);

    private Lobby _currentLobby;
    private string _playerLobbyId;
    private string _lobbyCode = "";
    private bool _isInitialized = false;
    private bool _isNetcodeConfigured = false;
    public string LobbyCode => _lobbyCode;
    public bool IsInitialized => _isInitialized;

    private UIManager _uiManager;
    private GameManager _gameManager;

    private PlayerType _selectedPlayerType = PlayerType.Runner;
    private Dictionary<ulong, PlayerType> _clientPlayerTypes = new Dictionary<ulong, PlayerType>();
    private int _playerCount = 0;
    private int _maxPlayers = 0;

    // Constante para Heartbeat do Lobby
    private const float HEARTBEAT_INTERVAL = 10f;
    private Coroutine _heartbeatCoroutine;

    private Dictionary<ulong, PlayerType> _activeClientCount = new Dictionary<ulong, PlayerType>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    void Start()
    {
        // Tenta inicializar os serviços na inicialização.
        InitializeUnityServices();

        // Configurações iniciais do NetworkManager, se estiver disponível
        if (NetworkManager.Singleton != null)
        {
            RegisterNetworkCallbacks();
        }
    }

    public async Task InitializeUnityServices()
    {
        if (_isInitialized) return;

        try
        {
            // Inicializa os serviços da Unity
            await UnityServices.InitializeAsync();

            // Autentica o jogador anonimamente
            AuthenticationService.Instance.SignedIn += () =>
            {
                _playerLobbyId = AuthenticationService.Instance.PlayerId;
                //Debug.Log($"Autenticação bem-sucedida. Player ID: {_playerLobbyId}");
            };

            await AuthenticationService.Instance.SignInAnonymouslyAsync();

            _isInitialized = true;
            //Debug.Log("Unity Services inicializados com sucesso.");
        }
        catch (Exception e)
        {
            //Debug.LogError($"Erro ao inicializar Unity Services: {e}");
        }
    }

    // Método para configurar o NetworkManager quando estiver pronto
    public void ConfigureNetworkManager()
    {
        if (NetworkManager.Singleton == null || _isNetcodeConfigured) return;

        //Debug.Log("Configuring NetworkManager with Connection Approval");
        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        _isNetcodeConfigured = true; // Marca como configurado
    }

    private void RegisterNetworkCallbacks()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        //Debug.Log("Network Server iniciado.");
        // O Host (server) também se conecta a si mesmo, então o OnClientConnected também será chamado.
    }

    new void OnDestroy()
    {
        if (NetworkManager.Singleton != null && _isInitialized)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ====================================================================
    // Connection Approval
    // ====================================================================
    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        //Debug.Log($"Connection approval request from client {request.ClientNetworkId}");

        // Verifica se o servidor está cheio
        int currentTotalPlayers = totalPlayers.Value + totalGuards.Value;
        int maxTotalPlayers = MAX_PLAYERS + MAX_GUARDS;

        if (currentTotalPlayers >= maxTotalPlayers)
        {
            response.Approved = false;
            response.Reason = "Server is full";
            //Debug.Log($"Connection rejected: Server full ({currentTotalPlayers}/{maxTotalPlayers})");
            return;
        }

        byte[] connectionData = request.Payload;
        PlayerType clientSelectedType = PlayerType.Runner;

        if (connectionData != null && connectionData.Length > 0)
        {
            int playerTypeIndex = connectionData[0];
            if (Enum.IsDefined(typeof(PlayerType), playerTypeIndex))
            {
                clientSelectedType = (PlayerType)playerTypeIndex;
            }
        }

        if (NetworkManager.Singleton.IsHost && request.ClientNetworkId == NetworkManager.Singleton.LocalClientId)
        {
            // O HOST sempre usa o _selectedPlayerType local
            if (!_clientPlayerTypes.ContainsKey(request.ClientNetworkId))
            {
                _clientPlayerTypes.Add(request.ClientNetworkId, _selectedPlayerType);
                Debug.Log($"=== HOST APROVADO - Tipo: {_selectedPlayerType} ===");
            }

            response.Approved = true;
            response.CreatePlayerObject = false;
            response.Pending = false;
            return;
        }

        if (!_clientPlayerTypes.ContainsKey(request.ClientNetworkId))
        {
            _clientPlayerTypes.Add(request.ClientNetworkId, clientSelectedType);
            //Debug.Log($"PlayerType {clientSelectedType} armazenado para Client: {request.ClientNetworkId}");
        }

        // Aprova a conexão
        response.Approved = true;
        response.CreatePlayerObject = false;
        response.Pending = false;

        //Debug.Log($"Connection approved for client {request.ClientNetworkId}");
    }
    private void OnClientConnected(ulong clientId)
    {
        //Debug.Log($"Client {clientId} conectado.");

        if (IsServer)
        {
            // Lógica do Servidor: Adicionar o cliente ao gerenciamento
            HandleClientConnected(clientId);

            // Atualiza o lobby com o número de jogadores atualizado
            if (_currentLobby != null)
            {
                // Nota: A atualização do Lobby no servidor deve ser feita periodicamente (Heartbeat)
                // ou através de um coroutine de verificação de jogadores
            }
        }
        else
        {
            // Adiciona a lógica de UI para o cliente, se necessário, mas a cena já está carregada
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        //Debug.Log($"Client {clientId} desconectado.");

        if (IsServer)
        {
            // Lógica do Servidor: Remover o cliente do gerenciamento
            HandleClientDisconnected(clientId);
        }

        // Se o host se desconectar, todos os clientes locais devem ser notificados
        if (clientId == NetworkManager.ServerClientId)
        {
            // A UI pode mostrar uma mensagem de erro ou retornar ao menu principal
            Disconnect(true);
            //Debug.Log("Host desconectou. Voltando ao menu principal.");
        }
    }

    public byte[] GetConnectionPayload()
    {
        byte playerTypeIndex = (byte)_selectedPlayerType;
        return new byte[] { playerTypeIndex };
    }

    // ====================================================================
    // Ciclo de Vida do Netcode
    // ====================================================================

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        //Debug.Log("NetworkConnectionManager spawned on network");

        _uiManager = FindFirstObjectByType<UIManager>();
        _gameManager = FindFirstObjectByType<GameManager>();

        totalPlayers.OnValueChanged += OnPlayerCountChanged;
        totalGuards.OnValueChanged += OnPlayerCountChanged;

        UpdatePlayerCounter();

        // Notifica o UI após spawn
        if (IsServer)
        {
            Invoke(nameof(DelayedLobbyUpdate), 1f);
        }
        /*
        if (IsHost)
        {
            ulong hostClientId = NetworkManager.Singleton.LocalClientId;
            // Verifica se o cliente host não tem um player object
            if (NetworkManager.Singleton.ConnectedClients[hostClientId].PlayerObject == null)
            {
                if (_gameManager != null)
                {
                    PlayerType playerType = GetPlayerType(hostClientId);
                    _gameManager.SpawnPlayerForClient(hostClientId, playerType);
                    //Debug.Log($"Spawnando host player {hostClientId} como {playerType} em OnNetworkSpawn");
                }
            }
        }
        */
    }

    private void DelayedLobbyUpdate()
    {
        if (IsServer && IsSpawned)
        {
            UpdateLobbyTextsClientRpc(_lobbyCode);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        totalPlayers.OnValueChanged -= OnPlayerCountChanged;
        totalGuards.OnValueChanged -= OnPlayerCountChanged;
    }

    // ====================================================================
    // Lógica de Conexão e Lobby
    // ====================================================================

    public PlayerType GetPlayerType(ulong clientId)
    {
        if (_clientPlayerTypes.ContainsKey(clientId))
        {
            return _clientPlayerTypes[clientId];
        }
        return PlayerType.Runner;
    }

    public void SetPlayerType(bool isCatcher)
    {
        _selectedPlayerType = isCatcher ? PlayerType.Catcher : PlayerType.Runner;
        Debug.Log($"Set Player Type to: {_selectedPlayerType}");
    }

    public async void StartHostWithScene(string sceneName)
    {
        try
        {
            //Debug.Log($"=== INICIANDO HOST COMO: {_selectedPlayerType} ===");

            if (!_isInitialized)
            {
                await InitializeUnityServices();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            // 1. CONFIGURAR RELAY
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS + MAX_GUARDS);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            //Debug.Log($"Relay criado: {joinCode}");

            // Configurar transporte
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            // 2. CRIAR LOBBY COM INFORMAÇÃO DA CENA
            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
            {
                { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                { "SceneName", new DataObject(DataObject.VisibilityOptions.Member, sceneName) }
            }
            };

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync($"Lobby_{joinCode}", MAX_PLAYERS + MAX_GUARDS, options);
            _lobbyCode = _currentLobby.LobbyCode;

            //Debug.Log($"Lobby criado: {_lobbyCode}");

            ConfigureNetworkManager();

            byte[] connectionPayload = GetConnectionPayload();
            Debug.Log($"=== VERIFICAÇÃO FINAL - Payload do Host: {(PlayerType)connectionPayload[0]} ===");
            NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;

            //Debug.Log($"Payload de conexão configurado: {(PlayerType)connectionPayload[0]}");

            // 3. INICIAR HOST
            if (NetworkManager.Singleton.StartHost())
            {
                //Debug.Log("Host iniciado com sucesso!");

                ulong hostClientId = NetworkManager.Singleton.LocalClientId;

                // CORREÇÃO: Garantir que o host é registrado com o tipo correto
                if (!_clientPlayerTypes.ContainsKey(hostClientId))
                {
                    _clientPlayerTypes.Add(hostClientId, _selectedPlayerType);
                    //Debug.Log($"Tipo de jogador do Host {hostClientId} registrado como: {_selectedPlayerType}");
                }

                // 4. TROCA DE CENA
                NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

                // 5. ATUALIZAR UI
                if (_uiManager != null)
                {
                    _uiManager.ShowLobbyUI(true, _lobbyCode);
                }

                // 6. INICIAR HEARTBEAT
#pragma warning disable 4014
                HeartbeatLobbyCoroutine();
#pragma warning restore 4014
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ERRO no Host: {e.Message}");
        }
    }

    public async void StartClientWithCode(string joinCode)
    {
        try
        {
            //Debug.Log($"=== CONECTANDO COMO CLIENT: {joinCode} ===");

            if (!_isInitialized)
            {
                await InitializeUnityServices();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            // 1. ENTRAR NO LOBBY
            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);
            _currentLobby = lobby;

            // 2. OBTER INFORMAÇÕES DO LOBBY
            string relayJoinCode = _currentLobby.Data["RelayJoinCode"].Value;
            string sceneName = _currentLobby.Data["SceneName"].Value;

            //Debug.Log($"Conectando ao Relay: {relayJoinCode}, Cena: {sceneName}");

            // 3. CONFIGURAR CLIENTE RELAY
            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            byte playerTypeByte = (byte)_selectedPlayerType;
            if (NetworkManager.Singleton.NetworkConfig != null)
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionData = new byte[] { playerTypeByte };
            }

            byte[] connectionPayload = GetConnectionPayload();
            NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;

            // 4. INICIAR CLIENTE
            if (NetworkManager.Singleton.StartClient())
            {
                //Debug.Log("Cliente conectado com sucesso!");

                // 5. ATUALIZAR UI PARA MODO LOBBY
                if (_uiManager != null)
                {
                    _uiManager.ShowLobbyUI(false, joinCode);
                }
            }
        }
        catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
        {
            Debug.LogError($"Lobby não encontrado: {joinCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"ERRO no Client: {e.Message}");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void LoadGameSceneServerRpc(string sceneName)
    {
        if (!IsServer) return;

        //Debug.Log($"Host solicitando troca para cena: {sceneName}");

        // Notificar todos os clientes
        NotifySceneChangeClientRpc(sceneName);

        // Carregar cena para todos
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    [ClientRpc]
    private void NotifySceneChangeClientRpc(string sceneName)
    {
        //Debug.Log($"Recebido comando para trocar para cena: {sceneName}");

        // Opcional: Mostrar tela de loading
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowLoadingMessage($"Carregando: {sceneName}...");
        }
    }

    private async Task HeartbeatLobbyCoroutine()
    {
        while (_currentLobby != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            bool heartbeatSuccess = await SendHeartbeatWithRetry();

            if (heartbeatSuccess)
            {
                UpdateLobbyTextsClientRpc(_lobbyCode);
                //Debug.Log("Heartbeat enviado para o lobby");
            }
            else
            {
                Debug.LogWarning("Falha no heartbeat, tentando recriar lobby...");
                await RecreateLobbyWithSameCode();
            }

            await Task.Delay(TimeSpan.FromSeconds(HEARTBEAT_INTERVAL));
        }
    }

    private async Task<bool> SendHeartbeatWithRetry()
    {
        int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Tentativa {attempt + 1} de heartbeat falhou: {e.Message}");

                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(1000 * (attempt + 1)); // Backoff exponencial
                }
            }
        }

        return false;
    }

    private async Task<bool> RecreateLobbyWithSameCode()
    {
        try
        {
            // Tenta recriar o lobby com o mesmo código
            CreateLobbyOptions createOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Player(AuthenticationService.Instance.PlayerId, null, new Dictionary<string, PlayerDataObject> {
                {"PlayerType", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, _selectedPlayerType.ToString())}
            }),
                Data = new Dictionary<string, DataObject>
            {
                { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, _lobbyCode) }
            }
            };

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync($"Lobby_{_lobbyCode}", MAX_PLAYERS + MAX_GUARDS, createOptions);
            //Debug.Log($"Lobby recriado com sucesso. Código: {_lobbyCode}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Falha ao recriar lobby: {e}");
            return false;
        }
    }

    public async Task LeaveLobby()
    {
        if (_currentLobby != null)
        {
            try
            {
                // Se for o Host, exclui o lobby
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                }
                // Se for Client, sai do lobby
                else if (!string.IsNullOrEmpty(_currentLobby.Id))
                {
                    await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Erro ao sair do lobby: {e}");
            }

            _currentLobby = null;
            _lobbyCode = "";
            //Debug.Log("Lobby abandonado/excluído.");
        }
    }

    // ====================================================================
    // Sincronização e Handlers
    // ====================================================================
    private void OnPlayerCountChanged(int previous, int current)
    {
        //Debug.Log($"Player count changed: {previous} -> {current}. Current total players: {totalPlayers.Value + totalGuards.Value}");
        UpdatePlayerCounter();
    }

    private void HandleClientConnected(ulong clientId)
    {
        // Se já contamos este cliente, saímos. (CORREÇÃO para o BUG de 2 jogadores)
        if (_activeClientCount.ContainsKey(clientId))
        {
            Debug.LogWarning($"Client {clientId} já foi contado. Ignorando contagem duplicada.");
            return;
        }

        // 1. Obter o PlayerType (deve ter sido armazenado no ApprovalCheck)
        if (_clientPlayerTypes.TryGetValue(clientId, out PlayerType playerType))
        {
            // 2. Incrementar a contagem correta (NetworkVariable)
            if (playerType == PlayerType.Runner)
            {
                totalPlayers.Value++;
            }
            else // Catcher
            {
                totalGuards.Value++;
            }

            // 3. Adicionar ao rastreamento de clientes ativos/contados
            _activeClientCount.Add(clientId, playerType);

            // 4. O UpdatePlayerCounter é chamado no listener, mas chamamos diretamente para garantir a atualização
            UpdatePlayerCounter();

            //Debug.Log($"Client {clientId} ({playerType}) adicionado. Total Runners: {totalPlayers.Value}, Total Catchers: {totalGuards.Value}");
        }
        else
        {
            // Fallback: Contagem padrão
            Debug.LogError($"Client {clientId} conectado, mas PlayerType não encontrado. Contando como Runner por padrão.");
            totalPlayers.Value++;
            _activeClientCount.Add(clientId, PlayerType.Runner);
            UpdatePlayerCounter();
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // 1. Verificar se o cliente estava na lista de contados
        if (_activeClientCount.TryGetValue(clientId, out PlayerType playerType))
        {
            // 2. Decrementar a contagem correta
            if (playerType == PlayerType.Runner)
            {
                totalPlayers.Value--;
            }
            else // Catcher
            {
                totalGuards.Value--;
            }

            // 3. Remover do rastreamento de clientes ativos/contados
            _activeClientCount.Remove(clientId);

            // 4. Atualizar a UI em todos os clientes
            UpdatePlayerCounter();

            //Debug.Log($"Client {clientId} ({playerType}) removido. Total Runners: {totalPlayers.Value}, Total Catchers: {totalGuards.Value}");
        }

        // 5. Limpar o cache do tipo de jogador (armazenado no ApprovalCheck)
        if (_clientPlayerTypes.ContainsKey(clientId))
        {
            _clientPlayerTypes.Remove(clientId);
        }
    }

    // ====================================================================
    // Lógica do Jogo
    // ====================================================================

    // MUDANÇA: Removemos o ServerRpc e usamos um método direto no servidor
    public void StartGame()
    {
        if (!IsServer)
        {
            Debug.LogError("StartGame called but we are not server!");
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogError("NetworkConnectionManager not spawned on network!");
            return;
        }

        //Debug.Log("Starting game directly on server");

        if (_gameManager != null)
        {
            _gameManager.StartGame();
        }
        else
        {
            Debug.LogError("GameManager not found!");
        }
    }

    // MANTEMOS O ServerRpc mas com verificação de spawn
    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc()
    {
        if (!IsServer)
        {
            Debug.LogWarning("StartGameServerRpc called but we are not server!");
            return;
        }

        if (!IsSpawned)
        {
            Debug.LogError("NetworkConnectionManager not spawned on network in ServerRpc!");
            return;
        }

        //Debug.Log("Starting game via ServerRpc");

        if (_gameManager != null)
        {
            _gameManager.StartGame();
        }
        else
        {
            Debug.LogError("GameManager not found in ServerRpc!");
        }
    }

    public async void Disconnect(bool goToMainMenu = true)
    {
        if (_currentLobby != null)
        {
            if (IsHost || IsServer)
            {
                await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
            }
            else
            {
                await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _playerLobbyId);
            }
            _currentLobby = null;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            LeaveLobby(); // Garante que o lobby é limpo no Unity Services
            NetworkManager.Singleton.Shutdown();
        }

        if (goToMainMenu)
        {
            SceneManager.LoadScene("MenuScene");
        }
    }

    // ====================================================================
    // Lógica da UI (Auxiliar)
    // ====================================================================

    private void UpdatePlayerCounter()
    {
        int currentTotal = totalPlayers.Value + totalGuards.Value;
        int maxTotal = MAX_PLAYERS + MAX_GUARDS;

        if (_uiManager == null)
            _uiManager = FindFirstObjectByType<UIManager>();

        if (_uiManager != null)
        {
            _uiManager.UpdatePlayerCounter(currentTotal, maxTotal);
            //Debug.Log($"UI Updated: {currentTotal}/{maxTotal} players (Players: {totalPlayers.Value}, Guards: {totalGuards.Value})");
        }
    }

    [ClientRpc]
    private void UpdateLobbyTextsClientRpc(string lobbyCode)
    {
        //Debug.Log($"UpdateLobbyTextsClientRpc called with code: {lobbyCode}");

        if (UIManager.Instance != null)
        {
            int currentTotal = totalPlayers.Value + totalGuards.Value;
            int maxTotal = MAX_PLAYERS + MAX_GUARDS;

            UIManager.Instance.UpdatePlayerCounter(currentTotal, maxTotal);

            var gameManager = FindFirstObjectByType<GameManager>();
            bool gameIsRunning = gameManager != null && gameManager.gameStarted.Value;

            if (!gameIsRunning)
            {
                bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
                // Chama o método atualizado no UIManager
                UIManager.Instance.ShowLobbyUI(isHost, lobbyCode);
            }
        }
    }
}