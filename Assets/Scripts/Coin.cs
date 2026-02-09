using UnityEngine;
using Unity.Netcode;
public class Coin : NetworkBehaviour
{
    public int scoreValue = 1;
    public float rotationSpeed = 100f;

    void Update()
    {
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0, Space.World);
    }
}