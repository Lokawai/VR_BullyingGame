using System;
using System.Reflection;
using Convai.Runtime.Behaviors;
using UnityEngine;

namespace Convai.Sample.Behaviors
{
    /// <summary>
    ///     Sample player behaviour that enforces push-to-talk by requiring a key to be pressed before broadcasting
    ///     transcripts.
    /// </summary>
    public class PlayerPushToTalkBehavior : ConvaiPlayerBehaviorBase
    {
        [SerializeField] private KeyCode pushToTalkKey = KeyCode.V;

        /// <inheritdoc />
        public override bool OnInputStarted(IConvaiPlayerAgent agent)
        {
            if (!IsPushToTalkHeld())
            {
                Debug.LogWarning($"[PushToTalk] Input ignored because {pushToTalkKey} is not held.");
                return true;
            }

            return false;
        }

        private bool IsPushToTalkHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(pushToTalkKey);
#elif ENABLE_INPUT_SYSTEM
            return IsInputSystemKeyHeld(pushToTalkKey);
#else
            return false;
#endif
        }

        private static bool IsInputSystemKeyHeld(KeyCode keyCode)
        {
            var keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
            var keyType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
            if (keyboardType == null || keyType == null) return false;

            object keyboard = keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (keyboard == null) return false;

            string keyName = ConvertToInputSystemKeyName(keyCode);
            if (string.IsNullOrEmpty(keyName)) return false;

            object keyEnumValue;
            try
            {
                keyEnumValue = Enum.Parse(keyType, keyName, false);
            }
            catch
            {
                return false;
            }

            PropertyInfo indexer = keyboardType.GetProperty("Item", new[] { keyType });
            object keyControl = indexer?.GetValue(keyboard, new[] { keyEnumValue });
            if (keyControl == null) return false;

            PropertyInfo isPressedProperty =
                keyControl.GetType().GetProperty("isPressed", BindingFlags.Public | BindingFlags.Instance);
            if (isPressedProperty?.GetValue(keyControl) is bool isPressed) return isPressed;

            return false;
        }

        private static string ConvertToInputSystemKeyName(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.Alpha0: return "Digit0";
                case KeyCode.Alpha1: return "Digit1";
                case KeyCode.Alpha2: return "Digit2";
                case KeyCode.Alpha3: return "Digit3";
                case KeyCode.Alpha4: return "Digit4";
                case KeyCode.Alpha5: return "Digit5";
                case KeyCode.Alpha6: return "Digit6";
                case KeyCode.Alpha7: return "Digit7";
                case KeyCode.Alpha8: return "Digit8";
                case KeyCode.Alpha9: return "Digit9";
                case KeyCode.Keypad0: return "Numpad0";
                case KeyCode.Keypad1: return "Numpad1";
                case KeyCode.Keypad2: return "Numpad2";
                case KeyCode.Keypad3: return "Numpad3";
                case KeyCode.Keypad4: return "Numpad4";
                case KeyCode.Keypad5: return "Numpad5";
                case KeyCode.Keypad6: return "Numpad6";
                case KeyCode.Keypad7: return "Numpad7";
                case KeyCode.Keypad8: return "Numpad8";
                case KeyCode.Keypad9: return "Numpad9";
                case KeyCode.LeftControl: return "LeftCtrl";
                case KeyCode.RightControl: return "RightCtrl";
                case KeyCode.Return: return "Enter";
                case KeyCode.BackQuote: return "Backquote";
                default: return keyCode.ToString();
            }
        }
    }
}
