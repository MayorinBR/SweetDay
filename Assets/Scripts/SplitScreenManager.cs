using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Creates and manages per-player camera instances for local split-screen play.
/// </summary>
public class SplitScreenManager : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [SerializeField, Tooltip("Prefab that must contain a Camera and a CameraFollow component.")]
    private GameObject cameraPrefab;

    // ====================================================================
    // Private
    // ====================================================================

    private readonly List<Camera> _playerCameras = new List<Camera>();
    private readonly List<CameraFollow> _cameraFollows = new List<CameraFollow>();

    /// <summary>Maximum seconds to wait for local players to spawn before giving up.</summary>
    private const float SpawnWaitTimeout = 5f;

    /// <summary>Polling interval while waiting for players to appear.</summary>
    private const float SpawnPollInterval = 0.5f;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Start() => StartCoroutine(WaitAndSetup());

    private void OnDestroy() => CleanupCameras();

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Tears down existing cameras and rebuilds them from scratch.
    /// Call this after the player roster changes (e.g. extra local players spawned).
    /// </summary>
    public void RefreshCameras()
    {
        StopAllCoroutines();
        StartCoroutine(WaitAndSetup());
    }

    /// <summary>Alias for <see cref="RefreshCameras"/> used after a game reset.</summary>
    public void ResetCameras() => RefreshCameras();

    /// <summary>
    /// Destroys all camera GameObjects managed by this component and clears the
    /// internal lists.  Also sweeps the scene for any stray "PlayerCamera_*" objects.
    /// </summary>
    public void CleanupCameras()
    {
        StopAllCoroutines();

        foreach (var cam in _playerCameras)
        {
            if (cam != null) Destroy(cam.gameObject);
        }

        _playerCameras.Clear();
        _cameraFollows.Clear();

        // Safety sweep: destroy any leftover player-camera GameObjects.
        foreach (var go in GameObject.FindGameObjectsWithTag("MainCamera"))
        {
            if (go.name.Contains("PlayerCamera"))
                Destroy(go);
        }
    }

    /// <summary>
    /// Forces every <see cref="CameraFollow"/> in the scene to snap to its current target.
    /// Used after a teleport or round reset to prevent the camera from sliding in
    /// from a stale position.
    /// </summary>
    public void ResetAllCameras()
    {
        foreach (var cf in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
        {
            if (cf.Target != null) cf.ForcePosition();
        }
    }

    /// <summary>
    /// Reassigns each existing <see cref="CameraFollow"/> to the first local-owner
    /// player or guard it can find, then snaps the camera position.
    /// Called by <see cref="GameManager"/> after a round reset via ClientRpc.
    /// </summary>
    public void ReassignCamerasAfterReset()
    {
        foreach (var cf in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
        {
            // Try runners first.
            var runner = FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
                .FirstOrDefault(p => p.IsOwner);

            if (runner != null)
            {
                cf.Target = runner.transform;
                cf.ForcePosition();
                continue;
            }

            // Fall back to guards.
            var guard = FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
                .FirstOrDefault(g => g.IsOwner);

            if (guard != null)
            {
                cf.Target = guard.transform;
                cf.ForcePosition();
            }
        }
    }

    /// <summary>
    /// Finds the <see cref="CameraFollow"/> currently tracking <paramref name="player"/>
    /// and forces it to snap to the player's position.
    /// </summary>
    public void ResetCameraForPlayer(PlayerMovement player)
    {
        if (player == null || !player.IsOwner) return;
        FindCameraFollowing(player.transform)?.ForcePosition();
    }

    /// <summary>
    /// Finds the <see cref="CameraFollow"/> currently tracking <paramref name="guard"/>
    /// and forces it to snap to the guard's position.
    /// </summary>
    public void ResetCameraForPlayer(Guard guard)
    {
        if (guard == null || !guard.IsOwner) return;
        FindCameraFollowing(guard.transform)?.ForcePosition();
    }

    // ====================================================================
    // Private ÅESetup Coroutine
    // ====================================================================

    /// <summary>
    /// Polls until all expected local player objects are present in the scene,
    /// then calls <see cref="SetupSplitScreen"/>.
    /// </summary>
    private IEnumerator WaitAndSetup()
    {
        CleanupCameras();

        int expected = GameSettings.LocalPlayerCount;
        float elapsed = 0f;

        var localObjects = new List<GameObject>();

        while (localObjects.Count < expected && elapsed < SpawnWaitTimeout)
        {
            localObjects.Clear();

            localObjects.AddRange(
                FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude)
                    .Where(p => p.IsSpawned && p.IsOwner)
                    .Select(p => p.gameObject));

            localObjects.AddRange(
                FindObjectsByType<Guard>(FindObjectsInactive.Exclude)
                    .Where(g => g.IsSpawned && g.IsOwner)
                    .Select(g => g.gameObject));

            if (localObjects.Count < expected)
            {
                elapsed += SpawnPollInterval;
                yield return new WaitForSeconds(SpawnPollInterval);
            }
        }

        if (localObjects.Count == 0) yield break;

        // Sort by player number so cameras are assigned in the correct order.
        localObjects = localObjects
            .Where(o => o != null)
            .OrderBy(o =>
            {
                var pm = o.GetComponent<PlayerMovement>();
                if (pm != null) return pm.playerNumber.Value;
                var g = o.GetComponent<Guard>();
                return g != null ? g.playerNumber.Value : 99;
            })
            .ToList();

        SetupSplitScreen(localObjects);
    }

    // ====================================================================
    // Private ÅECamera Setup
    // ====================================================================

    /// <summary>
    /// Instantiates one camera prefab per target in <paramref name="targets"/>,
    /// assigns viewports, and wires up <see cref="CameraFollow"/> targets.
    /// </summary>
    private void SetupSplitScreen(List<GameObject> targets)
    {
        CleanupCameras();

        // Disable the scene's main camera when running split-screen.
        if (Camera.main != null && targets.Count > 1)
            Camera.main.enabled = false;

        for (int i = 0; i < targets.Count; i++)
        {
            GameObject camObj = Instantiate(cameraPrefab);
            Camera cam = camObj.GetComponent<Camera>();
            CameraFollow follow = camObj.GetComponent<CameraFollow>();

            // Only P1 keeps an AudioListener.
            if (i > 0)
            {
                var listener = camObj.GetComponentInChildren<AudioListener>();
                if (listener != null) Destroy(listener);
            }

            cam.rect = GetViewportRect(i, targets.Count);

            if (follow != null)
            {
                follow.Target = targets[i].transform;
                follow.ForcePosition();
                _cameraFollows.Add(follow);
            }

            camObj.name = $"PlayerCamera_P{i + 1}";
            _playerCameras.Add(cam);
        }
    }

    /// <summary>
    /// Returns the normalised <see cref="Rect"/> for camera <paramref name="index"/>
    /// given <paramref name="total"/> active players.
    /// </summary>
    private static Rect GetViewportRect(int index, int total)
    {
        switch (total)
        {
            case 1:
                return new Rect(0f, 0f, 1f, 1f);

            case 2:
                // P1 left | P2 right
                return index == 0
                    ? new Rect(0f, 0f, 0.5f, 1f)
                    : new Rect(0.5f, 0f, 0.5f, 1f);

            case 3:
                // P1 full top | P2 bottom-left | P3 bottom-right
                if (index == 0) return new Rect(0f, 0.5f, 1f, 0.5f);
                if (index == 1) return new Rect(0f, 0f, 0.5f, 0.5f);
                return new Rect(0.5f, 0f, 0.5f, 0.5f);

            case 4:
                // 2◊2 grid
                if (index == 0) return new Rect(0f, 0.5f, 0.5f, 0.5f);
                if (index == 1) return new Rect(0.5f, 0.5f, 0.5f, 0.5f);
                if (index == 2) return new Rect(0f, 0f, 0.5f, 0.5f);
                return new Rect(0.5f, 0f, 0.5f, 0.5f);

            default:
                return new Rect(0f, 0f, 1f, 1f);
        }
    }

    // ====================================================================
    // Private ÅEUtility
    // ====================================================================

    /// <summary>
    /// Returns the first <see cref="CameraFollow"/> whose <c>Target</c> matches
    /// <paramref name="target"/>, or <c>null</c> if none is found.
    /// </summary>
    private static CameraFollow FindCameraFollowing(Transform target)
    {
        foreach (var cf in FindObjectsByType<CameraFollow>(FindObjectsInactive.Exclude))
        {
            if (cf.Target == target) return cf;
        }
        return null;
    }
}
