using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static GameManager;
public class UIManager : MonoBehaviour
{
    [Header("Game HUD")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI timerText;
    public GameObject endGamePanel;
    public Sprite lifeIconSprite;
    public float lifeIconWidth = 50f;
    public Transform livesContainer;

    [Header("Dash UI")]
    public GameObject dashUIContainer;
    public GameObject dashIconAvailable; // Ícone/Botão quando o dash está pronto
    public GameObject dashCooldownDisplay; // Painel que mostra o cooldown
    public TextMeshProUGUI dashCooldownText; // TextMeshProUGUI dentro do painel

    [Header("Attack UI")]
    public GameObject attackUIContainer;
    public GameObject attackIconAvailable;
    public GameObject attackCooldownDisplay;
    public TextMeshProUGUI attackCooldownText;

    public GameObject virtualJoystickContainer;

    private List<GameObject> lifeIcons = new List<GameObject>();
    public static UIManager Instance { get; private set; }

    [Header("Lobby UI in Game Scene")]
    public GameObject lobbyPanel;
    public Button startMatchButton;
    public Button disconnectButton;
    public TextMeshProUGUI lobbyCodeText;
    public TextMeshProUGUI playerCountText;
    public TextMeshProUGUI stageNameText;

    // Variáveis adicionais necessárias
    [Header("Additional UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject gameHudPanel;
    private GameManager _gameManager;

    private enum UIState { MainMenu, Lobby, Game }
    private UIState _currentState = UIState.MainMenu;
    private bool _isHost = false;
    private int _lastPlayerCount = 0;
    private PlayerMovement _localRunner;
    private Guard _localCatcher;

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
        //Debug.Log("UIManager Start called");
#if UNITY_ANDROID || UNITY_IOS
        // Define a orientação como Paisagem (Landscape) para garantir que o celular fique virado.
        Screen.orientation = ScreenOrientation.LandscapeLeft;
#endif
        InitializeUI();

        Invoke(nameof(DelayedUIUpdate), 1.5f);

        // Registrar para eventos de mudança de cena
        SceneManager.sceneLoaded += OnSceneLoaded;
        lobbyPanel.SetActive(true);
        // Adicione a lógica de conexão dos botões:
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
        if (NetworkManager.Singleton != null)
        {
            bool currentIsHost = NetworkManager.Singleton.IsHost;
            if (currentIsHost != _isHost)
            {
                _isHost = currentIsHost;
                UpdateStartMatchButton();
                //Debug.Log($"Host status changed: {_isHost}");
            }
        }
        UpdateDashUI();
        UpdateAttackUI();
    }

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
            Debug.Log($"RefreshPlayerCounter: ConnectionManager não disponível");
            UpdatePlayerCounter(GameSettings.LocalPlayerCount, NetworkConnectionManager.MAX_TOTAL_PLAYERS);
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void SetLocalPlayerMovement(PlayerMovement playerMovement)
    {
        _localRunner = playerMovement;
    }

    public void SetLocalCatcher(Guard guard)
    {
        _localCatcher = guard;
    }

    private void InitializeUI()
    {
        // Configurar estado inicial baseado na cena atual
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == "MenuScene")
        {
            ShowMainMenuUI();
        }
        else
        {
            ShowGameUI();
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectButtonClicked);
        }

        UpdateLobbyCode("---");

        RefreshPlayerCounter();
    }

    private void DelayedUIUpdate()
    {
        //Debug.Log("DelayedUIUpdate called");
        UpdateStartMatchButton();

        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
        if (connectionManager != null && !string.IsNullOrEmpty(connectionManager.LobbyCode))
        {
            UpdateLobbyCode(connectionManager.LobbyCode);
            //Debug.Log($"Lobby code updated: {connectionManager.LobbyCode}");
        }
    }

    private void UpdateStartMatchButton()
    {
        if (startMatchButton == null)
        {
            Debug.LogWarning("StartMatchButton is not assigned in inspector!");
            return;
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        startMatchButton.gameObject.SetActive(isHost);

        if (isHost)
        {
            startMatchButton.onClick.RemoveAllListeners();
            startMatchButton.onClick.AddListener(OnStartMatchButtonClicked);

            // O HOST SEMPRE PODE INICIAR A PARTIDA, MESMO SOZINHO
            startMatchButton.interactable = true;

            var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
            if (connectionManager != null && connectionManager.IsSpawned)
            {
                int totalPlayers = connectionManager.totalPlayers.Value + connectionManager.totalGuards.Value;
                if (totalPlayers != _lastPlayerCount)
                {
                    _lastPlayerCount = totalPlayers;
                    //Debug.Log($"Host can start match. Connected players: {totalPlayers}");
                }
            }
        }

        //Debug.Log($"Start Match Button - Active: {isHost}, Interactable: {startMatchButton.interactable}");
    }

    public void ShowMainMenuUI()
    {
        _currentState = UIState.MainMenu;
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (gameHudPanel != null) gameHudPanel.SetActive(false);
    }

    public void ShowLobbyUI(bool isHost, string lobbyCode)
    {
        _currentState = UIState.Lobby;
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(true);
            Debug.Log("Lobby panel ativado");
        }
        else
        {
            Debug.LogError("lobbyPanel é nulo! Verifique a referência no Inspector");
        }

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (gameHudPanel != null) gameHudPanel.SetActive(false);

        UpdateLobbyCode(lobbyCode);
        UpdateStartMatchButton();
    }

    public void ShowGameUI()
    {
        _currentState = UIState.Game;
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (gameHudPanel != null) gameHudPanel.SetActive(true);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        //Debug.Log($"Cena carregada: {scene.name}");

        if (scene.name == "MenuScene")
        {
            ShowMainMenuUI();
        }
        else
        {
            ShowGameUI();
        }
    }

    public void ShowLoadingMessage(string message)
    {
        //Debug.Log($"UI Loading: {message}");

        // Implementação simples - você pode expandir com uma UI de loading própria
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
    /// Chamado pelo GameManager para ativar/desativar o lobby e o HUD do jogo.
    /// </summary>
    /// <param name="started">True se o jogo começou, False se voltou ao lobby (ou menu)</param>
    public void HandleGameStart(bool started)
    {
        if (lobbyPanel != null)
        {
            lobbyPanel.SetActive(!started);
        }

        if (startMatchButton != null)
        {
            startMatchButton.interactable = !started;
        }
        if (virtualJoystickContainer != null)
        {
            virtualJoystickContainer.SetActive(started);
        }
    }

    /// <summary>
    /// Chamado quando o botão de Start Match é clicado (apenas Host).
    /// </summary>
    private void OnStartMatchButtonClicked()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            // Garante que o GameManager foi encontrado, já que ele pode ter sido spawnado após o UIManager
            if (_gameManager == null)
            {
                _gameManager = FindFirstObjectByType<GameManager>();
            }

            if (_gameManager != null)
            {
                _gameManager.SpawnAllPlayersAndStartGame();

                ShowGameUI();
            }
            else
            {
                Debug.LogError("GameManager não encontrado. Não foi possível iniciar a partida.");
            }
        }
    }

    public void OnDisconnectButtonClicked()
    {
        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
        if (connectionManager != null)
        {
            // Força limpeza completa e vai para o menu
            connectionManager.ForceCleanDisconnect();
        }
        else
        {
            // Fallback: Vai direto para o menu
            UnityEngine.SceneManagement.SceneManager.LoadScene("MenuScene");
        }
    }

    public void OnRestartGameButtonClicked()
    {
        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null && NetworkManager.Singleton.IsHost)
        {
            gameManager.ResetGameServerRpc();
        }
    }

    public void UpdateScoreText(int newScore)
    {
        if (scoreText != null)
        {
            var gameManager = FindFirstObjectByType<GameManager>();
            int targetScore = gameManager != null ? gameManager.scoreToWin : 10;
            scoreText.text = $"Coins: {newScore} / {targetScore}";
        }
    }

    public void UpdateTimerText(float newTime)
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(newTime / 60F);
            int seconds = Mathf.FloorToInt(newTime - minutes * 60);
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    public void UpdateLobbyCode(string code)
    {
        if (lobbyCodeText != null)
        {
            lobbyCodeText.text = $"Lobby: {code}";
        }
    }

    public void UpdatePlayerCounter(int current, int max)
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {current}/{max}";

            // LOG TEMPORÁRIO PARA DEBUG
            Debug.Log($"UpdatePlayerCounter chamado: current={current}, max={max}");
        }

        if (startMatchButton != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            startMatchButton.interactable = true;

            if (current != _lastPlayerCount)
            {
                _lastPlayerCount = current;
            }
        }
    }

    public void UpdateLivesUI(int currentLives)
    {
        foreach (var icon in lifeIcons)
        {
            Destroy(icon);
        }
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

    /// <summary>
    /// Updates the Dash UI based on the local player's cooldown.
    /// </summary>
    private void UpdateDashUI()
    {
        // Se não temos uma referência ao jogador local
        if (_localRunner == null)
        {
            // Tenta encontrar o player local (Owner)
            PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
                // Se ainda não encontrou, desativa a UI
                if (dashUIContainer != null) dashUIContainer.SetActive(false);
                return;
            }
        }

        // Verifica se é um Runner
        if (!_localRunner.IsRunner.Value)
        {
            if (dashUIContainer != null) dashUIContainer.SetActive(false);
            return;
        }

        // Ativa o container do dash UI
        if (dashUIContainer != null) dashUIContainer.SetActive(true);

        // Obtém o cooldown da variável de rede
        float cooldown = _localRunner.DashCooldownRemaining.Value;

        // DEBUG: Log para verificar valores
        // Debug.Log($"Dash UI - Cooldown: {cooldown}, IsRunner: {_localPlayerMovement.IsRunner.Value}, IsOwner: {_localPlayerMovement.IsOwner}");

        if (cooldown <= 0f)
        {
            // Dash Disponível - Esconde o painel de cooldown
            if (dashIconAvailable != null) dashIconAvailable.SetActive(true);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(false);
        }
        else
        {
            // Dash em Cooldown - Mostra o painel com o texto
            if (dashIconAvailable != null) dashIconAvailable.SetActive(false);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(true);

            if (dashCooldownText != null)
            {
                dashCooldownText.text = cooldown.ToString("F1");
            }
        }
    }

    private void UpdateAttackUI()
    {
        // Se não temos uma referência ao guard local
        if (_localCatcher == null)
        {
            // Tenta encontrar o guard local (Owner)
            Guard[] allGuards = FindObjectsByType<Guard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

        // Ativa o container do attack UI
        if (attackUIContainer != null) attackUIContainer.SetActive(true);

        // Obtém o cooldown da variável de rede
        float cooldown = _localCatcher.AttackCooldownRemaining.Value;

        // Debug.Log($"Attack UI - Cooldown: {cooldown}");

        if (cooldown <= 0f)
        {
            // Attack Disponível - Esconde o painel de cooldown
            if (attackIconAvailable != null) attackIconAvailable.SetActive(true);
            if (attackCooldownDisplay != null) attackCooldownDisplay.SetActive(false);
        }
        else
        {
            // Attack em Cooldown - Mostra o painel com o texto
            if (attackIconAvailable != null) attackIconAvailable.SetActive(false);
            if (attackCooldownDisplay != null) attackCooldownDisplay.SetActive(true);

            if (attackCooldownText != null)
            {
                attackCooldownText.text = cooldown.ToString("F1");
            }
        }
    }

    public void ShowEndGamePanel(EndGameResult result)
    {
        if (endGamePanel == null) return;

        endGamePanel.SetActive(true);

        // Encontrar os textos para atualizar
        Text victoryText = endGamePanel.transform.Find("VictoryText")?.GetComponent<Text>();
        Text messageText = endGamePanel.transform.Find("MessageText")?.GetComponent<Text>();
        Text descriptionText = endGamePanel.transform.Find("DescriptionText")?.GetComponent<Text>();

        if (victoryText != null)
        {
            victoryText.text = result.didLocalPlayerWin ? "VICTORY!" : "DEFEAT!";
            victoryText.color = result.didLocalPlayerWin ? Color.green : Color.red;
        }

        if (messageText != null)
        {
            messageText.text = result.message;
        }

        if (descriptionText != null)
        {
            if (result.didLocalPlayerWin)
            {
                descriptionText.text = $"Congratulations! The {result.winningTeam} have won!";
            }
            else
            {
                descriptionText.text = $"Better luck next time! The {result.losingTeam} have lost!";
            }
        }

        // Também podemos mostrar uma imagem diferente baseada no resultado
        Image backgroundImage = endGamePanel.GetComponent<Image>();
        if (backgroundImage != null)
        {
            backgroundImage.color = result.didLocalPlayerWin ?
                new Color(0.1f, 0.5f, 0.1f, 0.8f) : // Verde escuro para vitória
                new Color(0.5f, 0.1f, 0.1f, 0.8f);  // Vermelho escuro para derrota
        }
    }

    public void ShowSimpleEndGame(string message)
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);

            // Tenta encontrar qualquer componente de texto no painel para escrever a mensagem
            TMPro.TextMeshProUGUI textMesh = endGamePanel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (textMesh != null)
            {
                textMesh.text = message;
                // Opcional: Mudar a cor do texto
                textMesh.color = message.Contains("Victory") ? Color.green : Color.red;
            }
            else
            {
                // Fallback para UI Legacy (Text comum)
                Text legacyText = endGamePanel.GetComponentInChildren<Text>();
                if (legacyText != null)
                {
                    legacyText.text = message;
                    legacyText.color = message.Contains("Victory") ? Color.green : Color.red;
                }
            }
        }
    }

    public void HideEndGamePanel()
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
    }

    public void QuitGame()
    {
        var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();

        if (connectionManager != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            connectionManager.Disconnect(false);
        }

        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
    void OnEnable()
    {
        _gameManager = FindFirstObjectByType<GameManager>();
        if (_gameManager != null)
        {
            UpdateLivesUI(_gameManager.playerLives.Value);
        }
    }
}