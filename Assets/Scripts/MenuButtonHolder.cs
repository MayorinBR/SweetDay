using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MenuButtonHolder : MonoBehaviour
{
    [Header("Referências de Botões")]
    public Button hostButton;
    public Button joinButton;
    public Button quitButton;
    public TMP_Dropdown sceneDropdown;
    public TMP_Dropdown playerTypeDropdown;
    public TMP_InputField joinCodeInput;

    void Start()
    {
        ReconnectButtons();
    }

    void OnEnable()
    {
        // Reconectar sempre que ativar (após carregar cena)
        ReconnectButtons();
    }

    public void ReconnectButtons()
    {
        Debug.Log("Reconectando botões...");

        // Atualizar referências no MenuManager
        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.mainMenuPanel = transform.parent?.gameObject;

            if (sceneDropdown != null)
            {
                MenuManager.Instance.sceneDropdown = sceneDropdown;
                sceneDropdown.onValueChanged.RemoveAllListeners();
                sceneDropdown.onValueChanged.AddListener(MenuManager.Instance.OnSceneSelected);
            }

            if (playerTypeDropdown != null)
            {
                MenuManager.Instance.playerTypeDropdown = playerTypeDropdown;
                playerTypeDropdown.onValueChanged.RemoveAllListeners();
                playerTypeDropdown.onValueChanged.AddListener(MenuManager.Instance.OnPlayerTypeSelected);
            }

            if (joinCodeInput != null)
            {
                MenuManager.Instance.joinCodeInput = joinCodeInput;
            }
        }

        // Configurar botões
        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(OnHostButtonClicked);
            Debug.Log("Botão Host reconectado");
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(OnJoinButtonClicked);
            Debug.Log("Botão Join reconectado");
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitButtonClicked);
            Debug.Log("Botão Quit reconectado");
        }
    }

    private void OnHostButtonClicked()
    {
        Debug.Log("Host button clicked");
        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.StartHost();
        }
        else
        {
            Debug.LogError("MenuManager.Instance is null!");
            // Tentar encontrar o MenuManager
            MenuManager manager = FindFirstObjectByType<MenuManager>();
            if (manager != null)
            {
                manager.StartHost();
            }
        }
    }

    private void OnJoinButtonClicked()
    {
        Debug.Log("Join button clicked");
        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.JoinGame();
        }
    }

    private void OnQuitButtonClicked()
    {
        Debug.Log("Quit button clicked");
        if (MenuManager.Instance != null)
        {
            MenuManager.Instance.QuitGame();
        }
    }

    // Método para encontrar botões automaticamente
    public void FindButtonsAutomatically()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);

        foreach (Button button in buttons)
        {
            string buttonName = button.name.ToLower();

            if (buttonName.Contains("host") || buttonName.Contains("criar"))
            {
                hostButton = button;
                Debug.Log($"Botão Host encontrado: {button.name}");
            }
            else if (buttonName.Contains("join") || buttonName.Contains("entrar"))
            {
                joinButton = button;
                Debug.Log($"Botão Join encontrado: {button.name}");
            }
            else if (buttonName.Contains("quit") || buttonName.Contains("sair"))
            {
                quitButton = button;
                Debug.Log($"Botão Quit encontrado: {button.name}");
            }
        }

        // Encontrar dropdowns
        TMP_Dropdown[] dropdowns = GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (TMP_Dropdown dropdown in dropdowns)
        {
            string dropdownName = dropdown.name.ToLower();

            if (dropdownName.Contains("scene") || dropdownName.Contains("cena"))
            {
                sceneDropdown = dropdown;
                Debug.Log($"Dropdown de cena encontrado: {dropdown.name}");
            }
            else if (dropdownName.Contains("player") || dropdownName.Contains("tipo"))
            {
                playerTypeDropdown = dropdown;
                Debug.Log($"Dropdown de tipo encontrado: {dropdown.name}");
            }
        }

        // Encontrar input field
        TMP_InputField input = GetComponentInChildren<TMP_InputField>(true);
        if (input != null)
        {
            joinCodeInput = input;
            Debug.Log($"Input field encontrado: {input.name}");
        }

        // Reconectar após encontrar
        ReconnectButtons();
    }

    // Método público para reconectar de outros scripts
    public static void ReconnectAllButtonsInScene()
    {
        MenuButtonHolder holder = FindFirstObjectByType<MenuButtonHolder>();
        if (holder != null)
        {
            holder.FindButtonsAutomatically();
            holder.ReconnectButtons();
        }
    }
}