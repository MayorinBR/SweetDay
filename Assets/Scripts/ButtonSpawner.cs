using UnityEngine;
using UnityEngine.UI; // Certifique-se de que este using esteja presente

/// <summary>
/// Defines a safe area where the player can stay for a set time to earn coins.
/// </summary>
public class ButtonSpawner : MonoBehaviour
{
    [Header("Coin Spawner Button Settings")]
    /// <summary>
    /// The time (in seconds) the player needs to stay in the zone to trigger coin spawn.
    /// </summary>
    public float timeToStayInZone = 5f;

    /// <summary>
    /// The number of coins to spawn when the time is complete.
    /// </summary>
    public int coinsToSpawn = 5;

    /// <summary>
    /// The radius around the zone's center where coins will be spawned.
    /// </summary>
    public float coinSpawnRadius = 3f;

    [Header("UI Settings")]
    /// <summary>
    /// Prefab for the progress bar UI that appears over the player.
    /// This should be a Canvas with a Slider component.
    /// </summary>
    public GameObject progressBarUIPrefab;

    /// <summary>
    /// Offset for the progress bar UI position relative to the player.
    /// </summary>
    public Vector3 progressBarOffset = new Vector3(0, 2f, 0);

    private float _currentStayTime = 0f;
    private bool _playerInZone = false;
    private GameObject _playerGameObject; // Store reference to the player
    private Slider _currentProgressBarInstance; // Reference to the instantiated progress bar UI
    private CoinSpawner _coinSpawner; // Reference to the CoinSpawner script

    void Start()
    {
        // Get reference to the CoinSpawner in the scene
        _coinSpawner = Object.FindFirstObjectByType<CoinSpawner>();
        if (_coinSpawner == null)
        {
            Debug.LogError("CoinSpawner not found in the scene! Please add a CoinSpawner GameObject.");
        }

        // Ensure the trigger is set up
        Collider collider = GetComponent<Collider>();
        if (collider == null || !collider.isTrigger)
        {
            Debug.LogWarning("ButtonSpawner needs a Collider marked as 'Is Trigger' to function.");
        }
    }

    /// <summary>
    /// Called when another collider enters this trigger.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerInZone = true;
            _playerGameObject = other.gameObject;
            _currentStayTime = 0f; // Reset time when player enters
            Debug.Log("Player entered safe zone. Timer started.");
            ShowProgressBar(_playerGameObject.transform);
        }
    }

    /// <summary>
    /// Called once per frame while another collider is staying in this trigger.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && _playerInZone)
        {
            _currentStayTime += Time.deltaTime;
            UpdateProgressBar(_currentStayTime / timeToStayInZone);

            if (_currentStayTime >= timeToStayInZone)
            {
                Debug.Log("Player stayed long enough! Spawning coins.");
                SpawnCoinsNearArea();
                _currentStayTime = 0f; // Reset for next cycle or disable zone
                _playerInZone = false; // Player effectively 'completes' the cycle
                HideProgressBar(); // Hide and destroy the bar after completion
                // Optionally, you might want to disable this safe zone temporarily
                gameObject.SetActive(false);
                Invoke("ReactivateSafeZone", 10f); // Example: Reactivate after 10 seconds
            }
        }
    }

    /// <summary>
    /// Called when another collider exits this trigger.
    /// </summary>
    /// <param name="other">The other Collider involved in this collision.</param>
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            _playerInZone = false;
            _currentStayTime = 0f; // Reset time when player leaves
            Debug.Log("Player exited safe zone. Timer reset.");
            HideProgressBar(); // Hide and destroy the bar if player leaves
        }
    }

    /// <summary>
    /// Spawns coins around the zone's position using the CoinSpawner.
    /// </summary>
    private void SpawnCoinsNearArea()
    {
        CoinSpawner spawner = Object.FindFirstObjectByType<CoinSpawner>(); // Find the CoinSpawner in the scene
        if (spawner != null)
        {
            spawner.SpawnCoinsAtLocation(transform.position, coinsToSpawn, coinSpawnRadius);
        }
        else
        {
            Debug.LogError("CoinSpawner not found in the scene! Cannot spawn coins.");
        }
    }

    /// <summary>
    /// Shows and initializes the progress bar UI over the player.
    /// </summary>
    /// <param name="playerTransform">The transform of the player GameObject.</param>
    private void ShowProgressBar(Transform playerTransform)
    {
        if (progressBarUIPrefab == null)
        {
            Debug.LogWarning("Progress Bar UI Prefab not assigned to SafeZone!");
            return;
        }

        // --- CORREÇÃO 1: Garante que apenas uma barra exista ---
        // Se já existe uma instância da barra, destrua-a antes de criar uma nova.
        // Isso evita múltiplas barras.
        if (_currentProgressBarInstance != null)
        {
            Destroy(_currentProgressBarInstance.gameObject);
            _currentProgressBarInstance = null; // Limpa a referência
        }
        // --- FIM DA CORREÇÃO 1 ---

        // Instantiate the progress bar and parent it to the player
        GameObject progressBarGO = Instantiate(progressBarUIPrefab, playerTransform);
        progressBarGO.transform.localPosition = progressBarOffset; // Position relative to player

        // --- CORREÇÃO 2: Fixa a rotação da barra de progresso ---
        // Define a rotação local da barra para Quaternion.identity (sem rotação)
        // Isso fará com que ela não gire com o player.
        progressBarGO.transform.localRotation = Quaternion.identity;
        // --- FIM DA CORREÇÃO 2 ---

        _currentProgressBarInstance = progressBarGO.GetComponentInChildren<Slider>(); // Get the Slider component

        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.gameObject.SetActive(true);
            _currentProgressBarInstance.minValue = 0;
            _currentProgressBarInstance.maxValue = 1;
            _currentProgressBarInstance.value = 0;
        }
        else
        {
            Debug.LogError("No Slider component found in ProgressBar UI Prefab or its children!");
        }
    }

    /// <summary>
    /// Updates the value of the progress bar.
    /// </summary>
    /// <param name="progress">The progress value (0 to 1).</param>
    private void UpdateProgressBar(float progress)
    {
        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.value = progress;
        }
    }

    /// <summary>
    /// Hides and destroys the progress bar UI.
    /// </summary>
    private void HideProgressBar()
    {
        if (_currentProgressBarInstance != null)
        {
            Destroy(_currentProgressBarInstance.gameObject);
            _currentProgressBarInstance = null;
        }
    }

    /// <summary>
    /// Reactivate the zone after a delay
    /// </summary>
    private void ReactivateSafeZone()
    {
        gameObject.SetActive(true);
        Debug.Log("Safe zone reactivated.");
    }
}