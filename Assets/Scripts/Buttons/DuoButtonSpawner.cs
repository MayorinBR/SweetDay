using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pressure-plate zone that requires two players to be present simultaneously
/// in order to fully activate and spawn coins.
///
/// <b>Progress rules (server-authoritative):</b>
/// <list type="bullet">
///   <item>0 players → timer resets to 0, bar hidden.</item>
///   <item>1 player  → timer fills to 50 % and holds (waiting for second player).</item>
///   <item>2 players → timer continues from current value to 100 %, then activates.</item>
///   <item>Any player leaves → timer resets to 0 immediately.</item>
/// </list>
///
/// Uses <see cref="NetworkObject.NetworkObjectId"/> to track individual players so
/// server-side physics triggers and client-side RPCs never double-count.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DuoButtonSpawner : NetworkBehaviour, IButtonZone
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Settings")]
    /// <summary>Total seconds two players must stand in the zone together to activate it.</summary>
    public float timeToStayInZone = 5f;

    /// <summary>Number of coins spawned on activation.</summary>
    public int coinsToSpawn = 5;

    /// <summary>Scatter radius for spawned coins.</summary>
    public float coinSpawnRadius = 3f;

    [Header("UI Settings")]
    /// <summary>World-space prefab containing a <see cref="Slider"/> for progress display.</summary>
    public GameObject progressBarUIPrefab;

    /// <summary>Local offset at which the bar is anchored above the zone.</summary>
    public Vector3 progressBarOffset = new Vector3(0f, 2f, 0f);

    /// <summary>Uniform scale applied to the instantiated progress bar.</summary>
    public float progressBarScale = 0.5f;

    /// <summary>Fill colour when one player is present (waiting state).</summary>
    public Color colorOnePlayer = new Color(1f, 0.85f, 0.1f);

    /// <summary>Fill colour when two players are present (active fill state).</summary>
    public Color colorTwoPlayers = new Color(0.25f, 0.85f, 0.25f);

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Normalised fill progress (0–1). Written server-side.</summary>
    private NetworkVariable<float> _progressNet = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Number of players currently in the zone. Drives the bar colour on all clients.</summary>
    public NetworkVariable<int> PlayersInZone = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ====================================================================
    // Private – Server State
    // ====================================================================

    /// <summary>
    /// Tracks the <see cref="NetworkObject.NetworkObjectId"/> of each player in the zone.
    /// HashSet prevents double-counting when server-physics and a client RPC both fire.
    /// </summary>
    private readonly HashSet<ulong> _playersInZone = new HashSet<ulong>();

    // ====================================================================
    // Private – Client State
    // ====================================================================

    private Slider _progressBar;
    private Image _fillImage;
    private bool _isInitialized;

    private ButtonManager _buttonManager;
    private GameManager _gameManager;

    // ====================================================================
    // Lifecycle
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CacheReferences();

        _progressNet.OnValueChanged += OnProgressChanged;
        PlayersInZone.OnValueChanged += OnPlayerCountChanged;

        if (_progressNet.Value > 0f)
            OnProgressChanged(0f, _progressNet.Value);

        _isInitialized = true;
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _progressNet.OnValueChanged -= OnProgressChanged;
        PlayersInZone.OnValueChanged -= OnPlayerCountChanged;
        HideProgressBar();
    }

    private void Update()
    {
        if (!IsServer || !_isInitialized) return;

        int count = _playersInZone.Count;
        if (count == 0) return;

        float cap = count >= 2 ? 1f : 0.5f;

        if (_progressNet.Value < cap)
        {
            _progressNet.Value = Mathf.Min(
                _progressNet.Value + Time.deltaTime / timeToStayInZone,
                cap);
        }

        if (_progressNet.Value >= 1f)
            ActivateZone();
    }

    // ====================================================================
    // IButtonZone
    // ====================================================================

    /// <inheritdoc/>
    public void ResetButton()
    {
        if (IsServer)
        {
            _playersInZone.Clear();
            _progressNet.Value = 0f;
            PlayersInZone.Value = 0;
        }
        HideProgressBar();
    }

    // ====================================================================
    // Trigger Handlers
    // ====================================================================

    private void OnTriggerEnter(Collider other)
    {
        if (!_isInitialized || !other.CompareTag("Player")) return;

        var netObj = other.GetComponent<NetworkObject>();
        if (netObj == null) return;

        if (IsServer)
            RegisterEnter(netObj.NetworkObjectId);
        else if (netObj.IsOwner)
            PlayerEnteredServerRpc(netObj.NetworkObjectId);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_isInitialized || !other.CompareTag("Player")) return;

        var netObj = other.GetComponent<NetworkObject>();
        if (netObj == null) return;

        if (IsServer)
            RegisterExit(netObj.NetworkObjectId);
        else if (netObj.IsOwner)
            PlayerExitedServerRpc(netObj.NetworkObjectId);
    }

    // ====================================================================
    // Server RPCs
    // ====================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlayerEnteredServerRpc(ulong networkObjectId) => RegisterEnter(networkObjectId);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlayerExitedServerRpc(ulong networkObjectId) => RegisterExit(networkObjectId);

    // ====================================================================
    // Private – Register Enter / Exit
    // ====================================================================

    private void RegisterEnter(ulong id)
    {
        _playersInZone.Add(id);
        PlayersInZone.Value = _playersInZone.Count;
    }

    private void RegisterExit(ulong id)
    {
        bool wasPresent = _playersInZone.Remove(id);
        PlayersInZone.Value = _playersInZone.Count;

        if (wasPresent)
            _progressNet.Value = _playersInZone.Count == 0
                ? 0f
                : Mathf.Min(_progressNet.Value, 0.5f);
    }

    // ====================================================================
    // Private – Activation
    // ====================================================================

    private void ActivateZone()
    {
        if (!IsServer) return;
        CacheReferences();

        _gameManager?.SpawnCoinsFromButtonServerRpc(
            new NetworkObjectReference(NetworkObject),
            transform.position,
            coinsToSpawn,
            coinSpawnRadius);

        _buttonManager?.OnButtonCompleted(this);

        _progressNet.Value = 0f;
        _playersInZone.Clear();
        PlayersInZone.Value = 0;
    }

    private void CacheReferences()
    {
        _gameManager ??= FindAnyObjectByType<GameManager>();
        _buttonManager ??= FindAnyObjectByType<ButtonManager>();
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
            UpdateProgressBar(current);
        }
        else
        {
            HideProgressBar();
        }
    }

    private void OnPlayerCountChanged(int previous, int current)
    {
        if (_fillImage == null) return;
        _fillImage.color = current >= 2 ? colorTwoPlayers : colorOnePlayer;
    }

    private void ShowProgressBar()
    {
        if (_progressBar != null || progressBarUIPrefab == null) return;

        var barGO = Instantiate(
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

            var fillArea = _progressBar.fillRect;
            if (fillArea != null)
                _fillImage = fillArea.GetComponent<Image>();
        }
        else
        {
            Debug.LogError("[DuoButtonSpawner] No Slider found in progressBarUIPrefab.");
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
        _fillImage = null;
    }

    // ====================================================================
    // Editor Gizmos
    // ====================================================================

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, coinSpawnRadius);
    }
}