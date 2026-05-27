using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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

    /// <summary>When <c>true</c> the timer stops and player movement is blocked on all clients.</summary>
    public NetworkVariable<bool> isPaused = new NetworkVariable<bool>(false);

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
    private LobbyManager _lobbyManager;

    // Tracks how many players of each type have been spawned in the current session.
    // Reset at the start of SpawnAllPlayersAndStartGame and ResetGame.
    private int _runnerSpawnIndex = 0;
    private int _catcherSpawnIndex = 0;

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
        isPaused.OnValueChanged += OnIsPausedChanged;

        if (IsServer)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedDuringGame;

        _uiManager = FindAnyObjectByType<UIManager>();
        _lobbyManager = FindAnyObjectByType<LobbyManager>();

        if (IsServer)
        {
            playerLives.Value = InitialLives;
            gameTimer.Value = GameDuration;

            // Auto-start: when the game scene loads after the host clicked
            // "Start Match" in LobbySetupPanel, all players are already connected
            // and we can spawn + start immediately.
            // NetworkManager.IsListening is true only when a session already exists
            // (i.e. we came from the lobby, not from a fresh editor play).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                // Small delay to ensure all NetworkObjects in the scene have spawned.
                StartCoroutine(AutoStartAfterSceneLoad());
            }
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
        isPaused.OnValueChanged -= OnIsPausedChanged;

        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedDuringGame;
    }

    private void Update()
    {
        if (!IsServer || !gameStarted.Value || _gameEnded || isPaused.Value) return;

        gameTimer.Value -= Time.deltaTime;

        if (gameTimer.Value <= 0f)
        {
            gameTimer.Value = 0f;
            EndGame(false);
        }
    }

    // ====================================================================
    // Public Game Flow (Server only)
    // ====================================================================

    /// <summary>
    /// Waits two frames for all scene <see cref="NetworkObject"/>s to finish
    /// spawning, then calls <see cref="SpawnAllPlayersAndStartGame"/> automatically.
    ///
    /// Guards:
    /// <list type="bullet">
    ///   <item>Skips if the active scene is the Lobby or Menu scene (those don't have
    ///         spawn points).</item>
    ///   <item>Skips and logs an error if <see cref="playerSpawnPoints"/> or
    ///         <see cref="guardSpawnPoints"/> are not assigned in the Inspector.</item>
    /// </list>
    /// </summary>
    private System.Collections.IEnumerator AutoStartAfterSceneLoad()
    {
        // Wait two frames: one for scene objects to register, one for clients to sync.
        yield return null;
        yield return null;

        // Only run in the actual game scene, not in Lobby or Menu scenes.
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (activeScene == NetworkConnectionManager.LobbySceneName ||
            activeScene == NetworkConnectionManager.MenuSceneName)
            yield break;

        // ── Show game UI immediately ────────────────────────────────────
        // Must happen BEFORE any early-return guard so the lobby UI always
        // disappears when the game scene loads — even if spawn points are missing.
        LobbyManager.Instance?.ShowGameUI();
        UIManager.Instance?.ShowGameUI();
        ShowGameUIAllClientsClientRpc();   // Notify join clients too.

        // ── Validate only the spawn points we actually need ─────────────
        var assignments = GameSettings.SlotAssignments;

        bool needRunnerPoints = assignments.Count == 0  // fallback always needs both
            || assignments.Exists(a => a.SlotIndex < LobbyStateManager.RunnerSlotCount);

        bool needCatcherPoints = assignments.Count == 0
            || assignments.Exists(a => a.SlotIndex >= LobbyStateManager.RunnerSlotCount);

        if (needRunnerPoints && (playerSpawnPoints == null || playerSpawnPoints.Length == 0))
        {
            Debug.LogError("[GameManager] playerSpawnPoints is not assigned. " +
                           "Assign it in the Game Scene's GameManager Inspector.");
            yield break;
        }

        if (needCatcherPoints && (guardSpawnPoints == null || guardSpawnPoints.Length == 0))
        {
            Debug.LogError("[GameManager] guardSpawnPoints is not assigned. " +
                           "Assign it in the Game Scene's GameManager Inspector.");
            yield break;
        }

        SpawnAllPlayersAndStartGame();
    }

    /// <summary>
    /// Tells every client to hide lobby UI and show the game HUD.
    /// Runs via Netcode RPC so join clients are covered even when
    /// <see cref="AutoStartAfterSceneLoad"/> only runs on the server.
    /// </summary>
    [ClientRpc]
    private void ShowGameUIAllClientsClientRpc()
    {
        // Hide any lobby panel that may have survived scene loading
        // (e.g. if it lives under a DontDestroyOnLoad parent).
        var lobbyPanel = FindAnyObjectByType<InteractiveLobbyPanel>(FindObjectsInactive.Include);
        if (lobbyPanel != null) lobbyPanel.gameObject.SetActive(false);

        LobbyManager.Instance?.ShowGameUI();
        UIManager.Instance?.ShowGameUI();
    }

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
    }

    /// <summary>
    /// Spawns the host's own player object, then all remote clients' objects,
    /// then kicks off <see cref="StartGame"/>.  Server-only.
    /// </summary>
    public void SpawnAllPlayersAndStartGame()
    {
        if (!IsServer) return;

        Debug.Log("[GameManager] === SPAWNING ALL PLAYERS ===");

        _runnerSpawnIndex = 0;
        _catcherSpawnIndex = 0;

        var slotList = GameSettings.SlotAssignments;

        if (slotList.Count > 0)
        {
            // ── Slot-based spawning (lobby path) ─────────────────────────
            var sorted = new List<GameSettings.SlotAssignment>(slotList);
            sorted.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

            foreach (var a in sorted)
            {
                bool isRunner = a.SlotIndex < LobbyStateManager.RunnerSlotCount;
                int spawnIndex = isRunner ? a.SlotIndex
                                                 : a.SlotIndex - LobbyStateManager.RunnerSlotCount;
                PlayerType type = isRunner ? PlayerType.Runner : PlayerType.Catcher;

                SpawnPlayerAtIndex(a.ClientId, type, spawnIndex, a.LocalPlayerIndex);
            }
        }
        else
        {
            // ── Fallback: no slot data (e.g. direct editor test) ─────────
            Debug.LogWarning("[GameManager] No slot assignments found — using fallback spawn.");

            var cm = FindAnyObjectByType<NetworkConnectionManager>();
            if (cm == null) { Debug.LogError("[GameManager] NetworkConnectionManager missing."); return; }

            SpawnPlayerAtIndex(NetworkManager.ServerClientId,
                cm.GetPlayerType(NetworkManager.ServerClientId), 0, 0);

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == NetworkManager.ServerClientId) continue;
                if (client.PlayerObject != null) continue;

                var t = cm.GetPlayerType(client.ClientId);
                int idx = t == PlayerType.Runner ? _runnerSpawnIndex : _catcherSpawnIndex;
                SpawnPlayerAtIndex(client.ClientId, t, idx, 0);
            }
        }

        // Update lobby counter.
        var connMgr = FindAnyObjectByType<NetworkConnectionManager>();
        if (connMgr != null && connMgr.IsSpawned)
            connMgr.UpdatePlayerCountServerRpc();

        StartCoroutine(CountdownAndStart());
        Debug.Log($"[GameManager] Spawned {slotList.Count} player(s) — countdown starting.");
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

    /// <summary>Spawns coins near a completed button's position.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SpawnCoinsFromButtonServerRpc(NetworkObjectReference buttonRef, Vector3 position, int count, float radius)
    {
        coinSpawner?.SpawnCoinsAtPosition(position, count, radius);
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
    /// Spawns one player at the <paramref name="spawnIndex"/>-th spawn point
    /// and, for local players, pairs their physical device by
    /// <paramref name="localPlayerIndex"/>.
    ///
    /// Display numbers: Runner spawnIndex 0 → P1, Catcher spawnIndex 0 → P5.
    /// </summary>
    private void SpawnPlayerAtIndex(
        ulong clientId,
        PlayerType type,
        int spawnIndex,
        int localPlayerIndex = 0)
    {
        if (!IsServer) return;

        GameObject prefab = type == PlayerType.Runner ? playerPrefab : guardPrefab;
        Transform[] spawnPoints = type == PlayerType.Runner ? playerSpawnPoints : guardSpawnPoints;

        if (prefab == null)
        {
            Debug.LogError($"[GameManager] {type} prefab not assigned.");
            return;
        }
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[GameManager] No spawn points for {type}. Assign in Inspector.");
            return;
        }

        int spawnIdx = Mathf.Clamp(spawnIndex, 0, spawnPoints.Length - 1);
        int displayNum = type == PlayerType.Runner ? spawnIndex + 1 : spawnIndex + 5;

        GameObject obj = Instantiate(prefab, spawnPoints[spawnIdx].position, Quaternion.identity);
        obj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        if (type == PlayerType.Runner && obj.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayNum;
            obj.name = $"Runner_P{displayNum}";
        }
        else if (type == PlayerType.Catcher && obj.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayNum;
            obj.name = $"Catcher_P{displayNum}";
        }

        // Pair the physical device on the local machine.
        // For remote clients the assignment happens on their own machine via
        // ControlSetupManager when their PlayerMovement/Guard.OnNetworkSpawn fires.
        bool isLocalClient = clientId == NetworkManager.Singleton.LocalClientId;
        if (isLocalClient)
        {
            StartCoroutine(AssignDeviceAfterSpawn(obj, localPlayerIndex));
        }

        if (type == PlayerType.Runner) _runnerSpawnIndex++;
        else _catcherSpawnIndex++;

        Debug.Log($"[GameManager] Spawned {type} P{displayNum} for client {clientId} " +
                  $"(localIdx={localPlayerIndex}) at spawnPoint[{spawnIdx}].");
    }

    private System.Collections.IEnumerator AssignDeviceAfterSpawn(
        GameObject playerObj, int localPlayerIndex)
    {
        yield return null; // Wait one frame for PlayerInput to initialise.

        if (LocalPlayerManager.Instance != null)
            LocalPlayerManager.Instance.AssignDeviceToPlayer(playerObj, localPlayerIndex);
        else
            ControlSetupManager.Instance?.AssignControlScheme(playerObj, localPlayerIndex);
    }

    /// <summary>
    /// Fully resets the game: despawns players and coins, resets variables, respawns coins.
    /// </summary>
    private void ResetGame()
    {
        if (!IsServer) return;

        localSpawner?.DespawnLocalPlayers();
        coinSpawner?.ResetSpawner();

        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude))
            if (pm.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();

        foreach (var g in FindObjectsByType<Guard>(FindObjectsInactive.Exclude))
            if (g.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();

        score.Value = 0;
        playerLives.Value = InitialLives;
        gameTimer.Value = GameDuration;
        gameStarted.Value = false;
        _gameEnded = false;
        _runnerSpawnIndex = 0;
        _catcherSpawnIndex = 0;

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
            if (coin.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned)
                n.Despawn();
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
    {
        _lobbyManager?.HandleGameStart(current);
        _uiManager?.HandleGameStart(current);
    }

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
    // ====================================================================
    // Countdown
    // ====================================================================

    /// <summary>
    /// Runs the 3-2-1-Go countdown on all clients, then calls <see cref="StartGame"/>.
    /// Players are already spawned but movement is blocked until <see cref="gameStarted"/>
    /// becomes <c>true</c> at the end of the sequence.
    /// </summary>
    private System.Collections.IEnumerator CountdownAndStart()
    {
        // Spawn all game objects while the countdown plays.
        if (coinSpawner != null)
            coinSpawner.SpawnCoins();
        else
            Debug.LogError("[GameManager] CoinSpawner reference is missing!");

        StartCountdownClientRpc();
        yield return new WaitForSeconds(4.5f);
        StartGame();
    }

    [ClientRpc]
    private void StartCountdownClientRpc()
        => UIManager.Instance?.ShowCountdown();

    // ====================================================================
    // Pause
    // ====================================================================

    /// <summary>Sets or clears the global pause state. Callable by any client.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPausedServerRpc(bool paused) => isPaused.Value = paused;

    private void OnIsPausedChanged(bool previous, bool current)
        => UIManager.Instance?.OnPauseStateChanged(current);

    // ====================================================================
    // Player Disconnect During Game
    // ====================================================================

    private void OnClientDisconnectedDuringGame(ulong clientId)
    {
        if (!gameStarted.Value) return;
        NotifyPlayerLeftClientRpc();
    }

    [ClientRpc]
    private void NotifyPlayerLeftClientRpc()
        => UIManager.Instance?.ShowPlayerLeftMessage();

}