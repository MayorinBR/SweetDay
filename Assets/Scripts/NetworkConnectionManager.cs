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
/// Manages the full connection lifecycle: Unity Services initialisation,
/// Relay allocation, Lobby creation/heartbeat, connection approval,
/// client tracking, and scene transitions.
/// </summary>
public class NetworkConnectionManager : NetworkBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

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

    /// <summary>
    /// Returns the total number of players (local + remote runners + remote guards).
    /// </summary>
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

    private UIManager _uiManager;
    private GameManager _gameManager;

    private PlayerType _selectedPlayerType = PlayerType.Runner;
    private Dictionary<ulong, PlayerType> _clientPlayerTypes = new Dictionary<ulong, PlayerType>();
    private Dictionary<ulong, PlayerType> _activeClientCount = new Dictionary<ulong, PlayerType>();

    private int _lastPlayerCount;

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
        // If this component lives on a child object, reparent it here.
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
        InitializeUnityServices();

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

        _uiManager = FindAnyObjectByType<UIManager>();
        _gameManager = FindAnyObjectByType<GameManager>();

        totalPlayers.OnValueChanged += OnPlayerCountChanged;
        totalGuards.OnValueChanged += OnPlayerCountChanged;

        UpdatePlayerCounter();

        if (IsServer)
            Invoke(nameof(DelayedLobbyUpdate), 1f);
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

    /// <summary>
    /// Registers connection/disconnection callbacks on <see cref="NetworkManager.Singleton"/>.
    /// </summary>
    private void RegisterNetworkCallbacks()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    /// <summary>
    /// Attaches the connection-approval callback and connection event handlers.
    /// Called once before <see cref="NetworkManager.StartHost"/>.
    /// </summary>
    public void ConfigureNetworkManager()
    {
        if (NetworkManager.Singleton == null || _isNetcodeConfigured) return;

        NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        _isNetcodeConfigured = true;
    }

    private void OnServerStarted() { /* Reserved for future server-start logic. */ }

    private void OnClientConnected(ulong clientId)
    {
        if (IsServer) HandleClientConnected(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer) HandleClientDisconnected(clientId);

        // If the host dropped, go back to the main menu.
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

        // Parse the player type from the connection payload.
        PlayerType clientType = PlayerType.Runner;
        byte[] payload = request.Payload;
        if (payload != null && payload.Length > 0
            && Enum.IsDefined(typeof(PlayerType), (int)payload[0]))
        {
            clientType = (PlayerType)payload[0];
        }

        // For the host's own approval, use the locally selected type.
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
    // ====================================================================>

    /// <summary>
    /// Creates a Relay allocation, creates a Unity Lobby, and starts as host,
    /// then loads <paramref name="sceneName"/> for all connected clients.
    /// Re-tries lobby creation up to five times to avoid ambiguous lobby codes.
    /// </summary>
    public async void StartHostWithScene(string sceneName)
    {
        // Guard: prevent double-starts if the NetworkManager is already listening.
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

                    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                    transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

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

                    if (IsCodeClean(_currentLobby.LobbyCode))
                    {
                        _lobbyCode = _currentLobby.LobbyCode;
                        break;
                    }

                    // Code contains forbidden characters – delete and retry.
                    Debug.LogWarning($"[NetworkConnectionManager] Lobby code '{_currentLobby.LobbyCode}' " +
                                     "contains forbidden characters. Retrying.");
                    await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                    _currentLobby = null;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[NetworkConnectionManager] Host attempt {i + 1} failed: {e.Message}");
                    if (i == maxRetries - 1) throw;
                }

                if (i < maxRetries - 1)
                    await Task.Delay(500);
            }

            if (_currentLobby == null)
            {
                Debug.LogError("[NetworkConnectionManager] Could not create a clean lobby after all retries.");
                return;
            }

            ConfigureNetworkManager();

            byte[] payload = GetConnectionPayload();
            NetworkManager.Singleton.NetworkConfig.ConnectionData = payload;

            if (!NetworkManager.Singleton.IsListening)
            {
                bool success = NetworkManager.Singleton.StartHost();
                if (!success)
                {
                    Debug.LogError("[NetworkConnectionManager] NetworkManager.StartHost() returned false.");
                }
            }
            else
            {
                Debug.LogWarning("[NetworkConnectionManager] NetworkManager is already running. Skipping StartHost.");
            }

            ulong hostId = NetworkManager.Singleton.LocalClientId;
            _clientPlayerTypes.TryAdd(hostId, _selectedPlayerType);

            NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

            _uiManager?.ShowLobbyUI(true, _lobbyCode);

#pragma warning disable 4014
            HeartbeatLobbyAsync();
#pragma warning restore 4014
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
            catch (LobbyServiceException e) when (e.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                // Already gone – that's fine.
            }
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
            SceneManager.LoadScene("MenuScene");
            Invoke(nameof(ResetUIAfterSceneLoad), 0.5f);
        }
    }

    /// <summary>Convenience wrapper around <see cref="Disconnect"/> with menu navigation.</summary>
    public void ForceCleanDisconnect() => Disconnect(true);

    // ====================================================================
    // Game Flow
    // ====================================================================>

    /// <summary>
    /// Instructs the <see cref="GameManager"/> to start the game.
    /// Must be called directly on the server.
    /// </summary>
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

    /// <summary>
    /// ServerRpc version of <see cref="StartGame"/> for client-initiated calls.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void StartGameServerRpc()
    {
        _gameManager ??= FindAnyObjectByType<GameManager>();
        _gameManager?.StartGame();
    }

    /// <summary>
    /// Triggers a server-side player-count refresh.
    /// Only callable when the object is already spawned on the network.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void UpdatePlayerCountServerRpc() => UpdatePlayerCounter();

    /// <summary>
    /// Instructs all clients to load <paramref name="sceneName"/> via Netcode scene management.
    /// </summary>
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
        => _clientPlayerTypes.TryGetValue(clientId, out var t) ? t : PlayerType.Runner;

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
        if (_activeClientCount.TryGetValue(clientId, out PlayerType playerType))
        {
            if (playerType == PlayerType.Runner) totalPlayers.Value--;
            else totalGuards.Value--;

            _activeClientCount.Remove(clientId);
            UpdatePlayerCounter();
        }

        _clientPlayerTypes.Remove(clientId);
    }

    // ====================================================================
    // Private – Lobby Heartbeat
    // ====================================================================

    /// <summary>
    /// Long-running async loop that sends heartbeat pings to the Unity Lobby
    /// service and attempts to recreate the lobby on sustained failures.
    /// </summary>
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

        // Count actual spawned objects rather than relying solely on connection events.
        int runners = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude).Length;
        int catchers = FindObjectsByType<Guard>(FindObjectsInactive.Exclude).Length;

        totalPlayers.Value = runners;
        totalGuards.Value = catchers;

        if (IsSpawned) UpdateLobbyTextsClientRpc(_lobbyCode);
    }

    private void ResetConnectionState()
    {
        _clientPlayerTypes.Clear();
        _activeClientCount.Clear();

        // Only write NetworkVariables while still spawned.
        if (IsSpawned)
        {
            totalPlayers.Value = 0;
            totalGuards.Value = 0;
        }

        _selectedPlayerType = PlayerType.Runner;
        _lastPlayerCount = 0;
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

    private void ResetUIAfterSceneLoad()
        => Invoke(nameof(DelayedUIReset), 0.5f);

    private void DelayedUIReset()
    {
        FindAnyObjectByType<UIManager>()?.ShowMainMenuUI();
        MenuButtonHolder.ReconnectAllButtonsInScene();
    }

    private void DelayedLobbyUpdate()
    {
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
        if (UIManager.Instance == null) return;

        int current = GameSettings.LocalPlayerCount + totalPlayers.Value + totalGuards.Value;
        UIManager.Instance.UpdatePlayerCounter(current, MAX_TOTAL_PLAYERS);

        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && gm.gameStarted.Value) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        UIManager.Instance.ShowLobbyUI(isHost, lobbyCode);
    }

    [ClientRpc]
    private void NotifySceneChangeClientRpc(string sceneName)
    {
        UIManager.Instance?.ShowLoadingMessage($"Loading: {sceneName}...");
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