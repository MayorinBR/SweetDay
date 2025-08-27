using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;
using Unity.Netcode;

public class UIManager : MonoBehaviour
{
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI timerText; // これがタイマーUIです
    public GameObject endGamePanel;
    public Sprite lifeIconSprite;
    public float lifeIconWidth = 50f;
    public Transform livesContainer;

    private List<GameObject> lifeIcons = new List<GameObject>();

    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public static UIManager Instance { get; private set; }

    public void UpdateScoreUI(int score)
    {
        if (scoreText != null)
        {
            scoreText.text = "Score: " + score;
        }
    }

    // GameManagerから呼び出されるタイマー更新メソッド
    public void UpdateTimerUI(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = Mathf.Max(0, Mathf.RoundToInt(timeRemaining)).ToString();
        }
    }

    public void UpdateLivesUI(int lives)
    {
        foreach (var icon in lifeIcons)
        {
            Destroy(icon);
        }
        lifeIcons.Clear();

        for (int i = 0; i < lives; i++)
        {
            GameObject newIconGO = new GameObject("LifeIcon" + i);
            newIconGO.transform.SetParent(livesContainer, false);
            Image newIconImage = newIconGO.AddComponent<Image>();
            newIconImage.sprite = lifeIconSprite;

            RectTransform iconRectTransform = newIconGO.GetComponent<RectTransform>();
            if (iconRectTransform != null)
            {
                iconRectTransform.sizeDelta = new Vector2(lifeIconWidth, lifeIconWidth);
                float xPos = i * (lifeIconWidth + 10);
                iconRectTransform.pivot = new Vector2(0.5f, 0.5f);
                iconRectTransform.anchorMin = new Vector2(0f, 0.5f);
                iconRectTransform.anchorMax = new Vector2(0f, 0.5f);
                iconRectTransform.anchoredPosition = new Vector2(xPos + lifeIconWidth / 2, 0);
            }
            lifeIcons.Add(newIconGO);
        }
    }

    public void ShowEndGamePanel(bool won)
    {
        if (endGamePanel != null)
        {
            endGamePanel.SetActive(true);
            TextMeshProUGUI resultText = endGamePanel.GetComponentInChildren<TextMeshProUGUI>();
            if (resultText != null)
            {
                resultText.text = won ? "You Won!" : "Game Over!";
            }
        }
    }
}