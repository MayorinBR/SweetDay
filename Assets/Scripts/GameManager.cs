using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central authority for game state, scoring, player spawning, and lifecycle management.
/// Must be a NetworkBehaviour because it owns networked variables and server-side logic.
/// </summary>
public class GameManager : NetworkBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="GameManager"/>.</summary>
    public static GameManager Instance { get; private set; }

    // ====================================================================
    // Constants
    // ====================================================================

    /// <summary>Total game duration in seconds.</summary>
    private const float GameDuration = 180f;

    /// <summary>Number of lives players start with.</summary>
    private const int InitialLives = 3;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Shared score for the runners team, replicated to all clients.</summary>
    public NetworkVariable<int> score = new NetworkVariable<int>(0);

    /// <summary>Remaining lives for the runners team, replicated to all clients.</summary>
    public NetworkVariable<int> playerLives = new NetworkVariable<int>(InitialLives);

    /// <summary>Remaining game time in seconds, decremented server-side each frame.</summary>
    public NetworkVariable<float> gameTimer = new NetworkVariable<float>(GameDuration);

    /// <summary>Whether the game is currently active (timer running, inputs accepted).</summary>
    public NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);

    // ====================================================================
    // Inspector Fields
    // ====================================================================

    /// <summary>Score the runners must reach to win.</summary>
    public int scoreToWin = 20;

    /// <summary>Panel shown when the game ends (set inactive during play).</summary>
    public GameObject endGamePanel;

    /// <summary>Prefab used to spawn Guard (catcher) players.</summary>
    public GameObject guardPrefab;

    /// <summary>Prefab used to spawn Runner players.</summary>
    public GameObject playerPrefab;

    /// <summary>World-space spawn points for runners, indexed by player slot.</summary>
    public Transform[] playerSpawnPoints;

    /// <summary>World-space spawn points for guards, indexed by player slot.</summary>
    public Transform[] guardSpawnPoints;

    [Header("Spawners")]
    /// <summary>Reference to the local (split-screen) player spawner.</summary>
    public LocalSpawner localSpawner;

    /// <summary>Reference to the coin spawner responsible for placing coins on the map.</summary>
    public CoinSpawner coinSpawner;

    // ====================================================================
    // Private State
    // ====================================================================

    private bool _gameEnded;
    private UIManager _uiManager;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        score.OnValueChanged += OnScoreChanged;
        playerLives.OnValueChanged += OnLivesChanged;
        gameTimer.OnValueChanged += OnTimerChanged;
        gameStarted.OnValueChanged += OnGameStartedChanged;

        _uiManager = FindAnyObjectByType<UIManager>();

        if (IsServer)
        {
            playerLives.Value = InitialLives;
            gameTimer.Value = GameDuration;

            // Spawn any extra local (split-screen) players immediately.
            LocalSpawner spawner = FindAnyObjectByType<LocalSpawner>();
            spawner?.SpawnLocalPlayers();
        }
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        score.OnValueChanged -= OnScoreChanged;
        playerLives.OnValueChanged -= OnLivesChanged;
        gameTimer.OnValueChanged -= OnTimerChanged;
        gameStarted.OnValueChanged -= OnGameStartedChanged;
    }

    private void Update()
    {
        if (!IsServer || !gameStarted.Value || _gameEnded) return;

        gameTimer.Value -= Time.deltaTime;

        if (gameTimer.Value <= 0f)
        {
            gameTimer.Value = 0f;
            EndGame(false); // Time expired -> catchers win.
        }
    }

    // ====================================================================
    // Public Game Flow (Server only)
    // ====================================================================

    /// <summary>
    /// Starts the match: sets the game-started flag and spawns the initial coin layout.
    /// Should only be called on the server/host.
    /// </summary>
    public void StartGame()
    {
        if (!IsServer) return;

        if (gameStarted.Value)
        {
            Debug.LogWarning("[GameManager] StartGame called but game is already running.");
            return;
        }

        gameStarted.Value = true;

        if (coinSpawner != null)
            coinSpawner.SpawnCoins();
        else
            Debug.LogError("[GameManager] CoinSpawner reference is missing!");
    }

    /// <summary>
    /// Spawns the host's own player object, then all remote clients' objects,
    /// then kicks off <see cref="StartGame"/>.  Server-only.
    /// </summary>
    public void SpawnAllPlayersAndStartGame()
    {
        if (!IsServer) return;

        Debug.Log("[GameManager] === SPAWNING ALL PLAYERS ===");

        var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
        if (connectionManager == null)
        {
            Debug.LogError("[GameManager] NetworkConnectionManager not found – cannot spawn players.");
            return;
        }

        // Warn if local multiplayer is configured but no gamepads are present.
        if (GameSettings.LocalPlayerCount > 1 && Gamepad.all.Count == 0)
            Debug.LogWarning("[GameManager] Local multiplayer active but no gamepads detected.");

        // 1. Spawn host player + any split-screen companions.
        SpawnLocalPlayers();

        // 2. Spawn all remote clients that are already connected.
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.ClientId == NetworkManager.ServerClientId) continue;

            if (client.PlayerObject == null)
            {
                PlayerType playerType = connectionManager.GetPlayerType(client.ClientId);
                SpawnPlayerForClient(client.ClientId, playerType);
            }
        }

        // 3. Update the lobby player-count display.
        //    Guard: only call ServerRpc when the NetworkBehaviour is already spawned.
        if (connectionManager.IsSpawned)
            connectionManager.UpdatePlayerCountServerRpc();
        else
            Debug.LogWarning("[GameManager] NetworkConnectionManager not yet spawned; skipping UpdatePlayerCountServerRpc.");

        // 4. Start the game.
        StartGame();

        Debug.Log($"[GameManager] Connected clients: {NetworkManager.Singleton.ConnectedClientsList.Count}, " +
                  $"Local: {GameSettings.LocalPlayerCount}");
    }

    /// <summary>
    /// Ends the game, stops the timer, and notifies all clients of the result.
    /// </summary>
    /// <param name="runnersWon"><c>true</c> if the runners reached the score target; <c>false</c> if catchers won.</param>
    public void EndGame(bool runnersWon)
    {
        if (!IsServer || _gameEnded) return;

        _gameEnded = true;
        gameStarted.Value = false;

        NotifyEndGameClientRpc(runnersWon);
    }

    // ====================================================================
    // Server RPCs
    // ====================================================================

    /// <summary>
    /// Adds <paramref name="value"/> to the global score.
    /// Ends the game if the target score is reached.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddScoreServerRpc(int value)
    {
        if (_gameEnded) return;

        score.Value += value;

        if (score.Value >= scoreToWin)
        {
            score.Value = scoreToWin;
            EndGame(true);
        }
    }

    /// <summary>Subtracts <paramref name="value"/> from the global score, clamped to zero.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubtractScoreServerRpc(int value)
    {
        if (_gameEnded) return;
        score.Value = Mathf.Max(0, score.Value - value);
    }

    /// <summary>
    /// Processes a hit on the player referenced by <paramref name="playerRef"/>:
    /// checks invulnerability, decrements lives, drops coins, and checks for game-over.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ProcessPlayerHitWithReferenceServerRpc(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out NetworkObject playerNetObj))
        {
            Debug.LogWarning("[GameManager] ProcessPlayerHit: NetworkObject not found.");
            return;
        }

        if (!playerNetObj.TryGetComponent<PlayerMovement>(out var pm))
        {
            Debug.LogWarning("[GameManager] ProcessPlayerHit: PlayerMovement not found.");
            return;
        }

        if (pm.IsInvulnerable.Value) return;

        if (playerLives.Value > 0) playerLives.Value--;

        int coinsLost = coinSpawner != null
            ? pm.ReceiveHitAndDropCoins_Server(coinSpawner)
            : 0;

        if (coinsLost > 0)
            score.Value = Mathf.Max(0, score.Value - coinsLost);

        if (playerLives.Value <= 0)
            EndGame(false);
    }

    /// <summary>
    /// Spawns coins near a completed button's position and notifies the ButtonSpawner.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SpawnCoinsFromButtonServerRpc(NetworkObjectReference buttonRef, Vector3 position, int count, float radius)
    {
        coinSpawner?.SpawnCoinsAtPosition(position, count, radius);
        // ButtonSpawner handles its own re-arming via ButtonManager.
    }

    /// <summary>Resets the game state and restarts without reloading the scene.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ResetGameServerRpc() => ResetGame();

    // ====================================================================
    // Client RPCs
    // ====================================================================

    /// <summary>Hides the end-game panel on all clients.</summary>
    [ClientRpc]
    public void HideEndGamePanelClientRpc()
    {
        _uiManager?.HideEndGamePanel();
    }

    // ====================================================================
    // Private Helpers
    // ====================================================================

    /// <summary>
    /// Spawns the host's player object plus any additional local (split-screen) players.
    /// </summary>
    private void SpawnLocalPlayers()
    {
        if (!IsServer) return;

        var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
        if (connectionManager == null) return;

        PlayerType hostType = connectionManager.GetPlayerType(NetworkManager.ServerClientId);
        SpawnPlayerForClient(NetworkManager.ServerClientId, hostType);

        if (GameSettings.LocalPlayerCount > 1)
        {
            LocalSpawner spawner = FindAnyObjectByType<LocalSpawner>();
            spawner?.SpawnLocalPlayers();
        }
    }

    /// <summary>
    /// Instantiates and network-spawns the appropriate prefab for a given client.
    /// </summary>
    /// <param name="clientId">Target client's network ID.</param>
    /// <param name="type">Whether this client is a Runner or Catcher.</param>
    private void SpawnPlayerForClient(ulong clientId, PlayerType type)
    {
        if (!IsServer) return;

        GameObject prefab = type == PlayerType.Runner ? playerPrefab : guardPrefab;
        Transform[] spawnPoints = type == PlayerType.Runner ? playerSpawnPoints : guardSpawnPoints;

        int spawnIndex = (int)(clientId % (ulong)spawnPoints.Length);
        Vector3 spawnPos = spawnPoints[spawnIndex].position;
        int displayId = (int)clientId + 1;

        GameObject playerObj = Instantiate(prefab, spawnPos, Quaternion.identity);
        NetworkObject netObj = playerObj.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId);

        if (type == PlayerType.Runner && playerObj.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayId;
            playerObj.name = "Runner_P" + displayId;
        }
        else if (type == PlayerType.Catcher && playerObj.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayId;
            playerObj.name = "Catcher_P" + displayId;
        }

        Debug.Log($"[GameManager] Spawned {type} for client {clientId} as P{displayId}.");
    }

    /// <summary>
    /// Fully resets the game: despawns players and coins, resets variables, respawns coins.
    /// </summary>
    private void ResetGame()
    {
        if (!IsServer) return;

        localSpawner?.DespawnLocalPlayers();
        coinSpawner?.ResetSpawner();

        // Despawn all networked player objects.
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude))
        {
            if (pm.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();
        }
        foreach (var g in FindObjectsByType<Guard>(FindObjectsInactive.Exclude))
        {
            if (g.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();
        }

        // Reset network state.
        score.Value = 0;
        playerLives.Value = InitialLives;
        gameTimer.Value = GameDuration;
        gameStarted.Value = false;
        _gameEnded = false;

        DespawnAllCoins();
        coinSpawner?.SpawnCoins();

        HideEndGamePanelClientRpc();
        ReassignCamerasClientRpc();
    }

    /// <summary>Despawns every active coin from the network.</summary>
    private void DespawnAllCoins()
    {
        if (!IsServer) return;

        foreach (var coin in FindObjectsByType<Coin>(FindObjectsInactive.Exclude))
        {
            if (coin.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned)
                n.Despawn();
        }
    }

    /// <summary>Teleports all runners and guards back to their spawn points.</summary>
    private void ResetAllPlayersPosition()
    {
        if (!IsServer) return;

        var allRunners = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allRunners.Length; i++)
        {
            if (playerSpawnPoints.Length == 0) break;
            Vector3 pos = playerSpawnPoints[i % playerSpawnPoints.Length].position;
            allRunners[i].TeleportPlayerClientRpc(pos);
            ResetCameraForPlayerClientRpc(allRunners[i].NetworkObjectId, pos);
        }

        var allGuards = FindObjectsByType<Guard>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allGuards.Length; i++)
        {
            if (guardSpawnPoints.Length == 0) break;
            Vector3 pos = guardSpawnPoints[i % guardSpawnPoints.Length].position;
            allGuards[i].TeleportPlayerClientRpc(pos);
            ResetCameraForPlayerClientRpc(allGuards[i].NetworkObjectId, pos);
        }

        ResetAllCamerasClientRpc();
    }

    // ====================================================================
    // Client RPC Helpers
    // ====================================================================

    [ClientRpc]
    private void NotifyEndGameClientRpc(bool runnersWon)
    {
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        bool isRunner = localPlayerObj != null && localPlayerObj.GetComponent<PlayerMovement>() != null;

        string message = runnersWon
            ? (isRunner ? "Runners Victory!" : "Catchers Lose!")
            : (isRunner ? "Runners Lose!" : "Catchers Victory!");

        Debug.Log($"[GameManager] Game Over: {message}");
        UIManager.Instance?.ShowSimpleEndGame(message);
    }

    [ClientRpc]
    private void ReassignCamerasClientRpc()
    {
        FindAnyObjectByType<SplitScreenManager>()?.ReassignCamerasAfterReset();
    }

    [ClientRpc]
    private void ResetCameraForPlayerClientRpc(ulong playerNetworkId, Vector3 newPosition)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(playerNetworkId, out NetworkObject netObj)) return;

        if (!netObj.IsOwner) return;

        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null && cam.Target == netObj.transform)
            cam.ForcePosition();
    }

    [ClientRpc]
    private void ResetAllCamerasClientRpc()
    {
        FindAnyObjectByType<SplitScreenManager>()?.ResetAllCameras();
    }

    // ====================================================================
    // NetworkVariable Callbacks
    // ====================================================================

    private void OnGameStartedChanged(bool previous, bool current)
        => _uiManager?.HandleGameStart(current);

    private void OnScoreChanged(int previous, int current)
        => _uiManager?.UpdateScoreText(current);

    private void OnLivesChanged(int previous, int current)
        => _uiManager?.UpdateLivesUI(current);

    private void OnTimerChanged(float previous, float current)
        => _uiManager?.UpdateTimerText(current);

    // ====================================================================
    // Nested Types
    // ====================================================================

    /// <summary>Carries the win/loss result passed to <see cref="UIManager"/>.</summary>
    public struct EndGameResult
    {
        /// <summary>Whether the local player's team won.</summary>
        public bool didLocalPlayerWin;
        /// <summary>Display name of the winning team.</summary>
        public string winningTeam;
        /// <summary>Display name of the losing team.</summary>
        public string losingTeam;
        /// <summary>Short result message shown in the end-game panel.</summary>
        public string message;
    }
}
