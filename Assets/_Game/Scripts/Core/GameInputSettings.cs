using UnityEngine;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FPSLAN
{
    public enum GameAction
    {
        MoveForward,
        MoveBackward,
        MoveLeft,
        MoveRight,
        Fire,
        Melee,
        Super,
        Jump,
        Sprint,
        Scoreboard,
        Pause,
        Aim,
        Interact,
        FeedPet,
        Emote,
    }

    public static class GameInputSettings
    {
        const string PreferencePrefix = "fpslan.input.";

        public static KeyCode GetBinding(GameAction action)
        {
            var fallback = DefaultBinding(action);
            var stored = PlayerPrefs.GetInt(PreferencePrefix + action, (int)fallback);
            var key = (KeyCode)stored;
            return key != KeyCode.None && System.Enum.IsDefined(typeof(KeyCode), key) ? key : fallback;
        }

        public static bool TrySetBinding(GameAction action, KeyCode key)
        {
            if (key == KeyCode.None || !System.Enum.IsDefined(typeof(KeyCode), key))
                return false;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6 &&
                (int)key - (int)KeyCode.Mouse0 > 4)
                return false;

            foreach (GameAction other in System.Enum.GetValues(typeof(GameAction)))
                if (other != action && GetBinding(other) == key)
                    return false;

            PlayerPrefs.SetInt(PreferencePrefix + action, (int)key);
            PlayerPrefs.Save();
            return true;
        }

        public static void ResetBindings()
        {
            foreach (GameAction action in System.Enum.GetValues(typeof(GameAction)))
                PlayerPrefs.DeleteKey(PreferencePrefix + action);
            PlayerPrefs.Save();
        }

        public static bool IsHeld(GameAction action) => IsHeld(GetBinding(action));
        public static bool WasPressedThisFrame(GameAction action) => ReadKeyPressedThisFrame(GetBinding(action));
        public static bool WasPressedThisFrame(KeyCode key) => ReadKeyPressedThisFrame(key);
        public static bool HasCustomBinding(GameAction action) => PlayerPrefs.HasKey(PreferencePrefix + action);

        public static string BindingLabel(GameAction action)
        {
            var key = GetBinding(action);
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return "Mouse " + ((int)key - (int)KeyCode.Mouse0 + 1);
            return key.ToString().Replace("Left", "Left ").Replace("Right", "Right ");
        }

        static KeyCode DefaultBinding(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return KeyCode.W;
                case GameAction.MoveBackward: return KeyCode.S;
                case GameAction.MoveLeft: return KeyCode.A;
                case GameAction.MoveRight: return KeyCode.D;
                case GameAction.Fire: return KeyCode.Mouse0;
                case GameAction.Melee: return KeyCode.Q;
                case GameAction.Super: return KeyCode.E;
                case GameAction.Jump: return KeyCode.Space;
                case GameAction.Sprint: return KeyCode.LeftShift;
                case GameAction.Scoreboard: return KeyCode.Tab;
                case GameAction.Pause: return KeyCode.Escape;
                case GameAction.Aim: return KeyCode.Mouse1;
                case GameAction.Interact: return KeyCode.F;
                case GameAction.FeedPet: return KeyCode.T;
                case GameAction.Emote: return KeyCode.Y;
                default: return KeyCode.None;
            }
        }

        static bool IsHeld(KeyCode key)
        {
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return MouseButton(mouse, (int)key - (int)KeyCode.Mouse0)?.isPressed ?? false;
            var control = FindKey(key);
            return control != null && control.isPressed;
#else
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return Input.GetMouseButton((int)key - (int)KeyCode.Mouse0);
            return Input.GetKey(key);
#endif
        }

        static bool ReadKeyPressedThisFrame(KeyCode key)
        {
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return MouseButton(mouse, (int)key - (int)KeyCode.Mouse0)?.wasPressedThisFrame ?? false;
            var control = FindKey(key);
            return control != null && control.wasPressedThisFrame;
#else
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return Input.GetMouseButtonDown((int)key - (int)KeyCode.Mouse0);
            return Input.GetKeyDown(key);
#endif
        }

#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
        static ButtonControl MouseButton(Mouse mouse, int index)
        {
            if (mouse == null) return null;
            switch (index)
            {
                case 0: return mouse.leftButton;
                case 1: return mouse.rightButton;
                case 2: return mouse.middleButton;
                case 3: return mouse.forwardButton;
                case 4: return mouse.backButton;
                default: return null;
            }
        }

        static KeyControl FindKey(KeyCode key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return null;

            switch (key)
            {
                case KeyCode.Space: return keyboard.spaceKey;
                case KeyCode.LeftShift: return keyboard.leftShiftKey;
                case KeyCode.RightShift: return keyboard.rightShiftKey;
                case KeyCode.LeftControl: return keyboard.leftCtrlKey;
                case KeyCode.RightControl: return keyboard.rightCtrlKey;
                case KeyCode.LeftAlt: return keyboard.leftAltKey;
                case KeyCode.RightAlt: return keyboard.rightAltKey;
                case KeyCode.Tab: return keyboard.tabKey;
                case KeyCode.Escape: return keyboard.escapeKey;
                case KeyCode.Backspace: return keyboard.backspaceKey;
                case KeyCode.Return: return keyboard.enterKey;
                case KeyCode.LeftArrow: return keyboard.leftArrowKey;
                case KeyCode.RightArrow: return keyboard.rightArrowKey;
                case KeyCode.UpArrow: return keyboard.upArrowKey;
                case KeyCode.DownArrow: return keyboard.downArrowKey;
                case KeyCode.Delete: return keyboard.deleteKey;
                case KeyCode.Insert: return keyboard.insertKey;
                case KeyCode.Home: return keyboard.homeKey;
                case KeyCode.End: return keyboard.endKey;
                case KeyCode.PageUp: return keyboard.pageUpKey;
                case KeyCode.PageDown: return keyboard.pageDownKey;
            }

            return keyboard.FindKeyOnCurrentKeyboardLayout(key.ToString());
        }
#endif
    }
}
