using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Navegação dos menus com o comando PlayStation: a cruzeta (ou o stick esquerdo) move o foco para o botão mais
    /// próximo nessa direção, o X carrega no botão com foco e o Círculo carrega no BACK / CLOSE / CANCEL do ecrã.
    ///
    /// Funciona em todos os menus porque os botões (MenuTheme.Button, MenuButton, ArcadeGUI.Hit e as áreas clicáveis
    /// dos cartões) chamam <see cref="Item"/>: no Repaint cada botão regista o seu retângulo (em coordenadas de ecrã) e,
    /// se tiver o foco, desenha um contorno. O "clique" do comando acontece no Repaint; a seguir o resto desse desenho é
    /// interrompido (GUIUtility.ExitGUI) para o frame seguinte já desenhar o ecrã novo sem erros de layout.
    /// Só está ativo com o cursor livre (menus); em jogo o cursor está preso e o comando controla o jogador.
    /// </summary>
    public static class PadNav
    {
        struct Entry { public Rect screen; public string text; }

        static readonly List<Entry> s_Cur = new List<Entry>(64);
        static readonly List<Entry> s_Prev = new List<Entry>(64);
        static Vector2 s_Focus;
        static bool s_HasFocus;
        static bool s_ActivatePending, s_BackPending, s_ExitPending;
        static int s_FireFrame = -1;
        static Vector2 s_StickDir;
        static float s_NextRepeat;
        static Texture2D s_White;

        /// <summary>O comando está a navegar os menus (comando ativo, cursor livre e há botões no ecrã).</summary>
        public static bool Active { get; private set; }

        static readonly string[] BackWords = { "BACK", "CLOSE", "CANCEL", "RESUME", "‹ BACK", "< BACK" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            s_Cur.Clear(); s_Prev.Clear();
            s_HasFocus = false; s_ActivatePending = s_BackPending = s_ExitPending = false; Active = false;
            var go = new GameObject("[PadNav]");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        class Runner : MonoBehaviour
        {
            void Update() => Tick();
        }

        static void Tick()
        {
            s_Prev.Clear();
            s_Prev.AddRange(s_Cur);
            s_Cur.Clear();
            s_ExitPending = false;

            Active = GamepadInput.Active && Cursor.lockState != CursorLockMode.Locked && s_Prev.Count > 0;
            if (!Active) { s_ActivatePending = s_BackPending = false; return; }

            // o foco segue o botão mais próximo (os menus animam e mudam de posição)
            int nearest = s_HasFocus ? Nearest(s_Focus) : -1;
            // ecrã novo (o botão com foco desapareceu): recomeça no primeiro botão com texto, em cima à esquerda
            if (nearest < 0 || (s_Prev[nearest].screen.center - s_Focus).sqrMagnitude > 40f * 40f) nearest = TopLeft();
            if (nearest >= 0) { s_Focus = s_Prev[nearest].screen.center; s_HasFocus = true; }

            var dir = ReadDirection();
            if (dir != Vector2.zero && s_HasFocus)
            {
                int next = InDirection(s_Focus, dir);
                if (next >= 0) { s_Focus = s_Prev[next].screen.center; Sfx.Play2D(Sound.UiClick, 0.15f); }
            }
            if (GamepadInput.RawPressed(PadButton.Cross)) s_ActivatePending = s_HasFocus;
            if (GamepadInput.RawPressed(PadButton.Circle)) s_BackPending = true;
        }

        static Vector2 ReadDirection()
        {
            if (GamepadInput.RawPressed(PadButton.DpadUp)) return Vector2.down;     // ecrã: y cresce para baixo
            if (GamepadInput.RawPressed(PadButton.DpadDown)) return Vector2.up;
            if (GamepadInput.RawPressed(PadButton.DpadLeft)) return Vector2.left;
            if (GamepadInput.RawPressed(PadButton.DpadRight)) return Vector2.right;
            // stick esquerdo com repetição
            var st = GamepadInput.LeftStick;
            Vector2 d = Vector2.zero;
            if (st.magnitude > 0.6f)
                d = Mathf.Abs(st.x) > Mathf.Abs(st.y) ? new Vector2(Mathf.Sign(st.x), 0f) : new Vector2(0f, -Mathf.Sign(st.y));
            if (d == Vector2.zero) { s_StickDir = Vector2.zero; return Vector2.zero; }
            if (d != s_StickDir) { s_StickDir = d; s_NextRepeat = Time.unscaledTime + 0.35f; return d; }
            if (Time.unscaledTime >= s_NextRepeat) { s_NextRepeat = Time.unscaledTime + 0.13f; return d; }
            return Vector2.zero;
        }

        static int Nearest(Vector2 p)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < s_Prev.Count; i++)
            {
                float d = (s_Prev[i].screen.center - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static int TopLeft()
        {
            int best = -1; float bs = float.MaxValue;
            for (int i = 0; i < s_Prev.Count; i++)
            {
                var c = s_Prev[i].screen.center;
                float sc = c.y * 2f + c.x + (string.IsNullOrEmpty(s_Prev[i].text) ? 100000f : 0f);
                if (sc < bs) { bs = sc; best = i; }
            }
            return best;
        }

        static int InDirection(Vector2 from, Vector2 dir)
        {
            int best = -1; float bs = float.MaxValue;
            var perp = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < s_Prev.Count; i++)
            {
                var d = s_Prev[i].screen.center - from;
                float along = Vector2.Dot(d, dir);
                if (along < 6f) continue;
                float side = Mathf.Abs(Vector2.Dot(d, perp));
                float sc = along + side * 2.5f;
                if (sc < bs) { bs = sc; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Chamado por cada botão depois de o desenhar (em qualquer evento). Devolve true quando o comando carrega nele.
        /// </summary>
        public static bool Item(Rect r, string text = null, bool enabled = true)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return false;
            // botão a seguir a um "clique" do comando neste desenho: interrompe (o ecrã já mudou)
            if (s_ExitPending && s_FireFrame == Time.frameCount) GUIUtility.ExitGUI();
            if (!enabled || r.width <= 1f || r.height <= 1f) return false;

            var p0 = GUIUtility.GUIToScreenPoint(new Vector2(r.xMin, r.yMin));
            var p1 = GUIUtility.GUIToScreenPoint(new Vector2(r.xMax, r.yMax));
            var screen = Rect.MinMaxRect(p0.x, p0.y, p1.x, p1.y);
            s_Cur.Add(new Entry { screen = screen, text = text });
            if (!Active) return false;

            bool focused = s_HasFocus && (screen.center - s_Focus).sqrMagnitude < 225f;
            if (focused) DrawFocus(r);

            bool fire = false;
            if (s_BackPending && text != null && IsBack(text)) { s_BackPending = false; fire = true; }
            else if (focused && s_ActivatePending) { s_ActivatePending = false; fire = true; }
            if (fire)
            {
                s_ExitPending = true;
                s_FireFrame = Time.frameCount;
                Sfx.Play2D(Sound.UiClick, 0.35f);
            }
            return fire;
        }

        /// <summary>Área clicável invisível (cartões, ícones): rato ou comando.</summary>
        public static bool Hit(Rect r, string text = null)
        {
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            return Item(r, text) || clicked;
        }

        static bool IsBack(string text)
        {
            var t = text.Trim().ToUpperInvariant();
            foreach (var w in BackWords) if (t == w) return true;
            return false;
        }

        static void DrawFocus(Rect r)
        {
            if (s_White == null) s_White = Texture2D.whiteTexture;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f);
            var c = new Color(0.45f, 0.98f, 0.95f, pulse);
            float t = Mathf.Max(2f, r.height * 0.05f);
            float g = t + 2f;
            var o = new Rect(r.x - g, r.y - g, r.width + g * 2f, r.height + g * 2f);
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(o.x, o.y, o.width, t), s_White);
            GUI.DrawTexture(new Rect(o.x, o.yMax - t, o.width, t), s_White);
            GUI.DrawTexture(new Rect(o.x, o.y, t, o.height), s_White);
            GUI.DrawTexture(new Rect(o.xMax - t, o.y, t, o.height), s_White);
            GUI.color = old;
        }
    }
}
