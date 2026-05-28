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
    /// Scale presets for each coin-level step (auto-initialised in <see cref="Awake"/>).
    /// </summary>
    public Vector3[] backpackScales = new Vector3[MaxBackpackLevels];

    // ====================================================================
    // Inspector – Coin Interaction
    // ====================================================================

    [Header("Coin Interaction Settings")]
    /// <summary>Seconds to complete a coin collection action.</summary>
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

    /// <summary>Always <c>true</c> for the Runner role.</summary>
    public NetworkVariable<bool> IsRunner = new NetworkVariable<bool>(true);

    /// <summary>Remaining dash cooldown in seconds; server-authoritative.</summary>
    public NetworkVariable<float> DashCooldownRemaining = new NetworkVariable<float>(0f);

    /// <summary>Display number shown in the player name and UI (1-based).</summary>
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    /// <summary>Whether the player is currently protected from hits.</summary>
    public NetworkVariable<bool> IsInvulnerable = new NetworkVariable<bool>(false);

    // ====================================================================
    // Public Fields
    // ====================================================================

    /// <summary>Lerp factor for remote-client position interpolation.</summary>
    public float networkMovementSmoothness = 5f;

    /// <summary>Duration of the invulnerability window after being hit.</summary>
    public float invulnerabilityDuration = 3f;

    // ====================================================================
    // Private – Components
    // ====================================================================

    private CharacterController _characterController;
    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    // ====================================================================
    // Private – Movement
    // ====================================================================

    private float _baseMoveSpeed;
    private float _verticalVelocity;

    // ====================================================================
    // Private – Input State (Input System)
    // ====================================================================

    /// <summary>Movement vector read from the Input System "Move" action.</summary>
    private Vector2 _inputMove;

    /// <summary>Movement vector sent by the virtual joystick (mobile).</summary>
    private Vector2 _mobileMoveVector;

    private bool _dashPressed;
    private bool _collectPressed;
    private bool _dropPressed;

    // ====================================================================
    // Private – Coin Interaction
    // ====================================================================

    /// <summary>All coins currently inside the player's collection trigger.</summary>
    private readonly System.Collections.Generic.List<Coin> _nearbyCoins = new();
    private bool _isCollecting;

    // ====================================================================
    // Private – Dash
    // ====================================================================

    private bool _isDashing;
    private float _localDashCooldown;

    // ====================================================================
    // Private – Backpack
    // ====================================================================

    private int _scoreToWin = 20;
    private float _coinsPerLevel = 1f;

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
            Debug.LogError("[PlayerMovement] Missing CharacterController.");

        playerNumber.OnValueChanged += (_, n) => gameObject.name = "Runner_P" + n;
        if (playerNumber.Value > 0) gameObject.name = "Runner_P" + playerNumber.Value;

        coinsCarried.OnValueChanged += OnCoinsCarriedChanged;
        DashCooldownRemaining.OnValueChanged += OnDashCooldownChanged;

        if (IsOwner)
        {
            _localDashCooldown = DashCooldownRemaining.Value;
            FindAnyObjectByType<MobileButtonsSetup>()?.FindAndSetupButtons();
            FindAnyObjectByType<SplitScreenManager>()?.RefreshCameras();
            FindAnyObjectByType<UIManager>()?.SetLocalPlayerMovement(this);

            if (!IsServer)
                StartCoroutine(AssignControlsFromSlotData());
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
            _coinsPerLevel = Mathf.Max(1f, (float)_scoreToWin / MaxBackpackLevels);
        }
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        coinsCarried.OnValueChanged -= OnCoinsCarriedChanged;
        DashCooldownRemaining.OnValueChanged -= OnDashCooldownChanged;
    }

    /// <summary>
    /// Waits one frame then assigns the correct device from <see cref="GameSettings.SlotAssignments"/>
    /// on the client machine that owns this player object.
    /// </summary>
    private System.Collections.IEnumerator AssignControlsFromSlotData()
    {
        yield return null;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        int localIdx = 0;
        int countForClient = 0;

        foreach (var slot in GameSettings.SlotAssignments)
        {
            if (slot.ClientId != localClientId) continue;
            if (slot.SlotIndex < LobbyStateManager.RunnerSlotCount)
            {
                if (countForClient == 0) localIdx = slot.LocalPlayerIndex;
                countForClient++;
            }
        }

        if (ControlSetupManager.Instance != null)
            ControlSetupManager.Instance.AssignControlScheme(gameObject, localIdx);
    }

    private void Update()
    {
        if (IsServer && DashCooldownRemaining.Value > 0f)
            DashCooldownRemaining.Value = Mathf.Max(0f, DashCooldownRemaining.Value - Time.deltaTime);

        if (!IsOwner) return;

        ApplyGravity();

        if (!_isCollecting)
            HandleInput();
    }

    // ====================================================================
    // Public – Input System Callbacks  (Send Messages mode)
    // ====================================================================

    /// <summary>
    /// Called by <see cref="PlayerInput"/> (Send Messages) when the
    /// <c>Move</c> action changes.
    /// </summary>
    public void OnMove(InputValue value)
    {
        if (!IsOwner) return;
        _inputMove = value.Get<Vector2>();
    }

    /// <summary>
    /// Called by <see cref="PlayerInput"/> (Send Messages) when the
    /// <c>Dash</c> action is pressed.
    /// </summary>
    public void OnDash(InputValue value)
    {
        if (!IsOwner || !value.isPressed) return;
        _dashPressed = true;
    }

    /// <summary>
    /// Called by <see cref="PlayerInput"/> (Send Messages) when the
    /// <c>Collect</c> action is pressed.
    /// </summary>
    public void OnCollect(InputValue value)
    {
        if (!IsOwner || !value.isPressed) return;
        _collectPressed = true;
    }

    /// <summary>
    /// Called by <see cref="PlayerInput"/> (Send Messages) when the
    /// <c>Drop</c> action is pressed.
    /// </summary>
    public void OnDrop(InputValue value)
    {
        if (!IsOwner || !value.isPressed) return;
        _dropPressed = true;
    }

    // ====================================================================
    // Public – Mobile Input
    // ====================================================================

    /// <summary>Sets the movement direction from the virtual joystick.</summary>
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;

    /// <summary>Called by the mobile Dash button.</summary>
    public void OnDashButtonClicked() => _dashPressed = true;

    /// <summary>Called by the mobile Collect button.</summary>
    public void OnCollectButtonClicked() => _collectPressed = true;

    /// <summary>Called by the mobile Drop button.</summary>
    public void OnDropButtonClicked() => _dropPressed = true;

    // ====================================================================
    // Public – Teleport
    // ====================================================================

    /// <summary>Teleports the player and snaps the camera. Called via ClientRpc.</summary>
    [ClientRpc]
    public void TeleportPlayerClientRpc(Vector3 newPosition)
    {
        if (_characterController != null) _characterController.enabled = false;
        transform.position = newPosition;
        if (_characterController != null) _characterController.enabled = true;

        if (!IsOwner) return;

        foreach (var cam in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
            if (cam.Target == transform) cam.ForcePosition();

        FindAnyObjectByType<SplitScreenManager>()?.ResetCameraForPlayer(this);
    }

    // ====================================================================
    // Public – Server-Side Hit
    // ====================================================================

    /// <summary>
    /// Drops all carried coins and starts invulnerability.
    /// Must be called on the server.
    /// </summary>
    /// <returns>Number of coins dropped.</returns>
    public int ReceiveHitAndDropCoins_Server(CoinSpawner coinSpawner)
    {
        if (!IsServer || IsInvulnerable.Value) return 0;

        int dropped = coinsCarried.Value;
        coinsCarried.Value = 0;
        UpdateMoveSpeed();

        if (dropped > 0)
            coinSpawner.SpawnDroppedCoinsDirectional_Server(transform.position, transform.forward, dropped);

        IsInvulnerable.Value = true;
        StartCoroutine(InvulnerabilityTimerCoroutine());
        StartInvulnerabilityVisualsClientRpc();
        return dropped;
    }

    // ====================================================================
    // Public – Utility
    // ====================================================================

    /// <summary>Returns the locally tracked dash cooldown for UI display.</summary>
    public float GetLocalDashCooldown() => _localDashCooldown;

    /// <summary>Recalculates <see cref="moveSpeed"/> based on coins carried.</summary>
    public void UpdateMoveSpeed()
        => moveSpeed = Mathf.Max(1f, _baseMoveSpeed - coinsCarried.Value * speedReductionPerCoin);

    // ====================================================================
    // Private – Input Handling
    // ====================================================================

    private void HandleInput()
    {
        if (_gameManager != null && (!_gameManager.gameStarted.Value || _gameManager.isPaused.Value)) return;
        if (!IsRunner.Value || _characterController == null) return;

        // Mobile joystick overrides Input System when active.
        float h = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.x : _inputMove.x;
        float v = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.y : _inputMove.y;

        Vector3 move = new Vector3(h, 0f, v);
        if (move.magnitude > 1f) move.Normalize();

        _characterController.Move(move * moveSpeed * Time.deltaTime);

        if (move != Vector3.zero)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(move),
                rotationSpeed * Time.deltaTime);

        if (_collectPressed)
        {
            var coin = GetNextNearbyCoin();
            if (coin != null)
                CollectCoinServerRpc(coin.GetComponent<NetworkObject>());
        }

        if (_dropPressed && coinsCarried.Value > 0)
            DropCoinServerRpc();

        if (_dashPressed && DashCooldownRemaining.Value <= 0f && !_isDashing)
        {
            _localDashCooldown = DashCooldownDuration;
            Vector3 dir = move.magnitude > 0.1f ? move : transform.forward;
            DashServerRpc(dir);
        }

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
        _characterController.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    // ====================================================================
    // Private – Coin RPCs
    // ====================================================================

    [ServerRpc]
    private void CollectCoinServerRpc(NetworkObjectReference coinRef)
    {
        if (_isCollecting) return;
        if (coinsCarried.Value < _scoreToWin)
            StartCoroutine(CollectCoinCoroutine(coinRef));
    }

    private IEnumerator CollectCoinCoroutine(NetworkObjectReference coinRef)
    {
        _isCollecting = true;
        yield return new WaitForSeconds(collectionDuration);

        coinRef.TryGet(out NetworkObject netObj);
        if (netObj != null && netObj.IsSpawned)
        {
            var coin = netObj.GetComponent<Coin>();
            if (coin != null)
            {
                _gameManager?.AddScoreServerRpc(coin.scoreValue);
                coinsCarried.Value++;
                netObj.Despawn();
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
        _coinSpawner?.SpawnDroppedCoinsDirectional_Server(transform.position, transform.forward, 1);
    }

    // ====================================================================
    // Private – Dash RPCs
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
        float speed = (moveSpeed * DashDistanceMultiplier) / DashDuration;
        float end = Time.time + DashDuration;
        while (Time.time < end)
        {
            _characterController.Move(direction * speed * Time.deltaTime);
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

    /// <summary>Change size of the bagpack based on coins carried.</summary>
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

    /// <summary>
    /// Returns the first valid coin still inside the collection trigger.
    /// Cleans up stale references (null or already despawned) in the process.
    /// </summary>
    private Coin GetNextNearbyCoin()
    {
        _nearbyCoins.RemoveAll(c => c == null || !c.IsSpawned);
        return _nearbyCoins.Count > 0 ? _nearbyCoins[0] : null;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsOwner || !other.CompareTag("Coin")) return;
        var coin = other.GetComponent<Coin>();
        if (coin != null && !_nearbyCoins.Contains(coin))
            _nearbyCoins.Add(coin);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsOwner || !other.CompareTag("Coin")) return;
        var coin = other.GetComponent<Coin>();
        if (coin != null) _nearbyCoins.Remove(coin);
    }

    // ====================================================================
    // Private – Backpack
    // ====================================================================

    private void UpdateBackpackModel()
    {
        if (backpackTransform == null) return;
        if (coinsCarried.Value == 0) { backpackTransform.localScale = initialBackpackScale; return; }
        int idx = Mathf.Clamp(coinsCarried.Value - 1, 0, backpackScales.Length - 1);
        backpackTransform.localScale = backpackScales[idx];
    }

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

#if UNITY_EDITOR
    // ====================================================================
    // Editor Gizmos
    // ====================================================================

    /// <summary>
    /// Draws the coin drop fan in the Scene view when this player is selected.
    /// Parameters are read from the scene's <see cref="CoinSpawner"/>; falls back
    /// to the hardcoded defaults if no spawner is found.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        var spawner = FindAnyObjectByType<CoinSpawner>();
        float spread = spawner != null ? spawner.dropSpreadAngle : 55f;
        float minDist = spawner != null ? spawner.dropMinDistance : 0.6f;
        float maxDist = spawner != null ? spawner.dropMaxDistance : 3.5f;

        Vector3 origin = transform.position + Vector3.up * 0.1f;
        Vector3 behindDir = -transform.forward;

        UnityEngine.Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);

        // Left and right boundary rays.
        Vector3 leftEdge = Quaternion.AngleAxis(-spread, Vector3.up) * behindDir;
        Vector3 rightEdge = Quaternion.AngleAxis(spread, Vector3.up) * behindDir;

        UnityEngine.Gizmos.DrawLine(origin + leftEdge * minDist, origin + leftEdge * maxDist);
        UnityEngine.Gizmos.DrawLine(origin + rightEdge * minDist, origin + rightEdge * maxDist);

        // Inner and outer arcs.
        DrawDropArc(origin, behindDir, spread, minDist);
        DrawDropArc(origin, behindDir, spread, maxDist);
    }

    private static void DrawDropArc(Vector3 center, Vector3 baseDir, float halfAngle, float radius)
    {
        const int Steps = 24;
        float step = halfAngle * 2f / Steps;
        Vector3 prev = center + Quaternion.AngleAxis(-halfAngle, Vector3.up) * baseDir * radius;
        for (int i = 1; i <= Steps; i++)
        {
            Vector3 next = center + Quaternion.AngleAxis(-halfAngle + step * i, Vector3.up) * baseDir * radius;
            UnityEngine.Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}