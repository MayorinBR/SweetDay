using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the spawning of coin objects in the scene.
/// </summary>
public class CoinSpawner : MonoBehaviour
{
    /// <summary>
    /// Reference to the coin prefab to be spawned.
    /// </summary>
    public GameObject coinPrefab;

    /// <summary>
    /// The number of coins to generate.
    /// </summary>
    public int numberOfCoinsToSpawn = 20;

    /// <summary>
    /// The size of the area within which coins will be spawned.
    /// </summary>
    public Vector3 spawnAreaSize = new Vector3(40, 0, 40);

    /// <summary>
    /// The rotation (in Euler angles) that the spawned coins will have.
    /// Use this to make coins appear \"standing up\".
    /// </summary>
    public Vector3 spawnRotationEuler = new Vector3(0f, 0f, 90f); // Controls the rotation of the coin spawn.

    /// <summary>
    /// Optional: List to keep references to active coins, if needed for future mechanics.
    /// </summary>
    private List<GameObject> activeCoins = new List<GameObject>();

    /// <summary>
    /// The LayerMask that defines what layers are considered obstacles.
    /// Assign the "Obstacle" layer in the Inspector.
    /// </summary>
    public LayerMask obstacleLayer; // Atribua a camada "Obstacle" no Inspector!

    /// <summary>
    /// The LayerMask that defines what layers are considered coins.
    /// Assign the "Coin" layer in the Inspector.
    /// </summary>
    public LayerMask coinLayer; // NOVO: Atribua a camada "Coin" no Inspector!

    /// <summary>
    /// The radius to check for obstacles and other coins around the spawn point.
    /// This should be slightly larger than the coin's collider radius.
    /// </summary>
    public float coinOverlapRadius = 0.5f; // Ajuste este valor, ligeiramente maior que o raio da moeda

    /// <summary>
    /// Spawns coins randomly in the scene within the defined area.
    /// </summary>
    public void SpawnCoins()
    {
        if (coinPrefab == null)
        {
            Debug.LogError("Coin Prefab not assigned to CoinSpawner!");
            return;
        }

        // Clears existing coins before spawning new ones, if any
        foreach (GameObject coin in activeCoins)
        {
            if (coin != null)
            {
                Destroy(coin);
            }
        }
        activeCoins.Clear();

        Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);

        // Combine as camadas de obstáculo e moeda para a verificação de sobreposição
        LayerMask combinedObstacleAndCoinLayers = obstacleLayer | coinLayer;

        for (int i = 0; i < numberOfCoinsToSpawn; i++)
        {
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;
            int maxAttempts = 10; // Evita loop infinito se não houver espaço suficiente
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                // Calculate a random position within the spawn area
                float randomX = Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
                float randomZ = Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2);
                spawnPosition = new Vector3(randomX, 0.5f, randomZ); // Altura fixa para a moeda (ajuste se necessário)

                // Verifica se a posição proposta colide com um obstáculo OU outra moeda
                // QueryTriggerInteraction.Collide garante que ele detecte colliders marcados como Is Trigger
                if (!Physics.CheckSphere(spawnPosition, coinOverlapRadius, combinedObstacleAndCoinLayers, QueryTriggerInteraction.Collide))
                {
                    positionFound = true; // Posição válida encontrada
                }
                attempts++;
            }

            if (positionFound)
            {
                GameObject newCoin = Instantiate(coinPrefab, spawnPosition, desiredSpawnRotation);
                activeCoins.Add(newCoin);
            }
            else
            {
                Debug.LogWarning("Could not find a safe spawn position for coin after " + maxAttempts + " attempts.");
            }
        }

        Debug.Log("Spawned " + activeCoins.Count + " coins.");
    }

    /// <summary>
    /// Spawns a specified number of coins around a given position.
    /// </summary>
    /// <param name="position">The central position around which to spawn coins.</param>
    /// <param name="count">The number of coins to spawn.</param>
    /// <param name="radius">The radius around the position within which coins will be spawned.</param>
    public void SpawnCoinsAtLocation(Vector3 position, int count, float radius = 2f)
    {
        if (coinPrefab == null)
        {
            Debug.LogError("Coin Prefab not assigned to CoinSpawner!");
            return;
        }

        Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);

        // Combine as camadas de obstáculo e moeda para a verificação de sobreposição
        LayerMask combinedObstacleAndCoinLayers = obstacleLayer | coinLayer;

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;
            int maxAttempts = 10;
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                // Certifique-se de que a altura de spawn é consistente com a do seu prefab de moeda
                spawnPosition = position + new Vector3(randomCircle.x, 0.5f, randomCircle.y);

                // Verifica se a posição proposta colide com um obstáculo OU outra moeda
                // QueryTriggerInteraction.Collide garante que ele detecte colliders marcados como Is Trigger
                if (!Physics.CheckSphere(spawnPosition, coinOverlapRadius, combinedObstacleAndCoinLayers, QueryTriggerInteraction.Collide))
                {
                    positionFound = true;
                }
                attempts++;
            }

            if (positionFound)
            {
                GameObject newCoin = Instantiate(coinPrefab, spawnPosition, desiredSpawnRotation);
                activeCoins.Add(newCoin);
            }
            else
            {
                Debug.LogWarning("Could not find a safe spawn position for coin at location after " + maxAttempts + " attempts.");
            }
        }
        Debug.Log("Spawned " + activeCoins.Count + " coins around " + position + ".");
    }
}