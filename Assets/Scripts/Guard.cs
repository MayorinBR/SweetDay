using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the Guard (catcher) character: movement, attack swing animation,
/// and network synchronisation of cooldown state.
/// </summary>
public class Guard : NetworkBehaviour
{
    // ====================================================================
    // Inspector – Movement
    // ====================================================================

    [Header("Movement Settings")]
    /// <summary>Horizontal movement speed in units per second.</summary>
    public float moveSpeed = 5f;

    /// <summary>Rotation speed in degrees per second.</summary>
    public float rotationSpeed = 720f;

    // ====================================================================
    // Inspector – Attack
    // ====================================================================

    [Header("Attack Settings")]
    /// <summary>Prefab containing the <see cref="CatcherAttack"/> hitbox script.</summary>
    public GameObject attackHitboxPrefab;

    /// <summary>Seconds between consecutive attacks.</summary>
    public float attackCooldown = 1f;

    /// <summary>Duration of the full swing animation in seconds.</summary>
    public float swingDuration = 0.2f;

    // ====================================================================
    // Inspector – Weapon Visuals
    // ====================================================================

    [Header("Weapon Visual Settings")]
    /// <summary>Local position of the weapon while idle.</summary>
    public Vector3 idleLocalPosition = new Vector3(0.6f, 0f, 0.35f);

    /// <summary>Local position of the weapon at attack impact.</summary>
    public Vector3 attackLocalPosition = new Vector3(0f, 0f, 0.6f);

    /// <summary>Local Euler rotation of the weapon while idle.</summary>
    public Vector3 idleLocalRotationEuler = new Vector3(0f, 90f, 0f);

    // ====================================================================
    // Inspector – Physics
    // ====================================================================

    [Header("Physics Settings")]
    /// <summary>Gravitational acceleration applied each frame (negative = downward).</summary>
    public float gravity = -9.81f;

    // ====================================================================
    // Inspector – Network Smoothing
    // ====================================================================

    /// <summary>Lerp factor for remote-client position interpolation.</summary>
    public float networkMovementSmoothness = 5f;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Always <c>true</c> for the Guard role.</summary>
    public NetworkVariable<bool> IsCatcher = new NetworkVariable<bool>(true);

    /// <summary>Remaining attack cooldown in seconds, replicated for UI display.</summary>
    public NetworkVariable<float> AttackCooldownRemaining = new NetworkVariable<float>(0f);

    /// <summary>Display number shown in the player name and UI (1-based).</summary>
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    // ====================================================================
    // Private – State
    // ====================================================================

    private CharacterController _characterController;
    private GameManager _gameManager;
    private CatcherAttack _catcherAttackScript;

    private float _verticalVelocity;
    private float _localAttackCooldown;
    private bool _isSwinging;

    // Network interpolation for non-owner clients.
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;

    // ====================================================================
    // Private – Input State
    // ====================================================================

    /// <summary>Movement vector from the Input System "Move" action.</summary>
    private Vector2 _inputMove;

    /// <summary>Movement vector from the virtual joystick (mobile).</summary>
    private Vector2 _mobileMoveVector;

    private bool _attackPressed;

    // ====================================================================
    // NetworkBehaviour
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
            Debug.LogError("[Guard] Missing CharacterController.");

        _networkPosition = transform.position;
        _networkRotation = transform.rotation;

        playerNumber.OnValueChanged += (_, n) => gameObject.name = "Catcher_P" + n;
        if (playerNumber.Value > 0) gameObject.name = "Catcher_P" + playerNumber.Value;

        AttackCooldownRemaining.OnValueChanged += OnAttackCooldownChanged;

        if (attackHitboxPrefab != null)
        {
            var hitboxGO = Instantiate(attackHitboxPrefab, transform);
            _catcherAttackScript = hitboxGO.GetComponent<CatcherAttack>();
            if (_catcherAttackScript != null)
                _catcherAttackScript.ownerNetworkObject = GetComponent<NetworkObject>();

            hitboxGO.transform.localPosition = idleLocalPosition;
            hitboxGO.transform.localRotation = Quaternion.Euler(idleLocalRotationEuler);
            hitboxGO.SetActive(true);

            _catcherAttackScript.enabled = false;
            var col = hitboxGO.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        if (!IsOwner) return;

        _localAttackCooldown = AttackCooldownRemaining.Value;

        FindAnyObjectByType<MobileButtonsSetup>()?.FindAndSetupButtons();
        FindAnyObjectByType<SplitScreenManager>()?.RefreshCameras();
        FindAnyObjectByType<UIManager>()?.SetLocalCatcher(this);

        if (!IsServer)
            StartCoroutine(AssignControlsFromSlotData());

        _gameManager = FindAnyObjectByType<GameManager>();
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        AttackCooldownRemaining.OnValueChanged -= OnAttackCooldownChanged;
    }

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Update()
    {
        if (IsOwner)
        {
            if (_localAttackCooldown > 0f)
            {
                _localAttackCooldown -= Time.deltaTime;
                if (_localAttackCooldown < 0f) _localAttackCooldown = 0f;
                if (IsServer) AttackCooldownRemaining.Value = _localAttackCooldown;
            }

            if (!_isSwinging) HandleMovement();
            HandleAttackInput();
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position, _networkPosition, Time.deltaTime * networkMovementSmoothness);
            transform.rotation = Quaternion.Lerp(
                transform.rotation, _networkRotation, Time.deltaTime * networkMovementSmoothness);
        }

        ApplyGravity();
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
    /// <c>Attack</c> action is pressed.
    /// </summary>
    public void OnAttack(InputValue value)
    {
        if (!IsOwner || !value.isPressed) return;
        _attackPressed = true;
    }

    // ====================================================================
    // Public – Mobile Input
    // ====================================================================

    /// <summary>Sets the movement direction from the virtual joystick (mobile).</summary>
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;

    /// <summary>Called by the mobile Attack button.</summary>
    public void OnAttackButtonClicked() => _attackPressed = true;

    // ====================================================================
    // Public – Teleport
    // ====================================================================>

    /// <summary>Teleports the guard and snaps the camera. Called via ClientRpc.</summary>
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
    // Private – Movement
    // ====================================================================

    private void HandleMovement()
    {
        if (_gameManager != null && (!_gameManager.gameStarted.Value || _gameManager.isPaused.Value)) return;

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
    }

    private void ApplyGravity()
    {
        if (_characterController == null) return;
        _verticalVelocity = _characterController.isGrounded
            ? -0.5f
            : _verticalVelocity + gravity * Time.deltaTime;
        _characterController.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    // ====================================================================
    // Private – Attack
    // ====================================================================

    private void HandleAttackInput()
    {
        if (!IsOwner || _isSwinging || AttackCooldownRemaining.Value > 0f) return;
        if (!_attackPressed) return;

        _attackPressed = false;
        AttackServerRpc();
    }

    [ServerRpc]
    private void AttackServerRpc()
    {
        if (AttackCooldownRemaining.Value > 0f) return;

        _localAttackCooldown = attackCooldown;
        AttackCooldownRemaining.Value = attackCooldown;

        StartCoroutine(SwingAnimationCoroutine(swingDuration));
        PerformAttack();
        SetAttackCooldownClientRpc(attackCooldown);
        AnimateAttackClientRpc();
    }

    [ClientRpc]
    private void SetAttackCooldownClientRpc(float cooldown)
    {
        if (!IsOwner) return;
        _localAttackCooldown = cooldown;
    }

    [ClientRpc]
    private void AnimateAttackClientRpc()
        => StartCoroutine(SwingAnimationCoroutine(swingDuration));

    private void PerformAttack()
    {
        if (_catcherAttackScript != null)
            _catcherAttackScript.enabled = true;
    }

    private void OnAttackCooldownChanged(float oldValue, float newValue)
    {
        if (IsOwner && Mathf.Abs(_localAttackCooldown - newValue) > 0.1f)
            _localAttackCooldown = newValue;
    }

    // ====================================================================
    // Private – Swing Animation
    // ====================================================================

    private IEnumerator SwingAnimationCoroutine(float duration)
    {
        if (_catcherAttackScript == null) yield break;

        _isSwinging = true;
        Transform hitbox = _catcherAttackScript.transform;

        Quaternion startRot = Quaternion.Euler(idleLocalRotationEuler);
        Quaternion midRot = Quaternion.Euler(90f, 90f, 0f);
        Quaternion endRot = Quaternion.Euler(90f, -90f, 0f);

        yield return LerpTransform(hitbox, idleLocalPosition, attackLocalPosition, startRot, midRot, duration * 0.1f);
        yield return LerpRotation(hitbox, midRot, endRot, duration * 0.8f);
        yield return LerpTransform(hitbox, attackLocalPosition, idleLocalPosition, endRot, startRot, duration * 0.1f);

        hitbox.localPosition = idleLocalPosition;
        hitbox.localRotation = startRot;
        _isSwinging = false;
    }

    private static IEnumerator LerpTransform(
        Transform t, Vector3 fromPos, Vector3 toPos,
        Quaternion fromRot, Quaternion toRot, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float f = elapsed / duration;
            t.localPosition = Vector3.Lerp(fromPos, toPos, f);
            t.localRotation = Quaternion.Lerp(fromRot, toRot, f);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static IEnumerator LerpRotation(Transform t, Quaternion from, Quaternion to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            t.localRotation = Quaternion.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Waits one frame then assigns the correct device from <see cref="GameSettings.SlotAssignments"/>
    /// on the client machine that owns this Guard object.
    /// </summary>
    private System.Collections.IEnumerator AssignControlsFromSlotData()
    {
        yield return null;

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        int localIdx = 0;

        foreach (var slot in GameSettings.SlotAssignments)
        {
            if (slot.ClientId != localClientId) continue;
            if (slot.SlotIndex >= LobbyStateManager.RunnerSlotCount)
            {
                localIdx = slot.LocalPlayerIndex;
                break;
            }
        }

        if (ControlSetupManager.Instance != null)
            ControlSetupManager.Instance.AssignControlScheme(gameObject, localIdx);
    }
}