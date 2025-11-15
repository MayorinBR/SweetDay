using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;

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
    private PlayerMovement _localPlayerMovement;

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
        InitializeUI();

        Invoke(nameof(DelayedUIUpdate), 1.5f);

        // Registrar para eventos de mudança de cena
        SceneManager.sceneLoaded += OnSceneLoaded;

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
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void SetLocalPlayerMovement(PlayerMovement player)
    {
        _localPlayerMovement = player;
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
        UpdatePlayerCounter(0, NetworkConnectionManager.MAX_PLAYERS + NetworkConnectionManager.MAX_GUARDS);
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

    // ====================================================================
    // NOVOS MÉTODOS DE CONTROLE DE UI ENTRE CENAS
    // ====================================================================

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
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
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

    // ADICIONAR método para detectar mudança de cena
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

    // ADICIONAR método para mostrar mensagem de loading
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

    // ====================================================================
    // MÉTODOS EXISTENTES (COM MODIFICAÇÕES)
    // ====================================================================

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
            startMatchButton.interactable = false;
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
                // NOVO: Chama o método que faz o spawn dos jogadores E inicia o jogo
                _gameManager.SpawnAllPlayersAndStartGame();

                // O painel de lobby será escondido pela lógica de rede, mas chamamos ShowGameUI
                // para transicionar o UI localmente (caso a lógica de rede demore ou seja assíncrona).
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
            connectionManager.Disconnect(true);
        }
        else
        {
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
            scoreText.text = $"Score: {newScore} / {targetScore}";
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
            lobbyCodeText.text = $"LOBBY CODE: {code}";
        }
    }

    public void UpdatePlayerCounter(int current, int max)
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {current} / {max}";
        }

        if (startMatchButton != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            startMatchButton.interactable = true;

            if (current != _lastPlayerCount)
            {
                _lastPlayerCount = current;
                //Debug.Log($"Player counter updated: {current}/{max}. Host can always start match.");
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
        if (_localPlayerMovement == null)
        {
            _localPlayerMovement = FindFirstObjectByType<PlayerMovement>(); // Tenta encontrar o player
            if (_localPlayerMovement == null || !_localPlayerMovement.IsRunner.Value)
            {
                dashUIContainer.SetActive(false);
                return; // Apenas runners
            }
            else
            {
                dashUIContainer.SetActive(true);
            }
                
        }

        float cooldown = _localPlayerMovement.DashCooldownRemaining.Value;

        if (cooldown <= 0f)
        {
            // Dash Disponível
            if (dashIconAvailable != null) dashIconAvailable.SetActive(true);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(false);
        }
        else
        {
            // Dash em Cooldown
            if (dashIconAvailable != null) dashIconAvailable.SetActive(false);
            if (dashCooldownDisplay != null) dashCooldownDisplay.SetActive(true);

            if (dashCooldownText != null)
            {
                // Mostra o tempo restante com uma casa decimal
                dashCooldownText.text = cooldown.ToString("F1");
            }
        }
    }

    public void ShowEndGamePanel(bool won)
    {
        ShowLobbyUI(false, "");

        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);
            TextMeshProUGUI resultText = endGamePanel.GetComponentInChildren<TextMeshProUGUI>();
            if (resultText != null)
            {
                resultText.text = won ? "You Won!" : "You Lost!";
            }

            Button[] buttons = endGamePanel.GetComponentsInChildren<Button>();
            if (buttons.Length > 1)
            {
                Button restartButton = buttons[1];
                if (restartButton != null)
                {
                    restartButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
                    restartButton.onClick.RemoveAllListeners();
                    restartButton.onClick.AddListener(OnRestartGameButtonClicked);
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