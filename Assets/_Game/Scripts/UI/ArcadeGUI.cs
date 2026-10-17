using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Primitivas de desenho IMGUI no estilo cartoon/arcade do jogo (painéis arredondados com contorno, texto com
    /// contorno grosso, botões, barras de progresso). Partilhado pelos ecrãs de progressão.
    /// </summary>
    public static class ArcadeGUI
    {
        public static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
        public static readonly Color PanelDark = new Color(0.07f, 0.08f, 0.16f, 0.95f);
        public static readonly Color PanelMid = new Color(0.12f, 0.14f, 0.25f, 0.95f);
        public static readonly Color PanelBlue = new Color(0.16f, 0.32f, 0.62f, 0.95f);
        public static readonly Color Ink = new Color(0.04f, 0.03f, 0.08f, 1f);
        public static readonly Color Good = new Color(0.4f, 0.9f, 0.45f);
        public static readonly Color Bad = new Color(0.9f, 0.35f, 0.3f);
        public static readonly Color Muted = new Color(0.75f, 0.8f, 0.9f);

        static Texture2D s_Rounded;
        static float s_StyleScale = -1f;

        public static GUIStyle Title, Big, Label, Small, Tiny, Center, SmallCenter, TinyCenter, BigCenter, Right, SmallRight, Wrap, Button, ButtonSmall, PanelStyle;

        public static float Scale => Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);
        public static Texture2D White => Texture2D.whiteTexture;

        public static void EnsureStyles()
        {
            float s = Scale;
            if (Title != null && Mathf.Approximately(s, s_StyleScale)) return;
            s_StyleScale = s;
            if (s_Rounded == null) s_Rounded = MakeRounded(32, 10);

            Title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(40 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(26 * s), fontStyle = FontStyle.Bold };
            Label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(17 * s), fontStyle = FontStyle.Bold };
            Small = new GUIStyle(Label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Normal };
            Tiny = new GUIStyle(Label) { fontSize = Mathf.RoundToInt(12 * s) };
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            SmallCenter = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter };
            TinyCenter = new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter };
            BigCenter = new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter };
            Right = new GUIStyle(Label) { alignment = TextAnchor.MiddleRight };
            SmallRight = new GUIStyle(Small) { alignment = TextAnchor.MiddleRight };
            Wrap = new GUIStyle(Small) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            foreach (var st in new[] { Title, Big, Label, Small, Tiny, Center, SmallCenter, TinyCenter, BigCenter, Right, SmallRight, Wrap })
                st.normal.textColor = Color.white;
            Title.normal.textColor = Gold;

            Button = MakeButton(17 * s);
            ButtonSmall = MakeButton(13 * s);
            PanelStyle = new GUIStyle { border = new RectOffset(10, 10, 10, 10) };
            PanelStyle.normal.background = s_Rounded;
        }

        static GUIStyle MakeButton(float size)
        {
            var b = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(size), fontStyle = FontStyle.Bold, border = new RectOffset(10, 10, 10, 10) };
            b.normal.background = s_Rounded;
            b.hover.background = s_Rounded;
            b.active.background = s_Rounded;
            b.normal.textColor = Color.white;
            b.hover.textColor = new Color(1f, 0.95f, 0.7f);
            b.active.textColor = Gold;
            return b;
        }

        static Texture2D MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ArcadeRounded" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    float shade = 0.86f + 0.14f * (y / (float)size);
                    px[y * size + x] = new Color(shade, shade, shade, a);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // ------------------------------------------------------------------ primitivas

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void Panel(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            PanelStyle.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        public static void InkPanel(Rect r, Color c, float border = 3f)
        {
            float b = border * Scale;
            Panel(new Rect(r.x - b, r.y - b, r.width + b * 2f, r.height + b * 2f), Ink);
            Panel(r, c);
        }

        public static void Tex(Rect r, Texture t, Color c)
        {
            if (t == null) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = old;
        }

        public static void Outlined(Rect r, string text, GUIStyle style, Color color, float thickness = 1.5f)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, color.a);
            float t = thickness * Scale;
            GUI.Label(new Rect(r.x - t, r.y - t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + t, r.y - t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x - t, r.y + t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + t, r.y + t, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        public static bool Btn(Rect r, string text, Color c, bool enabled = true, GUIStyle style = null)
        {
            var oldBg = GUI.backgroundColor;
            bool oldEnabled = GUI.enabled;
            GUI.backgroundColor = enabled ? c : Art.Darken(c, 0.45f);
            GUI.enabled = oldEnabled && enabled;
            bool clicked = GUI.Button(r, text, style ?? Button);
            GUI.enabled = oldEnabled;
            GUI.backgroundColor = oldBg;
            if (clicked) Sfx.Play2D(Sound.Reload, 0.35f);
            return clicked;
        }

        /// <summary>Área clicável invisível (cartões).</summary>
        public static bool Hit(Rect r) => GUI.Button(r, GUIContent.none, GUIStyle.none);

        /// <summary>Barra de progresso arredondada com texto.</summary>
        public static void Bar(Rect r, float t, Color fill, string text, GUIStyle style = null)
        {
            float s = Scale;
            InkPanel(r, new Color(0.1f, 0.11f, 0.2f, 0.95f), 2f);
            t = Mathf.Clamp01(t);
            if (t > 0f) Panel(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * t), r.height), fill);
            if (t > 0.02f) Panel(new Rect(r.x + 4 * s, r.y + 3 * s, Mathf.Max(0f, r.width * t - 8 * s), Mathf.Max(2f, r.height * 0.2f)), new Color(1f, 1f, 1f, 0.28f));
            if (!string.IsNullOrEmpty(text)) Outlined(r, text, style ?? TinyCenter, Color.white, 1f);
        }

        /// <summary>Medalha redonda com número (nível).</summary>
        public static void Badge(Rect r, string text, Color c)
        {
            Tex(new Rect(r.x - 2 * Scale, r.y - 2 * Scale, r.width + 4 * Scale, r.height + 4 * Scale), Art.CircleTexture, Ink);
            Tex(r, Art.CircleTexture, c);
            Outlined(r, text, r.height > 40 * Scale ? BigCenter : Center, Color.white, 1.5f);
        }

        public static string Duration(System.TimeSpan t)
        {
            if (t.TotalDays >= 1) return Mathf.FloorToInt((float)t.TotalDays) + "d " + t.Hours + "h";
            if (t.TotalHours >= 1) return t.Hours + "h " + t.Minutes.ToString("00") + "m";
            return t.Minutes + "m " + t.Seconds.ToString("00") + "s";
        }
    }
}
