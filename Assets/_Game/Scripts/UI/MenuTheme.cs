using UnityEngine;

namespace FPSLAN
{
    /// <summary>Visual partilhado pelos menus, definiÃƒÂ§ÃƒÂµes, progressÃƒÂ£o e resultados.</summary>
    public static class MenuTheme
    {
        public static readonly Color Deep = new Color(0.02f, 0.05f, 0.1f, 1f);
        public static readonly Color Glass = new Color(0.16f, 0.27f, 0.45f, 0.62f);
        public static readonly Color Cyan = new Color(0.25f, 0.88f, 0.82f, 1f);
        public static readonly Color CyanDim = new Color(0.25f, 0.75f, 0.78f, 0.55f);
        public static readonly Color Coral = new Color(0.9f, 0.43f, 0.37f, 1f);
        public static readonly Color White = new Color(0.9f, 0.94f, 1f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.7f, 0.82f, 1f);
        public static readonly Color Selected = new Color(0.12f, 0.55f, 0.6f, 0.95f);
        public static float Scale => Mathf.Clamp(Mathf.Min(Screen.width / 1440f, Screen.height / 810f), 0.45f, 3f);
        public static Font Font { get; private set; }
        static GUIStyle s_Fill, s_Line, s_Glow;
        static GUISkin s_Skin;
        static float s_Scale = -1f;
        static readonly System.Collections.Generic.Dictionary<GUIStyle, GUIStyle> s_TextStyles = new System.Collections.Generic.Dictionary<GUIStyle, GUIStyle>();
        static readonly System.Collections.Generic.Dictionary<int, float> s_Hover = new System.Collections.Generic.Dictionary<int, float>();
        public static GUISkin Skin { get { Ensure(); return s_Skin; } }

        public static void Ensure()
        {
            if (s_Fill == null)
            {
                try { Font = UnityEngine.Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32); }
                catch { Font = null; }
                s_Fill = Slice(GameUI.MakeRoundedFill(64, 14), 16);
                s_Line = Slice(GameUI.MakeRoundedStroke(64, 14, 2.2f), 16);
                s_Glow = Slice(GameUI.MakeGlow(96, 30), 40);
                s_Skin = Object.Instantiate(GUI.skin);
                s_Skin.name = "FPSLAN Main Menu Theme";
                s_Skin.font = Font;
                var box = Surface(Glass, CyanDim);
                var field = Surface(new Color(0.9f, 0.92f, 0.95f, 0.95f), CyanDim);
                foreach (var state in new[] { s_Skin.box.normal, s_Skin.box.hover, s_Skin.box.active }) state.background = box;
                s_Skin.box.border = new RectOffset(16, 16, 16, 16);
                foreach (var style in new[] { s_Skin.textField, s_Skin.textArea })
                {
                    style.border = new RectOffset(16, 16, 16, 16);
                    foreach (var state in new[] { style.normal, style.focused, style.hover, style.active })
                    { state.background = field; state.textColor = Deep; }
                }
                s_Skin.horizontalSlider.normal.background = Surface(Deep, CyanDim);
                s_Skin.horizontalSlider.border = new RectOffset(16, 16, 16, 16);
                var thumb = Surface(Cyan, Cyan);
                foreach (var state in new[] { s_Skin.horizontalSliderThumb.normal, s_Skin.horizontalSliderThumb.hover, s_Skin.horizontalSliderThumb.active }) state.background = thumb;
                s_Skin.horizontalSliderThumb.border = new RectOffset(16, 16, 16, 16);
                s_Skin.verticalScrollbar.normal.background = Surface(Deep, CyanDim);
                foreach (var state in new[] { s_Skin.verticalScrollbarThumb.normal, s_Skin.verticalScrollbarThumb.hover, s_Skin.verticalScrollbarThumb.active }) state.background = thumb;
                s_Skin.verticalScrollbarThumb.border = new RectOffset(16, 16, 16, 16);
#if UNITY_EDITOR
                foreach (var control in new[] { s_Skin.box, s_Skin.textField, s_Skin.textArea, s_Skin.horizontalSlider, s_Skin.horizontalSliderThumb, s_Skin.verticalScrollbar, s_Skin.verticalScrollbarThumb })
                    foreach (var state in new[] { control.normal, control.hover, control.active, control.focused, control.onNormal, control.onHover, control.onActive, control.onFocused })
                        state.scaledBackgrounds = System.Array.Empty<Texture2D>();
#endif
            }
            if (!Mathf.Approximately(s_Scale, Scale))
            {
                s_Scale = Scale;
                s_Skin.label.fontSize = Mathf.Max(10, Mathf.RoundToInt(17 * Scale));
                s_Skin.label.normal.textColor = White;
                s_Skin.textField.fontSize = Mathf.Max(10, Mathf.RoundToInt(17 * Scale));
                s_Skin.horizontalSlider.fixedHeight = 18 * Scale;
                s_Skin.horizontalSliderThumb.fixedHeight = 22 * Scale;
                s_Skin.horizontalSliderThumb.fixedWidth = 16 * Scale;
                s_Skin.verticalScrollbar.fixedWidth = 12 * Scale;
                s_Skin.verticalScrollbarThumb.fixedWidth = 12 * Scale;
            }
        }

        static GUIStyle Slice(Texture2D texture, int border) => new GUIStyle { border = new RectOffset(border, border, border, border), normal = { background = texture } };
        static Texture2D Surface(Color fill, Color edge)
        {
            const int n = 64; const float radius = 14;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var p = new Vector2(Mathf.Abs(x + 0.5f - n / 2f), Mathf.Abs(y + 0.5f - n / 2f)) - Vector2.one * (n / 2f - radius);
                float d = new Vector2(Mathf.Max(0, p.x), Mathf.Max(0, p.y)).magnitude + Mathf.Min(0, Mathf.Max(p.x, p.y)) - radius;
                var c = d > -2.2f ? edge : fill;
                c.a *= Mathf.Clamp01(0.5f - d);
                pixels[y * n + x] = c;
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        static void Draw(GUIStyle style, Rect r, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color; GUI.color *= color;
            style.Draw(r, GUIContent.none, false, false, false, false); GUI.color = old;
        }
        static Rect Expand(Rect r, float amount) => new Rect(r.x - amount, r.y - amount, r.width + amount * 2, r.height + amount * 2);
        public static void Panel(Rect r, bool selected = false)
        {
            Ensure();
            if (selected) Draw(s_Glow, Expand(r, 12 * Scale), new Color(Cyan.r, Cyan.g, Cyan.b, 0.3f));
            Draw(s_Fill, r, Glass);
            Draw(s_Line, r, selected ? Cyan : new Color(0.55f, 0.75f, 0.95f, 0.3f));
            float k = 9 * Scale;
            Line(new Rect(r.x + 10 * Scale, r.y + 10 * Scale, k, 1.5f * Scale), CyanDim);
            Line(new Rect(r.xMax - 19 * Scale, r.yMax - 11 * Scale, k, 1.5f * Scale), CyanDim);
        }
        public static void Line(Rect r, Color c)
        { var old = GUI.color; GUI.color *= c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        public static void Text(Rect r, string text, GUIStyle style, Color color)
        {
            if (!s_TextStyles.TryGetValue(style, out var clean))
            {
                if (s_TextStyles.Count > 512) s_TextStyles.Clear();
                clean = new GUIStyle { padding = new RectOffset(style.padding.left, style.padding.right, style.padding.top, style.padding.bottom) };
                foreach (var state in new[] { clean.normal, clean.hover, clean.active, clean.focused, clean.onNormal, clean.onHover, clean.onActive, clean.onFocused })
                {
                    state.background = null;
#if UNITY_EDITOR
                    state.scaledBackgrounds = System.Array.Empty<Texture2D>();
#endif
                }
                s_TextStyles[style] = clean;
            }
            clean.font = style.font;
            clean.fontSize = style.fontSize;
            clean.fontStyle = style.fontStyle;
            clean.alignment = style.alignment;
            clean.wordWrap = style.wordWrap;
            clean.clipping = style.clipping;
            clean.normal.textColor = color;
            GUI.Label(r, text, clean);
        }
        public static bool Button(Rect r, string text, Color intent, bool enabled, GUIStyle style)
        {
            Ensure();
            bool selected = intent == Selected;
            bool hover = GUI.enabled && enabled && r.Contains(Event.current.mousePosition);
            int id = GUIUtility.GetControlID("FPSLAN.MenuTheme.Button".GetHashCode(), FocusType.Passive, r);
            s_Hover.TryGetValue(id, out float h);
            if (Event.current.type == EventType.Repaint)
            {
                h = Mathf.MoveTowards(h, hover || selected ? 1f : 0f, Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 9f);
                if (s_Hover.Count > 2048) s_Hover.Clear();
                s_Hover[id] = h;
            }
            Color fill = selected ? Selected : new Color(0.27f, 0.36f, 0.58f, 0.92f);
            if (intent.r > 0.65f && intent.r > intent.g * 1.3f) fill = Coral;
            fill = Color.Lerp(fill, selected ? new Color(0.19f, 0.43f, 0.56f, 0.96f) : new Color(0.33f, 0.46f, 0.72f, 0.96f), h * 0.7f);
            if (!enabled || !GUI.enabled) fill = new Color(0.18f, 0.22f, 0.32f, 0.6f);
            if (selected) Draw(s_Glow, Expand(r, 12 * Scale), new Color(Cyan.r, Cyan.g, Cyan.b, 0.5f));
            Draw(s_Fill, r, fill); Draw(s_Line, r, selected || hover ? Cyan : new Color(0.55f, 0.7f, 0.95f, 0.25f));
            if (selected) Line(new Rect(r.x + 3 * Scale, r.y + 8 * Scale, 4 * Scale, Mathf.Max(1, r.height - 16 * Scale)), Cyan);
            Text(r, text, style, !enabled ? Muted : White);
            var evt = Event.current;
            if (GUI.enabled && enabled && evt.type == EventType.MouseDown && evt.button == 0 && r.Contains(evt.mousePosition))
            { GUIUtility.hotControl = id; evt.Use(); }
            if (evt.type == EventType.MouseUp && evt.button == 0 && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                bool clicked = GUI.enabled && enabled && r.Contains(evt.mousePosition);
                evt.Use();
                return clicked;
            }
            return false;
        }
    }
}
