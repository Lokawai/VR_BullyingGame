using UnityEngine;

public class Player : MonoBehaviour
{

    [SerializeField] GameObject attacker;

    [SerializeField] GameObject player;

    public int maxhealth = 100;

    public int currentHealth = 100;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentHealth = maxhealth  ;
    }

    // Update is called once per frame
    void Update()
    {
        if (attacker ) { 
        
        currentHealth -= 5 ;
        }
    }
}
