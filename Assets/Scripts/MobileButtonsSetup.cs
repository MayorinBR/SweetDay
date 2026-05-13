using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires on-screen mobile buttons to the local player's action callbacks.
///
/// Active only on Android, iOS, and the Unity Editor (for testing).
/// On all other platforms the entire GameObject is deactivated at start.
/// </summary>
public class MobileButtonsSetup : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Runner Buttons")]
    /// <summary>Button that triggers the runner's dash ability.</summary>
    public Button dashButton;

    /// <summary>Button that triggers coin collection.</summary>
    public Button collectButton;

    /// <summary>Button that drops the runner's currently held coin.</summary>
    public Button dropButton;

    [Header("Catcher Buttons")]
    /// <summary>Button that triggers the guard's attack.</summary>
    public Button attackButton;

    // ====================================================================
    // Private
    // ====================================================================

    private bool _isSetupComplete;
    private GameObject _currentPlayerTracked;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start()
    {
#if UNITY_EDITOR || UNITY_ANDROID || UNITY_IOS
        SetButtonVisibility(visible: false);

        if (NetworkManager.Singleton != null)
        {
            FindAndSetupButtons();

            NetworkManager.Singleton.OnClientConnectedCallback += _ =>
            {
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
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;

        // Re-run setup if setup never completed or if the tracked player was destroyed.
        if (!_isSetupComplete || _currentPlayerTracked == null)
            FindAndSetupButtons();
#endif
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Searches for the local-owner player or guard and wires the appropriate buttons.
    /// Called automatically every frame until setup succeeds, and again whenever
    /// a new client connects.
    /// </summary>
    public void FindAndSetupButtons()
    {
        // Try to find the local Runner first.
        PlayerMovement runner = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
            .FirstOrDefault(p => p.IsOwner);

        if (runner != null)
        {
            _currentPlayerTracked = runner.gameObject;
            SetupRunnerButtons(runner);
            _isSetupComplete = true;
            return;
        }

        // If no Runner found, try the local Guard.
        Guard guard = FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
            .FirstOrDefault(g => g.IsOwner);

        if (guard != null)
        {
            _currentPlayerTracked = guard.gameObject;
            SetupCatcherButtons(guard);
            _isSetupComplete = true;
        }
    }

    // ====================================================================
    // Private Helpers
    // ====================================================================

    private void SetupRunnerButtons(PlayerMovement runner)
    {
        SetButtonVisibility(visible: true, isRunner: true);

        dashButton.onClick.RemoveAllListeners();
        dashButton.onClick.AddListener(runner.OnDashButtonClicked);

        collectButton.onClick.RemoveAllListeners();
        collectButton.onClick.AddListener(runner.OnCollectButtonClicked);

        dropButton.onClick.RemoveAllListeners();
        dropButton.onClick.AddListener(runner.OnDropButtonClicked);
    }

    private void SetupCatcherButtons(Guard guard)
    {
        SetButtonVisibility(visible: true, isRunner: false);

        attackButton.onClick.RemoveAllListeners();
        attackButton.onClick.AddListener(guard.OnAttackButtonClicked);
    }

    /// <summary>
    /// Shows or hides button groups based on the current role.
    /// When <paramref name="visible"/> is <c>false</c>, all buttons are hidden regardless of role.
    /// </summary>
    private void SetButtonVisibility(bool visible, bool isRunner = true)
    {
        bool showRunner = visible && isRunner;
        bool showCatcher = visible && !isRunner;

        if (dashButton != null) dashButton.gameObject.SetActive(showRunner);
        if (collectButton != null) collectButton.gameObject.SetActive(showRunner);
        if (dropButton != null) dropButton.gameObject.SetActive(showRunner);
        if (attackButton != null) attackButton.gameObject.SetActive(showCatcher);
    }
}
