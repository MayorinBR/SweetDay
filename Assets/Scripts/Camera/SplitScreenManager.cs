using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Defines which player group occupies the screen when only one physical
/// display is detected.
/// </summary>
public enum SingleScreenTarget
{
    /// <summary>Runners are shown on the single screen (default).</summary>
    Runners,
    /// <summary>Catchers are shown on the single screen.</summary>
    Catchers,
}

/// <summary>
/// Creates and manages per-player cameras for local split-screen play
/// In all cases the cameras are tiled to fill their assigned display.
/// </summary>
public class SplitScreenManager : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [SerializeField, Tooltip("Prefab with a Camera + CameraFollow component. Used for all player cameras.")]
    private GameObject cameraPrefab;

    // ====================================================================
    // Private
    // ====================================================================

    /// <summary>All cameras created for Runner players (Display 1).</summary>
    private readonly List<Camera> _runnerCameras = new List<Camera>();

    /// <summary>All cameras created for Catcher players (Display 2).</summary>
    private readonly List<Camera> _catcherCameras = new List<Camera>();

    private readonly List<CameraFollow> _cameraFollows = new List<CameraFollow>();

    private const float SpawnWaitTimeout = 5f;
    private const float SpawnPollInterval = 0.25f;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start() => StartCoroutine(WaitAndSetup());

    private void OnDestroy() => CleanupCameras();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Tears down all cameras and rebuilds them from scratch.
    /// Call whenever the player roster changes.
    /// </summary>
    public void RefreshCameras()
    {
        StopAllCoroutines();
        StartCoroutine(WaitAndSetup());
    }

    /// <summary>Alias for <see cref="RefreshCameras"/> used after a game reset.</summary>
    public void ResetCameras() => RefreshCameras();

    /// <summary>
    /// Destroys all managed camera GameObjects and clears internal lists.
    /// </summary>
    public void CleanupCameras()
    {
        StopAllCoroutines();

        foreach (var cam in _runnerCameras) if (cam != null) Destroy(cam.gameObject);
        foreach (var cam in _catcherCameras) if (cam != null) Destroy(cam.gameObject);

        _runnerCameras.Clear();
        _catcherCameras.Clear();
        _cameraFollows.Clear();
    }

    /// <summary>
    /// Snaps every active <see cref="CameraFollow"/> to its current target.
    /// </summary>
    public void ResetAllCameras()
    {
        foreach (var cf in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
            if (cf.Target != null) cf.ForcePosition();
    }

    /// <summary>
    /// Reassigns each managed <see cref="CameraFollow"/> to the first local-owner
    /// player of the appropriate type, then snaps its position.
    /// Called after a round reset.
    /// </summary>
    public void ReassignCamerasAfterReset()
    {
        AssignFollowTargets(
            _runnerCameras,
            FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
                .Where(p => p.IsOwner)
                .OrderBy(p => p.playerNumber.Value)
                .Select(p => p.gameObject)
                .ToList());

        AssignFollowTargets(
            _catcherCameras,
            FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
                .Where(g => g.IsOwner)
                .OrderBy(g => g.playerNumber.Value)
                .Select(g => g.gameObject)
                .ToList());
    }

    /// <summary>Finds the camera tracking <paramref name="player"/> and snaps it.</summary>
    public void ResetCameraForPlayer(PlayerMovement player)
    {
        if (player == null || !player.IsOwner) return;
        FindCameraFollowing(player.transform)?.ForcePosition();
    }

    /// <summary>Finds the camera tracking <paramref name="guard"/> and snaps it.</summary>
    public void ResetCameraForPlayer(Guard guard)
    {
        if (guard == null || !guard.IsOwner) return;
        FindCameraFollowing(guard.transform)?.ForcePosition();
    }

    // ====================================================================
    // Private – Setup Coroutine
    // ====================================================================

    /// <summary>
    /// Polls until at least one locally-owned player has spawned
    /// (up to <see cref="SpawnWaitTimeout"/> seconds), then builds cameras.
    /// No longer depends on <see cref="GameSettings"/> counts — the actual
    /// number of local players is determined at runtime from spawned objects.
    /// </summary>
    private IEnumerator WaitAndSetup()
    {
        CleanupCameras();

        float elapsed = 0f;

        List<GameObject> runners = new List<GameObject>();
        List<GameObject> catchers = new List<GameObject>();

        while (runners.Count == 0 && catchers.Count == 0 && elapsed < SpawnWaitTimeout)
        {
            runners = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
                .Where(p => p.IsSpawned && p.IsOwner)
                .OrderBy(p => p.playerNumber.Value)
                .Select(p => p.gameObject)
                .ToList();

            catchers = FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
                .Where(g => g.IsSpawned && g.IsOwner)
                .OrderBy(g => g.playerNumber.Value)
                .Select(g => g.gameObject)
                .ToList();

            if (runners.Count == 0 && catchers.Count == 0)
            {
                elapsed += SpawnPollInterval;
                yield return new WaitForSeconds(SpawnPollInterval);
            }
            else
            {
                break;
            }
        }

        if (runners.Count == 0 && catchers.Count == 0)
        {
            Debug.LogWarning("[SplitScreenManager] No locally-owned players found after timeout.");
            yield break;
        }

        BuildCameras(runners, catchers);
    }

    // ====================================================================
    // Private – Camera Creation
    // ====================================================================

    /// <summary>
    /// Builds per-player cameras according to which roles are actually present.
    ///
    /// <b>Rules (in priority order):</b>
    /// <list type="number">
    ///   <item>Only Runners present → all cameras on Display 1.</item>
    ///   <item>Only Catchers present → all cameras on Display 1.</item>
    ///   <item>Both present → Runners on Display 1, Catchers on Display 2.
    ///         In the Editor Display 2 is simulated by a second Game View;
    ///         in a build a second monitor must be connected.</item>
    /// </list>
    ///
    /// The Inspector field <see cref="singleScreenFallback"/> is no longer used
    /// for the single-type case — whichever type is playing always gets Screen 1.
    /// </summary>
    private void BuildCameras(List<GameObject> runners, List<GameObject> catchers)
    {
        CleanupCameras();

        // Disable the scene main camera — replaced by per-player cameras.
        if (Camera.main != null) Camera.main.enabled = false;

        bool hasRunners = runners.Count > 0;
        bool hasCatchers = catchers.Count > 0;
        bool bothPresent = hasRunners && hasCatchers;

        if (!bothPresent)
        {
            // ── Single role: whichever is playing uses Display 1 ──────────
            List<GameObject> targets = hasRunners ? runners : catchers;
            bool isCatchers = !hasRunners;

            for (int i = 0; i < targets.Count; i++)
            {
                var cam = CreateCamera(
                    $"Camera_P{i + 1}", targets[i].transform,
                    GetViewportRect(i, targets.Count),
                    MultiScreenManager.RunnerDisplayIndex,   // always Display 1
                    keepListener: i == 0);

                if (isCatchers) _catcherCameras.Add(cam);
                else _runnerCameras.Add(cam);
            }

            Debug.Log($"[SplitScreenManager] Single-role session — " +
                      $"{(isCatchers ? "Catchers" : "Runners")} ({targets.Count} camera(s)) on Display 1.");

            SplitScreenBorder.Instance?.Rebuild(targets.Count);
        }
        else
        {
            // ── Both roles: Runners → Display 1, Catchers → Display 2 ────
#if UNITY_EDITOR
            // In the Editor, open a second Game View and set it to Display 2
            // via the dropdown in the Game View title bar.
            Debug.Log("[SplitScreenManager] Dual-role session in Editor. " +
                      "Open a second Game View and set it to Display 2 for the catcher view.");
#else
            if (Display.displays.Length <= MultiScreenManager.CatcherDisplayIndex)
            {
                Debug.LogWarning("[SplitScreenManager] Two roles detected but only one " +
                                 "physical display connected. Catchers will be invisible. " +
                                 "Connect a second monitor for split-display play.");
            }
#endif

            for (int i = 0; i < runners.Count; i++)
                _runnerCameras.Add(CreateCamera(
                    $"RunnerCamera_P{i + 1}", runners[i].transform,
                    GetViewportRect(i, runners.Count),
                    MultiScreenManager.RunnerDisplayIndex, keepListener: i == 0));

            for (int i = 0; i < catchers.Count; i++)
                _catcherCameras.Add(CreateCamera(
                    $"CatcherCamera_P{i + 1}", catchers[i].transform,
                    GetViewportRect(i, catchers.Count),
                    MultiScreenManager.CatcherDisplayIndex, keepListener: i == 0));

            Debug.Log($"[SplitScreenManager] Dual-role — " +
                      $"{runners.Count} runner camera(s) on Display 1, " +
                      $"{catchers.Count} catcher camera(s) on Display 2.");

            SplitScreenBorder.Instance?.Rebuild(runners.Count);
        }
    }

    /// <summary>
    /// Instantiates one camera from <see cref="cameraPrefab"/>, configures its
    /// viewport, display target, and <see cref="CameraFollow"/> target.
    /// </summary>
    private Camera CreateCamera(
        string name,
        Transform target,
        Rect viewportRect,
        int displayIndex,
        bool keepListener)
    {
        GameObject obj = Instantiate(cameraPrefab);
        obj.name = name;

        Camera cam = obj.GetComponent<Camera>();
        cam.rect = viewportRect;
        cam.targetDisplay = displayIndex;

        // Remove extra AudioListeners to prevent Unity's warning.
        if (!keepListener)
        {
            var al = obj.GetComponentInChildren<AudioListener>();
            if (al != null) Destroy(al);
        }

        CameraFollow follow = obj.GetComponent<CameraFollow>();
        if (follow != null)
        {
            follow.Target = target;
            follow.ForcePosition();
            _cameraFollows.Add(follow);
        }

        return cam;
    }

    // ====================================================================
    // Private – Helpers
    // ====================================================================

    /// <summary>
    /// Re-assigns the <see cref="CameraFollow"/> on each camera in
    /// <paramref name="cameras"/> to the corresponding target in
    /// <paramref name="targets"/>, then snaps each camera.
    /// </summary>
    private static void AssignFollowTargets(List<Camera> cameras, List<GameObject> targets)
    {
        int count = Mathf.Min(cameras.Count, targets.Count);
        for (int i = 0; i < count; i++)
        {
            var follow = cameras[i].GetComponent<CameraFollow>();
            if (follow == null) continue;
            follow.Target = targets[i].transform;
            follow.ForcePosition();
        }
    }

    /// <summary>
    /// Returns the normalised <see cref="Rect"/> for camera at <paramref name="index"/>
    /// among <paramref name="total"/> cameras sharing the same display.
    /// </summary>
    private static Rect GetViewportRect(int index, int total)
    {
        switch (total)
        {
            case 1:
                return new Rect(0f, 0f, 1f, 1f);

            case 2:
                // Left | Right
                return index == 0
                    ? new Rect(0f, 0f, 0.5f, 1f)
                    : new Rect(0.5f, 0f, 0.5f, 1f);

            case 3:
                // Full top | bottom-left | bottom-right
                if (index == 0) return new Rect(0f, 0.5f, 1f, 0.5f);
                if (index == 1) return new Rect(0f, 0f, 0.5f, 0.5f);
                return new Rect(0.5f, 0f, 0.5f, 0.5f);

            case 4:
                // 2 × 2 grid
                if (index == 0) return new Rect(0f, 0.5f, 0.5f, 0.5f);
                if (index == 1) return new Rect(0.5f, 0.5f, 0.5f, 0.5f);
                if (index == 2) return new Rect(0f, 0f, 0.5f, 0.5f);
                return new Rect(0.5f, 0f, 0.5f, 0.5f);

            default:
                return new Rect(0f, 0f, 1f, 1f);
        }
    }

    private static CameraFollow FindCameraFollowing(Transform target)
    {
        foreach (var cf in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
            if (cf.Target == target) return cf;
        return null;
    }
}