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
    /// Use this to make coins appear "standing up".
    /// </summary>
    public Vector3 spawnRotationEuler = new Vector3(0f, 0f, 90f); // Controls the rotation of the coin spawn.

    /// <summary>
    /// Optional: List to keep references to active coins, if needed for future mechanics.
    /// </summary>
    private List<GameObject> activeCoins = new List<GameObject>();

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
        // This is useful for round restarts or if you regenerate coins during the game
        foreach (GameObject coin in activeCoins)
        {
            if (coin != null) // Checks if the object still exists before trying to destroy
            {
                Destroy(coin);
            }
        }
        activeCoins.Clear(); // Clears the list after destroying

        Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);

        for (int i = 0; i < numberOfCoinsToSpawn; i++)
        {
            // Calculate a random position within the spawn area
            float randomX = Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
            float randomZ = Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2);
            Vector3 spawnPosition = new Vector3(randomX, 0, randomZ); // Fixed height for the coin

            // Instantiate the coin with the desired rotation
            GameObject newCoin = Instantiate(coinPrefab, spawnPosition, desiredSpawnRotation); // Aplicando a rotação
            activeCoins.Add(newCoin); // Add to the list of active coins
        }

        Debug.Log("Spawned " + activeCoins.Count + " coins.");
    }
}