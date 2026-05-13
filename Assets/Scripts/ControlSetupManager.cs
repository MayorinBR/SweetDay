using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

/// <summary>
/// Assigns input devices (keyboard/mouse or gamepads) to each local player slot.
/// Persists across scene loads so device assignments survive scene transitions.
///
/// Distribution logic:
///   - If there are fewer gamepads than local players, the earliest player indices
///     are assigned the keyboard; the remaining players receive gamepads in order.
///   - Example: 4 local players, 3 gamepads -> Player 0 = Keyboard, Players 1-3 = Gamepads.
/// </summary>
public class ControlSetupManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="ControlSetupManager"/>.</summary>
    public static ControlSetupManager Instance { get; private set; }

    // ====================================================================
    // Private State
    // ====================================================================

    /// <summary>Maps a player index to the <see cref="InputDevice"/> assigned to that slot.</summary>
    private readonly Dictionary<int, InputDevice> _playerDevices = new Dictionary<int, InputDevice>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        foreach (var device in _playerDevices.Values)
        {
            if (device != null) ReleaseDevice(device);
        }
        _playerDevices.Clear();
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Assigns the appropriate control scheme and input device to the
    /// <see cref="PlayerInput"/> component on <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The player GameObject that has a <see cref="PlayerInput"/> component.</param>
    /// <param name="playerIndex">Zero-based local player index.</param>
    public void AssignControlScheme(GameObject player, int playerIndex)
    {
        if (!player.TryGetComponent<PlayerInput>(out var pInput)) return;

        // Try immediate assignment; if the user is not yet valid, retry over several frames.
        if (pInput.user.valid)
            AssignControlSchemeInternal(pInput, playerIndex);
        else
            StartCoroutine(DelayedAssignment(pInput, playerIndex));
    }

    /// <summary>
    /// Releases the device assigned to <paramref name="playerIndex"/> and removes the mapping.
    /// </summary>
    public void ReleasePlayerDevice(int playerIndex)
    {
        if (!_playerDevices.TryGetValue(playerIndex, out var device)) return;
        _playerDevices.Remove(playerIndex);
        ReleaseDevice(device);
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="playerIndex"/> has an assigned input device.
    /// </summary>
    public bool HasControlAssigned(int playerIndex)
        => _playerDevices.TryGetValue(playerIndex, out var d) && d != null;

    /// <summary>
    /// Returns the <see cref="InputDevice"/> assigned to <paramref name="playerIndex"/>,
    /// or <c>null</c> if none is assigned.
    /// </summary>
    public InputDevice GetPlayerDevice(int playerIndex)
        => _playerDevices.TryGetValue(playerIndex, out var d) ? d : null;

    /// <summary>Logs the current device assignments and available hardware to the console.</summary>
    public void DebugCurrentDevices()
    {
        Debug.Log("=== ControlSetupManager – Current Devices ===");
        if (_playerDevices.Count == 0)
        {
            Debug.Log("  No devices assigned yet.");
        }
        else
        {
            foreach (var kvp in _playerDevices)
            {
                string type = kvp.Value is Gamepad ? "Gamepad" :
                              kvp.Value is Keyboard ? "Keyboard" : "Unknown";
                Debug.Log($"  Player {kvp.Key + 1}: {type} – {kvp.Value?.name ?? "null"}");
            }
        }

        Debug.Log($"  Gamepads connected: {Gamepad.all.Count}");
        for (int i = 0; i < Gamepad.all.Count; i++)
            Debug.Log($"    [{i}] {Gamepad.all[i].name}");

        Debug.Log($"  Keyboard available: {(Keyboard.current != null ? "Yes" : "No")}");
    }

    // ====================================================================
    // Private – Assignment Logic
    // ====================================================================

    /// <summary>
    /// Retries device assignment over multiple frames, waiting until the
    /// <see cref="InputUser"/> is valid.
    /// </summary>
    private IEnumerator DelayedAssignment(PlayerInput pInput, int playerIndex)
    {
        const int maxAttempts = 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            if (pInput.user.valid)
            {
                AssignControlSchemeInternal(pInput, playerIndex);
                yield break;
            }
            yield return null;
        }

        Debug.LogError($"[ControlSetupManager] Failed to assign controls for Player {playerIndex} " +
                       $"after {maxAttempts} attempts. Using emergency fallback.");
        EmergencyAssignment(pInput, playerIndex);
    }

    private void AssignControlSchemeInternal(PlayerInput pInput, int playerIndex)
    {
        try
        {
            if (pInput.user.valid)
                pInput.user.UnpairDevices();

            InputDevice device = GetDeviceForPlayer(playerIndex);

            if (device == null)
            {
                Debug.LogWarning($"[ControlSetupManager] No device available for Player {playerIndex + 1}. " +
                                 "Using minimal fallback.");
                MinimalFallback(pInput, playerIndex);
                return;
            }

            InputUser.PerformPairingWithDevice(device, pInput.user);

            if (device is Gamepad)
            {
                pInput.SwitchCurrentControlScheme("Gamepad", device);
                Debug.Log($"[ControlSetupManager] Player {playerIndex + 1} -> Gamepad: {device.name}");
            }
            else if (device is Keyboard)
            {
                if (Mouse.current != null)
                {
                    InputUser.PerformPairingWithDevice(Mouse.current, pInput.user);
                    pInput.SwitchCurrentControlScheme("KeyboardMouse", Keyboard.current, Mouse.current);
                    Debug.Log($"[ControlSetupManager] Player {playerIndex + 1} -> Keyboard + Mouse");
                }
                else
                {
                    pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current);
                    Debug.Log($"[ControlSetupManager] Player {playerIndex + 1} -> Keyboard (no mouse)");
                }
            }

            _playerDevices[playerIndex] = device;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ControlSetupManager] Error assigning controls for Player {playerIndex}: {e.Message}");
            MinimalFallback(pInput, playerIndex);
        }
    }

    /// <summary>
    /// Selects the best available device for the given player index, respecting
    /// the distribution rule: keyboard first for slots without a gamepad.
    /// </summary>
    private InputDevice GetDeviceForPlayer(int playerIndex)
    {
        var gamepads = Gamepad.all.ToList();
        bool kbAvailable = Keyboard.current != null && !_playerDevices.ContainsValue(Keyboard.current);
        int totalLocal = GameSettings.LocalPlayerCount;
        int kbSlots = Mathf.Max(0, totalLocal - gamepads.Count);

        // Remove already-assigned gamepads from the pool.
        foreach (var d in _playerDevices.Values)
        {
            if (d is Gamepad gp) gamepads.Remove(gp);
        }

        if (playerIndex < kbSlots)
        {
            // This slot uses the keyboard.
            return kbAvailable ? (InputDevice)Keyboard.current : null;
        }
        else
        {
            // This slot uses a gamepad.
            int gpIndex = playerIndex - kbSlots;
            return gpIndex < gamepads.Count ? gamepads[gpIndex] : null;
        }
    }

    private void MinimalFallback(PlayerInput pInput, int playerIndex)
    {
        try
        {
            if (Keyboard.current != null && !_playerDevices.ContainsValue(Keyboard.current))
            {
                pInput.SwitchCurrentControlScheme("KeyboardMouse", Keyboard.current, Mouse.current);
                Debug.LogWarning($"[ControlSetupManager] Player {playerIndex + 1} -> Shared keyboard (fallback).");
            }
            else
            {
                Debug.LogWarning($"[ControlSetupManager] Player {playerIndex + 1} -> No control assigned.");
            }
        }
        catch
        {
            // Swallow – the game must continue even without a fully configured device.
        }
    }

    private void EmergencyAssignment(PlayerInput pInput, int playerIndex)
    {
        Debug.LogWarning($"[ControlSetupManager] Emergency assignment for Player {playerIndex + 1}.");

        var gamepads = Gamepad.all;
        if (playerIndex < gamepads.Count)
            pInput.SwitchCurrentControlScheme("Gamepad", gamepads[playerIndex]);
        else if (Keyboard.current != null)
            pInput.SwitchCurrentControlScheme("KeyboardMouse", Keyboard.current, Mouse.current);
    }

    private static void PairDeviceToPlayer(PlayerInput pInput, InputDevice device, string scheme)
    {
        pInput.user.UnpairDevices();
        InputUser.PerformPairingWithDevice(device, pInput.user);
        pInput.SwitchCurrentControlScheme(scheme, device);
    }

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
