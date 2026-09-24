using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

/// <summary>
/// Assigns Input System devices to each local player slot for dual-screen
/// local multiplayer.
/// </summary>
public class ControlSetupManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="ControlSetupManager"/>.</summary>
    public static ControlSetupManager Instance { get; private set; }

    // ====================================================================
    // Constants
    // ====================================================================

    // Fallback scheme names used when auto-detection cannot find a match.
    // These must match your Input Action Asset's Control Scheme names exactly.
    // If you see "Cannot find control scheme" errors, update these values to
    // match what you named your schemes (e.g. "Keyboard&Mouse", "Controller", etc.)
    internal const string FallbackSchemeKeyboard = "Keyboard";
    internal const string FallbackSchemeGamepad = "Gamepad";
    private const int MaxRetryFrames = 10;

    // ====================================================================
    // Private State
    // ====================================================================

    /// <summary>
    /// Maps a global player index to the primary <see cref="InputDevice"/> for that slot.
    /// Global index = runner slots first, then catcher slots.
    /// e.g. 2 runners + 1 catcher -> indices 0,1 = runners, index 2 = catcher.
    /// </summary>
    private readonly Dictionary<int, InputDevice> _playerDevices = new Dictionary<int, InputDevice>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }

        Instance = this;

        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        foreach (var device in _playerDevices.Values)
            if (device != null) ReleaseDevice(device);

        _playerDevices.Clear();
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Assigns the appropriate control scheme and device to <paramref name="player"/>'s
    /// <see cref="PlayerInput"/> component.
    /// </summary>
    /// <param name="player">GameObject that owns a <see cref="PlayerInput"/> component.</param>
    /// <param name="globalIndex">
    /// Zero-based global player index.
    /// Runner indices start at 0; Catcher indices start at <see cref="GameSettings.LocalRunnerCount"/>.
    /// </param>
    public void AssignControlScheme(GameObject player, int globalIndex)
    {
        if (!player.TryGetComponent<PlayerInput>(out var pInput)) return;

        if (pInput.user.valid)
            AssignInternal(pInput, globalIndex);
        else
            StartCoroutine(RetryAssign(pInput, globalIndex));
    }

    /// <summary>
    /// Convenience wrapper for Runner players.
    /// <paramref name="runnerIndex"/> is the zero-based index within the runner group.
    /// </summary>
    public void AssignRunnerControl(GameObject player, int runnerIndex)
        => AssignControlScheme(player, runnerIndex);

    /// <summary>
    /// Convenience wrapper for Catcher players.
    /// <paramref name="catcherIndex"/> is the zero-based index within the catcher group.
    /// Internally offset by <see cref="GameSettings.LocalRunnerCount"/>.
    /// </summary>
    public void AssignCatcherControl(GameObject player, int catcherIndex)
        => AssignControlScheme(player, GameSettings.LocalRunnerCount + catcherIndex);

    /// <summary>Releases the device assigned to <paramref name="globalIndex"/>.</summary>
    public void ReleasePlayerDevice(int globalIndex)
    {
        if (!_playerDevices.TryGetValue(globalIndex, out var device)) return;
        _playerDevices.Remove(globalIndex);
        ReleaseDevice(device);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="globalIndex"/> has an assigned device.
    /// </summary>
    public bool HasControlAssigned(int globalIndex)
        => _playerDevices.TryGetValue(globalIndex, out var d) && d != null;

    /// <summary>
    /// Returns the primary <see cref="InputDevice"/> for <paramref name="globalIndex"/>,
    /// or <c>null</c> if none is assigned.
    /// </summary>
    public InputDevice GetPlayerDevice(int globalIndex)
        => _playerDevices.TryGetValue(globalIndex, out var d) ? d : null;

    /// <summary>Logs all current device assignments to the Unity console.</summary>
    public void DebugCurrentDevices()
    {
        Debug.Log("=== ControlSetupManager – Current Devices ===");

        if (_playerDevices.Count == 0)
        {
            Debug.Log("  No devices assigned yet.");
        }
        else
        {
            int runnerCount = GameSettings.LocalRunnerCount;
            int catcherCount = GameSettings.LocalCatcherCount;

            foreach (var kvp in _playerDevices.OrderBy(k => k.Key))
            {
                string role = kvp.Key < runnerCount ? "Runner" : "Catcher";
                int local = kvp.Key < runnerCount
                    ? kvp.Key
                    : kvp.Key - runnerCount;
                string device = kvp.Value is Gamepad ? $"Gamepad ({kvp.Value.name})" :
                                kvp.Value is Keyboard ? $"Keyboard ({kvp.Value.name})" : kvp.Value.name;
                Debug.Log($"  [{role} {local + 1}] global={kvp.Key} -> {device}");
            }
        }

        Debug.Log($"  Gamepads connected: {Gamepad.all.Count}");
        Debug.Log($"  Keyboard available: {Keyboard.current != null}");
    }

    // ====================================================================
    // Private – Assignment
    // ====================================================================

    private void AssignInternal(PlayerInput pInput, int globalIndex)
    {
        try
        {
            // Clear any previous pairing cleanly.
            if (pInput.user.valid)
                pInput.user.UnpairDevices();

            // Honour the device the player physically used to join the session.
            // SelectDeviceFor is only used as a fallback if no registration exists.
            InputDevice registeredDevice = LocalPlayerManager.Instance?.GetDevice(globalIndex);
            InputDevice device = (registeredDevice != null && !_playerDevices.ContainsValue(registeredDevice))
                ? registeredDevice
                : SelectDeviceFor(globalIndex);

            if (device == null)
            {
                Debug.LogWarning($"[ControlSetupManager] No device available for global index {globalIndex}. " +
                                 "Player will have no dedicated input.");
                ApplyFallback(pInput, globalIndex);
                return;
            }

            // Pair primary device.
            InputUser.PerformPairingWithDevice(device, pInput.user);

            if (device is Keyboard)
            {
                // Pair mouse alongside keyboard.
                if (Mouse.current != null)
                    InputUser.PerformPairingWithDevice(Mouse.current, pInput.user);

                pInput.SwitchCurrentControlScheme(
                    FindKeyboardScheme(pInput),
                    Mouse.current != null
                        ? new InputDevice[] { Keyboard.current, Mouse.current }
                        : new InputDevice[] { Keyboard.current });

                Debug.Log($"[ControlSetupManager] Global {globalIndex} -> Keyboard+Mouse");
            }
            else if (device is Gamepad)
            {
                pInput.SwitchCurrentControlScheme(FindGamepadScheme(pInput), device);
                Debug.Log($"[ControlSetupManager] Global {globalIndex} -> Gamepad: {device.name}");
            }

            _playerDevices[globalIndex] = device;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ControlSetupManager] Error assigning index {globalIndex}: {e.Message}");
            ApplyFallback(pInput, globalIndex);
        }
    }

    /// <summary>
    /// Selects the best available device for a given global player index.
    ///
    /// Assignment table (example: 2 runners, 1 catcher):
    /// <code>
    ///   Global 0 (Runner 1) -> Keyboard+Mouse
    ///   Global 1 (Runner 2) -> Gamepad[0]
    ///   Global 2 (Catcher 1) -> Gamepad[1]
    /// </code>
    ///
    /// The keyboard slot is always global index 0 (Runner 1).
    /// All other slots draw from the gamepad pool in connection order.
    /// </summary>
    private InputDevice SelectDeviceFor(int globalIndex)
    {
        // Build the available gamepad pool (excluding already-assigned ones).
        var availableGamepads = Gamepad.all
            .Where(gp => !_playerDevices.ContainsValue(gp))
            .ToList();

        bool kbFree = Keyboard.current != null
                   && !_playerDevices.ContainsValue(Keyboard.current);

        // Global index 0 always gets keyboard (Runner 1).
        if (globalIndex == 0 && kbFree)
            return Keyboard.current;

        // All other slots get a gamepad.
        // The gamepad index within the pool is:
        //   globalIndex - 1  (because index 0 consumed the keyboard).
        int gpPoolIndex = globalIndex > 0 ? globalIndex - 1 : 0;
        return gpPoolIndex < availableGamepads.Count
            ? availableGamepads[gpPoolIndex]
            : null;
    }

    private IEnumerator RetryAssign(PlayerInput pInput, int globalIndex)
    {
        for (int i = 0; i < MaxRetryFrames; i++)
        {
            if (pInput.user.valid)
            {
                AssignInternal(pInput, globalIndex);
                yield break;
            }
            yield return null;
        }

        Debug.LogError($"[ControlSetupManager] InputUser never became valid for global index {globalIndex}. " +
                       "Using emergency fallback.");
        EmergencyAssign(pInput, globalIndex);
    }

    // ====================================================================
    // Private – Fallbacks
    // ====================================================================

    private void ApplyFallback(PlayerInput pInput, int globalIndex)
    {
        try
        {
            bool kbUsed = _playerDevices.ContainsValue(Keyboard.current);
            if (Keyboard.current != null && !kbUsed)
            {
                if (Mouse.current != null)
                    InputUser.PerformPairingWithDevice(Mouse.current, pInput.user);

                pInput.SwitchCurrentControlScheme(FindKeyboardScheme(pInput),
                    Mouse.current != null
                        ? new InputDevice[] { Keyboard.current, Mouse.current }
                        : new InputDevice[] { Keyboard.current });

                Debug.LogWarning($"[ControlSetupManager] Global {globalIndex} -> Shared keyboard (fallback).");
            }
            else
            {
                Debug.LogWarning($"[ControlSetupManager] Global {globalIndex} -> No input assigned.");
            }
        }
        catch
        {
            // Swallow – the game must continue regardless.
        }
    }

    private void EmergencyAssign(PlayerInput pInput, int globalIndex)
    {
        Debug.LogWarning($"[ControlSetupManager] Emergency assignment for global index {globalIndex}.");

        var gamepads = Gamepad.all;
        if (globalIndex < gamepads.Count)
            pInput.SwitchCurrentControlScheme(FindGamepadScheme(pInput), gamepads[globalIndex]);
        else if (Keyboard.current != null)
            pInput.SwitchCurrentControlScheme(FindKeyboardScheme(pInput), Keyboard.current, Mouse.current);
    }


    // ====================================================================
    // Private – Scheme Name Auto-Detection
    // ====================================================================

    /// <summary>
    /// Returns the control scheme name for Keyboard+Mouse from
    /// <paramref name="pInput"/>'s action asset.
    /// Searches for a scheme whose name contains "keyboard" (case-insensitive).
    /// Falls back to <see cref="FallbackSchemeKeyboardMouse"/> if not found.
    /// </summary>
    private static string FindKeyboardScheme(PlayerInput pInput)
    {
        if (pInput?.actions == null) return FallbackSchemeKeyboard;

        foreach (var scheme in pInput.actions.controlSchemes)
        {
            string lower = scheme.name.ToLowerInvariant();
            if (lower.Contains("keyboard"))
                return scheme.name;
        }

        Debug.LogWarning($"[ControlSetupManager] No Keyboard+Mouse scheme found in " +
                         $"'{pInput.actions.name}'. " +
                         $"Available schemes: {ListSchemes(pInput)} " +
                         $"Falling back to '{FallbackSchemeKeyboard}'.");
        return FallbackSchemeKeyboard;
    }

    /// <summary>
    /// Returns the control scheme name for Gamepad from
    /// <paramref name="pInput"/>'s action asset.
    /// Searches for a scheme whose name contains "gamepad" or "controller".
    /// Falls back to <see cref="FallbackSchemeGamepad"/> if not found.
    /// </summary>
    private static string FindGamepadScheme(PlayerInput pInput)
    {
        if (pInput?.actions == null) return FallbackSchemeGamepad;

        foreach (var scheme in pInput.actions.controlSchemes)
        {
            string lower = scheme.name.ToLowerInvariant();
            if (lower.Contains("gamepad") || lower.Contains("controller"))
                return scheme.name;
        }

        Debug.LogWarning($"[ControlSetupManager] No Gamepad scheme found in " +
                         $"'{pInput.actions.name}'. " +
                         $"Available schemes: {ListSchemes(pInput)} " +
                         $"Falling back to '{FallbackSchemeGamepad}'.");
        return FallbackSchemeGamepad;
    }

    private static string ListSchemes(PlayerInput pInput)
    {
        if (pInput?.actions == null) return "(none)";
        var names = new System.Collections.Generic.List<string>();
        foreach (var s in pInput.actions.controlSchemes)
            names.Add($"'{s.name}'");
        return string.Join(", ", names);
    }

    // ====================================================================
    // Private – Device Release
    // ====================================================================

    private static void ReleaseDevice(InputDevice device)
    {
        foreach (var user in InputUser.all)
        {
            if (user.valid && user.pairedDevices.Contains(device))
            {
                user.UnpairDevice(device);
                break;
            }
        }
    }
}