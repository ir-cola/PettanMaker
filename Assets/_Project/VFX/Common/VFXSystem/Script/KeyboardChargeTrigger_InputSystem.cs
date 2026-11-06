using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class KeyboardChargeTrigger_InputSystem : ChargeTriggerBase
{
    [Header("プレビュー用キー")]
    [SerializeField] private Key chargeKey = Key.Space;

    private bool wasPressed;

    private void OnEnable()
    {
        wasPressed = false;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        KeyControl keyControl = keyboard[chargeKey];

        if (keyControl == null)
        {
            return;
        }

        bool isPressed = keyControl.isPressed;

        if (isPressed && !wasPressed)
        {
            RaiseChargeStarted();
        }

        if (!isPressed && wasPressed)
        {
            RaiseChargeReleased();
        }

        wasPressed = isPressed;
    }
}