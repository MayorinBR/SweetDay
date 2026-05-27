using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents an interactive pressure-plate zone on the map.
/// </summary>
public class ButtonSpawner : NetworkBehaviour, IButtonZone
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Settings")]
    /// <summary>Seconds a runner must remain inside the zone to activate it.</summary>
    public float timeToStayInZone = 5f;

    /// <summary>Number of coins spawned when the zone is fully activated.</summary>
    public int coinsToSpawn = 5;

    /// <summary>Radius around the button centre in which the coins are scattered.</summary>
    public float coinSpawnRadius = 3f;

    [Header("UI Settings")]
    /// <summary>
    /// World-space prefab that contains a <see cref="Slider"/> component used to display
    /// fill progress above the zone.
    /// </summary>
    public GameObject progressBarUIPrefab;

    /// <summary>Local offset from the button's position at which the progress bar is anchored.</summary>
    public Vector3 progressBarOffset = new Vector3(0f, 2f, 0f);

    /// <summary>
    /// Uniform scale applied to the progress bar prefab instance.
    /// Values below 1 make it smaller; adjust in the Inspector to taste.
    /// </summary>
    public float progressBarScale = 0.5f;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>
    /// Dwell time of the player currently inside the zone.
    /// Written server-side; all clients read it to animate the progress bar.
    /// </summary>
    private NetworkVariable<float> _currentStayTimeNet = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ====================================================================
    // Private State
    // ====================================================================

    private ButtonManager _buttonManager;
    private GameManager _gameManager;
    private GameObject _playerInZone;
    private Slider _progressBar;
    private bool _isPlayerInZone;
    private bool _isInitialized;

    // ====================================================================
    // Unity / NetworkBehaviour Lifecycle
    // ====================================================================

    private void OnEnable()
    {
        _isPlayerInZone = false;
        _playerInZone = null;

        // Reset the server-side timer only after the network is ready.
        if (IsSpawned && IsServer)
            _currentStayTimeNet.Value = 0f;
    }

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        CacheReferences();

        _currentStayTimeNet.OnValueChanged += OnProgressChanged;

        // Sync progress bar if this client joins mid-activation.
        if (_currentStayTimeNet.Value > 0f)
            OnProgressChanged(0f, _currentStayTimeNet.Value);

        _isInitialized = true;
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _currentStayTimeNet.OnValueChanged -= OnProgressChanged;
        HideProgressBar();
    }

    private void Update()
    {
        if (!_isInitialized || !IsServer || !_isPlayerInZone) return;

        _currentStayTimeNet.Value += Time.deltaTime;

        if (_currentStayTimeNet.Value >= timeToStayInZone)
            ActivateButton();
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Resets progress and clears the in-zone flag so the button can be re-activated
    /// after its cooldown expires.  Called by <see cref="ButtonManager"/>.
    /// </summary>
    public void ResetButton()
    {
        if (IsServer)
        {
            _currentStayTimeNet.Value = 0f;
            _isPlayerInZone = false;
        }

        _playerInZone = null;
        HideProgressBar();
    }

    // ====================================================================
    // Private – Activation Logic
    // ====================================================================

    private void ActivateButton()
    {
        if (!IsServer) return;

        CacheReferences();

        _gameManager?.SpawnCoinsFromButtonServerRpc(
            new NetworkObjectReference(NetworkObject),
            transform.position,
            coinsToSpawn,
            coinSpawnRadius);

        _buttonManager?.OnButtonCompleted(this);

        // Reset timer so the zone can be reused after its cooldown.
        _currentStayTimeNet.Value = 0f;
    }

    /// <summary>Lazily populates scene-object references.</summary>
    private void CacheReferences()
    {
        _gameManager ??= FindAnyObjectByType<GameManager>();
        _buttonManager ??= FindAnyObjectByType<ButtonManager>();
    }

    // ====================================================================
    // Trigger Handlers
    // ====================================================================

    private void OnTriggerEnter(Collider other)
    {
        if (!_isInitialized || !other.CompareTag("Player")) return;

        _playerInZone = other.gameObject;

        if (IsServer) _isPlayerInZone = true;
        else NotifyServerEnteredServerRpc(inside: true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_isInitialized) return;
        if (!other.CompareTag("Player")) return;
        if (other.gameObject != _playerInZone) return;

        _playerInZone = null;

        if (IsServer)
        {
            _isPlayerInZone = false;
            _currentStayTimeNet.Value = 0f;
        }
        else
        {
            NotifyServerEnteredServerRpc(inside: false);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void NotifyServerEnteredServerRpc(bool inside)
    {
        _isPlayerInZone = inside;
        if (!inside) _currentStayTimeNet.Value = 0f;
    }

    // ====================================================================
    // Private – Progress Bar UI
    // ====================================================================

    private void OnProgressChanged(float previous, float current)
    {
        if (!_isInitialized) return;

        if (current > 0f)
        {
            if (_progressBar == null) ShowProgressBar();
            UpdateProgressBar(current / timeToStayInZone);
        }
        else
        {
            HideProgressBar();
        }
    }

    private void ShowProgressBar()
    {
        if (_progressBar != null) return;

        if (progressBarUIPrefab == null)
        {
            Debug.LogError("[ButtonSpawner] progressBarUIPrefab is not assigned.");
            return;
        }

        GameObject barGO = Instantiate(
            progressBarUIPrefab,
            transform.position + progressBarOffset,
            Quaternion.identity,
            transform);

        barGO.transform.localScale = Vector3.one * progressBarScale;

        _progressBar = barGO.GetComponentInChildren<Slider>();

        if (_progressBar != null)
        {
            _progressBar.minValue = 0f;
            _progressBar.maxValue = 1f;
            _progressBar.value = 0f;
        }
        else
        {
            Debug.LogError("[ButtonSpawner] Could not find a Slider component in progressBarUIPrefab.");
        }
    }

    private void UpdateProgressBar(float normalised)
    {
        if (_progressBar != null)
            _progressBar.value = Mathf.Clamp01(normalised);
    }

    private void HideProgressBar()
    {
        if (_progressBar == null) return;

        Destroy(_progressBar.transform.parent != null
            ? _progressBar.transform.parent.gameObject
            : _progressBar.gameObject);

        _progressBar = null;
    }

    // ====================================================================
    // Editor
    // ====================================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, coinSpawnRadius);
    }
}