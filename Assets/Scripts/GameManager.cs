using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the overall game state, including score, time, spawning of game elements,
/// and game progression (win/loss conditions). Implements the Singleton pattern.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Gets the singleton instance of the GameManager.
    /// </summary>
    public static GameManager Instance { get; private set; }

    /// <summary>
    /// Reference to the CoinSpawner script in the scene.
    /// </summary>
    public CoinSpawner coinSpawner;

    /// <summary>
    /// The current score value.
    /// </summary>
    private int score = 0;

    /// <summary>
    /// The number of power-ups to generate.
    /// </summary>
    public int numberOfPowerUpsToSpawn = 3;

    /// <summary>
    /// Array of possible spawn points for the player.
    /// </summary>
    public Transform[] playerSpawnPoints;

    /// <summary>
    /// Reference to the Player prefab to be instantiated.
    /// </summary>
    public GameObject playerPrefab;

    private GameObject currentPlayer; // Reference to the currently active player instance

    /// <summary>
    /// Reference to the Guard prefab to be instantiated.
    /// </summary>
    public GameObject guardPrefab;

    /// <summary>
    /// The number of guards to generate.
    /// </summary>
    public int numberOfGuardsToSpawn = 3;

    /// <summary>
    /// Array of possible spawn points for the guards.
    /// </summary>
    public Transform[] guardSpawnPoints;

    /// <summary>
    /// List to keep track of active guard instances.
    /// </summary>
    private List<GameObject> activeGuards = new List<GameObject>();

    /// <summary>
    /// The total duration of the game in seconds. Set to 180 seconds (3 minutes).
    /// </summary>
    public float gameDuration = 180f;

    /// <summary>
    /// Current time remaining in the game.
    /// </summary>
    private float timeRemaining;

    /// <summary>
    /// Flag to indicate if the game has ended.
    /// </summary>
    private bool gameEnded = false;

    /// <summary>
    /// The score required to win the game.
    /// </summary>
    public int scoreToWin = 10;

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Implements the Singleton pattern to ensure only one GameManager exists.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    /// <summary>
    /// Start is called before the first frame update.
    /// Initializes game elements like player, coins, power-ups, and guards.
    /// </summary>
    void Start()
    {
        timeRemaining = gameDuration;

        if (playerPrefab != null && currentPlayer == null)
        {
            SpawnPlayer();
        }

        if (coinSpawner != null)
        {
            coinSpawner.SpawnCoins();
        }
        else
        {
            Debug.LogError("Coin Spawner not assigned to GameManager!");
        }

        SpawnGuards();
    }

    /// <summary>
    /// Update is called once per frame.
    /// Manages the game timer and checks for win/loss conditions.
    /// </summary>
    void Update()
    {
        if (gameEnded) return;

        timeRemaining -= Time.deltaTime;
        if (UIManager.Instance != null) // Added check to prevent error if UIManager doesn't exist yet
        {
            UIManager.Instance.UpdateTimerUI(timeRemaining);
        }

        if (timeRemaining <= 0)
        {
            timeRemaining = 0;
            EndGame(false); // Game ends with a loss
        }

        // Win condition can be the total number of coins in the game, or if all coins have been collected
        // If you changed the scoreToWin logic to be the total number of spawned coins, use:
        // if (score >= coinSpawner.numberOfCoinsToSpawn)
        if (score >= scoreToWin) // Keeps the original scoreToWin logic
        {
            EndGame(true); // Game ends with a win
        }
    }

    /// <summary>
    /// Adds a specified amount to the player's score.
    /// </summary>
    /// <param name="amount">The amount of score to add.</param>
    public void AddScore(int amount)
    {
        score += amount;
        Debug.Log("Score: " + score);
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateScoreUI(score);
        }
    }

    /// <summary>
    /// Gets the current score.
    /// </summary>
    /// <returns>The current score.</returns>
    public int GetScore() { return score; }

    /// <summary>
    /// Gets the remaining time in the game.
    /// </summary>
    /// <returns>The time remaining in seconds.</returns>
    public float GetTimeRemaining() { return timeRemaining; }

    /// <summary>
    /// Spawns the player character at a random spawn point.
    /// </summary>
    public void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab not assigned!");
            return;
        }

        if (playerSpawnPoints.Length == 0)
        {
            Debug.LogError("No player spawn points assigned!");
            return;
        }

        // Choose a random spawn point
        int randomIndex = Random.Range(0, playerSpawnPoints.Length);
        Transform spawnPoint = playerSpawnPoints[randomIndex];

        // Destroy existing player if any
        if (currentPlayer != null)
        {
            Destroy(currentPlayer);
        }

        // Instantiate the player
        currentPlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        Debug.Log("Player spawned at: " + spawnPoint.position);

        // Once the player is spawned, tell the CameraFollow script to follow this new player.
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.Target = currentPlayer.transform;
            Debug.Log("Camera is now following the player.");
        }
        else
        {
            Debug.LogWarning("CameraFollow.Instance not found. Camera will not be configured to follow the player automatically. Make sure CameraFollow script is attached to your Main Camera and it has an active GameObject.");
        }
    }

    /// <summary>
    /// Spawns guards at their designated spawn points.
    /// </summary>
    void SpawnGuards()
    {
        if (guardPrefab == null)
        {
            Debug.LogError("Guard Prefab not assigned!");
            return;
        }

        if (guardSpawnPoints.Length == 0)
        {
            Debug.LogError("No guard spawn points assigned!");
            return;
        }

        // Clear existing guards
        foreach (GameObject guard in activeGuards)
        {
            if (guard != null)
            {
                Destroy(guard);
            }
        }
        activeGuards.Clear();

        // Spawn new guards
        for (int i = 0; i < numberOfGuardsToSpawn; i++)
        {
            if (i < guardSpawnPoints.Length) // Ensure we don't go out of bounds of spawn points array
            {
                Transform spawnPoint = guardSpawnPoints[i];
                GameObject newGuard = Instantiate(guardPrefab, spawnPoint.position, spawnPoint.rotation);
                activeGuards.Add(newGuard);
            }
            else
            {
                Debug.LogWarning("Not enough guard spawn points for all guards. Spawning fewer guards.");
                break;
            }
        }
        Debug.Log("Spawned " + activeGuards.Count + " guards.");
    }

    /// <summary>
    /// Ends the game, displaying a win or loss message and triggering a restart.
    /// </summary>
    /// <param name="won">True if the player won, false otherwise.</param>
    void EndGame(bool won)
    {
        gameEnded = true;
        Debug.Log("Game Over! You " + (won ? "Won!" : "Lost!"));
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowEndGamePanel(won);
        }
        StartCoroutine(RestartGameAfterDelay(3f));
    }

    /// <summary>
    /// Restarts the current game scene after a specified delay.
    /// </summary>
    /// <param name="delay">The delay in seconds before restarting the scene.</param>
    IEnumerator RestartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}