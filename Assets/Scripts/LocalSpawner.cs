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
    // Inspector Fields
    // ====================================================================

    [SerializeField, Tooltip("Prefab for the Runner player type.")]
    private GameObject runnerPrefab;

    [SerializeField, Tooltip("Prefab for the Guard (catcher) player type.")]
    private GameObject guardPrefab;

    // ====================================================================
    // Private State
    // ====================================================================

    private readonly List<GameObject> _localPlayers = new List<GameObject>();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Spawns split-screen companion players (indices 1+) on the server.
    /// Player 0 is already spawned by <see cref="GameManager.SpawnAllPlayersAndStartGame"/>.
    /// </summary>
    public void SpawnLocalPlayers()
    {
        if (!IsServer) return;

        Debug.Log($"[LocalSpawner] SpawnLocalPlayers – LocalPlayerCount: {GameSettings.LocalPlayerCount}");

        CleanupLocalPlayers();

        for (int i = 1; i < GameSettings.LocalPlayerCount; i++)
            SpawnAdditionalLocalPlayer(i);

        Debug.Log($"[LocalSpawner] Additional local players spawned: {_localPlayers.Count}");

        StartCoroutine(NotifySplitScreenManager());
    }

    /// <summary>
    /// Despawns and destroys all split-screen companion players managed by this spawner.
    /// Should only be called on the server.
    /// </summary>
    public void DespawnLocalPlayers()
    {
        if (!IsServer) return;
        CleanupLocalPlayers();
    }

    // ====================================================================
    // Private – Spawning
    // ====================================================================

    private void SpawnAdditionalLocalPlayer(int playerIndex)
    {
        PlayerType hostType = GameSettings.IsCatcher ? PlayerType.Catcher : PlayerType.Runner;
        GameObject prefab = hostType == PlayerType.Runner ? runnerPrefab : guardPrefab;

        // Choose a spawn point, cycling through available slots.
        Vector3 spawnPos = Vector3.zero;
        if (GameManager.Instance != null)
        {
            Transform[] points = hostType == PlayerType.Runner
                ? GameManager.Instance.playerSpawnPoints
                : GameManager.Instance.guardSpawnPoints;

            if (points != null && points.Length > 0)
                spawnPos = points[playerIndex % points.Length].position;
        }

        GameObject newPlayer = Instantiate(prefab, spawnPos, Quaternion.identity);

        // Spawn on the network, owned by the local (host) client.
        if (newPlayer.TryGetComponent<NetworkObject>(out var netObj))
            netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId, false);

        // Assign display ID and name before assigning controls.
        int displayId = playerIndex + 1;
        if (newPlayer.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayId;
            newPlayer.name = "Runner_P" + displayId;
        }
        else if (newPlayer.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayId;
            newPlayer.name = "Catcher_P" + displayId;
        }

        // Defer control assignment to the next frame so PlayerInput is fully initialised.
        StartCoroutine(AssignControlsNextFrame(newPlayer, playerIndex));

        _localPlayers.Add(newPlayer);
    }

    // ====================================================================
    // Private – Control Assignment
    // ====================================================================

    private IEnumerator AssignControlsNextFrame(GameObject player, int playerIndex)
    {
        yield return null; // Wait one frame for PlayerInput initialisation.

        EnsureControlSetupManager();

        if (ControlSetupManager.Instance != null)
            ControlSetupManager.Instance.AssignControlScheme(player, playerIndex);
        else
            FallbackControlSetup(player, playerIndex);
    }

    /// <summary>
    /// Basic control assignment used when <see cref="ControlSetupManager"/> is unavailable.
    /// </summary>
    private void FallbackControlSetup(GameObject player, int playerIndex)
    {
        if (!player.TryGetComponent<PlayerInput>(out var pInput)) return;

        var gamepads = Gamepad.all;

        if (playerIndex == 0)
        {
            if (Keyboard.current != null)
                pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current, Mouse.current);
            else if (gamepads.Count > 0)
                pInput.SwitchCurrentControlScheme("Gamepad", gamepads[0]);
            else
                Debug.LogWarning("[LocalSpawner] Player 1: no input device available.");
        }
        else if (playerIndex < gamepads.Count)
        {
            pInput.SwitchCurrentControlScheme("Gamepad", gamepads[playerIndex]);
        }
        else
        {
            Debug.LogWarning($"[LocalSpawner] Player {playerIndex + 1}: no dedicated input device found.");
        }
    }

    /// <summary>Creates a <see cref="ControlSetupManager"/> GameObject if one does not already exist.</summary>
    private static void EnsureControlSetupManager()
    {
        if (ControlSetupManager.Instance != null) return;

        var managerObj = new GameObject("ControlSetupManager");
        managerObj.AddComponent<ControlSetupManager>();
        DontDestroyOnLoad(managerObj);
        Debug.Log("[LocalSpawner] ControlSetupManager created automatically.");
    }

    // ====================================================================
    // Private – Cleanup
    // ====================================================================

    private void CleanupLocalPlayers()
    {
        foreach (var player in _localPlayers)
        {
            if (player == null) continue;

            if (player.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned)
                n.Despawn();

            Destroy(player);
        }
        _localPlayers.Clear();
    }

    private IEnumerator NotifySplitScreenManager()
    {
        yield return null; // Allow all spawned objects to be registered.

        SplitScreenManager ssm = FindAnyObjectByType<SplitScreenManager>();
        if (ssm != null)
            ssm.RefreshCameras();
        else
            Debug.LogWarning("[LocalSpawner] SplitScreenManager not found in scene.");
    }

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void OnDestroy()
    {
        CleanupLocalPlayers();

        if (ControlSetupManager.Instance == null) return;
        for (int i = 0; i < GameSettings.LocalPlayerCount; i++)
            ControlSetupManager.Instance.ReleasePlayerDevice(i);
    }
}
