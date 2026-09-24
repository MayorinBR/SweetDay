using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws solid-colour divider lines between split-screen viewports on the same display.
/// Place this component on a <see cref="Canvas"/> (Screen Space – Overlay) in the Game Scene.
/// <see cref="SplitScreenManager"/> calls <see cref="Rebuild"/> automatically after cameras
/// are created; no manual wiring is required.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class SplitScreenBorder : MonoBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="SplitScreenBorder"/>.</summary>
    public static SplitScreenBorder Instance { get; private set; }

    // ====================================================================
    // Inspector
    // ====================================================================

    /// <summary>Colour of every divider line.</summary>
    [SerializeField] private Color borderColor = Color.black;

    /// <summary>Pixel thickness of each divider line.</summary>
    [SerializeField] private float borderThickness = 6f;

    // ====================================================================
    // Private
    // ====================================================================

    private readonly List<GameObject> _lines = new List<GameObject>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
    }

    // ====================================================================
    // Public API
    // ====================================================================

    /// <summary>
    /// Clears existing lines and redraws them for <paramref name="cameraCount"/> cameras
    /// sharing the same display.
    /// </summary>
    /// <param name="cameraCount">
    /// Number of cameras on the display (1 = no borders, 2–4 = appropriate grid lines).
    /// </param>
    public void Rebuild(int cameraCount)
    {
        ClearLines();

        switch (cameraCount)
        {
            case 2:
                CreateVertical(0.5f, 0f, 1f);
                break;
            case 3:
                CreateHorizontal(0.5f);
                CreateVertical(0.5f, 0f, 0.5f);
                break;
            case 4:
                CreateHorizontal(0.5f);
                CreateVertical(0.5f, 0f, 1f);
                break;
        }
    }

    /// <summary>Removes all divider lines without rebuilding them.</summary>
    public void Clear() => ClearLines();

    // ====================================================================
    // Private – Line Construction
    // ====================================================================

    /// <summary>
    /// Creates a vertical line at normalised X position <paramref name="xNorm"/>
    /// spanning from <paramref name="yMinNorm"/> to <paramref name="yMaxNorm"/>.
    /// </summary>
    private void CreateVertical(float xNorm, float yMinNorm, float yMaxNorm)
    {
        var rt = CreateLineRect("Border_V");
        rt.anchorMin = new Vector2(xNorm, yMinNorm);
        rt.anchorMax = new Vector2(xNorm, yMaxNorm);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(borderThickness, 0f);
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Creates a full-width horizontal line at normalised Y position <paramref name="yNorm"/>.
    /// </summary>
    private void CreateHorizontal(float yNorm)
    {
        var rt = CreateLineRect("Border_H");
        rt.anchorMin = new Vector2(0f, yNorm);
        rt.anchorMax = new Vector2(1f, yNorm);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, borderThickness);
        rt.anchoredPosition = Vector2.zero;
    }

    private RectTransform CreateLineRect(string objName)
    {
        var go = new GameObject(objName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        go.GetComponent<Image>().color = borderColor;
        _lines.Add(go);
        return go.GetComponent<RectTransform>();
    }

    private void ClearLines()
    {
        foreach (var line in _lines)
            if (line != null) Destroy(line);
        _lines.Clear();
    }
}