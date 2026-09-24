using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the activation of two physical displays and routes player cameras
/// to the correct screen.
/// </summary>
public class MultiScreenManager : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="MultiScreenManager"/>.</summary>
    public static MultiScreenManager Instance { get; private set; }

    // ====================================================================
    // Constants
    // ====================================================================

    /// <summary>Unity Display index used for the Runner screen.</summary>
    public const int RunnerDisplayIndex = 0;

    /// <summary>Unity Display index used for the Catcher screen.</summary>
    public const int CatcherDisplayIndex = 1;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        ActivateDisplays();
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Activates Display 1 unconditionally, and activates Display 2 when
    /// <see cref="GameSettings.UseSecondDisplay"/> is <c>true</c>.
    /// Safe to call multiple times; subsequent calls on an already-active display
    /// are no-ops.
    /// </summary>
    public void ActivateDisplays()
    {
        // Display 1 is always active by default in Unity; explicit activation
        // is harmless and makes the intent clear.
        if (Display.displays.Length > RunnerDisplayIndex)
        {
            Display.displays[RunnerDisplayIndex].Activate();
            Debug.Log("[MultiScreenManager] Display 1 (Runner screen) activated.");
        }

        if (GameSettings.UseSecondDisplay)
        {
            if (Display.displays.Length > CatcherDisplayIndex)
            {
                Display.displays[CatcherDisplayIndex].Activate();
                Debug.Log("[MultiScreenManager] Display 2 (Catcher screen) activated.");
            }
            else
            {
                Debug.LogWarning("[MultiScreenManager] Display 2 requested but not available. " +
                                 "Make sure a second monitor is connected before launching the build. " +
                                 "In the Editor, open a second Game View and set it to Display 2.");
            }
        }
    }

    /// <summary>
    /// Routes <paramref name="cam"/> to the correct display based on whether it
    /// is tracking a Runner or a Catcher.
    /// </summary>
    /// <param name="cam">The camera to route.</param>
    /// <param name="isRunnerCamera">
    /// <c>true</c> if the camera tracks a Runner (Display 1);
    /// <c>false</c> if it tracks a Catcher (Display 2).
    /// </param>
    public void AssignCameraToDisplay(Camera cam, bool isRunnerCamera)
    {
        if (cam == null) return;
        cam.targetDisplay = isRunnerCamera ? RunnerDisplayIndex : CatcherDisplayIndex;
    }

    /// <summary>
    /// Returns <c>true</c> when a second physical display is detected at runtime.
    /// Always returns <c>true</c> in the Editor (the second Game View acts as the display).
    /// </summary>
    public bool IsSecondDisplayAvailable()
    {
#if UNITY_EDITOR
        return true;
#else
        return Display.displays.Length > CatcherDisplayIndex;
#endif
    }
}