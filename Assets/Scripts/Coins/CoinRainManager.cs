using UnityEngine;

/// <summary>
/// Spawns decorative falling coins in screen space to create a celebratory
/// "coin rain" visual effect (e.g. on the main-menu or end-game screen).
/// Spawn rate, fall speed, scale variation, and depth range are all
/// configurable via the Inspector.
/// </summary>
public class CoinRainManager : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Spawn Settings")]
    /// <summary>Prefab instantiated for each falling coin.</summary>
    public GameObject coin3DPrefab;

    /// <summary>Interval in seconds between coin spawns.</summary>
    public float spawnRate = 0.2f;

    [Header("Movement & Variety")]
    /// <summary>Minimum fall speed in units per second.</summary>
    public float minFallSpeed = 3f;

    /// <summary>Maximum fall speed in units per second.</summary>
    public float maxFallSpeed = 8f;

    [Header("Scale (base ratio 8 : 1 : 8)")]
    /// <summary>Minimum random scale multiplier applied to the base scale.</summary>
    public float minScaleMultiplier = 0.7f;

    /// <summary>Maximum random scale multiplier applied to the base scale.</summary>
    public float maxScaleMultiplier = 1.3f;

    [Header("Depth Control")]
    /// <summary>Minimum camera-space depth at which coins are spawned.</summary>
    public float minDepth = 96f;

    /// <summary>Maximum camera-space depth at which coins are spawned.</summary>
    public float maxDepth = 104f;

    // ====================================================================
    // Private
    // ====================================================================

    private static readonly Vector3 BaseScale = new Vector3(8f, 1f, 8f);

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start()
    {
        InvokeRepeating(nameof(SpawnCoin), 0f, spawnRate);
    }

    // ====================================================================
    // Private ÅESpawning
    // ====================================================================

    /// <summary>
    /// Instantiates one coin at a random screen-space position above the visible area,
    /// assigns randomised fall speed and rotation, and adds a <see cref="CoinBehavior"/>
    /// component to drive its movement.
    /// </summary>
    private void SpawnCoin()
    {
        if (Camera.main == null || coin3DPrefab == null) return;

        float depth = Random.Range(minDepth, maxDepth);
        Vector3 screenPos = new Vector3(Random.Range(0, Screen.width), Screen.height + 100f, depth);
        Vector3 spawnPos = Camera.main.ScreenToWorldPoint(screenPos);

        Quaternion rotation = Quaternion.Euler(
            Random.Range(0f, 360f),
            Random.Range(0f, 360f),
            Random.Range(0f, 360f));

        GameObject coin = Instantiate(coin3DPrefab, spawnPos, rotation);

        float scale = Random.Range(minScaleMultiplier, maxScaleMultiplier);
        coin.transform.localScale = BaseScale * scale;

        float fallSpeed = Random.Range(minFallSpeed, maxFallSpeed);
        Vector3 rotationSpeed = new Vector3(
            Random.Range(75f, 150f),
            Random.Range(75f, 150f),
            Random.Range(75f, 150f));

        coin.AddComponent<CoinBehavior>().Setup(fallSpeed, rotationSpeed);
    }
}

/// <summary>
/// Added at runtime to each coin spawned by <see cref="CoinRainManager"/>.
/// Moves the coin downward and rotates it each frame; destroys the GameObject
/// when it falls below the visible screen area.
/// </summary>
public class CoinBehavior : MonoBehaviour
{
    // ====================================================================
    // Private
    // ====================================================================

    private float _fallSpeed;
    private Vector3 _rotationSpeed;
    private Camera _cam;

    /// <summary>
    /// Screen-space Y threshold below which the coin is destroyed.
    /// A negative margin ensures the coin is fully off-screen before destruction.
    /// </summary>
    private const float DestroyBelowScreenY = -150f;

    // ====================================================================
    // Public API
    // ====================================================================>

    /// <summary>
    /// Initialises the coin's movement parameters.
    /// </summary>
    /// <param name="speed">Downward fall speed in world units per second.</param>
    /// <param name="rotSpeed">Per-axis rotation speed in degrees per second.</param>
    public void Setup(float speed, Vector3 rotSpeed)
    {
        _fallSpeed = speed;
        _rotationSpeed = rotSpeed;
        _cam = Camera.main;
    }

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Update()
    {
        transform.Translate(Vector3.down * _fallSpeed * Time.deltaTime, Space.World);
        transform.Rotate(_rotationSpeed * Time.deltaTime);

        // Destroy once the coin is well below the bottom of the screen.
        if (_cam != null)
        {
            Vector3 screenPos = _cam.WorldToScreenPoint(transform.position);
            if (screenPos.y < DestroyBelowScreenY)
                Destroy(gameObject);
        }
    }
}
