using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Linq;

public class MobileButtonsSetup : MonoBehaviour
{
    // Arraste os botões do Inspector para cá
    [Header("Runner Buttons")]
    public Button dashButton; // Z
    public Button collectButton; // X
    public Button dropButton; // C

    [Header("Catcher Buttons")]
    public Button attackButton; // X

    private bool _isSetupComplete = false;

    private void Start()
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        // 1. Desliga todos os botões por padrão no início
        SetButtonVisibility(false);

        if (NetworkManager.Singleton != null)
        {
            // 2. Tenta configurar imediatamente se já estiver conectado (Host/Server)
            FindAndSetupButtons();

            // 3. Subscreve ao evento de conexão do cliente (importante para clientes que se conectam depois)
            NetworkManager.Singleton.OnClientConnectedCallback += (clientId) => FindAndSetupButtons();
        }
#else
        gameObject.SetActive(false);
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        if (!_isSetupComplete && NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            FindAndSetupButtons();
        }
#endif
    }

    private void FindAndSetupButtons()
    {
        // Garante que não tentamos configurar a UI antes que o NetworkManager esteja ativo
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
        {
            return;
        }

        // Tenta encontrar o Runner local
        PlayerMovement runner = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
                                .FirstOrDefault(p => p.IsOwner);

        if (runner != null)
        {
            SetupRunnerButtons(runner);
            _isSetupComplete = true; // Marca como completo
            return;
        }

        // Tenta encontrar o Catcher local
        Guard guard = FindObjectsByType<Guard>(FindObjectsSortMode.None)
                        .FirstOrDefault(g => g.IsOwner);

        if (guard != null)
        {
            SetupGuardButtons(guard);
            _isSetupComplete = true; // Marca como completo
            return;
        }

        // Se nenhum jogador local foi encontrado (e o cliente está conectado), _isSetupComplete permanece false
        // e tentará novamente no próximo Update.
    }

    private void SetupRunnerButtons(PlayerMovement runner)
    {
        // Liga os botões do Runner
        SetButtonVisibility(true, isRunner: true);

        // Remove listeners antigos para evitar chamadas duplicadas
        dashButton.onClick.RemoveAllListeners();
        collectButton.onClick.RemoveAllListeners();
        dropButton.onClick.RemoveAllListeners();

        // Adiciona novos listeners
        dashButton.onClick.AddListener(runner.OnDashButtonClicked);
        collectButton.onClick.AddListener(runner.OnCollectButtonClicked);
        dropButton.onClick.AddListener(runner.OnDropButtonClicked);

        Debug.Log("Botões Mobile ligados ao Runner local.");
    }

    private void SetupGuardButtons(Guard guard)
    {
        // Liga o botão de ataque do Catcher
        SetButtonVisibility(true, isRunner: false);

        // Remove listeners antigos
        attackButton.onClick.RemoveAllListeners();

        // Adiciona novo listener
        attackButton.onClick.AddListener(guard.OnAttackButtonClicked);

        Debug.Log("Botões Mobile ligados ao Catcher local.");
    }

    private void SetButtonVisibility(bool visible, bool isRunner = true)
    {
        if (isRunner)
        {
            // O Runner usa Z, X, C
            if (dashButton != null) dashButton.gameObject.SetActive(visible);
            if (collectButton != null) collectButton.gameObject.SetActive(visible);
            if (dropButton != null) dropButton.gameObject.SetActive(visible);
            if (attackButton != null) attackButton.gameObject.SetActive(false);
        }
        else // Catcher
        {
            // O Catcher usa apenas o botão de Ataque (X)
            if (dashButton != null) dashButton.gameObject.SetActive(false);
            if (collectButton != null) collectButton.gameObject.SetActive(false);
            if (dropButton != null) dropButton.gameObject.SetActive(false);
            if (attackButton != null) attackButton.gameObject.SetActive(visible);
        }

        // Se visible for false (setup inicial), desliga todos
        if (!visible)
        {
            if (dashButton != null) dashButton.gameObject.SetActive(false);
            if (collectButton != null) collectButton.gameObject.SetActive(false);
            if (dropButton != null) dropButton.gameObject.SetActive(false);
            if (attackButton != null) attackButton.gameObject.SetActive(false);
        }
    }
}