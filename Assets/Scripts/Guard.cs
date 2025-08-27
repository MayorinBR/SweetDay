using UnityEngine;
using Unity.Netcode;

public class Guard : NetworkBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // Apenas o servidor processa a colisão
        if (!IsServer) return;

        if (other.CompareTag("Player"))
        {
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                // Chama a função RPC no servidor para perder uma vida
                gameManager.LoseLifeServerRpc();
            }
        }
    }
}