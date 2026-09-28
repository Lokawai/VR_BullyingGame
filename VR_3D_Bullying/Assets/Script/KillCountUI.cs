using UnityEngine;
using TMPro;

public class KillCountUI : MonoBehaviour
{
    public static KillCountUI Instance { get; private set; }

    [Header("Score")]
    public TextMeshProUGUI scoreText;
    public int pointsPerKill = 5;
    public int targetScore = 500;

    [Header("Win UI")]
    public GameObject winCanvas;

    private int score;
    private bool gameEnded;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        score = 0;
        gameEnded = false;

        // 確保由 Play Mode 開始時遊戲正常運行
        Time.timeScale = 1f;

        if (winCanvas != null)
        {
            winCanvas.SetActive(false);
        }

        UpdateText();
    }

    public void AddKill()
    {
        // 已勝利後，不能再加分
        if (gameEnded)
        {
            return;
        }

        score += pointsPerKill;
        UpdateText();

        if (score >= targetScore)
        {
            WinGame();
        }
    }

    void UpdateText()
    {
        if (scoreText != null)
        {
            scoreText.text = score.ToString();
        }
    }

    void WinGame()
    {
        gameEnded = true;

        if (winCanvas != null)
        {
            winCanvas.SetActive(true);
        }
        else
        {
            Debug.LogError(
                "Win Canvas is not assigned in KillCountUI."
            );
        }

        Time.timeScale = 0f;

        Debug.Log("You reached 500 points - game stopped.");
    }

    public int GetScore()
    {
        return score;
    }
}