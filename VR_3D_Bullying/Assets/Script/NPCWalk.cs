using UnityEngine;

public class NPCWalk : MonoBehaviour
{
   public float speed = 2f;
    public Transform[] waypoints;
    int index;

    void Update()
    {
        if (waypoints.Length == 0) return;

        Vector3 target = waypoints[index].position;
        Vector3 dir = target - transform.position;

        if (dir.magnitude < 0.1f)
        {
            index = (index + 1) % waypoints.Length;
            return;
        }

        dir.Normalize();
        transform.position += dir * speed * Time.deltaTime;
        transform.forward = dir;
        // Animator just plays walk on loop, no parameters needed
    }
}
