using UnityEngine;

public class AttackPlayer : MonoBehaviour
{
    public int damage = 10;

    // 使用 Trigger 與玩家的 OnTriggerEnter 搭配
    void OnTriggerEnter(Collider other)
    {
        // 玩家物件的 Tag 請設為 "VR Player"
        if (other.gameObject.CompareTag("VR Player"))
        {
            // 先嘗試直接 GetComponent
            Player player = other.gameObject.GetComponent<Player>();
            
            // 如果沒有，再往上找 Parent（例如 Player 腳本掛在 XR Origin 上）
            if (player == null)
            {
                player = other.gameObject.GetComponentInParent<Player>();
            }

            if (player != null)
            {
                player.TakeDamage(damage);
            }
        }
    }
}