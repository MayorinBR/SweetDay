using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the Runner player character: movement, dashing, coin collection/dropping,
/// and hit/invulnerability logic in a networked environment.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    // ====================================================================
    // Inspector – Movement
    // ====================================================================

    [Header("Movement Settings")]
    /// <summary>Base horizontal movement speed in units per second.</summary>
    public float moveSpeed = 5f;

    /// <summary>Rotation speed in degrees per second, applied via <see cref="Quaternion.Slerp"/>.</summary>
    public float rotationSpeed = 720f;

    /// <summary>Speed reduction applied per coin carried (stacks linearly).</summary>
    public float speedReductionPerCoin = 0.25f;

    // ====================================================================
    // Inspector – Backpack
    // ====================================================================

    [Header("Backpack Settings")]
    /// <summary>Transform of the backpack mesh; scaled based on coins carried.</summary>
    public Transform backpackTransform;

    /// <summary>Scale applied when the runner carries zero coins.</summary>
    public Vector3 initialBackpackScale = new Vector3(0.4f, 0.2f, 0.4f);

    /// <summary>
    /// Scale presets for each coin-level step.
    /// Index 0 = 1 coin carried, index N-1 = N coins carried.
    /// Automatically initialised in <see cref="Awake"/> if left empty.
    /// </summary>
    public Vector3[] backpackScales = new Vector3[MaxBackpackLevels];

    // ====================================================================
    // Inspector – Coin Interaction
    // ====================================================================

    [Header("Coin Interaction Settings")]
    /// <summary>Seconds it takes to complete a coin collection action.</summary>
    public float collectionDuration = 0.5f;

    /// <summary>Distance in front of the player at which dropped coins appear.</summary>
    public float dropDistance = 2f;

    // ====================================================================
    // Inspector – Physics
    // ====================================================================

    [Header("Physics Settings")]
    /// <summary>Gravitational acceleration applied every frame (negative = downward).</summary>
    public float gravity = -9.81f;

    // ====================================================================
    // Inspector – Visuals
    // ====================================================================

    [Header("Damage Settings")]
    [SerializeField]
    private MeshRenderer meshRenderer;

    // ====================================================================
    // Constants
    // ====================================================================

    private const int MaxBackpackLevels = 20;
    private const float DashCooldownDuration = 5f;
    private const float DashDuration = 0.15f;
    private const float DashDistanceMultiplier = 1.5f;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Number of coins currently held, replicated to all clients.</summary>
    public NetworkVariable<int> coinsCarried = new NetworkVariable<int>(0);

    /// <summary>Whether this character is acting as a Runner (always <c>true</c> here).</summary>
    public NetworkVariable<bool> IsRunner = new NetworkVariable<bool>(true);

    /// <summary>Remaining dash cooldown in seconds; server-authoritative.</summary>
    public NetworkVariable<float> DashCooldownRemaining = new NetworkVariable<float>(0f);

    /// <summary>Display number shown in the player name and UI (1-based).</summary>
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    /// <summary>Whether the player is currently protected from hits.</summary>
    public NetworkVariable<bool> IsInvulnerable = new NetworkVariable<bool>(false);

    // ====================================================================
    // Other Public Fields
    // ====================================================================

    /// <summary>Lerp factor used to smooth remote-client position interpolation.</summary>
    public float networkMovementSmoothness = 5f;

    /// <summary>Duration of the invulnerability window after being hit.</summary>
    public float invulnerabilityDuration = 3f;

    // ====================================================================
    // Private – Component References
    // ====================================================================

    private CharacterController _characterController;
    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    // ====================================================================
    // Private – Movement State
    // ====================================================================

    private float _baseMoveSpeed;
    private float _verticalVelocity;

    // ====================================================================
    // Private – Coin Interaction
    // ====================================================================

    private Coin _currentNearbyCoin;
    private bool _isCollecting;

    // ====================================================================
    // Private – Dash State
    // ====================================================================

    private bool _isDashing;
    private float _localDashCooldown;

    // ====================================================================
    // Private – Backpack Progression
    // ====================================================================

    private int _scoreToWin = 20;
    private float _coinsPerLevel = 1f;

    // ====================================================================
    // Private – Input (New Input System)
    // ====================================================================

    private Vector2 _inputMove;

    // Mobile virtual-button overrides.
    private Vector2 _mobileMoveVector;
    private bool _dashPressed;
    private bool _collectPressed;
    private bool _dropPressed;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        InitialiseBackpackScales();
    }

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _baseMoveSpeed = moveSpeed;
        _characterController = GetComponent<CharacterController>();

        if (_characterController == null)
            Debug.LogError("[PlayerMovement] Missing CharacterController component.");

        // Keep the GameObject name in sync with the networked player number.
        playerNumber.OnValueChanged += (_, n) => gameObject.name = "Runner_P" + n;
        if (playerNumber.Value > 0)
            gameObject.name = "Runner_P" + playerNumber.Value;

        coinsCarried.OnValueChanged += OnCoinsCarriedChanged;
        DashCooldownRemaining.OnValueChanged += OnDashCooldownChanged;

        if (IsOwner)
        {
            _localDashCooldown = DashCooldownRemaining.Value;

            // Notify UI systems about this newly spawned local player.
            FindAnyObjectByType<MobileButtonsSetup>()?.FindAndSetupButtons();
            FindAnyObjectByType<SplitScreenManager>()?.RefreshCameras();
            FindAnyObjectByType<UIManager>()?.SetLocalPlayerMovement(this);
        }

        _coinSpawner = FindAnyObjectByType<CoinSpawner>();
        _gameManager = FindAnyObjectByType<GameManager>();

        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    private void Start()
    {
        _gameManager ??= FindAnyObjectByType<GameManager>();

        if (_gameManager != null)
        {
            _scoreToWin = _gameManager.scoreToWin;
            CalculateProgressionFactor();
        }
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        coinsCarried.OnValueChanged -= OnCoinsCarriedChanged;
        DashCooldownRemaining.OnValueChanged -= OnDashCooldownChanged;
    }

    private void Update()
    {
        // Only the server decrements the authoritative dash cooldown.
        if (IsServer && DashCooldownRemaining.Value > 0f)
            DashCooldownRemaining.Value = Mathf.Max(0f, DashCooldownRemaining.Value - Time.deltaTime);

        if (!IsOwner) return;

        ApplyGravity();

        if (!_isCollecting)
            HandlePlayerInput();
    }

    // ====================================================================
    // Public – Mobile Input API
    // ====================================================================

    /// <summary>Sets the movement direction from a virtual joystick (mobile).</summary>
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;

    /// <summary>Called by the mobile Dash button.</summary>
    public void OnDashButtonClicked() => _dashPressed = true;

    /// <summary>Called by the mobile Collect button.</summary>
    public void OnCollectButtonClicked() => _collectPressed = true;

    /// <summary>Called by the mobile Drop button.</summary>
    public void OnDropButtonClicked() => _dropPressed = true;

    // ====================================================================
    // Public – New Input System Bindings (via PlayerInput SendMessage/UnityEvents)
    // ====================================================================

    /// <summary>Bound to the "Move" action; called by <see cref="PlayerInput"/>.</summary>
    public void OnMove(InputAction.CallbackContext ctx)
    {
        if (!IsOwner) return;
        _inputMove = ctx.ReadValue<Vector2>();
    }

    /// <summary>Bound to the "Dash" action.</summary>
    public void OnDash(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || !ctx.performed) return;
        _dashPressed = true;
    }

    /// <summary>Bound to the "Collect" action.</summary>
    public void OnCollect(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || !ctx.performed) return;
        _collectPressed = true;
    }

    /// <summary>Bound to the "Drop" action.</summary>
    public void OnDrop(InputAction.CallbackContext ctx)
    {
        if (!IsOwner || !ctx.performed) return;
        _dropPressed = true;
    }

    // ====================================================================
    // Public – Teleport (called via ClientRpc from GameManager)
    // ====================================================================

    /// <summary>
    /// Teleports this player to <paramref name="newPosition"/> and forces the camera to snap.
    /// Disables and re-enables the <see cref="CharacterController"/> as required by Unity.
    /// </summary>
    [ClientRpc]
    public void TeleportPlayerClientRpc(Vector3 newPosition)
    {
        if (_characterController != null) _characterController.enabled = false;
        transform.position = newPosition;
        if (_characterController != null) _characterController.enabled = true;

        if (!IsOwner) return;

        foreach (var cam in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
        {
            if (cam.Target == transform) cam.ForcePosition();
        }

        FindAnyObjectByType<SplitScreenManager>()?.ResetCameraForPlayer(this);
    }

    // ====================================================================
    // Public – Server-Side Hit Processing
    // ====================================================================

    /// <summary>
    /// Drops all carried coins and starts the invulnerability window.
    /// Must be called on the server; returns the number of coins dropped.
    /// </summary>
    public int ReceiveHitAndDropCoins_Server(CoinSpawner coinSpawner)
    {
        if (!IsServer || IsInvulnerable.Value) return 0;

        int dropped = coinsCarried.Value;
        coinsCarried.Value = 0;
        UpdateMoveSpeed();

        if (dropped > 0)
            coinSpawner.SpawnDroppedCoins_Server(transform.position, dropped, dropDistance);

        IsInvulnerable.Value = true;
        StartCoroutine(InvulnerabilityTimerCoroutine());
        StartInvulnerabilityVisualsClientRpc();

        return dropped;
    }

    // ====================================================================
    // Public – Utility
    // ====================================================================

    /// <summary>Returns the locally tracked dash cooldown value for UI display.</summary>
    public float GetLocalDashCooldown() => _localDashCooldown;

    /// <summary>
    /// Recalculates <see cref="moveSpeed"/> based on the number of coins currently carried,
    /// clamped to a minimum of 1 unit/s.
    /// </summary>
    public void UpdateMoveSpeed()
    {
        moveSpeed = Mathf.Max(1f, _baseMoveSpeed - coinsCarried.Value * speedReductionPerCoin);
    }

    // ====================================================================
    // Private – Input Handling
    // ====================================================================

    private void HandlePlayerInput()
    {
        if (!IsRunner.Value || _characterController == null) return;

        // Prefer virtual joystick; fall back to the New Input System vector.
        float h = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.x : _inputMove.x;
        float v = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.y : _inputMove.y;

        Vector3 move = new Vector3(h, 0f, v);
        if (move.magnitude > 1f) move.Normalize();

        _characterController.Move(move * moveSpeed * Time.deltaTime);

        if (move != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(move),
                rotationSpeed * Time.deltaTime);
        }

        // Collect.
        if (_collectPressed)
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.IsSpawned)
                CollectCoinServerRpc(_currentNearbyCoin.GetComponent<NetworkObject>());
            else
                _currentNearbyCoin = null;
        }

        // Drop.
        if (_dropPressed && coinsCarried.Value > 0)
            DropCoinServerRpc();

        // Dash.
        if (_dashPressed && DashCooldownRemaining.Value <= 0f && !_isDashing)
        {
            _localDashCooldown = DashCooldownDuration;
            Vector3 dir = move.magnitude > 0.1f ? move : transform.forward;
            DashServerRpc(dir);
        }

        // Consume all input flags in one place.
        _dashPressed = false;
        _collectPressed = false;
        _dropPressed = false;
    }

    private void ApplyGravity()
    {
        if (_characterController == null) return;

        _verticalVelocity = _characterController.isGrounded
            ? -2f
            : _verticalVelocity + gravity * Time.deltaTime;

        _characterController.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
    }

    // ====================================================================
    // Private – Coin ServerRpcs
    // ====================================================================

    [ServerRpc]
    private void CollectCoinServerRpc(NetworkObjectReference coinRef)
    {
        if (coinsCarried.Value < _scoreToWin)
            StartCoroutine(CollectCoinCoroutine(coinRef));
    }

    private IEnumerator CollectCoinCoroutine(NetworkObjectReference coinRef)
    {
        _isCollecting = true;
        yield return new WaitForSeconds(collectionDuration);

        coinRef.TryGet(out NetworkObject coinNetObj);
        if (coinNetObj != null && coinNetObj.IsSpawned)
        {
            var coin = coinNetObj.GetComponent<Coin>();
            if (coin != null)
            {
                _gameManager?.AddScoreServerRpc(coin.scoreValue);
                coinsCarried.Value++;
                coinNetObj.Despawn();
            }
        }

        _isCollecting = false;
    }

    [ServerRpc]
    private void DropCoinServerRpc()
    {
        if (coinsCarried.Value <= 0) return;

        _gameManager?.AddScoreServerRpc(-1);
        coinsCarried.Value--;

        Vector3 dropPos = transform.position - transform.forward * dropDistance;
        _coinSpawner?.SpawnSingleCoinServerRpc(dropPos, Quaternion.identity);
    }

    // ====================================================================
    // Private – Dash
    // ====================================================================

    [ServerRpc]
    private void DashServerRpc(Vector3 direction)
    {
        if (_isDashing || DashCooldownRemaining.Value > 0f) return;

        DashCooldownRemaining.Value = DashCooldownDuration;
        DashClientRpc(direction, DashCooldownDuration);
    }

    [ClientRpc]
    private void DashClientRpc(Vector3 direction, float cooldown)
    {
        if (IsOwner) _localDashCooldown = cooldown;
        StartCoroutine(DashCoroutine(direction));
    }

    private IEnumerator DashCoroutine(Vector3 direction)
    {
        _isDashing = true;

        float dashSpeed = (moveSpeed * DashDistanceMultiplier) / DashDuration;
        float endTime = Time.time + DashDuration;

        while (Time.time < endTime)
        {
            _characterController.Move(direction * dashSpeed * Time.deltaTime);
            yield return null;
        }

        _isDashing = false;
    }

    // ====================================================================
    // Private – Invulnerability
    // ====================================================================

    [ClientRpc]
    private void StartInvulnerabilityVisualsClientRpc()
        => StartCoroutine(FlashVisualsCoroutine(invulnerabilityDuration));

    private IEnumerator InvulnerabilityTimerCoroutine()
    {
        yield return new WaitForSeconds(invulnerabilityDuration);
        IsInvulnerable.Value = false;
    }

    private IEnumerator FlashVisualsCoroutine(float duration)
    {
        if (meshRenderer == null) yield break;

        float end = Time.time + duration;
        while (Time.time < end)
        {
            meshRenderer.enabled = !meshRenderer.enabled;
            yield return new WaitForSeconds(0.1f);
        }

        meshRenderer.enabled = true;
    }

    // ====================================================================
    // Private – NetworkVariable Callbacks
    // ====================================================================

    private void OnCoinsCarriedChanged(int previous, int current)
    {
        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    private void OnDashCooldownChanged(float previous, float current)
    {
        if (IsOwner && Mathf.Abs(_localDashCooldown - current) > 0.1f)
            _localDashCooldown = current;
    }

    // ====================================================================
    // Private – Trigger Detection
    // ====================================================================

    private void OnTriggerEnter(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin"))
            _currentNearbyCoin = other.GetComponent<Coin>();
    }

    private void OnTriggerExit(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin")
            && _currentNearbyCoin != null
            && _currentNearbyCoin.gameObject == other.gameObject)
        {
            _currentNearbyCoin = null;
        }
    }

    // ====================================================================
    // Private – Backpack Model
    // ====================================================================

    private void UpdateBackpackModel()
    {
        if (backpackTransform == null) return;

        if (coinsCarried.Value == 0)
        {
            backpackTransform.localScale = initialBackpackScale;
            return;
        }

        int index = Mathf.Clamp(coinsCarried.Value - 1, 0, backpackScales.Length - 1);
        backpackTransform.localScale = backpackScales[index];
    }

    private void CalculateProgressionFactor()
    {
        _coinsPerLevel = Mathf.Max(1f, (float)_scoreToWin / MaxBackpackLevels);
    }

    /// <summary>
    /// Procedurally fills <see cref="backpackScales"/> with evenly interpolated
    /// scale values between the initial small size and the maximum full-backpack size.
    /// Called once in <see cref="Awake"/>; existing non-zero values are preserved.
    /// </summary>
    private void InitialiseBackpackScales()
    {
        if (backpackScales == null || backpackScales.Length < MaxBackpackLevels)
            backpackScales = new Vector3[MaxBackpackLevels];

        for (int i = 0; i < MaxBackpackLevels; i++)
        {
            float t = (float)i / (MaxBackpackLevels - 1);
            float s = Mathf.Lerp(0.5f, 1.5f, t);
            float h = Mathf.Lerp(0.2f, 0.7f, t);
            backpackScales[i] = new Vector3(s, h, s);
        }
    }
}
