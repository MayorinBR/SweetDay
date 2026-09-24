using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static container for global game-configuration values that persist across scene.
/// </summary>
public static class GameSettings
{
    // ====================================================================
    // Private Backing Fields
    // ====================================================================

    private static int _localRunnerCount = 1;
    private static int _localCatcherCount = 0;

    // ====================================================================
    // Properties – Runner / Catcher Counts
    // ====================================================================

    /// <summary>
    /// Number of Runner players sharing Screen 1 (1–4).
    /// Automatically clamped to the valid range on assignment.
    /// </summary>
    public static int LocalRunnerCount
    {
        get => _localRunnerCount;
        set => _localRunnerCount = Mathf.Clamp(value, 1, 4);
    }

    /// <summary>
    /// Number of Catcher (Guard) players sharing Screen 2 (0–2).
    /// 0 means no local catchers (catcher may be remote or absent).
    /// Automatically clamped to the valid range on assignment.
    /// </summary>
    public static int LocalCatcherCount
    {
        get => _localCatcherCount;
        set => _localCatcherCount = Mathf.Clamp(value, 0, 2);
    }

    // ====================================================================
    // Properties – Derived / Compatibility
    // ====================================================================

    /// <summary>
    /// Total number of local players (runners + catchers).
    /// Setting this value assigns all slots to Runners and resets the catcher count
    /// to zero.  Kept for backwards compatibility; prefer setting
    /// <see cref="LocalRunnerCount"/> and <see cref="LocalCatcherCount"/> directly.
    /// </summary>
    public static int LocalPlayerCount
    {
        get => _localRunnerCount + _localCatcherCount;
        set
        {
            // Legacy behaviour: treat the whole count as runners.
            _localRunnerCount = Mathf.Clamp(value, 1, 4);
            _localCatcherCount = 0;
        }
    }

    /// <summary>
    /// The game scene (map) selected by the host in the lobby.
    /// Written by <see cref="LobbyStateManager.PersistSlotAssignments"/> before
    /// the game scene loads.
    /// </summary>
    public static string SelectedScene = "TestScene_Flat";

    // ====================================================================
    // Slot Assignment Record
    // ====================================================================

    /// <summary>
    /// Carries all data needed to spawn one player in the game scene.
    /// Written by <see cref="LobbyStateManager.PersistSlotAssignments"/> before
    /// the scene transition and read by <see cref="GameManager"/>.
    /// </summary>
    public struct SlotAssignment
    {
        /// <summary>Network clientId of the machine that owns this slot.</summary>
        public ulong ClientId;

        /// <summary>
        /// Zero-based index of the physical controller on that machine.
        /// 0 = keyboard/mouse, 1–5 = gamepads.
        /// Used by <see cref="ControlSetupManager"/> to pair the correct device.
        /// </summary>
        public int LocalPlayerIndex;

        /// <summary>Lobby slot index (0–5). Slots 0–3 = Runner, 4–5 = Catcher.</summary>
        public int SlotIndex;
    }

    /// <summary>
    /// Ordered list of slot assignments persisted before the game scene loads.
    /// Sorted by <see cref="SlotAssignment.SlotIndex"/> ascending so P1 always
    /// spawns before P2 etc.
    /// </summary>
    public static List<SlotAssignment> SlotAssignments = new List<SlotAssignment>();

    /// <summary>
    /// Legacy dictionary kept so existing code that references
    /// <c>ClientSlotAssignments</c> still compiles. Populated from
    /// <see cref="SlotAssignments"/> by <see cref="GameManager"/>.
    /// </summary>
    public static Dictionary<ulong, int> ClientSlotAssignments = new Dictionary<ulong, int>();

    /// <summary>Maximum local Runner slots (Screen 1).</summary>
    public const int MaxRunnerSlots = LobbyStateManager.RunnerSlotCount;

    /// <summary>Maximum local Catcher slots (Screen 2).</summary>
    public const int MaxCatcherSlots = LobbyStateManager.CatcherSlotCount;
    /// <summary>
    /// Whether the local host's primary role is Catcher.
    /// <c>false</c> for the Runner role.
    /// </summary>
    public static bool IsCatcher = false;

    /// <summary>
    /// <c>true</c> when more than one Runner player shares Screen 1,
    /// indicating that split-screen camera setup is required on that display.
    /// </summary>
    public static bool IsRunnerSplitScreen => _localRunnerCount > 1;

    /// <summary>
    /// <c>true</c> when more than one Catcher player shares Screen 2.
    /// </summary>
    public static bool IsCatcherSplitScreen => _localCatcherCount > 1;

    /// <summary>
    /// <c>true</c> when any form of local multiplayer is active
    /// (either multiple runners or at least one local catcher).
    /// </summary>
    public static bool IsLocalMultiplayer => _localRunnerCount > 1 || _localCatcherCount > 0;

    /// <summary>
    /// <c>true</c> when a second physical display is available and should be used
    /// for the Catcher screen.
    /// </summary>
    public static bool UseSecondDisplay => _localCatcherCount > 0;
}