using UnityEngine;

/// <summary>
/// Static container for global game-configuration values that must persist
/// across scene loads without requiring a MonoBehaviour or DontDestroyOnLoad.
/// </code>
/// </summary>
public static class GameSettings
{
    // ====================================================================
    // Private Backing Fields
    // ====================================================================

    private static int _localPlayerCount = 1;

    // ====================================================================
    // Properties
    // ====================================================================

    /// <summary>
    /// Number of players sharing the local machine (1ÅE).
    /// Setting this value automatically clamps it to the valid range.
    /// A value of 1 means single-player / online-only mode.
    /// A value greater than 1 activates split-screen mode.
    /// </summary>
    public static int LocalPlayerCount
    {
        get => _localPlayerCount;
        set => _localPlayerCount = Mathf.Clamp(value, 1, 4);
    }

    /// <summary>
    /// <c>true</c> when the host player has chosen the Catcher (Guard) role;
    /// <c>false</c> for the Runner role.
    /// Set before calling <c>StartHostWithScene</c>.
    /// </summary>
    public static bool IsCatcher = false;

    /// <summary>
    /// <c>true</c> when more than one player is sharing the local machine,
    /// indicating that split-screen camera setup is required.
    /// </summary>
    public static bool IsSplitScreen => _localPlayerCount > 1;
}
