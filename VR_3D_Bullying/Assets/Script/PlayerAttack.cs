using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAttack : MonoBehaviour
{
    [Header("XR Input")]
    public InputActionProperty attackAction;

    [Header("Attack")]
    public Camera attackCamera;
    public int damage = 10;
    public float attackRange = 3f;
    public float attackCooldown = 0.5f;

    private float nextAttackTime;

    void Start()
    {
        if (attackCamera == null)
        {
            attackCamera = Camera.main;
        }
    }

    void OnEnable()
    {
        if (attackAction.action != null)
        {
            attackAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (attackAction.action != null)
        {
            attackAction.action.Disable();
        }
    }

    void Update()
    {
        if (attackAction.action == null)
        {
            return;
        }

        if (attackAction.action.WasPressedThisFrame())
        {
            TryAttack();
        }
    }

    public void TryAttack()
    {
        if (Time.time < nextAttackTime)
        {
            return;
        }

        nextAttackTime = Time.time + attackCooldown;

        if (attackCamera == null)
        {
            Debug.LogWarning("PlayerAttack：請將 Main Camera 拖入 Attack Camera。");
            return;
        }

        Ray ray = new Ray(
            attackCamera.transform.position,
            attackCamera.transform.forward
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            attackRange,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        ))
        {
            EnemyHealth enemy = hit.collider.GetComponentInParent<EnemyHealth>();

            if (enemy != null)
            {
                enemy.TakeDamage(damage);
                Debug.Log("Hit enemy: " + enemy.name);
            }
        }
    }
}