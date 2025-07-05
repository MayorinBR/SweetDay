using UnityEngine;

/// <summary>
/// Manages the overall game state, including score and singleton access.
/// Implements the Singleton pattern to ensure only one instance exists.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Gets the singleton instance of the GameManager.
    /// </summary>
    public static GameManager Instance { get; private set; }

    private int score = 0; // Current score

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Implements the Singleton pattern to ensure only one GameManager exists.
    /// </summary>
    void Awake()
    {
        // Implements the Singleton pattern to ensure only one instance of the GameManager exists
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Optional: keeps the GameManager between scenes
        }
    }

    /// <summary>
    /// Adds the specified amount to the current score.
    /// </summary>
    /// <param name="amount">The amount of score to add.</param>
    public void AddScore(int amount)
    {
        score += amount;
        Debug.Log("Score: " + score); // For now, display in the console

        // TODO: Update score UI here
    }

    /// <summary>
    /// Gets the current score.
    /// </summary>
    /// <returns>The current score.</returns>
    public int GetScore()
    {
        return score;
    }
}