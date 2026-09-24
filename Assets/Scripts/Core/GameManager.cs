using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Thin orchestrator for the match: owns all networked state (<see cref="NetworkVariable{T}"/>
/// fields) and all RPC entry points, delegating spawn/despawn logic to
/// <see cref="GameSpawnSystem"/> and score/timer/lifecycle logic to <see cref="GameStateSystem"/>.
/// Must remain a <see cref="NetworkBehaviour"/> because Netcode requires ClientRpc/ServerRpc
/// methods and NetworkVariables to live on a NetworkBehaviour.
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
    public const float GameDuration = 180f;

    /// <summary>Number of lives players start with.</summary>
    public const int InitialLives = 3;

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

    /// <summary>Panel shown when the game ends (set inactive during play).</summary>
    public GameObject endGamePanel;

    // ====================================================================
    // System References
    // ====================================================================

    /// <summary>Handles spawning/despawning of players and coins. Lives on the same GameObject.</summary>
    public GameSpawnSystem spawnSystem { get; private set; }

    /// <summary>Handles score, hit processing, countdown, and win/lose/reset flow. Lives on the same GameObject.</summary>
    public GameStateSystem stateSystem { get; private set; }

    /// <summary>Score the runners must reach to win. Forwards to <see cref="GameStateSystem.scoreToWin"/>.</summary>
    public int scoreToWin => stateSystem.scoreToWin;

    /// <summary>World-space spawn points for runners. Forwards to <see cref="GameSpawnSystem.playerSpawnPoints"/>.</summary>
    public Transform[] playerSpawnPoints => spawnSystem.playerSpawnPoints;

    /// <summary>World-space spawn points for guards. Forwards to <see cref="GameSpawnSystem.guardSpawnPoints"/>.</summary>
    public Transform[] guardSpawnPoints => spawnSystem.guardSpawnPoints;

    // ====================================================================
    // Private State
    // ====================================================================

    private UIManager _uiManager;
    private LobbyManager _lobbyManager;

    /// <summary>Server-only: client IDs that have reported ready via <see cref="NotifyClientReadyServerRpc"/>.</summary>
    private readonly HashSet<ulong> _readyClients = new HashSet<ulong>();

    /// <summary>Server-only: guards against starting the match more than once.</summary>
    private bool _matchStarting;

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

        spawnSystem = GetComponent<GameSpawnSystem>();
        stateSystem = GetComponent<GameStateSystem>();

        if (spawnSystem == null)
            Debug.LogError("[GameManager] Missing GameSpawnSystem component on the same GameObject.");
        if (stateSystem == null)
            Debug.LogError("[GameManager] Missing GameStateSystem component on the same GameObject.");
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
            NetworkManager.Singleton.OnClientDisconnectCallback += stateSystem.OnClientDisconnectedDuringGame;

        _uiManager = FindAnyObjectByType<UIManager>();
        _lobbyManager = FindAnyObjectByType<LobbyManager>();

        if (IsServer)
        {
            playerLives.Value = InitialLives;
            gameTimer.Value = GameDuration;
        }

        // Every client (including the host) reports itself ready once its
        // own GameManager replica has spawned — i.e. once its local scene
        // has actually finished loading. The server waits until everyone
        // currently connected has reported in before spawning players and
        // starting the countdown (see NotifyClientReadyServerRpc). This is
        // what keeps the loading screen up for a client that is still
        // connecting/loading instead of racing ahead on the host alone.
        // NetworkManager.IsListening is true only when a session already
        // exists (i.e. we came from the lobby, not from a fresh editor play).
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NotifyClientReadyServerRpc();
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
            NetworkManager.Singleton.OnClientDisconnectCallback -= stateSystem.OnClientDisconnectedDuringGame;
    }

    /// <summary>
    /// Server-only: called by every client once its own game scene has
    /// loaded. Starts the spawn-and-countdown sequence only once every
    /// currently connected client has reported in, so the loading screen
    /// stays up for everyone until the slowest client is actually ready.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void NotifyClientReadyServerRpc(RpcParams rpcParams = default)
    {
        _readyClients.Add(rpcParams.Receive.SenderClientId);

        if (_matchStarting || _readyClients.Count < NetworkManager.Singleton.ConnectedClientsIds.Count)
            return;

        _matchStarting = true;
        StartCoroutine(AutoStartAfterSceneLoad());
    }

    // ====================================================================
    // Public Game Flow (Server only)
    // ====================================================================

    /// <summary>
    /// Called once every client has finished loading the game scene (see
    /// <see cref="NotifyClientReadyServerRpc"/>). Waits two more frames for
    /// scene <see cref="NetworkObject"/>s to finish registering, then calls
    /// <see cref="SpawnAllPlayersAndStartGame"/> automatically.
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
                           "Assign it in the Game Scene's GameSpawnSystem Inspector.");
            yield break;
        }

        if (needCatcherPoints && (guardSpawnPoints == null || guardSpawnPoints.Length == 0))
        {
            Debug.LogError("[GameManager] guardSpawnPoints is not assigned. " +
                           "Assign it in the Game Scene's GameSpawnSystem Inspector.");
            yield break;
        }

        SpawnAllPlayersAndStartGame();
    }

    /// <summary>
    /// Tells every client to hide lobby UI and show the game HUD.
    /// Runs via Netcode RPC so join clients are covered even when
    /// <see cref="AutoStartAfterSceneLoad"/> only runs on the server.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void ShowGameUIAllClientsClientRpc()
    {
        // Hide any lobby panel that may have survived scene loading
        // (e.g. if it lives under a DontDestroyOnLoad parent).
        var lobbyPanel = FindAnyObjectByType<InteractiveLobbyPanel>(FindObjectsInactive.Include);
        if (lobbyPanel != null) lobbyPanel.gameObject.SetActive(false);

        LobbyManager.Instance?.ShowGameUI();
        UIManager.Instance?.ShowGameUI();
    }

    /// <summary>Starts the match. Delegates to <see cref="GameStateSystem.StartGame"/>.</summary>
    public void StartGame() => stateSystem.StartGame();

    /// <summary>
    /// Spawns every player via <see cref="GameSpawnSystem"/>, then kicks off the
    /// countdown-and-start sequence via <see cref="GameStateSystem"/>. Server-only.
    /// </summary>
    public void SpawnAllPlayersAndStartGame()
    {
        if (!IsServer) return;

        spawnSystem.SpawnAllPlayers();
        StartCoroutine(stateSystem.CountdownAndStart());
    }

    // ====================================================================
    // Server RPCs
    // ====================================================================

    /// <summary>Adds <paramref name="value"/> to the global score. Delegates to <see cref="GameStateSystem.AddScore"/>.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddScoreServerRpc(int value) => stateSystem.AddScore(value);

    /// <summary>Subtracts <paramref name="value"/> from the global score. Delegates to <see cref="GameStateSystem.SubtractScore"/>.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SubtractScoreServerRpc(int value) => stateSystem.SubtractScore(value);

    /// <summary>Processes a hit on a player. Delegates to <see cref="GameStateSystem.ProcessPlayerHit"/>.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ProcessPlayerHitWithReferenceServerRpc(NetworkObjectReference playerRef)
        => stateSystem.ProcessPlayerHit(playerRef);

    /// <summary>Spawns coins near a completed button's position.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SpawnCoinsFromButtonServerRpc(NetworkObjectReference buttonRef, Vector3 position, int count, float radius)
        => spawnSystem.SpawnCoinsAtPosition(position, count, radius);

    /// <summary>Resets the game state and restarts without reloading the scene.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ResetGameServerRpc() => stateSystem.ResetGame();

    /// <summary>Sets or clears the global pause state. Callable by any client.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPausedServerRpc(bool paused) => stateSystem.SetPaused(paused);

    /// <summary>
    /// Performs a full in-place game restart without disconnecting any player.
    /// Resets positions, coins, lives, timer and re-runs the countdown sequence.
    /// Host-only; all clients participate via existing NetworkVariables and RPCs.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void FullRestartGameServerRpc()
    {
        if (!IsServer) return;
        StartCoroutine(stateSystem.FullRestartSequence());
    }

    // ====================================================================
    // Client RPCs
    // ====================================================================

    /// <summary>Hides the end-game panel on all clients.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void HideEndGamePanelClientRpc() => _uiManager?.HideEndGamePanel();

    /// <summary>Shows the win/lose message on all clients. Called by <see cref="GameStateSystem.EndGame"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void NotifyEndGameClientRpc(bool runnersWon)
    {
        var localPlayerObj = NetworkManager.Singleton.LocalClient?.PlayerObject;
        bool isRunner = localPlayerObj != null && localPlayerObj.GetComponent<PlayerMovement>() != null;

        string message = runnersWon
            ? (isRunner ? "Runners Victory!" : "Catchers Lose!")
            : (isRunner ? "Runners Lose!" : "Catchers Victory!");

        Debug.Log($"[GameManager] Game Over: {message}");
        UIManager.Instance?.ShowSimpleEndGame(message);
    }

    /// <summary>Re-assigns split-screen cameras after a reset. Called by <see cref="GameStateSystem.ResetGame"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void ReassignCamerasClientRpc()
        => FindAnyObjectByType<SplitScreenManager>()?.ReassignCamerasAfterReset();

    /// <summary>Snaps a single player's camera after a teleport. Called by <see cref="GameSpawnSystem.ResetAllPlayersPosition"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void ResetCameraForPlayerClientRpc(ulong playerNetworkId, Vector3 newPosition)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
            .TryGetValue(playerNetworkId, out NetworkObject netObj)) return;

        if (!netObj.IsOwner) return;

        CameraFollow cam = FindAnyObjectByType<CameraFollow>();
        if (cam != null && cam.Target == netObj.transform)
            cam.ForcePosition();
    }

    /// <summary>Resets all split-screen cameras. Called by <see cref="GameSpawnSystem.ResetAllPlayersPosition"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void ResetAllCamerasClientRpc()
        => FindAnyObjectByType<SplitScreenManager>()?.ResetAllCameras();

    /// <summary>Shows the 3-2-1-Go countdown. Called by <see cref="GameStateSystem.CountdownAndStart"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void StartCountdownClientRpc()
    {
        // UIManager.Instance may be null on late-joining clients;
        // fall back to a scene search to guarantee the countdown shows.
        var ui = UIManager.Instance ?? FindAnyObjectByType<UIManager>();
        ui?.ShowCountdown();
    }

    /// <summary>Notifies all clients that a player disconnected mid-match. Called by <see cref="GameStateSystem.OnClientDisconnectedDuringGame"/>.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void NotifyPlayerLeftClientRpc()
        => UIManager.Instance?.ShowPlayerLeftMessage();

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

    private void OnIsPausedChanged(bool previous, bool current)
        => UIManager.Instance?.OnPauseStateChanged(current);

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
