using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Hitbox component attached to the Guard's weapon object.
/// When enabled it activates the collider for <see cref="activeTime"/> seconds,
/// detects the first <c>"Player"</c>-tagged object it touches, and asks the server
/// to process the hit via <see cref="GameManager.ProcessPlayerHitWithReferenceServerRpc"/>.
/// </summary>
public class CatcherAttack : MonoBehaviour
{
    // ====================================================================
    // Public Fields
    // ====================================================================

    /// <summary>
    /// Reference to the <see cref="NetworkObject"/> that owns this hitbox (the Guard).
    /// Populated by <see cref="Guard.OnNetworkSpawn"/> immediately after instantiation.
    /// </summary>
    [HideInInspector]
    public NetworkObject ownerNetworkObject;

    /// <summary>
    /// How long the hitbox collider remains active after the script is enabled.
    /// Should be kept short (≤ <see cref="Guard.swingDuration"/>) to match the animation.
    /// </summary>
    public float activeTime = 0.2f;

    // ====================================================================
    // Private
    // ====================================================================

    /// <summary>Prevents the same swing from registering more than one hit.</summary>
    private bool _hasHit;

    private GameManager _gameManager;
    private Collider _collider;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        if (_collider == null)
            Debug.LogError("[CatcherAttack] No Collider found on hitbox GameObject.");
    }

    private void Start()
    {
        _gameManager = FindAnyObjectByType<GameManager>();
    }

    private void OnEnable()
    {
        _hasHit = false;

        if (_collider != null) _collider.enabled = true;

        // Auto-deactivate after activeTime so the hitbox never stays open accidentally.
        CancelInvoke(nameof(DeactivateDamage));
        Invoke(nameof(DeactivateDamage), activeTime);
    }

    // ====================================================================
    // Private – Deactivation
    // ====================================================================

    /// <summary>
    /// Disables the collider and the script itself, ending the active damage window.
    /// </summary>
    private void DeactivateDamage()
    {
        if (_collider != null) _collider.enabled = false;
        enabled = false;
    }

    // ====================================================================
    // Collision Detection
    // ====================================================================

    private void OnTriggerEnter(Collider other)
    {
        if (_gameManager == null || !_gameManager.IsServer) return;
        if (ownerNetworkObject == null || _hasHit) return;
        if (!other.CompareTag("Player")) return;

        var playerMovement = other.GetComponent<PlayerMovement>();
        if (playerMovement == null) return;

        var playerNetObj = other.GetComponent<NetworkObject>();
        if (playerNetObj == null) return;

        // Prevent the guard from hitting itself
        if (playerNetObj.NetworkObjectId == ownerNetworkObject.NetworkObjectId) return;

        _hasHit = true;

        _gameManager.ProcessPlayerHitWithReferenceServerRpc(
            new NetworkObjectReference(playerNetObj));

        DeactivateDamage();
        CancelInvoke(nameof(DeactivateDamage));
    }
}
