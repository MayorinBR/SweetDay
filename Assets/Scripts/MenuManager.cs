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
    public GameObject mainMenuPanel;

    [Header("Menu Options")]
    public TMP_Dropdown sceneDropdown;
    public TMP_Dropdown playerTypeDropdown;

    [Header("Join Game")]
    public TMP_InputField joinCodeInput;

    private NetworkConnectionManager _connectionManager;
    private string _selectedScene = "TestScene"; // Valor padrão

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    void Start()
    {
        _connectionManager = NetworkConnectionManager.Instance;

        // Configurar dropdowns
        if (sceneDropdown != null)
        {
            // Adicione as cenas que deseja permitir.
            sceneDropdown.options.Clear();
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("TestScene"));
            sceneDropdown.options.Add(new TMP_Dropdown.OptionData("BeachScene"));

            // Define o valor inicial (se houver)
            if (sceneDropdown.options.Count > 0)
            {
                _selectedScene = sceneDropdown.options[sceneDropdown.value].text;
            }

            sceneDropdown.onValueChanged.AddListener(OnSceneSelected);
        }

        if (playerTypeDropdown != null)
        {
            playerTypeDropdown.onValueChanged.AddListener(OnPlayerTypeSelected);
            OnPlayerTypeSelected(playerTypeDropdown.value);
        }

        if (joinCodeInput != null)
        {
            // 1. Configuração para forçar MAIÚSCULAS
            joinCodeInput.onValidateInput += DelegateOnValidateInput;

            // 2. Limita o número de caracteres para 6
            joinCodeInput.characterLimit = 6;

            // O Relay Service usa apenas caracteres alfanuméricos, isso pode ser útil:
            joinCodeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
        }

        ShowMainMenu();

        // Registrar para eventos de mudança de cena
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "MenuScene")
        {
            UIManager.Instance.ShowMainMenuUI();
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

    private void OnSceneSelected(int index)
    {
        if (sceneDropdown != null && index >= 0 && index < sceneDropdown.options.Count)
        {
            _selectedScene = sceneDropdown.options[index].text;
            //Debug.Log($"Cena selecionada: {_selectedScene}");
        }
    }

    private void OnPlayerTypeSelected(int index)
    {
        bool isRunner = (index == 0);
        _connectionManager.SetPlayerType(isRunner);
    }

    // ADICIONAR getter para cena selecionada
    public string GetSelectedScene()
    {
        if (sceneDropdown != null && sceneDropdown.options.Count > 0)
        {
            return sceneDropdown.options[sceneDropdown.value].text;
        }
        return "TestScene";
    }

    // ====================================================================
    // MÉTODOS DE CONEXÃO
    // ====================================================================
    public void StartHost()
    {
        if (_connectionManager != null)
        {
            // 1. Define o tipo de jogador UMA VEZ apenas, baseado no dropdown atual
            bool isCatcher = playerTypeDropdown.value == 1;
            _connectionManager.SetPlayerType(isCatcher);

            // DEBUG: Verificar o tipo definido
            Debug.Log($"=== HOST: Tipo de jogador definido como: {(isCatcher ? "Catcher" : "Runner")} ===");

            // 2. Configura o payload de conexão ANTES de iniciar o host
            byte[] connectionPayload = _connectionManager.GetConnectionPayload();
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionPayload;
                Debug.Log($"Payload configurado: {(PlayerType)connectionPayload[0]}");
            }

            string sceneName = _selectedScene;
            _connectionManager.StartHostWithScene(sceneName);
        }
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

    public void ShowMainMenu()
    {
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(true);
        }
        // Assegure que o UI Manager na cena principal (Menu) esteja ativo, se houver um.
        var uiManager = FindFirstObjectByType<UIManager>(FindObjectsInactive.Include);
        if (uiManager != null)
        {
            uiManager.gameObject.SetActive(true);
            // Lógica para esconder painéis do jogo e mostrar painéis de conexão, se necessário.
        }
    }
}