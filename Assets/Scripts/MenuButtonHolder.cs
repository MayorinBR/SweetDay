using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Holds references to all main-menu UI controls and wires their callbacks to
/// <see cref="MenuManager"/> whenever the component is enabled or the scene reloads.
/// </summary>
public class MenuButtonHolder : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Button References")]
    /// <summary>Button that starts a hosted session.</summary>
    public Button hostButton;

    /// <summary>Button that joins an existing session by code.</summary>
    public Button joinButton;

    /// <summary>Button that quits the application.</summary>
    public Button quitButton;

    [Header("Dropdown References")]
    /// <summary>Dropdown for selecting the game scene (map).</summary>
    public TMP_Dropdown sceneDropdown;

    /// <summary>Dropdown for selecting Runner or Catcher role.</summary>
    public TMP_Dropdown playerTypeDropdown;

    [Header("Input References")]
    /// <summary>Input field where the player types the lobby join code.</summary>
    public TMP_InputField joinCodeInput;

    /// <summary>Dropdown for choosing how many local players share this machine.</summary>
    public TMP_Dropdown localPlayersDropdown;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start() => ReconnectButtons();
    private void OnEnable() => ReconnectButtons();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Re-wires all button and dropdown listeners to the current
    /// <see cref="MenuManager.Instance"/>.  Safe to call multiple times.
    /// </summary>
    public void ReconnectButtons()
    {
        if (MenuManager.Instance != null)
        {
            // Update the MenuManager's platform-panel reference.
#if UNITY_ANDROID || UNITY_IOS
            MenuManager.Instance.mobilePanel = transform.parent?.gameObject;
#else
            MenuManager.Instance.pcPanel = transform.parent?.gameObject;
#endif

            // Scene dropdown.
            if (sceneDropdown != null)
            {
                MenuManager.Instance.sceneDropdown = sceneDropdown;
                sceneDropdown.onValueChanged.RemoveAllListeners();
                sceneDropdown.onValueChanged.AddListener(MenuManager.Instance.OnSceneSelected);
            }

            // Player-type dropdown.
            if (playerTypeDropdown != null)
            {
                MenuManager.Instance.playerTypeDropdown = playerTypeDropdown;
                playerTypeDropdown.onValueChanged.RemoveAllListeners();
                playerTypeDropdown.onValueChanged.AddListener(MenuManager.Instance.OnPlayerTypeSelected);
            }

            // Join-code input.
            if (joinCodeInput != null)
                MenuManager.Instance.joinCodeInput = joinCodeInput;

            // Local-players dropdown.
            if (localPlayersDropdown != null)
            {
                localPlayersDropdown.onValueChanged.RemoveAllListeners();
                localPlayersDropdown.onValueChanged.AddListener(MenuManager.Instance.OnLocalPlayerCountSelected);
            }
        }

        // Host button.
        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(OnHostButtonClicked);
        }

        // Join button.
        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinButtonClicked);
        }

        // Quit button.
        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitButtonClicked);
        }
    }

    /// <summary>
    /// Scans child GameObjects for buttons, dropdowns, and input fields by name,
    /// then calls <see cref="ReconnectButtons"/> to wire them up.
    /// Useful when the hierarchy is built dynamically.
    /// </summary>
    public void FindButtonsAutomatically()
    {
        foreach (var btn in GetComponentsInChildren<Button>(includeInactive: true))
        {
            string n = btn.name.ToLower();
            if (n.Contains("host") || n.Contains("criar")) hostButton = btn;
            else if (n.Contains("join") || n.Contains("entrar")) joinButton = btn;
            else if (n.Contains("quit") || n.Contains("sair")) quitButton = btn;
        }

        foreach (var dd in GetComponentsInChildren<TMP_Dropdown>(includeInactive: true))
        {
            string n = dd.name.ToLower();
            if (n.Contains("scene") || n.Contains("cena")) sceneDropdown = dd;
            else if (n.Contains("player") || n.Contains("tipo")) playerTypeDropdown = dd;
        }

        var input = GetComponentInChildren<TMP_InputField>(includeInactive: true);
        if (input != null) joinCodeInput = input;

        ReconnectButtons();
    }

    /// <summary>
    /// Static convenience method: finds the first <see cref="MenuButtonHolder"/> in the
    /// scene, auto-discovers its controls, and reconnects all listeners.
    /// Intended to be called after a scene load by <see cref="NetworkConnectionManager"/>.
    /// </summary>
    public static void ReconnectAllButtonsInScene()
    {
        var holder = FindAnyObjectByType<MenuButtonHolder>();
        if (holder != null)
        {
            holder.FindButtonsAutomatically();
            holder.ReconnectButtons();
        }
    }

    // ====================================================================
    // Private ÅEButton Callbacks
    // ====================================================================

    private void OnHostButtonClicked()
    {
        if (MenuManager.Instance != null)
            MenuManager.Instance.StartHost();
        else
        {
            Debug.LogError("[MenuButtonHolder] MenuManager.Instance is null ÅEcannot start host.");
            FindAnyObjectByType<MenuManager>()?.StartHost();
        }
    }

    private void OnJoinButtonClicked()
        => MenuManager.Instance?.JoinGame();

    private void OnQuitButtonClicked()
        => MenuManager.Instance?.QuitGame();
}
