using UnityEngine;
using UnityEngine.AI;

public class FindThePlayer : MonoBehaviour
{
    [Header("Targeting")]
    public Transform player;

    [Header("Distances")]
    public float chaseRange = 10f;
    public float attackRange = 2.5f;
    public float stopBuffer = 0.2f;

    [Header("Combat")]
    public int damage = 10;
    public float attackCooldown = 2f;

    private float lastAttackTime;
    private NavMeshAgent agent;
    private Animator animator;
    private Player playerScript;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("VR Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (player != null)
        {
            playerScript = player.GetComponent<Player>();

            if (playerScript == null)
            {
                playerScript = player.GetComponentInParent<Player>();
            }
        }
    }

    void Update()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(
            transform.position,
            player.position
        );

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
        Vector3 directionToPlayer =
            (player.position - transform.position).normalized;

        Vector3 targetPosition =
            player.position - directionToPlayer * (attackRange - stopBuffer);

        agent.isStopped = false;
        agent.SetDestination(targetPosition);

        if (animator != null)
        {
            animator.SetFloat("Blend", 1f);
        }
    }

    void AttackPlayer()
    {
        agent.isStopped = true;
        agent.ResetPath();

        Vector3 direction =
            player.position - transform.position;

        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime * 5f
            );
        }

        if (animator != null)
        {
            animator.SetFloat("Blend", 0f);
        }

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            if (playerScript != null)
            {
                playerScript.TakeDamage(damage);
            }

            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }

            lastAttackTime = Time.time;
        }
    }

    void StopChasing()
    {
        agent.isStopped = true;
        agent.ResetPath();

        if (animator != null)
        {
            animator.SetFloat("Blend", 0f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}