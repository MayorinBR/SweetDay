using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Central controller for the main-menu UI and network session entry points.
/// </summary>
public class MenuManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="MenuManager"/>.</summary>
    public static MenuManager Instance { get; private set; }

    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("UI Elements")]
    /// <summary>Root panel shown on PC / Editor builds.</summary>
    public GameObject pcPanel;

    /// <summary>Root panel shown on Android / iOS builds.</summary>
    public GameObject mobilePanel;

    [Header("Menu Options")]
    /// <summary>Dropdown for choosing the game scene (map) to load.</summary>
    public TMP_Dropdown sceneDropdown;

    /// <summary>Dropdown for choosing Runner (index 0) or Catcher (index 1) role.</summary>
    public TMP_Dropdown playerTypeDropdown;

    /// <summary>Dropdown for setting how many players share this machine (1–4).</summary>
    public TMP_Dropdown localPlayersDropdown;

    [Header("Join Game")]
    /// <summary>Input field where the player types the 6-character lobby code.</summary>
    public TMP_InputField joinCodeInput;

    // ====================================================================
    // Private
    // ====================================================================

    private NetworkConnectionManager _connectionManager;
    private string _selectedScene = "TestScene_Flat";

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    void Awake()
    {
        // Ensure only one instance exists across scene loads.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        _connectionManager = NetworkConnectionManager.Instance;

        // Create a NetworkConnectionManager if one does not already exist.
        if (_connectionManager == null)
        {
            GameObject connectionManagerObj = new GameObject("NetworkConnectionManager");
            _connectionManager = connectionManagerObj.AddComponent<NetworkConnectionManager>();
            DontDestroyOnLoad(connectionManagerObj);
        }

        // ── Scene dropdown ───────────────────────────────────────────────
        if (sceneDropdown != null)
        {
            sceneDropdown.options.Clear();
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("TestScene_Flat"));
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("TestScene_Hill"));

            if (sceneDropdown.options.Count > 0)
                _selectedScene = sceneDropdown.options[sceneDropdown.value].text;

            sceneDropdown.onValueChanged.AddListener(OnSceneSelected);
        }

        // ── Player-type dropdown ─────────────────────────────────────────
        if (playerTypeDropdown != null)
        {
            playerTypeDropdown.onValueChanged.AddListener(OnPlayerTypeSelected);
            OnPlayerTypeSelected(playerTypeDropdown.value);
        }
        else
        {
            Debug.LogWarning("MenuManager: playerTypeDropdown was not assigned in the Inspector!");
        }

        // ── Join-code input ──────────────────────────────────────────────
        if (joinCodeInput != null)
        {
            joinCodeInput.onValidateInput += DelegateOnValidateInput;
            joinCodeInput.characterLimit = 6;
            joinCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        }

        // ── Local-players dropdown ───────────────────────────────────────
        if (localPlayersDropdown != null)
        {
            localPlayersDropdown.ClearOptions();

            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            for (int i = 1; i <= 4; i++)
                options.Add(new TMP_Dropdown.OptionData($"{i} Player{(i > 1 ? "s" : "")}"));

            localPlayersDropdown.AddOptions(options);
            localPlayersDropdown.onValueChanged.AddListener(OnLocalPlayerCountSelected);
            OnLocalPlayerCountSelected(localPlayersDropdown.value);
        }

        SceneManager.sceneLoaded += OnSceneLoaded;

        ShowMainMenu();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ====================================================================
    // Private – Scene Load Handler
    // ====================================================================

    /// <summary>
    /// Reacts to scene-load events.
    /// When returning to <c>MenuScene</c>, shuts down the network and restores the menu UI.
    /// When entering a game scene, shows the lobby panel (if connected) or the game HUD.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MenuScene")
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            UIManager.Instance.ShowMainMenuUI();
            SetupPlatformUI(); 
        }
        else
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
                bool isHost = NetworkManager.Singleton.IsHost;
                string lobbyCode = connectionManager != null ? connectionManager.LobbyCode : "N/A";

                UIManager.Instance.ShowLobbyUI(isHost, lobbyCode);
            }
            else
            {
                UIManager.Instance.ShowGameUI();
            }
        }
    }

    // ====================================================================
    // Public – Dropdown Callbacks
    // ====================================================================

    /// <summary>
    /// Called when the scene-select dropdown value changes.
    /// Updates <see cref="_selectedScene"/> to match the chosen option text.
    /// </summary>
    public void OnSceneSelected(int index)
    {
        if (sceneDropdown != null && index >= 0 && index < sceneDropdown.options.Count)
            _selectedScene = sceneDropdown.options[index].text;
    }

    /// <summary>
    /// Called when the player-type dropdown value changes.
    /// Index 0 = Runner, index 1 = Catcher.
    /// Forwards the choice to <see cref="NetworkConnectionManager.SetPlayerType"/>.
    /// </summary>
    public void OnPlayerTypeSelected(int index)
    {
        bool isRunner = (index == 0);
        _connectionManager.SetPlayerType(isRunner);
    }

    /// <summary>
    /// Called when the local-player-count dropdown value changes.
    /// Maps dropdown index to player count (index 0 -> 1 player, index 3 -> 4 players)
    /// and stores the result in <see cref="GameSettings.LocalPlayerCount"/>.
    /// </summary>
    public void OnLocalPlayerCountSelected(int index)
    {
        int playerCount = index + 1;
        GameSettings.LocalPlayerCount = playerCount;
        Debug.Log($"[MenuManager] Local player count set to: {GameSettings.LocalPlayerCount}");
    }

    // ====================================================================
    // Public – Scene Query
    // ====================================================================

    /// <summary>
    /// Returns the scene name currently selected in <see cref="sceneDropdown"/>.
    /// Falls back to <c>"TestScene_Flat"</c> if the dropdown is null or empty.
    /// </summary>
    public string GetSelectedScene()
    {
        if (sceneDropdown != null && sceneDropdown.options.Count > 0)
            return sceneDropdown.options[sceneDropdown.value].text;

        return "TestScene_Flat";
    }

    // ====================================================================
    // Public – Connection Actions
    // ====================================================================

    /// <summary>
    /// Reads the current dropdown selections, configures the Netcode connection payload,
    /// and delegates the host-start flow to <see cref="NetworkConnectionManager.StartHostWithScene"/>.
    /// Temporarily disables the host button to prevent accidental double-clicks.
    /// </summary>
    public async void StartHost()
    {
        if (_connectionManager != null)
        {
            SetHostButtonState(false);

            try
            {
                bool isCatcher = playerTypeDropdown.value == 1;
                _connectionManager.SetPlayerType(isCatcher);

                byte[] connectionPayload = _connectionManager.GetConnectionPayload();
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
                    NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;

                string sceneName = _selectedScene;
                _connectionManager.StartHostWithScene(sceneName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error starting host: {e}");
                SetHostButtonState(true);
            }
            finally
            {
                Invoke(nameof(ReenableHostButton), 3f);
            }
        }
    }

    /// <summary>
    /// Validates the join-code input and delegates to
    /// <see cref="NetworkConnectionManager.StartClientWithCode"/>.
    /// Rejects codes that are empty, not exactly 6 characters, or contain ambiguous characters
    /// (0, O, I, L).
    /// </summary>
    public void JoinGame()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.ToUpper().Trim() : "";

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogError("The lobby code cannot be empty.");
            return;
        }

        if (code.Length != 6)
        {
            Debug.LogError("The lobby code must be exactly 6 characters.");
            return;
        }

        foreach (char c in code)
        {
            if (!char.IsLetterOrDigit(c) || c == '0' || c == 'O' || c == 'I' || c == 'L')
            {
                Debug.LogError("Code contains invalid characters. Use letters (except O, I, L) and digits (except 0).");
                return;
            }
        }

        if (_connectionManager != null)
        {
            _connectionManager.SetPlayerType(playerTypeDropdown.value == 1);

            SetJoinButtonState(false);

            try
            {
                _connectionManager.StartClientWithCode(code);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error joining lobby: {e}");
            }
            finally
            {
                SetJoinButtonState(true);
            }
        }
    }

    /// <summary>Quits the application (also stops Play Mode in the Unity Editor).</summary>
    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Public – UI Helpers
    // ====================================================================

    /// <summary>
    /// Activates the platform-appropriate panel and ensures the
    /// <see cref="UIManager"/> GameObject is active in the current scene.
    /// </summary>
    public void ShowMainMenu()
    {
        SetupPlatformUI();
        var uiManager = FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
        if (uiManager != null)
        {
            uiManager.gameObject.SetActive(true);
        }
    }

    // ====================================================================
    // Private – Platform UI
    // ====================================================================

    /// <summary>
    /// Deactivates both panels then activates the one that matches the current
    /// platform: <see cref="pcPanel"/> on PC / Editor, <see cref="mobilePanel"/>
    /// on Android / iOS.
    /// </summary>
    private void SetupPlatformUI()
    {
        if (pcPanel != null) pcPanel.SetActive(false);
        if (mobilePanel != null) mobilePanel.SetActive(false);

#if UNITY_ANDROID || UNITY_IOS
        if (mobilePanel != null) mobilePanel.SetActive(true);
        Debug.Log("Menu: Loading Mobile interface");
#else
        if (pcPanel != null) pcPanel.SetActive(true);
        Debug.Log("Menu: Loading PC interface");
#endif
    }

    // ====================================================================
    // Private – Button State Helpers
    // ====================================================================

    /// <summary>
    /// Enables or disables the first child <see cref="Button"/> found on this GameObject
    /// and updates its label text to reflect the loading state.
    /// </summary>
    private void SetHostButtonState(bool interactable)
    {
        Button hostButton = GetComponentInChildren<Button>();
        if (hostButton != null)
        {
            hostButton.interactable = interactable;
            hostButton.GetComponentInChildren<TextMeshProUGUI>().text =
                interactable ? "Host Game" : "Loading...";
        }
    }

    /// <summary>Re-enables the host button after the <c>Invoke</c> delay.</summary>
    private void ReenableHostButton()
    {
        SetHostButtonState(true);
    }

    /// <summary>
    /// Enables or disables the <see cref="Button"/> that is a parent of
    /// <see cref="joinCodeInput"/> in the hierarchy.
    /// </summary>
    private void SetJoinButtonState(bool interactable)
    {
        Button joinButton = joinCodeInput?.GetComponentInParent<Button>();
        if (joinButton != null)
            joinButton.interactable = interactable;
    }

    // ====================================================================
    // Private – Input Validation
    // ====================================================================

    /// <summary>
    /// <see cref="TMP_InputField.OnValidateInput"/> delegate that converts every typed
    /// character to its uppercase equivalent, ensuring lobby codes are always uppercase.
    /// </summary>
    private char DelegateOnValidateInput(string text, int charIndex, char addedChar)
    {
        return char.ToUpper(addedChar);
    }
}