using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages the Lobby Setup screen that appears after a session is created or joined.
/// </summary>
public class LobbySetupPanel : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Player Count Dropdowns")]
    /// <summary>Dropdown for the number of local Runners on Screen 1 (options: 1, 2, 3, 4).</summary>
    public TMP_Dropdown runnerCountDropdown;

    /// <summary>Dropdown for the number of local Catchers on Screen 2 (options: 1, 2).</summary>
    public TMP_Dropdown catcherCountDropdown;

    [Header("Scene Selection (host only)")]
    /// <summary>Dropdown listing available game scenes. Hidden for non-host clients.</summary>
    public TMP_Dropdown sceneDropdown;

    [Header("Lobby Info")]
    /// <summary>Displays the join code that online players use to connect.</summary>
    public TextMeshProUGUI lobbyCodeText;

    /// <summary>Live player count label, e.g. "Players: 3 / 6".</summary>
    public TextMeshProUGUI playerCountText;

    /// <summary>Badge label showing "Local Session" or "Online Session".</summary>
    public TextMeshProUGUI sessionModeText;

    [Header("Action Buttons")]
    /// <summary>Starts the match. Shown only for the host.</summary>
    public Button startMatchButton;

    /// <summary>Disconnects and returns to the main menu.</summary>
    public Button disconnectButton;

    /// <summary>Quits the application.</summary>
    public Button quitButton;

    // ====================================================================
    // Constants
    // ====================================================================

    private const int MaxRunners = 4;
    private const int MaxCatchers = 2;

    // ====================================================================
    // Private
    // ====================================================================

    private NetworkConnectionManager _connectionManager;
    private string _selectedScene = "TestScene_Flat";

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void OnEnable()
    {
        _connectionManager = NetworkConnectionManager.Instance;

        InitialiseDropdowns();
        WireButtons();
        Refresh();

        // Subscribe to player-count changes so the label updates in real time.
        if (_connectionManager != null)
        {
            _connectionManager.totalPlayers.OnValueChanged += OnPlayerCountChanged;
            _connectionManager.totalGuards.OnValueChanged += OnPlayerCountChanged;
        }
    }

    private void OnDisable()
    {
        if (_connectionManager != null)
        {
            _connectionManager.totalPlayers.OnValueChanged -= OnPlayerCountChanged;
            _connectionManager.totalGuards.OnValueChanged -= OnPlayerCountChanged;
        }
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Refreshes all labels and button states.  Call whenever the panel
    /// is shown or a connection-state change is detected.
    /// </summary>
    public void Refresh()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        // Lobby code.
        if (lobbyCodeText != null)
        {
            string code = _connectionManager != null ? _connectionManager.LobbyCode : "—";
            lobbyCodeText.text = string.IsNullOrEmpty(code) ? "Local (no code)" : $"Code: {code}";
        }

        // Session mode badge.
        if (sessionModeText != null)
            sessionModeText.text = _connectionManager != null && !string.IsNullOrEmpty(_connectionManager.LobbyCode)
                ? "Online Session"
                : "Local Session";

        // Player count.
        RefreshPlayerCount();

        // Scene dropdown visible only for host.
        if (sceneDropdown != null)
            sceneDropdown.gameObject.SetActive(isHost);

        // Start button visible only for host.
        if (startMatchButton != null)
            startMatchButton.gameObject.SetActive(isHost);

        // Dropdowns editable only by host; always hidden on mobile.
#if UNITY_ANDROID || UNITY_IOS
        if (runnerCountDropdown  != null) runnerCountDropdown.gameObject.SetActive(false);
        if (catcherCountDropdown != null) catcherCountDropdown.gameObject.SetActive(false);
#else
        bool editable = isHost;
        if (runnerCountDropdown != null) runnerCountDropdown.interactable = editable;
        if (catcherCountDropdown != null) catcherCountDropdown.interactable = editable;
#endif
    }

    // ====================================================================
    // Private – Initialisation
    // ====================================================================

    private void InitialiseDropdowns()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Mobile: single player per device — split-screen is disabled.
        GameSettings.LocalRunnerCount  = 1;
        GameSettings.LocalCatcherCount = 0;
        if (runnerCountDropdown  != null) runnerCountDropdown.gameObject.SetActive(false);
        if (catcherCountDropdown != null) catcherCountDropdown.gameObject.SetActive(false);
#else
        // Runner count (1–4).
        if (runnerCountDropdown != null)
        {
            runnerCountDropdown.ClearOptions();
            var opts = new List<TMP_Dropdown.OptionData>();
            for (int i = 1; i <= MaxRunners; i++)
                opts.Add(new TMP_Dropdown.OptionData(i == 1 ? "1 Runner" : $"{i} Runners"));
            runnerCountDropdown.AddOptions(opts);

            runnerCountDropdown.value = Mathf.Clamp(GameSettings.LocalRunnerCount - 1, 0, MaxRunners - 1);
            runnerCountDropdown.onValueChanged.RemoveAllListeners();
            runnerCountDropdown.onValueChanged.AddListener(OnRunnerCountChanged);
        }

        // Catcher count (1–2).
        if (catcherCountDropdown != null)
        {
            catcherCountDropdown.ClearOptions();
            catcherCountDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("1 Catcher"),
                new TMP_Dropdown.OptionData("2 Catchers"),
            });

            catcherCountDropdown.value = Mathf.Clamp(GameSettings.LocalCatcherCount - 1, 0, MaxCatchers - 1);
            catcherCountDropdown.onValueChanged.RemoveAllListeners();
            catcherCountDropdown.onValueChanged.AddListener(OnCatcherCountChanged);
        }
#endif

        // Scene dropdown (host only).
        if (sceneDropdown != null)
        {
            sceneDropdown.ClearOptions();
            sceneDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("TestScene_Flat"),
                new TMP_Dropdown.OptionData("TestScene_Hill"),
            });
            sceneDropdown.onValueChanged.RemoveAllListeners();
            sceneDropdown.onValueChanged.AddListener(OnSceneSelected);
            _selectedScene = sceneDropdown.options[sceneDropdown.value].text;
        }
    }

    private void WireButtons()
    {
        if (startMatchButton != null)
        {
            startMatchButton.onClick.RemoveAllListeners();
            startMatchButton.onClick.AddListener(OnStartMatchClicked);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectClicked);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);
        }
    }

    // ====================================================================
    // Private – Dropdown Callbacks
    // ====================================================================

    private void OnRunnerCountChanged(int index)
    {
        GameSettings.LocalRunnerCount = index + 1;
        Debug.Log($"[LobbySetupPanel] LocalRunnerCount = {GameSettings.LocalRunnerCount}");
    }

    private void OnCatcherCountChanged(int index)
    {
        GameSettings.LocalCatcherCount = index + 1;
        Debug.Log($"[LobbySetupPanel] LocalCatcherCount = {GameSettings.LocalCatcherCount}");
    }

    private void OnSceneSelected(int index)
    {
        if (sceneDropdown != null && index >= 0 && index < sceneDropdown.options.Count)
            _selectedScene = sceneDropdown.options[index].text;
    }

    // ====================================================================
    // Private – Button Callbacks
    // ====================================================================

    /// <summary>
    /// Public entry point for external callers (e.g. <see cref="UIManager"/>)
    /// to trigger the Start Match flow.
    /// Equivalent to the host clicking the <see cref="startMatchButton"/>.
    /// </summary>
    public void TriggerStartMatch() => OnStartMatchClicked();

    private void OnStartMatchClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        // Store the selected scene so GameManager can reference it if needed.
        GameSettings.SelectedScene = _selectedScene;

        // Load the game scene for all connected clients via Netcode's scene manager.
        // When the game scene finishes loading, GameManager.OnNetworkSpawn
        // automatically calls SpawnAllPlayersAndStartGame().
        var status = NetworkManager.Singleton.SceneManager.LoadScene(
            _selectedScene, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
            Debug.LogError($"[LobbySetupPanel] Failed to load scene '{_selectedScene}': {status}");
    }

    private void OnDisconnectClicked()
    {
        // Disconnect shuts down Netcode and loads MenuScene.
        if (_connectionManager != null)
            _connectionManager.Disconnect(goToMainMenu: true);
        else
            SceneManager.LoadScene(NetworkConnectionManager.MenuSceneName);
    }

    private void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Private – Player Count
    // ====================================================================

    private void OnPlayerCountChanged(int previous, int current) => RefreshPlayerCount();

    private void RefreshPlayerCount()
    {
        if (playerCountText == null || _connectionManager == null) return;

        int local = GameSettings.LocalRunnerCount + GameSettings.LocalCatcherCount;
        int online = _connectionManager.totalPlayers.Value + _connectionManager.totalGuards.Value;
        int total = local + online;
        int max = NetworkConnectionManager.MAX_TOTAL_PLAYERS;

        playerCountText.text = $"Players: {total} / {max}";
    }

    // ====================================================================
    // Public – Scene to load (read by GameManager / NetworkConnectionManager)
    // ====================================================================

    /// <summary>Returns the scene name selected in <see cref="sceneDropdown"/>.</summary>
    public string GetSelectedScene() => _selectedScene;
}