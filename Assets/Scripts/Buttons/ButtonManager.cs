using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the interactive pressure-plate buttons scattered
/// across the map.  Works with any <see cref="MonoBehaviour"/> that implements
/// <see cref="IButtonZone"/>, including <see cref="ButtonSpawner"/> and
/// <see cref="DuoButtonSpawner"/>.
/// </summary>
public class ButtonManager : NetworkBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Settings")]
    /// <summary>Maximum number of buttons that may be active simultaneously.</summary>
    public int activeButtonsAtOnce = 4;

    /// <summary>Seconds to wait after a button is completed before spawning a replacement.</summary>
    public float respawnDelay = 10f;

    /// <summary>Radius used by <see cref="Physics.CheckSphere"/> to verify a spawn position is clear.</summary>
    public float checkRadius = 1.5f;

    /// <summary>Layer mask checked when testing whether a candidate spawn position is obstructed.</summary>
    public LayerMask obstructionLayers;

    [Header("References")]
    /// <summary>
    /// All button zones managed by this component.
    /// Accepts any <see cref="MonoBehaviour"/> that implements <see cref="IButtonZone"/>
    /// — drag in <see cref="ButtonSpawner"/> or <see cref="DuoButtonSpawner"/> instances.
    /// </summary>
    public List<MonoBehaviour> allButtons = new List<MonoBehaviour>();

    // ====================================================================
    // Private State
    // ====================================================================

    private readonly List<MonoBehaviour> _inactivePool = new List<MonoBehaviour>();
    private int _currentlyActive;
    private bool _isInitialized;

    // ====================================================================
    // NetworkBehaviour
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        Debug.Log($"[ButtonManager] OnNetworkSpawn — {allButtons.Count} buttons registered.");
        StartCoroutine(WaitForButtonsThenInitialize());
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Called by an <see cref="IButtonZone"/> when a player successfully activates it.
    /// Deactivates the zone, returns it to the inactive pool, and schedules a replacement.
    /// Server-only.
    /// </summary>
    public void OnButtonCompleted(MonoBehaviour button)
    {
        if (!IsServer)
        {
            Debug.LogError("[ButtonManager] OnButtonCompleted called on a client — server only.");
            return;
        }

        if (button == null)
        {
            Debug.LogError("[ButtonManager] OnButtonCompleted received a null button.");
            return;
        }

        _currentlyActive--;
        DeactivateButton(button);

        if (!_inactivePool.Contains(button))
            _inactivePool.Add(button);

        Debug.Log($"[ButtonManager] '{button.name}' completed. Active: {_currentlyActive}, Pool: {_inactivePool.Count}");
        StartCoroutine(RespawnAfterDelay());
    }

    /// <summary>Attempts to activate a random button from the inactive pool. Server-only.</summary>
    public void TrySpawnRandomButton()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ButtonManager] TrySpawnRandomButton called on a client.");
            return;
        }

        if (!_isInitialized)
        {
            Debug.LogWarning("[ButtonManager] TrySpawnRandomButton called before initialization.");
            return;
        }

        if (_inactivePool.Count == 0 || _currentlyActive >= activeButtonsAtOnce) return;

        StartCoroutine(SpawnSequence());
    }

    // ====================================================================
    // Private — Initialization
    // ====================================================================

    private IEnumerator WaitForButtonsThenInitialize()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            bool allSpawned = true;
            foreach (var btn in allButtons)
            {
                var netObj = btn != null ? btn.GetComponent<NetworkObject>() : null;
                if (netObj == null || !netObj.IsSpawned) { allSpawned = false; break; }
            }
            if (allSpawned) break;

            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        _inactivePool.Clear();
        _currentlyActive = 0;

        foreach (var btn in allButtons)
        {
            if (btn == null) { Debug.LogError("[ButtonManager] Null entry in allButtons list."); continue; }

            var netObj = btn.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
            {
                Debug.LogWarning($"[ButtonManager] '{btn.name}' not spawned after timeout — skipping.");
                continue;
            }

            _inactivePool.Add(btn);
            btn.gameObject.SetActive(false);
            (btn as IButtonZone)?.ResetButton();
            DeactivateButtonClientRpc(netObj.NetworkObjectId);
        }

        _isInitialized = true;
        Debug.Log($"[ButtonManager] Initialized. Pool size: {_inactivePool.Count}");

        yield return new WaitForSeconds(2f);

        for (int i = 0; i < activeButtonsAtOnce; i++)
        {
            if (_inactivePool.Count == 0) break;
            TrySpawnRandomButton();
            yield return new WaitForSeconds(0.2f);
        }
    }

    // ====================================================================
    // Private — Spawn / Deactivate
    // ====================================================================

    private IEnumerator SpawnSequence()
    {
        int maxAttempts = Mathf.Min(10, _inactivePool.Count * 2);

        for (int attempt = 0; attempt < maxAttempts && _inactivePool.Count > 0; attempt++)
        {
            int index = Random.Range(0, _inactivePool.Count);
            MonoBehaviour candidate = _inactivePool[index];

            if (candidate == null) { _inactivePool.RemoveAt(index); continue; }

            bool blocked = Physics.CheckSphere(candidate.transform.position, checkRadius, obstructionLayers);
            if (!blocked)
            {
                _inactivePool.RemoveAt(index);
                _currentlyActive++;

                candidate.gameObject.SetActive(true);
                (candidate as IButtonZone)?.ResetButton();

                var netObj = candidate.GetComponent<NetworkObject>();
                if (netObj != null) ActivateButtonClientRpc(netObj.NetworkObjectId);

                Debug.Log($"[ButtonManager] Activated '{candidate.name}'. Active: {_currentlyActive}");
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        Debug.LogWarning("[ButtonManager] Failed to find a valid spawn position after all attempts.");
    }

    private void DeactivateButton(MonoBehaviour button)
    {
        button.gameObject.SetActive(false);
        (button as IButtonZone)?.ResetButton();

        var netObj = button.GetComponent<NetworkObject>();
        if (netObj != null) DeactivateButtonClientRpc(netObj.NetworkObjectId);
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        TrySpawnRandomButton();
    }

    // ====================================================================
    // Client RPCs
    // ====================================================================

    /// <summary>Activates the button with the given <paramref name="networkObjectId"/> on all clients.</summary>
    [ClientRpc]
    private void ActivateButtonClientRpc(ulong networkObjectId)
    {
        if (IsServer) return;

        if (!TryGetSpawnedObject(networkObjectId, out var netObj)) return;

        netObj.gameObject.SetActive(true);
        netObj.GetComponent<IButtonZone>()?.ResetButton();
    }

    /// <summary>Deactivates the button with the given <paramref name="networkObjectId"/> on all clients.</summary>
    [ClientRpc]
    private void DeactivateButtonClientRpc(ulong networkObjectId)
    {
        if (IsServer) return;

        if (TryGetSpawnedObject(networkObjectId, out var netObj))
            netObj.gameObject.SetActive(false);
    }

    // ====================================================================
    // Private — Utilities
    // ====================================================================

    private static bool TryGetSpawnedObject(ulong id, out NetworkObject result)
    {
        result = null;

        if (NetworkManager.Singleton?.SpawnManager == null)
        {
            Debug.LogError("[ButtonManager] SpawnManager is not available.");
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out result) || result == null)
        {
            Debug.LogWarning($"[ButtonManager] NetworkObject {id} not found in SpawnedObjects.");
            return false;
        }

        return true;
    }

    // ====================================================================
    // Editor Gizmos
    // ====================================================================

    private void OnDrawGizmos()
    {
        if (allButtons == null) return;

        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            Gizmos.color = btn.gameObject.activeSelf ? Color.green : Color.red;
            Gizmos.DrawWireSphere(btn.transform.position, checkRadius);
        }
    }
}