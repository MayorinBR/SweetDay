/// <summary>
/// Lightweight value type used to pass lobby slot information between
/// <see cref="LobbyStateManager"/> and <see cref="InteractiveLobbyPanel"/>.
/// Slot indices:
/// <list type="bullet">
///   <item>0–3 → Runner slots P1–P4</item>
///   <item>4–5 → Catcher slots P5–P6</item>
/// </list>
/// </summary>
public readonly struct LobbySlotState
{
    /// <summary>Zero-based slot index (0–5).</summary>
    public readonly int SlotIndex;

    /// <summary><c>true</c> when a player has claimed this slot.</summary>
    public readonly bool IsClaimed;

    /// <summary>
    /// ClientId of the player who claimed this slot.
    /// Only valid when <see cref="IsClaimed"/> is <c>true</c>.
    /// </summary>
    public readonly ulong ClientId;

    // ── Derived helpers ─────────────────────────────────────────────────

    /// <summary><c>true</c> when this slot belongs to the Runner group (indices 0–3).</summary>
    public bool IsRunnerSlot => SlotIndex < LobbyStateManager.RunnerSlotCount;

    /// <summary>Tag shown on the button when the slot is claimed, e.g. "P1", "P5".</summary>
    public string DisplayTag => $"P{SlotIndex + 1}";

    // ── Constructor ──────────────────────────────────────────────────────

    /// <summary>Creates a <see cref="LobbySlotState"/>.</summary>
    public LobbySlotState(int slotIndex, bool isClaimed, ulong clientId)
    {
        SlotIndex = slotIndex;
        IsClaimed = isClaimed;
        ClientId = clientId;
    }
}