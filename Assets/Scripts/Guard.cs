using System.Collections;
using UnityEngine;
using Unity.Netcode;

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

    /// <summary>Rotation speed in degrees per second used with <see cref="Quaternion.Slerp"/>.</summary>
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
    public Vector3 idleLocalPosition = new Vector3(0.6f, 0.0f, 0.35f);

    /// <summary>Local position of the weapon at the moment of attack impact.</summary>
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

    /// <summary>Lerp factor used to smooth remote-client position interpolation.</summary>
    public float networkMovementSmoothness = 5f;

    // ====================================================================
    // Network Variables
    // ====================================================================

    /// <summary>Whether this character is the catcher role (always <c>true</c> for Guard).</summary>
    public NetworkVariable<bool> IsCatcher = new NetworkVariable<bool>(true);

    /// <summary>Remaining attack cooldown in seconds, replicated so the UI can display it.</summary>
    public NetworkVariable<float> AttackCooldownRemaining = new NetworkVariable<float>(0f);

    /// <summary>Display number shown in the player name and UI (e.g. 1 for "Catcher_P1").</summary>
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    // ====================================================================
    // Private – State
    // ====================================================================

    private CharacterController _characterController;
    private GameManager _gameManager;
    private CatcherAttack _catcherAttackScript;

    private float _verticalVelocity;
    private float _localAttackCooldown;
    private bool _attackOnCooldown;
    private bool _isSwinging;
    private float _lastAttackTime = -10f;

    // Network interpolation targets for non-owner clients.
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;

    // Mobile input state.
    private Vector2 _mobileMoveVector = Vector2.zero;
    private bool _attackPressed;

    // ====================================================================
    // NetworkBehaviour Overrides
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
            Debug.LogError("[Guard] Missing CharacterController component.");

        _networkPosition = transform.position;
        _networkRotation = transform.rotation;

        // Keep the GameObject name in sync with the player number.
        playerNumber.OnValueChanged += (_, newVal) => gameObject.name = "Catcher_P" + newVal;
        if (playerNumber.Value > 0)
            gameObject.name = "Catcher_P" + playerNumber.Value;

        AttackCooldownRemaining.OnValueChanged += OnAttackCooldownChanged;

        // Instantiate the hitbox prefab locally (non-networked visual/collider).
        if (attackHitboxPrefab != null)
        {
            var hitboxGO = Instantiate(attackHitboxPrefab, transform);
            _catcherAttackScript = hitboxGO.GetComponent<CatcherAttack>();

            if (_catcherAttackScript != null)
                _catcherAttackScript.ownerNetworkObject = GetComponent<NetworkObject>();

            hitboxGO.SetActive(true);
            hitboxGO.transform.localPosition = idleLocalPosition;
            hitboxGO.transform.localRotation = Quaternion.Euler(idleLocalRotationEuler);

            _catcherAttackScript.enabled = false;
            var col = hitboxGO.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        if (!IsOwner) return;

        _localAttackCooldown = AttackCooldownRemaining.Value;
        _attackOnCooldown = AttackCooldownRemaining.Value > 0f;

        // Notify UI systems about the newly spawned local guard.
        FindAnyObjectByType<MobileButtonsSetup>()?.FindAndSetupButtons();
        FindAnyObjectByType<SplitScreenManager>()?.RefreshCameras();
        FindAnyObjectByType<UIManager>()?.SetLocalCatcher(this);

        _gameManager = FindAnyObjectByType<GameManager>();
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        AttackCooldownRemaining.OnValueChanged -= OnAttackCooldownChanged;
    }

    // ====================================================================
    // Unity Update
    // ====================================================================

    private void Update()
    {
        if (IsOwner)
        {
            // Owner decrements cooldown locally for responsive UI.
            if (_localAttackCooldown > 0f)
            {
                _localAttackCooldown -= Time.deltaTime;
                if (_localAttackCooldown < 0f) _localAttackCooldown = 0f;
                _attackOnCooldown = _localAttackCooldown > 0f;

                if (IsServer)
                    AttackCooldownRemaining.Value = _localAttackCooldown;
            }

            if (!_isSwinging) HandleMovement();
            HandleAttackInput();
        }
        else
        {
            // Remote clients interpolate towards the server-driven position.
            transform.position = Vector3.Lerp(transform.position, _networkPosition,
                                              Time.deltaTime * networkMovementSmoothness);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation,
                                                 Time.deltaTime * networkMovementSmoothness);
        }

        ApplyGravity();
    }

    // ====================================================================
    // Public – Mobile Input API
    // ====================================================================

    /// <summary>Sets the movement direction from a virtual joystick.</summary>
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;

    /// <summary>Called by the mobile attack button.</summary>
    public void OnAttackButtonClicked() => _attackPressed = true;

    // ====================================================================
    // Public – Teleport (called by GameManager ClientRpc)
    // ====================================================================

    /// <summary>
    /// Teleports this character to <paramref name="newPosition"/> and forces the
    /// camera to snap immediately.  Disables then re-enables the
    /// <see cref="CharacterController"/> as required by Unity.
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
            if (cam.Target == transform)
                cam.ForcePosition();
        }

        FindAnyObjectByType<SplitScreenManager>()?.ResetCameraForPlayer(this);
    }

    // ====================================================================
    // Private – Movement
    // ====================================================================

    /// <summary>Reads input and moves the character controller horizontally.</summary>
    public void HandleMovement()
    {
        float h = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.x : Input.GetAxis("Horizontal");
        float v = _mobileMoveVector.magnitude > 0.1f ? _mobileMoveVector.y : Input.GetAxis("Vertical");

        Vector3 move = new Vector3(h, 0f, v);
        if (move.magnitude > 1f) move.Normalize();

        _characterController.Move(move * moveSpeed * Time.deltaTime);

        if (move != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                                  Quaternion.LookRotation(move),
                                                  rotationSpeed * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        _verticalVelocity = _characterController.isGrounded
            ? -0.5f
            : _verticalVelocity + gravity * Time.deltaTime;

        _characterController.Move(new Vector3(0f, _verticalVelocity * Time.deltaTime, 0f));
    }

    // ====================================================================
    // Private – Attack Input
    // ====================================================================

    private void HandleAttackInput()
    {
        if (!IsOwner || _isSwinging || AttackCooldownRemaining.Value > 0f) return;

        bool attack = Input.GetKeyDown(KeyCode.Z) || _attackPressed;
        _attackPressed = false;

        if (attack) AttackServerRpc();
    }

    // ====================================================================
    // Private – Attack RPCs
    // ====================================================================

    [ServerRpc]
    private void AttackServerRpc()
    {
        if (AttackCooldownRemaining.Value > 0f) return;

        _localAttackCooldown = attackCooldown;
        AttackCooldownRemaining.Value = attackCooldown;
        _lastAttackTime = Time.time;

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
        _attackOnCooldown = cooldown > 0f;
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
        {
            _localAttackCooldown = newValue;
            _attackOnCooldown = newValue > 0f;
        }
    }

    // ====================================================================
    // Private – Swing Animation Coroutine
    // ====================================================================

    private IEnumerator SwingAnimationCoroutine(float duration)
    {
        if (_catcherAttackScript == null) yield break;

        _isSwinging = true;
        Transform hitbox = _catcherAttackScript.transform;

        Quaternion startRot = Quaternion.Euler(idleLocalRotationEuler);
        Quaternion midRot = Quaternion.Euler(90f, 90f, 0f);
        Quaternion endRot = Quaternion.Euler(90f, -90f, 0f);

        // Phase 1 – wind-up (10 % of duration).
        yield return LerpTransform(hitbox, idleLocalPosition, attackLocalPosition,
                                   startRot, midRot, duration * 0.1f);

        // Phase 2 – swing arc (80 % of duration).
        yield return LerpRotation(hitbox, midRot, endRot, duration * 0.8f);

        // Phase 3 – return to idle (10 % of duration).
        yield return LerpTransform(hitbox, attackLocalPosition, idleLocalPosition,
                                   endRot, startRot, duration * 0.1f);

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

    private static IEnumerator LerpRotation(
        Transform t, Quaternion from, Quaternion to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            t.localRotation = Quaternion.Lerp(from, to, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
