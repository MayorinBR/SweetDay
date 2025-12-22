using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ====================================================================
    // Network Variables (Sincronized)
    // ====================================================================
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    public NetworkVariable<int> playerLives = new NetworkVariable<int>(3);
    public NetworkVariable<float> gameTimer = new NetworkVariable<float>(120.0f);
    public NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);

    // ...

    // ====================================================================
    // Variáveis do Inspector
    // ====================================================================
    public int scoreToWin = 10;
    public GameObject endGamePanel;
    public CoinSpawner coinSpawner;
    public GameObject guardPrefab;
    public GameObject playerPrefab;
    public Transform[] playerSpawnPoints;
    public Transform[] guardSpawnPoints;

    // ====================================================================
    // Variáveis Privadas
    // ====================================================================
    private bool gameEnded = false;
    private UIManager _uiManager;
    private NetworkConnectionManager _connectionManager;

    // Variáveis para controlar os índices de spawn usados
    private int _currentPlayerSpawnIndex = 0;
    private int _currentGuardSpawnIndex = 0;

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

        // REGISTRAR EVENTO DE CARREGAMENTO DE CENA
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnSceneEvent;
        }
        /*
        if (IsServer)
        {
            // SPAWN AUTOMÁTICO QUANDO NA CENA DE JOGO
            if (IsGameScene())
            {
                UIManager.Instance.StartCoroutine(SpawnPlayersAfterSceneLoad());
            }
        }
        */
        if (IsServer)
        {
            // O servidor zera o timer e as vidas
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

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnSceneEvent;
        }
    }

    void Update()
    {
        if (!IsServer) return;

        if (gameStarted.Value && !gameEnded)
        {
            gameTimer.Value -= Time.deltaTime;

            if (gameTimer.Value <= 0)
            {
                gameTimer.Value = 0;
                EndGame(false); // Tempo esgotado, jogadores perdem
            }
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

        // Ativar botões
        ButtonSpawner[] buttons = FindObjectsByType<ButtonSpawner>(FindObjectsSortMode.None);
        foreach (var button in buttons)
        {
            button.SetButtonActiveClientRpc(true);
        }

        //Debug.Log("Jogo iniciado - todos os sistemas ativos");
    }

    public void SpawnPlayerForClient(ulong clientId, PlayerType playerType)
    {
        if (!IsServer) return;

        // DEBUG LOG EXPANDIDO
        Debug.Log($"=== SPAWNING: Client {clientId} como {playerType} ===");
        Debug.Log($"=== PlayerType recebido: {playerType} ===");

        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
        if (connectionManager != null)
        {
            PlayerType storedType = connectionManager.GetPlayerType(clientId);
            Debug.Log($"=== Tipo armazenado no NetworkConnectionManager: {storedType} ===");
        }

        if (NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
        {
            var existingPlayer = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            if (existingPlayer != null)
            {
                //Debug.Log($"Client {clientId} já tem um objeto de jogador, ignorando spawn duplicado");
                return;
            }
        }

        GameObject playerPrefabToSpawn = (playerType == PlayerType.Catcher) ? guardPrefab : playerPrefab;
        Transform[] spawnPoints = (playerType == PlayerType.Catcher) ? guardSpawnPoints : playerSpawnPoints;

        int spawnIndex = 0;
        if (playerType == PlayerType.Runner)
        {
            spawnIndex = _currentPlayerSpawnIndex % playerSpawnPoints.Length;
            _currentPlayerSpawnIndex++;
        }
        else
        {
            spawnIndex = _currentGuardSpawnIndex % guardSpawnPoints.Length;
            _currentGuardSpawnIndex++;
        }

        Transform spawnPoint = spawnPoints[spawnIndex];

        GameObject playerObject = Instantiate(playerPrefabToSpawn, spawnPoint.position, spawnPoint.rotation);
        NetworkObject netObject = playerObject.GetComponent<NetworkObject>();

        if (netObject != null)
        {
            netObject.SpawnAsPlayerObject(clientId);

            //Debug.Log($"Spawned {playerType} for client {clientId} at {spawnPoint.position}");
        }
    }

    public void SpawnAllPlayersAndStartGame()
    {
        if (!IsServer) return;

        // 1. Spawna todos os jogadores conectados.
        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
        if (connectionManager != null)
        {
            // Itera sobre todos os clientes conectados e spawna o objeto de jogador para cada um.
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                // Verifica se o objeto de jogador já existe (previne duplicação)
                if (client.PlayerObject == null)
                {
                    // Usa o método existente no Connection Manager e GameManager
                    PlayerType playerType = connectionManager.GetPlayerType(client.ClientId);
                    SpawnPlayerForClient(client.ClientId, playerType);
                }
            }
        }
        else
        {
            Debug.LogError("NetworkConnectionManager não encontrado. Não foi possível spawnar jogadores.");
        }

        // 2. Inicia o jogo (ativa timer, moedas, etc.)
        StartGame();
    }

    public void EndGame(bool won)
    {
        if (!IsServer || gameEnded) return;
        gameEnded = true;
        gameStarted.Value = false; // Para parar o timer

        ShowEndGamePanelClientRpc(won);

        // Despawn de moedas
        DespawnAllCoins();
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

        gameEnded = false;

        // Reset Network Variables
        score.Value = 0;
        playerLives.Value = 3;
        gameTimer.Value = 180.0f;
        gameStarted.Value = false;

        // Reset do coin spawner
        if (coinSpawner != null)
        {
            coinSpawner.ResetSpawner();
        }

        // Reset posição dos jogadores
        ResetAllPlayersPositionClientRpc();

        // Reset dos botões de spawn
        ButtonSpawner[] buttons = FindObjectsByType<ButtonSpawner>(FindObjectsSortMode.None);
        foreach (var button in buttons)
        {
            button.SetButtonActiveClientRpc(false);
        }

        HideEndGamePanelClientRpc();
    }

    // ====================================================================
    // NOVOS MÉTODOS PARA CONTROLE DE CENA
    // ====================================================================

    // ADICIONAR método auxiliar para detectar cena de jogo
    private bool IsGameScene()
    {
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return currentScene != "MenuScene" && currentScene != "MainMenu";
    }

    // ADICIONAR corrotina para spawn após carregamento
    private IEnumerator SpawnPlayersAfterSceneLoad()
    {
        //Debug.Log("Aguardando cena carregar completamente...");
        yield return new WaitForSeconds(1.5f);

        //Debug.Log("Fazendo spawn dos jogadores...");

        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject == null)
            {
                PlayerType playerType = NetworkConnectionManager.Instance.GetPlayerType(client.Key);
                SpawnPlayerForClient(client.Key, playerType);
                //Debug.Log($"Spawnado {playerType} para cliente {client.Key}");
            }
        }

        // Iniciar jogo automaticamente após spawn
        //StartGame();
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        if (sceneEvent.SceneEventType == SceneEventType.LoadComplete && IsServer)
        {
            // Debug.Log($"Cena {sceneEvent.SceneName} carregada - preparando spawn...");

            if (IsGameScene())
            {
                // REMOVA ou COMENTE ESTA LINHA:
                // StartCoroutine(SpawnPlayersAfterSceneLoad()); 
            }
        }
    }

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
            EndGame(true); // Player/Thief Won
        }
    }
    /*
    [ServerRpc(RequireOwnership = false)]
    public void LoseLifeServerRpc()
    {
        if (gameEnded) return;

        playerLives.Value--;

        if (playerLives.Value <= 0)
        {
            EndGame(false); // Player/Thief Lost
        }
        else
        {
            // Teleporta todos os jogadores (ladrões) de volta aos pontos de spawn
            ResetAllPlayersPositionClientRpc();
        }
    }
    */

    [ServerRpc(RequireOwnership = false)]
    public void ProcessPlayerHitServerRpc(ulong playerId)
    {
        if (!IsServer) return;

        // Tenta encontrar o objeto do jogador na rede
        NetworkObject playerNetworkObject = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(playerId);
        if (playerNetworkObject == null)
        {
            Debug.LogWarning($"Player NetworkObject not found for ID: {playerId}");
            return;
        }

        PlayerMovement playerMovement = playerNetworkObject.GetComponent<PlayerMovement>();
        if (playerMovement == null)
        {
            Debug.LogWarning($"PlayerMovement component not found on player object ID: {playerId}");
            return;
        }

        // 1. Checa a Invulnerabilidade
        if (playerMovement.IsInvulnerable.Value)
        {
            Debug.Log($"Player {playerId} hit but is invulnerable.");
            return;
        }

        // 2. Perde uma vida
        if (playerLives.Value > 0)
        {
            playerLives.Value--;
            Debug.Log($"Player {playerId} lost a life. Remaining lives: {playerLives.Value}");
        }

        // 3. Solta moedas e Inicia Invulnerabilidade
        int coinsLost = 0;

        if (coinSpawner != null)
        {
            // Chama o método no PlayerMovement (que roda no servidor) e CAPTURA o valor das moedas perdidas.
            coinsLost = playerMovement.ReceiveHitAndDropCoins_Server(coinSpawner);
        }
        else
        {
            Debug.LogError("CoinSpawner reference is missing in GameManager! Coins cannot be dropped.");
        }

        // =======================================================
        // DEDUÇÃO DA PONTUAÇÃO GLOBAL (GameManager.score)
        // =======================================================
        if (coinsLost > 0)
        {
            // Reduz a pontuação global (depositada) pelo valor das moedas perdidas
            score.Value = Mathf.Max(0, score.Value - coinsLost);
            Debug.Log($"Score global penalizado em {coinsLost} pontos (moedas perdidas). Novo score: {score.Value}");
        }

        // 4. Checa Fim de Jogo (Opcional, adicione sua lógica de morte)
        if (playerLives.Value <= 0)
        {
            EndGame(false); // Chame sua função de fim de jogo
            Debug.Log("Game Over!");
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
        // Encontra todos os objetos de jogador (Player) no servidor
        GameObject[] playerObjects = GameObject.FindGameObjectsWithTag("Player");

        // Os guardas também precisam ser teleportados se for o caso
        GameObject[] guardObjects = GameObject.FindGameObjectsWithTag("Guard");

        List<GameObject> allPlayers = new List<GameObject>();
        allPlayers.AddRange(playerObjects);
        allPlayers.AddRange(guardObjects);

        foreach (GameObject playerObject in allPlayers)
        {
            NetworkObject netObject = playerObject.GetComponent<NetworkObject>();
            if (netObject == null) continue;

            Transform spawnPoint = null;
            PlayerType playerType = NetworkConnectionManager.Instance.GetPlayerType(netObject.OwnerClientId);

            if (playerType == PlayerType.Runner)
            {
                // Usa o ponto de spawn 0 para resetar
                if (playerSpawnPoints.Length > 0)
                    spawnPoint = playerSpawnPoints[0];
            }
            else // Guard
            {
                // Usa o ponto de spawn 0 para resetar
                if (guardSpawnPoints.Length > 0)
                    spawnPoint = guardSpawnPoints[0];
            }

            if (spawnPoint != null)
            {
                // Usar Teleport para evitar problemas de interpolação
                playerObject.transform.position = spawnPoint.position;
                playerObject.transform.rotation = spawnPoint.rotation;

                // Se for um jogador, resetar as moedas carregadas
                PlayerMovement playerMovement = playerObject.GetComponent<PlayerMovement>();
                if (playerMovement != null)
                {
                    playerMovement.coinsCarried.Value = 0;
                }
            }
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
            if (coin.IsSpawned)
            {
                coin.GetComponent<NetworkObject>().Despawn();
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

    [ClientRpc]
    public void ShowEndGamePanelClientRpc(bool won)
    {
        if (_uiManager != null)
        {
            _uiManager.ShowEndGamePanel(won);
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