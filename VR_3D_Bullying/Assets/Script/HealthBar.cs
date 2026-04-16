using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class HealthBar : MonoBehaviour
{
    [SerializeField] private Slider slider;

    private void SetHealth(int health) { 

        slider.value = health;
    }
}
