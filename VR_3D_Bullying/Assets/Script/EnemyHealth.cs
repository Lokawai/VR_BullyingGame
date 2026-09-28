using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 30;

    private int currentHealth;
    private bool isDead;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead)
        {
            return;
        }

        currentHealth -= damage;

        Debug.Log(
            gameObject.name +
            " HP: " +
            currentHealth
        );

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        if (KillCountUI.Instance != null)
        {
            KillCountUI.Instance.AddKill();
        }
        else
        {
            Debug.LogError(
                "KillCountUI not found. " +
                "Check that KillCountCanvas is active and has KillCountUI."
            );
        }

        Destroy(gameObject);
    }
}