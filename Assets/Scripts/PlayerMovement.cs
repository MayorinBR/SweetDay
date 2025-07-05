using UnityEngine;

/// <summary>
/// Controls the movement of the player character.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    /// <summary>
    /// The movement speed of the player.
    /// </summary>
    public float moveSpeed = 5f;

    /// <summary>
    /// Update is called once per frame.
    /// Handles player input and moves the character accordingly.
    /// </summary>
    void Update()
    {
        // Capture horizontal input (A/D or Left/Right Arrow keys)
        float horizontalInput = Input.GetAxis("Horizontal");
        // Capture vertical input (W/S or Up/Down Arrow keys)
        float verticalInput = Input.GetAxis("Vertical");

        // Create a movement direction vector
        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);

        // Normalize the vector to ensure diagonal movement isn't faster
        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        // Move the character
        transform.position += movement * moveSpeed * Time.deltaTime;
    }
}