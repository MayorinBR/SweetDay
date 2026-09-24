using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Smoothly follows a target <see cref="Transform"/> (normally the local player's object).
/// </summary>
public class CameraFollow : MonoBehaviour
{
    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>Gets or sets the transform this camera should follow.</summary>
    public Transform Target
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>
    /// Positional offset from the target in world space.
    /// Y controls height, Z controls how far behind the player the camera sits.
    /// Keeping Y large and |Z| small produces a top-down angle that minimises
    /// perspective speed distortion between the horizontal and depth axes.
    /// </summary>
    public Vector3 offset = new Vector3(0f, 14f, -5f);

    /// <summary>
    /// Fixed Euler angles applied to the camera every <c>LateUpdate</c>.
    /// X should approximately match <c>arctan(|offset.y| / |offset.z|)</c> so the
    /// camera looks toward the player rather than past them.
    /// </summary>
    public Vector3 cameraRotation = new Vector3(70f, 0f, 0f);

    /// <summary>
    /// Exponential decay rate for position smoothing (units per second).
    /// Higher values make the camera more responsive; lower values add lag.
    /// Unlike a plain Lerp factor this is frame-rate independent.
    /// Typical range: 6 (cinematic lag) – 20 (near-instant).
    /// </summary>
    public float smoothSpeed = 10f;

    // ====================================================================
    // Private
    // ====================================================================

    private Transform _target;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Update()
    {
        if (_target == null)
        {
            FindAndAssignTarget();
            return;
        }

        // If the target's NetworkObject was despawned, look for a new target.
        var netObj = _target.GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
            FindAndAssignTarget();
    }

    private void LateUpdate()
    {
        if (_target == null) return;

        Vector3 desired = _target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));
        transform.rotation = Quaternion.Euler(cameraRotation);
    }

    // ====================================================================
    // Public Helpers
    // ====================================================================

    /// <summary>
    /// Immediately teleports the camera to the target's position without any smoothing.
    /// Call this after the player is teleported or the scene is first loaded to prevent
    /// the camera from sliding in from a stale position.
    /// </summary>
    public void ForcePosition()
    {
        // Re-find a target if we lost the reference.
        if (_target == null)
            FindAndAssignTarget();

        if (_target == null) return;

        transform.position = _target.position + offset;
        Debug.Log($"[CameraFollow] ForcePosition -> {transform.position}  (target: {_target.name})");
    }

    // ====================================================================
    // Private Helpers
    // ====================================================================

    /// <summary>
    /// Scans all active <see cref="NetworkObject"/>s for one owned by the local client
    /// that has a <see cref="PlayerMovement"/> or <see cref="Guard"/> component.
    /// Assigns it as the new target and snaps to its position immediately.
    /// </summary>
    private void FindAndAssignTarget()
    {
        foreach (var netObj in FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude))
        {
            if (!netObj.IsOwner || !netObj.IsSpawned) continue;

            if (netObj.GetComponent<PlayerMovement>() != null ||
                netObj.GetComponent<Guard>() != null)
            {
                _target = netObj.transform;
                ForcePosition();
                Debug.Log($"[CameraFollow] Target assigned -> {netObj.name}");
                return;
            }
        }
    }
}