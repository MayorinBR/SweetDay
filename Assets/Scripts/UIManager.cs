using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static GameManager;

/// <summary>
/// Manages all in-game and lobby UI panels: HUD, lobby screen, end-game panel,
/// and cooldown displays for Dash and Attack abilities.
/// </summary>
public class UIManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="UIManager"/>.</summary>
    public static UIManager Instance { get; private set; }

    // ====================================================================
    // Inspector – HUD
    // ====================================================================

    [Header("Game HUD")]
    /// <summary>Label that displays the current score and target (e.g. "Coins: 5 / 20").</summary>
    public TextMeshProUGUI scoreText;

    /// <summary>Label that displays the remaining game time in MM:SS format.</summary>
    public TextMeshProUGUI timerText;

    /// <summary>Panel shown at the end of a match with the result.</summary>
    public GameObject endGamePanel;

    /// <summary>Sprite used to render each life icon in <see cref="livesContainer"/>.</summary>
    public Sprite lifeIconSprite;

    /// <summary>Width in pixels of each life icon image.</summary>
    public float lifeIconWidth = 50f;

    /// <summary>Parent transform under which life-icon GameObjects are instantiated.</summary>
    public Transform livesContainer;

    // ====================================================================
    // Inspector – Dash UI
    // ====================================================================

    [Header("Dash UI")]
    /// <summary>Root container for all Dash-related UI elements.</summary>
    public GameObject dashUIContainer;

    /// <summary>Icon or button shown when the dash ability is ready to use.</summary>
    public GameObject dashIconAvailable;

    /// <summary>Panel shown while the dash ability is on cooldown.</summary>
    public GameObject dashCooldownDisplay;

    /// <summary>Text label inside <see cref="dashCooldownDisplay"/> showing remaining seconds.</summary>
    public TextMeshProUGUI dashCooldownText;

    // ====================================================================
    // Inspector – Attack UI
    // ====================================================================

    [Header("Attack UI")]
    /// <summary>Root container for all Attack-related UI elements.</summary>
    public GameObject attackUIContainer;

    /// <summary>Icon or button shown when the attack ability is ready to use.</summary>
    public GameObject attackIconAvailable;

    /// <summary>Panel shown while the attack ability is on cooldown.</summary>
    public GameObject attackCooldownDisplay;

    /// <summary>Text label inside <see cref="attackCooldownDisplay"/> showing remaining seconds.</summary>
    public TextMeshProUGUI attackCooldownText;

    /// <summary>Parent container for the on-screen virtual joystick (mobile only).</summary>
    public GameObject virtualJoystickContainer;

    // ====================================================================
    // Inspector – Lobby UI
    // ====================================================================

    [Header("Lobby UI in Game Scene")]
    /// <summary>Panel shown in the game scene while players are waiting for the match to start.</summary>
    public GameObject lobbyPanel;

    /// <summary>Button that the host clicks to start the match.</summary>
    public Button startMatchButton;

    /// <summary>Button that disconnects the local client and returns to the main menu.</summary>
    public Button disconnectButton;

    /// <summary>Label displaying the current lobby join code.</summary>
    public TextMeshProUGUI lobbyCodeText;

    /// <summary>Label displaying the current connected player count (e.g. "Players: 2/6").</summary>
    public TextMeshProUGUI playerCountText;

    /// <summary>Label displaying the name of the selected game stage/map.</summary>
    public TextMeshProUGUI stageNameText;

    // ====================================================================
    // Inspector – Additional Panels
    // ====================================================================

    [Header("Additional UI Panels")]
    /// <summary>Panel shown when the local client is on the main-menu scene.</summary>
    public GameObject mainMenuPanel;

    /// <summary>Panel shown during active gameplay (score, timer, lives).</summary>
    public GameObject gameHudPanel;

    // ====================================================================
    // Private
    // ====================================================================

    private GameManager _gameManager;

    /// <summary>Tracks the current high-level UI state to avoid redundant panel toggling.</summary>
    private enum UIState { MainMenu, Lobby, Game }
    private UIState _currentState = UIState.MainMenu;

    private bool _isHost = false;
    private int _lastPlayerCount = 0;

    /// <summary>Reference to the local Runner; set by <see cref="SetLocalPlayerMovement"/>.</summary>
    private PlayerMovement _localRunner;

    /// <summary>Reference to the local Guard; set by <see cref="SetLocalCatcher"/>.</summary>
    private Guard _localCatcher;

    /// <summary>Pool of instantiated life-icon GameObjects, rebuilt by <see cref="UpdateLivesUI"/>.</summary>
    private List<GameObject> lifeIcons = new List<GameObject>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    void Start()
    {
#if UNITY_ANDROID || UNITY_IOS
        // Force landscape orientation on mobile devices.
        Screen.orientation = ScreenOrientation.LandscapeLeft;
#endif
        InitializeUI();

        Invoke(nameof(DelayedUIUpdate), 1.5f);

        SceneManager.sceneLoaded += OnSceneLoaded;

        lobbyPanel.SetActive(true);

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

        if (NetworkConnectionManager.Instance != null)
        {
            NetworkConnectionManager.Instance.totalPlayers.OnValueChanged += (prev, curr) => RefreshPlayerCounter();
            NetworkConnectionManager.Instance.totalGuards.OnValueChanged += (prev, curr) => RefreshPlayerCounter();
        }
    }

    void Update()
    {
        // Detect host-status changes and refresh the Start Match button accordingly.
        if (NetworkManager.Singleton != null)
        {
            bool currentIsHost = NetworkManager.Singleton.IsHost;
            if (currentIsHost != _isHost)
            {
                _isHost = currentIsHost;
                UpdateStartMatchButton();
            }
        }

        UpdateDashUI();
        UpdateAttackUI();
    }

    void OnEnable()
    {
        _gameManager = FindAnyObjectByType<GameManager>();
        if (_gameManager != null)
            UpdateLivesUI(_gameManager.playerLives.Value);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ====================================================================
    // Public – Player References
    // ====================================================================

    /// <summary>
    /// Stores the local <see cref="PlayerMovement"/> reference so the Dash UI
    /// can read its cooldown NetworkVariable each frame.
    /// Called by <see cref="PlayerMovement.OnNetworkSpawn"/> on the owning client.
    /// </summary>
    public void SetLocalPlayerMovement(PlayerMovement playerMovement)
    {
        _localRunner = playerMovement;
    }

    /// <summary>
    /// Stores the local <see cref="Guard"/> reference so the Attack UI
    /// can read its cooldown NetworkVariable each frame.
    /// Called by <see cref="Guard.OnNetworkSpawn"/> on the owning client.
    /// </summary>
    public void SetLocalCatcher(Guard guard)
    {
        _localCatcher = guard;
    }

    // ====================================================================
    // Private – Initialisation
    // ====================================================================

    /// <summary>
    /// Sets up the initial UI state based on the currently active scene and
    /// wires the disconnect button listener.
    /// </summary>
    private void InitializeUI()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == "MenuScene")
            ShowMainMenuUI();
        else
            ShowGameUI();

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);
        }

        UpdateLobbyCode("---");
        RefreshPlayerCounter();
    }

    /// <summary>
    /// Runs 1.5 seconds after <c>Start</c> to update the Start Match button and
    /// the lobby code label once the network has had time to initialise.
    /// </summary>
    private void DelayedUIUpdate()
    {
        UpdateStartMatchButton();

        var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
        if (connectionManager != null && !string.IsNullOrEmpty(connectionManager.LobbyCode))
            UpdateLobbyCode(connectionManager.LobbyCode);
    }

    // ====================================================================
    // Private – Player Counter
    // ====================================================================

    /// <summary>
    /// Reads the current runner and guard counts from <see cref="NetworkConnectionManager"/>
    /// and updates the player-count label.
    /// Falls back to <see cref="GameSettings.LocalPlayerCount"/> when the connection
    /// manager is not yet spawned on the network.
    /// </summary>
    private void RefreshPlayerCounter()
    {
        var conn = NetworkConnectionManager.Instance;
        if (conn != null && conn.IsSpawned)
        {
            int totalRunners = conn.totalPlayers.Value;
            int totalCatchers = conn.totalGuards.Value;
            int total = totalRunners + totalCatchers;

            Debug.Log($"RefreshPlayerCounter: runners={totalRunners}, catchers={totalCatchers}, total={total}");

            UpdatePlayerCounter(total, NetworkConnectionManager.MAX_TOTAL_PLAYERS);
        }
        else
        {
            Debug.Log("RefreshPlayerCounter: ConnectionManager not available yet.");
            UpdatePlayerCounter(GameSettings.LocalPlayerCount, NetworkConnectionManager.MAX_TOTAL_PLAYERS);
        }
    }

    // ====================================================================
    // Private – Start Match Button
    // ====================================================================

    /// <summary>
    /// Shows or hides the Start Match button depending on whether the local
    /// client is the host, and refreshes its interactable state.
    /// </summary>
    private void UpdateStartMatchButton()
    {
        if (startMatchButton == null)
        {
            Debug.LogWarning("StartMatchButton is not assigned in the Inspector!");
            return;
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        startMatchButton.gameObject.SetActive(isHost);

        if (isHost)
        {
            startMatchButton.onClick.RemoveAllListeners();
            startMatchButton.onClick.AddListener(OnStartMatchButtonClicked);

            // The host can always start the match, even when playing alone.
            startMatchButton.interactable = true;

            var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
            if (connectionManager != null && connectionManager.IsSpawned)
            {
                int totalPlayers = connectionManager.totalPlayers.Value + connectionManager.totalGuards.Value;
                if (totalPlayers != _lastPlayerCount)
                    _lastPlayerCount = totalPlayers;
            }
        }
    }

    // ====================================================================
    // Public – Panel Control
    // ====================================================================

    /// <summary>
    /// Switches to the main-menu panel layout: hides the lobby and HUD panels,
    /// shows <see cref="mainMenuPanel"/>.
    /// </summary>
    public void ShowMainMenuUI()
    {
        _currentState = UIState.MainMenu;
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (gameHudPanel != null) gameHudPanel.SetActive(false);
    }

    /// <summary>
    /// Switches to the in-lobby layout: shows <see cref="lobbyPanel"/>,
    /// hides other panels, and refreshes the lobby code and Start Match button.
    /// </summary>
    /// <param name="isHost">Whether the local client is the session host.</param>
    /// <param name="lobbyCode">The human-readable join code to display.</param>
    public void ShowLobbyUI(bool isHost, string lobbyCode)
    {
        _currentState = UIState.Lobby;

        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            Debug.Log("Lobby panel activated.");
        }
        else
        {
            Debug.LogError("lobbyPanel is null! Check the Inspector reference.");
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (gameHudPanel != null) gameHudPanel.SetActive(false);

        UpdateLobbyCode(lobbyCode);
        UpdateStartMatchButton();
    }

    /// <summary>
    /// Switches to the in-game HUD layout: shows <see cref="gameHudPanel"/>,
    /// hides the lobby and main-menu panels.
    /// </summary>
    public void ShowGameUI()
    {
        _currentState = UIState.Game;
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (gameHudPanel != null) gameHudPanel.SetActive(true);
    }

    /// <summary>
    /// Reacts to Unity scene-load events.
    /// Shows the main-menu UI when the <c>MenuScene</c> is loaded;
    /// shows the game HUD for any other scene.
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MenuScene")
            ShowMainMenuUI();
        else
            ShowGameUI();
    }

    /// <summary>
    /// Displays a transient status message inside <see cref="lobbyPanel"/> by searching
    /// its children for a <see cref="TextMeshProUGUI"/> whose name contains "Status"
    /// or "Message".
    /// </summary>
    public void ShowLoadingMessage(string message)
    {
        if (lobbyPanel != null)
        {
            TextMeshProUGUI[] texts = lobbyPanel.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var text in texts)
            {
                if (text.name.Contains("Status") || text.name.Contains("Message"))
                {
                    text.text = message;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Called by <see cref="GameManager"/> when <c>gameStarted</c> changes.
    /// Hides the lobby panel when the game starts and shows the virtual joystick.
    /// </summary>
    /// <param name="started"><c>true</c> when the match begins; <c>false</c> when returning to lobby.</param>
    public void HandleGameStart(bool started)
    {
        if (lobbyPanel != null)
            lobbyPanel.SetActive(!started);

        if (startMatchButton != null)
            startMatchButton.interactable = !started;

        if (virtualJoystickContainer != null)
            virtualJoystickContainer.SetActive(started);
    }

    // ====================================================================
    // Private – Button Callbacks
    // ====================================================================

    /// <summary>
    /// Called when the host clicks the Start Match button.
    /// Delegates to <see cref="GameManager.SpawnAllPlayersAndStartGame"/> and
    /// transitions the UI to the game HUD.
    /// </summary>
    private void OnStartMatchButtonClicked()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            if (_gameManager == null)
                _gameManager = FindAnyObjectByType<GameManager>();

            if (_gameManager != null)
            {
                _gameManager.SpawnAllPlayersAndStartGame();
                ShowGameUI();
            }
            else
            {
                Debug.LogError("GameManager not found. Cannot start the match.");
            }
        }
    }

    /// <summary>
    /// Called when the Disconnect button is clicked.
    /// Delegates to <see cref="NetworkConnectionManager.ForceCleanDisconnect"/>
    /// or falls back to loading <c>MenuScene</c> directly.
    /// </summary>
    public void OnDisconnectButtonClicked()
    {
        var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();
        if (connectionManager != null)
            connectionManager.ForceCleanDisconnect();
        else
            SceneManager.LoadScene("MenuScene");
    }

    /// <summary>
    /// Called by the Restart button on the end-game panel.
    /// Asks the server to reset the game via <see cref="GameManager.ResetGameServerRpc"/>.
    /// </summary>
    public void OnRestartGameButtonClicked()
    {
        var gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager != null && NetworkManager.Singleton.IsHost)
            gameManager.ResetGameServerRpc();
    }

    // ====================================================================
    // Public – HUD Text Updates
    // ====================================================================

    /// <summary>
    /// Updates the score label to show <paramref name="newScore"/> against the
    /// target defined in <see cref="GameManager.scoreToWin"/>.
    /// </summary>
    public void UpdateScoreText(int newScore)
    {
        if (scoreText != null)
        {
            var gameManager = FindAnyObjectByType<GameManager>();
            int targetScore = gameManager != null ? gameManager.scoreToWin : 10;
            scoreText.text = $"Coins: {newScore} / {targetScore}";
        }
    }

    /// <summary>
    /// Updates the timer label, formatting <paramref name="newTime"/> seconds
    /// as <c>MM:SS</c>.
    /// </summary>
    public void UpdateTimerText(float newTime)
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(newTime / 60f);
            int seconds = Mathf.FloorToInt(newTime - minutes * 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    /// <summary>Updates the lobby-code label to display <paramref name="code"/>.</summary>
    public void UpdateLobbyCode(string code)
    {
        if (lobbyCodeText != null)
            lobbyCodeText.text = $"Lobby: {code}";
    }

    /// <summary>
    /// Updates the player-count label and keeps the Start Match button interactable
    /// for the host regardless of the current count.
    /// </summary>
    /// <param name="current">Number of currently connected players.</param>
    /// <param name="max">Maximum allowed players in the session.</param>
    public void UpdatePlayerCounter(int current, int max)
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {current}/{max}";
            Debug.Log($"UpdatePlayerCounter called: current={current}, max={max}");
        }

        if (startMatchButton != null
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsHost)
        {
            startMatchButton.interactable = true;

            if (current != _lastPlayerCount)
                _lastPlayerCount = current;
        }
    }

    /// <summary>
    /// Destroys all existing life-icon GameObjects and instantiates a new row of
    /// <paramref name="currentLives"/> icons inside <see cref="livesContainer"/>.
    /// </summary>
    public void UpdateLivesUI(int currentLives)
    {
        foreach (var icon in lifeIcons)
            Destroy(icon);
        lifeIcons.Clear();

        if (livesContainer == null || lifeIconSprite == null) return;

        for (int i = 0; i < currentLives; i++)
        {
            GameObject newIconGO = new GameObject("LifeIcon", typeof(Image));
            newIconGO.transform.SetParent(livesContainer, false);

            Image iconImage = newIconGO.GetComponent<Image>();
            iconImage.sprite = lifeIconSprite;

            RectTransform iconRectTransform = newIconGO.GetComponent<RectTransform>();
            if (iconRectTransform != null)
            {
                iconRectTransform.sizeDelta = new Vector2(lifeIconWidth, lifeIconWidth);
                float xPos = i * (lifeIconWidth + 10);
                iconRectTransform.pivot = new Vector2(0.5f, 0.5f);
                iconRectTransform.anchorMin = new Vector2(0f, 0.5f);
                iconRectTransform.anchorMax = new Vector2(0f, 0.5f);
                iconRectTransform.anchoredPosition = new Vector2(xPos + lifeIconWidth / 2, 0);
            }

            lifeIcons.Add(newIconGO);
        }
    }

    // ====================================================================
    // Private – Cooldown UI
    // ====================================================================

    /// <summary>
    /// Updates the Dash cooldown UI every frame by reading
    /// <see cref="PlayerMovement.DashCooldownRemaining"/> from the local runner.
    /// Lazily locates the local runner on the first call if the reference is null.
    /// Hides the container entirely when no local runner exists or the character
    /// is not in the Runner role.
    /// </summary>
    private void UpdateDashUI()
    {
        if (_localRunner == null)
        {
            PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include);

            foreach (PlayerMovement player in allPlayers)
            {
                if (player.IsOwner && player.IsRunner.Value)
                {
                    _localRunner = player;
                    break;
                }
            }

            if (_localRunner == null)
            {
                if (dashUIContainer != null) dashUIContainer.SetActive(false);
                return;
            }
        }

        if (!_localRunner.IsRunner.Value)
        {
            if (dashUIContainer != null) dashUIContainer.SetActive(false);
            return;
        }

        if (dashUIContainer != null) dashUIContainer.SetActive(true);

        float cooldown = _localRunner.DashCooldownRemaining.Value;

        if (cooldown <= 0f)
        {
            if (dashIconAvailable != null) dashIconAvailable.SetActive(true);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(false);
        }
        else
        {
            if (dashIconAvailable != null) dashIconAvailable.SetActive(false);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(true);
            if (dashCooldownText != null) dashCooldownText.text = cooldown.ToString("F1");
        }
    }

    /// <summary>
    /// Updates the Attack cooldown UI every frame by reading
    /// <see cref="Guard.AttackCooldownRemaining"/> from the local guard.
    /// Lazily locates the local guard on the first call if the reference is null.
    /// Hides the container entirely when no local guard exists.
    /// </summary>
    private void UpdateAttackUI()
    {
        // Se não temos uma referência ao guard local
        if (_localCatcher == null)
        {
            // Tenta encontrar o guard local (Owner)
            Guard[] allGuards = FindObjectsByType<Guard>(FindObjectsInactive.Include);
            foreach (Guard guard in allGuards)
            {
                if (guard.IsOwner)
                {
                    _localCatcher = guard;
                    break;
                }
            }

            if (_localCatcher == null)
            {
                // Se ainda não encontrou, desativa a UI
                if (attackUIContainer != null) attackUIContainer.SetActive(false);
                return;
            }
        }

        if (attackUIContainer != null) attackUIContainer.SetActive(true);

        float cooldown = _localCatcher.AttackCooldownRemaining.Value;

        if (cooldown <= 0f)
        {
            if (attackIconAvailable != null) attackIconAvailable.SetActive(true);
            if (attackCooldownDisplay != null) attackCooldownDisplay.SetActive(false);
        }
        else
        {
            if (attackIconAvailable != null) attackIconAvailable.SetActive(false);
            if (attackCooldownDisplay != null) attackCooldownDisplay.SetActive(true);
            if (attackCooldownText != null) attackCooldownText.text = cooldown.ToString("F1");
        }
    }

    // ====================================================================
    // Public – End-Game Panel
    // ====================================================================

    /// <summary>
    /// Populates and shows the end-game panel using a rich <see cref="EndGameResult"/>
    /// struct, setting the victory/defeat text, description, and background colour.
    /// </summary>
    /// <param name="result">Struct containing win/loss state and team name strings.</param>
    public void ShowEndGamePanel(EndGameResult result)
    {
        if (endGamePanel == null) return;

        endGamePanel.SetActive(true);

        Text victoryText = endGamePanel.transform.Find("VictoryText")?.GetComponent<Text>();
        Text messageText = endGamePanel.transform.Find("MessageText")?.GetComponent<Text>();
        Text descriptionText = endGamePanel.transform.Find("DescriptionText")?.GetComponent<Text>();

        if (victoryText != null)
        {
            victoryText.text = result.didLocalPlayerWin ? "VICTORY!" : "DEFEAT!";
            victoryText.color = result.didLocalPlayerWin ? Color.green : Color.red;
        }

        if (messageText != null)
            messageText.text = result.message;

        if (descriptionText != null)
        {
            descriptionText.text = result.didLocalPlayerWin
                ? $"Congratulations! The {result.winningTeam} have won!"
                : $"Better luck next time! The {result.losingTeam} have lost!";
        }

        Image backgroundImage = endGamePanel.GetComponent<Image>();
        if (backgroundImage != null)
        {
            backgroundImage.color = result.didLocalPlayerWin
                ? new Color(0.1f, 0.5f, 0.1f, 0.8f)   // Dark green – victory.
                : new Color(0.5f, 0.1f, 0.1f, 0.8f);  // Dark red   – defeat.
        }
    }

    /// <summary>
    /// Activates the end-game panel and displays a plain string <paramref name="message"/>.
    /// Uses <see cref="TextMeshProUGUI"/> if available, falling back to legacy <see cref="Text"/>.
    /// Colours the text green when the message contains "Victory", red otherwise.
    /// </summary>
    public void ShowSimpleEndGame(string message)
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);

            TextMeshProUGUI textMesh = endGamePanel.GetComponentInChildren<TextMeshProUGUI>();
            if (textMesh != null)
            {
                textMesh.text = message;
                textMesh.color = message.Contains("Victory") ? Color.green : Color.red;
            }
            else
            {
                Text legacyText = endGamePanel.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = message;
                    legacyText.color = message.Contains("Victory") ? Color.green : Color.red;
                }
            }
        }
    }

    /// <summary>Hides the end-game panel.</summary>
    public void HideEndGamePanel()
    {
        if (endGamePanel != null)
            endGamePanel.SetActive(false);
    }

    // ====================================================================
    // Public – Quit
    // ====================================================================

    /// <summary>
    /// Shuts down the network connection (if active) then quits the application.
    /// In the Unity Editor, stops Play Mode instead.
    /// </summary>
    public void QuitGame()
    {
        var connectionManager = FindAnyObjectByType<NetworkConnectionManager>();

        if (connectionManager != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            connectionManager.Disconnect(false);
        }

        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}