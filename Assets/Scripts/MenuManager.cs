using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the main-menu screen only.
/// The menu has exactly four interactive elements:
/// <list type="bullet">
///   <item><see cref="localMultiplayerButton"/>  – creates a session for local split-screen play (relay optional).</item>
///   <item><see cref="onlineMultiplayerButton"/> – creates a relay + lobby for online play.</item>
///   <item><see cref="joinCodeInput"/>           – text field for an existing lobby code.</item>
///   <item><see cref="joinButton"/>              – joins the session whose code is typed above.</item>
/// </list>
///
/// All three host paths (local, online, join) navigate to the
/// <see cref="LobbySetupPanel"/> after a connection is established.
/// Scene-selection and player-count configuration happens there, not here.
///
/// <b>Persistence:</b> this object persists across scenes via
/// <c>DontDestroyOnLoad</c>.  It must be — or become — a root GameObject;
/// <see cref="Awake"/> silently detaches from any parent before calling
/// <c>DontDestroyOnLoad</c>.
/// </summary>
public class MenuManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="MenuManager"/>.</summary>
    public static MenuManager Instance { get; private set; }

    // ====================================================================
    // Inspector – Main Menu Buttons
    // ====================================================================

    [Header("Main Menu Buttons")]
    /// <summary>Starts a local split-screen session (relay created so online guests can join later).</summary>
    public Button localMultiplayerButton;

    /// <summary>Starts an online session with Unity Relay and Lobby.</summary>
    public Button onlineMultiplayerButton;

    /// <summary>Input field for typing the lobby join code.</summary>
    public TMP_InputField joinCodeInput;

    /// <summary>Joins the session identified by the code in <see cref="joinCodeInput"/>.</summary>
    public Button joinButton;

    [Header("Feedback")]
    /// <summary>Optional status label for errors / loading messages visible on the main menu.</summary>
    public TextMeshProUGUI statusText;

    // ====================================================================
    // Constants
    // ====================================================================

    private const string ForbiddenChars = "0OIL";

    // ====================================================================
    // Private
    // ====================================================================

    private NetworkConnectionManager _connectionManager;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // DontDestroyOnLoad only works on root GameObjects.
        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _connectionManager = NetworkConnectionManager.Instance;

        if (_connectionManager == null)
        {
            var go = new GameObject("NetworkConnectionManager");
            _connectionManager = go.AddComponent<NetworkConnectionManager>();
            DontDestroyOnLoad(go);
        }

        WireButtons();
        InitialiseJoinCodeInput();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ====================================================================
    // Private – Wiring
    // ====================================================================

    private void WireButtons()
    {
        if (localMultiplayerButton != null)
        {
            localMultiplayerButton.onClick.RemoveAllListeners();
            localMultiplayerButton.onClick.AddListener(OnLocalMultiplayerClicked);
        }

        if (onlineMultiplayerButton != null)
        {
            onlineMultiplayerButton.onClick.RemoveAllListeners();
            onlineMultiplayerButton.onClick.AddListener(OnOnlineMultiplayerClicked);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinClicked);
        }
    }

    private void InitialiseJoinCodeInput()
    {
        if (joinCodeInput == null) return;
        joinCodeInput.characterLimit = 6;
        joinCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        joinCodeInput.onValidateInput += (_, __, c) => char.ToUpper(c);
    }

    // ====================================================================
    // Public – wrappers called by MenuButtonHolder after scene reloads
    // ====================================================================

    /// <summary>Public entry point for <see cref="MenuButtonHolder"/> → local multiplayer button.</summary>
    public void OnLocalMultiplayerClickedPublic() => OnLocalMultiplayerClicked();

    /// <summary>Public entry point for <see cref="MenuButtonHolder"/> → online multiplayer button.</summary>
    public void OnOnlineMultiplayerClickedPublic() => OnOnlineMultiplayerClicked();

    /// <summary>Public entry point for <see cref="MenuButtonHolder"/> → join button.</summary>
    public void OnJoinClickedPublic() => OnJoinClicked();

    // ====================================================================
    // Private – Button Callbacks
    // ====================================================================

    /// <summary>
    /// Creates a relay + lobby and navigates to the Lobby Setup screen.
    /// Uses <see cref="SessionMode.Local"/> so <see cref="LobbySetupPanel"/>
    /// knows the primary intent is split-screen.
    /// </summary>
    private void OnLocalMultiplayerClicked()
    {
        SetStatus("Creating local session\u2026");
        SetAllButtonsInteractable(false);

        // Default: 1 runner, 0 catchers.
        // User adjusts counts in LobbySetupPanel (LobbyScene).
        GameSettings.LocalRunnerCount = 1;
        GameSettings.LocalCatcherCount = 0;

        _connectionManager.StartSessionAndGoToLobby(
            SessionMode.Local,
            onSuccess: () =>
            {
                // Navigation is handled by Netcode: StartSessionAndGoToLobby
                // loads LobbyScene for all clients automatically.
                SetStatus(string.Empty);
                SetAllButtonsInteractable(true);
            },
            onFailure: msg =>
            {
                SetStatus($"Error: {msg}");
                SetAllButtonsInteractable(true);
            });
    }

    /// <summary>
    /// Creates a relay + lobby for fully online play and navigates to the
    /// Lobby Setup screen.
    /// </summary>
    private void OnOnlineMultiplayerClicked()
    {
        SetStatus("Creating online session…");
        SetAllButtonsInteractable(false);

        GameSettings.LocalRunnerCount = 1;
        GameSettings.LocalCatcherCount = 0;

        _connectionManager.StartSessionAndGoToLobby(
            SessionMode.Online,
            onSuccess: () =>
            {
                // Navigation handled by Netcode scene loading.
                SetStatus(string.Empty);
                SetAllButtonsInteractable(true);
            },
            onFailure: msg =>
            {
                SetStatus($"Error: {msg}");
                SetAllButtonsInteractable(true);
            });
    }

    /// <summary>
    /// Joins the session identified by the code in <see cref="joinCodeInput"/>
    /// and navigates to the Lobby Setup screen.
    /// </summary>
    private void OnJoinClicked()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.ToUpper().Trim() : string.Empty;

        if (!ValidateJoinCode(code)) return;

        SetStatus("Joining session…");
        SetAllButtonsInteractable(false);

        _connectionManager.JoinSessionByCode(
            code,
            onSuccess: () =>
            {
                // Netcode auto-syncs the client to the host's current scene (LobbyScene).
                SetStatus(string.Empty);
                SetAllButtonsInteractable(true);
            },
            onFailure: msg =>
            {
                SetStatus($"Error: {msg}");
                SetAllButtonsInteractable(true);
            });
    }

    // ====================================================================
    // Private – Helpers
    // ====================================================================

    private bool ValidateJoinCode(string code)
    {
        if (string.IsNullOrEmpty(code))
        { SetStatus("Lobby code cannot be empty."); return false; }

        if (code.Length != 6)
        { SetStatus("Lobby code must be exactly 6 characters."); return false; }

        foreach (char c in code)
        {
            if (!char.IsLetterOrDigit(c) || ForbiddenChars.IndexOf(c) >= 0)
            { SetStatus("Invalid character in lobby code."); return false; }
        }

        return true;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }
    }

    private void SetAllButtonsInteractable(bool value)
    {
        if (localMultiplayerButton != null) localMultiplayerButton.interactable = value;
        if (onlineMultiplayerButton != null) onlineMultiplayerButton.interactable = value;
        if (joinButton != null) joinButton.interactable = value;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MenuScene")
        {
            SetStatus(string.Empty);
            SetAllButtonsInteractable(true);
        }
    }

    // ====================================================================
    // Public – called by other systems
    // ====================================================================

    /// <summary>Quits the application.</summary>
    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}