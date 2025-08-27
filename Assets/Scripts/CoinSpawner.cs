using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    [Header("Initial Map Spawn Settings")]
    public int numberOfCoinsToSpawn = 20;
    public Vector3 spawnAreaSize = new Vector3(40, 0, 40);

    [Header("Collision and Layers")]
    public LayerMask obstacleLayer;
    public LayerMask playerLayer;
    public LayerMask guardLayer;
    public float coinOverlapRadius = 0.5f;

    public Vector3 spawnRotationEuler = new Vector3(0f, 0f, 90f);

    private LayerMask combinedAvoidanceLayers;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        combinedAvoidanceLayers = obstacleLayer | playerLayer | guardLayer;

        if (IsServer)
        {
            SpawnCoins();
        }
    }

    public void SpawnCoins()
    {
        if (!IsServer) return;

        for (int i = 0; i < numberOfCoinsToSpawn; i++)
        {
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;
            int maxAttempts = 10;
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                Vector3 randomPosition = transform.position + new Vector3(
                    Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2),
                    0.5f,
                    Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2)
                );
                spawnPosition = randomPosition;
                if (!Physics.CheckSphere(spawnPosition, coinOverlapRadius, combinedAvoidanceLayers))
                {
                    positionFound = true;
                }
                attempts++;
            }

            if (positionFound)
            {
                Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);
                GameObject newCoin = Instantiate(coinPrefab, spawnPosition, desiredSpawnRotation);
                newCoin.GetComponent<NetworkObject>().Spawn();
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SpawnCoinsAroundPositionServerRpc(NetworkObjectReference buttonSpawnerRef, Vector3 position, int count, float radius)
    {
        if (!IsServer) return;

        SpawnCoinsAroundPosition(position, count, radius);
    }

    public void SpawnCoinsAroundPosition(Vector3 position, int count, float radius)
    {
        if (!IsServer) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;
            int maxAttempts = 10;
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                spawnPosition = position + new Vector3(randomCircle.x, 0.5f, randomCircle.y);

                if (!Physics.CheckSphere(spawnPosition, coinOverlapRadius, combinedAvoidanceLayers))
                {
                    positionFound = true;
                }
                attempts++;
            }

            if (positionFound)
            {
                Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);
                GameObject newCoin = Instantiate(coinPrefab, spawnPosition, desiredSpawnRotation);
                newCoin.GetComponent<NetworkObject>().Spawn();
            }
        }
    }

    public void SpawnSingleCoin(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        // Use a rotação de spawn configurada para todas as moedas
        Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);

        GameObject newCoin = Instantiate(coinPrefab, position, desiredSpawnRotation);
        newCoin.GetComponent<NetworkObject>().Spawn();
    }
}