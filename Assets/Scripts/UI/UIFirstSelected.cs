using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Automatically selects a button when this panel becomes active, so a
/// controller player can navigate immediately without needing to click first.
/// </summary>
public class UIFirstSelected : MonoBehaviour
{
    // ====================================================================
    // Inspector
    // ====================================================================

    [Tooltip("Button to select when this panel activates. " +
             "Leave empty to auto-pick the first child Button.")]
    [SerializeField] private Button firstSelected;

    [Tooltip("Frames to wait before selecting (0 is usually fine; " +
             "increase if the EventSystem isn't ready in time).")]
    [SerializeField] private int delayFrames = 1;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void OnEnable()
    {
        StartCoroutine(SelectAfterDelay());
    }

    private void OnDisable()
    {
        StopAllCoroutines();

        // Clear selection so ghost highlights don't persist on disabled panels.
        if (EventSystem.current != null
            && EventSystem.current.currentSelectedGameObject != null)
        {
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ====================================================================
    // Private
    // ====================================================================

    private IEnumerator SelectAfterDelay()
    {
        for (int i = 0; i < delayFrames; i++)
            yield return null;

        Button target = firstSelected != null
            ? firstSelected
            : FindFirstActiveButton();

        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target.gameObject);
    }

    private Button FindFirstActiveButton()
    {
        foreach (var btn in GetComponentsInChildren<Button>())
        {
            if (btn.gameObject.activeInHierarchy && btn.interactable)
                return btn;
        }
        return null;
    }
}