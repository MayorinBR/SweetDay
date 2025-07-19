using UnityEngine;
using System.Collections; // Necessário para Coroutines

/// <summary>
/// Controls the movement of the player character.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    /// <summary>
    /// The current movement speed of the player.
    /// </summary>
    public float moveSpeed = 5f;

    /// <summary>
    /// The initial movement speed of the player. This value will be set once at Start.
    /// </summary>
    private float baseMoveSpeed;

    /// <summary>
    /// Reference to the CharacterController component attached to this GameObject.
    /// </summary>
    private CharacterController _characterController;

    [Header("Rotation Settings")]
    /// <summary>
    /// The speed at which the player rotates to face the movement direction.
    /// </summary>
    public float rotationSpeed = 720f; // Velocidade de rotação em graus por segundo

    // --- NOVAS VARIÁVEIS PARA COLETA/SOLTURA ---
    [Header("Coin Interaction Settings")]
    /// <summary>
    /// The duration (in seconds) the player is immobilized while collecting a coin.
    /// </summary>
    public float collectionDuration = 0.5f;

    /// <summary>
    /// The distance behind the player where a dropped coin will appear.
    /// </summary>
    public float dropDistance = 2.0f;

    /// <summary>
    /// Prefab of the coin to be instantiated when the player drops one.
    /// </summary>
    public GameObject coinPrefab; // Arraste o prefab da moeda para cá no Inspector

    /// <summary>
    /// The percentage of dropDistance for random variation in dropped coin position. (e.g., 0.25 for +/- 25%)
    /// </summary>
    [Range(0f, 1f)] // Limita o valor entre 0% e 100%
    public float dropPositionRandomness = 0.50f; // Variação de +/- 5% da dropDistance

    private bool _isCollecting = false; // Flag para imobilizar o player
    private Coin _currentNearbyCoin = null; // Referência à moeda próxima que pode ser coletada
    // --- FIM DAS NOVAS VARIÁVEIS ---

    void Start()
    {
        baseMoveSpeed = moveSpeed; // Store the initial speed
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError("CharacterController not found on player GameObject. Please add one!");
        }
    }

    /// <summary>
    /// Update is called once per frame.
    /// Handles player input and moves the character accordingly using CharacterController.
    /// </summary>
    void Update()
    {
        if (_characterController == null) return;

        // Se o player estiver coletando, ele não pode se mover
        if (_isCollecting)
        {
            // Opcional: Adicionar uma animação de coleta aqui
            return; // Impede qualquer movimento ou rotação
        }

        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);

        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        // Move o personagem usando o CharacterController
        _characterController.Move(movement * moveSpeed * Time.deltaTime);

        // Lógica de Rotação
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // --- LÓGICA DE INPUT PARA COLETA E SOLTURA ---
        // Input para Coletar Moeda ("X")
        if (Input.GetKeyDown(KeyCode.X))
        {
            // Verifica se a moeda ainda existe e está ativa antes de tentar coletar
            if (_currentNearbyCoin != null && _currentNearbyCoin.gameObject.activeSelf)
            {
                StartCoroutine(CollectCoinCoroutine(_currentNearbyCoin));
            }
            else
            {
                Debug.Log("Nenhuma moeda próxima ou ativa para coletar.");
                _currentNearbyCoin = null; // Garante que a referência seja limpa se a moeda não for mais válida
            }
        }

        // Input para Soltar Moeda ("Z")
        if (Input.GetKeyDown(KeyCode.Z))
        {
            if (GameManager.Instance != null && GameManager.Instance.GetScore() > 0)
            {
                // Calcula a posição base para soltar a moeda atrás do player
                Vector3 baseDropPosition = transform.position - transform.forward * dropDistance;
                baseDropPosition.y = 0.5f; // Ajusta a altura da moeda solta

                // --- NOVO CÓDIGO PARA ALEATORIEDADE NA POSIÇÃO DE DROP ---
                float randomOffsetRange = dropDistance * dropPositionRandomness;
                float randomXOffset = Random.Range(-randomOffsetRange, randomOffsetRange);
                float randomZOffset = Random.Range(-randomOffsetRange, randomOffsetRange);

                // Aplica o offset aleatório no plano XZ
                Vector3 finalDropPosition = baseDropPosition + new Vector3(randomXOffset, 0f, randomZOffset);
                // --- FIM DO NOVO CÓDIGO ---

                // Passa a rotação desejada para a moeda solta (ex: rotação de 90 graus no X para ficar "em pé")
                Quaternion dropRotation = Quaternion.Euler(0f, 0f, 90f); // Ou use a rotação do CoinSpawner se for pública

                GameManager.Instance.ReleaseCoin(1, finalDropPosition, dropRotation, coinPrefab); // Passa a posição final e rotação
            }
            else
            {
                Debug.Log("Você não tem moedas para soltar.");
            }
        }
        // --- FIM DA LÓGICA DE INPUT ---
    }

    /// <summary>
    /// Coroutine to handle the coin collection process with a delay.
    /// </summary>
    /// <param name="coinToCollect">The Coin script of the coin to be collected.</param>
    IEnumerator CollectCoinCoroutine(Coin coinToCollect)
    {
        _isCollecting = true; // Imobiliza o player
        Debug.Log("Coletando moeda... Player parado.");

        yield return new WaitForSeconds(collectionDuration); // Espera pelo tempo de coleta

        // Verifica novamente se a moeda ainda existe e está ativa antes de coletar
        if (coinToCollect != null && coinToCollect.gameObject.activeSelf)
        {
            coinToCollect.CollectCoin(); // Chama o método de coleta da moeda
            Debug.Log("Moeda coletada!");
        }
        else
        {
            Debug.Log("Moeda desapareceu antes da coleta ser concluída ou já foi coletada.");
        }

        _isCollecting = false; // Libera o player
        _currentNearbyCoin = null; // Limpa a referência da moeda
    }

    /// <summary>
    /// Called when this collider 'trigger' enters another collider.
    /// Detects if a coin is nearby for collection.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Coin")) // Certifique-se que suas moedas têm a tag "Coin"
        {
            Coin coin = other.GetComponent<Coin>();
            if (coin != null)
            {
                _currentNearbyCoin = coin;
                Debug.Log("Moeda detectada para coleta: " + _currentNearbyCoin.name);
            }
        }
    }

    /// <summary>
    /// Called when another collider exits this trigger.
    /// Clears the reference to the nearby coin.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Coin"))
        {
            // Limpa a referência apenas se a moeda que saiu é a que estava sendo rastreada
            if (_currentNearbyCoin != null && _currentNearbyCoin.gameObject == other.gameObject)
            {
                _currentNearbyCoin = null;
                Debug.Log("Moeda saiu da área de coleta.");
            }
        }
    }

    /// <summary>
    /// Sets a new movement speed for the player.
    /// </summary>
    /// <param name="newSpeed">The new speed value.</param>
    public void SetMoveSpeed(float newSpeed)
    {
        moveSpeed = newSpeed;
        Debug.Log("Player speed set to: " + moveSpeed);
    }

    /// <summary>
    /// Resets the player's movement speed to its base value.
    /// </summary>
    public void ResetMoveSpeed()
    {
        moveSpeed = baseMoveSpeed;
        Debug.Log("Player speed reset to: " + moveSpeed);
    }
}