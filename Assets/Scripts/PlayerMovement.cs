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

    // Variáveis para suavização de movimento em clientes remotos
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    public float networkMovementSmoothness = 5f;

    private Coin _currentNearbyCoin;
    private bool _isCollecting = false;

    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    private float _lastPositionUpdateTime = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.1f; // 10 updates por segundo

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

            // Garante que o jogador está na posição correta
            if (IsServer)
            {
                transform.position = new Vector3(transform.position.x, 0.58f, transform.position.z);
            }
        }
        else
        {
            // Clientes remotos começam com a posição/rotação atual
            _networkPosition = transform.position;
            _networkRotation = transform.rotation;
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
        if (!IsOwner)
        {
            // Interpolação suave para clientes remotos
            transform.position = Vector3.Lerp(transform.position, _networkPosition, networkMovementSmoothness * Time.deltaTime);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation, networkMovementSmoothness * Time.deltaTime);
            return;
        }

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

            // Atualiza posição com mais frequência quando se movendo
            if (Time.time - _lastPositionUpdateTime >= POSITION_UPDATE_INTERVAL)
            {
                UpdatePositionServerRpc(transform.position, transform.rotation);
                _lastPositionUpdateTime = Time.time;
            }
        }
        else if (Time.time - _lastPositionUpdateTime >= POSITION_UPDATE_INTERVAL * 2)
        {
            // Atualiza menos frequentemente quando parado
            UpdatePositionServerRpc(transform.position, transform.rotation);
            _lastPositionUpdateTime = Time.time;
        }

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

    [ServerRpc]
    public void UpdatePositionServerRpc(Vector3 position, Quaternion rotation)
    {
        UpdatePositionClientRpc(position, rotation);
    }

    [ClientRpc]
    public void UpdatePositionClientRpc(Vector3 position, Quaternion rotation)
    {
        if (IsOwner) return;

        _networkPosition = position;
        _networkRotation = rotation;
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