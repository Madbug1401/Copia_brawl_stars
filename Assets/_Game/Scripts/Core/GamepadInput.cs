using UnityEngine;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FPSLAN
{
    /// <summary>Botões do comando PlayStation (DualShock 4 / DualSense), pela ordem do Windows (DirectInput); a cruzeta
    /// (lida pelos eixos) e "nenhum" têm valores próprios.</summary>
    public enum PadButton
    {
        Square = 0, Cross = 1, Circle = 2, Triangle = 3, L1 = 4, R1 = 5, L2 = 6, R2 = 7, Share = 8, Options = 9,
        L3 = 10, R3 = 11, PS = 12, Touchpad = 13,
        DpadUp = 20, DpadDown = 21, DpadLeft = 22, DpadRight = 23,
        None = 99,
    }

    /// <summary>
    /// Comando PlayStation (DualShock 4 e DualSense, com fio ou Bluetooth). Liga-se nas definições (Controlos).
    /// Com o Input Manager antigo lê os botões JoystickButton0..13 e os eixos FPSLAN_* do InputManager.asset; com o
    /// Input System usa o Gamepad.current. As ações do jogo passam por GameInputSettings, por isso o comando funciona
    /// em tudo o que usa as teclas (disparar, mirar, Super, saltar, pausa, roda de sprays, pet, etc.).
    ///
    /// Os botões de cada ação mudam-se na aba CONTROLLER das definições (guardados em PlayerPrefs "fpslan.pad.*").
    /// Por defeito: L stick andar · R stick olhar · R2 disparar · L2 mirar · L1 Super · R1 facada · X saltar · L3 sprint ·
    /// ◯ agachar/slide · cruzeta ←/→ inclinar · ▢ reanimar · △ dar comida ao pet · Create roda de sprays ·
    /// touchpad marcador · Options pausa · R3 luneta térmica · cruzeta ↑/↓ zoom da luneta.
    /// </summary>
    public static class GamepadInput
    {
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

        /// <summary>Botão carregado (com o comando ativo nas definições).</summary>
        public static bool Held(PadButton b) => Active && RawHeld(b);
        /// <summary>Botão carregado neste frame (com o comando ativo nas definições).</summary>
        public static bool Pressed(PadButton b) => Active && RawPressed(b);

        /// <summary>Leitura direta (basta o comando estar ligado): usada também para mudar os botões nas definições.</summary>
        public static bool RawHeld(PadButton b)
        {
            if (b == PadButton.None || !Connected) return false;
            if (IsDpad(b)) { UpdateFrame(); return DpadDir(s_Dpad, b); }
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var c = Control(b);
            return c != null && c.isPressed;
#else
            return Input.GetKey(KeyCode.JoystickButton0 + (int)b);
#endif
        }

        public static bool RawPressed(PadButton b)
        {
            if (b == PadButton.None || !Connected) return false;
            if (IsDpad(b)) { UpdateFrame(); return DpadDir(s_Dpad, b) && !DpadDir(s_PrevDpad, b); }
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var c = Control(b);
            return c != null && c.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.JoystickButton0 + (int)b);
#endif
        }

        static bool IsDpad(PadButton b) => b >= PadButton.DpadUp && b <= PadButton.DpadRight;

        static bool DpadDir(Vector2 d, PadButton b)
        {
            switch (b)
            {
                case PadButton.DpadUp: return d.y > 0.5f;
                case PadButton.DpadDown: return d.y < -0.5f;
                case PadButton.DpadLeft: return d.x < -0.5f;
                case PadButton.DpadRight: return d.x > 0.5f;
                default: return false;
            }
        }

        /// <summary>Todos os botões que se podem atribuir (para a captura nas definições).</summary>
        public static readonly PadButton[] AllButtons =
        {
            PadButton.Cross, PadButton.Circle, PadButton.Square, PadButton.Triangle, PadButton.L1, PadButton.R1,
            PadButton.L2, PadButton.R2, PadButton.L3, PadButton.R3, PadButton.Share, PadButton.Options, PadButton.Touchpad,
            PadButton.DpadUp, PadButton.DpadDown, PadButton.DpadLeft, PadButton.DpadRight,
        };

        /// <summary>Primeiro botão carregado neste frame (None se nenhum).</summary>
        public static PadButton AnyPressed()
        {
            foreach (var b in AllButtons) if (RawPressed(b)) return b;
            return PadButton.None;
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

        static Vector2 Dead(Vector2 v, float dead)
        {
            float m = v.magnitude;
            if (m < dead) return Vector2.zero;
            return v / m * Mathf.Clamp01((m - dead) / (1f - dead));
        }

        public static Vector2 LeftStick
        {
            get
            {
                if (!Active) return Vector2.zero;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
                var v = Gamepad.current != null ? Dead(Gamepad.current.leftStick.ReadValue(), GameSession.ControllerDeadZoneL) : Vector2.zero;
#else
                var v = Dead(new Vector2(Axis(0, "FPSLAN_LX"), Axis(1, "FPSLAN_LY")), GameSession.ControllerDeadZoneL);
#endif
                return v;
            }
        }

        /// <summary>Stick esquerdo para andar, com a sensibilidade do stick esquerdo (máximo 1).</summary>
        public static Vector2 MoveStick => Vector2.ClampMagnitude(LeftStick * GameSession.ControllerMoveSensitivity, 1f);

        public static Vector2 RightStick
        {
            get
            {
                if (!Active) return Vector2.zero;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
                var v = Gamepad.current != null ? Dead(Gamepad.current.rightStick.ReadValue(), GameSession.ControllerDeadZoneR) : Vector2.zero;
#else
                var v = Dead(new Vector2(Axis(2, "FPSLAN_RX"), Axis(3, "FPSLAN_RY")), GameSession.ControllerDeadZoneR);
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
            if (!Connected) { s_Dpad = Vector2.zero; s_SprintLatch = false; return; }
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            s_Dpad = Gamepad.current != null ? Gamepad.current.dpad.ReadValue() : Vector2.zero;
#else
            s_Dpad = new Vector2(Axis(4, "FPSLAN_DX"), Axis(5, "FPSLAN_DY"));
#endif
            // sprint: clicar no botão do sprint (L3 por defeito) liga; desliga quando o stick volta ao centro
            var sprint = GetBinding(SprintId);
            if (Active && !IsDpad(sprint) && RawPressed(sprint)) s_SprintLatch = !s_SprintLatch;
            if (!Active || LeftStick.sqrMagnitude < 0.25f * 0.25f) s_SprintLatch = false;
        }

        /// <summary>Zoom da luneta neste frame: +1 aproximar, −1 afastar (botões ZoomIn / ZoomOut).</summary>
        public static float ZoomNotches
        {
            get
            {
                if (!Active) return 0f;
                if (RawPressed(GetBinding(ZoomInId))) return 1f;
                if (RawPressed(GetBinding(ZoomOutId))) return -1f;
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

        /// <summary>Texto de diagnóstico (aba CONTROLLER): botões carregados e valores dos sticks/cruzeta, lidos agora.</summary>
        public static string DebugReadout()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Buttons:");
            bool any = false;
            foreach (var b in AllButtons)
                if (RawHeld(b)) { sb.Append(' ').Append(ButtonLabel(b)); any = true; }
            if (!any) sb.Append(" none");
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
            var g = Gamepad.current;
            var l = g != null ? g.leftStick.ReadValue() : Vector2.zero;
            var r = g != null ? g.rightStick.ReadValue() : Vector2.zero;
#else
            var l = new Vector2(Axis(0, "FPSLAN_LX"), Axis(1, "FPSLAN_LY"));
            var r = new Vector2(Axis(2, "FPSLAN_RX"), Axis(3, "FPSLAN_RY"));
#endif
            sb.Append("   L stick ").Append(l.x.ToString("0.00")).Append(", ").Append(l.y.ToString("0.00"));
            sb.Append("   R stick ").Append(r.x.ToString("0.00")).Append(", ").Append(r.y.ToString("0.00"));
            if (!Application.isFocused) sb.Append("   (game window not focused: click inside the game)");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ ações do jogo e botões configuráveis

        const string PrefPrefix = "fpslan.pad.";
        public const string SprintId = "Sprint", ZoomInId = "ZoomIn", ZoomOutId = "ZoomOut";

        /// <summary>Ações que se podem mapear no comando (ids = nome da GameAction, mais o zoom da luneta).</summary>
        public static readonly string[] BindableIds =
        {
            "Fire", "Aim", "Super", "Melee", "Jump", SprintId, "Crouch", "LeanLeft", "LeanRight", "Interact", "FeedPet",
            "Emote", "Scoreboard", "Pause", "ThermalScope", ZoomInId, ZoomOutId,
        };

        public static PadButton DefaultBinding(string id)
        {
            switch (id)
            {
                case "Fire": return PadButton.R2;
                case "Aim": return PadButton.L2;
                case "Super": return PadButton.L1;
                case "Melee": return PadButton.R1;
                case "Jump": return PadButton.Cross;
                case SprintId: return PadButton.L3;
                case "Interact": return PadButton.Square;
                case "FeedPet": return PadButton.Triangle;
                case "Crouch": return PadButton.Circle;
                case "LeanLeft": return PadButton.DpadLeft;
                case "LeanRight": return PadButton.DpadRight;
                case "Emote": return PadButton.Share;
                case "Scoreboard": return PadButton.Touchpad;
                case "Pause": return PadButton.Options;
                case "ThermalScope": return PadButton.R3;
                case ZoomInId: return PadButton.DpadUp;
                case ZoomOutId: return PadButton.DpadDown;
                default: return PadButton.None;
            }
        }

        public static PadButton GetBinding(string id)
        {
            int v = PlayerPrefs.GetInt(PrefPrefix + id, (int)DefaultBinding(id));
            return System.Enum.IsDefined(typeof(PadButton), v) ? (PadButton)v : DefaultBinding(id);
        }

        /// <summary>Atribui o botão à ação; se outra ação já o usava, as duas trocam de botão. Devolve a ação trocada (ou null).</summary>
        public static string SetBinding(string id, PadButton b)
        {
            var old = GetBinding(id);
            string swapped = null;
            if (b != PadButton.None)
                foreach (var other in BindableIds)
                    if (other != id && GetBinding(other) == b)
                    {
                        PlayerPrefs.SetInt(PrefPrefix + other, (int)old);
                        swapped = other;
                    }
            PlayerPrefs.SetInt(PrefPrefix + id, (int)b);
            PlayerPrefs.Save();
            return swapped;
        }

        public static void ResetBindings()
        {
            foreach (var id in BindableIds) PlayerPrefs.DeleteKey(PrefPrefix + id);
            PlayerPrefs.Save();
        }

        public static bool ActionHeld(GameAction action)
        {
            if (!Active) return false;
            if (action == GameAction.Sprint) { UpdateFrame(); return s_SprintLatch; }
            var b = GetBinding(action.ToString());
            return b != PadButton.None && RawHeld(b);
        }

        public static bool ActionPressed(GameAction action)
        {
            if (!Active || action == GameAction.Sprint) return false;
            var b = GetBinding(action.ToString());
            return b != PadButton.None && RawPressed(b);
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
                case PadButton.Share: return "Create / Share";
                case PadButton.DpadUp: return "D-pad Up";
                case PadButton.DpadDown: return "D-pad Down";
                case PadButton.DpadLeft: return "D-pad Left";
                case PadButton.DpadRight: return "D-pad Right";
                case PadButton.None: return "—";
                default: return b.ToString();
            }
        }
    }
}
