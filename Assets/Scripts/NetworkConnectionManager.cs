using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Identifies the role a player has chosen before joining a session.
/// </summary>
public enum PlayerType { Runner, Catcher }

/// <summary>
/// Identifies whether a session is primarily local split-screen or online.
/// Both modes create a Unity Relay allocation so online players can always join.
/// </summary>
public enum SessionMode { Local, Online }

/// <summary>
/// Manages the full connection lifecycle: Unity Services initialisation,
/// Relay allocation, Lobby creation/heartbeat, connection approval,
/// client tracking, and scene transitions.
/// This object persists across scenes via <see cref="DontDestroyOnLoad"/>.
/// </summary>
public class NetworkConnectionManager : NetworkBehaviour
{
    // ====================================================================
    // Scene Name Constants
    // ====================================================================

    /// <summary>Name of the lobby configuration scene (loaded after session creation).</summary>
    public const string LobbySceneName = "LobbyScene";

    /// <summary>Name of the main menu scene (returned to on disconnect).</summary>
    public const string MenuSceneName = "MenuScene";

    /// <summary>Gets the singleton instance of <see cref="NetworkConnectionManager"/>.</summary>
    public static NetworkConnectionManager Instance { get; private set; }

    // ====================================================================
    // Capacity Constants
    // ====================================================================

    /// <summary>Maximum total players (local + remote) in a single session.</summary>
    public const int MAX_TOTAL_PLAYERS = 6;

    /// <summary>Maximum players sharing one machine (split-screen).</summary>
    public const int MAX_LOCAL_PLAYERS = 4;

    /// <summary>Maximum Guard (catcher) slots.</summary>
    public const int MAX_GUARDS = 2;

    /// <summary>Maximum Runner slots.</summary>
    public const int MAX_RUNNERS = 4;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Number of connected Runner players, replicated to all clients.</summary>
    public NetworkVariable<int> totalPlayers = new NetworkVariable<int>(0);

    /// <summary>Number of connected Guard (catcher) players, replicated to all clients.</summary>
    public NetworkVariable<int> totalGuards = new NetworkVariable<int>(0);

    // ====================================================================
    // Public Properties
    // ====================================================================

    /// <summary>Gets the human-readable lobby code for display in the UI.</summary>
    public string LobbyCode => _lobbyCode;

    /// <summary>Gets whether Unity Services have been successfully initialised.</summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>Returns the total number of players (local + remote runners + remote guards).</summary>
    public int TotalConnectedPlayers =>
        GameSettings.LocalPlayerCount + totalPlayers.Value + totalGuards.Value;

    // ====================================================================
    // Private Fields
    // ====================================================================

    private Lobby _currentLobby;
    private string _playerLobbyId;
    private string _lobbyCode = string.Empty;
    private bool _isInitialized;
    private bool _isNetcodeConfigured;

    /// <summary>Characters excluded from lobby codes to avoid visual confusion.</summary>
    private const string ForbiddenChars = "OIL0";

    private const float HeartbeatInterval = 10f;

    private LobbyManager _uiManager;
    private GameManager _gameManager;

    private PlayerType _selectedPlayerType = PlayerType.Runner;
    private Dictionary<ulong, PlayerType> _clientPlayerTypes = new Dictionary<ulong, PlayerType>();
    private Dictionary<ulong, PlayerType> _activeClientCount = new Dictionary<ulong, PlayerType>();

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

        // DontDestroyOnLoad requires the object to be a scene root.
        if (transform.parent != null)
        {
            Debug.LogWarning("[NetworkConnectionManager] Object is not a root GameObject. " +
                             "Detaching from parent so DontDestroyOnLoad works correctly.");
            transform.SetParent(null);
        }

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Fire-and-forget: InitializeAsync must not block Start().
        // Using a local async void wrapper avoids the InvalidOperationException
        // that occurs when a running Task is inadvertently disposed via the
        // discard pattern in some Unity/IL2CPP configurations.
        InitializeAsync();

        if (NetworkManager.Singleton != null)
            RegisterNetworkCallbacks();
    }

    // ====================================================================
    // NetworkBehaviour Overrides
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _uiManager = FindAnyObjectByType<LobbyManager>();
        _gameManager = FindAnyObjectByType<GameManager>();

        totalPlayers.OnValueChanged += OnPlayerCountChanged;
        totalGuards.OnValueChanged += OnPlayerCountChanged;

        UpdatePlayerCounter();

        if (IsServer)
            StartCoroutine(DelayedLobbyUpdateCoroutine());
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        totalPlayers.OnValueChanged -= OnPlayerCountChanged;
        totalGuards.OnValueChanged -= OnPlayerCountChanged;
    }

    // ====================================================================
    // Unity Services Initialisation
    // ====================================================================

    /// <summary>
    /// Fire-and-forget entry point called from <c>Start</c>.
    /// Using <c>async void</c> here is intentional: it lets the initialisation
    /// run without blocking the Unity main thread and without leaving a Task
    /// reference that could be disposed prematurely.
    /// </summary>
    private async void InitializeAsync()
    {
        await InitializeUnityServices();
    }

    /// <summary>
    /// Asynchronously initialises Unity Services and signs in the local player anonymously.
    /// Safe to call multiple times; subsequent calls are no-ops.
    /// </summary>
    public async Task InitializeUnityServices()
    {
        if (_isInitialized) return;

        try
        {
            await UnityServices.InitializeAsync();

            AuthenticationService.Instance.SignedIn += () =>
                _playerLobbyId = AuthenticationService.Instance.PlayerId;

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _isInitialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkConnectionManager] Failed to initialise Unity Services: {e.Message}");
        }
    }

    // ====================================================================
    // NetworkManager Callbacks
    // ====================================================================

    /// <summary>Registers connection/disconnection callbacks on <see cref="NetworkManager.Singleton"/>.</summary>
    private void RegisterNetworkCallbacks()
    {
        if (_isNetcodeConfigured) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        _isNetcodeConfigured = true;
    }

    /// <summary>
    /// Attaches the connection-approval callback before <see cref="NetworkManager.StartHost"/>.
    /// Safe to call multiple times; unsubscribes before subscribing to prevent duplicate registrations.
    /// </summary>
    public void ConfigureNetworkManager()
    {
        if (NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;

        RegisterNetworkCallbacks();
    }

    private void OnServerStarted() { /* Reserved for future server-start logic. */ }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer) HandleClientConnected(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer) HandleClientDisconnected(clientId);

        if (clientId == NetworkManager.ServerClientId)
            Disconnect(true);
    }

    // ====================================================================
    // Connection Approval
    // ====================================================================

    /// <summary>
    /// Called by Netcode for every pending connection request.
    /// Rejects connections when the session is full.
    /// </summary>
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        int totalConnected = GameSettings.LocalPlayerCount
                           + totalPlayers.Value
                           + totalGuards.Value;

        if (totalConnected >= MAX_TOTAL_PLAYERS)
        {
            response.Approved = false;
            response.Reason = "Server is full";
            return;
        }

        PlayerType clientType = PlayerType.Runner;
        byte[] payload = request.Payload;
        if (payload != null && payload.Length > 0
            && Enum.IsDefined(typeof(PlayerType), (int)payload[0]))
        {
            clientType = (PlayerType)payload[0];
        }

        if (NetworkManager.Singleton.IsHost
            && request.ClientNetworkId == NetworkManager.Singleton.LocalClientId)
        {
            _clientPlayerTypes.TryAdd(request.ClientNetworkId, _selectedPlayerType);
        }
        else
        {
            _clientPlayerTypes.TryAdd(request.ClientNetworkId, clientType);
        }

        response.Approved = true;
        response.CreatePlayerObject = false;
        response.Pending = false;
    }

    // ====================================================================
    // Host / Client Entry Points
    // ====================================================================

    /// <summary>
    /// Creates a Relay allocation, creates a Unity Lobby, and starts as host,
    /// then loads <paramref name="sceneName"/> for all connected clients.
    /// </summary>
    public async void StartHostWithScene(string sceneName)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.LogWarning("[NetworkConnectionManager] StartHostWithScene called while already listening. Ignoring.");
            return;
        }

        try
        {
            if (!_isInitialized)
                await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            const int maxRetries = 5;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var allocation = await RelayService.Instance.CreateAllocationAsync(MAX_TOTAL_PLAYERS);
                    string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                    var options = new CreateLobbyOptions
                    {
                        IsPrivate = false,
                        Data = new Dictionary<string, DataObject>
                        {
                            { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayCode)  },
                            { "SceneName",     new DataObject(DataObject.VisibilityOptions.Member, sceneName) },
                            { "LocalPlayers",  new DataObject(DataObject.VisibilityOptions.Member,
                                                              GameSettings.LocalPlayerCount.ToString()) }
                        }
                    };

                    _currentLobby = await LobbyService.Instance
                        .CreateLobbyAsync($"Lobby_{relayCode}", MAX_TOTAL_PLAYERS, options);

                    if (!IsCodeClean(_currentLobby.LobbyCode))
                    {
                        Debug.LogWarning($"[NetworkConnectionManager] Lobby code '{_currentLobby.LobbyCode}' " +
                                         "contains forbidden characters. Retrying.");
                        await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                        _currentLobby = null;

                        if (i < maxRetries - 1)
                            await Task.Delay(500);

                        continue;
                    }

                    // Code is clean — configure transport with THIS allocation and start host.
                    _lobbyCode = _currentLobby.LobbyCode;

                    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

                    ConfigureNetworkManager();
                    NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

                    if (!NetworkManager.Singleton.StartHost())
                    {
                        Debug.LogError("[NetworkConnectionManager] NetworkManager.StartHost() returned false.");
                        return;
                    }

                    ulong hostId = NetworkManager.Singleton.LocalClientId;
                    _clientPlayerTypes.TryAdd(hostId, _selectedPlayerType);

                    NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                    _uiManager?.ShowLobbyUI(true, _lobbyCode);
                    _ = HeartbeatLobbyAsync();
                    _uiManager?.ShowLobbySetupUI();
                    return; // Success — exit the method entirely.
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetworkConnectionManager] Host attempt {i + 1} failed: {e.Message}");
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(500);
                }
            }

            Debug.LogError("[NetworkConnectionManager] Could not create a clean lobby after all retries.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkConnectionManager] StartHostWithScene error: {e.Message}");
        }
    }

    /// <summary>
    /// Joins an existing session using a lobby code, configures the Relay transport,
    /// and starts as a client.
    /// </summary>
    public async void StartClientWithCode(string joinCode)
    {
        try
        {
            if (!_isInitialized)
                await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);
            _currentLobby = lobby;

            string relayJoinCode = _currentLobby.Data["RelayJoinCode"].Value;
            string sceneName = _currentLobby.Data["SceneName"].Value;

            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            if (NetworkManager.Singleton.StartClient())
                _uiManager?.ShowLobbyUI(false, joinCode);
        }
        catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
        {
            Debug.LogError($"[NetworkConnectionManager] Lobby not found for code: {joinCode}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkConnectionManager] StartClientWithCode error: {e.Message}");
        }
    }

    // ====================================================================
    // Disconnect / Cleanup
    // ====================================================================

    /// <summary>
    /// Shuts down the NetworkManager, leaves/deletes the lobby, resets all state,
    /// and optionally navigates to the main menu.
    /// </summary>
    public async void Disconnect(bool goToMainMenu = true)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            await Task.Delay(100);
        }

        if (_currentLobby != null && !string.IsNullOrEmpty(_currentLobby.Id))
        {
            try
            {
                bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
                if (isHost)
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                else
                    await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _playerLobbyId);
            }
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkConnectionManager] Lobby cleanup warning: {e.Message}");
            }
            finally
            {
                _currentLobby = null;
                _lobbyCode = string.Empty;
            }
        }

        ResetConnectionState();

        if (goToMainMenu)
        {
            SceneManager.LoadScene(MenuSceneName);

            // Use a coroutine instead of Invoke so we can check whether this
            // MonoBehaviour is still alive before executing, avoiding
            // MissingReferenceException when an async exception triggers
            // Disconnect after a scene transition has already destroyed the object.
            if (this != null && gameObject != null)
                StartCoroutine(DelayedUIResetCoroutine());
        }
    }

    /// <summary>Convenience wrapper around <see cref="Disconnect"/> with menu navigation.</summary>
    public void ForceCleanDisconnect() => Disconnect(true);

    // ====================================================================
    // Game Flow
    // ====================================================================

    /// <summary>Instructs the <see cref="GameManager"/> to start the game. Server-only.</summary>
    public void StartGame()
    {
        if (!IsServer)
        {
            Debug.LogError("[NetworkConnectionManager] StartGame called on a non-server instance.");
            return;
        }

        _gameManager ??= FindAnyObjectByType<GameManager>();
        if (_gameManager != null)
            _gameManager.StartGame();
        else
            Debug.LogError("[NetworkConnectionManager] GameManager not found.");
    }

    /// <summary>ServerRpc version of <see cref="StartGame"/> for client-initiated calls.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartGameServerRpc()
    {
        _gameManager ??= FindAnyObjectByType<GameManager>();
        _gameManager?.StartGame();
    }

    /// <summary>Triggers a server-side player-count refresh.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpdatePlayerCountServerRpc() => UpdatePlayerCounter();

    /// <summary>Instructs all clients to load <paramref name="sceneName"/>.</summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void LoadGameSceneServerRpc(string sceneName)
    {
        if (!IsServer) return;
        NotifySceneChangeClientRpc(sceneName);
        NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    // ====================================================================
    // Player Type Helpers
    // ====================================================================

    /// <summary>
    /// Returns the <see cref="PlayerType"/> registered for <paramref name="clientId"/>,
    /// defaulting to <see cref="PlayerType.Runner"/> if unknown.
    /// </summary>
    public PlayerType GetPlayerType(ulong clientId)
    {
        foreach (var a in GameSettings.SlotAssignments)
            if (a.ClientId == clientId)
                return a.SlotIndex < LobbyStateManager.RunnerSlotCount
                    ? PlayerType.Runner : PlayerType.Catcher;
        return _clientPlayerTypes.TryGetValue(clientId, out var t) ? t : PlayerType.Runner;
    }

    /// <summary>Sets the player type chosen by the local player before joining.</summary>
    public void SetPlayerType(bool isCatcher)
        => _selectedPlayerType = isCatcher ? PlayerType.Catcher : PlayerType.Runner;

    /// <summary>Returns the connection payload byte array encoding the chosen <see cref="PlayerType"/>.</summary>
    public byte[] GetConnectionPayload() => new byte[] { (byte)_selectedPlayerType };

    // ====================================================================
    // Private – Client Tracking
    // ====================================================================

    private void HandleClientConnected(ulong clientId)
    {
        if (_activeClientCount.ContainsKey(clientId))
        {
            Debug.LogWarning($"[NetworkConnectionManager] Client {clientId} already counted. Skipping duplicate.");
            return;
        }

        PlayerType playerType = _clientPlayerTypes.TryGetValue(clientId, out var t)
            ? t
            : PlayerType.Runner;

        if (!_clientPlayerTypes.ContainsKey(clientId))
        {
            Debug.LogWarning($"[NetworkConnectionManager] PlayerType not found for client {clientId}. Defaulting to Runner.");
            _clientPlayerTypes[clientId] = PlayerType.Runner;
        }

        if (playerType == PlayerType.Runner) totalPlayers.Value++;
        else totalGuards.Value++;

        _activeClientCount.Add(clientId, playerType);
        UpdatePlayerCounter();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        // Release the lobby slot so the button becomes available again.
        LobbyStateManager.Instance?.ReleaseClientSlot(clientId);

        if (_activeClientCount.TryGetValue(clientId, out PlayerType playerType))
        {
            if (playerType == PlayerType.Runner) totalPlayers.Value--;
            else totalGuards.Value--;
            _activeClientCount.Remove(clientId);
        }

        _clientPlayerTypes.Remove(clientId);
        GameSettings.SlotAssignments.RemoveAll(a => a.ClientId == clientId);
        UpdatePlayerCounter();
    }

    // ====================================================================
    // Private – Lobby Heartbeat
    // ====================================================================

    private async Task HeartbeatLobbyAsync()
    {
        while (_currentLobby != null
               && NetworkManager.Singleton != null
               && NetworkManager.Singleton.IsHost)
        {
            bool ok = await SendHeartbeatWithRetry();
            if (ok)
            {
                if (IsSpawned) UpdateLobbyTextsClientRpc(_lobbyCode);
            }
            else
            {
                Debug.LogWarning("[NetworkConnectionManager] Heartbeat failed; attempting lobby recreation.");
                await RecreateLobbyWithSameCode();
            }

            await Task.Delay(TimeSpan.FromSeconds(HeartbeatInterval));
        }
    }

    private async Task<bool> SendHeartbeatWithRetry()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkConnectionManager] Heartbeat attempt {attempt + 1} failed: {e.Message}");
                if (attempt < 2)
                    await Task.Delay(1000 * (attempt + 1));
            }
        }
        return false;
    }

    private async Task<bool> RecreateLobbyWithSameCode()
    {
        try
        {
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = new Player(
                    id: AuthenticationService.Instance.PlayerId,
                    allocationId: null,
                    data: new Dictionary<string, PlayerDataObject>
                    {
                        { "PlayerType", new PlayerDataObject(
                            PlayerDataObject.VisibilityOptions.Member,
                            _selectedPlayerType.ToString()) }
                    }),
                Data = new Dictionary<string, DataObject>
                {
                    { "JoinCode", new DataObject(DataObject.VisibilityOptions.Member, _lobbyCode) }
                }
            };

            _currentLobby = await LobbyService.Instance
                .CreateLobbyAsync($"Lobby_{_lobbyCode}", MAX_RUNNERS + MAX_GUARDS, options);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkConnectionManager] RecreateLobby failed: {e}");
            return false;
        }
    }

    // ====================================================================
    // Private – State Helpers
    // ====================================================================

    private void UpdatePlayerCounter()
    {
        if (!IsServer) return;

        // In the lobby phase: count connected clients (slots show actual selections).
        // In game phase: count spawned player objects for accuracy.
        int connected = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.ConnectedClientsList.Count
            : 0;

        // Update networkVariables so LobbyManager / InteractiveLobbyPanel can read them.
        totalPlayers.Value = connected;
        totalGuards.Value = 0; // detailed split shown by LobbyStateManager slots

        if (IsSpawned) UpdateLobbyTextsClientRpc(_lobbyCode);
    }

    private void ResetConnectionState()
    {
        _clientPlayerTypes.Clear();
        _activeClientCount.Clear();

        if (IsSpawned)
        {
            totalPlayers.Value = 0;
            totalGuards.Value = 0;
        }

        _selectedPlayerType = PlayerType.Runner;
        _isNetcodeConfigured = false;
    }

    private bool IsCodeClean(string code)
        => !string.IsNullOrEmpty(code) && code.IndexOfAny(ForbiddenChars.ToCharArray()) == -1;

    // ====================================================================
    // Private – Async Prep for New Game
    // ====================================================================

    /// <summary>
    /// Shuts down the current session cleanly and resets all state so a fresh host
    /// or client flow can begin.
    /// </summary>
    public async Task<bool> CleanStartNewGame(string sceneName)
    {
        if (!_isInitialized)
            await InitializeUnityServices();

        ResetConnectionState();

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkConnectionManager] Re-auth failed: {e.Message}");
                return false;
            }
        }

        if (NetworkManager.Singleton != null)
        {
            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
                await Task.Delay(200);
            }

            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            _isNetcodeConfigured = false;
        }

        return true;
    }

    // ====================================================================
    // UI Helpers
    // ====================================================================

    /// <summary>
    /// Waits one second after scene load, then resets the main-menu UI and
    /// reconnects button listeners.  Uses a coroutine (not <c>Invoke</c>) so
    /// the null check on <c>this</c> prevents a
    /// <see cref="MissingReferenceException"/> when the object is destroyed
    /// mid-flight by an async exception during a scene transition.
    /// </summary>
    private System.Collections.IEnumerator DelayedUIResetCoroutine()
    {
        yield return new UnityEngine.WaitForSeconds(1f);

        // Guard: the object may have been destroyed while we were waiting.
        if (this == null) yield break;

        FindAnyObjectByType<LobbyManager>()?.ShowMainMenuUI();
        MenuButtonHolder.ReconnectAllButtonsInScene();
    }

    private System.Collections.IEnumerator DelayedLobbyUpdateCoroutine()
    {
        yield return new UnityEngine.WaitForSeconds(1f);
        if (this == null) yield break;
        if (IsServer && IsSpawned)
            UpdateLobbyTextsClientRpc(_lobbyCode);
    }

    private void OnPlayerCountChanged(int previous, int current)
        => UpdatePlayerCounter();

    // ====================================================================
    // Client RPCs
    // ====================================================================

    [ClientRpc]
    private void UpdateLobbyTextsClientRpc(string lobbyCode)
    {
        if (LobbyManager.Instance == null) return;

        int current = GameSettings.LocalPlayerCount + totalPlayers.Value + totalGuards.Value;
        LobbyManager.Instance.UpdatePlayerCounter(current, MAX_TOTAL_PLAYERS);

        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.gameStarted.Value) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        LobbyManager.Instance.ShowLobbyUI(isHost, lobbyCode);
    }

    [ClientRpc]
    private void NotifySceneChangeClientRpc(string sceneName)
    {
        LobbyManager.Instance?.ShowLoadingMessage($"Loading: {sceneName}...");
    }

    // ====================================================================
    // Public – New Session Entry Points (called by MenuManager)
    // ====================================================================

    /// <summary>
    /// Creates a Unity Relay + Lobby and starts as host, then invokes
    /// <paramref name="onSuccess"/> or <paramref name="onFailure"/> on the
    /// main thread.  The scene is NOT loaded yet — the host navigates to
    /// <see cref="LobbySetupPanel"/> first, then calls
    /// <see cref="GameManager.SpawnAllPlayersAndStartGame"/> when ready.
    ///
    /// Both <see cref="SessionMode.Local"/> and <see cref="SessionMode.Online"/>
    /// create a Relay so online players can join via the lobby code.
    /// The <paramref name="mode"/> value is stored for display purposes in
    /// <see cref="LobbySetupPanel"/>.
    /// </summary>
    /// <param name="mode">Session intent (local split-screen vs online-first).</param>
    /// <param name="onSuccess">Called on the main thread when the host is ready.</param>
    /// <param name="onFailure">Called on the main thread with an error message on failure.</param>
    public async void StartSessionAndGoToLobby(
        SessionMode mode,
        System.Action onSuccess,
        System.Action<string> onFailure)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            onFailure?.Invoke("A session is already running.");
            return;
        }

        try
        {
            if (!_isInitialized)
                await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            const int maxRetries = 5;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var allocation = await RelayService.Instance.CreateAllocationAsync(MAX_TOTAL_PLAYERS);
                    string relayCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                    var options = new CreateLobbyOptions
                    {
                        IsPrivate = false,
                        Data = new Dictionary<string, DataObject>
                        {
                            { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayCode) },
                            { "SessionMode",   new DataObject(DataObject.VisibilityOptions.Member, mode.ToString()) }
                        }
                    };

                    _currentLobby = await LobbyService.Instance
                        .CreateLobbyAsync($"Lobby_{relayCode}", MAX_TOTAL_PLAYERS, options);

                    if (!IsCodeClean(_currentLobby.LobbyCode))
                    {
                        await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                        _currentLobby = null;
                        if (i < maxRetries - 1) { await Task.Delay(500); continue; }
                        else throw new Exception("Could not obtain a clean lobby code after all retries.");
                    }

                    _lobbyCode = _currentLobby.LobbyCode;

                    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

                    ConfigureNetworkManager();
                    NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

                    if (!NetworkManager.Singleton.StartHost())
                        throw new Exception("NetworkManager.StartHost() returned false.");

                    _clientPlayerTypes.TryAdd(NetworkManager.Singleton.LocalClientId, _selectedPlayerType);

                    // Load the Lobby Scene for all connected clients via Netcode's
                    // scene manager.  Clients who join later are automatically placed
                    // in the host's current scene by Netcode.
                    NetworkManager.Singleton.SceneManager.LoadScene(
                        LobbySceneName, LoadSceneMode.Single);

                    _ = HeartbeatLobbyAsync();
                    onSuccess?.Invoke();
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetworkConnectionManager] Session attempt {i + 1} failed: {e.Message}");
                    if (i == maxRetries - 1) throw;
                    await Task.Delay(500);
                }
            }
        }
        catch (Exception e)
        {
            onFailure?.Invoke(e.Message);
        }
    }

    /// <summary>
    /// Joins an existing session by lobby code and starts as a client,
    /// then invokes <paramref name="onSuccess"/> or <paramref name="onFailure"/>.
    /// </summary>
    /// <param name="joinCode">The 6-character lobby code.</param>
    /// <param name="onSuccess">Called on the main thread when the client is connected.</param>
    /// <param name="onFailure">Called on the main thread with an error message on failure.</param>
    public async void JoinSessionByCode(
        string joinCode,
        System.Action onSuccess,
        System.Action<string> onFailure)
    {
        try
        {
            if (!_isInitialized)
                await InitializeUnityServices();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(joinCode);
            _currentLobby = lobby;

            string relayJoinCode = _currentLobby.Data["RelayJoinCode"].Value;

            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            NetworkManager.Singleton.NetworkConfig.ConnectionData = GetConnectionPayload();

            if (!NetworkManager.Singleton.StartClient())
                throw new Exception("NetworkManager.StartClient() returned false.");

            _lobbyCode = joinCode;
            onSuccess?.Invoke();
        }
        catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
        {
            onFailure?.Invoke($"Lobby '{joinCode}' not found.");
        }
        catch (Exception e)
        {
            onFailure?.Invoke(e.Message);
        }
    }

    // ====================================================================
    // Cleanup
    // ====================================================================

    /// <inheritdoc/>
    public override void OnDestroy()
    {
        base.OnDestroy();

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }
}