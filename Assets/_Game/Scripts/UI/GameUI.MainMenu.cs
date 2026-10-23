using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Menu principal (referência visual: gamejam.pdf). Desenhado em IMGUI como o resto do GameUI, sem objetos na cena
    /// nem referências manuais: as imagens vêm de Resources/UI/Menu e a lógica reutiliza ConnectionManager, GameSession,
    /// BrawlerRegistry, MatchManager e BotManager.
    ///
    /// Fluxos:
    ///  • PLAY ONLINE → modo → mapa → personagem → JOGAR
    ///  • PLAY LOCAL  → criar servidor (modo → personagem) ou ligar a um servidor (código → personagem)
    /// O nome do jogador edita-se em Definições → Perfil. Ao escolher um mapa, o fundo aproxima-se da ilha
    /// correspondente (sem nunca mostrar o nome dela).
    /// O menu antigo continua em <c>DrawMainMenu()</c>; basta pôr <see cref="UseNewMenu"/> a false para voltar a ele.
    /// </summary>
    public partial class GameUI
    {
        /// <summary>true = menu novo; false = menu antigo (fallback).</summary>
        public static bool UseNewMenu = true;

        // ================================================================== dados do menu

        struct MenuCharacter
        {
            public string name, key, brawlerId;
            public MenuCharacter(string name, string key, string brawlerId) { this.name = name; this.key = key; this.brawlerId = brawlerId; }
        }

        /// <summary>Personagens do menu → brawler jogável (por brawlerId). Os nomes em jogo vêm do próprio brawler.</summary>
        static readonly MenuCharacter[] s_Roster =
        {
            new MenuCharacter("LINCE", "lince", "sniper"),
            new MenuCharacter("RIOT", "riot", "shotgunner"),
            new MenuCharacter("LUPUS MARINUS", "lupus", "artillery"),
            new MenuCharacter("MR.S", "mrs", "sprayer"),
            new MenuCharacter("N.CAGE", "ncage", "trapper"),
            new MenuCharacter("EKKO", "ekko", "gunslinger"),
        };

        struct MenuMode
        {
            public string title, subtitle, unavailableMessage;
            public string[] enumNames;
            public MenuMode(string title, string subtitle, string unavailableMessage, params string[] enumNames)
            { this.title = title; this.subtitle = subtitle; this.unavailableMessage = unavailableMessage; this.enumNames = enumNames; }
        }

        /// <summary>
        /// Um modo fica jogável automaticamente quando existir um valor com um destes nomes no enum GameMode
        /// (ex.: GameMode.GemGrab). Até lá, ao clicar aparece só a mensagem.
        /// </summary>
        static readonly MenuMode[] s_Modes =
        {
            new MenuMode("IT TAKES TWO", "The Pelourinho Heist · Lince & Riot", null, "ItTakesTwo", "Coop"),
            new MenuMode("DEATHMATCH", "Free for all", null, "Deathmatch"),
            new MenuMode("GEM RUSH", "Collect and hold the gems", "GEM RUSH is coming soon.", "GemGrab", "GemRush", "PiqueGema"),
            new MenuMode("SHOWDOWN", "Last one standing · ash storm on Fogo", null, "Showdown"),
        };

        struct MenuMap
        {
            public string key, coords;
            public bool playable;
            public Vector2 focus;   // ponto da ilha no fundo (UV, y para cima)
            public float zoom;
            public MenuMap(string key, bool playable, Vector2 focus, float zoom, string coords)
            { this.key = key; this.playable = playable; this.focus = focus; this.zoom = zoom; this.coords = coords; }
        }

        /// <summary>
        /// Mapas do carrossel (sem nomes no ecrã). Cada um aponta para uma ilha do fundo, para o efeito de aproximação.
        /// A Arena, Santiago (Cidade Velha) e Sal (Buracona) estão disponíveis.
        /// </summary>
        static readonly MenuMap[] s_Maps =
        {
            new MenuMap("pradaria", true, new Vector2(0.19f, 0.845f), 2.5f, "17°04'N  25°10'W"),
            new MenuMap("santiago", true, new Vector2(0.606f, 0.306f), 2.2f, "15°05'N  23°37'W"),
            new MenuMap("sal", true, new Vector2(0.775f, 0.772f), 2.7f, "16°45'N  22°57'W"),
            new MenuMap("boavista", false, new Vector2(0.8125f, 0.502f), 2.5f, "16°06'N  22°49'W"),
            new MenuMap("fogo", true, new Vector2(0.3125f, 0.264f), 2.8f, "14°55'N  24°23'W"),
        };

        const string KeyMenuMap = "fpslan.map";

        /// <summary>Mapa escolhido no menu (chave). Fica guardado para quando houver mais do que um mapa jogável.</summary>
        public static string SelectedMapKey => PlayerPrefs.GetString(KeyMenuMap, "pradaria");

        // ================================================================== estado

        enum MenuScreen { Main, Setup }
        enum SetupContext { Online, LocalHost, LocalJoin, OnlineJoin }
        static bool IsJoinCtx(SetupContext c) => c == SetupContext.LocalJoin || c == SetupContext.OnlineJoin;

        MenuScreen m_MScreen = MenuScreen.Main;
        SetupContext m_MSetupCtx = SetupContext.Online;
        float m_MScreenT = 0f;          // 0→1 entrada do ecrã atual
        bool m_MLocalOpen; float m_MLocalT;
        bool m_MOnlineOpen; float m_MOnlineT;
        string m_RoomCode = "";
        string m_MOnlineError;
        bool m_MAudioOpen; float m_MAudioT;
        int m_MFocus = 0;               // foco do teclado no menu principal (começa em PLAY ONLINE)
        int m_MModeSel = -1;
        bool MenuCoopSetup => !IsJoinCtx(m_MSetupCtx) && MatchManager.MenuSelectedMode == GameMode.ItTakesTwo;
        int m_MMapSel;
        float m_MMapScroll, m_MMapScrollTarget;
        int m_MCharPrev = -1; float m_MCharT = 1f;
        string m_MNotice; float m_MNoticeTime = -10f;
        string m_MLocalError;
        float m_MLastTime, m_MDt;
        float m_MAlpha = 1f;
        readonly Dictionary<int, float> m_MHover = new Dictionary<int, float>();
        readonly Dictionary<string, Texture2D> m_MTex = new Dictionary<string, Texture2D>();
        bool m_MInit;

        // fundo: câmara virtual sobre o arquipélago (foco em UV + zoom), com transição suave
        Vector2 m_BgFrom = new Vector2(0.5f, 0.5f), m_BgTo = new Vector2(0.5f, 0.5f), m_BgCur = new Vector2(0.5f, 0.5f);
        float m_BgZFrom = 1.06f, m_BgZTo = 1.06f, m_BgZCur = 1.06f, m_BgT = 1f, m_BgArc;
        int m_BgMap = -1;               // mapa em foco (-1 = vista geral)
        Vector2 m_BgAnchor;             // ponto do ecrã onde a ilha em foco deve ficar (por baixo do personagem)
        float m_BgAW, m_BgAWFrom, m_BgAWTo;
        float m_BgReturnAt;             // depois de espreitar um mapa indisponível, volta ao mapa escolhido

        // geometria base do layout (referência 1440×810)
        float K;
        float m_MOx, m_MOy;

        // estilos
        float m_MStyleK = -1f;
        Font m_MFont;
        GUIStyle m_MFill, m_MLine, m_MGlow, m_MSoftRect;
        GUIStyle m_MT40, m_MT30, m_MT24, m_MT20, m_MT17, m_MT14, m_MT12, m_MField, m_MPlaceholder;

        // paleta (azul profundo, vidro azulado, ciano, coral)
        static readonly Color MDeep = new Color(0.02f, 0.05f, 0.1f, 1f);
        static readonly Color MGlass = new Color(0.16f, 0.27f, 0.45f, 0.62f);
        static readonly Color MButtonFill = new Color(0.27f, 0.36f, 0.58f, 0.92f);
        static readonly Color MButtonHover = new Color(0.33f, 0.46f, 0.72f, 0.96f);
        static readonly Color MCyan = new Color(73f / 255f, 176f / 255f, 182f / 255f, 1f);
        static readonly Color MCyanDim = new Color(0.25f, 0.75f, 0.78f, 0.55f);
        static readonly Color MCoral = new Color(0.9f, 0.43f, 0.37f, 1f);
        static readonly Color MWhite = new Color(0.94f, 0.97f, 1f, 1f);
        static readonly Color MMuted = new Color(0.62f, 0.7f, 0.82f, 1f);

        // ================================================================== entrada

        /// <summary>Chamado no OnGUI quando não se está em jogo.</summary>
        void DrawNewMainMenu()
        {
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                float now = Time.unscaledTime;
                m_MDt = Mathf.Clamp(now - m_MLastTime, 0f, 0.1f);
                m_MLastTime = now;
                m_MScreenT = Mathf.MoveTowards(m_MScreenT, 1f, m_MDt * 4f);
                m_MLocalT = Mathf.MoveTowards(m_MLocalT, m_MLocalOpen ? 1f : 0f, m_MDt * 6f);
                m_MOnlineT = Mathf.MoveTowards(m_MOnlineT, m_MOnlineOpen ? 1f : 0f, m_MDt * 6f);
                m_MAudioT = Mathf.MoveTowards(m_MAudioT, m_MAudioOpen ? 1f : 0f, m_MDt * 7f);
                m_MCharT = Mathf.MoveTowards(m_MCharT, 1f, m_MDt * 5f);
                m_MMapScroll = Mathf.Lerp(m_MMapScroll, m_MMapScrollTarget, 1f - Mathf.Exp(-m_MDt * 10f));
                UpdateBackgroundCamera(m_MDt);
            }

            EnsureMenuStyles();
            if (DrawOpeningIntro()) return;
            MenuInit();

            bool connecting = NM != null && NM.IsListening;   // a ligar/criar (ainda sem jogador local)
            DrawMenuBackground();

            if (m_SettingsOpen)
            {
                DrawSettingsMenu();   // definições completas (perfil, teclas, jogabilidade, áudio)
                return;
            }

            if (!connecting) HandleMenuKeys(e);

            GUI.enabled = !connecting;
            float ease = EaseOut(m_MScreenT);
            m_MAlpha = ease;
            if (m_MScreen == MenuScreen.Main) DrawMenuMainScreen(ease);
            else DrawMenuSetupScreen(ease);
            m_MAlpha = 1f;

            DrawMenuTopRight();
            GUI.enabled = true;

            DrawMenuNotice();
            if (connecting) DrawMenuConnecting();
        }

        void MenuInit()
        {
            if (m_MInit) return;
            m_MInit = true;
            for (int i = 0; i < s_Modes.Length; i++)
                if (TryResolveMode(s_Modes[i], out var gm) && gm == MatchManager.MenuSelectedMode) { m_MModeSel = i; break; }
            if (m_MModeSel < 0) SelectMode(FirstPlayableMode(), false);
            m_MMapSel = 0;
            for (int i = 0; i < s_Maps.Length; i++) if (s_Maps[i].key == SelectedMapKey && s_Maps[i].playable) m_MMapSel = i;
            if (MenuCharIndexForHero(m_Hero) < 0) SelectMenuCharacter(0, false);
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo) SelectMode(m_MModeSel, false);
        }

        // ================================================================== ecrã principal

        void DrawMenuMainScreen(float ease)
        {
            float slide = (1f - ease) * -40f * K;
            var panel = V(82, 94, 484, 514);   // 3 botões (a história é contada dentro do IT TAKES TWO)
            panel.x += slide;
            DrawTechPanel(panel, true);

            MText(new Rect(panel.x, panel.y + 30 * K, panel.width, 56 * K), "MENU", m_MT40, MWhite);

            string[] labels = { "PLAY ONLINE", "PLAY LOCAL", "QUIT" };
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(panel.x + 48 * K, panel.y + 112 * K + i * 108 * K, panel.width - 96 * K, 86 * K);
                bool selected = (i == 1 && m_MLocalOpen) || (i == 0 && m_MOnlineOpen) || (m_MFocus == i && !m_MLocalOpen && !m_MOnlineOpen && GUI.enabled);
                var res = MenuButton(r, labels[i], 1000 + i, MBtn.Normal, selected, m_MT30);
                if (GUI.enabled && Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition)) m_MFocus = i;
                if (res == MClick.Clicked) ActivateMainItem(i);
            }

            var deco = new Rect(panel.x + 16 * K, panel.yMax + 34 * K, panel.width - 30 * K, 2 * K);
            DrawTechLine(deco, true);

            DrawProgressionHub();

            if (m_MLocalT > 0.001f)
            {
                float localH = LocalPanelHeight();
                float localY = Mathf.Max(panel.y, panel.y + 150 * K - Mathf.Max(0f, localH - 352 * K));
                DrawMenuLocalPanel(new Rect(panel.xMax + 14 * K, localY, 350 * K, localH));
            }
            if (m_MOnlineT > 0.001f)
                DrawMenuOnlinePanel(new Rect(panel.xMax + 14 * K, panel.y, 350 * K, OnlinePanelHeight));
        }

        void ActivateMainItem(int i)
        {
            switch (i)
            {
                case 0:
                    m_MLocalOpen = false;
                    m_MOnlineOpen = !m_MOnlineOpen;
                    m_MOnlineError = null;
                    break;
                case 1:
                    m_MOnlineOpen = false;
                    m_MLocalOpen = !m_MLocalOpen;
                    m_MLocalError = null;
                    break;
                case 2: QuitGame(); break;
            }
        }

        /// <summary>PLAY LOCAL: criar um servidor na rede local ou ligar a um com o código (IP).</summary>
        void DrawMenuLocalPanel(Rect r)
        {
            float t = EaseOut(m_MLocalT);
            float oldA = m_MAlpha;
            m_MAlpha *= t;
            r.x -= (1f - t) * 30f * K;
            DrawTechPanel(r, false);

            MText(new Rect(r.x, r.y + 14 * K, r.width, 34 * K), "PLAY LOCAL", m_MT20, MCyan);
            if (MenuIconButton(new Rect(r.xMax - 40 * K, r.y + 14 * K, 28 * K, 28 * K), "×", 1100)) { m_MLocalOpen = false; m_MAlpha = oldA; return; }

            float x = r.x + 22 * K, w = r.width - 44 * K, y = r.y + 60 * K;
            if (MenuButton(new Rect(x, y, w, 58 * K), "CREATE SERVER", 1101, MBtn.Primary, false, m_MT20) == MClick.Clicked)
            {
                m_MLocalOpen = false;
                OpenSetup(SetupContext.LocalHost);
            }
            y += 66 * K;
            MText(new Rect(x, y, w, 20 * K), "Your code: " + LocalCode(), m_MT12, MMuted);
            y += 34 * K;

            // separador "OR"
            MFill(new Rect(x, y + 10 * K, w * 0.4f, 1.5f * K), Fade(MCyanDim, 1f));
            MFill(new Rect(x + w * 0.6f, y + 10 * K, w * 0.4f, 1.5f * K), Fade(MCyanDim, 1f));
            MText(new Rect(x, y, w, 22 * K), "OR", m_MT14, MMuted);
            y += 34 * K;

            // Servidores encontrados automaticamente na rede (LanDiscovery): basta clicar.
            MText(new Rect(x, y, w, 20 * K), "SERVERS ON YOUR NETWORK", m_MT14, MWhite, TextAnchor.MiddleLeft);
            y += 26 * K;
            var found = LanDiscovery.Servers;
            if (found.Count == 0)
            {
                int dots = 1 + (int)(Time.unscaledTime * 2f) % 3;
                MText(new Rect(x, y, w, 40 * K), "Searching" + new string('.', dots) + "  Ask the host to press CREATE SERVER.", m_MT12, MMuted, TextAnchor.UpperLeft, true);
                y += 44 * K;
            }
            else
            {
                for (int i = 0; i < found.Count && i < MaxLocalServerRows; i++)
                {
                    var sv = found[i];
                    string label = sv.HostName + " · " + sv.Map + " · " + sv.Mode + " · " + sv.Players + "/" + sv.MaxPlayers;
                    if (!sv.SameVersion) label += " · OTHER BUILD";
                    var kind = sv.IsFull ? MBtn.Muted : (sv.SameVersion ? MBtn.Primary : MBtn.Normal);
                    if (MenuButton(new Rect(x, y, w, 38 * K), label, 1110 + i, kind, false, m_MT12) == MClick.Clicked)
                        JoinDiscoveredServer(sv);
                    y += 44 * K;
                }
            }
            y += 4 * K;

            MText(new Rect(x, y, w, 20 * K), "OR JOIN BY CODE", m_MT14, MWhite, TextAnchor.MiddleLeft);
            y += 26 * K;
            m_Ip = MenuField(new Rect(x, y, w, 46 * K), "mm_code", m_Ip, "Server code (host IP)", 40);
            y += 58 * K;
            if (MenuButton(new Rect(r.x + (r.width - 200 * K) / 2f, y, 200 * K, 48 * K), "CONNECT", 1102, MBtn.Coral, false, m_MT20) == MClick.Clicked)
                TryOpenLocalJoin();
            y += 56 * K;

            string status = m_MLocalError;
            if (string.IsNullOrEmpty(status) && !string.IsNullOrEmpty(ConnectionManager.FirewallMessage)) status = ConnectionManager.FirewallMessage;
            if (string.IsNullOrEmpty(status) && ConnectionManager.Instance != null) status = ConnectionManager.Instance.Status;
            if (!string.IsNullOrEmpty(status))
                MText(new Rect(x - 6 * K, y, w + 12 * K, 40 * K), status, m_MT12, new Color(1f, 0.75f, 0.65f), TextAnchor.UpperCenter, true);
            y += 44 * K;

            // Corrige a causa mais comum: a firewall do Windows a bloquear o jogo (rede "Pública" ou "Cancelar" no aviso).
            if (ConnectionManager.CanFixFirewall)
            {
                string fw = ConnectionManager.FixingFirewall ? "FIXING FIREWALL..." : "FIX FIREWALL";
                if (MenuButton(new Rect(r.x + (r.width - 200 * K) / 2f, y, 200 * K, 34 * K), fw, 1103, MBtn.Normal, false, m_MT12) == MClick.Clicked)
                {
                    m_MLocalError = null;
                    ConnectionManager.FixFirewall();
                }
            }

            m_MAlpha = oldA;
        }

        string LocalCode() => m_LocalIps != null && m_LocalIps.Count > 0 ? m_LocalIps[0] : "127.0.0.1";

        const int MaxLocalServerRows = 3;

        float LocalPanelHeight()
        {
            int rows = Mathf.Clamp(LanDiscovery.Servers.Count, 0, MaxLocalServerRows);
            float list = rows == 0 ? 44f : rows * 44f;
            float firewall = ConnectionManager.CanFixFirewall ? 44f : 0f;
            return (352f + 60f + list + firewall) * K;
        }

        /// <summary>Servidor encontrado na rede: preenche o código e passa à escolha de personagem.</summary>
        void JoinDiscoveredServer(LanDiscovery.ServerInfo sv)
        {
            if (sv.IsFull) { m_MLocalError = sv.HostName + "'s server is full."; return; }
            m_Ip = sv.Code;
            TryOpenLocalJoin();
            if (!sv.SameVersion)
                MenuNotice("Warning: " + sv.HostName + " has a different build of the game. If joining fails, use the same build on both PCs.");
        }

        /// <summary>Valida o código e passa à escolha de personagem (a ligação só acontece no fim).</summary>
        void TryOpenLocalJoin()
        {
            m_MLocalError = null;
            var code = (m_Ip ?? "").Trim();
            if (code.Length == 0) { m_MLocalError = "Enter the server code (host IP, e.g. 192.168.1.20)."; GUI.FocusControl("mm_code"); return; }
            if (!ConnectionManager.TryParseCode(code, out _, out _))
            { m_MLocalError = "Invalid code. Use the code shown on the host's screen."; GUI.FocusControl("mm_code"); return; }
            m_Ip = code;
            m_MLocalOpen = false;
            OpenSetup(SetupContext.LocalJoin);
        }

        void JoinLocal()
        {
            if (NM != null && NM.IsListening) return;
            if (ConnectionManager.Instance == null) { MenuNotice("Network system not found in this scene."); return; }
            var code = (m_Ip ?? "").Trim();
            SaveMenuPrefs();
            ConnectionManager.Instance.Join(code);
        }

        // ================================================================== PLAY ONLINE (Unity Relay)

        float OnlinePanelHeight => 400f * K;

        /// <summary>PLAY ONLINE: criar uma sala na Internet (Relay) ou entrar com o código de um amigo.</summary>
        void DrawMenuOnlinePanel(Rect r)
        {
            float t = EaseOut(m_MOnlineT);
            float oldA = m_MAlpha;
            m_MAlpha *= t;
            r.x -= (1f - t) * 30f * K;
            DrawTechPanel(r, false);

            MText(new Rect(r.x, r.y + 14 * K, r.width, 34 * K), "PLAY ONLINE", m_MT20, MCyan);
            if (MenuIconButton(new Rect(r.xMax - 40 * K, r.y + 14 * K, 28 * K, 28 * K), "×", 1200)) { m_MOnlineOpen = false; m_MAlpha = oldA; return; }

            float x = r.x + 22 * K, w = r.width - 44 * K, y = r.y + 60 * K;
            if (MenuButton(new Rect(x, y, w, 58 * K), "CREATE ROOM", 1201, MBtn.Primary, false, m_MT20) == MClick.Clicked)
            {
                m_MOnlineOpen = false;
                OpenSetup(SetupContext.Online);
            }
            y += 66 * K;
            MText(new Rect(x, y, w, 36 * K), "You host the match. Share the room code with your friends.", m_MT12, MMuted, TextAnchor.UpperCenter, true);
            y += 44 * K;

            MFill(new Rect(x, y + 10 * K, w * 0.4f, 1.5f * K), Fade(MCyanDim, 1f));
            MFill(new Rect(x + w * 0.6f, y + 10 * K, w * 0.4f, 1.5f * K), Fade(MCyanDim, 1f));
            MText(new Rect(x, y, w, 22 * K), "OR", m_MT14, MMuted);
            y += 34 * K;

            MText(new Rect(x, y, w, 20 * K), "JOIN WITH ROOM CODE", m_MT14, MWhite, TextAnchor.MiddleLeft);
            y += 26 * K;
            m_RoomCode = MenuField(new Rect(x, y, w, 46 * K), "mm_room", m_RoomCode, "Room code (e.g. K7Q2XB)", 16);
            y += 58 * K;
            if (MenuButton(new Rect(r.x + (r.width - 200 * K) / 2f, y, 200 * K, 48 * K), "JOIN", 1202, MBtn.Coral, false, m_MT20) == MClick.Clicked)
                TryOpenOnlineJoin();
            y += 56 * K;

            string status = m_MOnlineError;
            if (string.IsNullOrEmpty(status) && ConnectionManager.Instance != null) status = ConnectionManager.Instance.Status;
            if (!string.IsNullOrEmpty(status))
                MText(new Rect(x - 6 * K, y, w + 12 * K, 40 * K), status, m_MT12, new Color(1f, 0.75f, 0.65f), TextAnchor.UpperCenter, true);

            m_MAlpha = oldA;
        }

        void TryOpenOnlineJoin()
        {
            m_MOnlineError = null;
            var code = ConnectionManager.NormalizeRoomCode(m_RoomCode);
            if (code.Length == 0) { m_MOnlineError = "Enter the room code shown on the host's screen."; GUI.FocusControl("mm_room"); return; }
            if (!ConnectionManager.IsValidRoomCode(code)) { m_MOnlineError = "Invalid room code. Check it with the host."; GUI.FocusControl("mm_room"); return; }
            m_RoomCode = code;
            m_MOnlineOpen = false;
            OpenSetup(SetupContext.OnlineJoin);
        }

        void JoinFromSetup()
        {
            if (m_MSetupCtx != SetupContext.OnlineJoin) { JoinLocal(); return; }
            if (NM != null && NM.IsListening) return;
            if (ConnectionManager.Instance == null) { MenuNotice("Network system not found in this scene."); return; }
            SaveMenuPrefs();
            if (!ConnectionManager.Instance.JoinOnline(m_RoomCode)) MenuNotice(ConnectionManager.Instance.Status);
        }

        // ================================================================== ecrã de configuração

        void OpenSetup(SetupContext ctx)
        {
            m_MSetupCtx = ctx;
            if (!IsJoinCtx(ctx) && MatchManager.MenuSelectedMode == GameMode.ItTakesTwo)
                SelectMode(Mathf.Max(0, m_MModeSel), false);
            m_MScreen = MenuScreen.Setup;
            m_MScreenT = 0f;
            m_MAudioOpen = false;
            GUI.FocusControl(null);
            if (!IsJoinCtx(ctx)) FocusMapIsland(m_MMapSel);
            else FocusOverview();
        }

        void BackToMain()
        {
            m_MLocalOpen = m_MSetupCtx == SetupContext.LocalHost || m_MSetupCtx == SetupContext.LocalJoin;
            m_MOnlineOpen = m_MSetupCtx == SetupContext.OnlineJoin;
            m_MScreen = MenuScreen.Main;
            m_MScreenT = 0f;
            GUI.FocusControl(null);
            FocusOverview();
        }

        void DrawMenuSetupScreen(float ease)
        {
            var ctx = m_MSetupCtx;
            bool online = !IsJoinCtx(ctx);   // online e criar servidor local: modo · mapa · personagem
            bool join = IsJoinCtx(ctx);
            float slide = (1f - ease) * 30f * K;
            int step = 1;

            // ---------------- 1. modo (esquerda) ou dados do servidor (ao ligar)
            var title = VL(58, 64, 420, 56);
            title.x -= slide;
            if (!join)
            {
                MText(title, (step++) + "  GAME MODE", m_MT40, MWhite, TextAnchor.MiddleLeft);
                DrawTechLine(VL(58, 124, 360, 2), false);
                float modeStep = s_Modes.Length > 3 ? 54f : 70f, modeH = s_Modes.Length > 3 ? 46f : 58f;
                for (int i = 0; i < s_Modes.Length; i++)
                {
                    var r = VL(83, 150 + i * modeStep, 324, modeH);
                    r.x -= slide;
                    bool sel = m_MModeSel == i;
                    var res = MenuButton(r, s_Modes[i].title, 2000 + i, MBtn.Normal, sel, m_MT20);
                    if (res == MClick.Clicked) SelectMode(i, true);
                }
                var mode = s_Modes[Mathf.Clamp(m_MModeSel, 0, s_Modes.Length - 1)];
                MText(VL(83, 150 + s_Modes.Length * modeStep + 2, 324, 24), mode.subtitle, m_MT14, MMuted, TextAnchor.MiddleLeft);
            }
            else
            {
                MText(title, "SERVER", m_MT40, MWhite, TextAnchor.MiddleLeft);
                DrawTechLine(VL(58, 124, 360, 2), false);
                var info = VL(83, 150, 324, 124);
                DrawTechPanel(info, false);
                MText(new Rect(info.x + 18 * K, info.y + 14 * K, info.width - 36 * K, 20 * K), "CODE", m_MT12, MMuted, TextAnchor.MiddleLeft);
                MText(new Rect(info.x + 18 * K, info.y + 34 * K, info.width - 36 * K, 30 * K), m_Ip, m_MT24, MCyan, TextAnchor.MiddleLeft);
                MText(new Rect(info.x + 18 * K, info.y + 74 * K, info.width - 36 * K, 60 * K),
                    "The host selects the mode and map. Choose your character here.", m_MT12, MMuted, TextAnchor.UpperLeft, true);
            }

            if (!join)
            {
                bool competitive = MatchManager.MenuSelectedCompetitive && !MenuCoopSetup;
                var ranked = VL(83, 406, 324, 40);
                if (MenuButton(ranked, competitive ? "COMPETITIVE" : "CASUAL", 2098, MBtn.Normal, competitive, m_MT14) == MClick.Clicked && !MenuCoopSetup)
                {
                    MatchManager.MenuSelectedCompetitive = !competitive;
                    PlayerPrefs.SetInt("fpslan.competitive", !competitive ? 1 : 0);
                }
            }

            // ---------------- personagens (direita)
            var charPanel = VR(1051, 64, 367, 472);
            charPanel.x += slide;
            DrawTechPanel(charPanel, false);
            int charStep = online ? 3 : (join ? 1 : 2);
            MText(new Rect(charPanel.x, charPanel.y + 10 * K, charPanel.width, 36 * K), charStep + "  CHARACTER", m_MT24, MWhite);
            DrawTechLine(new Rect(charPanel.x + 40 * K, charPanel.y + 48 * K, charPanel.width - 80 * K, 2 * K), false);
            int selChar = Mathf.Max(0, MenuCharIndexForHero(m_Hero));
            float cw = (charPanel.width - 30 * K) / 2f, ch = (charPanel.height - 66 * K) / 3f;
            for (int i = 0; i < s_Roster.Length; i++)
            {
                var cell = new Rect(charPanel.x + 15 * K + (i % 2) * cw, charPanel.y + 60 * K + (i / 2) * ch, cw, ch);
                if (DrawCharacterCell(cell, i, i == selChar)) SelectMenuCharacter(i);
            }

            // ---------------- personagem em destaque (centro)
            float leftEdge = X(407), rightEdge = charPanel.x;
            float stageTop = online ? 14f : 40f, stageH = online ? 514f : 720f;
            var stage = new Rect(leftEdge + 10 * K, Y(stageTop), rightEdge - leftEdge - 20 * K, stageH * K);
            m_BgAnchor = new Vector2(stage.center.x, stage.y + stage.height * 0.5f);
            bool coop = !join && s_Modes[Mathf.Clamp(m_MModeSel, 0, s_Modes.Length - 1)].title == "IT TAKES TWO";
            DrawCharacterStage(stage, selChar, coop);

            // ---------------- 2. mapa (online e criar servidor local)
            var actions = VR(1051, 579, 367, 196);
            if (!join)
            {
                MText(VL(58, 528, 300, 40), "2  MAP", m_MT30, MWhite, TextAnchor.MiddleLeft);
                DrawTechLine(VL(58, 568, 380, 2), false);
                var strip = new Rect(X(58), Y(586), actions.x - X(58) - 24 * K, 176 * K);
                DrawMapCarousel(strip);
            }

            // ---------------- ações (canto inferior direito)
            actions.x += slide;
            DrawSetupActions(actions, ctx);

            if (MenuButton(new Rect(Screen.width - 250 * K, 14 * K, 150 * K, 40 * K), "‹  BACK", 2900, MBtn.Normal, false, m_MT17) == MClick.Clicked)
                BackToMain();
        }

        void SelectMode(int i, bool notify)
        {
            if (TryResolveMode(s_Modes[i], out var gm))
            {
                m_MModeSel = i;
                MatchManager.MenuSelectedMode = gm;
                PlayerPrefs.SetInt("fpslan.mode", (int)gm);
                if (gm == GameMode.ItTakesTwo)
                {
                    SelectMap(1);
                    if (!MatchManager.IsCoopStoryHero(BrawlerRegistry.Get(m_Hero))) SelectMenuCharacter(0, false);
                }
            }
            else
            {
                // modo ainda não existe: mostra a mensagem e mantém o modo jogável escolhido
                // (o IT TAKES TWO fica selecionado para se ver a pré-visualização a dois)
                if (s_Modes[i].title == "IT TAKES TWO") m_MModeSel = i;
                if (notify) MenuNotice(s_Modes[i].unavailableMessage);
            }
        }

        bool DrawCharacterCell(Rect cell, int i, bool selected)
        {
            var e = Event.current;
            bool hover = GUI.enabled && cell.Contains(e.mousePosition);
            float h = HoverAnim(3000 + i, hover || selected ? 1f : 0f);
            var portrait = MenuTexture("portrait_" + s_Roster[i].key);
            float size = Mathf.Min(cell.width - 10 * K, cell.height - 26 * K) * (0.9f + 0.08f * h);
            var pr = new Rect(cell.center.x - size / 2f, cell.y + (cell.height - 24 * K - size) / 2f, size, size);
            if (selected) MDraw9(m_MGlow, Expand(pr, 12 * K), Fade(MCyan, 0.5f));
            MDraw9(m_MFill, pr, Fade(new Color(0.08f, 0.14f, 0.26f, 1f), 1f));
            if (portrait != null) MTex(Expand(pr, -3 * K), portrait, Color.white, ScaleMode.ScaleAndCrop);
            if (selected) MDraw9(m_MLine, pr, Fade(MCyan, 1f));
            else MDraw9(m_MLine, pr, Fade(MCyanDim, 0.25f + 0.6f * h));
            bool available = MenuCharacterBrawler(i) != null && (!MenuCoopSetup || i < 2);
            MText(new Rect(cell.x - 10 * K, pr.yMax + 2 * K, cell.width + 20 * K, 22 * K), s_Roster[i].name, m_MT14,
                selected ? MCyan : (available ? MWhite : MMuted));
            if (GUI.enabled && GUI.Button(cell, GUIContent.none, GUIStyle.none))
            {
                if (!available) { MenuNotice(MenuCoopSetup ? "This mission features Lince and Riot." : s_Roster[i].name + " is not available yet."); return false; }
                return true;
            }
            return false;
        }

        void DrawCharacterStage(Rect stage, int selChar, bool coopPreview)
        {
            var b = MenuCharacterBrawler(selChar);
            // durante a viagem até à ilha o personagem esbate-se para o fundo ganhar protagonismo
            float reveal = m_BgT < 1f && m_BgMap >= 0 ? 1f - 0.8f * Mathf.Sin(Mathf.Clamp01(m_BgT * 1.1f) * Mathf.PI) : 1f;
            float oldA = m_MAlpha;
            m_MAlpha *= reveal;
            DrawCharacterStageInner(stage, selChar, coopPreview, b);
            m_MAlpha = oldA;
        }

        void DrawCharacterStageInner(Rect stage, int selChar, bool coopPreview, BrawlerDefinition b)
        {
            MFill(new Rect(stage.x + 12*K, stage.yMax - 108*K, stage.width - 24*K, 122*K), new Color(0.02f, 0.05f, 0.1f, 0.65f));
            if (coopPreview)
            {
                int second = selChar == 0 ? 1 : 0;
                float half = stage.width / 2f;
                DrawCharacterArt(new Rect(stage.x, stage.y + 34 * K, half, stage.height - 108 * K), selChar, 1f);
                DrawCharacterArt(new Rect(stage.x + half, stage.y + 34 * K, half, stage.height - 108 * K), second, 1f);
                MText(new Rect(stage.x, stage.y, half, 30 * K), "1", m_MT24, MWhite);
                MText(new Rect(stage.x + half, stage.y, half, 30 * K), "2", m_MT24, MWhite);
                MText(new Rect(stage.x, stage.yMax - 60 * K, stage.width, 26 * K), MatchManager.CoopMissionTitle, m_MT17, MCyan);
                if (b != null) MText(new Rect(stage.x + 28 * K, stage.yMax - 32 * K, stage.width - 56 * K, 44 * K), CharacterStories.Summary(b.brawlerId), m_MT12, MWhite, TextAnchor.UpperCenter, true);
                return;
            }

            float t = EaseOut(m_MCharT);
            var art = new Rect(stage.x, stage.y, stage.width, stage.height - 102 * K);
            if (m_MCharPrev >= 0 && m_MCharPrev != selChar && t < 1f)
                DrawCharacterArt(art, m_MCharPrev, 1f - t);
            DrawCharacterArt(new Rect(art.x, art.y + (1f - t) * 20f * K, art.width, art.height), selChar, t);

            MText(new Rect(stage.x, stage.yMax - 100 * K, stage.width, 42 * K), s_Roster[selChar].name, m_MT40, MWhite);
            if (b != null)
            {
                MText(new Rect(stage.x, stage.yMax - 60 * K, stage.width, 22 * K), CharacterStories.Role(b.brawlerId), m_MT14, MCyan);
                MText(new Rect(stage.x + 28 * K, stage.yMax - 34 * K, stage.width - 56 * K, 48 * K), CharacterStories.Summary(b.brawlerId), m_MT12, MWhite, TextAnchor.UpperCenter, true);
            }
        }

        void DrawCharacterArt(Rect r, int index, float alpha)
        {
            var tex = MenuTexture("char_" + s_Roster[index].key);
            if (tex == null || alpha <= 0.001f) return;
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f + index) * 4f * K;
            var rr = new Rect(r.x, r.y + bob, r.width, r.height);
            MTex(Expand(rr, 8 * K), tex, new Color(MCyan.r, MCyan.g, MCyan.b, 0.2f * alpha), ScaleMode.ScaleToFit);
            MTex(rr, tex, new Color(1f, 1f, 1f, alpha), ScaleMode.ScaleToFit);
        }

        void DrawMapCarousel(Rect strip)
        {
            float cardW = 250 * K, gap = 20 * K, cardH = strip.height;
            float total = s_Maps.Length * cardW + (s_Maps.Length - 1) * gap;
            float maxScroll = Mathf.Max(0f, total - strip.width);
            m_MMapScrollTarget = Mathf.Clamp(m_MMapScrollTarget, 0f, maxScroll);

            var e = Event.current;
            if (e.type == EventType.ScrollWheel && strip.Contains(e.mousePosition))
            {
                m_MMapScrollTarget = Mathf.Clamp(m_MMapScrollTarget + e.delta.y * 40f * K, 0f, maxScroll);
                e.Use();
            }

            GUI.BeginGroup(strip);
            for (int i = 0; i < s_Maps.Length; i++)
            {
                var r = new Rect(i * (cardW + gap) - m_MMapScroll, 0, cardW, cardH);
                if (r.xMax < 0 || r.x > strip.width) continue;
                if (DrawMapCard(r, i)) SelectMap(i);
            }
            GUI.EndGroup();

            if (maxScroll > 1f)
            {
                if (m_MMapScrollTarget > 1f &&
                    MenuIconButton(new Rect(strip.x - 6 * K, strip.center.y - 22 * K, 36 * K, 44 * K), "‹", 4100))
                    m_MMapScrollTarget = Mathf.Max(0f, m_MMapScrollTarget - (cardW + gap));
                if (m_MMapScrollTarget < maxScroll - 1f &&
                    MenuIconButton(new Rect(strip.xMax - 30 * K, strip.center.y - 22 * K, 36 * K, 44 * K), "›", 4101))
                    m_MMapScrollTarget = Mathf.Min(maxScroll, m_MMapScrollTarget + (cardW + gap));
            }
        }

        /// <summary>Cartão de mapa só com a imagem (sem nome): o jogador descobre a ilha pela aproximação do fundo.</summary>
        bool DrawMapCard(Rect r, int i)
        {
            var m = s_Maps[i];
            var e = Event.current;
            bool sel = m_MMapSel == i;
            bool hover = GUI.enabled && r.Contains(e.mousePosition);
            float h = HoverAnim(4000 + i, hover || sel ? 1f : 0f);
            var frame = Expand(r, -4 * K * (1f - h));
            if (sel) MDraw9(m_MGlow, Expand(frame, 10 * K), Fade(MCyan, 0.45f));
            MDraw9(m_MFill, frame, Fade(new Color(0.1f, 0.16f, 0.28f, 0.95f), 1f));
            var img = MenuTexture("map_" + m.key);
            if (img != null) MTex(Expand(frame, -8 * K), img, Color.white, ScaleMode.ScaleAndCrop);
            if (sel)
            {
                // marcador discreto no canto (sem texto)
                var dot = new Rect(frame.xMax - 26 * K, frame.y + 12 * K, 14 * K, 14 * K);
                MTex(Expand(dot, 4 * K), Art.CircleTexture, Fade(new Color(0f, 0f, 0f, 0.5f), 1f), ScaleMode.StretchToFill);
                MTex(dot, Art.CircleTexture, Fade(MCyan, 1f), ScaleMode.StretchToFill);
            }
            MDraw9(m_MLine, frame, Fade(sel ? MCyan : MCyanDim, sel ? 1f : 0.35f + 0.5f * h));
            if (MenuCoopSetup && m.key != "santiago")
            {
                MDraw9(m_MFill, frame, Fade(new Color(0f, 0f, 0f, 0.65f), 1f));
                MText(frame, "COMING SOON", m_MT14, MMuted);
            }

            if (GUI.enabled && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                FocusMapIsland(i);   // a aproximação acontece sempre: mostra de que ilha se trata
                if (MenuCoopSetup && m.key != "santiago") { MenuNotice("First mission: The Pelourinho Heist. More missions are coming later."); m_BgReturnAt = Time.unscaledTime + BgTravelTime + 1.6f; return false; }
                if (!m.playable) { PeekUnavailableMap(); return false; }
                return true;
            }
            return false;
        }

        /// <summary>Mapa ainda fechado: mostra a ilha, avisa e depois a câmara regressa ao mapa escolhido.</summary>
        void PeekUnavailableMap()
        {
            MenuNotice("This map is not available yet.");
            m_BgReturnAt = Time.unscaledTime + BgTravelTime + 1.6f;
        }

        void SelectMap(int i)
        {
            m_BgReturnAt = 0f;
            m_MMapSel = i;
            PlayerPrefs.SetString(KeyMenuMap, s_Maps[i].key);
        }

        void DrawSetupActions(Rect r, SetupContext ctx)
        {
            DrawTechPanel(r, false);
            float x = r.x + 18 * K, w = r.width - 36 * K, y = r.y + 12 * K;

            if (IsJoinCtx(ctx))
            {
                MText(new Rect(x, y, w, 20 * K), "READY TO JOIN", m_MT12, MMuted, TextAnchor.MiddleLeft);
                MText(new Rect(x, y + 18 * K, w, 28 * K), ctx == SetupContext.OnlineJoin ? "Online room " + m_RoomCode : "Server " + m_Ip, m_MT17, MCyan, TextAnchor.MiddleLeft);
                y += 70 * K;
                if (MenuButton(new Rect(x, y, w, 64 * K), "JOIN MATCH", 5000, MBtn.Coral, false, m_MT20) == MClick.Clicked)
                    JoinFromSetup();
                return;
            }

            if (ctx == SetupContext.LocalHost)
            {
                MText(new Rect(x, y, w, 20 * K), "SERVER CODE", m_MT12, MMuted, TextAnchor.MiddleLeft);
                MText(new Rect(x, y + 16 * K, w, 26 * K), LocalCode() + "  ·  UDP " + ConnectionManager.DefaultPort, m_MT17, MCyan, TextAnchor.MiddleLeft);
            }
            else
            {
                MText(new Rect(x, y, w, 20 * K), "MATCH", m_MT12, MMuted, TextAnchor.MiddleLeft);
                MText(new Rect(x, y + 16 * K, w, 26 * K), "Online room · code appears in game (Esc)", m_MT14, MCyan, TextAnchor.MiddleLeft);
            }
            y += 48 * K;

            // O cooperativo gere o parceiro e os guardas automaticamente.
            if (MenuCoopSetup)
            {
                MText(new Rect(x, y, w, 42 * K), "LINCE & RIOT\nAI partner solo · 2 players over LAN", m_MT14, MCyan, TextAnchor.MiddleLeft, true);
                y += 46 * K;
            }
            else
            {
            // bots
            MText(new Rect(x, y, 70 * K, 34 * K), "BOTS", m_MT14, MWhite, TextAnchor.MiddleLeft);
            if (MenuButton(new Rect(x + 60 * K, y, 34 * K, 34 * K), "−", 5001, MBtn.Normal, false, m_MT20) == MClick.Clicked)
                m_MenuBots = Mathf.Max(0, m_MenuBots - 1);
            MText(new Rect(x + 96 * K, y, 34 * K, 34 * K), m_MenuBots.ToString(), m_MT20, MWhite);
            if (MenuButton(new Rect(x + 132 * K, y, 34 * K, 34 * K), "+", 5002, MBtn.Normal, false, m_MT20) == MClick.Clicked)
                m_MenuBots = Mathf.Min(BotManager.MaxTotalPlayers - 1, m_MenuBots + 1);
            if (MenuButton(new Rect(x + 176 * K, y, w - 176 * K, 34 * K), BotManager.DifficultyLabel(BotManager.Difficulty).ToUpperInvariant(), 5003, MBtn.Normal, false, m_MT14) == MClick.Clicked)
            {
                BotManager.Difficulty = (BotDifficulty)(((int)BotManager.Difficulty + 1) % 3);
                PlayerPrefs.SetInt("fpslan.botdiff", (int)BotManager.Difficulty);
                BotManager.ApplyDifficulty();
            }
            y += 46 * K;
            }

            string label = ctx == SetupContext.Online ? "CREATE ROOM" : "CREATE SERVER";
            if (MenuButton(new Rect(x, y, w, 58 * K), label, 5004, MBtn.Primary, false, m_MT20) == MClick.Clicked)
                TryStartFromSetup(ctx);
        }

        void TryStartFromSetup(SetupContext ctx)
        {
            if (m_MModeSel < 0 || !TryResolveMode(s_Modes[m_MModeSel], out _))
            {
                MenuNotice(m_MModeSel >= 0 && s_Modes[m_MModeSel].unavailableMessage != null
                    ? s_Modes[m_MModeSel].unavailableMessage + " Choose another mode."
                    : "Choose a game mode.");
                return;
            }
            if (!IsJoinCtx(ctx) && !s_Maps[m_MMapSel].playable)
            {
                MenuNotice("This map is not available yet. Choose another.");
                return;
            }
            // Online: a partida corre neste PC (host) e os amigos entram pelo Unity Relay com o código da sala.
            StartHost(m_MenuBots, ctx == SetupContext.Online);
        }

        void StartHost(int bots, bool online = false)
        {
            if (NM != null && NM.IsListening) return;
            if (ConnectionManager.Instance == null) { MenuNotice("Network system not found in this scene."); return; }
            if (MatchManager.MenuSelectedMode == GameMode.GemGrab && !ConnectionManager.SupportsGemGrab(SelectedMapKey))
            { MenuNotice("GEM RUSH is available in Santiago, Sal and Fogo. Choose one of these islands."); return; }
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo) MatchManager.MenuSelectedCompetitive = false;
            SaveMenuPrefs();
            if (MatchManager.MenuSelectedMode == GameMode.ItTakesTwo)
            {
                SelectMap(1);
                bots = 0;
                if (!MatchManager.IsCoopStoryHero(BrawlerRegistry.Get(m_Hero)))
                { m_Hero = MatchManager.CoopHeroIndex("sniper"); GameSession.LocalHeroId = m_Hero; }
            }
            if (ConnectionManager.Instance.HostMap(SelectedMapKey, ConnectionManager.DefaultPort, online)) m_PendingBots = bots;
            else MenuNotice(ConnectionManager.Instance.Status);
        }

        static void QuitGame()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;   // no Editor: sai do Play em vez de fechar o Unity
#else
            Application.Quit();
#endif
        }

        // ================================================================== canto superior direito: ⋮ + áudio

        void DrawMenuTopRight()
        {
            var dots = new Rect(Screen.width - 54 * K, 20 * K, 30 * K, 50 * K);
            var e = Event.current;
            bool hover = GUI.enabled && dots.Contains(e.mousePosition);
            float h = HoverAnim(6000, hover || m_MAudioOpen ? 1f : 0f);
            MDraw9(m_MFill, dots, Fade(Color.Lerp(MGlass, MButtonHover, h), 1f));
            MDraw9(m_MLine, dots, Fade(MCyanDim, 0.4f + 0.6f * h));
            for (int i = 0; i < 3; i++)
                MTex(new Rect(dots.center.x - 3.5f * K, dots.y + 10 * K + i * 12 * K, 7 * K, 7 * K), Art.CircleTexture, MWhite, ScaleMode.StretchToFill);
            if (GUI.enabled && GUI.Button(dots, GUIContent.none, GUIStyle.none)) m_MAudioOpen = !m_MAudioOpen;

            if (m_MAudioT > 0.001f) DrawAudioQuickPanel();
        }

        void DrawAudioQuickPanel()
        {
            float t = EaseOut(m_MAudioT);
            float oldA = m_MAlpha;
            m_MAlpha = t;

            var row = new Rect(Screen.width - 250 * K, 22 * K + (1f - t) * -10f * K, 186 * K, 52 * K);
            DrawSolidPanel(row);
            bool muted = GameSession.Muted;
            if (MenuSpeakerButton(new Rect(row.x + 10 * K, row.y + 6 * K, 52 * K, 40 * K), true, muted, 6100))
                GameSession.Muted = !muted;
            if (MenuSpeakerButton(new Rect(row.x + 66 * K, row.y + 6 * K, 52 * K, 40 * K), false, false, 6101, "-"))
                SetMenuVolume(GameSession.MasterVolume - 0.1f);
            if (MenuSpeakerButton(new Rect(row.x + 122 * K, row.y + 6 * K, 52 * K, 40 * K), false, false, 6102, "+"))
                SetMenuVolume(GameSession.MasterVolume + 0.1f);

            var pill = new Rect(Screen.width - 104 * K, 96 * K + (1f - t) * -14f * K, 64 * K, 270 * K);
            DrawSolidPanel(pill);
            var track = new Rect(pill.center.x - 14 * K, pill.y + 18 * K, 28 * K, pill.height - 90 * K);
            float v = muted ? 0f : GameSession.MasterVolume;
            float nv = MenuVerticalSlider(track, v, 6103);
            if (!Mathf.Approximately(nv, v)) SetMenuVolume(nv);
            MText(new Rect(pill.x, track.yMax + 6 * K, pill.width, 22 * K), Mathf.RoundToInt(v * 100f) + "%", m_MT14, muted ? MCoral : MWhite);
            MText(new Rect(pill.x, track.yMax + 26 * K, pill.width, 20 * K), muted ? "MUTE" : "VOLUME", m_MT12, MMuted);

            // perfil, teclas, sensibilidade…
            if (MenuButton(new Rect(Screen.width - 250 * K, pill.yMax + 12 * K, 210 * K, 40 * K), "SETTINGS", 6104, MBtn.Normal, false, m_MT14) == MClick.Clicked)
            {
                m_MAudioOpen = false;
                OpenSettings();
            }
            m_MAlpha = oldA;
        }

        static void SetMenuVolume(float v)
        {
            v = Mathf.Clamp01(v);
            GameSession.MasterVolume = v;
            GameSession.Muted = v <= 0.001f;
        }

        // ================================================================== avisos e ligação

        void MenuNotice(string text)
        {
            m_MNotice = text;
            m_MNoticeTime = Time.unscaledTime;
        }

        void DrawMenuNotice()
        {
            if (string.IsNullOrEmpty(m_MNotice)) return;
            float age = Time.unscaledTime - m_MNoticeTime;
            if (age > 3.2f) return;
            float a = Mathf.Clamp01(age * 6f) * Mathf.Clamp01((3.2f - age) * 2f);
            float oldA = m_MAlpha; m_MAlpha = a;
            var r = new Rect((Screen.width - 640 * K) / 2f, Screen.height - 74 * K + (1f - a) * 12f * K, 640 * K, 46 * K);
            MDraw9(m_MFill, r, Fade(new Color(0.06f, 0.1f, 0.2f, 0.95f), 1f));
            MDraw9(m_MLine, r, Fade(MCoral, 0.9f));
            MText(r, m_MNotice, m_MT14, MWhite);
            m_MAlpha = oldA;
        }

        void DrawMenuConnecting()
        {
            MFill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.01f, 0.03f, 0.07f, 0.72f));
            var r = new Rect((Screen.width - 460 * K) / 2f, (Screen.height - 220 * K) / 2f, 460 * K, 220 * K);
            DrawTechPanel(r, true);
            bool server = NM != null && NM.IsServer;
            MText(new Rect(r.x, r.y + 26 * K, r.width, 36 * K), server ? "PREPARING MATCH" : "CONNECTING TO SERVER", m_MT24, MWhite);
            for (int i = 0; i < 5; i++)
            {
                float p = Mathf.Repeat(Time.unscaledTime * 1.4f - i * 0.18f, 1f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(p - 0.5f) * 2.5f);
                MTex(new Rect(r.center.x - 62 * K + i * 26 * K, r.y + 80 * K, 14 * K, 14 * K), Art.CircleTexture, new Color(MCyan.r, MCyan.g, MCyan.b, 0.25f + 0.75f * a), ScaleMode.StretchToFill);
            }
            var status = ConnectionManager.Instance != null ? ConnectionManager.Instance.Status : "";
            if (string.IsNullOrEmpty(status) && !server) status = "Connecting to " + m_Ip + "...";
            MText(new Rect(r.x + 20 * K, r.y + 104 * K, r.width - 40 * K, 40 * K), status, m_MT14, MMuted, TextAnchor.MiddleCenter, true);
            if (MenuButton(new Rect(r.center.x - 90 * K, r.yMax - 64 * K, 180 * K, 46 * K), "CANCEL", 7000, MBtn.Coral, false, m_MT17) == MClick.Clicked)
                ConnectionManager.Instance?.Leave();
        }

        // ================================================================== teclado

        void HandleMenuKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            bool typing = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());

            if (e.keyCode == KeyCode.Escape)
            {
                if (m_MAudioOpen) m_MAudioOpen = false;
                else if (m_MScreen == MenuScreen.Setup) BackToMain();
                else if (m_MLocalOpen) m_MLocalOpen = false;
                else if (m_MOnlineOpen) m_MOnlineOpen = false;
                GUI.FocusControl(null);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (m_MScreen == MenuScreen.Main)
                {
                    if (m_MLocalOpen && typing) TryOpenLocalJoin();
                    else if (m_MOnlineOpen && typing) TryOpenOnlineJoin();
                    else if (!typing) ActivateMainItem(m_MFocus);
                }
                else if (!typing)
                {
                    if (IsJoinCtx(m_MSetupCtx)) JoinFromSetup();
                    else TryStartFromSetup(m_MSetupCtx);
                }
                e.Use();
                return;
            }

            if (typing) return;
            if (m_MScreen == MenuScreen.Main)
            {
                if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S) { m_MFocus = (m_MFocus + 1) % 3; e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W) { m_MFocus = (m_MFocus + 2) % 3; e.Use(); }
                return;
            }

            int c = Mathf.Max(0, MenuCharIndexForHero(m_Hero));
            bool modes = !IsJoinCtx(m_MSetupCtx);
            int characterCount = MenuCoopSetup ? 2 : s_Roster.Length;
            if (e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.D) { SelectMenuCharacter((c + 1) % characterCount); e.Use(); }
            else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.A) { SelectMenuCharacter((c + characterCount - 1) % characterCount); e.Use(); }
            else if (modes && (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S))
            { SelectMode((Mathf.Max(0, m_MModeSel) + 1) % s_Modes.Length, true); e.Use(); }
            else if (modes && (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W))
            { SelectMode((Mathf.Max(0, m_MModeSel) + s_Modes.Length - 1) % s_Modes.Length, true); e.Use(); }
            else if (!IsJoinCtx(m_MSetupCtx) && (e.keyCode == KeyCode.E || e.keyCode == KeyCode.Q))
            {
                int n = s_Maps.Length, next = (m_MMapSel + (e.keyCode == KeyCode.E ? 1 : n - 1)) % n;
                FocusMapIsland(next);
                if (MenuCoopSetup) { SelectMap(1); FocusMapIsland(1); }
                else if (s_Maps[next].playable) SelectMap(next); else PeekUnavailableMap();
                e.Use();
            }
        }

        // ================================================================== personagens ↔ brawlers

        static BrawlerDefinition MenuCharacterBrawler(int menuIndex)
        {
            if (menuIndex < 0 || menuIndex >= s_Roster.Length) return null;
            var id = s_Roster[menuIndex].brawlerId;
            foreach (var b in BrawlerRegistry.All)
                if (b != null && b.available && b.brawlerId == id) return b;
            return null;
        }

        static int MenuCharIndexForHero(int heroIndex)
        {
            var hero = BrawlerRegistry.Get(heroIndex);
            if (hero == null) return -1;
            for (int i = 0; i < s_Roster.Length; i++)
                if (s_Roster[i].brawlerId == hero.brawlerId) return i;
            return -1;
        }

        void SelectMenuCharacter(int menuIndex, bool voice = true)
        {
            if (MenuCoopSetup && menuIndex >= 2) return;
            var b = MenuCharacterBrawler(menuIndex);
            if (b == null) return;
            int prev = MenuCharIndexForHero(m_Hero);
            if (prev == menuIndex) return;
            m_MCharPrev = prev;
            m_MCharT = 0f;
            m_Hero = b.runtimeIndex;
            GameSession.LocalHeroId = m_Hero;
            if (voice) MenuAudio.PlayVoice(s_Roster[menuIndex].key);   // fala do personagem
        }

        static bool TryResolveMode(MenuMode m, out GameMode mode)
        {
            if (m.enumNames != null)
                foreach (var n in m.enumNames)
                    if (System.Enum.IsDefined(typeof(GameMode), n))
                    {
                        mode = (GameMode)System.Enum.Parse(typeof(GameMode), n);
                        return true;
                    }
            mode = default;
            return false;
        }

        static int FirstPlayableMode()
        {
            for (int i = 0; i < s_Modes.Length; i++) if (TryResolveMode(s_Modes[i], out _)) return i;
            return 0;
        }

        // ================================================================== fundo: câmara sobre o arquipélago

        const float BgOverviewZoom = 1.06f;
        const float BgTravelTime = 1.9f;

        /// <summary>Volta à vista geral do arquipélago.</summary>
        void FocusOverview()
        {
            m_BgReturnAt = 0f;
            StartBgTravel(new Vector2(0.5f, 0.5f), BgOverviewZoom, -1);
        }

        /// <summary>Viaja até à ilha do mapa <paramref name="i"/> (sem mostrar o nome dela).</summary>
        void FocusMapIsland(int i)
        {
            if (i < 0 || i >= s_Maps.Length) return;
            StartBgTravel(s_Maps[i].focus, s_Maps[i].zoom, i);
        }

        void StartBgTravel(Vector2 focus, float zoom, int map)
        {
            if (map == m_BgMap && (m_BgTo - focus).sqrMagnitude < 1e-6f) return;
            m_BgFrom = m_BgCur;
            m_BgZFrom = m_BgZCur;
            m_BgTo = focus;
            m_BgZTo = zoom;
            m_BgMap = map;
            m_BgT = 0f;
            m_BgAWFrom = m_BgAW;
            m_BgAWTo = map >= 0 ? 1f : 0f;
            // salto entre duas ilhas: a câmara afasta-se um pouco a meio do caminho (efeito de voo)
            float dist = (focus - m_BgFrom).magnitude;
            m_BgArc = Mathf.Clamp01(dist * 2.2f) * Mathf.Min(m_BgZFrom, zoom) * 0.38f;
        }

        void UpdateBackgroundCamera(float dt)
        {
            if (m_BgReturnAt > 0f && Time.unscaledTime >= m_BgReturnAt)
            {
                m_BgReturnAt = 0f;
                if (m_MScreen == MenuScreen.Setup && !IsJoinCtx(m_MSetupCtx)) FocusMapIsland(m_MMapSel);
            }
            if (m_BgT < 1f) m_BgT = Mathf.Min(1f, m_BgT + dt / BgTravelTime);
            float t = m_BgT;
            float e = BgTravelEase(t);
            m_BgCur = Vector2.Lerp(m_BgFrom, m_BgTo, e);
            m_BgAW = Mathf.Lerp(m_BgAWFrom, m_BgAWTo, e);
            // zoom em escala logarítmica (aproximação uniforme) menos o "arco" do voo
            float z = BgTravelZoom(m_BgZFrom, m_BgZTo, t);
            m_BgZCur = Mathf.Max(1f, z - m_BgArc * Mathf.Sin(t * Mathf.PI));
        }

        // A intro e a seleção de mapas usam a mesma aceleração e o mesmo zoom logarítmico.
        static float BgTravelEase(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        static float BgTravelZoom(float from, float to, float t) =>
            Mathf.Exp(Mathf.Lerp(Mathf.Log(from), Mathf.Log(to),
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.15f - 0.1f))));

        /// <summary>Retângulo UV visível para um foco/zoom (cobre o ecrã, sem sair da imagem).</summary>
        /// <param name="anchorOffset">Onde o foco deve aparecer, em píxeis a partir do centro do ecrã.</param>
        static Rect BgView(Rect screen, Texture t, Vector2 focus, float zoom, Vector2 anchorOffset)
        {
            float sa = screen.width / Mathf.Max(1f, screen.height), ta = t.width / (float)Mathf.Max(1, t.height);
            float uw = 1f, uh = 1f;
            if (sa > ta) uh = ta / sa; else uw = sa / ta;
            uw /= zoom; uh /= zoom;
            float fx = focus.x - anchorOffset.x / Mathf.Max(1f, screen.width) * uw;
            float fy = focus.y + anchorOffset.y / Mathf.Max(1f, screen.height) * uh;
            float cx = Mathf.Clamp(fx, uw / 2f, 1f - uw / 2f);
            float cy = Mathf.Clamp(fy, uh / 2f, 1f - uh / 2f);
            return new Rect(cx - uw / 2f, cy - uh / 2f, uw, uh);
        }

        static Vector2 UVToScreen(Rect view, Vector2 uv) =>
            new Vector2((uv.x - view.x) / view.width * Screen.width, (1f - (uv.y - view.y) / view.height) * Screen.height);

        void DrawMenuBackground()
        {
            if (Event.current.type != EventType.Repaint) return;
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var old = GUI.color;
            GUI.color = MDeep;
            GUI.DrawTexture(screen, Texture2D.whiteTexture);

            float time = Time.unscaledTime;
            float travel = m_BgT < 1f ? Mathf.Sin(m_BgT * Mathf.PI) : 0f;   // 0 parado · 1 a meio do voo
            var bg = MenuTexture("bg_archipelago");
            Rect view = new Rect(0, 0, 1, 1);
            if (bg != null)
            {
                // deriva lenta e "respiração" por cima da câmara
                var drift = new Vector2(Mathf.Sin(time * 0.031f) * 0.012f, Mathf.Cos(time * 0.027f) * 0.01f) / m_BgZCur;
                float breathe = 1f + 0.02f * Mathf.Sin(time * 0.05f);
                var anchor = (m_BgAnchor - screen.center) * m_BgAW;
                view = BgView(screen, bg, m_BgCur + drift, m_BgZCur * breathe, anchor);
                float focusTint = m_BgMap >= 0 ? 1f : 0f;
                GUI.color = Color.Lerp(new Color(0.85f, 0.92f, 1f, 1f), new Color(0.95f, 1f, 1f, 1f), focusTint * 0.6f);
                GUI.DrawTextureWithTexCoords(screen, bg, view);
            }

            // vinheta (mais fechada quando há uma ilha em foco, para a destacar)
            float vig = 0.55f + (m_BgMap >= 0 ? 0.15f * EaseOut(m_BgT) : 0f);
            GUI.color = new Color(0.01f, 0.03f, 0.08f, vig);
            if (m_Vignette != null) GUI.DrawTexture(screen, m_Vignette);

            // marcador da ilha escolhida: anel ciano a "fixar o alvo" (sem nome)
            if (bg != null && m_BgMap >= 0) DrawIslandMarker(view, time);

            // nuvens: nos cantos em repouso; durante o voo atravessam o ecrã
            var clouds = MenuTexture("fx_clouds");
            if (clouds != null)
            {
                float cw = Screen.height * 0.95f, chh = cw * 0.56f;
                GUI.color = new Color(1f, 1f, 1f, 0.42f * (1f - travel * 0.5f));
                GUI.DrawTexture(new Rect(-cw * 0.45f + Mathf.Sin(time * 0.07f) * 18f, -chh * 0.35f, cw, chh), clouds);
                GUI.color = new Color(1f, 1f, 1f, 0.3f * (1f - travel * 0.5f));
                GUI.DrawTexture(new Rect(-cw * 0.55f + Mathf.Cos(time * 0.05f) * 22f, Screen.height - chh * 0.6f, cw, chh), clouds);

                if (travel > 0.001f)
                {
                    // camadas de nuvens que crescem e se afastam do centro: sensação de atravessar o céu
                    float p = m_BgT;
                    for (int i = 0; i < 4; i++)
                    {
                        float layer = Mathf.Repeat(p * 1.6f + i * 0.25f, 1f);
                        float scale = Mathf.Lerp(0.7f, 3.2f, layer * layer);
                        float w = Screen.width * scale, h = w * 0.56f;
                        float side = (i % 2 == 0 ? -1f : 1f);
                        float cx = Screen.width * 0.5f + side * Screen.width * (0.15f + 0.55f * layer);
                        float cy = Screen.height * (0.5f + ((i / 2) == 0 ? -0.18f : 0.2f) * (0.4f + layer));
                        float a = travel * Mathf.Sin(layer * Mathf.PI) * 0.55f;
                        GUI.color = new Color(1f, 1f, 1f, a);
                        if (side < 0) GUI.DrawTexture(new Rect(cx - w / 2f, cy - h / 2f, w, h), clouds);
                        else GUI.DrawTextureWithTexCoords(new Rect(cx - w / 2f, cy - h / 2f, w, h), clouds, new Rect(1, 0, -1, 1));
                    }
                    // véu branco suave no ponto mais alto do voo
                    GUI.color = new Color(0.9f, 0.96f, 1f, 0.16f * travel * travel);
                    GUI.DrawTexture(screen, Texture2D.whiteTexture);
                }
            }
            GUI.color = old;
        }

        void DrawIslandMarker(Rect view, float time)
        {
            var map = s_Maps[m_BgMap];
            var p = UVToScreen(view, map.focus);
            float arrive = Mathf.Clamp01((m_BgT - 0.55f) / 0.45f);       // aparece no fim da viagem
            if (arrive <= 0f) return;
            float a = EaseOut(arrive);
            float lockIn = Mathf.Lerp(2.2f, 1f, a);                        // o anel fecha sobre a ilha
            float baseR = Screen.height * 0.27f * lockIn;   // o anel enquadra a ilha e o personagem por cima dela

            var ring = MenuRingTexture();
            var old = GUI.color;

            // pulso que se expande
            float pulse = Mathf.Repeat(time * 0.6f, 1f);
            float pr = baseR * (1f + pulse * 0.35f);
            GUI.color = new Color(MCyan.r, MCyan.g, MCyan.b, 0.3f * a * (1f - pulse));
            GUI.DrawTexture(new Rect(p.x - pr, p.y - pr, pr * 2f, pr * 2f), ring);

            // anel principal + anel interior a rodar
            GUI.color = new Color(MCyan.r, MCyan.g, MCyan.b, 0.6f * a);
            GUI.DrawTexture(new Rect(p.x - baseR, p.y - baseR, baseR * 2f, baseR * 2f), ring);
            var segs = MenuRingSegmentsTexture();
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(time * 25f, p);
            float ir = baseR * 0.78f;
            GUI.color = new Color(MWhite.r, MWhite.g, MWhite.b, 0.35f * a);
            GUI.DrawTexture(new Rect(p.x - ir, p.y - ir, ir * 2f, ir * 2f), segs);
            GUI.matrix = m;

            // marcas de mira nos quatro lados
            float tick = baseR * 0.12f, th = Mathf.Max(2f, Screen.height * 0.003f);
            GUI.color = new Color(MCyan.r, MCyan.g, MCyan.b, 0.85f * a);
            GUI.DrawTexture(new Rect(p.x - baseR - tick * 0.6f, p.y - th / 2f, tick, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x + baseR - tick * 0.4f, p.y - th / 2f, tick, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - th / 2f, p.y - baseR - tick * 0.6f, th, tick), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - th / 2f, p.y + baseR - tick * 0.4f, th, tick), Texture2D.whiteTexture);
            GUI.color = old;

            // coordenadas discretas (pista, não o nome)
            if (!string.IsNullOrEmpty(map.coords) && m_MT12 != null)
            {
                float oldA = m_MAlpha; m_MAlpha = a * 0.85f;
                // canto inferior esquerdo do anel (fica na zona livre entre os modos e os mapas)
                var lr = new Rect(p.x - baseR * 0.74f - 210 * K, p.y + baseR * 0.74f, 200 * K, 20 * K);
                MFill(new Rect(lr.xMax - 60 * K, lr.yMax + 2 * K, 66 * K, 1.5f * K), MCyanDim);
                MText(lr, "", m_MT12, MCyan, TextAnchor.MiddleRight);
                m_MAlpha = oldA;
            }
        }

        Texture2D m_MRingTex, m_MRingSegTex;

        Texture2D MenuRingTexture()
        {
            if (m_MRingTex != null) return m_MRingTex;
            const int n = 256;
            m_MRingTex = MakeMenuTex(n, (x, y) =>
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float r = n / 2f - 6f;
                float line = Mathf.Clamp01(1.6f - Mathf.Abs(d - r));
                float glow = Mathf.Clamp01(1f - Mathf.Abs(d - r) / 7f) * 0.35f;
                return new Color(1f, 1f, 1f, Mathf.Max(line, glow));
            });
            return m_MRingTex;
        }

        Texture2D MenuRingSegmentsTexture()
        {
            if (m_MRingSegTex != null) return m_MRingSegTex;
            const int n = 256;
            m_MRingSegTex = MakeMenuTex(n, (x, y) =>
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float r = n / 2f - 4f;
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;      // 0..360
                bool seg = Mathf.Repeat(ang, 90f) < 52f;                       // 4 arcos
                float line = Mathf.Clamp01(1.8f - Mathf.Abs(d - r) * 0.6f);
                return new Color(1f, 1f, 1f, seg ? line : 0f);
            });
            return m_MRingSegTex;
        }

        // ================================================================== componentes

        enum MBtn { Normal, Primary, Coral, Muted, Disabled }
        enum MClick { None, Clicked, DisabledClicked }

        MClick MenuButton(Rect r, string text, int id, MBtn kind, bool selected, GUIStyle style, string badge = null)
        {
            var e = Event.current;
            bool interactive = GUI.enabled && kind != MBtn.Disabled;
            bool hover = GUI.enabled && r.Contains(e.mousePosition);
            float h = HoverAnim(id, (interactive && hover) || selected ? 1f : 0f);

            Color fill;
            switch (kind)
            {
                case MBtn.Coral: fill = Color.Lerp(MCoral, Color.Lerp(MCoral, Color.white, 0.25f), h); break;
                case MBtn.Primary: fill = Color.Lerp(new Color(0.12f, 0.55f, 0.6f, 0.95f), new Color(0.2f, 0.72f, 0.74f, 1f), h); break;
                case MBtn.Muted: fill = Color.Lerp(new Color(0.2f, 0.26f, 0.4f, 0.75f), new Color(0.26f, 0.33f, 0.5f, 0.85f), h); break;
                case MBtn.Disabled: fill = new Color(0.18f, 0.22f, 0.32f, 0.6f); break;
                default: fill = Color.Lerp(MButtonFill, MButtonHover, h); break;
            }

            var rr = r;
            if (selected) MDraw9(m_MGlow, Expand(rr, 12 * K), Fade(MCyan, 0.5f));
            MDraw9(m_MFill, rr, Fade(fill, 1f));
            var line = selected ? MCyan : Color.Lerp(new Color(0.55f, 0.7f, 0.95f, 0.25f), MCyan, h * 0.8f);
            MDraw9(m_MLine, rr, Fade(line, 1f));
            if (selected)
            {
                // barra ciano à esquerda, como no PDF
                MFill(new Rect(rr.x + 3 * K, rr.y + 8 * K, 4 * K, rr.height - 16 * K), Fade(MCyan, 1f));
            }

            if (!string.IsNullOrEmpty(text))
            {
                Color tc = kind == MBtn.Disabled ? new Color(0.6f, 0.66f, 0.76f, 0.8f)
                    : kind == MBtn.Muted ? new Color(0.75f, 0.8f, 0.9f, 0.9f)
                    : (selected ? MCyan : MWhite);
                var textRect = string.IsNullOrEmpty(badge) ? rr : new Rect(rr.x, rr.y - 6 * K, rr.width, rr.height);
                MText(textRect, text, style, tc);
            }
            if (!string.IsNullOrEmpty(badge))
                MText(new Rect(rr.x, rr.yMax - 22 * K, rr.width, 18 * K), badge, m_MT12, MCoral);

            if (!GUI.enabled) return MClick.None;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                return kind == MBtn.Disabled ? MClick.DisabledClicked : MClick.Clicked;
            return MClick.None;
        }

        bool MenuIconButton(Rect r, string glyph, int id)
        {
            var res = MenuButton(r, glyph, id, MBtn.Normal, false, m_MT24);
            return res == MClick.Clicked;
        }

        bool MenuSpeakerButton(Rect r, bool isMuteToggle, bool muted, int id, string sign = null)
        {
            var res = MenuButton(r, "", id, MBtn.Normal, isMuteToggle && muted, m_MT14);
            var icon = MenuSpeakerTexture(isMuteToggle && muted);
            float s = Mathf.Min(r.width, r.height) * 0.72f;
            if (sign != null) s *= 0.8f;
            MTex(new Rect(r.center.x - s / 2f - (sign != null ? 9 * K : 0f), r.center.y - s / 2f, s, s), icon, MWhite, ScaleMode.ScaleToFit);
            if (sign != null) MText(new Rect(r.center.x + 7 * K, r.y, r.width / 2f - 7 * K, r.height), sign, m_MT20, MCyan, TextAnchor.MiddleLeft);
            return res == MClick.Clicked;
        }

        string MenuField(Rect r, string controlName, string value, string placeholder, int maxLength)
        {
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            MDraw9(m_MFill, r, Fade(new Color(0.9f, 0.92f, 0.95f, 0.95f), 1f));
            MDraw9(m_MLine, r, Fade(focused ? MCyan : new Color(0.5f, 0.6f, 0.75f, 0.5f), 1f));
            var inner = new Rect(r.x + 14 * K, r.y, r.width - 28 * K, r.height);
            var oldCursor = GUI.skin.settings.cursorColor;
            GUI.skin.settings.cursorColor = new Color(0.1f, 0.2f, 0.35f);
            GUI.SetNextControlName(controlName);
            var v = GUI.TextField(inner, value ?? "", maxLength, m_MField);
            GUI.skin.settings.cursorColor = oldCursor;
            if (string.IsNullOrEmpty(v) && !focused) MText(inner, placeholder, m_MPlaceholder, new Color(0.35f, 0.4f, 0.5f, 1f), TextAnchor.MiddleLeft);
            return v;
        }

        float MenuVerticalSlider(Rect track, float value, int id)
        {
            int cid = GUIUtility.GetControlID(id, FocusType.Passive, track);
            var e = Event.current;
            var hit = Expand(track, 10 * K);
            switch (e.GetTypeForControl(cid))
            {
                case EventType.MouseDown:
                    if (GUI.enabled && hit.Contains(e.mousePosition)) { GUIUtility.hotControl = cid; value = SliderValue(track, e.mousePosition.y); e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == cid) { value = SliderValue(track, e.mousePosition.y); e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == cid) { GUIUtility.hotControl = 0; e.Use(); }
                    break;
                case EventType.ScrollWheel:
                    if (hit.Contains(e.mousePosition)) { value = Mathf.Clamp01(value - e.delta.y * 0.03f); e.Use(); }
                    break;
            }
            MDraw9(m_MFill, track, Fade(new Color(0.06f, 0.1f, 0.18f, 0.9f), 1f));
            float fh = Mathf.Max(track.width, track.height * value);
            var fill = new Rect(track.x, track.yMax - fh, track.width, fh);
            MDraw9(m_MFill, fill, Fade(new Color(0.2f, 0.65f, 0.68f, 0.95f), 1f));
            MDraw9(m_MLine, track, Fade(MCyanDim, 0.8f));
            return value;
        }

        static float SliderValue(Rect track, float y) => Mathf.Clamp01((track.yMax - y) / Mathf.Max(1f, track.height));

        /// <summary>Painel de vidro azulado com moldura ciano "tech" (cantos e linhas como no PDF).</summary>
        void DrawTechPanel(Rect r, bool ornate)
        {
            MDraw9(m_MFill, r, Fade(MGlass, 1f));
            MDraw9(m_MLine, r, Fade(new Color(0.55f, 0.75f, 0.95f, 0.22f), 1f));
            if (!ornate) return;
            float k = K, th = 2f * k;
            var c = Fade(MCyan, 0.85f);
            // topo: linha com ponta
            MFill(new Rect(r.x + 20 * k, r.y + 16 * k, r.width - 50 * k, th), c);
            MFill(new Rect(r.xMax - 70 * k, r.y + 12 * k, 40 * k, th * 1.5f), c);
            MTex(new Rect(r.x + 16 * k, r.y + 13 * k, 8 * k, 8 * k), Art.CircleTexture, c, ScaleMode.StretchToFill);
            // lado direito superior (suporte)
            MFill(new Rect(r.xMax - 22 * k, r.y + 16 * k, th, 90 * k), c);
            MFill(new Rect(r.xMax - 16 * k, r.y + 30 * k, th, 50 * k), Fade(MCyan, 0.5f));
            // lado esquerdo inferior
            MFill(new Rect(r.x + 16 * k, r.yMax - 110 * k, th, 92 * k), c);
            MFill(new Rect(r.x + 10 * k, r.yMax - 90 * k, th, 50 * k), Fade(MCyan, 0.5f));
            // fundo: linha com detalhe animado
            MFill(new Rect(r.x + 16 * k, r.yMax - 18 * k, r.width - 40 * k, th), c);
            MFill(new Rect(r.x + 50 * k, r.yMax - 24 * k, 60 * k, th * 1.5f), c);
            float p = Mathf.Repeat(Time.unscaledTime * 0.25f, 1f);
            MTex(new Rect(r.x + 16 * k + (r.width - 48 * k) * p, r.yMax - 21 * k, 8 * k, 8 * k), Art.CircleTexture, Fade(Color.white, 0.8f), ScaleMode.StretchToFill);
            MTex(new Rect(r.xMax - 28 * k, r.yMax - 22 * k, 10 * k, 10 * k), Art.CircleTexture, c, ScaleMode.StretchToFill);
        }

        /// <summary>Painel opaco (para menus por cima de outros painéis, ex.: áudio).</summary>
        void DrawSolidPanel(Rect r)
        {
            MDraw9(m_MGlow, Expand(r, 10 * K), Fade(new Color(0f, 0f, 0f, 1f), 0.5f));
            MDraw9(m_MFill, r, Fade(new Color(0.08f, 0.13f, 0.24f, 0.97f), 1f));
            MDraw9(m_MLine, r, Fade(MCyanDim, 0.7f));
        }

        void DrawTechLine(Rect r, bool withSegments)
        {
            var c = Fade(MCyan, 0.7f);
            MFill(new Rect(r.x, r.y, r.width, r.height), c);
            MFill(new Rect(r.x, r.y - 3 * K, r.width * 0.22f, r.height * 2.5f), Fade(MCyan, 0.9f));
            if (withSegments) MFill(new Rect(r.x + r.width * 0.3f, r.y - 4 * K, r.width * 0.12f, r.height * 2f), Fade(MCyan, 0.5f));
            MTex(new Rect(r.xMax - 6 * K, r.y - 5 * K, 12 * K, 12 * K), Art.CircleTexture, Fade(MCyan, 0.9f), ScaleMode.StretchToFill);
        }

        // ================================================================== primitivas

        Rect V(float x, float y, float w, float h) => new Rect(m_MOx + x * K, m_MOy + y * K, w * K, h * K);
        /// <summary>Ancorado à esquerda (x a partir da margem esquerda).</summary>
        Rect VL(float x, float y, float w, float h) => new Rect(x * K, Y(y), w * K, h * K);
        /// <summary>Ancorado à direita: x é medido na referência de 1440 de largura.</summary>
        Rect VR(float x, float y, float w, float h) => new Rect(Screen.width - (1440f - x) * K, Y(y), w * K, h * K);
        float X(float x) => x * K;
        float Y(float y) => m_MOy + y * K;

        static Rect Expand(Rect r, float d) => new Rect(r.x - d, r.y - d, r.width + d * 2f, r.height + d * 2f);
        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
        Color Fade(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a * m_MAlpha);

        float HoverAnim(int id, float target)
        {
            m_MHover.TryGetValue(id, out var v);
            if (Event.current.type == EventType.Repaint)
            {
                v = Mathf.MoveTowards(v, target, m_MDt * 7f);
                m_MHover[id] = v;
            }
            return v;
        }

        void MDraw9(GUIStyle style, Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint || c.a <= 0.001f) return;
            var old = GUI.color;
            GUI.color = c;
            style.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        void MFill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * m_MAlpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void MTex(Rect r, Texture t, Color c, ScaleMode mode)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * m_MAlpha);
            GUI.DrawTexture(r, t, mode, true);
            GUI.color = old;
        }

        void MText(Rect r, string text, GUIStyle style, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool wrap = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            int oldSize = style.fontSize;
            var oldFont = style.font;
            if (text.Length > 0 && System.Array.TrueForAll(text.ToCharArray(), ch => !char.IsLetter(ch))) style.font = GameTypography.Numbers;
            if (!wrap)
            {
                float width = style.CalcSize(new GUIContent(text)).x;
                if (width > r.width) style.fontSize = Mathf.Max(8, Mathf.FloorToInt(oldSize * r.width / width));
            }
            var oldColor = style.normal.textColor;
            var oldHoverColor = style.hover.textColor;
            var oldAnchor = style.alignment;
            var oldWrap = style.wordWrap;
            style.alignment = anchor;
            style.wordWrap = wrap;
            // sombra suave para destacar do fundo
            style.normal.textColor = new Color(0f, 0.02f, 0.06f, 0.6f * c.a * m_MAlpha);
            style.hover.textColor = style.normal.textColor;
            GUI.Label(new Rect(r.x + 1.5f * K, r.y + 2f * K, r.width, r.height), text, style);
            style.normal.textColor = new Color(c.r, c.g, c.b, c.a * m_MAlpha);
            style.hover.textColor = style.normal.textColor;
            GUI.Label(r, text, style);
            style.normal.textColor = oldColor;
            style.hover.textColor = oldHoverColor;
            style.alignment = oldAnchor;
            style.wordWrap = oldWrap;
            style.fontSize = oldSize;
            style.font = oldFont;
        }

        Texture2D MenuTexture(string name)
        {
            if (m_MTex.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("UI/Menu/" + name);
            if (t == null) Debug.LogWarning("[Menu] Imagem em falta: Resources/UI/Menu/" + name);
            m_MTex[name] = t;
            return t;
        }

        // ================================================================== estilos e texturas geradas

        void EnsureMenuStyles()
        {
            K = Mathf.Clamp(Mathf.Min(Screen.width / 1440f, Screen.height / 810f), 0.45f, 3f);
            m_MOx = 0f;
            m_MOy = Mathf.Max(0f, (Screen.height - 810f * K) * 0.5f);
            if (m_MFill != null && Mathf.Approximately(K, m_MStyleK)) return;
            m_MStyleK = K;

            if (m_MFont == null)
            {
                try { m_MFont = GameTypography.Body; }
                catch { m_MFont = null; }
            }

            m_MFill = NineSlice(MakeRoundedFill(64, 14), 16);
            m_MLine = NineSlice(MakeRoundedStroke(64, 14, 2.2f), 16);
            m_MGlow = NineSlice(MakeGlow(96, 30), 40);

            m_MT40 = MakeMenuText(40, FontStyle.Bold);
            m_MT30 = MakeMenuText(30, FontStyle.Bold);
            m_MT24 = MakeMenuText(24, FontStyle.Bold);
            m_MT20 = MakeMenuText(20, FontStyle.Bold);
            m_MT17 = MakeMenuText(17, FontStyle.Bold);
            m_MT14 = MakeMenuText(14, FontStyle.Bold);
            m_MT12 = MakeMenuText(12, FontStyle.Normal);
            m_MField = MakeMenuText(17, FontStyle.Normal);
            m_MField.alignment = TextAnchor.MiddleLeft;
            m_MField.normal.textColor = new Color(0.08f, 0.12f, 0.2f);
            m_MField.focused.textColor = m_MField.normal.textColor;
            m_MField.hover.textColor = m_MField.normal.textColor;
            m_MField.clipping = TextClipping.Clip;
            m_MPlaceholder = MakeMenuText(16, FontStyle.Normal);
        }

        GUIStyle MakeMenuText(int size, FontStyle style)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                font = size >= 30 ? GameTypography.Display : size >= 24 ? GameTypography.Title : m_MFont,
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size * K)),
                fontStyle = style,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            s.normal.textColor = MWhite;
            return s;
        }

        static GUIStyle NineSlice(Texture2D tex, int border)
        {
            var s = new GUIStyle { border = new RectOffset(border, border, border, border) };
            s.normal.background = tex;
            return s;
        }

        internal static Texture2D MakeRoundedFill(int size, int radius)
        {
            return MakeMenuTex(size, (x, y) =>
            {
                float d = RoundedDistance(x, y, size, radius);
                float a = Mathf.Clamp01(0.5f - d);
                float shade = 0.9f + 0.1f * (y / (float)size);   // ligeiro brilho no topo (efeito vidro)
                return new Color(shade, shade, shade, a);
            });
        }

        internal static Texture2D MakeRoundedStroke(int size, int radius, float width)
        {
            return MakeMenuTex(size, (x, y) =>
            {
                float d = RoundedDistance(x, y, size, radius);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(width + d + 0.5f);
                return new Color(1f, 1f, 1f, a);
            });
        }

        internal static Texture2D MakeGlow(int size, int falloff)
        {
            return MakeMenuTex(size, (x, y) =>
            {
                float d = RoundedDistance(x, y, size, falloff + 6);
                float a = Mathf.Clamp01(-d / falloff);
                a = a * a * (3f - 2f * a);
                return new Color(1f, 1f, 1f, a * 0.9f);
            });
        }

        /// <summary>Distância (negativa dentro) a um retângulo arredondado que ocupa a textura.</summary>
        static float RoundedDistance(int x, int y, int size, int radius)
        {
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Mathf.Clamp(px, radius, size - radius);
            float cy = Mathf.Clamp(py, radius, size - radius);
            float dx = px - cx, dy = py - cy;
            float outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
            if (px > radius && px < size - radius || py > radius && py < size - radius)
            {
                float edge = Mathf.Min(Mathf.Min(px, size - px), Mathf.Min(py, size - py));
                return Mathf.Max(outside, -edge);
            }
            return outside;
        }

        static Texture2D MakeMenuTex(int size, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f(x, y);
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        Texture2D m_MSpeaker, m_MSpeakerMuted;

        Texture2D MenuSpeakerTexture(bool muted)
        {
            if (muted) return m_MSpeakerMuted != null ? m_MSpeakerMuted : (m_MSpeakerMuted = MakeSpeaker(true));
            return m_MSpeaker != null ? m_MSpeaker : (m_MSpeaker = MakeSpeaker(false));
        }

        /// <summary>Ícone de altifalante desenhado em código (com ondas ou com um X de mudo).</summary>
        static Texture2D MakeSpeaker(bool muted)
        {
            const int n = 64;
            return MakeMenuTex(n, (x, y) =>
            {
                float px = x + 0.5f, py = n - (y + 0.5f);   // y para baixo
                float a = 0f;
                // corpo
                if (px >= 10 && px <= 22 && py >= 24 && py <= 40) a = 1f;
                // cone
                if (px >= 22 && px <= 36)
                {
                    float t = (px - 22f) / 14f;
                    float half = Mathf.Lerp(8f, 20f, t);
                    if (Mathf.Abs(py - 32f) <= half) a = 1f;
                }
                if (!muted)
                {
                    float dx = px - 36f, dy = py - 32f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Abs(Mathf.Atan2(dy, dx)) * Mathf.Rad2Deg;
                    if (dx > 0 && ang < 50f && (Mathf.Abs(r - 12f) < 2.2f || Mathf.Abs(r - 21f) < 2.2f)) a = 1f;
                }
                else
                {
                    float u = px - 50f, v = py - 32f;
                    if (Mathf.Abs(u) < 10f && Mathf.Abs(v) < 10f && (Mathf.Abs(u - v) < 2.6f || Mathf.Abs(u + v) < 2.6f)) a = 1f;
                }
                return new Color(1f, 1f, 1f, a);
            });
        }
    }
}
