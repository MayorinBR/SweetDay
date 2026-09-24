using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Scene-side bridge that holds references to the main-menu UI elements and
/// wires them to <see cref="MenuManager"/> whenever the component is enabled.
/// </summary>
public class MenuButtonHolder : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Main Menu Buttons")]
    /// <summary>Starts a local split-screen session. Wired to <see cref="MenuManager.OnLocalMultiplayerClickedPublic"/>.</summary>
    public Button localMultiplayerButton;

    /// <summary>Starts an online relay session. Wired to <see cref="MenuManager.OnOnlineMultiplayerClickedPublic"/>.</summary>
    public Button onlineMultiplayerButton;

    /// <summary>Joins an existing session by code. Wired to <see cref="MenuManager.OnJoinClickedPublic"/>.</summary>
    public Button joinButton;

    /// <summary>Quits the application. Wired to <see cref="MenuManager.QuitGame"/>.</summary>
    public Button quitButton;

    [Header("Input")]
    /// <summary>Input field for the lobby join code. Reference passed to <see cref="MenuManager.joinCodeInput"/>.</summary>
    public TMP_InputField joinCodeInput;

    [Header("Feedback")]
    /// <summary>Status label for loading/error messages. Reference passed to <see cref="MenuManager.statusText"/>.</summary>
    public TextMeshProUGUI statusText;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start() => ReconnectButtons();
    private void OnEnable() => ReconnectButtons();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Re-wires all button listeners and passes UI references to
    /// <see cref="MenuManager.Instance"/>.  Safe to call multiple times.
    /// </summary>
    public void ReconnectButtons()
    {
        if (MenuManager.Instance != null)
        {
            // Pass UI references so MenuManager can control them.
            if (joinCodeInput != null) MenuManager.Instance.joinCodeInput = joinCodeInput;
            if (statusText != null) MenuManager.Instance.statusText = statusText;

            // Wire buttons.
            if (localMultiplayerButton != null)
            {
                MenuManager.Instance.localMultiplayerButton = localMultiplayerButton;
                localMultiplayerButton.onClick.RemoveAllListeners();
                localMultiplayerButton.onClick.AddListener(MenuManager.Instance.OnLocalMultiplayerClickedPublic);
            }

            if (onlineMultiplayerButton != null)
            {
                MenuManager.Instance.onlineMultiplayerButton = onlineMultiplayerButton;
                onlineMultiplayerButton.onClick.RemoveAllListeners();
                onlineMultiplayerButton.onClick.AddListener(MenuManager.Instance.OnOnlineMultiplayerClickedPublic);
            }

            if (joinButton != null)
            {
                MenuManager.Instance.joinButton = joinButton;
                joinButton.onClick.RemoveAllListeners();
                joinButton.onClick.AddListener(MenuManager.Instance.OnJoinClickedPublic);
            }
        }

        // Quit button wired directly (does not require MenuManager).
        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);
        }
    }

    /// <summary>
    /// Scans children for buttons, input fields, and text labels by name and
    /// calls <see cref="ReconnectButtons"/>.
    /// </summary>
    public void FindButtonsAutomatically()
    {
        foreach (var btn in GetComponentsInChildren<Button>(includeInactive: true))
        {
            string n = btn.name.ToLower();
            if (n.Contains("local")) localMultiplayerButton = btn;
            else if (n.Contains("online")) onlineMultiplayerButton = btn;
            else if (n.Contains("join")) joinButton = btn;
            else if (n.Contains("quit")) quitButton = btn;
        }

        if (joinCodeInput == null)
            joinCodeInput = GetComponentInChildren<TMP_InputField>(includeInactive: true);

        foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true))
        {
            if (tmp.name.ToLower().Contains("status"))
            { statusText = tmp; break; }
        }

        ReconnectButtons();
    }

    /// <summary>
    /// Static convenience: finds the first <see cref="MenuButtonHolder"/> in the
    /// scene, auto-discovers controls, and reconnects listeners.
    /// </summary>
    public static void ReconnectAllButtonsInScene()
    {
        var holder = FindAnyObjectByType<MenuButtonHolder>();
        holder?.FindButtonsAutomatically();
        holder?.ReconnectButtons();
    }

    // ====================================================================
    // Private
    // ====================================================================

    private void OnQuitClicked()
    {
        if (MenuManager.Instance != null)
            MenuManager.Instance.QuitGame();
        else
            Application.Quit();
    }
}