/// <summary>
/// Common contract for all pressure-plate zone types managed by <see cref="ButtonManager"/>.
/// Implement this interface on any <see cref="Unity.Netcode.NetworkBehaviour"/> that acts
/// as an activatable zone so <see cref="ButtonManager"/> can manage it without knowing
/// the concrete type.
/// </summary>
public interface IButtonZone
{
    /// <summary>
    /// Resets progress and deactivates the zone so it can be reused after its cooldown.
    /// Called by <see cref="ButtonManager"/> after the zone is completed or the cooldown expires.
    /// </summary>
    void ResetButton();

    /// <summary>
    /// Server-only: removes a player from this zone's occupancy tracking without
    /// relying on a physical <c>OnTriggerExit</c>. Called by <see cref="ButtonManager"/>
    /// when a player's <see cref="Unity.Netcode.NetworkObject"/> despawns (e.g. on
    /// disconnect) while still standing in the zone, which never fires a trigger exit
    /// and would otherwise leave the zone stuck thinking that player is still present.
    /// </summary>
    void RemovePlayer(ulong networkObjectId);
}