using UnityEngine;
using UnityEngine.AI;

public class RandomWander : MonoBehaviour
{
    [Header("Wander Area")]
    public float wanderRadius = 8f;
    public float waitTimeMin = 1f;
    public float waitTimeMax = 4f;

    [Header("Movement")]
    public float arriveDistance = 0.5f;
    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Animation - Optional")]
    public Animator animator;
    public string speedParameterName = "Blend";

    private NavMeshAgent agent;
    private Vector3 homePosition;
    private float waitTimer;
    private bool isWaiting;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        homePosition = transform.position;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (agent == null)
        {
            Debug.LogWarning(name + " 無 NavMeshAgent，不能隨機行路。");
            enabled = false;
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning(
                name + " 未放喺 NavMesh 上，請檢查 Spawn Point 同 NavMesh Bake。"
            );
            enabled = false;
            return;
        }

        GoToNewRandomPoint();
    }

    void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        UpdateAnimation();

        if (isWaiting)
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
            {
                isWaiting = false;
                GoToNewRandomPoint();
            }

            return;
        }

        if (!agent.pathPending &&
            agent.remainingDistance <= arriveDistance)
        {
            StartWaiting();
        }
    }

    void GoToNewRandomPoint()
    {
        for (int i = 0; i < 15; i++)
        {
            Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
            randomDirection.y = 0f;

            Vector3 possiblePoint = homePosition + randomDirection;

            NavMeshHit hit;

            if (NavMesh.SamplePosition(
                possiblePoint,
                out hit,
                3f,
                navMeshAreaMask
            ))
            {
                agent.isStopped = false;
                agent.SetDestination(hit.position);
                return;
            }
        }

        StartWaiting();
    }

    void StartWaiting()
    {
        agent.isStopped = true;
        isWaiting = true;
        waitTimer = Random.Range(waitTimeMin, waitTimeMax);
    }

    void UpdateAnimation()
    {
        if (animator == null) return;

        float movementValue = agent.velocity.magnitude > 0.1f ? 1f : 0f;

        animator.SetFloat(speedParameterName, movementValue);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(
            Application.isPlaying ? homePosition : transform.position,
            wanderRadius
        );
    }
}