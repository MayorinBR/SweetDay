using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Displays the player's number (e.g. "P1", "P2") in a world-space text label
/// that always faces the main camera.
/// </summary>
public class PlayerOverheadUI : NetworkBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [SerializeField, Tooltip("TextMeshProUGUI component that renders the player-number label. " +
                              "Auto-detected from children if left empty.")]
    private TextMeshProUGUI idText;

    // ====================================================================
    // Private
    // ====================================================================

    private PlayerMovement _playerMovement;
    private Guard _guard;

    // ====================================================================
    // NetworkBehaviour
    // ====================================================================

    /// <inheritdoc/>
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Auto-find the text component if not wired in the Inspector.
        if (idText == null)
            idText = GetComponentInChildren<TextMeshProUGUI>();

        // Locate the owning character script in the parent hierarchy.
        _playerMovement = GetComponentInParent<PlayerMovement>();
        _guard = GetComponentInParent<Guard>();

        // Subscribe to future value changes so the label stays current.
        if (_playerMovement != null)
            _playerMovement.playerNumber.OnValueChanged += OnPlayerNumberChanged;
        else if (_guard != null)
            _guard.playerNumber.OnValueChanged += OnPlayerNumberChanged;

        // Render the value that is already available at spawn time.
        UpdateDisplay();
    }

    /// <inheritdoc/>
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_playerMovement != null)
            _playerMovement.playerNumber.OnValueChanged -= OnPlayerNumberChanged;
        else if (_guard != null)
            _guard.playerNumber.OnValueChanged -= OnPlayerNumberChanged;
    }

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void LateUpdate()
    {
        // Keep the label facing the main camera every frame (billboard effect).
        if (Camera.main == null) return;

        transform.LookAt(
            transform.position + Camera.main.transform.rotation * Vector3.forward,
            Camera.main.transform.rotation * Vector3.up);
    }

    // ====================================================================
    // Private EHelpers
    // ====================================================================

    private void OnPlayerNumberChanged(int previous, int current) => UpdateDisplay();

    /// <summary>
    /// Reads the current <c>playerNumber</c> from whichever character script is present
    /// and refreshes the label text and colour.
    /// </summary>
    private void UpdateDisplay()
    {
        if (idText == null) return;

        int id = 0;

        if (_playerMovement != null)
            id = _playerMovement.playerNumber.Value;
        else if (_guard != null)
            id = _guard.playerNumber.Value;

        // Fall back to OwnerClientId if the networked value hasn't arrived yet.
        if (id == 0)
            id = (int)OwnerClientId + 1;

        idText.text = $"P{id}";
        idText.color = _playerMovement != null ? Color.yellow : Color.red;
    }
}
