using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ChairWeaponDamage : MonoBehaviour
{
    [Header("Damage")]
    public int damage = 30;

    [Tooltip("同一隻敵人被椅子打中後，隔多久才可再受傷")]
    public float sameEnemyCooldown = 0.8f;

    [Header("Grab State")]
    public bool isHeld;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;
    private readonly Dictionary<EnemyHealth, float> lastHitTimes =
        new Dictionary<EnemyHealth, float>();

    void Awake()
    {
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
    }

    void OnEnable()
    {
        if (grabInteractable == null)
        {
            grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        }

        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }
    }

    void OnDisable()
    {
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        isHeld = true;
        Debug.Log("Chair grabbed - damage ON");
    }

    void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;
        lastHitTimes.Clear();
        Debug.Log("Chair released - damage OFF");
    }

    public void TryDamage(Collider other)
    {
        // 椅子未被玩家拿住：絕對不會造成傷害
        if (!isHeld)
        {
            return;
        }

        EnemyHealth enemy =
            other.GetComponentInParent<EnemyHealth>();

        if (enemy == null)
        {
            return;
        }

        if (lastHitTimes.TryGetValue(enemy, out float lastHitTime))
        {
            if (Time.time - lastHitTime < sameEnemyCooldown)
            {
                return;
            }
        }

        enemy.TakeDamage(damage);
        lastHitTimes[enemy] = Time.time;

        Debug.Log(
            "Chair hit enemy: " +
            enemy.name +
            " Damage: " +
            damage
        );
    }

    public void ClearEnemy(Collider other)
    {
        EnemyHealth enemy =
            other.GetComponentInParent<EnemyHealth>();

        if (enemy != null)
        {
            lastHitTimes.Remove(enemy);
        }
    }
}