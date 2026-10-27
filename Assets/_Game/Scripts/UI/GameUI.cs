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
        const int SettingsTabGraphics = 4;
        const int SettingsTabController = 5;

        // captura de botões do comando (aba CONTROLLER)
        bool m_CapturingPad;
        string m_CapturingPadId;
        int m_PadCaptureFrame;

        public static bool GameplayInputAllowed => s_Instance != null && s_Instance.m_InGame &&
            !s_Instance.m_Paused && !s_Instance.m_SettingsOpen && !ProgressionUI.IsOpen;

        bool m_InGame;
        bool m_Paused;
        public static bool IsGameplayOverlayVisible => s_Instance != null && s_Instance.m_InGame && !s_Instance.m_Paused && !s_Instance.m_SettingsOpen && !ProgressionUI.IsOpen;
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
        float m_HeadshotTimer;
        bool m_HeadshotFatal;
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

        GUIStyle m_Title, m_Label, m_Big, m_Small, m_Center, m_Button, m_ButtonSmall, m_Field, m_Tiny, m_TinyCenter, m_Panel, m_Card, m_Huge;
        GUIStyle m_RoleDescription, m_StatValue, m_Initial, m_CardName, m_CardRole;
        GUIStyle m_OverheadName, m_DamageNumber, m_Toast, m_SuperLabel, m_DeathOverlay, m_BigCenter, m_KillFeedStyle;
        Texture2D m_White, m_Rounded, m_Vignette, m_Circle, m_Ring;
        float m_StylesScale = -1f;

        BrawlerPreview m_Preview;

        static NetworkManager NM => NetworkManager.Singleton;

        // Paleta
        static readonly Color Gold = MenuTheme.Cyan;
        static readonly Color PanelDark = MenuTheme.Glass;
        static readonly Color PanelBlue = MenuTheme.Selected;
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
            GraphicsOptions.ApplyDisplay();   // resolução e ecrã inteiro/janela escolhidos em SETTINGS → GRAPHICS
            MatchManager.MenuSelectedCompetitive = PlayerPrefs.GetInt("fpslan.competitive", 0) == 1;
            // Ecrãs de progressão (perfil, personagens, skins, pets, missões, recompensas)
            if (GetComponent<ProgressionUI>() == null) gameObject.AddComponent<ProgressionUI>();
            // armas, mãos e projéteis da 1.ª pessoa carregados no menu (não no 1.º spawn)
            StartCoroutine(ViewModelRig.PrewarmRoutine());
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            m_Preview?.Dispose();
            ReleaseHudTextures();
        }

        // ================================================================== API estática

        /// <summary>Acertos confirmados pelo host neste PC (testes de rede).</summary>
        public static int HitConfirmCount;

        public static void ShowHitMarker(bool headshot, bool killed)
        {
            HitConfirmCount++;
            if (s_Instance == null) return;
            s_Instance.m_HitMarkerTimer = killed ? 0.35f : 0.18f;
            s_Instance.m_HitMarkerHead = headshot;
            s_Instance.m_HitMarkerKill = killed;
        }

        public static void FlashDamage()
        {
            if (s_Instance != null) s_Instance.m_DamageFlash = 0.45f;
        }

        /// <summary>Indicador especial de headshot (e de tiro fatal) junto à mira.</summary>
        public static void ShowHeadshot(bool fatal)
        {
            if (s_Instance == null) return;
            s_Instance.m_HeadshotTimer = fatal ? 1.4f : 0.9f;
            s_Instance.m_HeadshotFatal = fatal;
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
            if (s_Instance.m_KillFeed.Count > 2) s_Instance.m_KillFeed.RemoveAt(0);
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
            if (s_Instance.m_Toasts.Count > 1) s_Instance.m_Toasts.RemoveAt(0);
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
                case 1: text = "ELIMINATION!"; break;
                case 2: text = "DOUBLE!"; break;
                case 3: text = "TRIPLE!"; break;
                default: text = "UNSTOPPABLE!"; break;
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
            MenuAudio.SetMenuMusic(!m_InGame, m_MScreen == MenuScreen.Setup);   // música do menu fora das partidas

            if (!m_InGame) m_Paused = false;
            if (InputReader.PausePressed && !m_CapturingBinding)
            {
                if (ProgressionUI.IsOpen)
                    ProgressionUI.Close();
                else if (m_SettingsOpen)
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
            m_HeadshotTimer -= dt;
            m_DamageFlash -= dt;
            m_KillFeed.RemoveAll(k => Time.time - k.time > 6f);
            m_DamageNumbers.RemoveAll(d => Time.time - d.time > 0.9f);
            m_Toasts.RemoveAll(t => Time.time - t.time > 1.6f);

            // Pré-visualização 3D: só no menu ou na pausa (troca de personagem)
            // (o menu novo usa retratos 2D; a pré-visualização 3D fica só para a pausa)
            bool showPreview = !UseNewMenu && !m_InGame && !ProgressionUI.IsOpen;
            if (showPreview)
            {
                int idx = m_InGame && PlayerNetwork.Local != null ? PlayerNetwork.Local.HeroId.Value : m_Hero;
                var gx = GameExperience.Current;
                var b = BrawlerRegistry.Get(idx);
                var pet = gx.Pets.EquippedPet;
                m_Preview.Show(idx, b != null ? gx.Skins.EquippedSkinFor(SkinTarget.Brawler, b.brawlerId) : null,
                    pet, pet != null ? gx.PetProgression.Level(pet.petId) : 1, pet != null ? gx.Skins.EquippedSkinFor(SkinTarget.Pet, pet.petId) : null);
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
                        Toast("FIGHT!", new Color(0.4f, 1f, 0.5f));
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

        float Scale => MenuTheme.Scale;

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
            m_ButtonSmall = new GUIStyle(m_Button) { fontSize = Mathf.RoundToInt(13 * s) };

            m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(20 * s), alignment = TextAnchor.MiddleLeft, border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(12, 12, 4, 4) };
            m_Title.font = GameTypography.Display;
            m_BigCenter.font = GameTypography.Title;
            m_StatValue.font = m_DamageNumber.font = GameTypography.Numbers;
            m_Field.normal.background = m_Rounded;
            m_Field.focused.background = m_Rounded;
            m_Field.hover.background = m_Rounded;
            m_Field.normal.textColor = Ink;
            m_Field.focused.textColor = Ink;
            m_Field.hover.textColor = Ink;

            m_Panel = new GUIStyle { border = new RectOffset(10, 10, 10, 10) };
            m_Panel.normal.background = m_Rounded;
            m_Card = new GUIStyle(m_Panel);
            foreach (var style in new[] { m_Title, m_Huge, m_Label, m_Big, m_Small, m_Tiny, m_TinyCenter, m_Center, m_RoleDescription, m_StatValue, m_Initial, m_CardName, m_CardRole, m_OverheadName, m_DamageNumber, m_Toast, m_SuperLabel, m_DeathOverlay, m_BigCenter, m_KillFeedStyle, m_Button, m_ButtonSmall, m_Field }) style.font = MenuTheme.Font;
            m_Field.normal.background = MenuTheme.Skin.textField.normal.background;
            m_Field.focused.background = MenuTheme.Skin.textField.focused.background;
            m_Field.hover.background = MenuTheme.Skin.textField.hover.background;
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
        void InkPanel(Rect r, Color c, float border = 3f) => MenuTheme.Panel(r, c == PanelBlue);

        void Tex(Rect r, Texture t, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, t);
            GUI.color = old;
        }

        /// <summary>Texto com contorno preto grosso.</summary>
        void Outlined(Rect r, string text, GUIStyle style, Color color, float thickness = 2f) => MenuTheme.Text(r, text, style, color);

        bool Button(Rect r, string text, Color c) => MenuTheme.Button(r, text, c, true, m_Button);

        bool LayoutButton(string text, Color c, params GUILayoutOption[] opts)
        {
            var r = GUILayoutUtility.GetRect(new GUIContent(text), m_Button, opts);
            return MenuTheme.Button(r, text, c, true, m_Button);
        }

        // ================================================================== OnGUI

        void OnGUI()
        {
            GUI.skin = MenuTheme.Skin;
            EnsureStyles();

            if (!m_InGame)
            {
                if (ProgressionUI.IsOpen) return; // os ecrãs de progressão ocupam o ecrã
                if (UseNewMenu) { DrawNewMainMenu(); return; }   // GameUI.MainMenu.cs
                if (m_SettingsOpen) DrawSettingsMenu();
                else DrawMainMenu();
                return;
            }

            DrawVignettes();
            if (IsGameplayOverlayVisible) DrawOverheadBars();
            if (IsGameplayOverlayVisible) DrawDamageNumbers();
            if (!m_Paused && !m_SettingsOpen && !ProgressionUI.IsOpen) DrawHud();
            if (IsGameplayOverlayVisible) DrawToasts();

            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned && match.Phase.Value == MatchPhase.Ended)
                DrawMatchResults(match);
            else if (InputReader.ScoreboardHeld)
                DrawScoreboard();

            if (m_Paused && !ProgressionUI.IsOpen) DrawPauseMenu();
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
            // nível de zoom (roda do rato)
            float mag = GameSession.ScopeMagnification;
            Outlined(new Rect(cx - radius, cy + radius * 0.78f, radius * 2f, 30f * Scale), mag.ToString("0.0") + "x", m_Center, color, 1.5f);
            string thermalKey = GameInputSettings.GetBinding(GameAction.ThermalScope) == KeyCode.Mouse2 ? "Wheel click" : GameInputSettings.BindingLabel(GameAction.ThermalScope);
            Outlined(new Rect(cx - radius, cy + radius * 0.78f + 26f * Scale, radius * 2f, 24f * Scale),
                "Scroll: zoom · " + thermalKey + ": " + (ThermalScope.Enabled ? "normal view" : "thermal"), m_TinyCenter, new Color(1f, 1f, 1f, 0.7f), 1.5f);
            if (ThermalScope.Active)
                Outlined(new Rect(cx - radius, cy - radius * 0.86f, radius * 2f, 30f * Scale), "THERMAL", m_Center, new Color(1f, 0.55f, 0.15f, 0.95f), 1.5f);
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
            var titleRect = GUILayoutUtility.GetRect(10, 62 * s);
            Outlined(titleRect, "FPS-LAN", m_Title, Gold, 3f);
            // Nível, XP e moedas do perfil + atalhos para a progressão
            var statusRect = GUILayoutUtility.GetRect(10, 34 * s);
            ProgressionUI.DrawHeaderStatus(statusRect);
            GUILayout.Space(4 * s);
            DrawProgressionButtons(s, true);
            GUILayout.Space(6 * s);

            GUILayout.Label("PLAYER PROFILE", m_Small);
            m_Name = GUILayout.TextField(m_Name ?? "", 16, m_Field, GUILayout.Height(38 * s));

            GUILayout.Space(8 * s);
            GUILayout.Label("CHOOSE YOUR CHARACTER", m_Small);
            DrawBrawlerCards(s, ref m_Hero, false, area.width - 48 * s);

            GUILayout.Space(8 * s);
            GUILayout.Label("Game mode", m_Small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("‹", m_Button, GUILayout.Width(48 * s), GUILayout.Height(38 * s)) | PadNav.Item(GUILayoutUtility.GetLastRect(), "‹")) CycleGameMode(-1);
            GUILayout.Label(GameModeLabel(MatchManager.MenuSelectedMode), m_Label, GUILayout.Height(38 * s));
            if (GUILayout.Button("›", m_Button, GUILayout.Width(48 * s), GUILayout.Height(38 * s)) | PadNav.Item(GUILayoutUtility.GetLastRect(), "›")) CycleGameMode(1);
            // Casual: melhorias limitadas ativas · Competitiva: atributos normalizados, pets só cosméticos
            bool comp = MatchManager.MenuSelectedCompetitive;
            if (LayoutButton(comp ? "COMPETITIVE" : "CASUAL", comp ? new Color(0.85f, 0.35f, 0.3f) : new Color(0.3f, 0.65f, 0.4f),
                    GUILayout.Width(150 * s), GUILayout.Height(38 * s)))
            {
                MatchManager.MenuSelectedCompetitive = !comp;
                PlayerPrefs.SetInt("fpslan.competitive", comp ? 0 : 1);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * s);
            bool busy = NM != null && NM.IsListening;
            GUI.enabled = !busy;

            DrawBotControls(s, false);
            GUILayout.Space(8 * s);

            GUILayout.Label("CREATE A ROOM", m_Small);
            GUILayout.BeginHorizontal();
            if (LayoutButton("HOST", new Color(0.3f, 0.75f, 1f), GUILayout.Height(52 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = m_MenuBots;
            }
            if (LayoutButton("PRACTICE (with bots)", new Color(1f, 0.75f, 0.2f), GUILayout.Height(52 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = Mathf.Max(3, m_MenuBots);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8 * s);
            GUILayout.Label("JOIN A ROOM", m_Small);
            GUILayout.Label("Host IP · UDP " + ConnectionManager.DefaultPort, m_Tiny);
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
                if (LayoutButton("Cancel", new Color(0.9f, 0.35f, 0.3f), GUILayout.Height(36 * s)))
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
            GUILayout.Label("Your local IP: " + ips + "   ·   UDP " + ConnectionManager.DefaultPort, m_Small);
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
            StatBar(new Rect(bx, y, bw, 22 * s), "HEALTH", b.stats.maxHealth / 150f, b.stats.maxHealth.ToString(), new Color(0.4f, 0.95f, 0.45f));
            y += 28 * s;
            float range = b.primary.attack != null && b.primary.attack.delivery != null ? b.primary.attack.delivery.EffectiveRange : 0f;
            StatBar(new Rect(bx, y, bw, 22 * s), "RANGE", range / 45f, RangeLabel(range), new Color(0.35f, 0.75f, 1f));
            y += 28 * s;
            float dps = EstimateBurst(b);
            StatBar(new Rect(bx, y, bw, 22 * s), "DAMAGE", dps / 140f, Mathf.RoundToInt(dps).ToString(), new Color(1f, 0.5f, 0.3f));
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
            if (dash < 0) dash = role.IndexOf('·');
            return dash > 0 ? role.Substring(0, dash).Trim().ToUpperInvariant() : role.ToUpperInvariant();
        }

        static string RangeLabel(float r) => r >= 30f ? "Long" : (r >= 16f ? "Medium" : "Short");

        static float EstimateBurst(BrawlerDefinition b)
        {
            var a = b.primary.attack;
            if (a == null || a.delivery == null) return 0f;
            var e = a.effect;
            int shots = a.delivery.ShotCount;
            float near = Mathf.Max(e.nearMultiplier, e.farMultiplier);
            if (a.delivery is ArcProjectileDelivery) return e.splashDamage * shots;
            if (a.delivery is ConeVolumeDelivery cone) return e.damage * near * Mathf.Max(1, cone.ScheduledTicks);
            float direct = 0f;
            for (int i = 0; i < shots; i++) direct += e.BaseDamageForShot(i) * near;
            return direct + e.splashDamage * shots * 0.3f;
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

                Outlined(new Rect(icon.xMax + 8 * s, r.y + 10 * s, r.width - ic - 22 * s, 24 * s), b.ShortName, m_CardName, Color.white, 1.5f);
                Outlined(new Rect(icon.xMax + 8 * s, r.y + 36 * s, r.width - ic - 22 * s, 20 * s), ShortRole(b), m_CardRole, new Color(1f, 1f, 1f, 0.9f), 1f);

                // Nível da personagem (progressão individual)
                int charLevel = GameExperience.Current.Progression.CharacterLevel(b.brawlerId);
                var lvRect = new Rect(r.xMax - 28 * s, r.y + 5 * s, 23 * s, 23 * s);
                Tex(new Rect(lvRect.x - 2 * s, lvRect.y - 2 * s, lvRect.width + 4 * s, lvRect.height + 4 * s), m_Circle, Ink);
                Tex(lvRect, m_Circle, new Color(1f, 0.78f, 0.25f));
                Outlined(lvRect, charLevel.ToString(), m_TinyCenter, Color.white, 1f);

                if (b.available && PadNav.Hit(r) && !isSel)
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
            if (dash < 0) dash = role.IndexOf('·');
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
            if (LayoutButton("Difficulty: " + BotManager.DifficultyLabel(BotManager.Difficulty), new Color(0.55f, 0.5f, 0.95f), GUILayout.Height(36 * s)))
            {
                BotManager.Difficulty = (BotDifficulty)(((int)BotManager.Difficulty + 1) % 3);
                PlayerPrefs.SetInt("fpslan.botdiff", (int)BotManager.Difficulty);
                BotManager.ApplyDifficulty();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Atalhos para os ecrãs de progressão (menu principal e pausa).</summary>
        void DrawProgressionButtons(float s, bool includeSettings)
        {
            var c = new Color(0.28f, 0.36f, 0.56f);
            GUILayout.BeginHorizontal();
            if (LayoutButtonSmall("PROFILE", c, GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Profile);
            if (LayoutButtonSmall("CHARACTERS", c, GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Characters);
            if (LayoutButtonSmall("LEVELS", c, GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Levels);
            if (LayoutButtonSmall("SKINS", c, GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Skins);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (LayoutButtonSmall("PETS", new Color(0.35f, 0.55f, 0.35f), GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Pets);
            if (LayoutButtonSmall("QUESTS", new Color(0.55f, 0.45f, 0.2f), GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Quests);
            if (LayoutButtonSmall("REWARDS", new Color(0.6f, 0.4f, 0.2f), GUILayout.Height(32 * s))) ProgressionUI.Open(ProgressionScreen.Rewards);
            if (includeSettings && LayoutButtonSmall("SETTINGS", new Color(0.35f, 0.38f, 0.48f), GUILayout.Height(32 * s))) OpenSettings();
            GUILayout.EndHorizontal();
        }

        bool LayoutButtonSmall(string text, Color c, params GUILayoutOption[] opts)
        {
            var r = GUILayoutUtility.GetRect(new GUIContent(text), m_ButtonSmall, opts);
            return MenuTheme.Button(r, text, c, true, m_ButtonSmall);
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
                case GameMode.Wipeout: return "WIPEOUT · Team eliminations";
                case GameMode.Knockout: return "KNOCKOUT · Best of three";
                case GameMode.Showdown: return "SHOWDOWN · Last character standing";
                case GameMode.ItTakesTwo: return "IT TAKES TWO · Two-player co-op";
                case GameMode.GemGrab: return "GEM RUSH · Hold 10 gems as a team";
                default: return "DEATHMATCH · Free for all";
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
                if (!HudLabels.Place("player:" + p.Id, new Rect(sp.x - 130 * k, y - 23 * k, 260 * k, 44 * k), out var labelArea)) continue;
                x = labelArea.center.x - bw / 2f; y = labelArea.y + 23 * k;

                float hp01 = Mathf.Clamp01(p.Health.Health.Value / (float)Mathf.Max(1, p.Health.MaxHealth));
                var barRect = new Rect(x, y, bw, bh);
                bool mate = MatchManager.AreTeammates(me, p);
                InkPanel(barRect, mate ? new Color(0.05f, 0.18f, 0.08f, 0.95f) : new Color(0.2f, 0.05f, 0.08f, 0.95f), 2f * k);
                if (hp01 > 0f) Panel(new Rect(x, y, Mathf.Max(bh, bw * hp01), bh), mate
                    ? Color.Lerp(new Color(0.25f, 0.75f, 0.3f), new Color(0.4f, 0.95f, 0.45f), hp01)
                    : Color.Lerp(new Color(0.95f, 0.15f, 0.2f), new Color(1f, 0.35f, 0.3f), hp01));
                if (p.Health.IsSlowed) Fill(new Rect(x + 3, y + bh - 3 * k, bw - 6, 2 * k), new Color(0.75f, 0.5f, 1f));
                Outlined(new Rect(x, y - 1, bw, bh + 2), p.Health.Health.Value.ToString(), m_TinyCenter, Color.white, 1f);

                var label = (MatchManager.IsTeamMode(MatchManager.Instance.Mode.Value) ? (mate ? "ALLY · " : "ENEMY · ") : "") + p.DisplayName;
                if (GemGrabManager.IsActive && GemGrabManager.Instance != null)
                {
                    int carried = GemGrabManager.Instance.CarriedBy(p.Id);
                    if (carried > 0) label += " · " + carried + " GEMS";
                }
                m_OverheadName.fontSize = Mathf.RoundToInt(13 * k);
                var nameRect = new Rect(labelArea.x, y - 22 * k, labelArea.width, 21 * k);
                bool teamMode = MatchManager.Instance != null && MatchManager.IsTeamMode(MatchManager.Instance.Mode.Value);
                if (teamMode) Panel(nameRect, new Color(0.015f, 0.035f, 0.065f, 0.88f));
                Outlined(nameRect, label, m_OverheadName, teamMode ? (mate ? TeamPresentation.Ally : TeamPresentation.Enemy) : Color.Lerp(p.Brawler.Tint, Color.white, 0.4f), 1.2f);

                if (p.Combat.SuperReady)
                {
                    float d = 12 * k;
                    Tex(new Rect(x - d - 4 * k, y + bh / 2f - d / 2f, d, d), m_Circle, new Color(1f, 0.85f, 0.2f, 0.7f + 0.3f * Mathf.Sin(Time.time * 8f)));
                }
                if (p.Health.IsProtected)
                {
                    Outlined(new Rect(sp.x - 60, y + bh + 2 * k, 120, 16 * k), "SHIELD", m_OverheadName, new Color(0.5f, 0.85f, 1f), 1f);
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
                m_Toast.fontSize = Mathf.RoundToInt(20 * s);
                Outlined(new Rect(0, y, Screen.width, 60 * s * pop), t.text, m_Toast, c, 3f);
                y += 58 * s;
            }
        }

        void DrawHud()
        {
            var me = PlayerNetwork.Local;
            if (me == null) return;
            float s = Scale;
            EnsureHudStyles(s);
            if (me.Health.IsDead) DrawDeathOverlay(me);
            else DrawCrosshair(me, Screen.width / 2f, Screen.height / 2f, s);
            DrawHudVitals(me, s);
            DrawSuperButton(me, s);
            DrawHudHeader(me, s);
            DrawHudFeed(s);
            DrawHudNetwork(s);
            DrawHudEmotes(me, s);
            HudLabel(new Rect(22 * s, 16 * s, 360 * s, 22 * s),
                GameInputSettings.BindingLabel(GameAction.Scoreboard) + " · SCOREBOARD    " +
                GameInputSettings.BindingLabel(GameAction.Pause) + " · MENU", m_HudSmall, new Color(1, 1, 1, 0.65f));
        }

        void DrawCrosshair(PlayerNetwork me, float cx, float cy, float s)
        {
            float g = 6 * s, l = 9 * s, t = 3 * s;
            var cc = new Color(1f, 1f, 1f, 0.95f);
            var ink = new Color(0f, 0f, 0f, 0.6f);
            var brawler = me.Brawler;
            bool sniper = brawler != null && brawler.firstPerson.rig == RigStyle.Sniper;
            bool armed = me.Combat.InSuperMode;
            if (sniper)
            {
                // Mira própria da sniper: anel fino com ponto central (vermelha com o tiro fatal armado)
                var rc = armed ? new Color(1f, 0.25f, 0.2f, 0.95f) : cc;
                float d = 30 * s;
                Tex(new Rect(cx - d / 2f - 1, cy - d / 2f - 1, d + 2, d + 2), m_Ring, ink);
                Tex(new Rect(cx - d / 2f, cy - d / 2f, d, d), m_Ring, rc);
                Fill(new Rect(cx - 0.75f * s, cy - d * 0.9f, 1.5f * s, d * 0.35f), rc);
                Fill(new Rect(cx - 0.75f * s, cy + d * 0.55f, 1.5f * s, d * 0.35f), rc);
                Fill(new Rect(cx - d * 0.9f, cy - 0.75f * s, d * 0.35f, 1.5f * s), rc);
                Fill(new Rect(cx + d * 0.55f, cy - 0.75f * s, d * 0.35f, 1.5f * s), rc);
                Tex(new Rect(cx - 2f * s, cy - 2f * s, 4 * s, 4 * s), m_Circle, rc);
                if (armed) Outlined(new Rect(cx - 200, cy - 64 * s, 400, 24 * s), "FATAL SHOT READY · aim for the head", m_TinyCenter, rc, 1.2f);
            }
            else
            {
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
            }

            // Headshot / tiro fatal: anel que expande e texto
            if (m_HeadshotTimer > 0f)
            {
                float k = Mathf.Clamp01(m_HeadshotTimer / (m_HeadshotFatal ? 1.4f : 0.9f));
                float size = (m_HeadshotFatal ? 100f : 64f) * s * (1f + (1f - k) * 0.5f);
                var hc = m_HeadshotFatal ? new Color(1f, 0.2f, 0.15f, k) : new Color(1f, 0.85f, 0.2f, k);
                Tex(new Rect(cx - size / 2f, cy - size / 2f, size, size), m_Ring, hc);
                Outlined(new Rect(cx - 220, cy - 104 * s, 440, 36 * s), m_HeadshotFatal ? "FATAL SHOT!" : "HEADSHOT!", m_BigCenter, hc, 2.5f);
            }

            // Pet: direção de adversários detetados (Cão) ou ameaças nas costas (Galo)
            foreach (var ind in PetFollower.LocalIndicators)
            {
                float a = ind.angle * Mathf.Deg2Rad;
                float rad = 120 * s;
                var p = new Vector2(cx + Mathf.Sin(a) * rad, cy - Mathf.Cos(a) * rad);
                float d = (ind.alert ? 22f : 14f + 8f * ind.strength) * s;
                var col = ind.alert ? new Color(1f, 0.25f, 0.2f, 0.95f) : new Color(1f, 0.7f, 0.3f, 0.5f + 0.5f * ind.strength);
                Tex(new Rect(p.x - d / 2f - 2, p.y - d / 2f - 2, d + 4, d + 4), m_Circle, ink);
                Tex(new Rect(p.x - d / 2f, p.y - d / 2f, d, d), m_Circle, col);
                if (ind.alert) Outlined(new Rect(p.x - d / 2f, p.y - d / 2f, d, d), "!", m_TinyCenter, Color.white, 1f);
            }

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

            string status = me.Health.IsRooted ? "ROOTED · " + me.Status.RootRemaining.ToString("0.0") + "s" :
                me.Status != null && me.Status.HasDot.Value ? "POISONED" :
                me.Health.IsSlowed ? "SLOWED" : me.Health.Shield.Value > 0 ? "SHIELD · " + me.Health.Shield.Value :
                me.Health.IsProtected ? "SPAWN SHIELD" : me.Visuals != null && me.Visuals.InBush ? "HIDDEN" : "";
            if (status.Length > 0)
                Outlined(new Rect(cx - 160 * s, cy + 48 * s, 320 * s, 22 * s), status, m_TinyCenter, MenuTheme.Cyan);
        }

        void DrawSuperButton(PlayerNetwork me, float s)
        {
            float d = 116 * s;
            var r = new Rect(Screen.width - d - 72 * s, Screen.height - d - 62 * s, d, d);
            float sup = Mathf.Clamp01(me.Combat.SuperCharge.Value);
            bool ready = me.Combat.SuperReady;
            var b = me.Brawler;
            var superCol = b != null ? Color.Lerp(b.vfx.superColor, new Color(1f, 0.85f, 0.2f), 0.55f) : Gold;

            DrawHudSuperChrome(r, sup, ready, me.Combat.InSuperMode, s);

            // Especial em modo: indicador de duração (Minigun) ou de "armado" (tiro fatal)
            var combat = me.Combat;
            if (combat.InSuperMode && b != null)
            {
                var mode = b.super.mode;
                float frac = mode.duration > 0f ? Mathf.Clamp01(combat.ModeRemaining / Mathf.Max(0.01f, mode.duration))
                    : combat.ModeShotsRemaining / (float)Mathf.Max(1, mode.maxShots);
                float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 10f);
                var ring = new Rect(r.center.x - d * 0.62f * pulse, r.center.y - d * 0.62f * pulse, d * 1.24f * pulse, d * 1.24f * pulse);
                Tex(ring, m_Ring, Art.WithAlpha(superCol, 0.9f));
                // Barra de duração por cima do botão
                var bar = new Rect(r.x - 10 * s, r.y - 26 * s, d + 20 * s, 14 * s);
                Panel(bar, new Color(0.1f, 0.1f, 0.18f, 0.95f));
                if (frac > 0f) Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * frac), bar.height), superCol);
                string modeLabel = string.IsNullOrEmpty(mode.label) ? "SPECIAL" : mode.label.ToUpperInvariant();
                m_SuperLabel.fontSize = Mathf.RoundToInt(20 * s);
                Outlined(new Rect(r.x - 20 * s, r.y + d * 0.26f, d + 40 * s, 30 * s), modeLabel, m_SuperLabel, Color.white, 2f);
                string sub = mode.duration > 0f
                    ? combat.ModeRemaining.ToString("0.0") + "s · " + combat.ModeShotsRemaining + " bursts"
                    : (mode.cancellable ? "ARMED · " + GameInputSettings.BindingLabel(GameAction.Super) + " to cancel" : "ARMED");
                Outlined(new Rect(r.x - 30 * s, r.y + d * 0.55f, d + 60 * s, 22 * s), sub, m_TinyCenter, Color.white, 1f);
                if (combat.ModeActivation01 < 1f)
                    Outlined(new Rect(r.x - 40 * s, r.y - 52 * s, d + 80 * s, 22 * s), "TRANSFORMING...", m_TinyCenter, superCol, 1.2f);
                return;
            }

            string title = b != null && b.super.IsMode && !string.IsNullOrEmpty(b.super.mode.label)
                ? b.super.mode.label.ToUpperInvariant() : "SUPER";
            HudLabel(new Rect(r.x - 28 * s, r.y - 28 * s, d + 56 * s, 22 * s),
                title, m_HudSmall, ready ? HudCyan : Color.white);
            HudLabel(new Rect(r.x - 16 * s, r.yMax + 8 * s, d + 32 * s, 24 * s),
                ready ? "[" + GameInputSettings.BindingLabel(GameAction.Super) + "] READY" :
                Mathf.RoundToInt(sup * 100) + "%", m_HudSmall, ready ? HudCyan : Color.white);
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
            string who = killer != null && killer != me ? "ELIMINATED BY " + killer.DisplayName.ToUpperInvariant() : "ELIMINATED";
            var r = new Rect(0, Screen.height * 0.28f, Screen.width, 60 * s);
            m_DeathOverlay.fontSize = Mathf.RoundToInt(38 * s);
            Outlined(r, who, m_DeathOverlay, new Color(1f, 0.4f, 0.35f), 3f);
            if (killer != null && killer != me && killer.Brawler != null)
                Outlined(new Rect(0, r.yMax, Screen.width, 30 * s), killer.Brawler.displayName + " · " + ShortRole(killer.Brawler), m_Center, Color.Lerp(killer.Brawler.Tint, Color.white, 0.4f), 1.5f);
            if (!MatchManager.IsMatchOver)
            {
                int secs = Mathf.CeilToInt(me.Health.RespawnRemaining);
                Outlined(new Rect(0, r.yMax + 40 * s, Screen.width, 70 * s), secs > 0 ? secs.ToString() : "", m_Title, Color.white, 3f);
                Outlined(new Rect(0, r.yMax + 110 * s, Screen.width, 30 * s), "Respawning", m_Center, new Color(1f, 1f, 1f, 0.8f), 1.5f);
            }
        }

        // ================================================================== IT TAKES TWO (cooperativo)

        static readonly Color CoopGreen = new Color(0.45f, 1f, 0.55f);

        void DrawCoopHud(PlayerNetwork me, MatchManager match, Rect top, float s)
        {
            if (match.Phase.Value != MatchPhase.Playing) return;
            float cx = Screen.width / 2f;
            DrawCoopStory(me, match, top, s);

            // Laço: estado por baixo do painel do topo
            var bond = new Rect(cx - 190 * s, top.yMax + 6 * s, 380 * s, 22 * s);
            if (match.Phase.Value == MatchPhase.Playing)
            {
                if (match.HasBondPartner(me))
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f);
                    Panel(bond, new Color(0.1f, 0.4f, 0.2f, 0.75f));
                    Outlined(bond, $"BOND ACTIVE · +{Mathf.RoundToInt((match.coopBondDamageMultiplier - 1f) * 100f)}% damage · healing", m_TinyCenter, Color.Lerp(Color.white, CoopGreen, pulse), 1f);
                }
                else if (!me.Health.IsDead)
                {
                    Panel(bond, new Color(0.05f, 0.05f, 0.12f, 0.6f));
                    Outlined(bond, $"Stay within {Mathf.RoundToInt(match.coopBondRadius)} m for your bond", m_TinyCenter, new Color(1f, 1f, 1f, 0.75f), 1f);
                }
            }

            if (match.IsRoundIntermission.Value)
                Outlined(new Rect(22 * s, 170 * s, 310 * s, 24 * s), "Next wave in " + Mathf.CeilToInt(match.TimeRemaining) + "s", m_TinyCenter, Color.white);

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
                Outlined(new Rect(x - 120 * s, y + d / 2f + 2 * s, 240 * s, 20 * s), $"REVIVE {p.DisplayName.ToUpperInvariant()} · {Mathf.RoundToInt(dist)} m", m_TinyCenter, Color.white, 1f);

                float prog = p.Health.ReviveProgress.Value;
                if (prog > 0.001f)
                {
                    var bar = new Rect(cx - 160 * s, Screen.height * 0.62f, 320 * s, 20 * s);
                    InkPanel(bar, new Color(0.08f, 0.1f, 0.16f, 0.95f), 2f * s);
                    Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * prog), bar.height), CoopGreen);
                    Outlined(new Rect(bar.x, bar.y - 24 * s, bar.width, 22 * s), "REVIVING " + p.DisplayName.ToUpperInvariant() + "…", m_TinyCenter, Color.white, 1f);
                }
                else if (dist > match.coopReviveRadius)
                {
                    Outlined(new Rect(0, Screen.height * 0.62f, Screen.width, 24 * s), "Partner down · move closer to revive", m_Center, new Color(1f, 0.75f, 0.6f), 1.5f);
                }
                else
                {
                    Outlined(new Rect(0, Screen.height * 0.62f, Screen.width, 24 * s), "Hold [" + GameInputSettings.BindingLabel(GameAction.Interact) + "] while standing still. Damage interrupts.", m_Center, Color.white, 1.5f);
                }
            }
        }

        void DrawCoopDownedOverlay(PlayerNetwork me, float s)
        {
            var match = MatchManager.Instance;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.12f, 0.02f, 0.04f, 0.35f));
            var r = new Rect(0, Screen.height * 0.26f, Screen.width, 60 * s);
            m_DeathOverlay.fontSize = Mathf.RoundToInt(38 * s);
            Outlined(r, "YOU ARE DOWN!", m_DeathOverlay, new Color(1f, 0.4f, 0.35f), 3f);

            PlayerNetwork mate = null;
            foreach (var p in PlayerNetwork.All)
                if (p != me && MatchManager.IsCoopTeam(p) && !p.Health.IsDead) { mate = p; break; }

            string hint = mate != null
                ? mate.DisplayName + " can revive you by holding interact nearby."
                : "No partner available…";
            Outlined(new Rect(0, r.yMax + 4 * s, Screen.width, 30 * s), hint, m_Center, Color.white, 1.5f);

            float prog = me.Health.ReviveProgress.Value;
            var bar = new Rect(Screen.width / 2f - 180 * s, r.yMax + 50 * s, 360 * s, 24 * s);
            InkPanel(bar, new Color(0.08f, 0.1f, 0.16f, 0.95f), 2f * s);
            if (prog > 0.001f) Panel(new Rect(bar.x, bar.y, Mathf.Max(bar.height, bar.width * prog), bar.height), CoopGreen);
            Outlined(bar, prog > 0.001f ? "REVIVING… " + Mathf.RoundToInt(prog * 100f) + "%" : "Waiting for your partner", m_TinyCenter, Color.white, 1f);

            if (match != null && mate != null)
            {
                float dist = Vector3.Distance(me.transform.position, mate.transform.position);
                Outlined(new Rect(0, bar.yMax + 8 * s, Screen.width, 24 * s), $"Partner {Mathf.RoundToInt(dist)} m away · clearing the wave also revives you", m_Center, new Color(1f, 1f, 1f, 0.75f), 1f);
            }
        }

        void DrawScoreboard() => DrawScoreboard(Screen.width / 2f);

        void DrawScoreboard(float centerX)
        {
            float s = Scale;
            var players = PlayerNetwork.All.Where(p => p != null && p.IsSpawned)
                .OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList();

            float w = 680 * s, rowH = 42 * s;
            float h = 100 * s + rowH * players.Count;
            var area = new Rect(centerX - w / 2f, Screen.height * 0.15f, w, h);
            InkPanel(area, PanelDark, 4f * s);

            Outlined(new Rect(area.x, area.y + 10 * s, w, 40 * s), "SCOREBOARD", m_BigCenter, Gold, 2f);
            float y = area.y + 56 * s;
            DrawRow(area.x, y, w, "Player", "Brawler", "K", "A", "D", m_Small, Color.clear, Color.white, "Ping");
            y += 28 * s;
            int rank = 1;
            foreach (var p in players)
            {
                var bg = p == PlayerNetwork.Local ? new Color(1f, 0.8f, 0.3f, 0.28f) : new Color(1f, 1f, 1f, 0.06f);
                var name = (rank++) + ". " + (p.IsBot ? p.DisplayName + " (bot)" : p.DisplayName);
                var tint = p.Brawler != null ? Color.Lerp(p.Brawler.Tint, Color.white, 0.35f) : Color.white;
                DrawRow(area.x, y, w, name, p.Brawler != null ? p.Brawler.displayName : "?", p.Kills.Value.ToString(), p.Assists.Value.ToString(),
                    p.Deaths.Value.ToString(), m_Label, bg, tint, PingLabel(p));
                y += rowH;
            }
        }

        static string PingLabel(PlayerNetwork p)
        {
            if (p.IsBot) return "BOT";
            if (p.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId) return "HOST";
            return p.PingMs.Value < 0 ? "-" : p.PingMs.Value.ToString();
        }

        static Color PingColor(int ms) => ms < 80 ? new Color(0.6f, 1f, 0.6f) : ms < 150 ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.45f);

        void DrawRow(float x, float y, float w, string a, string b, string k, string assists, string d, GUIStyle style, Color bg, Color tint, string ping = "")
        {
            float s = Scale;
            if (bg.a > 0f) Panel(new Rect(x + 12 * s, y, w - 24 * s, 36 * s), bg);
            Outlined(new Rect(x + 26 * s, y + 5 * s, w * 0.44f, 30 * s), a, style, Color.white, 1.2f);
            Outlined(new Rect(x + w * 0.47f, y + 5 * s, w * 0.19f, 30 * s), b, style, tint, 1.2f);
            if (!string.IsNullOrEmpty(ping))
            {
                int ms;
                var pc = int.TryParse(ping, out ms) ? PingColor(ms) : new Color(0.75f, 0.8f, 0.9f);
                Outlined(new Rect(x + w * 0.665f, y + 5 * s, w * 0.09f, 30 * s), ping, style, pc, 1.2f);
            }
            Outlined(new Rect(x + w * 0.76f, y + 5 * s, 60 * s, 30 * s), k, style, new Color(0.6f, 1f, 0.6f), 1.2f);
            Outlined(new Rect(x + w * 0.84f, y + 5 * s, 60 * s, 30 * s), assists, style, new Color(0.6f, 0.85f, 1f), 1.2f);
            Outlined(new Rect(x + w * 0.92f, y + 5 * s, 60 * s, 30 * s), d, style, new Color(1f, 0.6f, 0.6f), 1.2f);
        }

        void DrawMatchResults(MatchManager match)
        {
            float s = Scale;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.02f, 0.08f, 0.5f));
            if (match.IsCoop)
            {
                DrawCoopStoryResults(match, s);
                return;
            }
            Outlined(new Rect(0, Screen.height * 0.03f, Screen.width, 70 * s), "WINNER: " + match.WinnerName.Value.ToString().ToUpperInvariant(), m_Title, Gold, 3f);
            Outlined(new Rect(0, Screen.height * 0.03f + 64 * s, Screen.width, 30 * s), "Next match in " + Mathf.CeilToInt(match.TimeRemaining) + "s", m_Center, Color.white, 1.5f);

            // Resultados de progressão (XP, níveis, desbloqueios) ao lado do placar
            if (ProgressionUI.HasReportFor(match.MatchId.Value.ToString()) && Screen.width > 1100 * s)
            {
                float panelW = Mathf.Min(520 * s, Screen.width * 0.4f);
                float boardW = 680 * s;
                float total = boardW + panelW + 30 * s;
                float left = (Screen.width - total) / 2f;
                DrawScoreboard(left + boardW / 2f);
                ProgressionUI.DrawReportPanel(new Rect(left + boardW + 30 * s, Screen.height * 0.15f, panelW, Mathf.Min(620 * s, Screen.height * 0.8f)));
            }
            else if (ProgressionUI.HasReportFor(match.MatchId.Value.ToString()))
            {
                // Ecrãs estreitos: só os resultados de progressão
                float panelW = Mathf.Min(520 * s, Screen.width - 40 * s);
                ProgressionUI.DrawReportPanel(new Rect((Screen.width - panelW) / 2f, Screen.height * 0.15f, panelW, Mathf.Min(620 * s, Screen.height * 0.8f)));
            }
            else DrawScoreboard();
        }

        void OpenSettings()
        {
            m_SettingsOpen = true;
            m_SettingsTab = UseNewMenu && !m_InGame ? SettingsTabProfile : SettingsTabControls;
            m_Name = GameSession.LocalName;
            m_CapturingBinding = false;
            m_SettingsMessage = null;
            m_GfxResIndex = -1;
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
            m_CapturingPad = false;
        }

        static string ActionLabel(GameAction action)
        {
            switch (action)
            {
                case GameAction.MoveForward: return "Move forward";
                case GameAction.MoveBackward: return "Move backward";
                case GameAction.MoveLeft: return "Move left";
                case GameAction.MoveRight: return "Move right";
                case GameAction.Fire: return "Primary attack";
                case GameAction.Melee: return "Melee";
                case GameAction.Super: return "Super";
                case GameAction.Jump: return "Jump";
                case GameAction.Sprint: return "Sprint";
                case GameAction.Scoreboard: return "Scoreboard";
                case GameAction.Pause: return "Pause";
                case GameAction.Aim: return "Aim";
                case GameAction.Interact: return "Revive partner (hold)";
                case GameAction.FeedPet: return "Feed pet (UPRA)";
                case GameAction.Emote: return "SITA spray wheel (hold)";
                case GameAction.Crouch: return "Crouch / slide (while sprinting)";
                case GameAction.ThermalScope: return "Thermal scope (Lince, while aiming)";
                case GameAction.LeanLeft: return "Lean left";
                case GameAction.LeanRight: return "Lean right";
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
            GUILayout.Label("SETTINGS", m_Big, GUILayout.ExpandWidth(true), GUILayout.Height(42f * s));
            if (LayoutButton("BACK", new Color(0.4f, 0.46f, 0.6f), GUILayout.Width(130f * s), GUILayout.Height(40f * s)))
                CloseSettings();
            GUILayout.EndHorizontal();
            GUILayout.Space(8f * s);

            GUILayout.BeginHorizontal();
            if (LayoutButton("PROFILE", m_SettingsTab == SettingsTabProfile ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabProfile);
            if (LayoutButton("CONTROLS", m_SettingsTab == SettingsTabControls ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabControls);
            if (LayoutButton("CONTROLLER", m_SettingsTab == SettingsTabController ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabController);
            if (LayoutButton("GAMEPLAY", m_SettingsTab == SettingsTabGameplay ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabGameplay);
            if (LayoutButton("AUDIO", m_SettingsTab == SettingsTabAudio ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabAudio);
            if (LayoutButton("GRAPHICS", m_SettingsTab == SettingsTabGraphics ? PanelBlue : new Color(0.22f, 0.24f, 0.34f),
                GUILayout.Height(40f * s)))
                ChangeSettingsTab(SettingsTabGraphics);
            GUILayout.EndHorizontal();

            if (m_SettingsTab == SettingsTabControls)
                DrawKeyboardSettings(s);
            else if (m_SettingsTab == SettingsTabGameplay)
                DrawGameplaySettings(s);
            else if (m_SettingsTab == SettingsTabAudio)
                DrawAudioSettings(s);
            else if (m_SettingsTab == SettingsTabProfile)
                DrawProfileSettings(s);
            else if (m_SettingsTab == SettingsTabGraphics)
                DrawGraphicsSettings(s);
            else if (m_SettingsTab == SettingsTabController)
                DrawControllerTab(s);
            GUILayout.EndArea();
        }

        void DrawKeyboardSettings(float s)
        {
            GUILayout.Space(10f * s);
            GUILayout.Label("Select an action, then press a key or mouse button. Aim: right mouse button.", m_Small);
            GUILayout.Space(6f * s);
            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll, false, true, GUILayout.ExpandHeight(true));

            var actions = new[]
            {
                GameAction.MoveForward, GameAction.MoveBackward, GameAction.MoveLeft, GameAction.MoveRight,
                GameAction.Fire, GameAction.Melee, GameAction.Super, GameAction.Jump, GameAction.Sprint,
                GameAction.Scoreboard, GameAction.Pause, GameAction.Aim, GameAction.ThermalScope, GameAction.Interact, GameAction.FeedPet, GameAction.Emote, GameAction.Crouch,
                GameAction.LeanLeft, GameAction.LeanRight,
            };
            foreach (var action in actions)
            {
                GUILayout.BeginHorizontal("box", GUILayout.Height(48f * s));
                GUILayout.Label(ActionLabel(action), m_Label, GUILayout.ExpandWidth(true));
                string label = m_CapturingBinding && m_CapturingAction == action
                    ? "PRESS A KEY..."
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

                // Campos de modo (manter premido / alternar) logo a seguir às teclas a que se aplicam
                if (action == GameAction.Aim)
                    { bool v = DrawHoldToggleRow("Aim mode", GameSession.AimToggle, s); if (v != GameSession.AimToggle) GameSession.AimToggle = v; }
                else if (action == GameAction.LeanRight)
                    { bool v = DrawHoldToggleRow("Lean mode", GameSession.LeanToggle, s); if (v != GameSession.LeanToggle) GameSession.LeanToggle = v; }
            }

            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(m_SettingsMessage))
                GUILayout.Label(m_SettingsMessage, m_Small);

            GUILayout.BeginHorizontal();
            if (LayoutButton("RESET CONTROLS", new Color(0.75f, 0.45f, 0.2f), GUILayout.Height(40f * s)))
            {
                GameInputSettings.ResetBindings();
                m_CapturingBinding = false;
                m_SettingsMessage = "Default controls restored.";
            }
            GUILayout.EndHorizontal();

            HandleBindingCapture();
        }

        static string PadActionLabel(string id)
        {
            switch (id)
            {
                case GamepadInput.ZoomInId: return "Zoom in (Lince, while aiming)";
                case GamepadInput.ZoomOutId: return "Zoom out (Lince, while aiming)";
                case GamepadInput.SprintId: return "Sprint (click to toggle)";
                default:
                    return System.Enum.TryParse<GameAction>(id, out var a) ? ActionLabel(a) : id;
            }
        }

        /// <summary>Aba CONTROLLER: comando PlayStation (ligar/desligar, estado, sensibilidade, inverter Y) e botões de cada ação.</summary>
        void DrawControllerTab(float s)
        {
            GUILayout.Space(10f * s);
            bool connected = GamepadInput.Connected;
            GUILayout.Label(connected ? "PlayStation controller connected: " + GamepadInput.Name
                : "No controller detected. PS5 DualSense / PS4 DualShock: USB cable, or Bluetooth (hold CREATE/SHARE + PS until the light flashes).", m_Small);
            GUILayout.BeginHorizontal();
            bool on = GameSession.ControllerEnabled;
            if (LayoutButton(on ? "CONTROLLER: ON" : "CONTROLLER: OFF", on ? new Color(0.2f, 0.62f, 0.38f) : new Color(0.45f, 0.3f, 0.3f),
                    GUILayout.Width(230f * s), GUILayout.Height(38f * s)))
                GameSession.ControllerEnabled = !on;
            bool inv = GameSession.ControllerInvertY;
            if (LayoutButton(inv ? "INVERT Y: ON" : "INVERT Y: OFF", new Color(0.28f, 0.36f, 0.56f),
                    GUILayout.Width(200f * s), GUILayout.Height(38f * s)))
                GameSession.ControllerInvertY = !inv;
            GUILayout.EndHorizontal();
            if (connected)
                GUILayout.Label("Live test → " + GamepadInput.DebugReadout(), m_Tiny);
            // sensibilidades dos sticks: slider (rato) e − / + (comando)
            float sv;
            sv = PadSlider("R stick sensitivity (look)", GameSession.ControllerLookSensitivity, 0.3f, 3f, 0.1f, s);
            if (!Mathf.Approximately(sv, GameSession.ControllerLookSensitivity)) GameSession.ControllerLookSensitivity = sv;
            sv = PadSlider("L stick sensitivity (move)", GameSession.ControllerMoveSensitivity, 0.5f, 2f, 0.1f, s);
            if (!Mathf.Approximately(sv, GameSession.ControllerMoveSensitivity)) GameSession.ControllerMoveSensitivity = sv;
            sv = PadSlider("R stick dead zone", GameSession.ControllerDeadZoneR, 0.02f, 0.5f, 0.02f, s);
            if (!Mathf.Approximately(sv, GameSession.ControllerDeadZoneR)) GameSession.ControllerDeadZoneR = sv;
            sv = PadSlider("L stick dead zone", GameSession.ControllerDeadZoneL, 0.02f, 0.5f, 0.02f, s);
            if (!Mathf.Approximately(sv, GameSession.ControllerDeadZoneL)) GameSession.ControllerDeadZoneL = sv;
            GUILayout.Space(6f * s);
            GUILayout.Label("Select an action, then press a button on the controller. Buttons already in use are swapped.", m_Tiny);

            m_SettingsScroll = GUILayout.BeginScrollView(m_SettingsScroll, false, true, GUILayout.ExpandHeight(true));
            foreach (var id in GamepadInput.BindableIds)
            {
                bool capturing = m_CapturingPad && m_CapturingPadId == id;
                GUILayout.BeginHorizontal("box", GUILayout.Height(48f * s));
                GUILayout.Label(PadActionLabel(id), m_Label, GUILayout.ExpandWidth(true));
                string label = capturing ? "PRESS A BUTTON..." : GamepadInput.ButtonLabel(GamepadInput.GetBinding(id));
                if (LayoutButton(label, capturing ? new Color(0.86f, 0.61f, 0.15f) : new Color(0.28f, 0.36f, 0.56f),
                        GUILayout.Width(210f * s), GUILayout.Height(38f * s)))
                {
                    if (!connected)
                        m_SettingsMessage = "Connect the controller first.";
                    else
                    {
                        m_CapturingPad = true;
                        m_CapturingPadId = id;
                        m_PadCaptureFrame = Time.frameCount;
                        m_SettingsMessage = null;
                    }
                }
                GUILayout.EndHorizontal();

                // modos (manter premido / alternar), partilhados com o teclado
                if (id == "Aim")
                    { bool v = DrawHoldToggleRow("Aim mode", GameSession.AimToggle, s); if (v != GameSession.AimToggle) GameSession.AimToggle = v; }
                else if (id == "LeanRight")
                    { bool v = DrawHoldToggleRow("Lean mode", GameSession.LeanToggle, s); if (v != GameSession.LeanToggle) GameSession.LeanToggle = v; }
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(m_SettingsMessage))
                GUILayout.Label(m_SettingsMessage, m_Small);
            if (LayoutButton("RESET CONTROLLER BUTTONS", new Color(0.75f, 0.45f, 0.2f), GUILayout.Height(40f * s)))
            {
                GamepadInput.ResetBindings();
                m_CapturingPad = false;
                m_SettingsMessage = "Default controller buttons restored.";
            }
            HandlePadCapture();
        }

        /// <summary>Linha com nome e valor, − / + (navegáveis com o comando) e slider (rato). Devolve o valor novo.</summary>
        float PadSlider(string label, float value, float min, float max, float step, float s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ": " + value.ToString("0.00"), m_Label, GUILayout.Width(330f * s));
            if (LayoutButton("−", new Color(0.28f, 0.36f, 0.56f), GUILayout.Width(44f * s), GUILayout.Height(32f * s)))
                value = Mathf.Max(min, value - step);
            float slid = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true), GUILayout.Height(32f * s));
            if (!Mathf.Approximately(slid, value)) value = slid;
            if (LayoutButton("+", new Color(0.28f, 0.36f, 0.56f), GUILayout.Width(44f * s), GUILayout.Height(32f * s)))
                value = Mathf.Min(max, value + step);
            GUILayout.EndHorizontal();
            return Mathf.Round(value * 100f) / 100f;
        }

        void HandlePadCapture()
        {
            if (!m_CapturingPad || Event.current.type != EventType.Repaint) return;
            // Escape cancela
            if (GameInputSettings.WasPressedThisFrame(KeyCode.Escape)) { m_CapturingPad = false; m_SettingsMessage = "Cancelled."; return; }
            if (Time.frameCount <= m_PadCaptureFrame + 1) return;
            var b = GamepadInput.AnyPressed();
            if (b == PadButton.None) return;
            if (b == PadButton.Options && m_CapturingPadId != "Pause" && GamepadInput.GetBinding("Pause") == PadButton.Options)
            {
                m_SettingsMessage = "Options is the pause button. Choose another one (or change Pause first).";
                return;
            }
            var swapped = GamepadInput.SetBinding(m_CapturingPadId, b);
            m_SettingsMessage = swapped != null
                ? PadActionLabel(m_CapturingPadId) + ": " + GamepadInput.ButtonLabel(b) + " (swapped with " + PadActionLabel(swapped) + ")."
                : PadActionLabel(m_CapturingPadId) + ": " + GamepadInput.ButtonLabel(b) + ".";
            m_CapturingPad = false;
        }

        /// <summary>Linha de definição HOLD / TOGGLE (dois botões, o escolhido destacado). Devolve o valor novo.</summary>
        bool DrawHoldToggleRow(string label, bool toggle, float s)
        {
            GUILayout.BeginHorizontal("box", GUILayout.Height(48f * s));
            GUILayout.Label(label, m_Label, GUILayout.ExpandWidth(true));
            var on = new Color(0.86f, 0.61f, 0.15f);
            var off = new Color(0.22f, 0.24f, 0.34f);
            if (LayoutButton("HOLD", toggle ? off : on, GUILayout.Width(103f * s), GUILayout.Height(38f * s))) toggle = false;
            if (LayoutButton("TOGGLE", toggle ? on : off, GUILayout.Width(103f * s), GUILayout.Height(38f * s))) toggle = true;
            GUILayout.EndHorizontal();
            return toggle;
        }

        void DrawGameplaySettings(float s)
        {
            GUILayout.Space(14f * s);
            GUILayout.Label("SENSITIVITY", m_Small);
            GUILayout.Label("Mouse: " + GameSession.Sensitivity.ToString("0.0"), m_Label);
            float sensitivity = GUILayout.HorizontalSlider(GameSession.Sensitivity, 0.2f, 8f);
            if (!Mathf.Approximately(sensitivity, GameSession.Sensitivity))
                GameSession.Sensitivity = sensitivity;

            GUILayout.Space(16f * s);
            GUILayout.Label("PRECISION AIM · LINCE", m_Small);
            GUILayout.Label("Aim sensitivity: " + GameSession.AimSensitivity.ToString("0.00"), m_Label);
            float aimSensitivity = GUILayout.HorizontalSlider(GameSession.AimSensitivity, 0.2f, 1f);
            if (!Mathf.Approximately(aimSensitivity, GameSession.AimSensitivity))
                GameSession.AimSensitivity = aimSensitivity;
            GUILayout.Label("Zoom (field of view): " + Mathf.RoundToInt(GameSession.ScopeFieldOfView) + "°", m_Label);
            float scopeFov = GUILayout.HorizontalSlider(GameSession.ScopeFieldOfView, GameSession.ScopeMinFov, GameSession.ScopeMaxFov);
            if (!Mathf.Approximately(scopeFov, GameSession.ScopeFieldOfView))
                GameSession.ScopeFieldOfView = scopeFov;
            if (LayoutButton("RESET AIM AND SENSITIVITY", new Color(0.4f, 0.46f, 0.6f),
                    GUILayout.Height(38f * s)))
            {
                GameSession.Sensitivity = 2f;
                GameSession.AimSensitivity = 0.5f;
                GameSession.ScopeFieldOfView = GameSession.ScopeDefaultFov;
            }

        }

        void DrawProfileSettings(float s)
        {
            GUILayout.Space(14f * s);
            GUILayout.Label("PLAYER NAME", m_Small);
            GUILayout.Label("Other players see this name in the scoreboard and above your character.", m_Tiny);
            GUILayout.Space(4f * s);
            GUI.SetNextControlName("profile_name");
            m_Name = GUILayout.TextField(m_Name ?? "", 16, m_Field, GUILayout.Height(42f * s));
            GUILayout.Space(8f * s);
            GUILayout.BeginHorizontal();
            if (LayoutButton("SAVE NAME", PanelBlue, GUILayout.Height(40f * s)))
            {
                SaveProfileName();
                GUI.FocusControl(null);
                m_SettingsMessage = "Name saved: " + m_Name;
            }
            GUILayout.EndHorizontal();
            if (m_InGame)
                GUILayout.Label("Your new name applies next match.", m_Tiny);

            var hero = BrawlerRegistry.Get(m_Hero);
            if (hero != null)
            {
                GUILayout.Space(18f * s);
                GUILayout.Label("SELECTED CHARACTER", m_Small);
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
            GUILayout.Label("MASTER VOLUME", m_Small);
            GUILayout.Label("Volume: " + Mathf.RoundToInt(GameSession.MasterVolume * 100f) + "%", m_Label);
            float volume = GUILayout.HorizontalSlider(GameSession.MasterVolume, 0f, 1f);
            if (!Mathf.Approximately(volume, GameSession.MasterVolume))
                GameSession.MasterVolume = volume;
            GUILayout.Space(8f * s);
            GUILayout.Label("Changes apply immediately and are saved on this device.", m_Tiny);
            if (LayoutButton("RESET VOLUME", new Color(0.4f, 0.46f, 0.6f), GUILayout.Height(38f * s)))
                GameSession.MasterVolume = 1f;
        }

        // ------------------------------------------------------------------ GRAPHICS

        int m_GfxResIndex = -1;
        bool m_GfxWindowed;
        List<Vector2Int> m_GfxResolutions;
        GUIStyle m_GfxCenter;
        GUIStyle GfxCenterStyle => m_GfxCenter != null && m_GfxCenter.fontSize == m_Label.fontSize ? m_GfxCenter
            : (m_GfxCenter = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter });

        void DrawGraphicsSettings(float s)
        {
            if (m_GfxResIndex < 0 || m_GfxResolutions == null)
            {
                m_GfxResolutions = GraphicsOptions.AvailableResolutions();
                m_GfxResIndex = Mathf.Max(0, m_GfxResolutions.IndexOf(GraphicsOptions.SavedResolution));
                m_GfxWindowed = GraphicsOptions.Windowed;
            }
            var off = new Color(0.22f, 0.24f, 0.34f);

            GUILayout.Space(14f * s);
            GUILayout.Label("QUALITY", m_Small);
            GUILayout.Label("Lower quality = more FPS on weaker PCs (fewer shadows, lights and effects). Applies immediately.", m_Tiny);
            GUILayout.Space(4f * s);
            var names = GraphicsOptions.QualityNames;
            int current = GraphicsOptions.Quality;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++)
                if (LayoutButton(names[i].ToUpperInvariant(), i == current ? PanelBlue : off, GUILayout.Height(38f * s)))
                    GraphicsOptions.Quality = i;
            GUILayout.EndHorizontal();

            GUILayout.Space(18f * s);
            GUILayout.Label("RESOLUTION", m_Small);
            GUILayout.Label("Lower resolution = more FPS. The image is stretched to fill the screen.", m_Tiny);
            GUILayout.Space(4f * s);
            m_GfxResIndex = Mathf.Clamp(m_GfxResIndex, 0, m_GfxResolutions.Count - 1);
            var res = m_GfxResolutions[m_GfxResIndex];
            var native = GraphicsOptions.Native;
            GUILayout.BeginHorizontal();
            if (LayoutButton("<", off, GUILayout.Width(64f * s), GUILayout.Height(40f * s)))
                m_GfxResIndex = Mathf.Max(0, m_GfxResIndex - 1);
            GUILayout.Label(res.x + " × " + res.y + (res == native ? "  (native)" : ""), GfxCenterStyle, GUILayout.ExpandWidth(true), GUILayout.Height(40f * s));
            if (LayoutButton(">", off, GUILayout.Width(64f * s), GUILayout.Height(40f * s)))
                m_GfxResIndex = Mathf.Min(m_GfxResolutions.Count - 1, m_GfxResIndex + 1);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f * s);
            GUILayout.BeginHorizontal();
            if (LayoutButton(m_GfxWindowed ? "WINDOWED" : "FULLSCREEN", off, GUILayout.Height(40f * s)))
                m_GfxWindowed = !m_GfxWindowed;
            if (LayoutButton("APPLY", PanelBlue, GUILayout.Height(40f * s)))
            {
                GraphicsOptions.SetDisplay(res, m_GfxWindowed);
                m_SettingsMessage = Application.isEditor
                    ? "Saved. Resolution changes only take effect in the built game."
                    : "Display set to " + res.x + " × " + res.y + (m_GfxWindowed ? " (windowed)." : ".");
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(18f * s);
            GUILayout.Label("HUD", m_Small);
            GUILayout.Label("Shown in the top-left corner during the match.", m_Tiny);
            GUILayout.Space(4f * s);
            GUILayout.BeginHorizontal();
            bool showPing = GraphicsOptions.ShowPing;
            if (LayoutButton(showPing ? "PING: ON" : "PING: OFF", showPing ? PanelBlue : off, GUILayout.Height(38f * s)))
                GraphicsOptions.ShowPing = !showPing;
            bool showFps = GraphicsOptions.ShowFps;
            if (LayoutButton(showFps ? "FPS: ON" : "FPS: OFF", showFps ? PanelBlue : off, GUILayout.Height(38f * s)))
                GraphicsOptions.ShowFps = !showFps;
            GUILayout.EndHorizontal();
            GUILayout.Label("Now: " + Screen.width + " × " + Screen.height + "  ·  " + Mathf.RoundToInt(GraphicsOptions.Fps) + " FPS  ·  " + names[current], m_Label);
            if (!string.IsNullOrEmpty(m_SettingsMessage))
                GUILayout.Label(m_SettingsMessage, m_Small);
        }

        /// <summary>Canto superior esquerdo do HUD: ping ao host (online/LAN) e, se ativado, FPS.</summary>
        void DrawHudNetwork(float s)
        {
            string text = null;
            var color = new Color(1f, 1f, 1f, 0.8f);
            var nm = NM;
            if (GraphicsOptions.ShowPing && nm != null && nm.IsListening)
            {
                if (!nm.IsServer)
                {
                    var local = PlayerNetwork.Local;
                    int ms = local != null ? local.LocalPingMs : -1;
                    text = ms < 0 ? "PING ..." : "PING " + ms + " ms";
                    if (ms >= 0) color = PingColor(ms);
                }
                else if (nm.ConnectedClientsIds.Count > 1 || (ConnectionManager.Instance != null && ConnectionManager.Instance.IsOnline))
                    text = "HOST";
            }
            if (GraphicsOptions.ShowFps)
                text = (text == null ? "" : text + "    ") + "FPS " + Mathf.RoundToInt(GraphicsOptions.Fps);
            if (text != null)
                HudLabel(new Rect(22 * s, 40 * s, 320 * s, 20 * s), text, m_HudFeed, color);

            // Host lento = jogo pior para todos (menos atualizações por segundo e colisões menos precisas)
            var match = MatchManager.Instance;
            bool othersPlaying = nm != null && nm.IsListening && (!nm.IsServer || nm.ConnectedClientsIds.Count > 1);
            if (match != null && match.IsSpawned && othersPlaying && match.HostFps.Value < 30)
            {
                string warn = nm.IsServer
                    ? "YOU ARE HOSTING AT " + match.HostFps.Value + " FPS · LOWER SETTINGS > GRAPHICS SO EVERYONE PLAYS SMOOTHLY"
                    : "HOST IS RUNNING AT " + match.HostFps.Value + " FPS · THE GAME MAY STUTTER";
                HudLabel(new Rect(22 * s, 60 * s, 620 * s, 20 * s), warn, m_HudFeed, new Color(1f, 0.75f, 0.35f));
            }
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
                m_SettingsMessage = "Binding updated.";
                m_CapturingBinding = false;
            }
            else
            {
                m_SettingsMessage = "That key is already assigned to another action.";
            }
        }

        void DrawPauseMenu()
        {
            DrawSharedMenuBackground();
            float s = Scale;
            string room = ConnectionManager.Instance != null ? ConnectionManager.Instance.RoomCode : "";
            bool hasRoom = !string.IsNullOrEmpty(room);
            float w = Mathf.Min(350 * s, Screen.width - 40 * s), h = (hasRoom ? 400 : 330) * s;
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            EnsureMenuStyles();
            DrawTechPanel(panel, true);
            MText(new Rect(panel.x + 24 * s, panel.y + 30 * s, w - 48 * s, 46 * s), "PAUSED", m_MT30, MWhite);
            float x = panel.x + 30 * s, bw = w - 60 * s;
            if (MenuButton(new Rect(x, panel.y + 105 * s, bw, 48 * s), "RESUME", 8101, MBtn.Normal, true, m_MT17) == MClick.Clicked) m_Paused = false;
            if (MenuButton(new Rect(x, panel.y + 165 * s, bw, 48 * s), "SETTINGS", 8102, MBtn.Normal, false, m_MT17) == MClick.Clicked) OpenSettings();
            if (MenuButton(new Rect(x, panel.y + 225 * s, bw, 48 * s), "LEAVE MATCH", 8103, MBtn.Normal, false, m_MT17) == MClick.Clicked)
            {
                m_Paused = false;
                PlayerPrefs.Save();
                ConnectionManager.Instance?.Leave();
            }
            if (hasRoom)
            {
                // Sala online: o código para os amigos entrarem (PLAY ONLINE → JOIN WITH ROOM CODE).
                MText(new Rect(x, panel.y + 288 * s, bw, 18 * s), "ROOM CODE", m_MT12, MMuted, TextAnchor.MiddleLeft);
                MText(new Rect(x, panel.y + 306 * s, bw - 96 * s, 40 * s), room, m_MT24, MCyan, TextAnchor.MiddleLeft);
                if (MenuButton(new Rect(x + bw - 90 * s, panel.y + 308 * s, 90 * s, 36 * s), "COPY", 8104, MBtn.Normal, false, m_MT14) == MClick.Clicked)
                    GUIUtility.systemCopyBuffer = room;
            }
        }

    }
}
