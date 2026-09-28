using UnityEngine;

public class ChairDamageHitbox : MonoBehaviour
{
    private ChairWeaponDamage chairWeapon;

    void Awake()
    {
        chairWeapon = GetComponentInParent<ChairWeaponDamage>();
    }

    void OnTriggerEnter(Collider other)
    {
        if (chairWeapon != null)
        {
            chairWeapon.TryDamage(other);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (chairWeapon != null)
        {
            chairWeapon.ClearEnemy(other);
        }
    }
}