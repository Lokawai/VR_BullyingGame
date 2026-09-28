using UnityEngine;
using TMPro;

public class KillCounter : MonoBehaviour
{
    [Header("Kill Count")]
    public int killCount = 0;

    [Header("UI - Optional")]
    public TMP_Text killText;

    void Start()
    {
        UpdateKillText();
    }

    public void AddKill()
    {
        killCount++;
        UpdateKillText();

        Debug.Log("Enemy Killed! Total Kills: " + killCount);
    }

    void UpdateKillText()
    {
        if (killText != null)
        {
            killText.text = "Killed: " + killCount;
        }
    }
}