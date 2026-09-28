using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;

public class enemyPatrol : MonoBehaviour
{
    GameObject player;

    NavMeshAgent agent;

    [SerializeField] LayerMask playerLayer, groundLayer;

    [Header("Health")]
    public int maxHealth = 100;
    private int enemyHealth;


    Vector3 walkPoint;
    bool walkPointSet;

    [SerializeField]
    float walkPointRange;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        player = GameObject.Find("Player");

         enemyHealth = maxHealth;
    }

    // Update is called once per frame
    void Update()
    {
        Patrol();
    }

    void Patrol()
    {
        if(!walkPointSet) SearchWalkPoint();
        if(walkPointSet) agent.SetDestination(walkPoint);
        if(Vector3.Distance(transform.position, walkPoint) < 10) walkPointSet = false;
    }

    void SearchWalkPoint()
    {
        float randomZ = Random.Range(-walkPointRange, walkPointRange);
        float randomX = Random.Range(-walkPointRange, walkPointRange);

        walkPoint = new Vector3(transform.position.x + randomX, transform.position.y, transform.position.z + randomZ);

        if(Physics.Raycast(walkPoint, Vector3.down, groundLayer))
        {
            walkPointSet = true;
        }
    }

    public void TakeDamage(int damage)
    {
        enemyHealth -= damage;
        if (enemyHealth < 0) enemyHealth = 0;

        Debug.Log("Enemy hit! HP: " + enemyHealth);

        if (enemyHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log("Enemy died");
        // 這裡可以：
        // - 播放死亡動畫
        // - 禁用 collider / renderer
        // - 延遲後重置位置或銷毀
        // 例如：
        // gameObject.SetActive(false);
        // 或 Destroy(gameObject);
    }
}
