using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the overall game state, including score, time, spawning of game elements,
/// and game progression (win/loss conditions). Implements the Singleton pattern.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Gets the singleton instance of the GameManager.
    /// </summary>
    public static GameManager Instance { get; private set; }

    /// <summary>
    /// Reference to the CoinSpawner script in the scene.
    /// </summary>
    public CoinSpawner coinSpawner;

    /// <summary>
    /// The current score value.
    /// </summary>
    private int score = 0;

    /// <summary>
    /// Array of possible spawn points for the player.
    /// </summary>
    public Transform[] playerSpawnPoints;

    /// <summary>
    /// Reference to the Player prefab to be instantiated.
    /// </summary>
    public GameObject playerPrefab;

    /// <summary>
    /// Reference to the currently active player instance
    /// </summary>
    private GameObject currentPlayer;

    /// <summary>
    /// Reference to the Guard prefab to be instantiated.
    /// </summary>
    public GameObject guardPrefab;

    /// <summary>
    /// The initial amount of lives that the players have.
    /// </summary>
    public int startingLives = 3;

    /// <summary>
    /// The current amount of lives that the players have.
    /// </summary>
    private int currentLives;

    /// <summary>
    /// Game over panel from scene.
    /// </summary>
    public GameObject gameOverPanel;

    /// <summary>
    /// The number of guards to generate.
    /// </summary>
    public int numberOfGuardsToSpawn = 3;

    /// <summary>
    /// Array of possible spawn points for the guards.
    /// </summary>
    public Transform[] guardSpawnPoints;

    /// <summary>
    /// List to keep track of active guard instances.
    /// </summary>
    private List<GameObject> activeGuards = new List<GameObject>();

    /// <summary>
    /// The total duration of the game in seconds. Set to 180 seconds (3 minutes).
    /// </summary>
    public float gameDuration = 180f;

    /// <summary>
    /// Current time remaining in the game.
    /// </summary>
    private float timeRemaining;

    /// <summary>
    /// Flag to indicate if the game has ended.
    /// </summary>
    private bool gameEnded = false;

    /// <summary>
    /// The score required to win the game.
    /// </summary>
    public int scoreToWin = 10;

    /// <summary>
    /// How many coins the player needs to collect before their speed is reduced.
    /// </summary>
    public int coinsPerSpeedReduction = 1;

    /// <summary>
    /// The percentage by which the player's speed is reduced. (e.g., 0.1 for 10%)
    /// </summary>
    [Range(0.01f, 0.5f)] // Limita o valor entre 1% e 50% para evitar reduções muito drásticas
    public float speedReductionPercentage = 0.08f;

    // --- VARIÁVEIS DA MOCHILA ---
    [Header("Backpack Growth Settings")]
    /// <summary>
    /// Reference to the backpack GameObject child of the player.
    /// This should be assigned in the Player prefab.
    /// </summary>
    public Transform playerBackpack; // Referência à mochila do player

    /// <summary>
    /// Number of coins required to advance one stage of backpack growth.
    /// </summary>
    public int coinsPerBackpackStage = 1; // Exemplo: A cada 3 moedas, a mochila cresce

    /// <summary>
    /// Array of scales for each backpack growth stage.
    /// Stage 0: Initial scale (0.5, 0.3, 0.5)
    /// Stage 1: ...
    /// Stage 4: Final scale (1.5, 0.8, 1)
    /// </summary>
    public Vector3[] backpackScales = new Vector3[11];

    private int currentBackpackStage = 0;
    // --- FIM DAS VARIÁVEIS DA MOCHILA ---

    // --- NOVA VARIÁVEL PARA VELOCIDADE BASE DO PLAYER ---
    private float _playerBaseMoveSpeed;
    // --- FIM DA NOVA VARIÁVEL ---

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Implements the Singleton pattern to ensure only one GameManager exists.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        // Inicializa as escalas da mochila com valores padrão se não forem definidas no Inspector
        if (backpackScales[0] == Vector3.zero) // Verifica se o primeiro elemento é zero, indicando não inicializado
        {
            backpackScales[0] = new Vector3(0.5f, 0.3f, 0.5f);
            backpackScales[1] = new Vector3(0.55f, 0.35f, 0.55f);
            backpackScales[2] = new Vector3(0.6f, 0.4f, 0.6f);
            backpackScales[3] = new Vector3(0.65f, 0.45f, 0.65f);
            backpackScales[4] = new Vector3(0.7f, 0.5f, 0.7f);
            backpackScales[5] = new Vector3(0.75f, 0.55f, 0.75f);
            backpackScales[6] = new Vector3(0.8f, 0.6f, 0.8f);
            backpackScales[7] = new Vector3(0.85f, 0.65f, 0.85f);
            backpackScales[8] = new Vector3(0.9f, 0.7f, 0.9f);
            backpackScales[8] = new Vector3(0.95f, 0.75f, 0.95f);
            backpackScales[9] = new Vector3(1f, 0.8f, 1.0f);
        }
    }

    /// <summary>
    /// Start is called before the first frame update.
    /// Initializes game elements like player, coins, power-ups, and guards.
    /// </summary>
    void Start()
    {
        timeRemaining = gameDuration;
        currentLives = startingLives;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateTimerUI(timeRemaining);
            UIManager.Instance.UpdateLivesUI(currentLives); // Chame o método para atualizar as vidas na UI
        }
        if (playerPrefab != null && currentPlayer == null)
        {
            SpawnPlayer();
        }

        if (coinSpawner != null)
        {
            coinSpawner.SpawnCoins();
        }
        else
        {
            Debug.LogError("Coin Spawner not assigned to GameManager!");
        }

        currentBackpackStage = 0; // Reseta o estágio da mochila no início do jogo
        // UpdatePlayerStats() será chamado dentro de SpawnPlayer() para garantir que tudo esteja configurado após o spawn
        SpawnGuards();
    }

    /// <summary>
    /// Update is called once per frame.
    /// Manages the game timer and checks for win/loss conditions.
    /// </summary>
    void Update()
    {
        if (gameEnded) return;

        timeRemaining -= Time.deltaTime;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateTimerUI(timeRemaining);
        }

        if (timeRemaining <= 0)
        {
            timeRemaining = 0;
            EndGame(false); // Game ends with a loss
        }

        if (score >= scoreToWin) // Keeps the original scoreToWin logic
        {
            EndGame(true); // Game ends with a win
        }
    }

    /// <summary>
    /// Adds a specified amount to the player's score.
    /// </summary>
    /// <param name="amount">The amount of score to add.</param>
    public void AddScore(int amount)
    {
        score += amount;
        Debug.Log("Score: " + score);
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateScoreUI(score);
        }
        UpdatePlayerStats(); // Atualiza velocidade e mochila
    }

    /// <summary>
    /// Reduces the player's score and updates speed/backpack.
    /// </summary>
    /// <param name="amount">The amount of score to reduce.</param>
    /// <param name="dropPosition">The position where the coin should be instantiated.</param>
    /// <param name="dropRotation">The rotation for the instantiated coin.</param> // NOVO PARÂMETRO
    /// <param name="coinPrefabToDrop">The coin prefab to instantiate.</param>
    public void ReleaseCoin(int amount, Vector3 dropPosition, Quaternion dropRotation, GameObject coinPrefabToDrop) // NOVO PARÂMETRO
    {
        score -= amount;
        score = Mathf.Max(0, score); // Garante que a pontuação não seja negativa
        Debug.Log("Score: " + score + " (Moeda solta)");
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateScoreUI(score);
        }
        UpdatePlayerStats(); // Atualiza velocidade e mochila

        // Instancia a moeda solta com a rotação fornecida
        if (coinPrefabToDrop != null)
        {
            Instantiate(coinPrefabToDrop, dropPosition, dropRotation); // Usando dropRotation
            Debug.Log("Moeda solta em: " + dropPosition);
        }
        else
        {
            Debug.LogWarning("Coin Prefab not assigned in PlayerMovement to drop a coin!");
        }
    }

    /// <summary>
    /// Updates the player's speed and backpack scale based on the current score.
    /// </summary>
    private void UpdatePlayerStats()
    {
        if (currentPlayer == null) return;

        // --- Lógica de Velocidade ---
        PlayerMovement playerMovement = currentPlayer.GetComponent<PlayerMovement>();
        if (playerMovement != null)
        {
            // Usa a velocidade base armazenada no GameManager
            float baseSpeed = _playerBaseMoveSpeed;

            // Calcula o estágio de redução de velocidade baseado na pontuação
            int speedReductionStage = score / coinsPerSpeedReduction;
            // Garante que o estágio não exceda um limite razoável para a velocidade não ficar negativa
            // A velocidade mínima é 1f, então o máximo de redução é (baseSpeed - 1f) / (baseSpeed * speedReductionPercentage)
            float maxPossibleReductionStages = (baseSpeed - 1f) / (baseSpeed * speedReductionPercentage);
            speedReductionStage = Mathf.Min(speedReductionStage, Mathf.FloorToInt(maxPossibleReductionStages));

            float newSpeed = baseSpeed * (1f - (speedReductionStage * speedReductionPercentage));
            // Garante que a velocidade não seja menor que um valor mínimo (ex: 1f)
            newSpeed = Mathf.Max(newSpeed, 1f); // Define uma velocidade mínima para o player não parar completamente
            playerMovement.SetMoveSpeed(newSpeed);
        }

        // --- Lógica de crescimento/diminuição da mochila ---
        if (playerBackpack != null)
        {
            int newBackpackStage = score / coinsPerBackpackStage;
            // Garante que o estágio não exceda os limites do array de escalas
            newBackpackStage = Mathf.Clamp(newBackpackStage, 0, backpackScales.Length - 1);

            if (newBackpackStage != currentBackpackStage)
            {
                currentBackpackStage = newBackpackStage;
                playerBackpack.localScale = backpackScales[currentBackpackStage];
                Debug.Log("Mochila ajustada para o estágio: " + currentBackpackStage + " com escala: " + backpackScales[currentBackpackStage]);
            }
        }
        else
        {
            Debug.LogWarning("Player Backpack Transform not assigned in GameManager!");
        }
    }


    /// <summary>
    /// Gets the current score.
    /// </summary>
    /// <returns>The current score.</returns>
    public int GetScore() { return score; }

    /// <summary>
    /// Gets the remaining time in the game.
    /// </summary>
    /// <returns>The time remaining in seconds.</returns>
    public float GetTimeRemaining() { return timeRemaining; }

    /// <summary>
    /// Spawns the player character at a random spawn point.
    /// </summary>
    public void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player Prefab not assigned!");
            return;
        }

        if (playerSpawnPoints.Length == 0)
        {
            Debug.LogError("No player spawn points assigned!");
            return;
        }

        // Choose a random spawn point
        int randomIndex = Random.Range(0, playerSpawnPoints.Length);
        Transform spawnPoint = playerSpawnPoints[randomIndex];

        // Destroy existing player if any
        if (currentPlayer != null)
        {
            Destroy(currentPlayer);
        }

        // Instantiate the player
        currentPlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        Debug.Log("Player spawned at: " + spawnPoint.position);

        // --- NOVO CÓDIGO PARA PEGAR A VELOCIDADE BASE DO PLAYER E ATRIBUIR A MOCHILA ---
        PlayerMovement pm = currentPlayer.GetComponent<PlayerMovement>();
        if (pm != null)
        {
            _playerBaseMoveSpeed = pm.moveSpeed; // Armazena a velocidade inicial do player
        }
        else
        {
            Debug.LogError("PlayerMovement script not found on player prefab!");
        }

        // Encontra a mochila como um filho do player recém-instanciado
        playerBackpack = currentPlayer.transform.Find("PlayerBackpack"); // Certifique-se que o nome é exato
        if (playerBackpack == null)
        {
            Debug.LogWarning("PlayerBackpack child GameObject not found on player prefab! Backpack growth will not work.");
        }
        // --- FIM DO NOVO CÓDIGO ---

        // Once the player is spawned, tell the CameraFollow script to follow this new player.
        if (CameraFollow.Instance != null)
        {
            CameraFollow.Instance.Target = currentPlayer.transform;
            Debug.Log("Camera is now following the player.");
        }
        else
        {
            Debug.LogWarning("CameraFollow.Instance not found. Camera will not be configured to follow the player automatically. Make sure CameraFollow script is attached to your Main Camera and it has an active GameObject.");
        }

        // Garante que a velocidade e o tamanho da mochila sejam atualizados para o estado inicial
        UpdatePlayerStats();
    }

    /// <summary>
    /// Spawns guards at their designated spawn points.
    /// </summary>
    void SpawnGuards()
    {
        if (guardPrefab == null)
        {
            Debug.LogError("Guard Prefab not assigned!");
            return;
        }

        if (guardSpawnPoints.Length == 0)
        {
            Debug.LogError("No guard spawn points assigned!");
            return;
        }

        // Clear existing guards
        foreach (GameObject guard in activeGuards)
        {
            if (guard != null)
            {
                Destroy(guard);
            }
        }
        activeGuards.Clear();

        // Spawn new guards
        for (int i = 0; i < numberOfGuardsToSpawn; i++)
        {
            if (i < guardSpawnPoints.Length) // Ensure we don't go out of bounds of spawn points array
            {
                Transform spawnPoint = guardSpawnPoints[i];
                GameObject newGuard = Instantiate(guardPrefab, spawnPoint.position, spawnPoint.rotation);
                activeGuards.Add(newGuard);
            }
            else
            {
                Debug.LogWarning("Not enough guard spawn points for all guards. Spawning fewer guards.");
                break;
            }
        }
        Debug.Log("Spawned " + activeGuards.Count + " guards.");
    }

    /// <summary>
    /// Reduce life points metho
    /// </summary>
    public void LoseLife()
    {
        if (!gameEnded)
        {
            currentLives--;
            Debug.Log("Vida perdida! Vidas restantes: " + currentLives);
            if (UIManager.Instance != null)
            {
                UIManager.Instance.UpdateLivesUI(currentLives);
            }

            if (currentLives <= 0)
            {
                Debug.Log("Game Over!");
                EndGame(false);
            }
            else
            {
                // Opcional: Adicione aqui alguma lógica como o jogador ficar invulnerável por um curto período após perder uma vida
                SpawnPlayer(); // Respawn do jogador após perder uma vida
                // Ao respawnar o player, a mochila e a velocidade serão resetadas via UpdatePlayerStats()
                SpawnGuards(); // Respawn dos guardas para manter o desafio
            }
        }
    }

    /// <summary>
    /// Ends the game, displaying a win or loss message and triggering a restart.
    /// </summary>
    /// <param name="won">True if the player won, false otherwise.</param>
    void EndGame(bool won)
    {
        gameEnded = true;
        Debug.Log("Game Over! You " + (won ? "Won!" : "Lost!"));
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowEndGamePanel(won);
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true); // Ativa o painel de Game Over
            }
        }
        StartCoroutine(RestartGameAfterDelay(3f));
    }

    /// <summary>
    /// Restarts the current game scene after a specified delay.
    /// </summary>
    /// <param name="delay">The delay in seconds before restarting the scene.</param>
    IEnumerator RestartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}