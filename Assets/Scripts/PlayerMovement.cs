using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f; // Velocidade de movimentação do jogador

    // Update é chamado uma vez por frame
    void Update()
    {
        // Captura a entrada horizontal (A/D ou Setas Esquerda/Direita)
        float horizontalInput = Input.GetAxis("Horizontal");
        // Captura a entrada vertical (W/S ou Setas Cima/Baixo)
        float verticalInput = Input.GetAxis("Vertical");

        // Cria um vetor de direção de movimento
        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);

        // Normaliza o vetor para garantir que a movimentação diagonal não seja mais rápida
        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        // Move o personagem
        transform.position += movement * moveSpeed * Time.deltaTime;
    }
}