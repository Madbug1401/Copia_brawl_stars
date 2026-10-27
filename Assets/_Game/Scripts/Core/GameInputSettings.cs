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
        Crouch,   // acrescentado no fim: as teclas guardadas usam o nome, mas assim nada muda de número
        ThermalScope,   // acrescentado no fim (as teclas guardadas usam o nome)
        LeanLeft,       // inclinar (estilo Rainbow Six Siege)
        LeanRight,
    }

    public static class GameInputSettings
    {
        const string PreferencePrefix = "fpslan.input.";

        // GetBinding corre dezenas de vezes por frame (cada leitura de input): as chaves e o conjunto de teclas
        // válidas são calculados uma vez, em vez de concatenar strings e usar Enum.IsDefined (reflexão + boxing).
        static readonly string[] s_Keys = BuildKeys();
        static readonly System.Collections.Generic.HashSet<int> s_DefinedKeyCodes = BuildDefinedKeyCodes();

        static string[] BuildKeys()
        {
            var actions = (GameAction[])System.Enum.GetValues(typeof(GameAction));
            int max = 0;
            foreach (var a in actions) max = Mathf.Max(max, (int)a);
            var keys = new string[max + 1];
            foreach (var a in actions) keys[(int)a] = PreferencePrefix + a;
            return keys;
        }

        static System.Collections.Generic.HashSet<int> BuildDefinedKeyCodes()
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode))) set.Add((int)k);
            return set;
        }

        static string Key(GameAction action)
        {
            int i = (int)action;
            return i >= 0 && i < s_Keys.Length && s_Keys[i] != null ? s_Keys[i] : PreferencePrefix + action;
        }

        static bool IsDefinedKey(KeyCode key) => s_DefinedKeyCodes.Contains((int)key);

        /// <summary>Versão do esquema de teclas por defeito (2: Q/E inclinam; corpo-a-corpo em C, Super em X).</summary>
        const int LayoutVersion = 2;
        static bool s_Migrated;

        /// <summary>
        /// Teclas guardadas com o esquema antigo: quem tinha o corpo-a-corpo no Q ou o Super no E (escolhido à mão)
        /// passa para os novos valores por defeito, para não chocar com o inclinar. Corre uma vez.
        /// </summary>
        static void MigrateLayout()
        {
            if (s_Migrated) return;
            s_Migrated = true;
            const string versionKey = PreferencePrefix + "layout";
            if (PlayerPrefs.GetInt(versionKey, 1) >= LayoutVersion) return;
            if (PlayerPrefs.HasKey(Key(GameAction.Melee)) && PlayerPrefs.GetInt(Key(GameAction.Melee)) == (int)KeyCode.Q)
                PlayerPrefs.DeleteKey(Key(GameAction.Melee));
            if (PlayerPrefs.HasKey(Key(GameAction.Super)) && PlayerPrefs.GetInt(Key(GameAction.Super)) == (int)KeyCode.E)
                PlayerPrefs.DeleteKey(Key(GameAction.Super));
            // Uma tecla escolhida à mão que agora é a de outra ação por defeito: a nova ação fica sem tecla até ser escolhida
            foreach (GameAction action in System.Enum.GetValues(typeof(GameAction)))
            {
                if (PlayerPrefs.HasKey(Key(action))) continue;
                var def = DefaultBinding(action);
                foreach (GameAction other in System.Enum.GetValues(typeof(GameAction)))
                    if (other != action && PlayerPrefs.HasKey(Key(other)) && PlayerPrefs.GetInt(Key(other)) == (int)def)
                        PlayerPrefs.SetInt(Key(action), (int)KeyCode.None);
            }
            PlayerPrefs.SetInt(versionKey, LayoutVersion);
            PlayerPrefs.Save();
        }

        public static KeyCode GetBinding(GameAction action)
        {
            MigrateLayout();
            var fallback = DefaultBinding(action);
            var stored = PlayerPrefs.GetInt(Key(action), (int)fallback);
            var key = (KeyCode)stored;
            if (key == KeyCode.None && PlayerPrefs.HasKey(Key(action))) return KeyCode.None; // sem tecla (migração)
            return key != KeyCode.None && IsDefinedKey(key) ? key : fallback;
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

            PlayerPrefs.SetInt(Key(action), (int)key);
            PlayerPrefs.Save();
            return true;
        }

        public static void ResetBindings()
        {
            foreach (GameAction action in System.Enum.GetValues(typeof(GameAction)))
                PlayerPrefs.DeleteKey(Key(action));
            PlayerPrefs.Save();
        }

        // teclado/rato ou comando PlayStation (GamepadInput)
        public static bool IsHeld(GameAction action) { var k = GetBinding(action); return (k != KeyCode.None && IsHeld(k)) || GamepadInput.ActionHeld(action); }
        public static bool WasPressedThisFrame(GameAction action) { var k = GetBinding(action); return (k != KeyCode.None && ReadKeyPressedThisFrame(k)) || GamepadInput.ActionPressed(action); }
        public static bool WasPressedThisFrame(KeyCode key) => ReadKeyPressedThisFrame(key);
        public static bool IsHeldKey(KeyCode key) => IsHeld(key);
        public static bool HasCustomBinding(GameAction action) => PlayerPrefs.HasKey(Key(action));

        public static string BindingLabel(GameAction action)
        {
            var key = GetBinding(action);
            if (key == KeyCode.None) return "—";
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
                case GameAction.Melee: return KeyCode.C;
                case GameAction.Super: return KeyCode.X;
                case GameAction.Jump: return KeyCode.Space;
                case GameAction.Sprint: return KeyCode.LeftShift;
                case GameAction.Scoreboard: return KeyCode.Tab;
                case GameAction.Pause: return KeyCode.Escape;
                case GameAction.Aim: return KeyCode.Mouse1;
                case GameAction.Interact: return KeyCode.F;
                case GameAction.FeedPet: return KeyCode.T;
                case GameAction.Emote: return KeyCode.Y;
                case GameAction.Crouch: return KeyCode.LeftControl;
                case GameAction.ThermalScope: return KeyCode.Mouse2;
                case GameAction.LeanLeft: return KeyCode.Q;
                case GameAction.LeanRight: return KeyCode.E;
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
