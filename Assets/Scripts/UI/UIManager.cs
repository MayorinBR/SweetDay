using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using static GameManager;

/// <summary>
/// Manages the in-game HUD: score, timer, lives, Dash and Attack cooldown
/// displays, the end-game panel, and the mobile virtual joystick toggle.
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
    /// <summary>Label showing the current score vs target, e.g. "Coins: 5 / 20".</summary>
    public TextMeshProUGUI scoreText;

    /// <summary>Label showing remaining time in MM:SS format.</summary>
    public TextMeshProUGUI timerText;

    /// <summary>Panel shown at the end of a match.</summary>
    public GameObject endGamePanel;

    [Header("End Game Buttons")]
    /// <summary>Restart button inside the end-game panel. Host-only.</summary>
    public UnityEngine.UI.Button endGameRestartButton;

    /// <summary>Back-to-menu button inside the end-game panel. Visible to all.</summary>
    public UnityEngine.UI.Button endGameMenuButton;

    /// <summary>Sprite used for each life icon.</summary>
    public Sprite lifeIconSprite;

    /// <summary>Width in pixels of each life icon.</summary>
    public float lifeIconWidth = 50f;

    /// <summary>Parent transform where life-icon GameObjects are instantiated.</summary>
    public Transform livesContainer;

    [Header("Game HUD Panel")]
    /// <summary>
    /// HUD panel rendered on Display 1 (Runners / single-screen fallback).
    /// Its <c>Canvas.targetDisplay</c> is set automatically in
    /// <see cref="ShowGameUI"/>.
    /// </summary>
    public GameObject gameHudPanel;

    /// <summary>
    /// Optional second HUD panel rendered on Display 2 (Catchers).
    /// Only needed for local split-screen setups where runners and catchers
    /// share the same machine.  Leave empty for online-only play.
    /// </summary>
    public GameObject gameHudPanelScreen2;

    // ====================================================================
    // Inspector – Dash UI
    // ====================================================================

    [Header("Dash UI")]
    /// <summary>Root container for Dash UI elements.</summary>
    public GameObject dashUIContainer;

    /// <summary>Icon shown when the dash is ready.</summary>
    public GameObject dashIconAvailable;

    /// <summary>Panel shown while the dash is on cooldown.</summary>
    public GameObject dashCooldownDisplay;

    /// <summary>Label showing remaining dash cooldown in seconds.</summary>
    public TextMeshProUGUI dashCooldownText;

    // ====================================================================
    // Inspector – Attack UI
    // ====================================================================

    [Header("Attack UI")]
    /// <summary>Root container for Attack UI elements.</summary>
    public GameObject attackUIContainer;

    /// <summary>Icon shown when the attack is ready.</summary>
    public GameObject attackIconAvailable;

    /// <summary>Panel shown while the attack is on cooldown.</summary>
    public GameObject attackCooldownDisplay;

    /// <summary>Label showing remaining attack cooldown in seconds.</summary>
    public TextMeshProUGUI attackCooldownText;

    [Header("Mobile")]
    /// <summary>Parent container for the on-screen virtual joystick (mobile only).</summary>
    public GameObject virtualJoystickContainer;

    [Header("Session Info")]
    /// <summary>
    /// Displays the lobby join code inside the game scene so players can share
    /// it with latecomers.  Populated by <see cref="ShowGameUI"/>.
    /// </summary>
    public TextMeshProUGUI lobbyCodeText;

    [Header("Countdown")]
    /// <summary>Fullscreen overlay shown during the pre-game countdown.</summary>
    public GameObject countdownPanel;

    /// <summary>Label that shows "3", "2", "1", or "Go!".</summary>
    public TextMeshProUGUI countdownText;

    [Header("Pause Menu")]
    /// <summary>Root panel for the pause menu.</summary>
    public GameObject pausePanel;

    /// <summary>
    /// Optional on-screen pause button for mobile / touch devices.
    /// Assign in the Inspector; no binding needed in the Input Action Asset.
    /// </summary>
    public UnityEngine.UI.Button pauseButton;

    /// <summary>Restart button — only visible for the host.</summary>
    public UnityEngine.UI.Button restartButton;

    [Header("Disconnect Message")]
    /// <summary>Panel shown briefly when another player leaves mid-game.</summary>
    public GameObject playerLeftPanel;

    /// <summary>Text inside <see cref="playerLeftPanel"/>.</summary>
    public TextMeshProUGUI playerLeftText;

    // ====================================================================
    // Private
    // ====================================================================

    private PlayerMovement _localRunner;
    private Guard _localCatcher;
    private bool _isPaused;
    private GameManager _gm;

    private readonly List<GameObject> _lifeIcons = new List<GameObject>();

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
#if UNITY_ANDROID || UNITY_IOS
        Screen.orientation = ScreenOrientation.LandscapeLeft;
#endif
        // Start with HUD hidden; shown when the game actually starts.
        if (gameHudPanel != null) gameHudPanel.SetActive(false);
        if (gameHudPanelScreen2 != null) gameHudPanelScreen2.SetActive(false);
        if (virtualJoystickContainer != null) virtualJoystickContainer.SetActive(false);
        EnsureOverlayCanvas(countdownPanel, sortingOrder: 200);
        EnsureOverlayCanvas(playerLeftPanel, sortingOrder: 150);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(OnPauseButtonPressed);

        if (endGameRestartButton != null)
            endGameRestartButton.onClick.AddListener(OnEndGameRestartClicked);

        if (endGameMenuButton != null)
            endGameMenuButton.onClick.AddListener(OnEndGameMenuClicked);
        if (countdownPanel != null) countdownPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        if (playerLeftPanel != null) playerLeftPanel.SetActive(false);
    }

    private void OnEnable()
    {
        var gm = FindAnyObjectByType<GameManager>();
        if (gm != null) UpdateLivesUI(gm.playerLives.Value);
    }

    private void Update()
    {
        UpdateDashUI();
        UpdateAttackUI();
        CheckPauseInput();
    }

    // ====================================================================
    // Public – Player References
    // ====================================================================

    /// <summary>
    /// Stores the local runner reference so the Dash UI can read its cooldown.
    /// Called by <see cref="PlayerMovement.OnNetworkSpawn"/> on the owning client.
    /// </summary>
    public void SetLocalPlayerMovement(PlayerMovement pm) => _localRunner = pm;

    /// <summary>
    /// Stores the local guard reference so the Attack UI can read its cooldown.
    /// Called by <see cref="Guard.OnNetworkSpawn"/> on the owning client.
    /// </summary>
    public void SetLocalCatcher(Guard guard) => _localCatcher = guard;

    // ====================================================================
    // Public – Panel Control
    // ====================================================================

    /// <summary>
    /// Shows the game HUD panel(s) and routes each to the correct physical
    /// display.
    ///
    /// <b>Display routing:</b>
    /// <list type="bullet">
    ///   <item><see cref="gameHudPanel"/> → Display 1 when the local player is a
    ///         Runner, Display 2 when the local player is a Catcher and no
    ///         <see cref="gameHudPanelScreen2"/> is assigned.</item>
    ///   <item><see cref="gameHudPanelScreen2"/> → Display 2 (for local
    ///         split-screen machines that have both Runners on Screen 1 and
    ///         Catchers on Screen 2).</item>
    /// </list>
    ///
    /// Also initialises all HUD labels with current server values so join
    /// clients see correct data immediately.
    /// </summary>
    public void ShowGameUI()
    {
        bool localIsCatcher = IsLocalPlayerCatcher();

        // ── Main HUD panel ────────────────────────────────────────────
        if (gameHudPanel != null)
        {
            // Determine display target:
            // – Dual-screen local: Screen2 panel handles catchers, main stays on Display 1.
            // – Online / single-screen: route based on local role.
            int mainDisplay = (gameHudPanelScreen2 != null || !localIsCatcher)
                ? MultiScreenManager.RunnerDisplayIndex
                : MultiScreenManager.CatcherDisplayIndex;

            RouteCanvasToDisplay(gameHudPanel, mainDisplay);
            gameHudPanel.SetActive(true);
        }

        // ── Screen 2 HUD panel (local dual-screen only) ───────────────
        if (gameHudPanelScreen2 != null)
        {
            RouteCanvasToDisplay(gameHudPanelScreen2, MultiScreenManager.CatcherDisplayIndex);
            gameHudPanelScreen2.SetActive(true);
        }

        // ── Hide any surviving lobby UI ───────────────────────────────
        var lobby = FindAnyObjectByType<InteractiveLobbyPanel>(FindObjectsInactive.Include);
        if (lobby != null) lobby.gameObject.SetActive(false);

        // ── Initialise HUD labels from current server values ──────────
        // OnValueChanged only fires on changes; join clients need current values.
        _gm = FindAnyObjectByType<GameManager>();
        if (_gm != null)
        {
            UpdateScoreText(_gm.score.Value);
            UpdateTimerText(_gm.gameTimer.Value);
            UpdateLivesUI(_gm.playerLives.Value);
        }

        // ── Lobby code ────────────────────────────────────────────────
        if (lobbyCodeText != null)
        {
            var cm = NetworkConnectionManager.Instance;
            string code = cm != null ? cm.LobbyCode : string.Empty;
            lobbyCodeText.text = string.IsNullOrEmpty(code) ? "Local" : $"Code: {code}";
        }
    }

    /// <summary>
    /// Called on <b>every client</b> when <see cref="GameManager.gameStarted"/>
    /// changes.  Shows the HUD and enables the virtual joystick.
    /// This is the primary path that ensures join clients see their UI.
    /// </summary>
    public void HandleGameStart(bool started)
    {
        if (started) ShowGameUI();

        if (virtualJoystickContainer != null)
            virtualJoystickContainer.SetActive(started);
    }

    // ====================================================================
    // Public – HUD Updates
    // ====================================================================

    /// <summary>Updates the score label (e.g. "Coins: 5 / 20").</summary>
    public void UpdateScoreText(int newScore)
    {
        if (scoreText == null) return;
        var gm = FindAnyObjectByType<GameManager>();
        int target = gm != null ? gm.scoreToWin : 10;
        scoreText.text = $"Coins: {newScore} / {target}";
    }

    /// <summary>Updates the timer label in MM:SS format.</summary>
    public void UpdateTimerText(float newTime)
    {
        if (timerText == null) return;
        int minutes = Mathf.FloorToInt(newTime / 60f);
        int seconds = Mathf.FloorToInt(newTime % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    /// <summary>Rebuilds the life-icon row to show <paramref name="currentLives"/> icons.</summary>
    public void UpdateLivesUI(int currentLives)
    {
        foreach (var icon in _lifeIcons) Destroy(icon);
        _lifeIcons.Clear();

        if (livesContainer == null || lifeIconSprite == null) return;

        for (int i = 0; i < currentLives; i++)
        {
            var go = new GameObject("LifeIcon", typeof(Image));
            go.transform.SetParent(livesContainer, false);
            go.GetComponent<Image>().sprite = lifeIconSprite;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(lifeIconWidth, lifeIconWidth);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(i * (lifeIconWidth + 10f) + lifeIconWidth * 0.5f, 0f);

            _lifeIcons.Add(go);
        }
    }

    // ====================================================================
    // Public – End-Game Panel
    // ====================================================================

    /// <summary>Shows the end-game panel with a rich result struct.</summary>
    public void ShowEndGamePanel(EndGameResult result)
    {
        if (endGamePanel == null) return;
        endGamePanel.SetActive(true);

        var victoryText = endGamePanel.transform.Find("VictoryText")?.GetComponent<Text>();
        var messageText = endGamePanel.transform.Find("MessageText")?.GetComponent<Text>();
        var descriptionText = endGamePanel.transform.Find("DescriptionText")?.GetComponent<Text>();

        if (victoryText != null)
        {
            victoryText.text = result.didLocalPlayerWin ? "VICTORY!" : "DEFEAT!";
            victoryText.color = result.didLocalPlayerWin ? Color.green : Color.red;
        }
        if (messageText != null) messageText.text = result.message;
        if (descriptionText != null)
            descriptionText.text = result.didLocalPlayerWin
                ? $"Congratulations! The {result.winningTeam} have won!"
                : $"Better luck next time! The {result.losingTeam} have lost!";

        var bg = endGamePanel.GetComponent<Image>();
        if (bg != null)
            bg.color = result.didLocalPlayerWin
                ? new Color(0.1f, 0.5f, 0.1f, 0.8f)
                : new Color(0.5f, 0.1f, 0.1f, 0.8f);

        RefreshEndGameButtons();
    }

    /// <summary>Shows the end-game panel with a plain message string.</summary>
    public void ShowSimpleEndGame(string message)
    {
        if (endGamePanel == null) return;
        endGamePanel.SetActive(true);

        var tmp = endGamePanel.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
        {
            tmp.text = message;
            tmp.color = message.Contains("Victory") ? Color.green : Color.red;
            return;
        }

        var legacy = endGamePanel.GetComponentInChildren<Text>();
        if (legacy != null)
        {
            legacy.text = message;
            legacy.color = message.Contains("Victory") ? Color.green : Color.red;
        }

        RefreshEndGameButtons();
    }

    /// <summary>Refreshes the end-game panel buttons after <see cref="ShowEndGamePanel"/> or <see cref="ShowSimpleEndGame"/>.</summary>
    private void RefreshEndGameButtons()
    {
        bool isHost = Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false;
        if (endGameRestartButton != null)
            endGameRestartButton.gameObject.SetActive(isHost);
        if (endGameMenuButton != null)
            endGameMenuButton.gameObject.SetActive(true);
    }

    /// <summary>Hides the end-game panel.</summary>
    public void HideEndGamePanel()
    {
        if (endGamePanel != null) endGamePanel.SetActive(false);
    }

    /// <summary>Quits the application.</summary>
    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Private – Cooldown UI
    // ====================================================================

    private void UpdateDashUI()
    {
        if (_localRunner == null)
        {
            foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include))
            {
                if (pm.IsOwner && pm.IsRunner.Value) { _localRunner = pm; break; }
            }

            if (_localRunner == null)
            { if (dashUIContainer != null) dashUIContainer.SetActive(false); return; }
        }

        if (!_localRunner.IsRunner.Value)
        { if (dashUIContainer != null) dashUIContainer.SetActive(false); return; }

        if (dashUIContainer != null) dashUIContainer.SetActive(true);

        float cd = _localRunner.DashCooldownRemaining.Value;
        bool ready = cd <= 0f;
        if (dashIconAvailable != null) dashIconAvailable.SetActive(ready);
        if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(!ready);
        if (!ready && dashCooldownText != null) dashCooldownText.text = cd.ToString("F1");
    }

    private void UpdateAttackUI()
    {
        if (_localCatcher == null)
        {
            foreach (var g in FindObjectsByType<Guard>(FindObjectsInactive.Include))
            {
                if (g.IsOwner) { _localCatcher = g; break; }
            }

            if (_localCatcher == null)
            { if (attackUIContainer != null) attackUIContainer.SetActive(false); return; }
        }

        if (attackUIContainer != null) attackUIContainer.SetActive(true);

        float cd = _localCatcher.AttackCooldownRemaining.Value;
        bool ready = cd <= 0f;
        if (attackIconAvailable != null) attackIconAvailable.SetActive(ready);
        if (attackCooldownDisplay != null) attackCooldownDisplay.SetActive(!ready);
        if (!ready && attackCooldownText != null) attackCooldownText.text = cd.ToString("F1");
    }

    // ====================================================================
    // Private – Display Helpers
    // ====================================================================

    /// <summary>
    /// Sets <paramref name="panel"/>'s root <see cref="Canvas"/>
    /// <c>targetDisplay</c> to <paramref name="displayIndex"/> so it renders
    /// on the correct physical screen.
    /// </summary>
    private static void RouteCanvasToDisplay(GameObject panel, int displayIndex)
    {
        if (panel == null) return;
        var canvas = panel.GetComponent<Canvas>();
        if (canvas == null) canvas = panel.GetComponentInParent<Canvas>();
        if (canvas != null) canvas.targetDisplay = displayIndex;
    }

    /// <summary>
    /// Returns <c>true</c> when the local client's slot assignment in
    /// <see cref="GameSettings.ClientSlotAssignments"/> indicates a Catcher role.
    /// Falls back to <see cref="GameSettings.IsCatcher"/> if no slot data exists.
    /// </summary>
    private static bool IsLocalPlayerCatcher()
    {
        if (NetworkManager.Singleton == null) return GameSettings.IsCatcher;

        ulong localId = NetworkManager.Singleton.LocalClientId;

        if (GameSettings.ClientSlotAssignments.TryGetValue(localId, out int slotIdx))
            return slotIdx >= LobbyStateManager.RunnerSlotCount;

        return GameSettings.IsCatcher;
    }
    // ====================================================================
    // Countdown
    // ====================================================================

    /// <summary>
    /// Displays the 3-2-1-Go sequence on top of the game scene.
    /// Called on every client via <see cref="GameManager.StartCountdownClientRpc"/>.
    /// </summary>
    public void ShowCountdown() => StartCoroutine(CountdownCoroutine());

    private System.Collections.IEnumerator CountdownCoroutine()
    {
        if (countdownPanel != null) countdownPanel.SetActive(true);

        for (int i = 3; i >= 1; i--)
        {
            if (countdownText != null) countdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        if (countdownText != null) countdownText.text = "Go!";
        yield return new WaitForSeconds(0.7f);

        if (countdownPanel != null) countdownPanel.SetActive(false);
    }

    // ====================================================================
    // Pause Menu
    // ====================================================================

    /// <summary>
    /// Called by <see cref="GameManager.OnIsPausedChanged"/> when the networked
    /// pause state changes.  Shows or hides the pause panel accordingly.
    /// </summary>
    public void OnPauseStateChanged(bool paused)
    {
        _isPaused = paused;

        if (pausePanel != null)
        {
            pausePanel.SetActive(paused);

            if (paused && restartButton != null)
                restartButton.gameObject.SetActive(
                    Unity.Netcode.NetworkManager.Singleton != null &&
                    Unity.Netcode.NetworkManager.Singleton.IsHost);
        }
    }

    /// <summary>Resumes the game. Callable by any player.</summary>
    public void OnResumeClicked()
    {
        _gm?.SetPausedServerRpc(false);
    }

    /// <summary>Restarts the round. Host only.</summary>
    public void OnRestartClicked()
    {
        if (Unity.Netcode.NetworkManager.Singleton == null ||
            !Unity.Netcode.NetworkManager.Singleton.IsHost) return;

        _gm?.SetPausedServerRpc(false);
        _gm?.ResetGameServerRpc();
    }

    /// <summary>
    /// Returns to the main menu.
    /// Host: disconnects the session (all clients return to menu).
    /// Client: disconnects only this client.
    /// </summary>
    public void OnMenuClicked()
    {
        _gm?.SetPausedServerRpc(false);
        NetworkConnectionManager.Instance?.Disconnect(goToMainMenu: true);
    }

    /// <summary>
    /// Quits the application.
    /// Host: disconnects the session first so clients are not left hanging.
    /// Client: disconnects then quits.
    /// </summary>
    public void OnQuitClicked()
    {
        _gm?.SetPausedServerRpc(false);
        NetworkConnectionManager.Instance?.Disconnect(goToMainMenu: false);
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Player Left Message
    // ====================================================================

    /// <summary>Shows a "A player has left the game" banner for 3 seconds.</summary>
    public void ShowPlayerLeftMessage()
    {
        StopCoroutine(nameof(PlayerLeftCoroutine));
        StartCoroutine(nameof(PlayerLeftCoroutine));
    }

    private System.Collections.IEnumerator PlayerLeftCoroutine()
    {
        if (playerLeftPanel != null)
        {
            if (playerLeftText != null)
                playerLeftText.text = "A player has left the game.";
            playerLeftPanel.SetActive(true);
        }
        yield return new WaitForSeconds(3f);
        if (playerLeftPanel != null) playerLeftPanel.SetActive(false);
    }

    // ====================================================================
    // Private – Pause Input
    // ====================================================================

    /// <summary>Triggers a full in-place restart from the end-game panel. Host-only.</summary>
    public void OnEndGameRestartClicked()
    {
        if (!(_gm is { } gm)) return;
        gm.FullRestartGameServerRpc();
    }

    /// <summary>Returns to the main menu from the end-game panel.</summary>
    public void OnEndGameMenuClicked()
    {
        NetworkConnectionManager.Instance?.Disconnect(goToMainMenu: true);
    }

    /// <summary>
    /// Called by the on-screen <see cref="pauseButton"/> on mobile devices.
    /// Toggles the pause state via <see cref="GameManager.SetPausedServerRpc"/>.
    /// </summary>
    public void OnPauseButtonPressed()
    {
        if (_gm == null || !_gm.gameStarted.Value) return;
        _gm.SetPausedServerRpc(!_isPaused);
    }

    /// <summary>
    /// Detects Escape (keyboard) or Start/Menu button (any gamepad) and
    /// toggles the pause state via <see cref="GameManager.SetPausedServerRpc"/>.
    /// Only active while the game is running.
    /// </summary>
    private void CheckPauseInput()
    {
        if (_gm == null || !_gm.gameStarted.Value) return;

        bool pausePressed =
            UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true;

        if (!pausePressed)
        {
            foreach (var gp in UnityEngine.InputSystem.Gamepad.all)
            {
                if (gp.startButton.wasPressedThisFrame)
                {
                    pausePressed = true;
                    break;
                }
            }
        }

        if (pausePressed)
            _gm.SetPausedServerRpc(!_isPaused);
    }

    // ====================================================================
    // Private – Canvas Overlay Helpers
    // ====================================================================

    /// <summary>
    /// Ensures <paramref name="panel"/> renders above all other canvases by
    /// adding an <c>overrideSorting</c> Canvas component if one is not already
    /// present at the panel's root.  Works whether the panel is a standalone
    /// root Canvas or a child of another Canvas.
    /// </summary>
    private static void EnsureOverlayCanvas(GameObject panel, int sortingOrder)
    {
        if (panel == null) return;

        var canvas = panel.GetComponent<Canvas>();
        if (canvas == null)
            canvas = panel.AddComponent<Canvas>();

        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
    }

}