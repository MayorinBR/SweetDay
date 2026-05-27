using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Adds a visual selection effect to any UI <see cref="Button"/> when it is
/// navigated to by a controller, keyboard, or mouse hover.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonSelectionEffect : MonoBehaviour,
    ISelectHandler, IDeselectHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Header("Scale Effect")]
    [Tooltip("How much the button grows when selected. 1.0 = no change.")]
    [SerializeField] private float selectedScale = 1.12f;

    [Tooltip("Speed of the scale lerp (higher = snappier).")]
    [SerializeField] private float animationSpeed = 10f;

    [Header("Outline Image  (optional)")]
    [Tooltip("Optional child Image used as a glow / border when selected.")]
    [SerializeField] private Image outlineImage;

    [Tooltip("Color applied to the outline image when selected.")]
    [SerializeField] private Color outlineColor = new Color(1f, 0.85f, 0.1f, 1f); // yellow

    [Header("Background Tint  (optional)")]
    [Tooltip("If true, tints the Button's own Image while selected.")]
    [SerializeField] private bool tintBackground = false;

    [Tooltip("Tint color applied to the Button's Image when selected.")]
    [SerializeField] private Color selectedTint = new Color(1f, 1f, 0.75f, 1f);

    // ====================================================================
    // Private
    // ====================================================================

    private Vector3 _baseScale;
    private Vector3 _targetScale;
    private Image _backgroundImage;
    private Color _originalTint;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        _baseScale = transform.localScale;
        _targetScale = _baseScale;

        _backgroundImage = GetComponent<Image>();
        if (_backgroundImage != null)
            _originalTint = _backgroundImage.color;

        // Ensure outline starts hidden.
        if (outlineImage != null)
            outlineImage.gameObject.SetActive(false);
    }

    private void Update()
    {
        // Smooth scale towards target.
        if (transform.localScale != _targetScale)
            transform.localScale = Vector3.Lerp(
                transform.localScale, _targetScale, Time.unscaledDeltaTime * animationSpeed);
    }

    private void OnDisable() => Deselect();

    // ====================================================================
    // EventSystem Callbacks
    // ====================================================================

    /// <summary>Called by EventSystem when a controller / keyboard selects this button.</summary>
    public void OnSelect(BaseEventData eventData) => Select();

    /// <summary>Called by EventSystem when focus moves to a different element.</summary>
    public void OnDeselect(BaseEventData eventData) => Deselect();

    /// <summary>Mouse hover also drives selection so visual feedback is consistent.</summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    /// <summary>When the mouse leaves, clear the EventSystem selection.</summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);

        Deselect();
    }

    // ====================================================================
    // Private Helpers
    // ====================================================================

    private void Select()
    {
        _targetScale = _baseScale * selectedScale;

        if (outlineImage != null)
        {
            outlineImage.color = outlineColor;
            outlineImage.gameObject.SetActive(true);
        }

        if (tintBackground && _backgroundImage != null)
            _backgroundImage.color = selectedTint;
    }

    private void Deselect()
    {
        _targetScale = _baseScale;

        if (outlineImage != null)
            outlineImage.gameObject.SetActive(false);

        if (tintBackground && _backgroundImage != null)
            _backgroundImage.color = _originalTint;
    }
}