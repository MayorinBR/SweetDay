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

    /// <summary>
    /// Duo-zone buttons managed in a separate pool from single buttons.
    /// Drag in <see cref="DuoButtonSpawner"/> instances here.
    /// </summary>
    public List<MonoBehaviour> duoButtons = new List<MonoBehaviour>();

    /// <summary>Maximum number of duo buttons active simultaneously.</summary>
    public int activeDuoButtonsAtOnce = 2;

    // ====================================================================
    // Private State
    // ====================================================================

    private readonly List<MonoBehaviour> _inactivePool = new List<MonoBehaviour>();
    private readonly List<MonoBehaviour> _inactiveDuoPool = new List<MonoBehaviour>();
    private int _currentlyActive;
    private int _currentlyActiveDuo;
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

        bool isDuo = duoButtons.Contains(button);
        if (isDuo) _currentlyActiveDuo--; else _currentlyActive--;

        DeactivateButton(button);

        var targetPool = isDuo ? _inactiveDuoPool : _inactivePool;
        if (!targetPool.Contains(button))
            targetPool.Add(button);

        Debug.Log($"[ButtonManager] '{button.name}' completed (duo={isDuo}).");
        StartCoroutine(RespawnAfterDelay(isDuo));
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

        if (_inactivePool.Count > 0 && _currentlyActive < activeButtonsAtOnce)
            StartCoroutine(SpawnSequence(false));

        if (_inactiveDuoPool.Count > 0 && _currentlyActiveDuo < activeDuoButtonsAtOnce)
            StartCoroutine(SpawnSequence(true));
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
        _inactiveDuoPool.Clear();
        _currentlyActive = 0;
        _currentlyActiveDuo = 0;

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

        foreach (var btn in duoButtons)
        {
            if (btn == null) continue;
            var netObj = btn.GetComponent<NetworkObject>();
            if (netObj == null || !netObj.IsSpawned) continue;
            _inactiveDuoPool.Add(btn);
            btn.gameObject.SetActive(false);
            (btn as IButtonZone)?.ResetButton();
            DeactivateButtonClientRpc(netObj.NetworkObjectId);
        }

        _isInitialized = true;
        Debug.Log($"[ButtonManager] Initialized. Pool: {_inactivePool.Count} single, {_inactiveDuoPool.Count} duo.");

        yield return new WaitForSeconds(2f);

        for (int i = 0; i < activeButtonsAtOnce; i++)
        {
            if (_inactivePool.Count == 0) break;
            StartCoroutine(SpawnSequence(false));
            yield return new WaitForSeconds(0.2f);
        }

        for (int i = 0; i < activeDuoButtonsAtOnce; i++)
        {
            if (_inactiveDuoPool.Count == 0) break;
            StartCoroutine(SpawnSequence(true));
            yield return new WaitForSeconds(0.2f);
        }
    }

    // ====================================================================
    // Private — Spawn / Deactivate
    // ====================================================================

    private IEnumerator SpawnSequence(bool isDuo)
    {
        var pool = isDuo ? _inactiveDuoPool : _inactivePool;
        int maxAttempts = Mathf.Min(10, pool.Count * 2);

        for (int attempt = 0; attempt < maxAttempts && pool.Count > 0; attempt++)
        {
            int index = Random.Range(0, pool.Count);
            MonoBehaviour candidate = pool[index];

            if (candidate == null) { pool.RemoveAt(index); continue; }

            bool blocked = Physics.CheckSphere(candidate.transform.position, checkRadius, obstructionLayers);
            if (!blocked)
            {
                pool.RemoveAt(index);
                if (isDuo) _currentlyActiveDuo++; else _currentlyActive++;

                candidate.gameObject.SetActive(true);
                (candidate as IButtonZone)?.ResetButton();

                var netObj = candidate.GetComponent<NetworkObject>();
                if (netObj != null) ActivateButtonClientRpc(netObj.NetworkObjectId);

                Debug.Log($"[ButtonManager] Activated '{candidate.name}' (duo={isDuo}).");
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

    private IEnumerator RespawnAfterDelay(bool isDuo)
    {
        yield return new WaitForSeconds(respawnDelay);
        if (!_isInitialized) yield break;
        if (isDuo && _inactiveDuoPool.Count > 0 && _currentlyActiveDuo < activeDuoButtonsAtOnce)
            StartCoroutine(SpawnSequence(true));
        else if (!isDuo && _inactivePool.Count > 0 && _currentlyActive < activeButtonsAtOnce)
            StartCoroutine(SpawnSequence(false));
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
    // ====================================================================
    // Public – Full Reset (used by in-place game restart)
    // ====================================================================

    /// <summary>
    /// Deactivates all active buttons and rebuilds the inactive pools from scratch.
    /// Call this when restarting the game in-place so buttons return to their initial state.
    /// Follow with <see cref="RespawnInitialButtons"/> to re-activate the opening set.
    /// </summary>
    public void FullReset()
    {
        if (!IsServer) return;

        foreach (var btn in allButtons)
        {
            if (btn == null) continue;
            (btn as IButtonZone)?.ResetButton();
            btn.gameObject.SetActive(false);
            var no = btn.GetComponent<NetworkObject>();
            if (no != null) DeactivateButtonClientRpc(no.NetworkObjectId);
        }

        foreach (var btn in duoButtons)
        {
            if (btn == null) continue;
            (btn as IButtonZone)?.ResetButton();
            btn.gameObject.SetActive(false);
            var no = btn.GetComponent<NetworkObject>();
            if (no != null) DeactivateButtonClientRpc(no.NetworkObjectId);
        }

        _inactivePool.Clear();
        _inactiveDuoPool.Clear();
        _currentlyActive = 0;
        _currentlyActiveDuo = 0;

        foreach (var btn in allButtons)
            if (btn != null) _inactivePool.Add(btn);

        foreach (var btn in duoButtons)
            if (btn != null) _inactiveDuoPool.Add(btn);
    }

    /// <summary>
    /// Spawns the initial set of buttons after a <see cref="FullReset"/>.
    /// Mirrors the end of <see cref="WaitForButtonsThenInitialize"/>.
    /// </summary>
    public void RespawnInitialButtons()
    {
        if (!IsServer || !_isInitialized) return;

        for (int i = 0; i < activeButtonsAtOnce; i++)
        {
            if (_inactivePool.Count == 0) break;
            StartCoroutine(SpawnSequence(false));
        }

        for (int i = 0; i < activeDuoButtonsAtOnce; i++)
        {
            if (_inactiveDuoPool.Count == 0) break;
            StartCoroutine(SpawnSequence(true));
        }
    }

}