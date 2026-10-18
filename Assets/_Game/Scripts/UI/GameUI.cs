using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// UI em IMGUI (zero assets), com visual arcade: painéis arredondados, texto com contorno, cartões de Brawler
    /// com pré-visualização 3D, HUD (vida, munição segmentada, botão de Super), números de dano, avisos,
    /// barras de vida sobre os inimigos, placar, kill feed e pausa com troca de personagem.
    /// </summary>
    public partial class GameUI : MonoBehaviour
    {
        static GameUI s_Instance;
        const int SettingsTabControls = 0;
        const int SettingsTabGameplay = 1;
        const int SettingsTabAudio = 2;
        const int SettingsTabProfile = 3;

        public static bool GameplayInputAllowed => s_Instance != null && s_Instance.m_InGame &&
            !s_Instance.m_Paused && !s_Instance.m_SettingsOpen;

        bool m_InGame;
        bool m_Paused;
        bool m_SettingsOpen;
        int m_SettingsTab;
        bool m_CapturingBinding;
        GameAction m_CapturingAction;
        int m_CaptureStartFrame;
        Vector2 m_SettingsScroll;
        string m_SettingsMessage;

        string m_Name;
        string m_Ip;
        int m_Hero;
        int m_MenuBots;
        static int m_PendingBots;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearPendingBots() => m_PendingBots = 0;
        List<string> m_LocalIps;

        float m_HitMarkerTimer;
        bool m_HitMarkerHead;
        bool m_HitMarkerKill;
        float m_DamageFlash;
        float m_LastHp = -1f;

        struct FeedEntry { public string killer, victim; public Color killerColor, victimColor; public float time; }
        struct DamageNumber { public Vector3 pos; public string text; public Color color; public float time; public float scale; public Vector2 drift; }
        struct ToastEntry { public string text; public Color color; public float time; }

        readonly List<FeedEntry> m_KillFeed = new List<FeedEntry>();
        readonly List<DamageNumber> m_DamageNumbers = new List<DamageNumber>();
        readonly List<ToastEntry> m_Toasts = new List<ToastEntry>();
        readonly Dictionary<ulong, bool> m_Visible = new Dictionary<ulong, bool>();
        float m_NextVisCheck;
        float m_LastKillTime = -10f;
        int m_Streak;
        MatchPhase m_LastPhase = MatchPhase.Playing;
        bool m_AnnouncedStart;

        GUIStyle m_Title, m_Label, m_Big, m_Small, m_Center, m_Button, m_Field, m_Tiny, m_TinyCenter, m_Panel, m_Card, m_Huge;
        GUIStyle m_RoleDescription, m_StatValue, m_Initial, m_CardName, m_CardRole;
        GUIStyle m_OverheadName, m_DamageNumber, m_Toast, m_SuperLabel, m_DeathOverlay, m_BigCenter, m_KillFeedStyle;
        Texture2D m_White, m_Rounded, m_Vignette, m_Circle, m_Ring;
        float m_StylesScale = -1f;

        BrawlerPreview m_Preview;

        static NetworkManager NM => NetworkManager.Singleton;

        // Paleta
        static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
        static readonly Color PanelDark = new Color(0.07f, 0.08f, 0.16f, 0.92f);
        static readonly Color PanelBlue = new Color(0.16f, 0.32f, 0.62f, 0.95f);
        static readonly Color Ink = new Color(0.04f, 0.03f, 0.08f, 1f);

        void Awake()
        {
            s_Instance = this;
            m_Name = GameSession.LocalName;
            m_Ip = GameSession.LastIp;
            m_Hero = BrawlerRegistry.Sanitize(GameSession.LocalHeroId);
            m_MenuBots = PlayerPrefs.GetInt("fpslan.bots", 0);
            MatchManager.MenuSelectedMode = (GameMode)Mathf.Clamp(PlayerPrefs.GetInt("fpslan.mode", 0), 0, System.Enum.GetValues(typeof(GameMode)).Length - 1);
            BotManager.Difficulty = (BotDifficulty)PlayerPrefs.GetInt("fpslan.botdiff", (int)BotDifficulty.Normal);
            m_LocalIps = ConnectionManager.GetLocalIPv4();
            m_Preview = new BrawlerPreview();
            GameSession.ApplyAudioSettings();
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            m_Preview?.Dispose();
        }

        // ================================================================== API estática

        public static void ShowHitMarker(bool headshot, bool killed)
        {
            if (s_Instance == null) return;
            s_Instance.m_HitMarkerTimer = killed ? 0.35f : 0.18f;
            s_Instance.m_HitMarkerHead = headshot;
            s_Instance.m_HitMarkerKill = killed;
        }

        public static void FlashDamage()
        {
            if (s_Instance != null) s_Instance.m_DamageFlash = 0.45f;
        }

        public static void AddKillFeed(string killer, string victim)
        {
            if (s_Instance == null) return;
            s_Instance.m_KillFeed.Add(new FeedEntry
            {
                killer = killer,
                victim = victim,
                killerColor = ColorForName(killer),
                victimColor = ColorForName(victim),
                time = Time.time,
            });
            if (s_Instance.m_KillFeed.Count > 5) s_Instance.m_KillFeed.RemoveAt(0);
        }

        /// <summary>Número de dano a flutuar sobre o alvo (só para quem atacou).</summary>
        public static void AddDamageNumber(Vector3 worldPos, int damage, bool headshot, bool killed)
        {
            if (s_Instance == null || damage <= 0) return;
            var list = s_Instance.m_DamageNumbers;
            // Junta acertos seguidos no mesmo alvo (rajadas) num só número a crescer
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var d = list[i];
                if (Time.time - d.time < 0.35f && (d.pos - worldPos).sqrMagnitude < 1.5f && !killed)
                {
                    int prev;
                    if (int.TryParse(d.text, out prev))
                    {
                        d.text = (prev + damage).ToString();
                        d.time = Time.time;
                        d.scale = Mathf.Min(1.6f, d.scale + 0.08f);
                        if (headshot) d.color = new Color(1f, 0.85f, 0.2f);
                        list[i] = d;
                        return;
                    }
                }
            }
            list.Add(new DamageNumber
            {
                pos = worldPos,
                text = damage.ToString(),
                color = killed ? new Color(1f, 0.3f, 0.25f) : (headshot ? new Color(1f, 0.85f, 0.2f) : Color.white),
                time = Time.time,
                scale = killed ? 1.5f : (headshot ? 1.25f : 1f),
                drift = new Vector2(Random.Range(-18f, 18f), 0f),
            });
            if (list.Count > 24) list.RemoveAt(0);
        }

        public static void Toast(string text, Color color)
        {
            if (s_Instance == null) return;
            s_Instance.m_Toasts.Add(new ToastEntry { text = text, color = color, time = Time.time });
            if (s_Instance.m_Toasts.Count > 3) s_Instance.m_Toasts.RemoveAt(0);
        }

        public static void AnnounceKill()
        {
            if (s_Instance == null) return;
            var ui = s_Instance;
            ui.m_Streak = Time.time - ui.m_LastKillTime < 4.5f ? ui.m_Streak + 1 : 1;
            ui.m_LastKillTime = Time.time;
            string text;
            switch (ui.m_Streak)
            {
                case 1: text = "ELIMINAÇÃO!"; break;
                case 2: text = "DUPLA!"; break;
                case 3: text = "TRIPLA!"; break;
                default: text = "IMPARÁVEL!"; break;
            }
            Toast(text, ui.m_Streak > 1 ? new Color(1f, 0.45f, 0.25f) : Gold);
        }

        static Color ColorForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Color.white;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.DisplayName == name)
                    return p.Brawler != null ? Color.Lerp(p.Brawler.Tint, Color.white, 0.25f) : Color.white;
            return Color.white;
        }

        // ================================================================== update

        void Update()
        {
            m_InGame = NM != null && NM.IsListening && PlayerNetwork.Local != null;
            MenuAudio.SetMenuMusic(!m_InGame);   // música do menu fora das partidas

            if (!m_InGame) m_Paused = false;
            if (InputReader.PausePressed && !m_CapturingBinding)
            {
                if (m_SettingsOpen)
                    CloseSettings();
                else
                    m_Paused = !m_Paused;
            }

            bool lockCursor = GameplayInputAllowed && Application.isFocused;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            // Bots pedidos no menu: adicionados assim que o host está pronto
            if (m_PendingBots > 0 && m_InGame && NM.IsServer)
            {
                // IT TAKES TWO: os inimigos (ondas) e o bot parceiro são geridos pelo próprio modo
                if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo) m_PendingBots = 0;
                else if (BotManager.AddBot()) m_PendingBots--;
                else m_PendingBots = 0;
            }

            float dt = Time.deltaTime;
            m_HitMarkerTimer -= dt;
            m_DamageFlash -= dt;
            m_KillFeed.RemoveAll(k => Time.time - k.time > 6f);
            m_DamageNumbers.RemoveAll(d => Time.time - d.time > 0.9f);
            m_Toasts.RemoveAll(t => Time.time - t.time > 1.6f);

            // Pré-visualização 3D: só no menu ou na pausa (troca de personagem)
            // (o menu novo usa retratos 2D; a pré-visualização 3D fica só para a pausa)
            bool showPreview = UseNewMenu ? (m_InGame && m_Paused) : (!m_InGame || m_Paused);
            if (showPreview)
            {
                int idx = m_InGame && PlayerNetwork.Local != null ? PlayerNetwork.Local.HeroId.Value : m_Hero;
                m_Preview.Show(idx);
                m_Preview.Tick(dt);
            }
            else m_Preview.Hide();

            if (m_InGame)
            {
                var me = PlayerNetwork.Local;
                if (me != null)
                {
                    float hp = me.Health.Health.Value;
                    if (m_LastHp >= 0f && hp < m_LastHp && !me.Health.IsDead) m_DamageFlash = Mathf.Max(m_DamageFlash, 0.35f);
                    m_LastHp = hp;
                }

                // Início/fim de partida
                var match = MatchManager.Instance;
                if (match != null && match.IsSpawned)
                {
                    var phase = match.Phase.Value;
                    if (phase == MatchPhase.Playing && (m_LastPhase == MatchPhase.Ended || !m_AnnouncedStart))
                    {
                        m_AnnouncedStart = true;
                        Toast("LUTEM!", new Color(0.4f, 1f, 0.5f));
                    }
                    m_LastPhase = phase;
                }

                if (Time.time >= m_NextVisCheck)
                {
                    m_NextVisCheck = Time.time + 0.15f;
                    m_Visible.Clear();
                    foreach (var p in PlayerNetwork.All)
                    {
                        if (p == null || p == me || !p.IsSpawned || p.Health.IsDead) continue;
                        m_Visible[p.Id] = me != null && CombatMath.HasLineOfSight(me, p);
                    }
                }
            }
            else
            {
                m_AnnouncedStart = false;
                m_LastHp = -1f;
            }
        }

        // ================================================================== estilos

        float Scale => Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);

        void EnsureStyles()
        {
            float s = Scale;
            if (m_Title != null && Mathf.Approximately(s, m_StylesScale)) return;
            m_StylesScale = s;

            m_White = Texture2D.whiteTexture;
            if (m_Rounded == null) m_Rounded = MakeRounded(32, 10);
            if (m_Vignette == null) m_Vignette = MakeVignette(128);
            if (m_Circle == null) m_Circle = Art.CircleTexture;
            if (m_Ring == null) m_Ring = Art.RingTexture;

            m_Title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(58 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            m_Title.normal.textColor = Gold;
            m_Huge = new GUIStyle(m_Title) { fontSize = Mathf.RoundToInt(46 * s) };
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold };
            m_Label.normal.textColor = Color.white;
            m_Big = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(30 * s) };
            m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Normal };
            m_Tiny = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(12 * s) };
            m_TinyCenter = new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleCenter };
            m_Center = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
            m_RoleDescription = new GUIStyle(m_Small) { wordWrap = true, alignment = TextAnchor.UpperCenter };
            m_StatValue = new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleRight };
            m_Initial = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter };
            m_CardName = new GUIStyle(m_Label) { alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
            m_CardRole = new GUIStyle(m_Tiny) { alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip, wordWrap = false };
            m_OverheadName = new GUIStyle(m_TinyCenter);
            m_DamageNumber = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter };
            m_Toast = new GUIStyle(m_Huge);
            m_SuperLabel = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter };
            m_DeathOverlay = new GUIStyle(m_Huge);
            m_BigCenter = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter };
            m_KillFeedStyle = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft };

            m_Button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold, border = new RectOffset(10, 10, 10, 10) };
            m_Button.normal.background = m_Rounded;
            m_Button.hover.background = m_Rounded;
            m_Button.active.background = m_Rounded;
            m_Button.onNormal.background = m_Rounded;
            m_Button.onHover.background = m_Rounded;
            m_Button.onActive.background = m_Rounded;
            m_Button.normal.textColor = Color.white;
            m_Button.hover.textColor = new Color(1f, 0.95f, 0.7f);
            m_Button.active.textColor = Gold;
            m_Button.onNormal.textColor = Ink;
            m_Button.onHover.textColor = Ink;

            m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(20 * s), alignment = TextAnchor.MiddleLeft, border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(12, 12, 4, 4) };
            m_Field.normal.background = m_Rounded;
            m_Field.focused.background = m_Rounded;
            m_Field.hover.background = m_Rounded;
            m_Field.normal.textColor = Ink;
            m_Field.focused.textColor = Ink;
            m_Field.hover.textColor = Ink;

            m_Panel = new GUIStyle { border = new RectOffset(10, 10, 10, 10) };
            m_Panel.normal.background = m_Rounded;
            m_Card = new GUIStyle(m_Panel);
        }

        static Texture2D MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIRounded" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    // Ligeiro gradiente vertical (mais claro em cima) para dar volume
                    float shade = 0.86f + 0.14f * (y / (float)size);
                    px[y * size + x] = new Color(shade, shade, shade, a);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static Texture2D MakeVignette(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UIVignette" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v) / 1.41f;
                    float a = Mathf.SmoothStep(0.45f, 1f, d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        // ================================================================== primitivas de desenho

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, m_White);
            GUI.color = old;
        }

        void Panel(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            m_Panel.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>Painel com contorno escuro (estilo cartoon).</summary>
        void InkPanel(Rect r, Color c, float border = 3f)
        {
            Panel(new Rect(r.x - border, r.y - border, r.width + border * 2f, r.height + border * 2f), Ink);
            Panel(r, c);
        }

        void Tex(Rect r, Texture t, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = old;
        }

        /// <summary>Texto com contorno preto grosso.</summary>
        void Outlined(Rect r, string text, GUIStyle style, Color color, float thickness = 2f)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, color.a);
            float t = thickness * Scale;
            GUI.Label(new Rect(r.x - t, r.y - t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + t, r.y - t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x - t, r.y + t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + t, r.y + t, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + t * 1.5f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        bool Button(Rect r, string text, Color c)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            bool clicked = GUI.Button(r, text, m_Button);
            GUI.backgroundColor = old;
            return clicked;
        }

        bool LayoutButton(string text, Color c, params GUILayoutOption[] opts)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = c;
            bool clicked = GUILayout.Button(text, m_Button, opts);
            GUI.backgroundColor = old;
            return clicked;
        }

        // ================================================================== OnGUI

        void OnGUI()
        {
            EnsureStyles();

            if (!m_InGame)
            {
                if (UseNewMenu) { DrawNewMainMenu(); return; }   // GameUI.MainMenu.cs
                if (m_SettingsOpen) DrawSettingsMenu();
                else DrawMainMenu();
                return;
            }

            DrawVignettes();
            DrawOverheadBars();
            DrawDamageNumbers();
            DrawHud();
            DrawToasts();

            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned && match.Phase.Value == MatchPhase.Ended)
                DrawMatchResults(match);
            else if (InputReader.ScoreboardHeld)
                DrawScoreboard();

            if (m_Paused) DrawPauseMenu();
            if (m_SettingsOpen) DrawSettingsMenu();
            if (m_InGame && !m_Paused && !m_SettingsOpen && IsScopeActive())
                DrawScopeOverlay();
        }

        static bool IsScopeActive()
        {
            var player = PlayerNetwork.Local;
            return player != null && player.Brawler != null &&
                player.Brawler.firstPerson.rig == RigStyle.Sniper && InputReader.AimHeld;
        }

        void DrawScopeOverlay()
        {
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            Tex(screen, m_Vignette, new Color(0f, 0f, 0f, 0.78f));
            float radius = Mathf.Min(Screen.height * 0.34f, Screen.width * 0.24f);
            var scope = new Rect(Screen.width * 0.5f - radius, Screen.height * 0.5f - radius,
                radius * 2f, radius * 2f);
            Tex(scope, m_Ring, new Color(0.42f, 0.94f, 0.86f, 0.88f));

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float line = Mathf.Max(1.5f, Scale * 2f);
            var color = new Color(0.9f, 1f, 0.9f, 0.86f);
            Fill(new Rect(cx - radius * 0.72f, cy - line * 0.5f, radius * 0.62f, line), color);
            Fill(new Rect(cx + radius * 0.1f, cy - line * 0.5f, radius * 0.62f, line), color);
            Fill(new Rect(cx - line * 0.5f, cy - radius * 0.72f, line, radius * 0.62f), color);
            Fill(new Rect(cx - line * 0.5f, cy + radius * 0.1f, line, radius * 0.62f), color);
            Fill(new Rect(cx - line, cy - line, line * 2f, line * 2f), color);
        }

        // ================================================================== menu principal

        void DrawMainMenu()
        {
            float s = Scale;
            // Faixas de fundo
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.08f, 0.2f, 0.45f));
            Tex(new Rect(0, 0, Screen.width, Screen.height), m_Vignette, new Color(0.02f, 0.02f, 0.08f, 0.8f));

            float w = 600 * s, h = Mathf.Min(Screen.height - 40 * s, 760 * s);
            var area = new Rect(40 * s, (Screen.height - h) / 2f, w, h);
            InkPanel(area, PanelDark, 4f * s);

            GUILayout.BeginArea(new Rect(area.x + 24 * s, area.y + 12 * s, area.width - 48 * s, area.height - 24 * s));
            var titleRect = GUILayoutUtility.GetRect(10, 70 * s);
            Outlined(titleRect, "FPS-LAN", m_Title, Gold, 3f);
            var subRect = GUILayoutUtility.GetRect(10, 26 * s);
            Outlined(subRect, "ARENA DE COMBATE  ·  MULTIPLAYER LAN", m_Center, new Color(0.7f, 0.9f, 1f));
            if (LayoutButton("DEFINIÇÕES  ·  TECLAS", new Color(0.28f, 0.36f, 0.56f), GUILayout.Height(38 * s)))
                OpenSettings();
            GUILayout.Space(8 * s);

            GUILayout.Label("PERFIL DO JOGADOR", m_Small);
            m_Name = GUILayout.TextField(m_Name ?? "", 16, m_Field, GUILayout.Height(38 * s));

            GUILayout.Space(8 * s);
            GUILayout.Label("ESCOLHE O TEU BRAWLER", m_Small);
            DrawBrawlerCards(s, ref m_Hero, false, area.width - 48 * s);

            GUILayout.Space(8 * s);
            GUILayout.Label("Modo de jogo", m_Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("‹", m_Button, GUILayout.Width(48 * s), GUILayout.Height(38 * s))) CycleGameMode(-1);
            GUILayout.Label(GameModeLabel(MatchManager.MenuSelectedMode), m_Big, GUILayout.Height(38 * s));
            if (GUILayout.Button("›", m_Button, GUILayout.Width(48 * s), GUILayout.Height(38 * s))) CycleGameMode(1);
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * s);
            bool busy = NM != null && NM.IsListening;
            GUI.enabled = !busy;

            DrawBotControls(s, false);
            GUILayout.Space(8 * s);

            GUILayout.Label("CRIAR UMA SALA", m_Small);
            GUILayout.BeginHorizontal();
            if (LayoutButton("HOST", new Color(0.3f, 0.75f, 1f), GUILayout.Height(52 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = m_MenuBots;
            }
            if (LayoutButton("TREINO (com bots)", new Color(1f, 0.75f, 0.2f), GUILayout.Height(52 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = Mathf.Max(3, m_MenuBots);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * s);
            GUILayout.Label("ENTRAR NUMA SALA", m_Small);
            GUILayout.Label("IP do anfitrião · UDP " + ConnectionManager.DefaultPort, m_Tiny);
            GUILayout.BeginHorizontal();
            m_Ip = GUILayout.TextField(m_Ip ?? "", 40, m_Field, GUILayout.Height(42 * s));
            if (LayoutButton("JOIN", new Color(0.45f, 0.9f, 0.45f), GUILayout.Width(130 * s), GUILayout.Height(42 * s)))
            {
                SaveMenuPrefs();
                ConnectionManager.Instance?.Join(m_Ip);
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (busy && !m_InGame)
            {
                GUILayout.Space(6 * s);
                if (LayoutButton("Cancelar", new Color(0.9f, 0.35f, 0.3f), GUILayout.Height(36 * s)))
                    ConnectionManager.Instance?.Leave();
            }

            var status = ConnectionManager.Instance != null ? ConnectionManager.Instance.Status : "";
            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(6 * s);
                GUILayout.Label(status, m_Label);
            }

            GUILayout.FlexibleSpace();
            var ips = m_LocalIps != null && m_LocalIps.Count > 0 ? string.Join("   ", m_LocalIps) : "desconhecido";
            GUILayout.Label("O teu IP na rede: " + ips + "   ·   UDP " + ConnectionManager.DefaultPort, m_Small);
            GUILayout.EndArea();

            DrawBrawlerShowcase(m_Hero, new Rect(area.xMax + 30 * s, area.y, Screen.width - area.xMax - 70 * s, h));
        }

        /// <summary>Lado direito do menu: modelo 3D a rodar + nome, papel e atributos.</summary>
        void DrawBrawlerShowcase(int index, Rect r)
        {
            if (r.width < 220 * Scale) return;
            float s = Scale;
            var b = BrawlerRegistry.Get(index);
            if (b == null) return;

            float pw = Mathf.Min(r.width, r.height * 0.8f);
            var podium = new Rect(r.x + (r.width - pw) / 2f, r.y, pw, pw * 1.25f);
            Tex(new Rect(podium.x - pw * 0.1f, podium.y + pw * 0.1f, pw * 1.2f, pw * 1.1f), m_Circle, Art.WithAlpha(b.Tint, 0.18f));
            if (m_Preview.Texture != null) GUI.DrawTexture(podium, m_Preview.Texture, ScaleMode.ScaleToFit, true);

            float y = Mathf.Min(podium.yMax - 30 * s, r.yMax - 210 * s);
            var nameRect = new Rect(r.x, y, r.width, 60 * s);
            Outlined(nameRect, b.displayName.ToUpperInvariant(), m_Huge, Color.Lerp(b.Tint, Color.white, 0.3f), 3f);
            y += 56 * s;
            Outlined(new Rect(r.x, y, r.width, 26 * s), RoleTitle(b), m_Center, Color.white);
            y += 34 * s;

            float bw = Mathf.Min(380 * s, r.width - 20 * s);
            float bx = r.x + (r.width - bw) / 2f;
            StatBar(new Rect(bx, y, bw, 22 * s), "VIDA", b.stats.maxHealth / 150f, b.stats.maxHealth.ToString(), new Color(0.4f, 0.95f, 0.45f));
            y += 28 * s;
            float range = b.primary.attack != null ? b.primary.attack.Range : 0f;
            StatBar(new Rect(bx, y, bw, 22 * s), "ALCANCE", range / 45f, RangeLabel(range), new Color(0.35f, 0.75f, 1f));
            y += 28 * s;
            float dps = EstimateBurst(b);
            StatBar(new Rect(bx, y, bw, 22 * s), "DANO", dps / 140f, Mathf.RoundToInt(dps).ToString(), new Color(1f, 0.5f, 0.3f));
            y += 34 * s;
            var desc = b.role ?? "";
            int dash = desc.IndexOf('—');
            if (dash >= 0) desc = desc.Substring(dash + 1).Trim();
            Outlined(new Rect(bx, y, bw, 60 * s), desc, m_RoleDescription, new Color(0.9f, 0.92f, 1f), 1.2f);
        }

        static string RoleTitle(BrawlerDefinition b)
        {
            var role = b.role ?? "";
            int dash = role.IndexOf('—');
            return dash > 0 ? role.Substring(0, dash).Trim().ToUpperInvariant() : role.ToUpperInvariant();
        }

        static string RangeLabel(float r) => r >= 30f ? "Longo" : (r >= 16f ? "Médio" : "Curto");

        static float EstimateBurst(BrawlerDefinition b)
        {
            var a = b.primary.attack;
            if (a == null || a.delivery == null) return 0f;
            var e = a.effect;
            int shots = a.delivery.ShotCount;
            float direct = e.damage * Mathf.Max(e.nearMultiplier, e.farMultiplier);
            if (a.delivery is ArcProjectileDelivery) return e.splashDamage * shots;
            if (a.delivery is ConeVolumeDelivery cone) return direct * Mathf.Max(1f, cone.duration / Mathf.Max(0.05f, cone.tickInterval));
            return direct * shots + e.splashDamage * shots * 0.3f;
        }

        void StatBar(Rect r, string label, float t, string value, Color c)
        {
            float s = Scale;
            InkPanel(r, new Color(0.12f, 0.13f, 0.22f, 0.95f), 2f * s);
            var fill = new Rect(r.x, r.y, Mathf.Max(r.height, r.width * Mathf.Clamp01(t)), r.height);
            Panel(fill, c);
            Outlined(new Rect(r.x + 10 * s, r.y - 1 * s, r.width, r.height), label, m_Tiny, Color.white, 1.5f);
            Outlined(new Rect(r.x, r.y, r.width - 10 * s, r.height), value, m_StatValue, Color.white, 1.5f);
        }

        /// <summary>Cartões dos Brawlers (2 linhas). Clicar escolhe; em jogo troca já.</summary>
        void DrawBrawlerCards(float s, ref int selected, bool applyNow, float width)
        {
            var all = BrawlerRegistry.All;
            int count = 0;
            foreach (var b in all) if (b != null) count++;
            if (count == 0) return;
            float gap = 8 * s;
            int preferredColumns = count <= 4 ? count : Mathf.CeilToInt(count / 2f);
            int columnsThatFit = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (132f * s + gap)));
            int perRow = Mathf.Min(preferredColumns, columnsThatFit);
            int rows = Mathf.CeilToInt(count / (float)perRow);
            float cw = (width - gap * (perRow - 1)) / perRow;
            float ch = 74 * s;
            var block = GUILayoutUtility.GetRect(width, rows * ch + (rows - 1) * gap);

            int i = 0;
            foreach (var b in all)
            {
                if (b == null) continue;
                int idx = b.runtimeIndex;
                int row = i / perRow, col = i % perRow;
                var r = new Rect(block.x + col * (cw + gap), block.y + row * (ch + gap), cw, ch);
                bool isSel = selected == idx;

                var bg = isSel ? Color.Lerp(b.Tint, Color.white, 0.15f) : Art.Darken(b.Tint, 0.5f);
                bg.a = b.available ? 1f : 0.35f;
                InkPanel(r, bg, (isSel ? 4f : 2f) * s);
                if (isSel) Panel(new Rect(r.x + 4 * s, r.y + 4 * s, r.width - 8 * s, 6 * s), new Color(1f, 1f, 1f, 0.5f));

                // Ícone: círculo com a inicial
                float ic = ch - 22 * s;
                var icon = new Rect(r.x + 8 * s, r.y + 11 * s, ic, ic);
                Tex(new Rect(icon.x - 2 * s, icon.y - 2 * s, icon.width + 4 * s, icon.height + 4 * s), m_Circle, Ink);
                Tex(icon, m_Circle, Color.Lerp(b.Tint, Color.white, 0.55f));
                Outlined(icon, FirstCharacterOrFallback(b.displayName).ToUpperInvariant(), m_Initial, Color.white, 1.5f);

                Outlined(new Rect(icon.xMax + 8 * s, r.y + 10 * s, r.width - ic - 22 * s, 24 * s), b.displayName, m_CardName, Color.white, 1.5f);
                Outlined(new Rect(icon.xMax + 8 * s, r.y + 36 * s, r.width - ic - 22 * s, 20 * s), ShortRole(b), m_CardRole, new Color(1f, 1f, 1f, 0.9f), 1f);

                if (b.available && GUI.Button(r, GUIContent.none, GUIStyle.none) && !isSel)
                {
                    selected = idx;
                    GameSession.LocalHeroId = idx;
                    if (applyNow && PlayerNetwork.Local != null) PlayerNetwork.Local.ChangeHero(idx);
                    MenuAudio.PlayVoiceForBrawler(b.brawlerId);
                }
                i++;
            }
        }

        static string ShortRole(BrawlerDefinition b)
        {
            var role = b.role ?? "";
            int dash = role.IndexOf('—');
            return dash > 0 ? role.Substring(0, dash).Trim() : role;
        }

        static string FirstCharacterOrFallback(string value) =>
            string.IsNullOrEmpty(value) ? "?" : value.Substring(0, 1);

        void DrawBotControls(float s, bool live)
        {
            GUILayout.BeginHorizontal();
            int count = live ? BotManager.BotCount : m_MenuBots;
            GUILayout.Label("Bots: " + count, m_Label, GUILayout.Width(100 * s), GUILayout.Height(36 * s));
            if (LayoutButton("−", new Color(0.85f, 0.4f, 0.35f), GUILayout.Width(46 * s), GUILayout.Height(36 * s)))
            {
                if (live) BotManager.RemoveBot();
                else m_MenuBots = Mathf.Max(0, m_MenuBots - 1);
            }
            if (LayoutButton("+", new Color(0.4f, 0.85f, 0.45f), GUILayout.Width(46 * s), GUILayout.Height(36 * s)))
            {
                if (live) BotManager.AddBot();
                else m_MenuBots = Mathf.Min(BotManager.MaxTotalPlayers - 1, m_MenuBots + 1);
            }
            GUILayout.Space(10 * s);
            if (LayoutButton("Dificuldade: " + BotManager.DifficultyLabel(BotManager.Difficulty), new Color(0.55f, 0.5f, 0.95f), GUILayout.Height(36 * s)))
            {
                BotManager.Difficulty = (BotDifficulty)(((int)BotManager.Difficulty + 1) % 3);
                PlayerPrefs.SetInt("fpslan.botdiff", (int)BotManager.Difficulty);
                BotManager.ApplyDifficulty();
            }
            GUILayout.EndHorizontal();
        }

        void CycleGameMode(int delta)
        {
            int count = System.Enum.GetValues(typeof(GameMode)).Length;
            int value = ((int)MatchManager.MenuSelectedMode + delta + count) % count;
            MatchManager.MenuSelectedMode = (GameMode)value;
            PlayerPrefs.SetInt("fpslan.mode", value);
        }

        static string GameModeLabel(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Wipeout: return "WIPEOUT · Eliminações por equipa";
                case GameMode.Knockout: return "KNOCKOUT · Melhor de três rondas";
                case GameMode.Showdown: return "SHOWDOWN · Último Brawler vivo";
                case GameMode.ItTakesTwo: return "IT TAKES TWO · Cooperativo a dois";
                case GameMode.GemGrab: return "PIQUE-GEMAS · Segura 10 gemas com a equipa";
                default: return "DEATHMATCH · Todos contra todos";
            }
        }

        void SaveMenuPrefs()
        {
            GameSession.LocalName = m_Name;
            m_Name = GameSession.LocalName;
            GameSession.LocalHeroId = m_Hero;
            GameSession.LastIp = (m_Ip ?? "").Trim();
            PlayerPrefs.SetInt("fpslan.bots", m_MenuBots);
            PlayerPrefs.Save();
        }

        // ================================================================== em jogo

        void DrawVignettes()
        {
            var me = PlayerNetwork.Local;
            if (me == null) return;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            if (m_DamageFlash > 0f)
                Tex(full, m_Vignette, new Color(0.9f, 0.05f, 0.05f, Mathf.Clamp01(m_DamageFlash * 2f)));
            if (!me.Health.IsDead)
            {
                float hp01 = me.Health.Health.Value / (float)Mathf.Max(1, me.Health.MaxHealth);
                if (hp01 < 0.35f)
                    Tex(full, m_Vignette, new Color(0.8f, 0f, 0f, (0.35f - hp01) * 1.6f * (0.7f + 0.3f * Mathf.Sin(Time.time * 6f))));
                if (me.Health.IsSlowed)
                    Tex(full, m_Vignette, new Color(0.55f, 0.25f, 0.95f, 0.45f));
                if (me.Visuals != null && me.Visuals.InBush)
                    Tex(full, m_Vignette, new Color(0.15f, 0.55f, 0.2f, 0.55f));
            }
        }

        /// <summary>Nome + barra de vida por cima dos outros jogadores visíveis.</summary>
        void DrawOverheadBars()
        {
            var me = PlayerNetwork.Local;
            var cam = me != null && me.Visuals != null ? me.Visuals.PlayerCamera : null;
            if (cam == null) return;
            float s = Scale;

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == me || !p.IsSpawned || p.Health.IsDead) continue;
                if (!m_Visible.TryGetValue(p.Id, out bool vis) || !vis) continue;
                if (p.Visuals != null && p.Visuals.IsConcealedForLocal) continue;

                var head = p.Visuals != null ? p.Visuals.HeadPosition : p.transform.position + Vector3.up * 1.6f;
                var world = head + Vector3.up * 0.95f;
                var sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f || sp.z > 50f) continue;

                float k = Mathf.Clamp(1.45f - sp.z / 35f, 0.55f, 1.2f) * s;
                float bw = 96f * k, bh = 13f * k;
                float x = sp.x - bw / 2f, y = Screen.height - sp.y;

                float hp01 = Mathf.Clamp01(p.Health.Health.Value / (float)Mathf.Max(1, p.Health.MaxHealth));
                var barRect = new Rect(x, y, bw, bh);
                bool mate = MatchManager.AreTeammates(me, p);
                InkPanel(barRect, mate ? new Color(0.05f, 0.18f, 0.08f, 0.95f) : new Color(0.2f, 0.05f, 0.08f, 0.95f), 2f * k);
                if (hp01 > 0f) Panel(new Rect(x, y, Mathf.Max(bh, bw * hp01), bh), mate
                    ? Color.Lerp(new Color(0.25f, 0.75f, 0.3f), new Color(0.4f, 0.95f, 0.45f), hp01)
                    : Color.Lerp(new Color(0.95f, 0.15f, 0.2f), new Color(1f, 0.35f, 0.3f), hp01));
                if (p.Health.IsSlowed) Fill(new Rect(x + 3, y + bh - 3 * k, bw - 6, 2 * k), new Color(0.75f, 0.5f, 1f));
                Outlined(new Rect(x, y - 1, bw, bh + 2), p.Health.Health.Value.ToString(), m_TinyCenter, Color.white, 1f);

                var label = p.DisplayName;
                m_OverheadName.fontSize = Mathf.RoundToInt(13 * k);
                Outlined(new Rect(sp.x - 130, y - 20 * k, 260, 18 * k), label, m_OverheadName, Color.Lerp(p.Brawler.Tint, Color.white, 0.4f), 1.2f);

                if (p.Combat.SuperReady)
                {
                    float d = 12 * k;
                    Tex(new Rect(x - d - 4 * k, y + bh / 2f - d / 2f, d, d), m_Circle, new Color(1f, 0.85f, 0.2f, 0.7f + 0.3f * Mathf.Sin(Time.time * 8f)));
                }
                if (p.Health.IsProtected)
                {
                    Outlined(new Rect(sp.x - 60, y + bh + 2 * k, 120, 16 * k), "ESCUDO", m_OverheadName, new Color(0.5f, 0.85f, 1f), 1f);
                }
            }
        }

        void DrawDamageNumbers()
        {
            var me = PlayerNetwork.Local;
            var cam = me != null && me.Visuals != null ? me.Visuals.PlayerCamera : null;
            if (cam == null) return;
            float s = Scale;
            foreach (var d in m_DamageNumbers)
            {
                float age = Time.time - d.time;
                var sp = cam.WorldToScreenPoint(d.pos);
                if (sp.z <= 0f) continue;
                float pop = age < 0.08f ? Mathf.Lerp(1.6f, 1f, age / 0.08f) : 1f;
                float size = Mathf.Clamp(26f * d.scale * pop * Mathf.Clamp(14f / Mathf.Max(4f, sp.z), 0.65f, 1.3f), 14f, 60f) * s;
                m_DamageNumber.fontSize = Mathf.RoundToInt(size);
                float rise = age * 60f * s;
                var c = d.color;
                c.a = Mathf.Clamp01(1.4f - age * 1.6f);
                var r = new Rect(sp.x - 100 + d.drift.x * age, Screen.height - sp.y - rise - 20, 200, 40 * s);
                Outlined(r, d.text, m_DamageNumber, c, 2f);
            }
        }

        void DrawToasts()
        {
            float s = Scale;
            float y = Screen.height * 0.24f;
            foreach (var t in m_Toasts)
            {
                float age = Time.time - t.time;
                float pop = age < 0.12f ? Mathf.Lerp(1.8f, 1f, age / 0.12f) : 1f;
                var c = t.color;
                c.a = Mathf.Clamp01((1.6f - age) * 2.5f);
                m_Toast.fontSize = Mathf.RoundToInt(44 * s * pop);
                Outlined(new Rect(0, y, Screen.width, 60 * s * pop), t.text, m_Toast, c, 3f);
                y += 58 * s;
            }
        }

        void DrawHud()
        {
            var me = PlayerNetwork.Local;
            if (me == null) return;
            float s = Scale;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            var b = me.Brawler;

            if (me.Health.IsDead)
            {
                DrawDeathOverlay(me);
            }
            else
            {
                DrawCrosshair(me, cx, cy, s);
            }

            // ---------------- vida (canto inferior esquerdo)
            float bw = 340 * s, bh = 34 * s;
            var hpRect = new Rect(30 * s, Screen.height - 64 * s, bw, bh);
            InkPanel(hpRect, new Color(0.12f, 0.13f, 0.22f, 0.95f), 3f * s);
            float hp01 = Mathf.Clamp01(me.Health.Health.Value / (float)Mathf.Max(1, me.Health.MaxHealth));
            var hpColor = Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(0.35f, 0.92f, 0.4f), hp01);
            if (hp01 > 0f) Panel(new Rect(hpRect.x, hpRect.y, Mathf.Max(bh, hpRect.width * hp01), hpRect.height), hpColor);
            Panel(new Rect(hpRect.x + 6 * s, hpRect.y + 4 * s, Mathf.Max(0f, hpRect.width * hp01 - 12 * s), 6 * s), new Color(1f, 1f, 1f, 0.3f));
            Outlined(new Rect(hpRect.x + 12 * s, hpRect.y, bw, bh), me.Health.Health.Value + " / " + me.Health.MaxHealth, m_Label, Color.white);

            // Retrato
            float pr = 64 * s;
            var portrait = new Rect(hpRect.x, hpRect.y - pr - 10 * s, pr, pr);
            Tex(new Rect(portrait.x - 3 * s, portrait.y - 3 * s, pr + 6 * s, pr + 6 * s), m_Circle, Ink);
            Tex(portrait, m_Circle, b != null ? b.Tint : Color.white);
            Outlined(portrait, b != null ? FirstCharacterOrFallback(b.displayName) : "?", m_Initial, Color.white, 2f);
            Outlined(new Rect(portrait.xMax + 10 * s, portrait.y + 8 * s, 400 * s, 30 * s), b != null ? b.displayName.ToUpperInvariant() : "", m_Big, Color.white, 2f);
            Outlined(new Rect(portrait.xMax + 12 * s, portrait.y + 40 * s, 400 * s, 22 * s), b != null ? ShortRole(b) : "", m_Small, new Color(0.85f, 0.9f, 1f), 1.2f);

            // ---------------- botão de Super (canto inferior direito)
            DrawSuperButton(me, s);

            // ---------------- topo: tempo + K/D + líder
            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned)
            {
                float tr = match.TimeRemaining;
                string time = match.Phase.Value == MatchPhase.Playing ? $"{(int)(tr / 60)}:{(int)(tr % 60):00}" : "FIM";
                if (match.IsCoop && match.Phase.Value == MatchPhase.Playing)
                    time = match.IsRoundIntermission.Value ? $"0:{Mathf.CeilToInt(tr):00}" : "";
                var top = new Rect(cx - 170 * s, 12 * s, 340 * s, 52 * s);
                InkPanel(top, PanelBlue, 3f * s);
                string modeName = match.IsCoop ? $"ONDA {Mathf.Max(1, match.CoopWave.Value)}/{match.coopTotalWaves}" :
                match.Mode.Value == GameMode.GemGrab ? "PIQUE-GEMAS" :
                    match.Mode.Value == GameMode.Wipeout ? "WIPEOUT" :
                    match.Mode.Value == GameMode.Knockout ? $"KNOCKOUT · RONDA {match.RoundNumber.Value}" :
                    match.Mode.Value == GameMode.Showdown ? "SHOWDOWN" : "DEATHMATCH";
                string modeScore = match.IsCoop
                    ? $"IT TAKES TWO  ·  INIMIGOS {match.CoopEnemiesLeft.Value}  ·  K {me.Kills.Value}"
                    : match.Mode.Value == GameMode.Deathmatch
                    ? $"K {me.Kills.Value}  ·  D {me.Deaths.Value}  ·  meta {match.killLimit}"
                    : match.Mode.Value == GameMode.Showdown
                        ? $"VIVOS {match.AlivePlayerCount}  ·  K {me.Kills.Value}"
                        : $"AZUL {match.BlueScore.Value}  -  {match.RedScore.Value} VERMELHA";
                Outlined(new Rect(top.x, top.y + 2 * s, top.width, 25 * s), $"{modeName}   {time}", m_BigCenter, Color.white, 2f);
                Outlined(new Rect(top.x, top.y + 27 * s, top.width, 22 * s), modeScore, m_TinyCenter, new Color(1f, 0.9f, 0.6f), 1f);

                if (match.IsCoop) DrawCoopHud(me, match, top, s);

                if (match.Mode.Value == GameMode.Deathmatch)
                {
                    PlayerNetwork leader = null;
                    foreach (var p in PlayerNetwork.All)
                        if (p != null && p.IsSpawned && (leader == null || p.Kills.Value > leader.Kills.Value)) leader = p;
                    if (leader != null && leader.Kills.Value > 0)
                    {
                        var lr = new Rect(cx - 160 * s, top.yMax + 8 * s, 320 * s, 22 * s);
                        Outlined(lr, "LÍDER: " + leader.DisplayName + " — " + leader.Kills.Value, m_TinyCenter, Gold, 1.2f);
                    }
                }
            }

            // ---------------- kill feed
            float ky = 14 * s;
            foreach (var k in m_KillFeed)
            {
                var r = new Rect(Screen.width - 420 * s, ky, 400 * s, 30 * s);
                float age = Time.time - k.time;
                float a = Mathf.Clamp01((6f - age) * 2f);
                Panel(r, new Color(0.05f, 0.05f, 0.12f, 0.7f * a));
                float x = r.x + 10 * s;
                if (!string.IsNullOrEmpty(k.killer))
                {
                    var kw = m_KillFeedStyle.CalcSize(new GUIContent(k.killer)).x;
                    Outlined(new Rect(x, r.y, kw + 4, r.height), k.killer, m_KillFeedStyle, Art.WithAlpha(k.killerColor, a), 1.2f);
                    x += kw + 10 * s;
                    Outlined(new Rect(x, r.y, 40 * s, r.height), "»", m_KillFeedStyle, new Color(1f, 0.4f, 0.3f, a), 1.2f);
                    x += 26 * s;
                }
                Outlined(new Rect(x, r.y, 260 * s, r.height), string.IsNullOrEmpty(k.killer) ? k.victim + " caiu" : k.victim, m_KillFeedStyle, Art.WithAlpha(k.victimColor, a), 1.2f);
                ky += 34 * s;
            }

            Outlined(new Rect(16 * s, 12 * s, 460 * s, 24 * s), "Tab: placar · Esc: menu / personagem / bots", m_Small, new Color(1f, 1f, 1f, 0.75f), 1f);
        }

        void DrawCrosshair(PlayerNetwork me, float cx, float cy, float s)
        {
            float g = 6 * s, l = 9 * s, t = 3 * s;
            var cc = new Color(1f, 1f, 1f, 0.95f);
            var ink = new Color(0f, 0f, 0f, 0.6f);
            // contorno + traços
            Fill(new Rect(cx - t / 2 - 1, cy - g - l - 1, t + 2, l + 2), ink);
            Fill(new Rect(cx - t / 2 - 1, cy + g - 1, t + 2, l + 2), ink);
            Fill(new Rect(cx - g - l - 1, cy - t / 2 - 1, l + 2, t + 2), ink);
            Fill(new Rect(cx + g - 1, cy - t / 2 - 1, l + 2, t + 2), ink);
            Fill(new Rect(cx - t / 2, cy - g - l, t, l), cc);
            Fill(new Rect(cx - t / 2, cy + g, t, l), cc);
            Fill(new Rect(cx - g - l, cy - t / 2, l, t), cc);
            Fill(new Rect(cx + g, cy - t / 2, l, t), cc);
            Tex(new Rect(cx - 2.5f * s, cy - 2.5f * s, 5 * s, 5 * s), m_Circle, cc);

            if (m_HitMarkerTimer > 0f)
            {
                var hc = m_HitMarkerKill ? new Color(1f, 0.25f, 0.2f) : (m_HitMarkerHead ? new Color(1f, 0.85f, 0.2f) : Color.white);
                float d = 14 * s, sz = 9 * s;
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                Fill(new Rect(cx - t / 2, cy - d - sz, t, sz), hc);
                Fill(new Rect(cx - t / 2, cy + d, t, sz), hc);
                Fill(new Rect(cx - d - sz, cy - t / 2, sz, t), hc);
                Fill(new Rect(cx + d, cy - t / 2, sz, t), hc);
                GUI.matrix = m;
            }

            // Munição segmentada debaixo da mira (estilo arcade)
            var b = me.Brawler;
            int maxCharges = b != null ? b.primary.maxCharges : 3;
            float ammo = me.Combat.Ammo;
            float segW = 34 * s, segH = 9 * s, gap = 4 * s;
            float totalW = maxCharges * segW + (maxCharges - 1) * gap;
            float ax = cx - totalW / 2f, ay = cy + 34 * s;
            Panel(new Rect(ax - 4 * s, ay - 4 * s, totalW + 8 * s, segH + 8 * s), new Color(0.03f, 0.03f, 0.08f, 0.75f));
            for (int i = 0; i < maxCharges; i++)
            {
                var r = new Rect(ax + i * (segW + gap), ay, segW, segH);
                Fill(r, new Color(0.25f, 0.18f, 0.08f, 0.9f));
                float fill = Mathf.Clamp01(ammo - i);
                if (fill > 0f) Fill(new Rect(r.x, r.y, r.width * fill, r.height), fill >= 1f ? new Color(1f, 0.62f, 0.12f) : new Color(0.75f, 0.5f, 0.2f));
                if (fill >= 1f) Fill(new Rect(r.x, r.y, r.width, 2 * s), new Color(1f, 0.9f, 0.6f, 0.8f));
            }

            float ly = ay + segH + 8 * s;
            if (me.Health.IsProtected)
            {
                Outlined(new Rect(cx - 150, ly, 300, 20 * s), "ESCUDO ATIVO (termina ao atacar)", m_TinyCenter, new Color(0.55f, 0.88f, 1f), 1f);
                ly += 18 * s;
            }
            if (me.Health.IsSlowed)
            {
                Outlined(new Rect(cx - 150, ly, 300, 20 * s), "ABRANDADO", m_TinyCenter, new Color(0.8f, 0.6f, 1f), 1f);
                ly += 18 * s;
            }
            if (me.Visuals != null && me.Visuals.InBush)
                Outlined(new Rect(cx - 150, ly, 300, 20 * s), "ESCONDIDO NO ARBUSTO", m_TinyCenter, new Color(0.55f, 1f, 0.55f), 1f);
        }

        void DrawSuperButton(PlayerNetwork me, float s)
        {
            float d = 120 * s;
            var r = new Rect(Screen.width - d - 36 * s, Screen.height - d - 30 * s, d, d);
            float sup = Mathf.Clamp01(me.Combat.SuperCharge.Value);
            bool ready = me.Combat.SuperReady;
            var b = me.Brawler;
            var superCol = b != null ? Color.Lerp(b.vfx.superColor, new Color(1f, 0.85f, 0.2f), 0.55f) : Gold;

            if (ready)
            {
                float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 7f);
                var glow = new Rect(r.center.x - d * 0.75f * pulse, r.center.y - d * 0.75f * pulse, d * 1.5f * pulse, d * 1.5f * pulse);
                Tex(glow, Art.SoftTexture, Art.WithAlpha(superCol, 0.55f));
            }
            Tex(new Rect(r.x - 4 * s, r.y - 4 * s, d + 8 * s, d + 8 * s), m_Circle, Ink);
            Tex(r, m_Circle, new Color(0.15f, 0.15f, 0.25f, 0.95f));

            // Enchimento vertical do círculo (de baixo para cima)
            if (sup > 0f && Event.current.type == EventType.Repaint)
            {
                var fillRect = new Rect(r.x, r.y + r.height * (1f - sup), r.width, r.height * sup);
                var old = GUI.color;
                GUI.color = ready ? superCol : Art.Darken(superCol, 0.75f);
                GUI.DrawTextureWithTexCoords(fillRect, m_Circle, new Rect(0f, 0f, 1f, sup));
                GUI.color = old;
            }
            Tex(r, m_Ring, new Color(1f, 1f, 1f, ready ? 0.9f : 0.35f));

            m_SuperLabel.fontSize = Mathf.RoundToInt(24 * s);
            Outlined(new Rect(r.x, r.y + d * 0.28f, d, 30 * s), "SUPER", m_SuperLabel, ready ? Color.white : new Color(1f, 1f, 1f, 0.7f), 2f);
            Outlined(new Rect(r.x, r.y + d * 0.55f, d, 22 * s), ready ? "Botão dir. / E" : Mathf.RoundToInt(sup * 100) + "%", m_TinyCenter, Color.white, 1f);
        }

        void DrawDeathOverlay(PlayerNetwork me)
        {
            float s = Scale;
            if (MatchManager.IsCoopActive && !MatchManager.IsMatchOver)
            {
                DrawCoopDownedOverlay(me, s);
                return;
            }
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.02f, 0.08f, 0.35f));
            var killer = PlayerNetwork.Find(me.Health.LastKiller.Value);
            string who = killer != null && killer != me ? "ELIMINADO POR " + killer.DisplayName.ToUpperInvariant() : "FOSTE ELIMINADO";
            var r = new Rect(0, Screen.height * 0.28f, Screen.width, 60 * s);
            m_DeathOverlay.fontSize = Mathf.RoundToInt(38 * s);
            Outlined(r, who, m_DeathOverlay, new Color(1f, 0.4f, 0.35f), 3f);
            if (killer != null && killer != me && killer.Brawler != null)
                Outlined(new Rect(0, r.yMax, Screen.width, 30 * s), killer.Brawler.displayName + " · " + ShortRole(killer.Brawler), m_Center, Color.Lerp(killer.Brawler.Tint, Color.white, 0.4f), 1.5f);
            if (!MatchManager.IsMatchOver)
            {
                int secs = Mathf.CeilToInt(me.Health.RespawnRemaining);
                Outlined(new Rect(0, r.yMax + 40 * s, Screen.width, 70 * s), secs > 0 ? secs.ToString() : "", m_Title, Color.white, 3f);
                Outlined(new Rect(0, r.yMax + 110 * s, Screen.width, 30 * s), "Esc para trocar de Brawler", m_Center, new Color(1f, 1f, 1f, 0.8f), 1.5f);
            }
        }

        // ================================================================== IT TAKES TWO (cooperativo)

        static readonly Color CoopGreen = new Color(0.45f, 1f, 0.55f);

        void DrawCoopHud(PlayerNetwork me, MatchManager match, Rect top, float s)
        {
            float cx = Screen.width / 2f;

            // Laço: estado por baixo do painel do topo
            var bond = new Rect(cx - 210 * s, top.yMax + 6 * s, 420 * s, 24 * s);
            if (match.Phase.Value == MatchPhase.Playing)
            {
                if (match.HasBondPartner(me))
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
                    Panel(bond, new Color(0.1f, 0.4f, 0.2f, 0.75f));
                    Outlined(bond, $"LAÇO ATIVO  ·  +{Mathf.RoundToInt((match.coopBondDamageMultiplier - 1f) * 100f)}% dano  ·  cura", m_TinyCenter, Color.Lerp(Color.white, CoopGreen, pulse), 1f);
                }
                else if (!me.Health.IsDead)
                {
                    Panel(bond, new Color(0.05f, 0.05f, 0.12f, 0.6f));
                    Outlined(bond, $"Fiquem a menos de {Mathf.RoundToInt(match.coopBondRadius)} m para ativar o LAÇO", m_TinyCenter, new Color(1f, 1f, 1f, 0.75f), 1f);
                }
            }

            // Entre ondas: contagem grande no centro
            if (match.Phase.Value == MatchPhase.Playing && match.IsRoundIntermission.Value)
            {
                int secs = Mathf.CeilToInt(match.TimeRemaining);
                string head = match.CoopWave.Value == 0 ? "PREPAREM-SE" : $"ONDA {match.CoopWave.Value} LIMPA!";
                var r = new Rect(0, Screen.height * 0.24f, Screen.width, 60 * s);
                m_DeathOverlay.fontSize = Mathf.RoundToInt(40 * s);
                Outlined(r, head, m_DeathOverlay, match.CoopWave.Value == 0 ? Gold : CoopGreen, 3f);
                Outlined(new Rect(0, r.yMax, Screen.width, 30 * s), $"Onda {match.CoopWave.Value + 1} de {match.coopTotalWaves} em {secs}s", m_Center, Color.white, 1.5f);
            }

            // Parceiro no chão: marcador no ecrã + barra de progresso quando estou perto
            var cam = me.Visuals != null ? me.Visuals.PlayerCamera : null;
            if (cam == null || me.Health.IsDead) return;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == me || !MatchManager.IsCoopTeam(p) || !p.Health.IsDead) continue;
                var world = p.transform.position + Vector3.up * 1.2f;
                var sp = cam.WorldToScreenPoint(world);
                float dist = Vector3.Distance(me.transform.position, p.transform.position);
                bool behind = sp.z <= 0f;
                float x = behind ? Screen.width - sp.x : sp.x;
                float y = behind ? Screen.height - 60 * s : Screen.height - sp.y;
                x = Mathf.Clamp(x, 60 * s, Screen.width - 60 * s);
                y = Mathf.Clamp(y, 90 * s, Screen.height - 60 * s);

                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
                float d = 34 * s;
                Tex(new Rect(x - d / 2f - 3 * s, y - d / 2f - 3 * s, d + 6 * s, d + 6 * s), m_Circle, Ink);
                Tex(new Rect(x - d / 2f, y - d / 2f, d, d), m_Circle, new Color(1f, 0.35f, 0.3f, pulse));
                Outlined(new Rect(x - d / 2f, y - d / 2f, d, d), "+", m_BigCenter, Color.white, 2f);
                Outlined(new Rect(x - 120 * s, y + d / 2f + 2 * s, 240 * s, 20 * s), $"LEVANTA {p.DisplayName.ToUpperInvariant()}  ·  {Mathf.RoundToInt(dist)} m", m_TinyCenter, Color.white, 1f);

                float prog = p.Health.ReviveProgress.Value;
                if (prog > 0.001f)
                {
                    var bar = new Rect(cx - 160 * s, Screen.height * 0.62f, 320 * s, 20 * s);
                    InkPanel(bar, new Color(0.08f, 0.1f, 0.16f, 0.95f), 2f * s);
                    Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * prog), bar.height), CoopGreen);
                    Outlined(new Rect(bar.x, bar.y - 24 * s, bar.width, 22 * s), "A LEVANTAR " + p.DisplayName.ToUpperInvariant() + "…", m_TinyCenter, Color.white, 1f);
                }
                else if (dist > match.coopReviveRadius)
                {
                    Outlined(new Rect(0, Screen.height * 0.62f, Screen.width, 24 * s), "O teu parceiro caiu! Vai até ele e fica perto para o levantar.", m_Center, new Color(1f, 0.75f, 0.6f), 1.5f);
                }
            }
        }

        void DrawCoopDownedOverlay(PlayerNetwork me, float s)
        {
            var match = MatchManager.Instance;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.12f, 0.02f, 0.04f, 0.35f));
            var r = new Rect(0, Screen.height * 0.26f, Screen.width, 60 * s);
            m_DeathOverlay.fontSize = Mathf.RoundToInt(38 * s);
            Outlined(r, "CAÍSTE!", m_DeathOverlay, new Color(1f, 0.4f, 0.35f), 3f);

            PlayerNetwork mate = null;
            foreach (var p in PlayerNetwork.All)
                if (p != me && MatchManager.IsCoopTeam(p) && !p.Health.IsDead) { mate = p; break; }

            string hint = mate != null
                ? mate.DisplayName + " pode levantar-te: tem de ficar perto de ti."
                : "Ninguém para te levantar…";
            Outlined(new Rect(0, r.yMax + 4 * s, Screen.width, 30 * s), hint, m_Center, Color.white, 1.5f);

            float prog = me.Health.ReviveProgress.Value;
            var bar = new Rect(Screen.width / 2f - 180 * s, r.yMax + 50 * s, 360 * s, 24 * s);
            InkPanel(bar, new Color(0.08f, 0.1f, 0.16f, 0.95f), 2f * s);
            if (prog > 0.001f) Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * prog), bar.height), CoopGreen);
            Outlined(bar, prog > 0.001f ? "A LEVANTAR… " + Mathf.RoundToInt(prog * 100f) + "%" : "À espera do parceiro", m_TinyCenter, Color.white, 1f);

            if (match != null && mate != null)
            {
                float dist = Vector3.Distance(me.transform.position, mate.transform.position);
                Outlined(new Rect(0, bar.yMax + 8 * s, Screen.width, 24 * s), $"Parceiro a {Mathf.RoundToInt(dist)} m  ·  limpar a onda também te levanta", m_Center, new Color(1f, 1f, 1f, 0.75f), 1f);
            }
        }

        void DrawScoreboard()
        {
            float s = Scale;
            var players = PlayerNetwork.All.Where(p => p != null && p.IsSpawned)
                .OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList();

            float w = 660 * s, rowH = 42 * s;
            float h = 100 * s + rowH * players.Count;
            var area = new Rect((Screen.width - w) / 2f, Screen.height * 0.15f, w, h);
            InkPanel(area, PanelDark, 4f * s);

            Outlined(new Rect(area.x, area.y + 10 * s, w, 40 * s), "PLACAR", m_BigCenter, Gold, 2f);
            float y = area.y + 56 * s;
            DrawRow(area.x, y, w, "Jogador", "Brawler", "K", "D", m_Small, Color.clear, Color.white);
            y += 28 * s;
            int rank = 1;
            foreach (var p in players)
            {
                var bg = p == PlayerNetwork.Local ? new Color(1f, 0.8f, 0.3f, 0.28f) : new Color(1f, 1f, 1f, 0.06f);
                var name = (rank++) + ". " + (p.IsBot ? p.DisplayName + " (bot)" : p.DisplayName);
                var tint = p.Brawler != null ? Color.Lerp(p.Brawler.Tint, Color.white, 0.35f) : Color.white;
                DrawRow(area.x, y, w, name, p.Brawler != null ? p.Brawler.displayName : "?", p.Kills.Value.ToString(), p.Deaths.Value.ToString(), m_Label, bg, tint);
                y += rowH;
            }
        }

        void DrawRow(float x, float y, float w, string a, string b, string k, string d, GUIStyle style, Color bg, Color tint)
        {
            float s = Scale;
            if (bg.a > 0f) Panel(new Rect(x + 12 * s, y, w - 24 * s, 36 * s), bg);
            Outlined(new Rect(x + 26 * s, y + 5 * s, w * 0.46f, 30 * s), a, style, Color.white, 1.2f);
            Outlined(new Rect(x + w * 0.5f, y + 5 * s, w * 0.28f, 30 * s), b, style, tint, 1.2f);
            Outlined(new Rect(x + w * 0.8f, y + 5 * s, 60 * s, 30 * s), k, style, new Color(0.6f, 1f, 0.6f), 1.2f);
            Outlined(new Rect(x + w * 0.9f, y + 5 * s, 60 * s, 30 * s), d, style, new Color(1f, 0.6f, 0.6f), 1.2f);
        }

        void DrawMatchResults(MatchManager match)
        {
            float s = Scale;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.02f, 0.08f, 0.5f));
            if (match.IsCoop)
            {
                bool win = match.CoopVictory.Value;
                string title = win ? "VITÓRIA A DOIS!" : "DERROTA";
                string sub = win ? $"Sobreviveram às {match.coopTotalWaves} ondas — juntos é melhor."
                                 : $"Caíram os dois na onda {Mathf.Max(1, match.CoopWave.Value)}. Fiquem mais perto um do outro!";
                Outlined(new Rect(0, Screen.height * 0.03f, Screen.width, 70 * s), title, m_Title, win ? Gold : new Color(1f, 0.4f, 0.35f), 3f);
                Outlined(new Rect(0, Screen.height * 0.03f + 64 * s, Screen.width, 30 * s), sub + "   ·   Nova partida em " + Mathf.CeilToInt(match.TimeRemaining) + "s", m_Center, Color.white, 1.5f);
                DrawScoreboard();
                return;
            }
            Outlined(new Rect(0, Screen.height * 0.03f, Screen.width, 70 * s), "VENCEDOR: " + match.WinnerName.Value.ToString().ToUpperInvariant(), m_Title, Gold, 3f);
            Outlined(new Rect(0, Screen.height * 0.03f + 64 * s, Screen.width, 30 * s), "Nova partida em " + Mathf.CeilToInt(match.TimeRemaining) + "s", m_Center, Color.white, 1.5f);
            DrawScoreboard();
        }

        void OpenSettings()
        {
            m_SettingsOpen = true;
            m_SettingsTab = UseNewMenu && !m_InGame ? SettingsTabProfile : SettingsTabControls;
            m_Name = GameSession.LocalName;
            m_CapturingBinding = false;
            m_SettingsMessage = null;
        }

        void CloseSettings()
        {
            m_SettingsOpen = false;
            m_CapturingBinding = false;
            m_SettingsMessage = null;
            SaveProfileName();
            PlayerPrefs.Save();
        }

        void ChangeSettingsTab(int tab)
        {
            if (m_SettingsTab == tab) return;
            m_SettingsTab = tab;
            m_CapturingBinding = false;
        }

        static string ActionLabel(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "Avançar";
                case GameAction.MoveBackward: return "Recuar";
                case GameAction.MoveLeft: return "Mover à esquerda";
                case GameAction.MoveRight: return "Mover à direita";
                case GameAction.Fire: return "Ataque principal";
                case GameAction.Melee: return "Facada";
                case GameAction.Super: return "Super";
                case GameAction.Jump: return "Salto";
                case GameAction.Sprint: return "Correr";
                case GameAction.Scoreboard: return "Placar";
                case GameAction.Pause: return "Pausa";
                case GameAction.Aim: return "Mirar (Lince)";
                default: return action.ToString();
            }
        }

        void DrawSettingsMenu()
        {
            float s = Scale;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.03f, 0.08f, 0.74f));

            float w = Mathf.Min(800f * s, Screen.width - 36f * s);
            float h = Mathf.Min(720f * s, Screen.height - 36f * s);
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            InkPanel(area, PanelDark, 4f * s);
            GUILayout.BeginArea(new Rect(area.x + 24f * s, area.y + 16f * s, area.width - 48f * s, area.height - 32f * s));

            GUILayout.BeginHorizontal();
            GUILayout.Label("DEFINIÇÕES", m_Big, GUILayout.ExpandWidth(true), GUILayout.Height(42f * s));
            if (LayoutButton("VOLTAR", new Color(0.4f, 0.46f, 0.6f), GUILayout.Width(130f * s), GUILayout.Height(40f * s)))
                CloseSettings();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * s);

            GUILayout.BeginHorizontal();
            if (LayoutButton("PERFIL", m_SettingsTab == SettingsTabProfile ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabProfile);
            if (LayoutButton("TECLAS", m_SettingsTab == SettingsTabControls ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabControls);
            if (LayoutButton("JOGABILIDADE", m_SettingsTab == SettingsTabGameplay ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabGameplay);
            if (LayoutButton("ÁUDIO", m_SettingsTab == SettingsTabAudio ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabAudio);
            GUILayout.EndHorizontal();

            if (m_SettingsTab == SettingsTabControls)
                DrawKeyboardSettings(s);
            else if (m_SettingsTab == SettingsTabGameplay)
                DrawGameplaySettings(s);
            else if (m_SettingsTab == SettingsTabAudio)
                DrawAudioSettings(s);
            else if (m_SettingsTab == SettingsTabProfile)
                DrawProfileSettings(s);
            GUILayout.EndArea();
        }

        void DrawKeyboardSettings(float s)
        {
            GUILayout.Space(10f * s);
            GUILayout.Label("Seleciona uma ação e prime a tecla ou botão do rato pretendido. Mira: botão direito.", m_Small);
            GUILayout.Space(6f * s);
            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll, false, true, GUILayout.ExpandHeight(true));

            var actions = new[]
            {
                GameAction.MoveForward, GameAction.MoveBackward, GameAction.MoveLeft, GameAction.MoveRight,
                GameAction.Fire, GameAction.Melee, GameAction.Super, GameAction.Jump, GameAction.Sprint,
                GameAction.Scoreboard, GameAction.Pause, GameAction.Aim,
            };
            foreach (var action in actions)
            {
                GUILayout.BeginHorizontal("box", GUILayout.Height(48f * s));
                GUILayout.Label(ActionLabel(action), m_Label, GUILayout.ExpandWidth(true));
                string label = m_CapturingBinding && m_CapturingAction == action
                    ? "PRIME UMA TECLA..."
                    : GameInputSettings.BindingLabel(action);
                if (LayoutButton(label, m_CapturingBinding && m_CapturingAction == action
                        ? new Color(0.86f, 0.61f, 0.15f) : new Color(0.28f, 0.36f, 0.56f),
                    GUILayout.Width(210f * s), GUILayout.Height(38f * s)))
                {
                    m_CapturingAction = action;
                    m_CapturingBinding = true;
                    m_CaptureStartFrame = Time.frameCount;
                    m_SettingsMessage = null;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(m_SettingsMessage))
                GUILayout.Label(m_SettingsMessage, m_Small);

            GUILayout.BeginHorizontal();
            if (LayoutButton("REPOR TECLAS", new Color(0.75f, 0.45f, 0.2f), GUILayout.Height(40f * s)))
            {
                GameInputSettings.ResetBindings();
                m_CapturingBinding = false;
                m_SettingsMessage = "Controlos predefinidos restaurados.";
            }
            GUILayout.EndHorizontal();

            HandleBindingCapture();
        }

        void DrawGameplaySettings(float s)
        {
            GUILayout.Space(14f * s);
            GUILayout.Label("SENSIBILIDADE", m_Small);
            GUILayout.Label("Rato: " + GameSession.Sensitivity.ToString("0.0"), m_Label);
            float sensitivity = GUILayout.HorizontalSlider(GameSession.Sensitivity, 0.2f, 8f);
            if (!Mathf.Approximately(sensitivity, GameSession.Sensitivity))
                GameSession.Sensitivity = sensitivity;

            GUILayout.Space(16f * s);
            GUILayout.Label("MIRA DE PRECISÃO · LINCE", m_Small);
            GUILayout.Label("Sensibilidade ao mirar: " + GameSession.AimSensitivity.ToString("0.00"), m_Label);
            float aimSensitivity = GUILayout.HorizontalSlider(GameSession.AimSensitivity, 0.2f, 1f);
            if (!Mathf.Approximately(aimSensitivity, GameSession.AimSensitivity))
                GameSession.AimSensitivity = aimSensitivity;
            GUILayout.Label("Zoom (campo de visão): " + Mathf.RoundToInt(GameSession.ScopeFieldOfView) + "°", m_Label);
            float scopeFov = GUILayout.HorizontalSlider(GameSession.ScopeFieldOfView, 20f, 55f);
            if (!Mathf.Approximately(scopeFov, GameSession.ScopeFieldOfView))
                GameSession.ScopeFieldOfView = scopeFov;
            if (LayoutButton("REPOR MIRA E SENSIBILIDADE", new Color(0.4f, 0.46f, 0.6f),
                    GUILayout.Height(38f * s)))
            {
                GameSession.Sensitivity = 2f;
                GameSession.AimSensitivity = 0.5f;
                GameSession.ScopeFieldOfView = 32f;
            }
        }

        void DrawProfileSettings(float s)
        {
            GUILayout.Space(14f * s);
            GUILayout.Label("NOME DO JOGADOR", m_Small);
            GUILayout.Label("É o nome que os outros jogadores veem no placar e por cima do teu personagem.", m_Tiny);
            GUILayout.Space(4f * s);
            GUI.SetNextControlName("profile_name");
            m_Name = GUILayout.TextField(m_Name ?? "", 16, m_Field, GUILayout.Height(42f * s));
            GUILayout.Space(8f * s);
            GUILayout.BeginHorizontal();
            if (LayoutButton("GUARDAR NOME", PanelBlue, GUILayout.Height(40f * s)))
            {
                SaveProfileName();
                GUI.FocusControl(null);
                m_SettingsMessage = "Nome guardado: " + m_Name;
            }
            GUILayout.EndHorizontal();
            if (m_InGame)
                GUILayout.Label("O novo nome é usado a partir da próxima partida.", m_Tiny);

            var hero = BrawlerRegistry.Get(m_Hero);
            if (hero != null)
            {
                GUILayout.Space(18f * s);
                GUILayout.Label("PERSONAGEM ESCOLHIDO", m_Small);
                GUILayout.Label(hero.displayName + "  ·  " + RoleTitle(hero), m_Label);
            }
            if (!string.IsNullOrEmpty(m_SettingsMessage))
            {
                GUILayout.Space(8f * s);
                GUILayout.Label(m_SettingsMessage, m_Small);
            }
        }

        /// <summary>Guarda o nome do perfil (vazio volta ao nome anterior).</summary>
        void SaveProfileName()
        {
            var n = (m_Name ?? "").Trim();
            if (n.Length == 0) { m_Name = GameSession.LocalName; return; }
            GameSession.LocalName = n;
            m_Name = GameSession.LocalName;
        }

        void DrawAudioSettings(float s)
        {
            GUILayout.Space(14f * s);
            GUILayout.Label("VOLUME GERAL", m_Small);
            GUILayout.Label("Volume: " + Mathf.RoundToInt(GameSession.MasterVolume * 100f) + "%", m_Label);
            float volume = GUILayout.HorizontalSlider(GameSession.MasterVolume, 0f, 1f);
            if (!Mathf.Approximately(volume, GameSession.MasterVolume))
                GameSession.MasterVolume = volume;
            GUILayout.Space(8f * s);
            GUILayout.Label("A alteração é aplicada imediatamente e guardada neste dispositivo.", m_Tiny);
            if (LayoutButton("REPOR VOLUME", new Color(0.4f, 0.46f, 0.6f), GUILayout.Height(38f * s)))
                GameSession.MasterVolume = 1f;
        }

        void HandleBindingCapture()
        {
            if (!m_CapturingBinding || Time.frameCount <= m_CaptureStartFrame) return;
            var current = Event.current;
            KeyCode key = KeyCode.None;
            if (current.type == EventType.KeyDown && current.keyCode != KeyCode.None)
                key = current.keyCode;
            else if (current.type == EventType.MouseDown)
                key = (KeyCode)((int)KeyCode.Mouse0 + current.button);

            if (key == KeyCode.None) return;
            current.Use();
            if (GameInputSettings.TrySetBinding(m_CapturingAction, key))
            {
                m_SettingsMessage = "Atalho atualizado.";
                m_CapturingBinding = false;
            }
            else
            {
                m_SettingsMessage = "Essa tecla já está atribuída a outra ação.";
            }
        }

        void DrawPauseMenu()
        {
            float s = Scale;
            bool host = NM != null && NM.IsServer;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.02f, 0.08f, 0.45f));
            float w = 600 * s, h = (host ? 660 : 580) * s;
            var area = new Rect(40 * s, (Screen.height - h) / 2f, w, h);
            InkPanel(area, PanelDark, 4f * s);
            GUILayout.BeginArea(new Rect(area.x + 22 * s, area.y + 14 * s, area.width - 44 * s, area.height - 28 * s));
            var tr = GUILayoutUtility.GetRect(10, 46 * s);
            Outlined(tr, "PAUSA", m_BigCenter, Gold, 2f);
            GUILayout.Space(6 * s);

            GUILayout.Label("Brawler (troca já)", m_Small);
            var me = PlayerNetwork.Local;
            int current = me != null ? me.HeroId.Value : m_Hero;
            int sel = current;
            DrawBrawlerCards(s, ref sel, true, area.width - 44 * s);
            m_Hero = sel;

            GUILayout.Space(10 * s);
            GUILayout.Label("Sensibilidade do rato: " + GameSession.Sensitivity.ToString("0.0"), m_Label);
            float sens = GUILayout.HorizontalSlider(GameSession.Sensitivity, 0.2f, 8f);
            if (!Mathf.Approximately(sens, GameSession.Sensitivity)) GameSession.Sensitivity = sens;

            if (host)
            {
                GUILayout.Space(12 * s);
                DrawBotControls(s, true);
            }

            GUILayout.FlexibleSpace();
            if (LayoutButton("Definições", new Color(0.28f, 0.36f, 0.56f), GUILayout.Height(42 * s)))
                OpenSettings();
            if (LayoutButton("Continuar", new Color(0.4f, 0.85f, 0.45f), GUILayout.Height(46 * s))) m_Paused = false;
            GUILayout.Space(6 * s);
            var leaveLabel = host ? "Terminar partida (host)" : "Sair da partida";
            if (LayoutButton(leaveLabel, new Color(0.9f, 0.35f, 0.3f), GUILayout.Height(46 * s)))
            {
                m_Paused = false;
                PlayerPrefs.Save();
                ConnectionManager.Instance?.Leave();
            }
            GUILayout.EndArea();

            DrawBrawlerShowcase(current, new Rect(area.xMax + 30 * s, area.y, Screen.width - area.xMax - 70 * s, h));
        }

    }
}
