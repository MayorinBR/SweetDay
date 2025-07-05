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
    /// The speed at which the coin rotates around its Y-axis.
    /// </summary>
    public float rotationSpeed = 100f;

    /// <summary>
    /// Called once per frame.
    /// Handles the continuous rotation of the coin.
    /// </summary>
    void Update()
    {
        // Rotate the coin around its local Y-axis
        // Time.deltaTime ensures the rotation speed is frame-rate independent.
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0, Space.World);
    }

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