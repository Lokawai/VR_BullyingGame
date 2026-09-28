using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class Player : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;
    public int currentHealth;

    [Header("VR 受傷紅色遮罩")]
    public Image damageOverlay;

    [Header("Game Over UI")]
    public GameObject gameOverPanel;

    [Header("Settings")]
    public float hitCooldown = 0.3f; // 兩次受傷之間的最小間隔
    private float lastHitTime = -999f;

        private bool gameEnded = false;

    void Start()
    {
        currentHealth = maxHealth;

        if (damageOverlay != null)
        {
            Color c = damageOverlay.color;
            c.a = 0f;
            damageOverlay.color = c;
        }
        if (damageOverlay != null)
        {
            Color c = damageOverlay.color;
            c.a = 0f;
            damageOverlay.color = c;
        }
    }

    // 被呼叫時扣血
    public void TakeDamage(int damage)
    {
        // 冷卻檢查：避免短時間內連續扣血
        if (Time.time - lastHitTime < hitCooldown)
        {
            return;
        }
        lastHitTime = Time.time;

        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

          if (damageOverlay != null && currentHealth > 0)
        {
            StartCoroutine(FlashRed());
        }

        if (currentHealth <= 0)
        {
            Die();
        }

         if (gameEnded)
        {
            return;
        }
    }

    IEnumerator FlashRed()
    {
        // 瞬間變紅
        Color c = damageOverlay.color;
        c.a = 0.4f; // VR 建議 0.3~0.5
        damageOverlay.color = c;

        // 短暫保持
        yield return new WaitForSeconds(0.15f);

        // 慢慢淡出
        float t = 0f;
        float duration = 0.4f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(0.4f, 0f, t / duration);
            c = damageOverlay.color;
            c.a = alpha;
            damageOverlay.color = c;
            yield return null;
        }
    }

    void Die()
    {
       // 防止重複呼叫死亡
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;

        Debug.Log("Player died");

        // 停止進行中的紅色受傷效果
        StopAllCoroutines();

        // 關閉紅色遮罩，讓 Game Over UI 清楚顯示
        if (damageOverlay != null)
        {
            Color c = damageOverlay.color;
            c.a = 0f;
            damageOverlay.color = c;
        }

        // 顯示你圖片中的反暴力／死亡提示畫面
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        else
        {
            Debug.LogWarning("尚未設定 Game Over Panel！");
        }

     

        // 暫停遊戲
        Time.timeScale = 0f;
        Debug.Log("Player died");
    }

    // 3D Trigger：碰到敵人時扣血
    void OnTriggerEnter(Collider other)
    {
        // 建議敵人的 Tag 設為 "Enemy"
        if (other.gameObject.CompareTag("Enemy Tom"))
        {
            TakeDamage(10); // 每次被碰到扣 10 血
        }
    }
}