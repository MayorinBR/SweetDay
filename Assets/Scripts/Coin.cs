using UnityEngine;

/// <summary>
/// Represents a collectible coin item in the game.
/// Handles interaction when the player collects it.
/// </summary>
public class Coin : MonoBehaviour
{
    /// <summary>
    /// The score value awarded to the player when this coin is collected.
    /// </summary>
    public int scoreValue = 1;

    /// <summary>
    /// Called when this collider 'trigger' enters another collider.
    /// Handles the collection of the coin by the player.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerEnter(Collider other)
    {
        // Checks if the colliding object is the player
        if (other.CompareTag("Player"))
        {
            // Adds score through the GameManager
            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddScore(scoreValue);
            }

            gameObject.SetActive(false); // Deactivates the coin
        }
    }
}