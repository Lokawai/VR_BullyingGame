using UnityEngine;

public class GameStartManager : MonoBehaviour
{
   [Header("UI 畫面設定")]
    public GameObject startPanel; // 請將含有文字與 Start 按鈕的橘色介面面板拖入此處

    void Start()
    {
        // 遊戲一開始時，將時間流速設為 0（暫停遊戲）
        Time.timeScale = 0f;

        // 確保開始畫面面板為顯示狀態
        if (startPanel != null)
        {
            startPanel.SetActive(true);
        }
    }

    // 當按下 Start 按鈕時呼叫此方法
    public void StartGame()
    {
        // 恢復正常遊戲時間流速
        Time.timeScale = 1f;

        // 隱藏開始畫面面板
        if (startPanel != null)
        {
            startPanel.SetActive(false);
        }
    }
}
