using UnityEngine;

/// <summary>
/// Represents a guard character in the game.
/// Handles interactions when the guard "catches" the player.
/// </summary>
public class Guard : MonoBehaviour
{
    /// <summary>
    /// Called when this collider 'trigger' enters another collider.
    /// Detects if the player has been caught by the guard.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerEnter(Collider other)
    {
        // Ensure the guard's collider is marked as Is Trigger
        // Or use OnCollisionEnter if you prefer physical collisions
        if (other.CompareTag("Player"))
            // Encontramos um guarda, então o jogador perde uma vida
            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoseLife();
            }
            else
            {
                Debug.LogError("GameManager Instance is null!");
            }
    }
}