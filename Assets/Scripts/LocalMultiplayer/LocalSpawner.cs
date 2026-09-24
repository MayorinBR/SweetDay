using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Spawns and manages additional local (split-screen) player objects on the host machine.
/// </summary>
public class LocalSpawner : NetworkBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [SerializeField, Tooltip("Prefab for the Runner player type.")]
    private GameObject runnerPrefab;

    [SerializeField, Tooltip("Prefab for the Guard (catcher) player type.")]
    private GameObject guardPrefab;

    // ====================================================================
    // Private
    // ====================================================================

    private readonly List<GameObject> _localRunners = new List<GameObject>();
    private readonly List<GameObject> _localCatchers = new List<GameObject>();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Spawns all additional local Runner and Catcher players.
    /// Runner 0 and (when <see cref="GameSettings.IsCatcher"/> is true) Catcher 0
    /// are handled by <see cref="GameManager"/>; this method spawns the rest.
    /// Server-only.
    /// </summary>
    public void SpawnLocalPlayers()
    {
        if (!IsServer) return;

        Debug.Log($"[LocalSpawner] SpawnLocalPlayers – " +
                  $"Runners: {GameSettings.LocalRunnerCount}, " +
                  $"Catchers: {GameSettings.LocalCatcherCount}");

        CleanupLocalPlayers();

        // Additional runners (Runner 0 already spawned by GameManager).
        for (int i = 1; i < GameSettings.LocalRunnerCount; i++)
            SpawnRunner(i);

        // Local catchers — skip index 0 when the host is already a catcher.
        int catcherStart = GameSettings.IsCatcher ? 1 : 0;
        for (int i = catcherStart; i < GameSettings.LocalCatcherCount; i++)
            SpawnCatcher(i);

        StartCoroutine(NotifySplitScreenManager());
    }

    /// <summary>Despawns and destroys all local companion players. Server-only.</summary>
    public void DespawnLocalPlayers()
    {
        if (!IsServer) return;
        CleanupLocalPlayers();
    }

    // ====================================================================
    // Private – Spawn Helpers
    // ====================================================================

    /// <summary>Spawns one additional local Runner at <paramref name="runnerIndex"/>.</summary>
    private void SpawnRunner(int runnerIndex)
    {
        Vector3 pos = Vector3.zero;
        if (GameManager.Instance?.playerSpawnPoints?.Length > 0)
            pos = GameManager.Instance.playerSpawnPoints[
                runnerIndex % GameManager.Instance.playerSpawnPoints.Length].position;

        GameObject obj = Instantiate(runnerPrefab, pos, Quaternion.identity);

        if (obj.TryGetComponent<NetworkObject>(out var netObj))
            netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId, false);

        int displayId = runnerIndex + 1;
        if (obj.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayId;
            obj.name = "Runner_P" + displayId;
        }

        // Global device index = runner's local index.
        StartCoroutine(AssignControlsNextFrame(obj, globalIndex: runnerIndex));
        _localRunners.Add(obj);
    }

    /// <summary>Spawns one local Catcher at <paramref name="catcherIndex"/>.</summary>
    private void SpawnCatcher(int catcherIndex)
    {
        Vector3 pos = Vector3.zero;
        if (GameManager.Instance?.guardSpawnPoints?.Length > 0)
            pos = GameManager.Instance.guardSpawnPoints[
                catcherIndex % GameManager.Instance.guardSpawnPoints.Length].position;

        GameObject obj = Instantiate(guardPrefab, pos, Quaternion.identity);

        if (obj.TryGetComponent<NetworkObject>(out var netObj))
            netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId, false);

        int displayId = catcherIndex + 1;
        if (obj.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayId;
            obj.name = "Catcher_P" + displayId;
        }

        // Global device index = runners consumed + catcher local index.
        int globalIndex = GameSettings.LocalRunnerCount + catcherIndex;
        StartCoroutine(AssignControlsNextFrame(obj, globalIndex));
        _localCatchers.Add(obj);
    }

    // ====================================================================
    // Private – Control Assignment
    // ====================================================================

    private IEnumerator AssignControlsNextFrame(GameObject player, int globalIndex)
    {
        yield return null; // Wait one frame for PlayerInput to initialise.

        EnsureControlSetupManager();

        if (ControlSetupManager.Instance != null)
            ControlSetupManager.Instance.AssignControlScheme(player, globalIndex);
        else
            FallbackControlSetup(player, globalIndex);
    }

    /// <summary>
    /// Basic device assignment used when <see cref="ControlSetupManager"/> is unavailable.
    /// </summary>
    private void FallbackControlSetup(GameObject player, int globalIndex)
    {
        if (!player.TryGetComponent<PlayerInput>(out var pInput)) return;

        var gamepads = Gamepad.all;
        if (globalIndex == 0 && Keyboard.current != null)
            pInput.SwitchCurrentControlScheme(ControlSetupManager.FallbackSchemeKeyboard, Keyboard.current, Mouse.current);
        else if (globalIndex - 1 < gamepads.Count)
            pInput.SwitchCurrentControlScheme(ControlSetupManager.FallbackSchemeGamepad, gamepads[Mathf.Max(0, globalIndex - 1)]);
        else
            Debug.LogWarning($"[LocalSpawner] No input device found for global index {globalIndex}.");
    }

    private static void EnsureControlSetupManager()
    {
        if (ControlSetupManager.Instance != null) return;
        var go = new GameObject("ControlSetupManager");
        go.AddComponent<ControlSetupManager>();
        DontDestroyOnLoad(go);
        Debug.Log("[LocalSpawner] ControlSetupManager created automatically.");
    }

    // ====================================================================
    // Private – Cleanup
    // ====================================================================

    private void CleanupLocalPlayers()
    {
        DespawnList(_localRunners);
        DespawnList(_localCatchers);
    }

    private static void DespawnList(List<GameObject> list)
    {
        foreach (var obj in list)
        {
            if (obj == null) continue;
            if (obj.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();
            Destroy(obj);
        }
        list.Clear();
    }

    private IEnumerator NotifySplitScreenManager()
    {
        yield return null;
        FindAnyObjectByType<SplitScreenManager>()?.RefreshCameras();
    }

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    public override void OnDestroy()
    {
        base.OnDestroy();
        CleanupLocalPlayers();

        if (ControlSetupManager.Instance == null) return;

        int total = GameSettings.LocalRunnerCount + GameSettings.LocalCatcherCount;
        for (int i = 0; i < total; i++)
            ControlSetupManager.Instance.ReleasePlayerDevice(i);
    }
}