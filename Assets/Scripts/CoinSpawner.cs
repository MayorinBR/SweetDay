using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class CoinSpawner : NetworkBehaviour
{
    public GameObject coinPrefab;

    [Header("Initial Map Spawn Settings")]
    public int numberOfCoinsToSpawn = 20;
    public Vector3 spawnAreaSize = new Vector3(40, 0, 40);

    [Header("Collision and Layers")]
    public LayerMask obstacleLayer;
    public LayerMask playerLayer;
    public LayerMask guardLayer;
    public LayerMask floorLayer;
    public float coinOverlapRadius = 0.5f;

    public Vector3 spawnRotationEuler = new Vector3(0f, 0f, 90f);

    private LayerMask combinedAvoidanceLayers;

    private bool hasSpawnedInitialCoins = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        combinedAvoidanceLayers = obstacleLayer | playerLayer | guardLayer;

        // O GameManager agora é responsável por chamar SpawnCoins()
    }

    // Método para spawn inicial no mapa
    public void SpawnCoins()
    {
        if (!IsServer) return;

        if (hasSpawnedInitialCoins)
        {
            Debug.LogWarning("Initial coins already spawned, skipping");
            return;
        }

        //Debug.Log($"Spawning {numberOfCoinsToSpawn} coins");
        hasSpawnedInitialCoins = true;

        for (int i = 0; i < numberOfCoinsToSpawn; i++)
        {
            Vector3 spawnPosition = Vector3.zero;
            bool positionFound = false;
            int maxAttempts = 10; // Reduzir tentativas para performance
            int attempts = 0;

            while (!positionFound && attempts < maxAttempts)
            {
                attempts++;

                // Calcula uma posição aleatória dentro da área de spawn
                float x = transform.position.x + Random.Range(-spawnAreaSize.x / 2, spawnAreaSize.x / 2);
                float z = transform.position.z + Random.Range(-spawnAreaSize.z / 2, spawnAreaSize.z / 2);

                // Encontra a altura do chão
                Ray ray = new Ray(new Vector3(x, transform.position.y + 10f, z), Vector3.down);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit, 20f, floorLayer))
                {
                    spawnPosition = hit.point + Vector3.up * 0.5f; // Elevar um pouco

                    // Verificação simplificada de colisão
                    Collider[] colliders = Physics.OverlapSphere(spawnPosition, coinOverlapRadius, combinedAvoidanceLayers);
                    if (colliders.Length == 0)
                    {
                        positionFound = true;
                    }
                }
            }

            if (positionFound)
            {
                Quaternion spawnRotation = Quaternion.Euler(spawnRotationEuler);
                GameObject coinInstance = Instantiate(coinPrefab, spawnPosition, spawnRotation);
                NetworkObject netObject = coinInstance.GetComponent<NetworkObject>();
                if (netObject != null)
                {
                    netObject.Spawn();
                    //Debug.Log($"Coin {i} spawned at {spawnPosition}");
                }
            }
            else
            {
                Debug.LogWarning($"Failed to find valid position for coin {i}");
            }
        }
    }

    public void ResetSpawner()
    {
        hasSpawnedInitialCoins = false;
    }

    // Método para spawn de moedas em posição específica
    public void SpawnCoinsAtPosition(Vector3 centerPosition, int count, float radius)
    {
        if (!IsServer) return;

        for (int i = 0; i < count; i++)
        {
            // Calcula uma posição aleatória dentro do raio
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            Vector3 spawnPosition = centerPosition + new Vector3(randomCircle.x, 0.5f, randomCircle.y);

            // Simplesmente instanciamos, mas você pode adicionar mais verificação de colisão aqui
            Quaternion spawnRotation = Quaternion.Euler(spawnRotationEuler);
            GameObject coinInstance = Instantiate(coinPrefab, spawnPosition, spawnRotation);
            NetworkObject netObject = coinInstance.GetComponent<NetworkObject>();
            netObject.Spawn();
        }
    }

    public void SpawnDroppedCoins_Server(Vector3 dropPosition, int count, float radius)
    {
        if (!IsServer) return;

        for (int i = 0; i < count; i++)
        {
            // Lógica para spawnar as moedas em um raio (dropDistance do PlayerMovement)
            Vector3 randomOffset = Random.insideUnitSphere * radius;
            randomOffset.y = 0; // Garante que o spawn é no plano horizontal
            Vector3 spawnPosition = dropPosition + randomOffset;

            // Tenta encontrar o chão (FloorLayer) para posicionar a moeda corretamente no Y
            // As Layers devem ser configuradas corretamente no Inspector
            if (Physics.Raycast(spawnPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f, floorLayer))
            {
                spawnPosition.y = hit.point.y + 0.5f; // Ajusta para a altura do chão + um pequeno offset
            }

            // Instancia a moeda
            GameObject coinGO = Instantiate(coinPrefab, spawnPosition, Quaternion.Euler(spawnRotationEuler));
            // Spawna a moeda na rede (todos veem)
            coinGO.GetComponent<NetworkObject>().Spawn();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SpawnSingleCoinServerRpc(Vector3 position, Quaternion rotation)
    {
        if (!IsServer) return;

        // 生成設定を適用
        Quaternion desiredSpawnRotation = Quaternion.Euler(spawnRotationEuler);

        GameObject newCoin = Instantiate(coinPrefab, position, desiredSpawnRotation);
        newCoin.GetComponent<NetworkObject>().Spawn();
    }

    /// <summary>
    /// Desenha a área de spawn de moedas no Unity Editor para visualização.
    /// </summary>
    private void OnDrawGizmos()
    {
        // 1. Desenha a área de spawn inicial (retangular)

        // Define a cor para o Gizmo da área de spawn inicial
        Gizmos.color = new Color(0f, 1f, 0f, 0.5f); // Verde semi-transparente

        // Cria a matriz de transformação para o desenho.
        // Isso garante que a área seja desenhada no centro do CoinSpawner e com sua rotação.
        Matrix4x4 rotationMatrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.matrix = rotationMatrix;

        // Desenha um cubo (Box) que representa a área de spawn.
        // O tamanho do cubo é definido pela variável 'spawnAreaSize'.
        // Usamos Vector3.up * 0.5f para elevar o gizmo do centro do objeto, tornando a visualização mais limpa.
        Gizmos.DrawWireCube(Vector3.up * 0.5f, spawnAreaSize);

        // 2. Desenha a área de spawn dos botões (circular)

        // O spawn dos botões é um raio em torno de uma posição, mas como o CoinSpawner não armazena
        // a posição dos botões, vamos desenhar um indicador no centro do CoinSpawner para 
        // lembrar que há outro tipo de spawn.

        // Limpa a matriz de transformação para desenhar o próximo gizmo no espaço mundial.
        Gizmos.matrix = Matrix4x4.identity;

        // Define a cor para o Gizmo do raio de colisão/evitação
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f); // Laranja para evitar moedas

        // Desenha o raio de 'coinOverlapRadius' para mostrar a área que cada moeda ocupa e evita.
        // Desenhamos na posição do spawner (pode ser ajustado para um local mais útil se for o caso).
        // Nota: O raio de spawn do botão ('coinSpawnRadius') está no 'ButtonSpawner.cs', 
        // mas não temos a referência dele aqui, então esta parte é apenas um lembrete.
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, coinOverlapRadius);

    }
}