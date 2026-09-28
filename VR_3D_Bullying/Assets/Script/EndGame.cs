using UnityEngine;
using UnityEngine.SceneManagement;
public class EndGame : MonoBehaviour
{
      public GameObject gameOverPanel;

    private bool gameEnded = false;

    void Start()
    {
        gameOverPanel.SetActive(false);
        Time.timeScale = 1f;
    }

    public void PlayerDied()
    {
        if (gameEnded)
            return;

        gameEnded = true;

        gameOverPanel.SetActive(true);

        // 停止遊戲時間
        Time.timeScale = 0f;

        // 停止玩家控制
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
