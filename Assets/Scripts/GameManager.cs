using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ====================================================================
    // Network Variables (Sincronized)
    // ====================================================================
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    public NetworkVariable<int> playerLives = new NetworkVariable<int>(initialLifes);
    public NetworkVariable<float> gameTimer = new NetworkVariable<float>(gameDuration);
    public NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);


    // ====================================================================
    // Game Variables
    // ====================================================================
    private const float gameDuration = 180.0f;
    private const int initialLifes = 3;

    // ====================================================================
    // Variáveis do Inspector
    // ====================================================================
    public int scoreToWin = 20;
    public GameObject endGamePanel;
    public GameObject guardPrefab;
    public GameObject playerPrefab;
    public Transform[] playerSpawnPoints;
    public Transform[] guardSpawnPoints;

    [Header("Spawners")]
    public LocalSpawner localSpawner;
    public CoinSpawner coinSpawner;

    // ====================================================================
    // Variáveis Privadas
    // ====================================================================
    private bool gameEnded = false;
    private UIManager _uiManager;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        score.OnValueChanged += OnScoreChanged;
        playerLives.OnValueChanged += OnLivesChanged;
        gameTimer.OnValueChanged += OnTimerChanged;
        gameStarted.OnValueChanged += OnGameStartedChanged;

        _uiManager = FindFirstObjectByType<UIManager>();

        if (IsServer)
        {
            LocalSpawner spawner = FindFirstObjectByType<LocalSpawner>();
            if (spawner != null)
            {
                spawner.SpawnLocalPlayers();
            }
            playerLives.Value = 3;
            gameTimer.Value = 180.0f; // Tempo inicial
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        score.OnValueChanged -= OnScoreChanged;
        playerLives.OnValueChanged -= OnLivesChanged;
        gameTimer.OnValueChanged -= OnTimerChanged;
        gameStarted.OnValueChanged -= OnGameStartedChanged;
    }

    // Classe auxiliar para armazenar o resultado
    public struct EndGameResult
    {
        public bool didLocalPlayerWin;
        public string winningTeam;
        public string losingTeam;
        public string message;
    }

    void Update()
    {
        if (!IsServer || !gameStarted.Value || gameEnded) return;

        gameTimer.Value -= Time.deltaTime;

        if (gameTimer.Value <= 0)
        {
            gameTimer.Value = 0;
            EndGame(false); // Passar false indica que runners não venceram (Catchers venceram)
        }
    }

    [ServerRpc]
    public void EndGameServerRpc(bool runnersWon)
    {
        if (gameEnded) return;
        gameEnded = true;

        // Notifica os clientes passando o resultado
        NotifyEndGameClientRpc(runnersWon);
    }

    [ClientRpc]
    private void NotifyEndGameClientRpc(bool runnersWon)
    {
        string message = "";

        // 1. Verificar se o jogador local é Runner ou Guard (Catcher)
        bool isRunner = false;
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;

        if (localPlayer != null)
        {
            // Verifica se o objeto local tem o componente PlayerMovement (Runner)
            isRunner = localPlayer.GetComponent<PlayerMovement>() != null;
        }

        // 2. Definir a mensagem baseada no time e no resultado
        if (runnersWon)
        {
            message = isRunner ? "Runners Victory!" : "Catchers Lose!";
        }
        else
        {
            message = isRunner ? "Runners Lose!" : "Catchers Victory!";
        }

        Debug.Log($"Game Over: {message}");

        // 3. Enviar para o UI
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowSimpleEndGame(message);
        }
    }

    // ====================================================================
    // Lógica do Jogo (Apenas Servidor)
    // ====================================================================

    // SUBSTITUIR o método StartGame existente por este:
    public void StartGame()
    {
        if (!IsServer) return;

        if (gameStarted.Value)
        {
            Debug.LogWarning("Jogo já iniciado, ignorando...");
            return;
        }

        gameStarted.Value = true;
        //Debug.Log("GameManager: Iniciando jogo!");

        // Spawn inicial de moedas
        if (coinSpawner != null)
        {
            coinSpawner.SpawnCoins();
        }
        else
        {
            Debug.LogError("CoinSpawner não encontrado!");
        }

        //Debug.Log("Jogo iniciado - todos os sistemas ativos");
    }

    private void SpawnPlayerForClient(ulong clientId, PlayerType type)
    {
        if (!IsServer) return;

        GameObject prefab = (type == PlayerType.Runner) ? playerPrefab : guardPrefab;
        Transform[] spawnPoints = (type == PlayerType.Runner) ? playerSpawnPoints : guardSpawnPoints;

        // Escolhe um ponto de spawn baseado no ID do cliente para evitar sobreposição
        int spawnIndex = (int)clientId % spawnPoints.Length;
        Vector3 spawnPos = spawnPoints[spawnIndex].position;

        GameObject playerObj = Instantiate(prefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();

        // O ID visual será o ID da rede + 1 (ex: Client 0 = P1, Client 1 = P2)
        int displayId = (int)clientId + 1;

        netObj.SpawnAsPlayerObject(clientId);

        // Atribui o identificador antes de spawnar na rede
        if (type == PlayerType.Runner)
        {
            if (playerObj.TryGetComponent<PlayerMovement>(out var pm))
            {
                pm.playerNumber.Value = displayId;
                playerObj.name = "Runner_P" + displayId;
            }
        }
        else
        {
            if (playerObj.TryGetComponent<Guard>(out var g))
            {
                g.playerNumber.Value = displayId;
                playerObj.name = "Catcher_P" + displayId;
            }
        }

        Debug.Log($"Spawnado {type} para cliente {clientId} como P{displayId}");
    }

    public void SpawnLocalPlayers()
    {
        if (!IsServer) return;

        Debug.Log($"GameManager: Spawning {GameSettings.LocalPlayerCount} local players");

        // Primeiro spawna o jogador principal (host)
        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
        if (connectionManager != null)
        {
            PlayerType hostType = connectionManager.GetPlayerType(NetworkManager.ServerClientId);
            connectionManager.UpdatePlayerCountServerRpc();
            SpawnPlayerForClient(NetworkManager.ServerClientId, hostType);
        }

        // Depois spawna jogadores locais adicionais (se houver)
        LocalSpawner spawner = FindFirstObjectByType<LocalSpawner>();
        if (spawner != null && GameSettings.LocalPlayerCount >= 0)
        {
            spawner.SpawnLocalPlayers();
        }
    }

    public void SpawnAllPlayersAndStartGame()
    {
        if (!IsServer) return;

        Debug.Log("=== SPAWNING ALL PLAYERS ===");

        // 1. Contar jogadores locais já spawnados
        int localPlayersSpawned = 0;
        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();

        if (connectionManager != null)
        {
            // 2. Spawna jogadores locais primeiro
            // Verifica dispositivos disponíveis
            Debug.Log($"Dispositivos disponíveis: {Gamepad.all.Count} gamepads, Teclado: {(Keyboard.current != null ? "Sim" : "Não")}");

            // Aviso se não há dispositivos suficientes
            if (GameSettings.LocalPlayerCount > 1 && Gamepad.all.Count == 0)
            {
                Debug.LogWarning("⚠ AVISO: Multiplayer local ativado mas nenhum gamepad encontrado!");
                Debug.LogWarning("O jogo continuará, mas alguns jogadores podem não ter controle.");
            }

            // Spawn normal continua mesmo sem controles suficientes
            SpawnLocalPlayers();

            // 3. Contar jogadores locais já existentes
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == NetworkManager.ServerClientId)
                {
                    localPlayersSpawned += GameSettings.LocalPlayerCount;
                    break;
                }
            }

            connectionManager.UpdatePlayerCountServerRpc();

            // 4. Spawna todos os jogadores remotos conectados
            // Itera sobre todos os clientes conectados e spawna o objeto de jogador para cada um.
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                // Pula o host se já foi spawnado acima
                if (client.ClientId == NetworkManager.ServerClientId) continue;

                // Verifica se o objeto de jogador já existe (previne duplicação)
                if (client.PlayerObject == null)
                {
                    PlayerType playerType = connectionManager.GetPlayerType(client.ClientId);
                    SpawnPlayerForClient(client.ClientId, playerType);
                }
            }
        }
        else
        {
            Debug.LogError("NetworkConnectionManager não encontrado. Não foi possível spawnar jogadores.");
        }

        // 5. Inicia o jogo (ativa timer, moedas, etc.)
        StartGame();

        int totalSpawned = NetworkManager.Singleton.ConnectedClientsList.Count;
        Debug.Log($"Total de jogadores conectados: {totalSpawned}");
        Debug.Log($"Jogadores locais: {GameSettings.LocalPlayerCount}");
        Debug.Log($"Jogadores remotos: {totalSpawned - 1}"); // -1 para o host
    }

    public void EndGame(bool runnersWon)
    {
        if (!IsServer || gameEnded) return;
        gameEnded = true;
        gameStarted.Value = false; // Para parar o timer

        // Determina quem venceu baseado no resultado
        // runnersWon = true significa que os Runners venceram
        // runnersWon = false significa que os Catchers venceram
        NotifyEndGameClientRpc(runnersWon);
    }

    // Método chamado pelo UIManager (RestartGame)
    [ServerRpc(RequireOwnership = false)]
    public void ResetGameServerRpc()
    {
        if (!IsServer) return;
        ResetGame();
    }

    private void ResetGame()
    {
        if (!IsServer) return;

        // 1. Limpa jogadores locais extras via LocalSpawner
        if (localSpawner != null)
        {
            localSpawner.DespawnLocalPlayers();
        }

        if (coinSpawner != null)
        {
            coinSpawner.ResetSpawner();
        }

        // 2. Limpa todos os jogadores da rede (Runners e Guards)
        PlayerMovement[] clients = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (var p in clients) p.GetComponent<NetworkObject>().Despawn();

        Guard[] guards = FindObjectsByType<Guard>(FindObjectsSortMode.None);
        foreach (var g in guards) g.GetComponent<NetworkObject>().Despawn();

        // 3. Reseta variáveis de rede
        score.Value = 0;
        playerLives.Value = initialLifes;
        gameTimer.Value = gameDuration;
        gameEnded = false;

        // 4. Em vez de recarregar a cena (que causa o spawn duplo), 
        DespawnAllCoins();
        coinSpawner.SpawnCoins();

        // O NetworkManager vai re-instanciar o player principal automaticamente se configurado,
        // mas se você usa LocalSpawner, chame-o aqui:
        // localSpawner.SpawnLocalPlayers();

        gameStarted.Value = false;

        // Oculta o painel de fim de jogo em todos os clientes
        HideEndGamePanelClientRpc();

        // 5. Reatribui as câmeras para todos os clients
        ReassignCamerasClientRpc();
    }

    [ClientRpc]
    private void ReassignCamerasClientRpc()
    {
        SplitScreenManager splitManager = FindFirstObjectByType<SplitScreenManager>();
        if (splitManager != null)
        {
            splitManager.ReassignCamerasAfterReset();
        }
    }

    // ====================================================================
    // NOVOS MÉTODOS PARA CONTROLE DE CENA
    // ====================================================================

    private void OnGameStartedChanged(bool previous, bool current)
    {
        if (_uiManager != null)
        {
            // O UIManager usará este valor para alternar entre LobbyUI e GameUI
            _uiManager.HandleGameStart(current);
        }
    }

    // ====================================================================
    // RPCs Recebidos (Corrigidos)
    // ====================================================================

    [ServerRpc(RequireOwnership = false)]
    public void AddScoreServerRpc(int value)
    {
        if (gameEnded) return;

        score.Value += value;

        if (score.Value >= scoreToWin)
        {
            score.Value = scoreToWin;
            EndGame(true); // Runners venceram (atingiram a pontuação)
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void ProcessPlayerHitWithReferenceServerRpc(NetworkObjectReference playerRef)
    {
        if (!IsServer) return;

        if (!playerRef.TryGet(out NetworkObject playerNetworkObject))
        {
            Debug.LogWarning("Player NetworkObject not found from reference");
            return;
        }

        PlayerMovement playerMovement = playerNetworkObject.GetComponent<PlayerMovement>();
        if (playerMovement == null)
        {
            Debug.LogWarning($"PlayerMovement component not found on player object");
            return;
        }

        // 1. Checa a Invulnerabilidade
        if (playerMovement.IsInvulnerable.Value)
        {
            return;
        }

        // 2. Perde uma vida
        if (playerLives.Value > 0)
        {
            playerLives.Value--;
        }

        // 3. Solta moedas e Inicia Invulnerabilidade
        int coinsLost = 0;

        if (coinSpawner != null)
        {
            // Usar a posição atual do jogador atingido
            coinsLost = playerMovement.ReceiveHitAndDropCoins_Server(coinSpawner);
        }

        // 4. Dedução da pontuação global
        if (coinsLost > 0)
        {
            score.Value = Mathf.Max(0, score.Value - coinsLost);
        }

        // 5. Checa Fim de Jogo
        if (playerLives.Value <= 0)
        {
            EndGame(false); // Catchers venceram (runners perderam todas as vidas)
        }
    }

    // Implementação de SpawnCoinsFromButtonServerRpc
    [ServerRpc(RequireOwnership = false)]
    public void SpawnCoinsFromButtonServerRpc(NetworkObjectReference buttonNetObjectRef, Vector3 position, int count, float radius)
    {
        if (!IsServer) return;

        if (coinSpawner != null)
        {
            // Chama o novo método em CoinSpawner
            coinSpawner.SpawnCoinsAtPosition(position, count, radius);

            // Reativa o botão (exemplo: após um cooldown ou se o botão desativou a si mesmo)
            if (buttonNetObjectRef.TryGet(out NetworkObject buttonNetObj))
            {
                ButtonSpawner button = buttonNetObj.GetComponent<ButtonSpawner>();
                if (button != null)
                {
                    // O ButtonSpawner deve lidar com o rearmamento após um cooldown, mas
                    // para o escopo, ele pode ser reativado aqui ou no próprio ButtonSpawner.
                    // button.SetButtonActiveClientRpc(true); 
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubtractScoreServerRpc(int value)
    {
        if (gameEnded) return;

        score.Value = Mathf.Max(0, score.Value - value);
    }

    // ====================================================================
    // Auxiliar e RPCs de Cliente
    // ====================================================================

    private void ResetAllPlayersPosition()
    {
        // Apenas o servidor executa a lógica de distribuição de posições
        if (!IsServer) return;

        Debug.Log("Resetando posições de todos os jogadores...");

        // 1. Resetar Runners (PlayerMovement)
        PlayerMovement[] allRunners = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        for (int i = 0; i < allRunners.Length; i++)
        {
            if (playerSpawnPoints.Length > 0)
            {
                // Usa o index para distribuir nos pontos de spawn
                Vector3 spawnPos = playerSpawnPoints[i % playerSpawnPoints.Length].position;

                // IMPORTANTE: Chama o ClientRpc para cada jogador
                allRunners[i].TeleportPlayerClientRpc(spawnPos);

                // Para garantir que a câmera seja resetada imediatamente
                ResetCameraForPlayerClientRpc(allRunners[i].NetworkObjectId, spawnPos);
            }
        }

        // 2. Resetar Guards (Guard)
        Guard[] allGuards = FindObjectsByType<Guard>(FindObjectsSortMode.None);
        for (int i = 0; i < allGuards.Length; i++)
        {
            if (guardSpawnPoints.Length > 0)
            {
                Vector3 spawnPos = guardSpawnPoints[i % guardSpawnPoints.Length].position;
                allGuards[i].TeleportPlayerClientRpc(spawnPos);

                // Para garantir que a câmera seja resetada imediatamente
                ResetCameraForPlayerClientRpc(allGuards[i].NetworkObjectId, spawnPos);
            }
        }

        // Chama o reset geral de câmeras
        ResetAllCamerasClientRpc();
    }

    [ClientRpc]
    private void ResetCameraForPlayerClientRpc(ulong playerNetworkId, Vector3 newPosition)
    {
        // Encontra o objeto pelo NetworkId
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerNetworkId, out NetworkObject netObj))
        {
            // Verifica se é o jogador local
            if (netObj.IsOwner)
            {
                // Força a câmera a seguir o jogador
                CameraFollow cameraFollow = FindFirstObjectByType<CameraFollow>();
                if (cameraFollow != null && cameraFollow.Target == netObj.transform)
                {
                    cameraFollow.ForcePosition();
                }
            }
        }
    }

    [ClientRpc]
    private void ResetAllCamerasClientRpc()
    {
        SplitScreenManager splitManager = FindFirstObjectByType<SplitScreenManager>();
        if (splitManager != null)
        {
            splitManager.ResetAllCameras();
        }
    }

    [ClientRpc]
    private void ResetAllPlayersPositionClientRpc()
    {
        ResetAllPlayersPosition();
    }

    private void DespawnAllCoins()
    {
        if (!IsServer) return;

        Coin[] allCoins = FindObjectsByType<Coin>(FindObjectsSortMode.None);
        foreach (Coin coin in allCoins)
        {
            if (coin != null)
            {
                NetworkObject netObj = coin.GetComponent<NetworkObject>();
                // Verifique se o objeto ainda está ativo na rede antes de desativar
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn();
                }
            }
        }
    }

    [ClientRpc]
    public void HideEndGamePanelClientRpc()
    {
        if (_uiManager != null)
        {
            _uiManager.HideEndGamePanel();
        }
    }

    // ====================================================================
    // Listeners de NetworkVariable
    // ====================================================================

    private void OnScoreChanged(int previous, int current)
    {
        if (_uiManager != null)
        {
            _uiManager.UpdateScoreText(current);
        }
    }

    private void OnLivesChanged(int previous, int current)
    {
        if (_uiManager != null)
        {
            _uiManager.UpdateLivesUI(current);
        }
    }

    private void OnTimerChanged(float previous, float current)
    {
        if (_uiManager != null)
        {
            _uiManager.UpdateTimerText(current);
        }
    }
}