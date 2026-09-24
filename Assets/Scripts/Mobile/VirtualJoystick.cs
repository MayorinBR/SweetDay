using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Implements a mobile virtual joystick that drives the movement of the local
/// player character (<see cref="PlayerMovement"/> or <see cref="Guard"/>).
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("UI References")]
    /// <summary>
    /// The fixed outer circle of the joystick.
    /// Auto-assigned to the immediate parent's <see cref="RectTransform"/> if left empty.
    /// </summary>
    public RectTransform background;

    /// <summary>
    /// The moving inner handle of the joystick.
    /// Auto-assigned to this GameObject's own <see cref="RectTransform"/> if left empty.
    /// </summary>
    public RectTransform handle;

    [Header("Settings")]
    /// <summary>
    /// Maximum displacement of the handle from the background centre, in canvas pixels.
    /// The normalised direction vector sent to the player is clamped so its magnitude
    /// never exceeds 1 regardless of this value.
    /// </summary>
    public float maxRadius = 100f;

    // ====================================================================
    // Private
    // ====================================================================

    private PlayerMovement _localPlayerMovement;
    private Guard _localGuard;

    /// <summary><c>true</c> once a local-owner player object has been located.</summary>
    private bool _isPlayerFound;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (handle == null)
            handle = GetComponent<RectTransform>();

        if (background == null && transform.parent != null)
            background = transform.parent.GetComponent<RectTransform>();

        // Ensure the handle starts centred.
        if (handle != null)
            handle.anchoredPosition = Vector2.zero;

        // Hide the background until the joystick is first touched.
        if (background != null)
            background.gameObject.SetActive(false);
    }

    private void Start() => FindLocalPlayer();

    private void Update()
    {
        // Keep polling until a local player is found.
        // Once found, the condition is never true again, so there is no ongoing cost.
        if (!_isPlayerFound
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsClient)
        {
            FindLocalPlayer();
        }
    }

    // ====================================================================
    // IPointerDownHandler / IDragHandler / IPointerUpHandler
    // ====================================================================

    /// <inheritdoc/>
    public void OnPointerDown(PointerEventData eventData)
    {
        // Re-validate the player reference in case the object was replaced mid-session.
        if (!_isPlayerFound || (_localPlayerMovement == null && _localGuard == null))
            FindLocalPlayer();

        OnDrag(eventData);
    }

    /// <inheritdoc/>
    public void OnDrag(PointerEventData eventData)
    {
        if (background == null || !_isPlayerFound) return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        // Compute a normalised direction vector, clamping the handle inside the circle.
        Vector2 direction = localPoint / maxRadius;
        if (direction.magnitude > 1f)
        {
            direction.Normalize();
            localPoint = direction * maxRadius;
        }

        // Update handle visual.
        handle.anchoredPosition = localPoint;

        // Forward the direction to the local player.
        SendMoveVector(direction);
    }

    /// <inheritdoc/>
    public void OnPointerUp(PointerEventData eventData)
    {
        // Snap handle back to centre.
        handle.anchoredPosition = Vector2.zero;

        // Stop the player's movement.
        SendMoveVector(Vector2.zero);
    }

    // ====================================================================
    // Private – Helpers
    // ====================================================================

    /// <summary>
    /// Searches all active <see cref="PlayerMovement"/> and <see cref="Guard"/> objects
    /// for one owned by the local client and caches the reference.
    /// Handles both first-time discovery and post-reset re-discovery (when both cached
    /// references have become <c>null</c>).
    /// </summary>
    private void FindLocalPlayer()
    {
        _localPlayerMovement = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
            .FirstOrDefault(p => p.IsOwner);

        if (_localPlayerMovement != null)
        {
            _localGuard = null;
            _isPlayerFound = true;
            Debug.Log("[VirtualJoystick] Connected to local Runner (PlayerMovement).");
            return;
        }

        _localGuard = FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
            .FirstOrDefault(g => g.IsOwner);

        if (_localGuard != null)
        {
            _isPlayerFound = true;
            Debug.Log("[VirtualJoystick] Connected to local Guard (Catcher).");
            return;
        }

        _isPlayerFound = false;
    }

    /// <summary>
    /// Forwards <paramref name="direction"/> to whichever local player script is cached.
    /// </summary>
    private void SendMoveVector(Vector2 direction)
    {
        if (_localPlayerMovement != null)
            _localPlayerMovement.SetMoveVector(direction);
        else if (_localGuard != null)
            _localGuard.SetMoveVector(direction);
    }
}