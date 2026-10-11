using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    [DefaultExecutionOrder(-100)]
    public class MobileControls : MonoBehaviour
    {
        enum Control
        {
            Move,
            Look,
            Fire,
            Super,
            Jump,
            Sprint,
            Aim,
            Pause,
            Scoreboard,
        }

        struct Finger
        {
            public Control control;
            public Vector2 origin;
        }

        static MobileControls s_Instance;
        static readonly Dictionary<int, Finger> s_Fingers = new Dictionary<int, Finger>();
        static Vector2 s_Move;
        static Vector2 s_Look;
        static bool s_Fire;
        static bool s_Aim;
        static bool s_AimToggleState;
        static bool s_Super;
        static bool s_Jump;
        static bool s_Sprint;
        static bool s_Pause;
        static bool s_Scoreboard;
        int m_EditorFingerId = -1;
        Control m_EditorControl;
        Vector2 m_EditorGrabOffset;

        Texture2D m_Circle;
        GUIStyle m_ButtonLabel;
        Vector2 m_StickOffset;
        static Rect SafeArea
        {
            get
            {
                var area = Screen.safeArea;
                return area.width > 0f && area.height > 0f ? area : new Rect(0f, 0f, Screen.width, Screen.height);
            }
        }

        public static bool Available => Application.isMobilePlatform && s_Instance != null;
        public static Vector2 Move => s_Move;
        public static Vector2 Look => s_Look;
        public static bool Fire => s_Fire;
        public static bool AimHeld => Available && s_Aim;
        public static bool SuperPressed => s_Super;
        public static bool JumpPressed => s_Jump;
        public static bool Sprint => s_Sprint;
        public static bool PausePressed => s_Pause;
        public static bool ScoreboardHeld => s_Scoreboard;
        static bool HudEditing => GameUI.MobileHudEditing;
        static bool HasScope
        {
            get
            {
                var player = PlayerNetwork.Local;
                var brawler = player != null ? player.Brawler : BrawlerRegistry.Get(GameSession.LocalHeroId);
                return brawler != null && brawler.firstPerson.rig == RigStyle.Sniper;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            s_Fingers.Clear();
            s_Move = Vector2.zero;
            s_Look = Vector2.zero;
            s_Fire = s_Aim = s_Super = s_Jump = s_Sprint = s_Pause = s_Scoreboard = false;
            s_AimToggleState = false;
            if (s_Instance != null) return;
            var go = new GameObject("MobileControls");
            DontDestroyOnLoad(go);
            go.AddComponent<MobileControls>();
        }

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
                pixels[y * 64 + x] = d <= 30f ? Color.white : Color.clear;
            }

            m_Circle = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                name = "MobileControlCircle",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            m_Circle.SetPixels32(pixels);
            m_Circle.Apply(false, true);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
                s_Fingers.Clear();
                s_AimToggleState = false;
                if (m_Circle != null) Destroy(m_Circle);
            }
        }

        public static void ResetAimToggleState()
        {
            s_AimToggleState = false;
            s_Aim = false;
        }

        void Update()
        {
            s_Move = Vector2.zero;
            s_Look = Vector2.zero;
            s_Fire = false;
            s_Aim = GameSession.MobileAimToggle && HasScope && s_AimToggleState;
            if (!HasScope)
                s_AimToggleState = false;
            s_Super = false;
            s_Jump = false;
            s_Sprint = false;
            s_Pause = false;
            s_Scoreboard = false;
            m_StickOffset = Vector2.zero;

            if (HudEditing)
            {
                s_Fingers.Clear();
                if (Application.isMobilePlatform) UpdateHudEditor();
                return;
            }

            m_EditorFingerId = -1;
            if (!GameUI.GameplayInputAllowed)
            {
                s_Fingers.Clear();
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                var position = touch.position;
                if (touch.phase == TouchPhase.Began)
                {
                    var control = GetControl(position);
                    s_Fingers[touch.fingerId] = new Finger { control = control, origin = position };
                    if (control == Control.Super) s_Super = true;
                    if (control == Control.Jump) s_Jump = true;
                    if (control == Control.Pause) s_Pause = true;
                    if (control == Control.Aim && GameSession.MobileAimToggle && HasScope)
                    {
                        s_AimToggleState = !s_AimToggleState;
                        s_Aim = s_AimToggleState;
                    }
                }

                if (!s_Fingers.TryGetValue(touch.fingerId, out var finger)) continue;
                switch (finger.control)
                {
                    case Control.Move:
                        m_StickOffset = Vector2.ClampMagnitude(position - finger.origin, StickRadius);
                        s_Move = m_StickOffset / StickRadius;
                        if (s_Move.magnitude < 0.12f) s_Move = Vector2.zero;
                        break;
                    case Control.Look:
                        float lookY = GameSession.InvertMobileLookY ? -touch.deltaPosition.y : touch.deltaPosition.y;
                        s_Look += new Vector2(touch.deltaPosition.x, lookY) * 0.1f;
                        break;
                    case Control.Fire:
                        s_Fire = true;
                        break;
                    case Control.Aim:
                        if (HasScope)
                            s_Aim = GameSession.MobileAimToggle ? s_AimToggleState : true;
                        break;
                    case Control.Sprint:
                        s_Sprint = true;
                        break;
                    case Control.Scoreboard:
                        s_Scoreboard = true;
                        break;
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    s_Fingers.Remove(touch.fingerId);
            }
        }

        Control GetControl(Vector2 point)
        {
            if (Inside(point, ButtonCenter(Control.Pause), ButtonRadius * 1.3f))
                return Control.Pause;
            if (Inside(point, ButtonCenter(Control.Scoreboard), ButtonRadius * 1.3f))
                return Control.Scoreboard;
            if (Inside(point, ButtonCenter(Control.Fire), ButtonRadius))
                return Control.Fire;
            if (HasScope && Inside(point, ButtonCenter(Control.Aim), ButtonRadius * 0.72f))
                return Control.Aim;
            if (Inside(point, ButtonCenter(Control.Super), ButtonRadius))
                return Control.Super;
            if (Inside(point, ButtonCenter(Control.Jump), ButtonRadius))
                return Control.Jump;
            if (Inside(point, ButtonCenter(Control.Sprint), ButtonRadius))
                return Control.Sprint;
            if (Inside(point, ButtonCenter(Control.Move), StickRadius))
                return Control.Move;
            var safe = SafeArea;
            if (point.x < safe.xMin + safe.width * 0.42f && point.y < safe.yMin + safe.height * 0.58f)
                return Control.Move;
            return Control.Look;
        }

        static bool Inside(Vector2 point, Vector2 center, float radius) =>
            (point - center).sqrMagnitude <= radius * radius;

        float ButtonRadius => ButtonRadiusStatic;
        float StickRadius => Mathf.Clamp(Screen.height * 0.17f, 62f, 92f);

        Vector2 ButtonCenter(Control control)
        {
            float r = ButtonRadius;
            var safe = SafeArea;
            var normalized = GetNormalizedPosition(control);
            if (normalized.HasValue)
                return ClampToSafeArea(control, new Vector2(
                    safe.xMin + normalized.Value.x * safe.width,
                    safe.yMin + normalized.Value.y * safe.height));

            switch (control)
            {
                case Control.Fire: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 1.55f, safe.yMin + r * 1.65f));
                case Control.Super: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 4.05f, safe.yMin + r * 1.65f));
                case Control.Aim: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 2.8f, safe.yMin + r * 2.875f));
                case Control.Jump: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 1.55f, safe.yMin + r * 4.1f));
                case Control.Sprint: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 4.05f, safe.yMin + r * 4.1f));
                case Control.Pause: return ClampToSafeArea(control, new Vector2(safe.xMax - r * 1.35f, safe.yMax - r * 1.35f));
                case Control.Scoreboard: return ClampToSafeArea(control, new Vector2(safe.xMin + r * 1.35f, safe.yMax - r * 1.35f));
                default: return ClampToSafeArea(control, new Vector2(safe.xMin + safe.width * 0.14f, safe.yMin + safe.height * 0.25f));
            }
        }

        static Vector2 ClampToSafeArea(Control control, Vector2 center)
        {
            var safe = SafeArea;
            float radius = ControlRadius(control);
            float horizontalInset = Mathf.Min(radius, safe.width * 0.5f);
            float verticalInset = Mathf.Min(radius, safe.height * 0.5f);
            return new Vector2(
                Mathf.Clamp(center.x, safe.xMin + horizontalInset, safe.xMax - horizontalInset),
                Mathf.Clamp(center.y, safe.yMin + verticalInset, safe.yMax - verticalInset));
        }

        static float ControlRadius(Control control) => control == Control.Move
            ? StickRadiusStatic
            : ButtonRadiusStatic * (control == Control.Pause || control == Control.Scoreboard ? 1.3f :
                control == Control.Aim ? 0.72f : 1f);

        static Vector2? GetNormalizedPosition(Control control)
        {
            string key = "fpslan.mobilehud." + control;
            if (!PlayerPrefs.HasKey(key + ".x") || !PlayerPrefs.HasKey(key + ".y"))
                return null;
            return new Vector2(PlayerPrefs.GetFloat(key + ".x"), PlayerPrefs.GetFloat(key + ".y"));
        }

        static void SetNormalizedPosition(Control control, Vector2 normalized)
        {
            var safe = SafeArea;
            float radius = ControlRadius(control);
            normalized.x = Mathf.Clamp(normalized.x, radius / safe.width, 1f - radius / safe.width);
            normalized.y = Mathf.Clamp(normalized.y, radius / safe.height, 1f - radius / safe.height);

            var candidate = new Vector2(safe.xMin + normalized.x * safe.width, safe.yMin + normalized.y * safe.height);
            foreach (Control other in System.Enum.GetValues(typeof(Control)))
            {
                if (other == control || other == Control.Look) continue;
                float otherRadius = ControlRadius(other);
                if (Inside(candidate, ButtonCenterForLayout(other), radius + otherRadius))
                    return;
            }

            string key = "fpslan.mobilehud." + control;
            PlayerPrefs.SetFloat(key + ".x", normalized.x);
            PlayerPrefs.SetFloat(key + ".y", normalized.y);
        }

        static Vector2 ButtonCenterForLayout(Control control)
        {
            var safe = SafeArea;
            var normalized = GetNormalizedPosition(control);
            if (normalized.HasValue)
                return ClampToSafeArea(control, new Vector2(
                    safe.xMin + normalized.Value.x * safe.width,
                    safe.yMin + normalized.Value.y * safe.height));
            float radius = ButtonRadiusStatic;
            switch (control)
            {
                case Control.Fire: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 1.55f, safe.yMin + radius * 1.65f));
                case Control.Super: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 4.05f, safe.yMin + radius * 1.65f));
                case Control.Aim: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 2.8f, safe.yMin + radius * 2.875f));
                case Control.Jump: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 1.55f, safe.yMin + radius * 4.1f));
                case Control.Sprint: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 4.05f, safe.yMin + radius * 4.1f));
                case Control.Pause: return ClampToSafeArea(control, new Vector2(safe.xMax - radius * 1.35f, safe.yMax - radius * 1.35f));
                case Control.Scoreboard: return ClampToSafeArea(control, new Vector2(safe.xMin + radius * 1.35f, safe.yMax - radius * 1.35f));
                default: return ClampToSafeArea(control, new Vector2(safe.xMin + safe.width * 0.14f, safe.yMin + safe.height * 0.25f));
            }
        }

        static float ButtonRadiusStatic => Mathf.Clamp(Screen.height * 0.115f, 42f, 68f) * GameSession.MobileButtonScale;
        static float StickRadiusStatic => Mathf.Clamp(Screen.height * 0.17f, 62f, 92f);

        public static void ResetHudLayout()
        {
            foreach (Control control in System.Enum.GetValues(typeof(Control)))
            {
                PlayerPrefs.DeleteKey("fpslan.mobilehud." + control + ".x");
                PlayerPrefs.DeleteKey("fpslan.mobilehud." + control + ".y");
            }
            GameSession.MobileButtonScale = 1f;
            PlayerPrefs.Save();
        }

        public static void DrawOverlay()
        {
            if (!Available || !GameUI.GameplayInputAllowed || s_Instance.m_Circle == null) return;
            s_Instance.DrawControls();
        }

        public static void DrawHudEditorOverlay()
        {
            if (s_Instance == null || !HudEditing || s_Instance.m_Circle == null) return;
            s_Instance.DrawHudEditor();
        }

        void UpdateHudEditor()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    var control = GetEditorControl(touch.position);
                    if (control.HasValue)
                    {
                        m_EditorFingerId = touch.fingerId;
                        m_EditorControl = control.Value;
                        m_EditorGrabOffset = ButtonCenter(m_EditorControl) - touch.position;
                    }
                }

                if (touch.fingerId != m_EditorFingerId) continue;
                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    SaveControlCenter(m_EditorControl, touch.position + m_EditorGrabOffset);
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    PlayerPrefs.Save();
                    m_EditorFingerId = -1;
                }
            }
        }

        Control? GetEditorControl(Vector2 point)
        {
            foreach (Control control in System.Enum.GetValues(typeof(Control)))
            {
                if (control == Control.Look) continue;
                float radius = control == Control.Move ? StickRadius : ButtonRadius *
                    (control == Control.Pause || control == Control.Scoreboard ? 0.75f : control == Control.Aim ? 0.72f : 1f);
                if (Inside(point, ButtonCenter(control), radius)) return control;
            }
            return null;
        }

        static void SaveControlCenter(Control control, Vector2 center)
        {
            var safe = SafeArea;
            var normalized = new Vector2((center.x - safe.xMin) / safe.width, (center.y - safe.yMin) / safe.height);
            SetNormalizedPosition(control, normalized);
        }

        void DrawHudEditor()
        {
            var oldColor = GUI.color;
            if (m_ButtonLabel == null)
            {
                m_ButtonLabel = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(ButtonRadius * 0.32f), 12, 22),
                    wordWrap = true,
                };
            }

            float stickRadius = StickRadius;
            var stickCenter = ButtonCenter(Control.Move);
            DrawCircle(stickCenter, stickRadius + 5f, new Color(0.02f, 0.04f, 0.09f, 0.95f));
            DrawCircle(stickCenter, stickRadius, new Color(0.16f, 0.55f, 0.78f, 0.92f));
            DrawLabel(ToGuiRect(stickCenter, stickRadius), "JOYSTICK");
            DrawEditorButton(Control.Fire, "ATAQUE", new Color(0.88f, 0.2f, 0.16f, 0.96f));
            if (HasScope)
                DrawEditorButton(Control.Aim, "MIRAR", new Color(0.12f, 0.65f, 0.62f, 0.96f), ButtonRadius * 0.72f);
            DrawEditorButton(Control.Super, "SUPER", new Color(0.88f, 0.58f, 0.08f, 0.96f));
            DrawEditorButton(Control.Jump, "SALTO", new Color(0.12f, 0.48f, 0.82f, 0.96f));
            DrawEditorButton(Control.Sprint, "CORRER", new Color(0.2f, 0.62f, 0.3f, 0.96f));
            DrawEditorButton(Control.Scoreboard, "PLACAR", new Color(0.18f, 0.3f, 0.66f, 0.96f), ButtonRadius * 0.82f);
            DrawEditorButton(Control.Pause, "PAUSA", new Color(0.48f, 0.26f, 0.7f, 0.96f), ButtonRadius * 0.82f);
            if (!Application.isMobilePlatform) HandleHudEditorMouse();
            GUI.color = oldColor;
        }

        void DrawEditorButton(Control control, string label, Color color, float radius = -1f)
        {
            var center = ButtonCenter(control);
            float size = radius > 0f ? radius : ButtonRadius;
            DrawCircle(center, size + 5f, new Color(0.02f, 0.04f, 0.09f, 0.95f));
            DrawCircle(center, size, color);
            DrawLabel(ToGuiRect(center, size), label);
        }

        void HandleHudEditorMouse()
        {
            var current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0)
            {
                var control = GetEditorControl(new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y));
                if (control.HasValue)
                {
                    m_EditorControl = control.Value;
                    var point = new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y);
                    m_EditorGrabOffset = ButtonCenter(m_EditorControl) - point;
                    m_EditorFingerId = -2;
                    current.Use();
                }
            }
            else if (current.type == EventType.MouseDrag && m_EditorFingerId == -2)
            {
                var point = new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y);
                SaveControlCenter(m_EditorControl, point + m_EditorGrabOffset);
                current.Use();
            }
            else if (current.type == EventType.MouseUp && m_EditorFingerId == -2)
            {
                PlayerPrefs.Save();
                m_EditorFingerId = -1;
                current.Use();
            }
        }

        void DrawControls()
        {
            var oldColor = GUI.color;
            if (m_ButtonLabel == null)
            {
                m_ButtonLabel = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.RoundToInt(ButtonRadius * 0.42f),
                };
            }

            float r = StickRadius;
            var safe = SafeArea;
            var stickCenter = ButtonCenter(Control.Move);
            DrawCircle(stickCenter, r, new Color(0.05f, 0.07f, 0.12f, 0.32f));
            DrawCircle(stickCenter + new Vector2(m_StickOffset.x, -m_StickOffset.y),
                r * 0.48f, new Color(0.85f, 0.9f, 1f, 0.45f));

            DrawButton(Control.Fire, "ATIRAR", new Color(1f, 0.32f, 0.2f, 0.48f));
            if (HasScope)
                DrawButton(Control.Aim, GameSession.MobileAimToggle ? (s_Aim ? "MIRAR\nATIVO" : "MIRAR\nTOCAR") : "MIRAR\nSEGURAR", s_Aim
                    ? new Color(0.08f, 0.8f, 0.68f, 0.8f)
                    : new Color(0.12f, 0.65f, 0.62f, 0.5f), ButtonRadius * 0.72f);
            var me = PlayerNetwork.Local;
            var superLabel = me == null ? "SUPER" : me.Combat.SuperReady ? "SUPER\nPRONTO" :
                "SUPER\n" + Mathf.RoundToInt(me.Combat.SuperCharge.Value * 100f) + "%";
            DrawButton(Control.Super, superLabel, new Color(1f, 0.75f, 0.16f, 0.5f));
            DrawButton(Control.Jump, "SALTO", new Color(0.26f, 0.72f, 1f, 0.45f));
            DrawButton(Control.Sprint, "CORRER", new Color(0.5f, 0.8f, 0.55f, 0.42f));
            DrawButton(Control.Scoreboard, "PLACAR", new Color(0.08f, 0.1f, 0.16f, 0.5f), ButtonRadius * 0.65f);
            DrawButton(Control.Pause, "PAUSA", new Color(0.08f, 0.1f, 0.16f, 0.5f), ButtonRadius * 0.65f);
            GUI.color = oldColor;
        }

        void DrawButton(Control control, string label, Color color, float radius = -1f)
        {
            var center = ButtonCenter(control);
            float r = radius > 0f ? radius : ButtonRadius;
            DrawCircle(center, r, color);
            DrawLabel(ToGuiRect(center, r), label);
        }

        static Rect ToGuiRect(Vector2 center, float radius) =>
            new Rect(center.x - radius, Screen.height - center.y - radius, radius * 2f, radius * 2f);

        void DrawCircle(Vector2 center, float radius, Color color)
        {
            var rect = new Rect(center.x - radius, Screen.height - center.y - radius, radius * 2f, radius * 2f);
            GUI.color = color;
            GUI.DrawTexture(rect, m_Circle);
        }

        void DrawLabel(Rect rect, string label)
        {
            GUI.color = Color.white;
            GUI.Label(rect, label, m_ButtonLabel);
        }
    }
}
