using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative spawner for collectible coins.
/// Handles the initial map scatter, targeted area spawns, and
/// dropped-coin re-spawns after a player is hit.
/// </summary>
public class CoinSpawner : NetworkBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    /// <summary>Prefab for the coin object. Must have a <see cref="NetworkObject"/> component.</summary>
    public GameObject coinPrefab;

    [Header("Initial Map Spawn Settings")]
    /// <summary>Number of coins placed when the game starts.</summary>
    public int numberOfCoinsToSpawn = 20;

    /// <summary>Full extents of the rectangular area in which coins are randomly scattered.</summary>
    public Vector3 spawnAreaSize = new Vector3(40f, 0f, 40f);

    [Header("Drop Scatter Settings")]
    /// <summary>
    /// Half-angle of the fan behind the player in which dropped coins are scattered.
    /// 0 = straight behind; 90 = full hemisphere behind.
    /// </summary>
    public float dropSpreadAngle = 55f;

    /// <summary>Minimum distance from the player at which a dropped coin can land.</summary>
    public float dropMinDistance = 0.6f;

    /// <summary>Maximum distance from the player at which a dropped coin can land.</summary>
    public float dropMaxDistance = 3.5f;

    [Header("Collision and Layers")]
    /// <summary>Static obstacles that coins must not overlap at spawn time.</summary>
    public LayerMask obstacleLayer;

    /// <summary>Player objects that coins must not overlap at spawn time.</summary>
    public LayerMask playerLayer;

    /// <summary>Guard objects that coins must not overlap at spawn time.</summary>
    public LayerMask guardLayer;

    /// <summary>Floor surfaces used for height-snapping raycasts.</summary>
    public LayerMask floorLayer;

    /// <summary>Radius of the overlap sphere used for coin-placement collision checks.</summary>
    public float coinOverlapRadius = 0.5f;

    /// <summary>Euler rotation applied to every spawned coin instance.</summary>
    public Vector3 spawnRotationEuler = new Vector3(0f, 0f, 90f);

    // ====================================================================
    // Private
    // ====================================================================

    private LayerMask _avoidanceMask;
    private bool _hasSpawnedInitialCoins;

    /// <summary>Attempts confined to the fan before expanding to a full circle.</summary>
    private const int DropFanAttempts = 8;

    /// <summary>Total placement attempts per coin (fan + full-circle fallback).</summary>
    private const int DropTotalAttempts = 14;

    // ====================================================================
    // NetworkBehaviour
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _avoidanceMask = obstacleLayer | playerLayer | guardLayer;
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Scatters <see cref="numberOfCoinsToSpawn"/> coins randomly across the play area.
    /// Guarded by a one-shot flag; call <see cref="ResetSpawner"/> to allow re-spawning
    /// (e.g. after a game reset).  Server-only.
    /// </summary>
    public void SpawnCoins()
    {
        if (!IsServer) return;

        if (_hasSpawnedInitialCoins)
        {
            Debug.LogWarning("[CoinSpawner] Initial coins already spawned – skipping.");
            return;
        }

        _hasSpawnedInitialCoins = true;

        for (int i = 0; i < numberOfCoinsToSpawn; i++)
        {
            if (TryFindSpawnPosition(out Vector3 pos))
                SpawnCoin(pos);
            else
                Debug.LogWarning($"[CoinSpawner] Could not find a valid position for coin {i}.");
        }
    }

    /// <summary>
    /// Clears the one-shot flag so <see cref="SpawnCoins"/> can be called again.
    /// </summary>
    public void ResetSpawner() => _hasSpawnedInitialCoins = false;

    /// <summary>
    /// Spawns <paramref name="count"/> coins in a circle of radius <paramref name="radius"/>
    /// centred on <paramref name="centerPosition"/>.  Server-only.
    /// </summary>
    public void SpawnCoinsAtPosition(Vector3 centerPosition, int count, float radius)
    {
        if (!IsServer) return;

        for (int i = 0; i < count; i++)
        {
            Vector2 rand = Random.insideUnitCircle * radius;
            SpawnCoin(centerPosition + new Vector3(rand.x, 0.5f, rand.y));
        }
    }

    /// <summary>
    /// Spawns <paramref name="count"/> coins near <paramref name="dropPosition"/> after a
    /// player drops their carried coins.  Each coin is height-snapped to the floor layer.
    /// Server-only.
    /// </summary>
    public void SpawnDroppedCoins_Server(Vector3 dropPosition, int count, float radius)
    {
        if (!IsServer) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = Random.insideUnitSphere * radius;
            offset.y = 0f;
            Vector3 pos = dropPosition + offset;

            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, floorLayer))
                pos.y = hit.point.y + 0.5f;

            SpawnCoin(pos);
        }
    }

    /// <summary>
    /// Spawns <paramref name="count"/> coins scattered in a fan-shaped area behind the player.
    /// Each coin lands at a random angle within <see cref="dropSpreadAngle"/> degrees
    /// of the player's back direction, at a random distance between
    /// <see cref="dropMinDistance"/> and <see cref="dropMaxDistance"/>.
    /// Each position is floor-snapped via a downward raycast.
    /// Server-only.
    /// </summary>
    /// <param name="origin">World-space spawn origin (typically the player's position).</param>
    /// <param name="facing">The direction the player is currently facing.</param>
    /// <param name="count">Number of coins to spawn.</param>
    public void SpawnDroppedCoinsDirectional_Server(Vector3 origin, Vector3 facing, int count)
    {
        if (!IsServer) return;

        Vector3 behindDir = -Vector3.ProjectOnPlane(facing, Vector3.up);
        if (behindDir.sqrMagnitude < 0.001f) behindDir = Vector3.back;
        else behindDir.Normalize();

        for (int i = 0; i < count; i++)
        {
            if (TryFindDropPosition(origin, behindDir, out Vector3 pos))
                SpawnCoin(pos);
            else
                Debug.LogWarning("[CoinSpawner] Could not find a valid drop position — coin skipped.");
        }
    }

    /// <summary>
    /// Searches for a valid coin drop position within the fan behind the player.
    /// The first <see cref="DropFanAttempts"/> tries are confined to the fan sector;
    /// remaining attempts expand to a full circle around <paramref name="origin"/>.
    /// A position is valid when a floor is detected below it and no obstacle
    /// overlaps within <see cref="coinOverlapRadius"/>.
    /// </summary>
    /// <param name="origin">World-space origin of the search (player position).</param>
    /// <param name="behindDir">Normalised direction pointing behind the player.</param>
    /// <param name="result">Valid spawn position, or <see cref="Vector3.zero"/> on failure.</param>
    /// <returns><c>true</c> if a valid position was found.</returns>
    private bool TryFindDropPosition(Vector3 origin, Vector3 behindDir, out Vector3 result)
    {
        for (int i = 0; i < DropTotalAttempts; i++)
        {
            // First half: constrained to the drop fan.
            // Second half: full-circle fallback in case the fan is entirely blocked.
            float angle = i < DropFanAttempts
                ? Random.Range(-dropSpreadAngle, dropSpreadAngle)
                : Random.Range(-180f, 180f);

            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * behindDir;
            float dist = Random.Range(dropMinDistance, dropMaxDistance);
            Vector3 pos = origin + dir * dist;
            pos.y = origin.y;

            if (!Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, floorLayer))
                continue;

            pos.y = hit.point.y + 0.5f;

            if (Physics.OverlapSphere(pos, coinOverlapRadius, obstacleLayer).Length == 0)
            {
                result = pos;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Spawns a single coin at <paramref name="position"/> from a client-initiated request.
    /// The <paramref name="rotation"/> parameter is accepted for API compatibility but
    /// the spawner always applies <see cref="spawnRotationEuler"/>.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SpawnSingleCoinServerRpc(Vector3 position, Quaternion rotation)
        => SpawnCoin(position);

    // ====================================================================
    // Private Helpers
    // ====================================================================

    /// <summary>Instantiates and network-spawns a coin at <paramref name="position"/>.</summary>
    private void SpawnCoin(Vector3 position)
    {
        GameObject coin = Instantiate(coinPrefab, position, Quaternion.Euler(spawnRotationEuler));
        coin.GetComponent<NetworkObject>()?.Spawn();
    }

    /// <summary>
    /// Attempts up to 10 raycasts to find a floor position that does not overlap
    /// any obstacle, player, or guard.
    /// </summary>
    private bool TryFindSpawnPosition(out Vector3 result)
    {
        const int maxAttempts = 10;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float x = transform.position.x + Random.Range(-spawnAreaSize.x * 0.5f, spawnAreaSize.x * 0.5f);
            float z = transform.position.z + Random.Range(-spawnAreaSize.z * 0.5f, spawnAreaSize.z * 0.5f);

            var ray = new Ray(new Vector3(x, transform.position.y + 10f, z), Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, 20f, floorLayer))
            {
                Vector3 candidate = hit.point + Vector3.up * 0.5f;

                if (Physics.OverlapSphere(candidate, coinOverlapRadius, _avoidanceMask).Length == 0)
                {
                    result = candidate;
                    return true;
                }
            }
        }

        result = Vector3.zero;
        return false;
    }

    // ====================================================================
    // Editor Gizmos
    // ====================================================================

    private void OnDrawGizmos()
    {
        // Green wire box – spawn area.
        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.up * 0.5f, spawnAreaSize);

        // Orange wire sphere – per-coin overlap radius.
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, coinOverlapRadius);
    }
}