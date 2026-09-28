using System.Collections.Generic;
using UnityEngine;

public class HandDamage : MonoBehaviour
{
    public int damage = 10;
    public float sameEnemyCooldown = 0.5f;

    private Dictionary<EnemyHealth, float> lastHitTime =
        new Dictionary<EnemyHealth, float>();

    void OnTriggerEnter(Collider other)
    {
        Debug.Log(
            "HAND HIT: " +
            gameObject.name +
            " touched " +
            other.name
        );

        EnemyHealth enemy =
            other.GetComponentInParent<EnemyHealth>();

        if (enemy == null)
        {
            Debug.LogWarning(
                "搵唔到 EnemyHealth，Collider 係：" +
                other.name
            );

            return;
        }

        if (lastHitTime.TryGetValue(enemy, out float lastTime))
        {
            if (Time.time - lastTime < sameEnemyCooldown)
            {
                return;
            }
        }

        enemy.TakeDamage(damage);
        lastHitTime[enemy] = Time.time;

        Debug.Log(
            "造成傷害：" +
            enemy.name +
            " Damage = " +
            damage
        );
    }

    void OnTriggerExit(Collider other)
    {
        EnemyHealth enemy =
            other.GetComponentInParent<EnemyHealth>();

        if (enemy != null)
        {
            lastHitTime.Remove(enemy);
        }
    }
}