using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Manages all lobby and navigation UI: panel switching between menu / lobby setup /
/// game scenes, lobby code display, player count, and session action buttons
/// (Start Match, Disconnect, Quit).
/// </summary>
public class LobbyManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="LobbyManager"/>.</summary>
    public static LobbyManager Instance { get; private set; }

    // ====================================================================
    // Inspector – Navigation Panels
    // ====================================================================

    [Header("Navigation Panels")]
    /// <summary>Root panel shown on the main-menu scene.</summary>
    public GameObject mainMenuPanel;

    /// <summary>
    /// Panel shown after a session is created/joined but before the match starts.
    /// Contains runner/catcher dropdowns, lobby code, and action buttons.
    /// Managed by <see cref="LobbySetupPanel"/>.
    /// </summary>
    public GameObject lobbySetupPanel;

    // ====================================================================
    // Inspector – Lobby Info Labels
    // ====================================================================

    [Header("Lobby Info")]
    /// <summary>Displays the current lobby join code.</summary>
    public TextMeshProUGUI lobbyCodeText;

    /// <summary>Displays the connected player count, e.g. "Players: 2 / 6".</summary>
    public TextMeshProUGUI playerCountText;

    /// <summary>Displays the selected stage/map name.</summary>
    public TextMeshProUGUI stageNameText;

    // ====================================================================
    // Inspector – Action Buttons
    // ====================================================================

    [Header("Action Buttons")]
    /// <summary>Visible and interactable for the host only. Starts the match.</summary>
    public Button startMatchButton;

    /// <summary>Disconnects the local client and returns to the main menu.</summary>
    public Button disconnectButton;

    // ====================================================================
    // Private
    // ====================================================================

    private bool _isHost;
    private int _lastPlayerCount;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        InitialiseButtons();

        SceneManager.sceneLoaded += OnSceneLoaded;

        if (NetworkConnectionManager.Instance != null)
        {
            NetworkConnectionManager.Instance.totalPlayers.OnValueChanged += (_, __) => RefreshPlayerCounter();
            NetworkConnectionManager.Instance.totalGuards.OnValueChanged += (_, __) => RefreshPlayerCounter();
        }

        // Deferred label update: network may not be ready at Start time.
        Invoke(nameof(DelayedLabelUpdate), 1.5f);
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null) return;
        bool isHost = NetworkManager.Singleton.IsHost;
        if (isHost == _isHost) return;
        _isHost = isHost;
        UpdateStartMatchButton();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ====================================================================
    // Public – Panel Navigation
    // ====================================================================

    /// <summary>Shows the main-menu panel; hides all other managed panels.</summary>
    public void ShowMainMenuUI()
    {
        if (lobbySetupPanel != null) lobbySetupPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    /// <summary>
    /// Shows the Lobby Setup panel. Hides main-menu and legacy lobby panels.
    /// </summary>
    public void ShowLobbySetupUI()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        if (lobbySetupPanel != null)
        {
            lobbySetupPanel.SetActive(true);
            lobbySetupPanel.GetComponent<LobbySetupPanel>()?.Refresh();
        }
        else
        {
            Debug.LogError("[LobbyManager] lobbySetupPanel is not assigned in the Inspector.");
        }
    }

    /// <summary>
    /// Shows the lobby setup panel.  Kept for backwards compatibility with
    /// callers that pass <paramref name="isHost"/> and <paramref name="lobbyCode"/>;
    /// both are applied automatically inside <see cref="ShowLobbySetupUI"/>.
    /// </summary>
    public void ShowLobbyUI(bool isHost, string lobbyCode)
    {
        ShowLobbySetupUI();
    }

    /// <summary>Hides all lobby panels (called when the game scene becomes active).</summary>
    public void ShowGameUI()
    {
        if (lobbySetupPanel != null) lobbySetupPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
    }

    /// <summary>
    /// Called when <see cref="GameManager.gameStarted"/> changes.
    /// Hides the lobby panel and disables the Start Match button when the match begins.
    /// </summary>
    public void HandleGameStart(bool started)
    {
        if (startMatchButton != null)
            startMatchButton.interactable = !started;
    }

    // ====================================================================
    // Public – Label Updates
    // ====================================================================

    /// <summary>Updates the lobby-code label.</summary>
    public void UpdateLobbyCode(string code)
    {
        if (lobbyCodeText != null)
            lobbyCodeText.text = $"Lobby: {code}";
    }

    /// <summary>Updates the player-count label and keeps Start Match interactable for the host.</summary>
    public void UpdatePlayerCounter(int current, int max)
    {
        if (playerCountText != null)
            playerCountText.text = $"Players: {current}/{max}";

        if (startMatchButton != null
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsHost)
        {
            startMatchButton.interactable = true;
            _lastPlayerCount = current;
        }
    }

    /// <summary>Displays a transient loading/status message inside the lobby panel.</summary>
    public void ShowLoadingMessage(string message)
    {
        if (lobbySetupPanel == null) return;
        foreach (var t in lobbySetupPanel.GetComponentsInChildren<TextMeshProUGUI>())
        {
            if (t.name.Contains("Status") || t.name.Contains("Message"))
            { t.text = message; break; }
        }
    }

    // ====================================================================
    // Public – Action Buttons
    // ====================================================================

    /// <summary>Called when the Disconnect button is clicked.</summary>
    public void OnDisconnectButtonClicked()
    {
        var cm = FindAnyObjectByType<NetworkConnectionManager>();
        if (cm != null) cm.ForceCleanDisconnect();
        else SceneManager.LoadScene(NetworkConnectionManager.MenuSceneName);
    }

    /// <summary>Called by the Restart button on the end-game panel.</summary>
    public void OnRestartGameButtonClicked()
    {
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null && NetworkManager.Singleton.IsHost)
            gm.ResetGameServerRpc();
    }

    /// <summary>Quits the application.</summary>
    public void QuitGame()
    {
        var cm = FindAnyObjectByType<NetworkConnectionManager>();
        if (cm != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            cm.Disconnect(false);

        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Private – Initialisation
    // ====================================================================

    private void InitialiseButtons()
    {
        if (startMatchButton != null)
        {
            startMatchButton.onClick.RemoveAllListeners();
            startMatchButton.onClick.AddListener(OnStartMatchButtonClicked);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);
        }

        UpdateLobbyCode("---");
        RefreshPlayerCounter();
        UpdateStartMatchButton();
    }

    private void DelayedLabelUpdate()
    {
        UpdateStartMatchButton();
        var cm = FindAnyObjectByType<NetworkConnectionManager>();
        if (cm != null && !string.IsNullOrEmpty(cm.LobbyCode))
            UpdateLobbyCode(cm.LobbyCode);
    }

    // ====================================================================
    // Private – Start Match Button
    // ====================================================================

    private void UpdateStartMatchButton()
    {
        if (startMatchButton == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        startMatchButton.gameObject.SetActive(isHost);

        if (!isHost) return;

        startMatchButton.onClick.RemoveAllListeners();
        startMatchButton.onClick.AddListener(OnStartMatchButtonClicked);
        startMatchButton.interactable = true;
    }

    private void OnStartMatchButtonClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;

        var lobbySetup = FindAnyObjectByType<LobbySetupPanel>();
        if (lobbySetup != null) { lobbySetup.TriggerStartMatch(); return; }

        Debug.LogError("[LobbyManager] LobbySetupPanel not found. Cannot start match.");
    }

    // ====================================================================
    // Private – Player Counter
    // ====================================================================

    private void RefreshPlayerCounter()
    {
        var conn = NetworkConnectionManager.Instance;
        if (conn != null && conn.IsSpawned)
        {
            int total = conn.totalPlayers.Value + conn.totalGuards.Value;
            UpdatePlayerCounter(total, NetworkConnectionManager.MAX_TOTAL_PLAYERS);
        }
        else
        {
            UpdatePlayerCounter(GameSettings.LocalPlayerCount, NetworkConnectionManager.MAX_TOTAL_PLAYERS);
        }
    }

    // ====================================================================
    // Private – Scene Load Handler
    // ====================================================================

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == NetworkConnectionManager.MenuSceneName)
            ShowMainMenuUI();
    }
}