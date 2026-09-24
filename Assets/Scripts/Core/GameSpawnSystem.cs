using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles spawning and despawning of players and coins, and resetting player
/// positions. Owns the prefab, spawn-point, and spawner references formerly on
/// <see cref="GameManager"/>. Plain <see cref="MonoBehaviour"/>: it has no
/// NetworkVariables or RPCs of its own. Camera/teleport RPCs it needs are
/// exposed as public methods on <see cref="GameManager"/>, which remains the
/// only NetworkBehaviour in this trio.
/// </summary>
public class GameSpawnSystem : MonoBehaviour
{
    // ====================================================================
    // Inspector Fields
    // ====================================================================

    /// <summary>Prefab used to spawn Guard (catcher) players.</summary>
    public GameObject guardPrefab;

    /// <summary>Prefab used to spawn Runner players.</summary>
    public GameObject playerPrefab;

    /// <summary>World-space spawn points for runners, indexed by player slot.</summary>
    public Transform[] playerSpawnPoints;

    /// <summary>World-space spawn points for guards, indexed by player slot.</summary>
    public Transform[] guardSpawnPoints;

    [Header("Spawners")]
    /// <summary>Reference to the local (split-screen) player spawner.</summary>
    public LocalSpawner localSpawner;

    /// <summary>Reference to the coin spawner responsible for placing coins on the map.</summary>
    public CoinSpawner coinSpawner;

    // ====================================================================
    // Private State
    // ====================================================================

    private GameManager _gameManager;

    // Tracks how many players of each type have been spawned in the current session.
    // Reset by ResetSpawnState (called from GameStateSystem.ResetGame).
    private int _runnerSpawnIndex;
    private int _catcherSpawnIndex;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake() => _gameManager = GetComponent<GameManager>();

    // ====================================================================
    // Public – Spawn
    // ====================================================================

    /// <summary>
    /// Spawns every player from <see cref="GameSettings.SlotAssignments"/> (or a
    /// fallback one-per-connected-client scheme when no slots were assigned),
    /// then refreshes the lobby player counter. Server-only.
    /// </summary>
    public void SpawnAllPlayers()
    {
        if (!_gameManager.IsServer) return;

        Debug.Log("[GameSpawnSystem] === SPAWNING ALL PLAYERS ===");

        _runnerSpawnIndex = 0;
        _catcherSpawnIndex = 0;

        var slotList = GameSettings.SlotAssignments;

        if (slotList.Count > 0)
        {
            // ── Slot-based spawning (lobby path) ─────────────────────────
            var sorted = new List<GameSettings.SlotAssignment>(slotList);
            sorted.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

            foreach (var a in sorted)
            {
                bool isRunner = a.SlotIndex < LobbyStateManager.RunnerSlotCount;
                int spawnIndex = isRunner ? a.SlotIndex
                                                 : a.SlotIndex - LobbyStateManager.RunnerSlotCount;
                PlayerType type = isRunner ? PlayerType.Runner : PlayerType.Catcher;

                SpawnPlayerAtIndex(a.ClientId, type, spawnIndex, a.LocalPlayerIndex);
            }
        }
        else
        {
            // ── Fallback: no slot data (e.g. direct editor test) ─────────
            Debug.LogWarning("[GameSpawnSystem] No slot assignments found — using fallback spawn.");

            var cm = FindAnyObjectByType<NetworkConnectionManager>();
            if (cm == null) { Debug.LogError("[GameSpawnSystem] NetworkConnectionManager missing."); return; }

            SpawnPlayerAtIndex(NetworkManager.ServerClientId,
                cm.GetPlayerType(NetworkManager.ServerClientId), 0, 0);

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.ClientId == NetworkManager.ServerClientId) continue;
                if (client.PlayerObject != null) continue;

                var t = cm.GetPlayerType(client.ClientId);
                int idx = t == PlayerType.Runner ? _runnerSpawnIndex : _catcherSpawnIndex;
                SpawnPlayerAtIndex(client.ClientId, t, idx, 0);
            }
        }

        var connMgr = FindAnyObjectByType<NetworkConnectionManager>();
        if (connMgr != null && connMgr.IsSpawned)
            connMgr.UpdatePlayerCountServerRpc();

        Debug.Log($"[GameSpawnSystem] Spawned {slotList.Count} player(s).");
    }

    /// <summary>
    /// Spawns one player at the <paramref name="spawnIndex"/>-th spawn point
    /// and, for local players, pairs their physical device by
    /// <paramref name="localPlayerIndex"/>.
    ///
    /// Display numbers: Runner spawnIndex 0 → P1, Catcher spawnIndex 0 → P5.
    /// </summary>
    private void SpawnPlayerAtIndex(
        ulong clientId,
        PlayerType type,
        int spawnIndex,
        int localPlayerIndex = 0)
    {
        if (!_gameManager.IsServer) return;

        GameObject prefab = type == PlayerType.Runner ? playerPrefab : guardPrefab;
        Transform[] spawnPoints = type == PlayerType.Runner ? playerSpawnPoints : guardSpawnPoints;

        if (prefab == null)
        {
            Debug.LogError($"[GameSpawnSystem] {type} prefab not assigned.");
            return;
        }
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError($"[GameSpawnSystem] No spawn points for {type}. Assign in Inspector.");
            return;
        }

        int spawnIdx = Mathf.Clamp(spawnIndex, 0, spawnPoints.Length - 1);
        int displayNum = type == PlayerType.Runner ? spawnIndex + 1 : spawnIndex + 5;

        GameObject obj = Instantiate(prefab, spawnPoints[spawnIdx].position, Quaternion.identity);
        obj.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        if (type == PlayerType.Runner && obj.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayNum;
            obj.name = $"Runner_P{displayNum}";
        }
        else if (type == PlayerType.Catcher && obj.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayNum;
            obj.name = $"Catcher_P{displayNum}";
        }

        // Pair the physical device on the local machine.
        // For remote clients the assignment happens on their own machine via
        // ControlSetupManager when their PlayerMovement/Guard.OnNetworkSpawn fires.
        bool isLocalClient = clientId == NetworkManager.Singleton.LocalClientId;
        if (isLocalClient)
            StartCoroutine(AssignDeviceAfterSpawn(obj, localPlayerIndex));

        if (type == PlayerType.Runner) _runnerSpawnIndex++;
        else _catcherSpawnIndex++;

        Debug.Log($"[GameSpawnSystem] Spawned {type} P{displayNum} for client {clientId} " +
                  $"(localIdx={localPlayerIndex}) at spawnPoint[{spawnIdx}].");
    }

    private IEnumerator AssignDeviceAfterSpawn(GameObject playerObj, int localPlayerIndex)
    {
        yield return null; // Wait one frame for PlayerInput to initialise.

        if (LocalPlayerManager.Instance != null)
            LocalPlayerManager.Instance.AssignDeviceToPlayer(playerObj, localPlayerIndex);
        else
            ControlSetupManager.Instance?.AssignControlScheme(playerObj, localPlayerIndex);
    }

    // ====================================================================
    // Public – Despawn / Reset
    // ====================================================================

    /// <summary>Despawns every active Runner and Guard from the network.</summary>
    public void DespawnAllPlayers()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude))
            if (pm.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();

        foreach (var g in FindObjectsByType<Guard>(FindObjectsInactive.Exclude))
            if (g.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned) n.Despawn();
    }

    /// <summary>Despawns every active coin from the network.</summary>
    public void DespawnAllCoins()
    {
        if (!_gameManager.IsServer) return;

        foreach (var coin in FindObjectsByType<Coin>(FindObjectsInactive.Exclude))
            if (coin.TryGetComponent<NetworkObject>(out var n) && n.IsSpawned)
                n.Despawn();
    }

    /// <summary>Teleports all runners and guards back to their spawn points and resets their cameras.</summary>
    public void ResetAllPlayersPosition()
    {
        if (!_gameManager.IsServer) return;

        var allRunners = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allRunners.Length; i++)
        {
            if (playerSpawnPoints.Length == 0) break;
            Vector3 pos = playerSpawnPoints[i % playerSpawnPoints.Length].position;
            allRunners[i].TeleportPlayerClientRpc(pos);
            _gameManager.ResetCameraForPlayerClientRpc(allRunners[i].NetworkObjectId, pos);
        }

        var allGuards = FindObjectsByType<Guard>(FindObjectsInactive.Exclude);
        for (int i = 0; i < allGuards.Length; i++)
        {
            if (guardSpawnPoints.Length == 0) break;
            Vector3 pos = guardSpawnPoints[i % guardSpawnPoints.Length].position;
            allGuards[i].TeleportPlayerClientRpc(pos);
            _gameManager.ResetCameraForPlayerClientRpc(allGuards[i].NetworkObjectId, pos);
        }

        _gameManager.ResetAllCamerasClientRpc();
    }

    /// <summary>
    /// Despawns local split-screen players, resets the coin spawner's internal
    /// state, and zeroes the runner/catcher spawn-index counters. Does not
    /// despawn networked players or coins — see <see cref="DespawnAllPlayers"/>
    /// and <see cref="DespawnAllCoins"/>.
    /// </summary>
    public void ResetSpawnState()
    {
        localSpawner?.DespawnLocalPlayers();
        coinSpawner?.ResetSpawner();
        _runnerSpawnIndex = 0;
        _catcherSpawnIndex = 0;
    }

    /// <summary>Spawns the initial/refreshed coin layout via <see cref="coinSpawner"/>.</summary>
    public void SpawnCoins()
    {
        if (coinSpawner == null)
        {
            Debug.LogError("[GameSpawnSystem] CoinSpawner reference is missing!");
            return;
        }
        coinSpawner.SpawnCoins();
    }

    /// <summary>Spawns <paramref name="count"/> coins around <paramref name="position"/> via <see cref="coinSpawner"/>.</summary>
    public void SpawnCoinsAtPosition(Vector3 position, int count, float radius)
        => coinSpawner?.SpawnCoinsAtPosition(position, count, radius);
}
