using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;

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

    [Header("Life Icon Layout")]
    /// <summary>
    /// The Sprite to use for the life icon.
    /// </summary>
    public Sprite lifeIconSprite;

    /// <summary>
    /// The desired width of each life icon in UI pixels.
    /// </summary>
    public float lifeIconWidth = 50f;

    /// <summary>
    /// The desired height of each life icon in UI pixels.
    /// </summary>
    public float lifeIconHeight = 50f;

    /// <summary>
    /// Spacing between each life icon.
    /// </summary>
    public float lifeIconSpacing = 10f;

    /// <summary>
    /// Life icons container.
    /// </summary>
    public Transform livesContainer;

    /// <summary>
    /// List of lifeIcons objects in the scene.
    /// </summary>
    private List<GameObject> lifeIcons = new List<GameObject>();

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
    /// Updates the life display in the UI.
    /// </summary>
    /// <param name="lives">The current score to display.</param>
    public void UpdateLivesUI(int lives)
    {
        // Limpa os ícones existentes
        foreach (GameObject icon in lifeIcons)
        {
            Destroy(icon);
        }
        lifeIcons.Clear();

        // Instancia novos ícones baseados no número de vidas
        for (int i = 0; i < lives; i++)
        {
            if (lifeIconSprite != null && livesContainer != null) 
            {
                GameObject newIconGO = new GameObject("LifeIcon_" + i); // Cria um novo GameObject
                Image newIconImage = newIconGO.AddComponent<Image>(); // Adiciona um componente Image a ele
                newIconImage.sprite = lifeIconSprite; // Atribui o Sprite

                // Define o pai para o container de vidas e mantém a escala local
                newIconGO.transform.SetParent(livesContainer, false);

                RectTransform iconRectTransform = newIconGO.GetComponent<RectTransform>();
                if (iconRectTransform != null)
                {
                    // Define o tamanho do ícone
                    iconRectTransform.sizeDelta = new Vector2(lifeIconWidth, lifeIconHeight);

                    // Calcula a posição X para cada ícone
                    float xPos = i * (lifeIconWidth + lifeIconSpacing);
                    // Centraliza o pivô do ícone e o posiciona no eixo Y no meio do container
                    iconRectTransform.pivot = new Vector2(0.5f, 0.5f); // Pivô no centro
                    iconRectTransform.anchorMin = new Vector2(0f, 0.5f); // Âncora à esquerda, meio
                    iconRectTransform.anchorMax = new Vector2(0f, 0.5f); // Âncora à esquerda, meio
                    iconRectTransform.anchoredPosition = new Vector2(xPos + lifeIconWidth / 2, 0); // Ajusta a posição X para começar da esquerda
                }

                lifeIcons.Add(newIconGO);
            }
            else
            {
                Debug.LogError("Life Icon Sprite ou Lives Container não atribuídos no UIManager!");
            }
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