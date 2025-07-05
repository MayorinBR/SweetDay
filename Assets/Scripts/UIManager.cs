using UnityEngine;
using TMPro; // Import the TextMeshPro namespace

/// <summary>
/// Manages the User Interface (UI) elements of the game,
/// such as displaying the score, timer, and end-game messages.
/// Implements the Singleton pattern for easy access.
/// </summary>
public class UIManager : MonoBehaviour
{
    /// <summary>
    /// Gets the singleton instance of the UIManager.
    /// </summary>
    public static UIManager Instance { get; private set; }

    /// <summary>
    /// Reference to the TextMeshProUGUI element that displays the player's score.
    /// </summary>
    public TextMeshProUGUI scoreText;

    /// <summary>
    /// Reference to the TextMeshProUGUI element that displays the remaining game time.
    /// </summary>
    public TextMeshProUGUI timerText;

    /// <summary>
    /// Reference to the GameObject that serves as the end-game panel (win/lose message).
    /// </summary>
    public GameObject endGamePanel;

    /// <summary>
    /// Reference to the TextMeshProUGUI element within the end-game panel that displays the message.
    /// </summary>
    public TextMeshProUGUI endGameMessageText;

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Implements the Singleton pattern to ensure only one UIManager exists.
    /// </summary>
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    /// <summary>
    /// Start is called before the first frame update.
    /// Initializes the UI elements to their default states.
    /// </summary>
    void Start()
    {
        // Ensures the end game panel is deactivated at the start
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(false);
        }
        UpdateScoreUI(0); // Initialize score to 0
    }

    /// <summary>
    /// Updates the score display in the UI.
    /// </summary>
    /// <param name="score">The current score to display.</param>
    public void UpdateScoreUI(int score)
    {
        if (scoreText != null)
        {
            scoreText.text = "Coins: " + score;
        }
    }

    /// <summary>
    /// Updates the timer display in the UI.
    /// </summary>
    /// <param name="timeRemaining">The remaining time in seconds to display.</param>
    public void UpdateTimerUI(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = Mathf.Max(0, Mathf.RoundToInt(timeRemaining)).ToString();
        }
    }

    /// <summary>
    /// Displays the end-game panel with a win or lose message.
    /// </summary>
    /// <param name="won">True if the player won, false if the player lost.</param>
    public void ShowEndGamePanel(bool won)
    {
        if (endGamePanel != null && endGameMessageText != null)
        {
            endGamePanel.SetActive(true);
            endGameMessageText.text = won ? "YOU WON!" : "YOU LOST!";
        }
    }
}