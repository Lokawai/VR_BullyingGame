using UnityEngine;
using UnityEngine.AI;

public class FindThePlayer : MonoBehaviour
{

  [Header("Targeting")]
    public Transform player;

    [Header("Distances")]
    public float chaseRange = 10f;
    public float attackRange = 2f;

    [Header("Combat")]
    public float attackCooldown = 1.5f;
    private float lastAttackTime;

    private NavMeshAgent agent;

    void Start()
    {
        // Get the NavMeshAgent component attached to this enemy
        agent = GetComponent<NavMeshAgent>();

        // Automatically find the player if not assigned in the inspector
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
            else
            {
                Debug.LogWarning("Player not found! Make sure your player has the 'Player' tag.");
            }
        }
    }

    void Update()
    {
        // Do nothing if there is no player to target
        if (player == null) return;

        // Calculate the distance between the enemy and the player
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer <= attackRange)
        {
            AttackPlayer();
        }
        else if (distanceToPlayer <= chaseRange)
        {
            ChasePlayer();
        }
        else
        {
            StopChasing();
        }
    }

    void ChasePlayer()
    {
        // Tell the NavMeshAgent to move to the player's current position
        agent.isStopped = false;
        agent.SetDestination(player.position);
    }

    void AttackPlayer()
    {
        // Stop moving while attacking
        agent.isStopped = true;
        
        // Face the player
        Vector3 direction = (player.position - transform.position).normalized;
        Quaternion lookRotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);

        // Check if enough time has passed to attack again
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            Debug.Log("Enemy attacks the player!");
            
            // TODO: Add your actual damage logic here (e.g., player.GetComponent<Health>().TakeDamage(10);)
            // TODO: Trigger attack animation here (e.g., animator.SetTrigger("Attack");)

            // Reset the cooldown timer
            lastAttackTime = Time.time;
        }
    }

    void StopChasing()
    {
        // Stop the agent from moving
        agent.isStopped = true;
    }

    // This draws visual circles in the Unity Editor so you can easily see the ranges
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }


}
