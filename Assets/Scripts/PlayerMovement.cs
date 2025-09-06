using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Controls the movement of the player character in a networked environment.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    // Public and Inspector-visible variables
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f;
    public float speedReductionPerCoin = 0.05f;

    [Header("Backpack Settings")]
    public Transform backpackTransform;
    public Vector3 initialBackpackScale = new Vector3(0.4f, 0.2f, 0.4f);
    public Vector3[] backpackScales = new Vector3[10];

    [Header("Coin Interaction Settings")]
    public float collectionDuration = 0.5f;
    public float dropDistance = 2.0f;

    // Network variables
    public NetworkVariable<int> coinsCarried = new NetworkVariable<int>(0);

    // Variables for smoothing movement on remote clients
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    public float networkMovementSmoothness = 5f;

    // Private variables
    private float baseMoveSpeed;
    private CharacterController _characterController;
    private Coin _currentNearbyCoin;
    private bool _isCollecting = false;

    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    private float _lastPositionUpdateTime = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.1f; // 10 updates per second

    void Awake()
    {
        // Define backpack scales
        backpackScales[0] = new Vector3(0.5f, 0.25f, 0.5f);
        backpackScales[1] = new Vector3(0.6f, 0.3f, 0.6f);
        backpackScales[2] = new Vector3(0.7f, 0.35f, 0.7f);
        backpackScales[3] = new Vector3(0.8f, 0.4f, 0.8f);
        backpackScales[4] = new Vector3(0.9f, 0.45f, 0.9f);
        backpackScales[5] = new Vector3(1.0f, 0.5f, 1.0f);
        backpackScales[6] = new Vector3(1.1f, 0.55f, 1.1f);
        backpackScales[7] = new Vector3(1.2f, 0.6f, 1.2f);
        backpackScales[8] = new Vector3(1.3f, 0.65f, 1.3f);
        backpackScales[9] = new Vector3(1.4f, 0.7f, 1.4f);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize components and variables
        baseMoveSpeed = moveSpeed;
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError("CharacterController not found on player GameObject. Please add one!");
        }

        // Find CoinSpawner and GameManager in the scene
        _coinSpawner = FindFirstObjectByType<CoinSpawner>();
        _gameManager = FindFirstObjectByType<GameManager>();

        // Subscribe to the network variable change event
        coinsCarried.OnValueChanged += OnCoinsCarriedChanged;

        // Ensure the initial state is correct for all clients
        UpdateBackpackModel();
    }

    void Update()
    {
        if (_isCollecting)
        {
            return;
        }

        if (IsOwner)
        {
            HandlePlayerInput();

            // Local player is responsible for sending their position to the server
            if (Time.time - _lastPositionUpdateTime > POSITION_UPDATE_INTERVAL)
            {
                SubmitPositionServerRpc(transform.position, transform.rotation);
                _lastPositionUpdateTime = Time.time;
            }
        }
        else
        {
            // For remote clients, interpolate movement to smooth position transition
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * networkMovementSmoothness);
            transform.rotation = Quaternion.Slerp(transform.rotation, _networkRotation, Time.deltaTime * networkMovementSmoothness);
        }
    }

    /// <summary>
    /// Captures player input and moves the CharacterController.
    /// </summary>
    private void HandlePlayerInput()
    {
        if (_characterController == null) return;

        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);
        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        _characterController.Move(movement * moveSpeed * Time.deltaTime);

        // Rotation logic based on movement
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // Input logic for collecting a coin ('X')
        if (Input.GetKeyDown(KeyCode.X))
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.IsSpawned)
            {
                CollectCoinServerRpc(_currentNearbyCoin.GetComponent<NetworkObject>());
            }
            else
            {
                Debug.Log("No coin nearby or active to collect.");
                _currentNearbyCoin = null;
            }
        }

        // Input logic for dropping a coin ('C')
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (coinsCarried.Value > 0)
            {
                DropCoinServerRpc();
            }
            else
            {
                Debug.Log("You have no coins to drop.");
            }
        }
    }

    // RPC to synchronize player position and rotation
    [ServerRpc(RequireOwnership = false)]
    private void SubmitPositionServerRpc(Vector3 pos, Quaternion rot)
    {
        _networkPosition = pos;
        _networkRotation = rot;
    }

    // RPC that runs on the server to collect a coin
    [ServerRpc]
    private void CollectCoinServerRpc(NetworkObjectReference coinObjectReference)
    {
        if (coinsCarried.Value < 10)
        {
            StartCoroutine(CollectCoinCoroutine(coinObjectReference));
        }
        else
        {
            Debug.Log("Backpack is full!");
        }
    }

    // Coroutine to handle the collection process with a delay on the server
    IEnumerator CollectCoinCoroutine(NetworkObjectReference coinObjectReference)
    {
        _isCollecting = true;
        yield return new WaitForSeconds(collectionDuration);

        // Get the NetworkObject and Coin script
        coinObjectReference.TryGet(out NetworkObject coinNetworkObject);
        if (coinNetworkObject != null && coinNetworkObject.IsSpawned)
        {
            Coin coinToCollect = coinNetworkObject.GetComponent<Coin>();
            if (coinToCollect != null)
            {
                _gameManager.AddScoreServerRpc(coinToCollect.scoreValue);
                coinsCarried.Value++;
                coinToCollect.GetComponent<NetworkObject>().Despawn();
            }
        }
        _isCollecting = false;
    }

    // RPC to drop a coin
    [ServerRpc]
    private void DropCoinServerRpc()
    {
        if (coinsCarried.Value > 0)
        {
            _gameManager.AddScoreServerRpc(-1);
            coinsCarried.Value--;

            Vector3 dropPosition = transform.position - transform.forward * dropDistance;
            _coinSpawner.SpawnSingleCoinServerRpc(dropPosition, Quaternion.identity);
        }
    }

    /// <summary>
    /// This method is called automatically when `coinsCarried.Value` changes.
    /// It's the ideal place to update the UI, backpack model, and player speed.
    /// </summary>
    private void OnCoinsCarriedChanged(int previousValue, int newValue)
    {
        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    /// <summary>
    /// Updates the size of the backpack model based on the number of coins carried.
    /// </summary>
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

    /// <summary>
    /// Updates the player's movement speed based on the number of coins carried.
    /// </summary>
    public void UpdateMoveSpeed()
    {
        float newSpeed = baseMoveSpeed - (coinsCarried.Value * speedReductionPerCoin);
        moveSpeed = Mathf.Max(1f, newSpeed);
    }

    /// <summary>
    /// Detects if a coin enters the collection area.
    /// </summary>
    void OnTriggerEnter(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin"))
        {
            Coin coin = other.GetComponent<Coin>();
            if (coin != null)
            {
                _currentNearbyCoin = coin;
            }
        }
    }

    /// <summary>
    /// Clears the coin reference when the player exits the collection area.
    /// </summary>
    void OnTriggerExit(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin"))
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.gameObject == other.gameObject)
            {
                _currentNearbyCoin = null;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        coinsCarried.OnValueChanged -= OnCoinsCarriedChanged;
    }
}