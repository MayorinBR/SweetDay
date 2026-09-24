using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Manages local player registration for split-screen play on a single machine.
/// Supports up to <see cref="MaxLocalPlayers"/> simultaneous controllers.
///
/// This object persists across scenes via <c>DontDestroyOnLoad</c>.
/// </summary>
public class LocalPlayerManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="LocalPlayerManager"/>.</summary>
    public static LocalPlayerManager Instance { get; private set; }

    // ====================================================================
    // Constants
    // ====================================================================

    /// <summary>Maximum local players on one machine (4 Runners + 2 Catchers).</summary>
    public const int MaxLocalPlayers = 6;

    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Join Button")]
    [Tooltip("Gamepad button players press to join. Default: Button South (A/Cross).")]
    [SerializeField] private GamepadButton gamepadJoinButton = GamepadButton.South;

    [Tooltip("Keyboard key players press to join. Default: Enter.")]
    [SerializeField] private Key keyboardJoinKey = Key.Enter;

    [Header("Debug")]
    [SerializeField] private bool logDeviceChanges = true;

    // ====================================================================
    // Events
    // ====================================================================

    /// <summary>Fired on the main thread when a new local player joins.</summary>
    public event System.Action<int, InputDevice> OnLocalPlayerJoined;

    /// <summary>Fired when a registered local player's device disconnects.</summary>
    public event System.Action<int> OnLocalPlayerLeft;

    // ====================================================================
    // Private
    // ====================================================================

    /// <summary>localPlayerIndex → registered InputDevice.</summary>
    private readonly Dictionary<int, InputDevice> _registeredDevices
        = new Dictionary<int, InputDevice>();

    /// <summary>Set of devices already registered (fast look-up).</summary>
    private readonly HashSet<InputDevice> _registeredSet = new HashSet<InputDevice>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);

        InputSystem.onDeviceChange += OnDeviceChange;
    }

    private void OnDestroy()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        PollForNewJoins();
    }

    // ====================================================================
    // Private – Join Detection
    // ====================================================================

    /// <summary>
    /// Each frame, checks every connected device for the join button.
    /// Registers the device if it is not already registered and the slot is free.
    /// </summary>
    private void PollForNewJoins()
    {
        if (_registeredDevices.Count >= MaxLocalPlayers) return;

        // ── Keyboard ─────────────────────────────────────────────────────
        if (Keyboard.current != null
            && !_registeredSet.Contains(Keyboard.current)
            && Keyboard.current[keyboardJoinKey].wasPressedThisFrame)
        {
            RegisterDevice(Keyboard.current);
            return;
        }

        // ── Gamepads ──────────────────────────────────────────────────────
        foreach (var gp in Gamepad.all)
        {
            if (_registeredSet.Contains(gp)) continue;
            if (gp[gamepadJoinButton].wasPressedThisFrame)
            {
                RegisterDevice(gp);
                return; // One join per frame to keep logs clean.
            }
        }
    }

    private void RegisterDevice(InputDevice device)
    {
        if (_registeredSet.Contains(device)) return;
        if (_registeredDevices.Count >= MaxLocalPlayers) return;

        // Assign the lowest available index.
        int localIndex = GetNextFreeIndex();

        _registeredDevices[localIndex] = device;
        _registeredSet.Add(device);

        if (logDeviceChanges)
            Debug.Log($"[LocalPlayerManager] Player {localIndex} joined: " +
                      $"{device.GetType().Name} – {device.displayName}");

        OnLocalPlayerJoined?.Invoke(localIndex, device);
    }

    private int GetNextFreeIndex()
    {
        for (int i = 0; i < MaxLocalPlayers; i++)
            if (!_registeredDevices.ContainsKey(i)) return i;
        return _registeredDevices.Count;
    }

    // ====================================================================
    // Private – Device Change
    // ====================================================================

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (change != InputDeviceChange.Disconnected
            && change != InputDeviceChange.Removed) return;

        if (!_registeredSet.Contains(device)) return;

        int localIndex = GetLocalIndex(device);
        _registeredDevices.Remove(localIndex);
        _registeredSet.Remove(device);

        if (logDeviceChanges)
            Debug.Log($"[LocalPlayerManager] Player {localIndex} left (device disconnected).");

        OnLocalPlayerLeft?.Invoke(localIndex);
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>Returns the <see cref="InputDevice"/> for <paramref name="localIndex"/>, or null.</summary>
    public InputDevice GetDevice(int localIndex)
        => _registeredDevices.TryGetValue(localIndex, out var d) ? d : null;

    /// <summary>Returns the local index for a given <see cref="InputDevice"/>, or -1.</summary>
    public int GetLocalIndex(InputDevice device)
    {
        foreach (var kv in _registeredDevices)
            if (kv.Value == device) return kv.Key;
        return -1;
    }

    /// <summary>Total number of registered local players.</summary>
    public int RegisteredCount => _registeredDevices.Count;

    /// <summary>Iterates all registered (localIndex, device) pairs.</summary>
    public IEnumerable<(int localIndex, InputDevice device)> GetAll()
    {
        foreach (var kv in _registeredDevices)
            yield return (kv.Key, kv.Value);
    }

    /// <summary>
    /// Assigns the correct Input System control scheme to <paramref name="playerObject"/>
    /// based on <paramref name="localIndex"/>.
    /// Delegates to <see cref="ControlSetupManager.AssignControlScheme"/>.
    /// </summary>
    public void AssignDeviceToPlayer(GameObject playerObject, int localIndex)
    {
        if (ControlSetupManager.Instance != null)
            ControlSetupManager.Instance.AssignControlScheme(playerObject, localIndex);
        else
            Debug.LogWarning($"[LocalPlayerManager] ControlSetupManager not found " +
                             $"for localIndex {localIndex}.");
    }

    /// <summary>
    /// Clears all registrations. Call when returning to the main menu so the
    /// next session starts fresh.
    /// </summary>
    public void ClearAll()
    {
        _registeredDevices.Clear();
        _registeredSet.Clear();
    }
}