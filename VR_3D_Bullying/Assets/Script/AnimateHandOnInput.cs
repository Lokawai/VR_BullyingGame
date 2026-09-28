using UnityEngine;
using UnityEngine.InputSystem;

public class AnimateHandOnInput : MonoBehaviour
{
    public InputActionProperty triggerValue;
    public InputActionProperty gripValue;
    public Animator handAnimator;

    void OnEnable()
    {
        if (triggerValue.action != null)
        {
            triggerValue.action.Enable();
        }

        if (gripValue.action != null)
        {
            gripValue.action.Enable();
        }
    }

    void OnDisable()
    {
        if (triggerValue.action != null)
        {
            triggerValue.action.Disable();
        }

        if (gripValue.action != null)
        {
            gripValue.action.Disable();
        }
    }

    void Update()
    {
        if (handAnimator == null)
        {
            return;
        }

        float trigger = 0f;
        float grip = 0f;

        if (triggerValue.action != null)
        {
            trigger = triggerValue.action.ReadValue<float>();
        }

        if (gripValue.action != null)
        {
            grip = gripValue.action.ReadValue<float>();
        }

        handAnimator.SetFloat("Trigger", trigger);
        handAnimator.SetFloat("Grip", grip);
    }
}