using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
public class MenuManager : MonoBehaviour
{
    public static MenuManager Instance { get; private set; }

    [Header("UI Elements")]
    public GameObject pcPanel;
    public GameObject mobilePanel;

    [Header("Menu Options")]
    public TMP_Dropdown sceneDropdown;
    public TMP_Dropdown playerTypeDropdown;
    public TMP_Dropdown localPlayersDropdown;

    [Header("Join Game")]
    public TMP_InputField joinCodeInput;

    private NetworkConnectionManager _connectionManager;
    private string _selectedScene = "TestScene_Flat";

    void Awake()
    {
        // GARANTIR QUE APENAS UMA INSTÂNCIA EXISTE
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

        // Garantir que a instância está correta
        if (_connectionManager == null)
        {
            GameObject connectionManagerObj = new GameObject("NetworkConnectionManager");
            _connectionManager = connectionManagerObj.AddComponent<NetworkConnectionManager>();
            DontDestroyOnLoad(connectionManagerObj);
        }

        // Configurar dropdowns
        if (sceneDropdown != null)
        {
            sceneDropdown.options.Clear();
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("TestScene_Flat"));
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("TestScene_Hill"));

            if (sceneDropdown.options.Count > 0)
            {
                _selectedScene = sceneDropdown.options[sceneDropdown.value].text;
            }

            sceneDropdown.onValueChanged.AddListener(OnSceneSelected);
        }

        if (playerTypeDropdown != null)
        {
            playerTypeDropdown.onValueChanged.AddListener(OnPlayerTypeSelected);
            // Só executa se o dropdown realmente existir
            OnPlayerTypeSelected(playerTypeDropdown.value);
        }
        else
        {
            Debug.LogWarning("MenuManager: playerTypeDropdown não foi atribuído no Inspector!");
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.onValidateInput += DelegateOnValidateInput;
            joinCodeInput.characterLimit = 6;
            joinCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        }

        if (localPlayersDropdown != null)
        {
            // Limpar opções existentes
            localPlayersDropdown.ClearOptions();

            // Adicionar opções de 1 a 4 jogadores locais
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            for (int i = 1; i <= 4; i++)
            {
                options.Add(new TMP_Dropdown.OptionData($"{i} Player{(i > 1 ? "s" : "")}"));
            }
            localPlayersDropdown.AddOptions(options);

            localPlayersDropdown.onValueChanged.AddListener(OnLocalPlayerCountSelected);
            // Definir valor inicial
            OnLocalPlayerCountSelected(localPlayersDropdown.value);
        }

        // Registrar para eventos de mudança de cena
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Configurar UI inicial
        ShowMainMenu();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MenuScene")
        {
            // Força o encerramento da rede ao voltar para o menu
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            UIManager.Instance.ShowMainMenuUI();
            SetupPlatformUI(); // Garante que o painel correto (PC/Mobile) apareça
        }
        else // Assume que é a cena de jogo
        {
            // Se estiver conectado, mostre o lobby UI por padrão.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                var connectionManager = FindFirstObjectByType<NetworkConnectionManager>();
                bool isHost = NetworkManager.Singleton.IsHost;
                string lobbyCode = connectionManager != null ? connectionManager.LobbyCode : "N/A";

                // MOSTRA O PAINEL DE LOBBY
                UIManager.Instance.ShowLobbyUI(isHost, lobbyCode);
            }
            else
            {
                // Fallback: Mostrar o HUD do jogo ou outra coisa
                UIManager.Instance.ShowGameUI();
            }
        }
    }

    // ====================================================================
    // UI Callbacks
    // ====================================================================

    public void OnSceneSelected(int index)
    {
        if (sceneDropdown != null && index >= 0 && index < sceneDropdown.options.Count)
        {
            _selectedScene = sceneDropdown.options[index].text;
            //Debug.Log($"Cena selecionada: {_selectedScene}");
        }
    }

    public void OnPlayerTypeSelected(int index)
    {
        bool isRunner = (index == 0);
        _connectionManager.SetPlayerType(isRunner);
    }

    public void OnLocalPlayerCountSelected(int index)
    {
        // index 0 = 1 player, index 1 = 2 players, etc.
        int playerCount = index + 1;
        GameSettings.LocalPlayerCount = playerCount;
        Debug.Log($"[MenuManager] Número de jogadores locais definido para: {GameSettings.LocalPlayerCount}");
    }

    // ADICIONAR getter para cena selecionada
    public string GetSelectedScene()
    {
        if (sceneDropdown != null && sceneDropdown.options.Count > 0)
        {
            return sceneDropdown.options[sceneDropdown.value].text;
        }
        return "TestScene_Flat";
    }

    // ====================================================================
    // MÉTODOS DE CONEXÃO
    // ====================================================================
    public async void StartHost()
    {
        if (_connectionManager != null)
        {
            // Feedback visual
            SetHostButtonState(false);

            try
            {
                // 1. Define o tipo de jogador
                bool isCatcher = playerTypeDropdown.value == 1;
                _connectionManager.SetPlayerType(isCatcher);

                //Debug.Log($"=== NOVA PARTIDA: Host como {(isCatcher ? "Catcher" : "Runner")} ===");

                // 2. Configurar payload ANTES de iniciar
                byte[] connectionPayload = _connectionManager.GetConnectionPayload();
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
                {
                    NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;
                    //Debug.Log($"Payload configurado: {(PlayerType)connectionPayload[0]}");
                }

                string sceneName = _selectedScene;

                // 3. Iniciar host
                _connectionManager.StartHostWithScene(sceneName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Erro ao iniciar host: {e}");
                // Reativar botão em caso de erro
                SetHostButtonState(true);
            }
            finally
            {
                // Reativar botão após um tempo (ou quando a partida realmente começar)
                Invoke(nameof(ReenableHostButton), 3f);
            }
        }
    }

    private void SetHostButtonState(bool interactable)
    {
        Button hostButton = GetComponentInChildren<Button>(); // Ajuste para seu botão específico
        if (hostButton != null)
        {
            hostButton.interactable = interactable;
            hostButton.GetComponentInChildren<TextMeshProUGUI>().text =
                interactable ? "Host Game" : "Loading...";
        }
    }

    private void ReenableHostButton()
    {
        SetHostButtonState(true);
    }

    public void JoinGame()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.ToUpper().Trim() : "";

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogError("O código de lobby não pode estar vazio.");
            // Adicione feedback visual para o usuário
            return;
        }

        // Validação mais rigorosa do código (Relay Join Codes geralmente têm 6 caracteres)
        if (code.Length != 6)
        {
            Debug.LogError("O código do lobby deve ter exatamente 6 caracteres.");
            return;
        }

        // Verifica se contém apenas caracteres válidos (A-Z e 0-9, excluindo ambiguidades)
        foreach (char c in code)
        {
            if (!char.IsLetterOrDigit(c) || c == '0' || c == 'O' || c == 'I' || c == 'L')
            {
                Debug.LogError("Código contém caracteres inválidos. Use apenas letras (exceto O, I, L) e números (exceto 0).");
                return;
            }
        }

        //
        //($"Tentando entrar no lobby com código: {code}");

        if (_connectionManager != null)
        {
            _connectionManager.SetPlayerType(playerTypeDropdown.value == 1);

            // Adiciona loading state
            SetJoinButtonState(false);

            try
            {
                // Chama o método funcional de Client/Lobby no NetworkConnectionManager
                _connectionManager.StartClientWithCode(code);
            }
            catch (Exception e)
            {
                Debug.LogError($"Erro ao entrar no lobby: {e}");
                // Mostra mensagem de erro para o usuário
            }
            finally
            {
                // Note: O estado do botão deve ser redefinido em caso de falha de conexão também,
                // ou quando o cliente se desconecta.
                SetJoinButtonState(true);
            }
        }
    }

    // Delegate de validação para forçar maiúsculas
    private char DelegateOnValidateInput(string text, int charIndex, char addedChar)
    {
        // Converte o caractere adicionado para maiúsculo
        return char.ToUpper(addedChar);
    }

    private void SetJoinButtonState(bool interactable)
    {
        Button joinButton = joinCodeInput?.GetComponentInParent<Button>();
        if (joinButton != null)
        {
            joinButton.interactable = interactable;
        }
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    private void SetupPlatformUI()
    {
        // Primeiro, desativa ambos para evitar conflito
        if (pcPanel != null) pcPanel.SetActive(false);
        if (mobilePanel != null) mobilePanel.SetActive(false);

#if UNITY_ANDROID || UNITY_IOS
        // Ativa o painel Mobile se estiver no Android ou iOS
        if(mobilePanel != null) mobilePanel.SetActive(true);
        Debug.Log("Menu: Carregando interface Mobile");
#else
        // Ativa o painel PC para Windows, Mac, Linux ou Editor
        if (pcPanel != null) pcPanel.SetActive(true);
        Debug.Log("Menu: Carregando interface PC");
#endif
    }

    public void ShowMainMenu()
    {
        SetupPlatformUI();
        // Assegure que o UI Manager na cena principal (Menu) esteja ativo, se houver um.
        var uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        if (uiManager != null)
        {
            uiManager.gameObject.SetActive(true);
            // Lógica para esconder painéis do jogo e mostrar painéis de conexão, se necessário.
        }
    }
}