using UnityEngine;

/// <summary>
/// Controls the camera's movement to follow a target smoothly.
/// Implements the Singleton pattern for easy access and allows dynamic target assignment.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    /// <summary>
    /// Gets the static instance of the CameraFollow, enabling easy access from other scripts (Singleton pattern).
    /// </summary>
    public static CameraFollow Instance { get; private set; }

    private Transform _target; // Private variable for the target

    /// <summary>
    /// Gets or sets the target Transform that the camera should follow.
    /// This property allows other scripts to dynamically set the camera's target.
    /// </summary>
    public Transform Target // Public property to define the target
    {
        get { return _target; }
        set { _target = value; } // Allows other scripts to set the target
    }

    /// <summary>
    /// The offset position of the camera relative to the target (X, Y, Z).
    /// This defines the camera's distance and height from the target.
    /// For a top-down view, Y should be positive (height) and Z can be 0 or slightly negative for perspective.
    /// </summary>
    public Vector3 offset = new Vector3(0f, 15f, 0f); // Camera position relative to the player (X, Y, Z). Z is set to 0 for a direct top-down view.

    /// <summary>
    /// The smoothing speed for the camera's movement.
    /// A lower value results in smoother, slower movement.
    /// </summary>
    public float smoothSpeed = 0.125f; // Speed at which the camera moves smoothly

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Implements the Singleton pattern to ensure only one CameraFollow instance exists.
    /// </summary>
    void Awake()
    {
        // Implements the Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            // Optional: DontDestroyOnLoad(gameObject); if the camera persists between scenes
        }
    }

    /// <summary>
    /// LateUpdate is called once per frame, after all Update functions have been called.
    /// Used to ensure the target has moved before the camera attempts to follow it,
    /// and to perform the smooth camera tracking.
    /// </summary>
    void LateUpdate()
    {
        if (_target == null) // Uses the _target variable
        {
            Debug.LogWarning("Camera target not assigned. Ensure GameManager sets it after player spawn.");
            return;
        }

        // Calculate the desired position of the camera based on the target's position and the offset.
        // The camera will follow the target's X and Z position, maintaining its own height (offset.y).
        Vector3 desiredPosition = _target.position + offset; // Uses _target

        // Smoothly interpolates the camera's current position towards the desired position.
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;
    }
}