using UnityEngine;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FPSLAN
{
    /// <summary>Botões do comando PlayStation (DualShock 4 / DualSense), pela ordem do Windows (DirectInput).</summary>
    public enum PadButton
    {
        Square, Cross, Circle, Triangle, L1, R1, L2, R2, Share, Options, L3, R3, PS, Touchpad,
    }

    /// <summary>
    /// Comando PlayStation (DualShock 4 e DualSense, com fio ou Bluetooth). Liga-se nas definições (Controlos).
    /// Com o Input Manager antigo lê os botões JoystickButton0..13 e os eixos FPSLAN_* do InputManager.asset; com o
    /// Input System usa o Gamepad.current. As ações do jogo passam por GameInputSettings, por isso o comando funciona
    /// em tudo o que usa as teclas (disparar, mirar, Super, saltar, pausa, roda de sprays, pet, etc.).
    ///
    /// Mapa: L stick andar · R stick olhar · R2 disparar · L2 mirar · L1 Super · R1 facada · X saltar · L3 sprint ·
    /// ▢ reanimar · △ dar comida ao pet · ◯ roda de sprays · touchpad marcador · Options pausa · R3 luneta térmica ·
    /// cruzeta ↑/↓ zoom da luneta.
    /// </summary>
    public static class GamepadInput
    {
        const float StickDead = 0.18f;
        /// <summary>Velocidade de rotação com o stick no máximo (graus/s) com a sensibilidade do comando a 1.</summary>
        public const float LookDegreesPerSecond = 200f;

        static float s_NextScan;
        static bool s_Connected;
        static string s_Name = "";
        static int s_Frame = -1;
        static Vector2 s_Dpad, s_PrevDpad;
        static bool s_SprintLatch;
        static readonly bool[] s_AxisMissing = new bool[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_NextScan = 0f;
            s_Connected = false;
            s_Name = "";
            s_Frame = -1;
            s_SprintLatch = false;
        }

        /// <summary>Há um comando ligado ao PC (verificado uma vez por segundo).</summary>
        public static bool Connected { get { Scan(); return s_Connected; } }
        public static string Name { get { Scan(); return s_Name; } }
        /// <summary>Comando ligado e ativo nas definições.</summary>
        public static bool Active => GameSession.ControllerEnabled && Connected;

        static void Scan()
        {
            if (Time.unscaledTime < s_NextScan) return;
            s_NextScan = Time.unscaledTime + 1f;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            s_Connected = g != null;
            s_Name = g != null ? g.displayName : "";
#else
            s_Connected = false;
            s_Name = "";
            foreach (var n in Input.GetJoystickNames())
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                s_Connected = true;
                s_Name = n.Trim();
                break;
            }
#endif
        }

        // ------------------------------------------------------------------ leitura base

        public static bool Held(PadButton b)
        {
            if (!Active) return false;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var c = Control(b);
            return c != null && c.isPressed;
#else
            return Input.GetKey(KeyCode.JoystickButton0 + (int)b);
#endif
        }

        public static bool Pressed(PadButton b)
        {
            if (!Active) return false;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var c = Control(b);
            return c != null && c.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.JoystickButton0 + (int)b);
#endif
        }

#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Controls.ButtonControl Control(PadButton b)
        {
            var g = Gamepad.current;
            if (g == null) return null;
            switch (b)
            {
                case PadButton.Square: return g.buttonWest;
                case PadButton.Cross: return g.buttonSouth;
                case PadButton.Circle: return g.buttonEast;
                case PadButton.Triangle: return g.buttonNorth;
                case PadButton.L1: return g.leftShoulder;
                case PadButton.R1: return g.rightShoulder;
                case PadButton.L2: return g.leftTrigger;
                case PadButton.R2: return g.rightTrigger;
                case PadButton.Share: return g.selectButton;
                case PadButton.Options: return g.startButton;
                case PadButton.L3: return g.leftStickButton;
                case PadButton.R3: return g.rightStickButton;
                default: return null;
            }
        }
#endif

        /// <summary>Eixo do InputManager.asset; se não existir (projeto sem os eixos FPSLAN_*) devolve 0 sem erros.</summary>
        static float Axis(int index, string name)
        {
            if (s_AxisMissing[index]) return 0f;
            try { return Input.GetAxisRaw(name); }
            catch (System.ArgumentException) { s_AxisMissing[index] = true; return 0f; }
        }

        static Vector2 Dead(Vector2 v)
        {
            float m = v.magnitude;
            if (m < StickDead) return Vector2.zero;
            return v / m * Mathf.Clamp01((m - StickDead) / (1f - StickDead));
        }

        public static Vector2 LeftStick
        {
            get
            {
                if (!Active) return Vector2.zero;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
                return Gamepad.current != null ? Dead(Gamepad.current.leftStick.ReadValue()) : Vector2.zero;
#else
                return Dead(new Vector2(Axis(0, "FPSLAN_LX"), Axis(1, "FPSLAN_LY")));
#endif
            }
        }

        public static Vector2 RightStick
        {
            get
            {
                if (!Active) return Vector2.zero;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
                var v = Gamepad.current != null ? Dead(Gamepad.current.rightStick.ReadValue()) : Vector2.zero;
#else
                var v = Dead(new Vector2(Axis(2, "FPSLAN_RX"), Axis(3, "FPSLAN_RY")));
#endif
                if (GameSession.ControllerInvertY) v.y = -v.y;
                return v;
            }
        }

        static void UpdateFrame()
        {
            if (s_Frame == Time.frameCount) return;
            s_Frame = Time.frameCount;
            s_PrevDpad = s_Dpad;
            if (!Active) { s_Dpad = Vector2.zero; s_SprintLatch = false; return; }
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            s_Dpad = Gamepad.current != null ? Gamepad.current.dpad.ReadValue() : Vector2.zero;
#else
            s_Dpad = new Vector2(Axis(4, "FPSLAN_DX"), Axis(5, "FPSLAN_DY"));
#endif
            // sprint: clicar no L3 liga; desliga quando o stick volta ao centro
            if (Pressed(PadButton.L3)) s_SprintLatch = !s_SprintLatch;
            if (LeftStick.sqrMagnitude < 0.25f * 0.25f) s_SprintLatch = false;
        }

        /// <summary>Cruzeta neste frame: +1 cima, −1 baixo (só no frame em que é carregada).</summary>
        public static float DpadVerticalPressed
        {
            get
            {
                UpdateFrame();
                if (s_Dpad.y > 0.5f && s_PrevDpad.y <= 0.5f) return 1f;
                if (s_Dpad.y < -0.5f && s_PrevDpad.y >= -0.5f) return -1f;
                return 0f;
            }
        }

        /// <summary>Rotação da câmara neste frame, já em "unidades de rato" (o jogo multiplica pela sensibilidade do rato).</summary>
        public static Vector2 LookDelta(float dt)
        {
            var v = RightStick;
            if (v == Vector2.zero) return Vector2.zero;
            // curva: mais precisão perto do centro, rápido no máximo
            v = v.normalized * (v.magnitude * v.magnitude);
            float deg = LookDegreesPerSecond * GameSession.ControllerLookSensitivity * dt;
            return v * deg / Mathf.Max(0.01f, GameSession.Sensitivity);
        }

        // ------------------------------------------------------------------ ações do jogo

        /// <summary>Botão do comando de cada ação (None se a ação não tiver botão).</summary>
        public static bool TryButton(GameAction action, out PadButton b)
        {
            switch (action)
            {
                case GameAction.Fire: b = PadButton.R2; return true;
                case GameAction.Aim: b = PadButton.L2; return true;
                case GameAction.Super: b = PadButton.L1; return true;
                case GameAction.Melee: b = PadButton.R1; return true;
                case GameAction.Jump: b = PadButton.Cross; return true;
                case GameAction.Interact: b = PadButton.Square; return true;
                case GameAction.FeedPet: b = PadButton.Triangle; return true;
                case GameAction.Emote: b = PadButton.Circle; return true;
                case GameAction.Scoreboard: b = PadButton.Touchpad; return true;
                case GameAction.Pause: b = PadButton.Options; return true;
                case GameAction.ThermalScope: b = PadButton.R3; return true;
                default: b = PadButton.Square; return false;
            }
        }

        public static bool ActionHeld(GameAction action)
        {
            if (!Active) return false;
            if (action == GameAction.Sprint) { UpdateFrame(); return s_SprintLatch; }
            return TryButton(action, out var b) && Held(b);
        }

        public static bool ActionPressed(GameAction action)
        {
            if (!Active) return false;
            return TryButton(action, out var b) && Pressed(b);
        }

        public static string ButtonLabel(PadButton b)
        {
            switch (b)
            {
                case PadButton.Square: return "Square";
                case PadButton.Cross: return "Cross (X)";
                case PadButton.Circle: return "Circle";
                case PadButton.Triangle: return "Triangle";
                case PadButton.Touchpad: return "Touchpad";
                default: return b.ToString();
            }
        }
    }
}
