using UnityEngine;

public class CoinRainManager : MonoBehaviour
{
    [Header("Configurações de Spawn")]
    public GameObject coin3DPrefab;
    public float spawnRate = 0.2f;

    [Header("Movimentação e Variedade")]
    public float minFallSpeed = 3f;
    public float maxFallSpeed = 8f;

    [Header("Escala (Proporção Original: 8, 1, 8)")]
    public float minScaleMultiplier = 0.7f;
    public float maxScaleMultiplier = 1.3f;

    [Header("Controle de Profundidade")]
    public float minDepth = 96f;
    public float maxDepth = 104f;

    private Vector3 originalScale = new Vector3(8f, 1f, 8f);

    void Start()
    {
        InvokeRepeating("SpawnCoin", 0f, spawnRate);
    }

    void SpawnCoin()
    {
        // 1. Profundidade e Posição (Spawn acima da visão da câmera)
        float randomDepth = Random.Range(minDepth, maxDepth);
        Vector3 screenPos = new Vector3(Random.Range(0, Screen.width), Screen.height + 100, randomDepth);
        Vector3 spawnPos = Camera.main.ScreenToWorldPoint(screenPos);

        // 2. Rotação inicial aleatória
        Quaternion randomRotation = Quaternion.Euler(Random.Range(0, 360), Random.Range(0, 360), Random.Range(0, 360));

        GameObject coin = Instantiate(coin3DPrefab, spawnPos, randomRotation);

        // 3. Aplicação da Escala Aleatória mantendo a proporção
        float randomScale = Random.Range(minScaleMultiplier, maxScaleMultiplier);
        coin.transform.localScale = originalScale * randomScale;

        // 4. Configura o comportamento individual
        float randomSpeed = Random.Range(minFallSpeed, maxFallSpeed);
        Vector3 randomRotationSpeed = new Vector3(Random.Range(75, 150), Random.Range(75, 150), Random.Range(75, 150));

        coin.AddComponent<CoinBehavior>().Setup(randomSpeed, randomRotationSpeed);
    }
}

public class CoinBehavior : MonoBehaviour
{
    private float fallSpeed;
    private Vector3 rotationSpeed;
    private Camera cam;

    public void Setup(float speed, Vector3 rotSpeed)
    {
        fallSpeed = speed;
        rotationSpeed = rotSpeed;
        cam = Camera.main;
    }

    void Update()
    {
        // Movimento de queda
        transform.Translate(Vector3.down * fallSpeed * Time.deltaTime, Space.World);

        // Rotação visual
        transform.Rotate(rotationSpeed * Time.deltaTime);

        // Despawn: Destrói quando a posição de tela estiver bem abaixo da borda inferior
        Vector3 screenPos = cam.WorldToScreenPoint(transform.position);
        if (screenPos.y < -150) // Margem de segurança para sumir totalmente
        {
            Destroy(gameObject);
        }
    }
}