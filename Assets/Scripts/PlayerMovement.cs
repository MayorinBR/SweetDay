using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class PlayerMovement : NetworkBehaviour
{
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f;
    private float baseMoveSpeed;
    private CharacterController _characterController;

    public Transform backpackTransform;
    public float speedReductionPerCoin = 0.05f;
    public Vector3 initialBackpackScale = new Vector3(0.4f, 0.2f, 0.4f);
    private Vector3[] backpackScales = new Vector3[10];

    public float collectionDuration = 0.5f;
    public float dropDistance = 2.0f;
    public GameObject coinPrefab;

    public NetworkVariable<int> coinsCarried = new NetworkVariable<int>(0);

    // VARIÁVEIS DE REDE PARA SINCRONIZAÇÃO DE MOVIMENTO
    public NetworkVariable<Vector3> NetworkPosition = new NetworkVariable<Vector3>();
    public NetworkVariable<Quaternion> NetworkRotation = new NetworkVariable<Quaternion>();

    private Coin _currentNearbyCoin;
    private bool _isCollecting = false;

    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        baseMoveSpeed = moveSpeed;
        _coinSpawner = FindFirstObjectByType<CoinSpawner>();
        _gameManager = FindFirstObjectByType<GameManager>();

        for (int i = 0; i < backpackScales.Length; i++)
        {
            backpackScales[i] = initialBackpackScale * (1 + (i + 1) * 0.2f);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            coinsCarried.OnValueChanged += OnCoinsCarriedChanged;

            CameraFollow cameraFollow = FindFirstObjectByType<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.Target = this.transform;
            }

            // CORREÇÃO: Define a posição de spawn para o dono (owner) do objeto
            // Garante que o jogador comece na altura correta (y = 0.58)
            transform.position = new Vector3(transform.position.x, 0.58f, transform.position.z);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            coinsCarried.OnValueChanged -= OnCoinsCarriedChanged;
        }
    }

    void Update()
    {
        // Se o objeto não é o local, ele simplesmente atualiza sua posição
        // com base nas variáveis de rede. Isso sincroniza o movimento.
        if (!IsOwner)
        {
            transform.position = NetworkPosition.Value;
            transform.rotation = NetworkRotation.Value;
            return;
        }

        // Se o jogador é o local (dono), ele pode se mover.
        if (_isCollecting)
        {
            _characterController.Move(Vector3.zero);
            return;
        }

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 direction = new Vector3(horizontal, 0, vertical).normalized;

        if (direction.magnitude >= 0.1f)
        {
            Quaternion toRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, toRotation, rotationSpeed * Time.deltaTime);

            _characterController.Move(direction * moveSpeed * Time.deltaTime);
        }

        // Atualiza as variáveis de rede para que o servidor possa replicar o movimento.
        UpdatePositionServerRpc(transform.position, transform.rotation);

        if (Input.GetKeyDown(KeyCode.X))
        {
            if (_currentNearbyCoin != null && !_isCollecting)
            {
                StartCoroutine(CollectCoin());
            }
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            if (coinsCarried.Value > 0)
            {
                RequestDropCoinServerRpc(transform.position, transform.forward, dropDistance);
            }
        }
    }

    // NOVO RPC para o cliente enviar sua posição/rotação ao servidor.
    [ServerRpc]
    public void UpdatePositionServerRpc(Vector3 position, Quaternion rotation)
    {
        NetworkPosition.Value = position;
        NetworkRotation.Value = rotation;
    }

    [ServerRpc]
    public void RequestCollectCoinServerRpc(ulong coinNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(coinNetworkObjectId, out NetworkObject networkObject))
        {
            networkObject.Despawn();
            _gameManager.score.Value += 1;
            coinsCarried.Value++;
        }
    }

    [ServerRpc]
    public void RequestDropCoinServerRpc(Vector3 playerPosition, Vector3 playerDirection, float distance)
    {
        if (coinsCarried.Value > 0)
        {
            Vector3 dropPosition = playerPosition - playerDirection.normalized * distance;
            _coinSpawner.SpawnSingleCoin(dropPosition, Quaternion.identity);
            coinsCarried.Value--;
            _gameManager.SubtractScoreServerRpc(1);
        }
    }

    IEnumerator CollectCoin()
    {
        _isCollecting = true;
        if (_currentNearbyCoin != null)
        {
            RequestCollectCoinServerRpc(_currentNearbyCoin.GetComponent<NetworkObject>().NetworkObjectId);
        }
        yield return new WaitForSeconds(collectionDuration);
        _isCollecting = false;
    }

    private void OnCoinsCarriedChanged(int oldCoins, int newCoins)
    {
        UpdatePlayerStats();
    }

    public void UpdatePlayerStats()
    {
        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    public void UpdateMoveSpeed()
    {
        float newSpeed = baseMoveSpeed - (coinsCarried.Value * speedReductionPerCoin);
        moveSpeed = Mathf.Max(1f, newSpeed);
    }

    private void UpdateBackpackModel()
    {
        if (backpackTransform == null) return;

        if (coinsCarried.Value == 0)
        {
            backpackTransform.localScale = initialBackpackScale;
        }
        else
        {
            int index = coinsCarried.Value - 1;

            if (index >= 0 && index < backpackScales.Length)
            {
                backpackTransform.localScale = backpackScales[index];
            }
            else if (index >= backpackScales.Length)
            {
                backpackTransform.localScale = backpackScales[backpackScales.Length - 1];
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Coin"))
        {
            Coin coin = other.GetComponent<Coin>();
            if (coin != null)
            {
                _currentNearbyCoin = coin;
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Coin"))
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.gameObject == other.gameObject)
            {
                _currentNearbyCoin = null;
            }
        }
    }
}