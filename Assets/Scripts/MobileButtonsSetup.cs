using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Linq;

public class MobileButtonsSetup : MonoBehaviour
{
    [Header("Runner Buttons")]
    public Button dashButton;
    public Button collectButton;
    public Button dropButton;

    [Header("Catcher Buttons")]
    public Button attackButton;

    private bool _isSetupComplete = false;
    // Adicionamos esta variável para monitorar se o player atual ainda existe
    private GameObject _currentPlayerTracked;

    private void Start()
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        SetButtonVisibility(false);

        if (NetworkManager.Singleton != null)
        {
            FindAndSetupButtons();
            // Tenta reconfigurar quando alguém conecta
            NetworkManager.Singleton.OnClientConnectedCallback += (clientId) => {
                _isSetupComplete = false;
                FindAndSetupButtons();
            };
        }
#else
        gameObject.SetActive(false);
#endif
    }

    private void Update()
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        // Se o player que estávamos seguindo foi destruído (nova partida), 
        // ou se nunca terminamos o setup, tentamos encontrar o novo player.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            if (!_isSetupComplete || _currentPlayerTracked == null)
            {
                FindAndSetupButtons();
            }
        }
#endif
    }

    public void FindAndSetupButtons()
    {
        // Busca o Player local (Runner)
        var runner = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
            .FirstOrDefault(p => p.IsOwner);

        if (runner != null)
        {
            _currentPlayerTracked = runner.gameObject;
            SetupRunnerButtons(runner);
            _isSetupComplete = true;
            return;
        }

        // Se não achou Runner, busca o Guard local (Catcher)
        var guard = FindObjectsByType<Guard>(FindObjectsSortMode.None)
            .FirstOrDefault(g => g.IsOwner);

        if (guard != null)
        {
            _currentPlayerTracked = guard.gameObject;
            SetupCatcherButtons(guard);
            _isSetupComplete = true;
            return;
        }
    }

    private void SetupRunnerButtons(PlayerMovement runner)
    {
        SetButtonVisibility(true, true);

        dashButton.onClick.RemoveAllListeners();
        dashButton.onClick.AddListener(runner.OnDashButtonClicked);

        collectButton.onClick.RemoveAllListeners();
        collectButton.onClick.AddListener(runner.OnCollectButtonClicked);

        dropButton.onClick.RemoveAllListeners();
        dropButton.onClick.AddListener(runner.OnDropButtonClicked);
    }

    private void SetupCatcherButtons(Guard guard)
    {
        SetButtonVisibility(true, false);

        attackButton.onClick.RemoveAllListeners();
        attackButton.onClick.AddListener(guard.OnAttackButtonClicked);
    }

    private void SetButtonVisibility(bool visible, bool isRunner = true)
    {
        if (isRunner)
        {
            if (dashButton != null) dashButton.gameObject.SetActive(visible);
            if (collectButton != null) collectButton.gameObject.SetActive(visible);
            if (dropButton != null) dropButton.gameObject.SetActive(visible);
            if (attackButton != null) attackButton.gameObject.SetActive(false);
        }
        else
        {
            if (dashButton != null) dashButton.gameObject.SetActive(false);
            if (collectButton != null) collectButton.gameObject.SetActive(false);
            if (dropButton != null) dropButton.gameObject.SetActive(false);
            if (attackButton != null) attackButton.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            if (dashButton != null) dashButton.gameObject.SetActive(false);
            if (collectButton != null) collectButton.gameObject.SetActive(false);
            if (dropButton != null) dropButton.gameObject.SetActive(false);
            if (attackButton != null) attackButton.gameObject.SetActive(false);
        }
    }
}