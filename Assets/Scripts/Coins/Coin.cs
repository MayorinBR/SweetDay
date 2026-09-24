using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Represents a collectible coin in the game world.
/// Rotates continuously on the world Y-axis as a visual indicator that the coin
/// is available for collection.
/// </summary>
public class Coin : NetworkBehaviour
{
    /// <summary>Score awarded to the runners team when this coin is collected.</summary>
    public int scoreValue = 1;

    /// <summary>Rotation speed in degrees per second around the world Y-axis.</summary>
    public float rotationSpeed = 100f;

    private void Update()
    {
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
    }
}
