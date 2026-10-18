using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Menu principal (referência visual: gamejam.pdf). Desenhado em IMGUI como o resto do GameUI, sem objetos na cena
    /// nem referências manuais: as imagens vêm de Resources/UI/Menu e a lógica reutiliza ConnectionManager, GameSession,
    /// BrawlerRegistry, MatchManager e BotManager.
    ///
    /// Ecrãs: Principal (Story Mode · Play Online · Start Server · Quit) com painel "Join a server" ao lado,
    /// Configuração da partida (modos · personagem · mapa) e painel rápido de áudio no canto superior direito.
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

        /// <summary>
        /// Personagens do menu → brawler jogável (por brawlerId). LIGAÇÃO PROVISÓRIA: só o Lince corresponde de origem;
        /// os restantes foram associados por ordem. Ajustar aqui (uma linha por personagem) quando a equipa decidir.
        /// </summary>
        static readonly MenuCharacter[] s_Roster =
        {
            new MenuCharacter("LINCE", "lince", "sniper"),
            new MenuCharacter("RIOT", "riot", "gunslinger"),
            new MenuCharacter("LUPUS MARINUS", "lupus", "trapper"),
            new MenuCharacter("MR.S", "mrs", "sprayer"),
            new MenuCharacter("N.CAGE", "ncage", "shotgunner"),
            new MenuCharacter("EKKO", "ekko", "artillery"),
        };

        struct MenuMode
        {
            public string title, subtitle, pendingLabel;
            public string[] enumNames;
            public MenuMode(string title, string subtitle, string pendingLabel, params string[] enumNames)
            { this.title = title; this.subtitle = subtitle; this.pendingLabel = pendingLabel; this.enumNames = enumNames; }
        }

        /// <summary>
        /// Um modo fica jogável automaticamente quando existir um valor com um destes nomes no enum GameMode
        /// (ex.: quando o Pique Gema for adicionado como GameMode.GemRush). Até lá aparece como "em desenvolvimento".
        /// </summary>
        static readonly MenuMode[] s_Modes =
        {
            new MenuMode("GEM RUSH", "Pique Gema · apanha e segura as gemas com a tua equipa", "EM DESENVOLVIMENTO", "GemRush", "GemGrab", "PiqueGema"),
            new MenuMode("IT TAKES TWO", "Cooperativo a dois · descobre a história das ilhas", "EM BREVE", "ItTakesTwo", "Coop"),
            new MenuMode("DEATHMATCH", "Todos contra todos", null, "Deathmatch"),
            new MenuMode("WIPEOUT", "Eliminações por equipa", null, "Wipeout"),
            new MenuMode("KNOCKOUT", "Equipas · melhor de três rondas", null, "Knockout"),
            new MenuMode("SHOWDOWN", "Sem respawn · vence o último vivo", null, "Showdown"),
        };

        struct MenuMap
        {
            public string name, place, key;
            public bool playable;
            public MenuMap(string name, string place, string key, bool playable) { this.name = name; this.place = place; this.key = key; this.playable = playable; }
        }

        /// <summary>Só a Pradaria (cena Arena) é jogável por agora; as ilhas são pré-visualizações.</summary>
        static readonly MenuMap[] s_Maps =
        {
            new MenuMap("PRADARIA", "Arena atual", "pradaria", true),
            new MenuMap("SANTIAGO", "Cidade Velha · Pelourinho", "santiago", false),
            new MenuMap("SAL", "Buracona · Olho Azul", "sal", false),
            new MenuMap("BOA VISTA", "Deserto de Viana", "boavista", false),
            new MenuMap("FOGO", "Pico do Fogo", "fogo", false),
        };

        const string KeyMenuMap = "fpslan.map";

        /// <summary>Mapa escolhido no menu (chave). Fica guardado para quando houver mais do que um mapa jogável.</summary>
        public static string SelectedMapKey => PlayerPrefs.GetString(KeyMenuMap, "pradaria");

        // ================================================================== estado

        enum MenuScreen { Main, Setup }
        enum SetupContext { Host, Join }

        MenuScreen m_MScreen = MenuScreen.Main;
        SetupContext m_MSetupCtx = SetupContext.Host;
        float m_MScreenT = 0f;          // 0→1 entrada do ecrã atual
        bool m_MJoinOpen; float m_MJoinT;
        bool m_MAudioOpen; float m_MAudioT;
        int m_MFocus = 1;               // foco do teclado no menu principal (começa em PLAY ONLINE)
        int m_MModeSel = -1;
        int m_MMapSel;
        float m_MMapScroll, m_MMapScrollTarget;
        int m_MCharPrev = -1; float m_MCharT = 1f;
        string m_MNotice; float m_MNoticeTime = -10f;
        string m_MJoinError;
        float m_MLastTime, m_MDt;
        float m_MAlpha = 1f;
        readonly Dictionary<int, float> m_MHover = new Dictionary<int, float>();
        readonly Dictionary<string, Texture2D> m_MTex = new Dictionary<string, Texture2D>();
        bool m_MInit;

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
        static readonly Color MCyan = new Color(0.25f, 0.88f, 0.82f, 1f);
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
                m_MJoinT = Mathf.MoveTowards(m_MJoinT, m_MJoinOpen ? 1f : 0f, m_MDt * 6f);
                m_MAudioT = Mathf.MoveTowards(m_MAudioT, m_MAudioOpen ? 1f : 0f, m_MDt * 7f);
                m_MCharT = Mathf.MoveTowards(m_MCharT, 1f, m_MDt * 5f);
                m_MMapScroll = Mathf.Lerp(m_MMapScroll, m_MMapScrollTarget, 1f - Mathf.Exp(-m_MDt * 10f));
            }

            MenuInit();
            EnsureMenuStyles();

            bool connecting = NM != null && NM.IsListening;   // a ligar/criar (ainda sem jogador local)
            DrawMenuBackground();

            if (m_SettingsOpen)
            {
                DrawSettingsMenu();   // definições completas existentes (teclas, jogabilidade, áudio)
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
            // Modo inicial = o que estava guardado (se existir na lista)
            for (int i = 0; i < s_Modes.Length; i++)
                if (TryResolveMode(s_Modes[i], out var gm) && gm == MatchManager.MenuSelectedMode) { m_MModeSel = i; break; }
            if (m_MModeSel < 0) m_MModeSel = FirstPlayableMode();
            // Mapa inicial
            m_MMapSel = 0;
            for (int i = 0; i < s_Maps.Length; i++) if (s_Maps[i].key == SelectedMapKey && s_Maps[i].playable) m_MMapSel = i;
            // Personagem inicial = brawler guardado (se pertencer ao roster)
            if (MenuCharIndexForHero(m_Hero) < 0) SelectMenuCharacter(0);
        }

        // ================================================================== ecrã principal

        void DrawMenuMainScreen(float ease)
        {
            float slide = (1f - ease) * -40f * K;
            var panel = V(82, 94, 484, 622);
            panel.x += slide;
            DrawTechPanel(panel, true);

            MText(new Rect(panel.x, panel.y + 30 * K, panel.width, 56 * K), "MENU", m_MT40, MWhite);

            string[] labels = { "STORY MODE", "PLAY ONLINE", "START SERVER", "QUIT" };
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(panel.x + 48 * K, panel.y + 112 * K + i * 108 * K, panel.width - 96 * K, 86 * K);
                bool storyDisabled = i == 0;
                bool selected = (i == 1 && m_MJoinOpen) || (m_MFocus == i && !m_MJoinOpen && GUI.enabled);
                var res = MenuButton(r, labels[i], 1000 + i, storyDisabled ? MBtn.Disabled : MBtn.Normal, selected, m_MT30,
                    storyDisabled ? "EM DESENVOLVIMENTO" : null);
                if (GUI.enabled && Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition)) m_MFocus = i;
                if (res == MClick.Clicked) ActivateMainItem(i);
                else if (res == MClick.DisabledClicked) MenuNotice("Story Mode ainda está em desenvolvimento.");
            }

            // linha decorativa por baixo do painel
            var deco = new Rect(panel.x + 16 * K, panel.yMax + 34 * K, panel.width - 30 * K, 2 * K);
            DrawTechLine(deco, true);

            // painel "Join a server" ao lado
            if (m_MJoinT > 0.001f) DrawMenuJoinPanel(new Rect(panel.xMax + 14 * K, panel.y + 80 * K, 330 * K, 380 * K));

            // rodapé com o IP local (útil para quem cria a sala)
            var ips = m_LocalIps != null && m_LocalIps.Count > 0 ? string.Join("  ·  ", m_LocalIps) : "desconhecido";
            MText(new Rect(panel.x, panel.yMax + 46 * K, panel.width + 400 * K, 24 * K),
                "O TEU IP NA REDE: " + ips + "   ·   UDP " + ConnectionManager.DefaultPort, m_MT12, MMuted, TextAnchor.MiddleLeft);
        }

        void ActivateMainItem(int i)
        {
            switch (i)
            {
                case 0: MenuNotice("Story Mode ainda está em desenvolvimento."); break;
                case 1:
                    m_MJoinOpen = !m_MJoinOpen;
                    m_MJoinError = null;
                    if (m_MJoinOpen) GUI.FocusControl("mm_name");
                    break;
                case 2: OpenSetup(SetupContext.Host); break;
                case 3: QuitGame(); break;
            }
        }

        void DrawMenuJoinPanel(Rect r)
        {
            float t = EaseOut(m_MJoinT);
            float oldA = m_MAlpha;
            m_MAlpha *= t;
            r.x -= (1f - t) * 30f * K;
            DrawTechPanel(r, false);

            MText(new Rect(r.x, r.y + 16 * K, r.width, 34 * K), "JOIN A SERVER", m_MT20, MCyan);
            if (MenuIconButton(new Rect(r.xMax - 40 * K, r.y + 14 * K, 28 * K, 28 * K), "×", 1100)) { m_MJoinOpen = false; m_MAlpha = oldA; return; }

            float x = r.x + 22 * K, w = r.width - 44 * K, y = r.y + 62 * K;
            m_Name = MenuField(new Rect(x, y, w, 46 * K), "mm_name", m_Name, "Nome do jogador", 16);
            y += 58 * K;
            m_Ip = MenuField(new Rect(x, y, w, 46 * K), "mm_code", m_Ip, "Código do servidor (IP do host)", 40);
            y += 58 * K;

            // personagem escolhido (abre a seleção)
            int ci = Mathf.Max(0, MenuCharIndexForHero(m_Hero));
            var chip = new Rect(x, y, w, 50 * K);
            if (MenuButton(chip, "", 1101, MBtn.Normal, false, m_MT14) == MClick.Clicked) OpenSetup(SetupContext.Join);
            var portrait = MenuTexture("portrait_" + s_Roster[ci].key);
            if (portrait != null) MTex(new Rect(chip.x + 6 * K, chip.y + 4 * K, 42 * K, 42 * K), portrait, Color.white, ScaleMode.ScaleAndCrop);
            MText(new Rect(chip.x + 58 * K, chip.y + 4 * K, chip.width - 70 * K, 22 * K), "PERSONAGEM", m_MT12, MMuted, TextAnchor.MiddleLeft);
            MText(new Rect(chip.x + 58 * K, chip.y + 22 * K, chip.width - 70 * K, 26 * K), s_Roster[ci].name + "   ›", m_MT17, MWhite, TextAnchor.MiddleLeft);
            y += 66 * K;

            var join = new Rect(r.x + (r.width - 200 * K) / 2f, y, 200 * K, 48 * K);
            if (MenuButton(join, "JOIN GAME", 1102, MBtn.Coral, false, m_MT20) == MClick.Clicked) TryJoin();
            y += 58 * K;

            string status = m_MJoinError;
            if (string.IsNullOrEmpty(status) && ConnectionManager.Instance != null) status = ConnectionManager.Instance.Status;
            if (!string.IsNullOrEmpty(status))
                MText(new Rect(x - 6 * K, y, w + 12 * K, 40 * K), status, m_MT12, new Color(1f, 0.75f, 0.65f), TextAnchor.UpperCenter, true);

            m_MAlpha = oldA;
        }

        void TryJoin()
        {
            m_MJoinError = null;
            var name = (m_Name ?? "").Trim();
            var code = (m_Ip ?? "").Trim();
            if (name.Length == 0) { m_MJoinError = "Escreve o teu nome."; GUI.FocusControl("mm_name"); return; }
            if (code.Length == 0) { m_MJoinError = "Escreve o código do servidor (IP do host, ex.: 192.168.1.20)."; GUI.FocusControl("mm_code"); return; }
            if (code != "localhost" && !System.Net.IPAddress.TryParse(code, out _))
            { m_MJoinError = "Código inválido. Usa o IP mostrado no ecrã do host (ex.: 192.168.1.20)."; GUI.FocusControl("mm_code"); return; }
            if (ConnectionManager.Instance == null) { m_MJoinError = "Erro: sistema de rede não encontrado na cena."; return; }
            if (NM != null && NM.IsListening) return; // já a ligar
            m_Name = name; m_Ip = code;
            SaveMenuPrefs();
            ConnectionManager.Instance.Join(code == "localhost" ? "127.0.0.1" : code);
        }

        // ================================================================== ecrã de configuração (modos · personagem · mapa)

        void OpenSetup(SetupContext ctx)
        {
            m_MSetupCtx = ctx;
            m_MScreen = MenuScreen.Setup;
            m_MScreenT = 0f;
            m_MAudioOpen = false;
            GUI.FocusControl(null);
        }

        void BackToMain()
        {
            m_MScreen = MenuScreen.Main;
            m_MScreenT = 0f;
            m_MJoinOpen = m_MSetupCtx == SetupContext.Join;
            GUI.FocusControl(null);
        }

        void DrawMenuSetupScreen(float ease)
        {
            bool hostCtx = m_MSetupCtx == SetupContext.Host;
            float slide = (1f - ease) * 30f * K;

            // ---------------- modos (esquerda)
            var title = VL(58, 70, 400, 56);
            title.x -= slide;
            MText(title, "GAME MODES", m_MT40, MWhite, TextAnchor.MiddleLeft);
            DrawTechLine(VL(58, 130, 360, 2), false);

            float my = 154;
            for (int i = 0; i < s_Modes.Length; i++)
            {
                var r = VL(83, my + i * 50, 324, 44);
                r.x -= slide;
                bool playable = TryResolveMode(s_Modes[i], out _);
                bool sel = m_MModeSel == i;
                var kind = !hostCtx ? MBtn.Disabled : (playable ? MBtn.Normal : MBtn.Muted);
                var res = MenuButton(r, s_Modes[i].title, 2000 + i, kind, sel && hostCtx, m_MT17, playable ? null : s_Modes[i].pendingLabel);
                if (res == MClick.Clicked) SelectMode(i);
                else if (res == MClick.DisabledClicked)
                    MenuNotice("No modo JOIN é o host quem escolhe o modo e o mapa.");
            }

            // nome do jogador
            var nameRect = VL(83, my + s_Modes.Length * 50 + 10, 324, 40);
            nameRect.x -= slide;
            MText(new Rect(nameRect.x, nameRect.y - 2 * K, nameRect.width, 18 * K), "NOME DO JOGADOR", m_MT12, MMuted, TextAnchor.UpperLeft);
            nameRect.y += 16 * K;
            m_Name = MenuField(nameRect, "mm_name_setup", m_Name, "Nome do jogador", 16);

            // ---------------- personagens (direita)
            var charPanel = VR(1051, 75, 367, 461);
            charPanel.x += slide;
            DrawTechPanel(charPanel, false);
            MText(new Rect(charPanel.x, charPanel.y + 10 * K, charPanel.width, 36 * K), "CHARACTERS", m_MT24, MWhite);
            DrawTechLine(new Rect(charPanel.x + 40 * K, charPanel.y + 48 * K, charPanel.width - 80 * K, 2 * K), false);
            int selChar = Mathf.Max(0, MenuCharIndexForHero(m_Hero));
            float cw = (charPanel.width - 40 * K) / 2f, ch = (charPanel.height - 70 * K) / 3f;
            for (int i = 0; i < s_Roster.Length; i++)
            {
                var cell = new Rect(charPanel.x + 20 * K + (i % 2) * cw, charPanel.y + 60 * K + (i / 2) * ch, cw, ch);
                if (DrawCharacterCell(cell, i, i == selChar)) SelectMenuCharacter(i);
            }

            // ---------------- personagem em destaque (centro)
            float leftEdge = VL(407, 0, 0, 0).x, rightEdge = charPanel.x;
            var stage = new Rect(leftEdge + 10 * K, Y(70), rightEdge - leftEdge - 20 * K, 470 * K);
            DrawCharacterStage(stage, selChar, s_Modes[Mathf.Clamp(m_MModeSel, 0, s_Modes.Length - 1)].title == "IT TAKES TWO");

            // ---------------- mapas (fundo)
            var mapTitle = VL(58, 532, 300, 40);
            MText(mapTitle, "MAP", m_MT30, MWhite, TextAnchor.MiddleLeft);
            DrawTechLine(VL(58, 572, 380, 2), false);
            var actions = VR(1051, 579, 367, 196);
            var strip = new Rect(X(58), Y(588), actions.x - X(58) - 24 * K, 172 * K);
            DrawMapCarousel(strip, hostCtx);

            // ---------------- ações (canto inferior direito)
            actions.x += slide;
            DrawSetupActions(actions, hostCtx);

            // voltar
            if (MenuButton(new Rect(Screen.width - 250 * K, 26 * K, 150 * K, 44 * K), "‹  VOLTAR", 2900, MBtn.Normal, false, m_MT17) == MClick.Clicked)
                BackToMain();
        }

        void SelectMode(int i)
        {
            m_MModeSel = i;
            if (TryResolveMode(s_Modes[i], out var gm))
            {
                MatchManager.MenuSelectedMode = gm;
                PlayerPrefs.SetInt("fpslan.mode", (int)gm);
            }
            else MenuNotice(s_Modes[i].title + ": " + s_Modes[i].pendingLabel.ToLowerInvariant() + ".");
        }

        bool DrawCharacterCell(Rect cell, int i, bool selected)
        {
            var e = Event.current;
            bool hover = GUI.enabled && cell.Contains(e.mousePosition);
            float h = HoverAnim(3000 + i, hover || selected ? 1f : 0f);
            var portrait = MenuTexture("portrait_" + s_Roster[i].key);
            float size = Mathf.Min(cell.width, cell.height - 24 * K) * (0.82f + 0.08f * h);
            var pr = new Rect(cell.center.x - size / 2f, cell.y + (cell.height - 24 * K - size) / 2f, size, size);
            if (selected)
            {
                MDraw9(m_MGlow, Expand(pr, 10 * K), Fade(MCyan, 0.55f));
                MDraw9(m_MLine, pr, Fade(MCyan, 0.9f));
            }
            else if (h > 0.01f) MDraw9(m_MLine, pr, Fade(MCyanDim, h));
            if (portrait != null) MTex(pr, portrait, Color.white, ScaleMode.ScaleAndCrop);
            bool available = MenuCharacterBrawler(i) != null;
            MText(new Rect(cell.x - 10 * K, pr.yMax + 2 * K, cell.width + 20 * K, 22 * K), s_Roster[i].name, m_MT14,
                selected ? MCyan : (available ? MWhite : MMuted));
            if (GUI.enabled && GUI.Button(cell, GUIContent.none, GUIStyle.none))
            {
                if (!available) { MenuNotice(s_Roster[i].name + " ainda não está ligado a um brawler jogável."); return false; }
                return true;
            }
            return false;
        }

        void DrawCharacterStage(Rect stage, int selChar, bool coopPreview)
        {
            var b = MenuCharacterBrawler(selChar);
            if (coopPreview)
            {
                // Pré-visualização do cooperativo (página 5 do PDF): jogador 1 e jogador 2
                int second = (selChar + 1) % s_Roster.Length;
                float half = stage.width / 2f;
                DrawCharacterArt(new Rect(stage.x, stage.y + 30 * K, half, stage.height - 70 * K), selChar, 1f);
                DrawCharacterArt(new Rect(stage.x + half, stage.y + 30 * K, half, stage.height - 70 * K), second, 1f);
                MText(new Rect(stage.x, stage.y, half, 30 * K), "1", m_MT24, MWhite);
                MText(new Rect(stage.x + half, stage.y, half, 30 * K), "2", m_MT24, MWhite);
                MText(new Rect(stage.x, stage.yMax - 34 * K, stage.width, 30 * K), "MODO COOPERATIVO  ·  EM BREVE", m_MT17, MCyan);
                return;
            }

            // cross-fade entre personagens
            float t = EaseOut(m_MCharT);
            var art = new Rect(stage.x, stage.y, stage.width, stage.height - 64 * K);
            if (m_MCharPrev >= 0 && m_MCharPrev != selChar && t < 1f)
                DrawCharacterArt(art, m_MCharPrev, 1f - t);
            DrawCharacterArt(new Rect(art.x, art.y + (1f - t) * 20f * K, art.width, art.height), selChar, t);

            MText(new Rect(stage.x, stage.yMax - 62 * K, stage.width, 40 * K), s_Roster[selChar].name, m_MT30, MWhite);
            if (b != null)
                MText(new Rect(stage.x, stage.yMax - 26 * K, stage.width, 24 * K),
                    RoleTitle(b) + "  ·  joga como " + b.displayName, m_MT14, MCyan);
        }

        void DrawCharacterArt(Rect r, int index, float alpha)
        {
            var tex = MenuTexture("char_" + s_Roster[index].key);
            if (tex == null || alpha <= 0.001f) return;
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f + index) * 4f * K;
            var rr = new Rect(r.x, r.y + bob, r.width, r.height);
            // halo ciano por trás (cópia ligeiramente maior e transparente)
            MTex(Expand(rr, 8 * K), tex, new Color(MCyan.r, MCyan.g, MCyan.b, 0.22f * alpha), ScaleMode.ScaleToFit);
            MTex(rr, tex, new Color(1f, 1f, 1f, alpha), ScaleMode.ScaleToFit);
        }

        void DrawMapCarousel(Rect strip, bool hostCtx)
        {
            float cardW = 270 * K, gap = 22 * K, cardH = strip.height;
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
                if (DrawMapCard(r, i, hostCtx)) SelectMap(i);
            }
            GUI.EndGroup();

            // setas
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

        bool DrawMapCard(Rect r, int i, bool hostCtx)
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
            var inner = Expand(frame, -10 * K);
            if (img != null)
            {
                var tint = m.playable ? Color.white : new Color(0.55f, 0.6f, 0.7f, 1f);
                MTex(inner, img, tint, ScaleMode.ScaleAndCrop);
            }
            // faixa com o nome
            var band = new Rect(inner.x, inner.yMax - 46 * K, inner.width, 46 * K);
            MFill(band, new Color(0.02f, 0.05f, 0.1f, 0.72f));
            MText(new Rect(band.x + 10 * K, band.y + 2 * K, band.width - 20 * K, 24 * K), m.name, m_MT17, sel ? MCyan : MWhite, TextAnchor.MiddleLeft);
            MText(new Rect(band.x + 10 * K, band.y + 22 * K, band.width - 20 * K, 20 * K), m.place, m_MT12, MMuted, TextAnchor.MiddleLeft);
            if (!m.playable)
            {
                var badge = new Rect(inner.xMax - 96 * K, inner.y + 8 * K, 88 * K, 24 * K);
                MDraw9(m_MFill, badge, Fade(new Color(0.05f, 0.1f, 0.2f, 0.9f), 1f));
                MText(badge, "EM BREVE", m_MT12, MCoral);
            }
            MDraw9(m_MLine, frame, Fade(sel ? MCyan : MCyanDim, sel ? 1f : 0.35f + 0.5f * h));

            if (GUI.enabled && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (!hostCtx) { MenuNotice("No modo JOIN é o host quem escolhe o mapa."); return false; }
                if (!m.playable) { MenuNotice(m.name + " ainda não está jogável. Por agora joga-se na Pradaria."); return false; }
                return true;
            }
            return false;
        }

        void SelectMap(int i)
        {
            m_MMapSel = i;
            PlayerPrefs.SetString(KeyMenuMap, s_Maps[i].key);
        }

        void DrawSetupActions(Rect r, bool hostCtx)
        {
            DrawTechPanel(r, false);
            float x = r.x + 18 * K, w = r.width - 36 * K, y = r.y + 12 * K;

            if (!hostCtx)
            {
                MText(new Rect(x, y, w, 40 * K), "O host escolhe o modo e o mapa.\nAqui escolhes só o teu personagem.", m_MT12, MMuted, TextAnchor.UpperCenter, true);
                y += 70 * K;
                if (MenuButton(new Rect(x, y, w, 56 * K), "CONFIRMAR PERSONAGEM", 5000, MBtn.Coral, false, m_MT17) == MClick.Clicked)
                {
                    SaveMenuPrefs();
                    BackToMain();
                }
                return;
            }

            var ips = m_LocalIps != null && m_LocalIps.Count > 0 ? m_LocalIps[0] : "127.0.0.1";
            MText(new Rect(x, y, w, 20 * K), "CÓDIGO DO SERVIDOR (LAN)", m_MT12, MMuted, TextAnchor.MiddleLeft);
            MText(new Rect(x, y + 16 * K, w, 26 * K), ips + "  ·  UDP " + ConnectionManager.DefaultPort, m_MT17, MCyan, TextAnchor.MiddleLeft);
            y += 48 * K;

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

            bool modeOk = m_MModeSel >= 0 && TryResolveMode(s_Modes[m_MModeSel], out _);
            bool mapOk = s_Maps[m_MMapSel].playable;
            var kind = modeOk && mapOk ? MBtn.Normal : MBtn.Disabled;
            float bw = (w - 10 * K) / 2f;
            var host = MenuButton(new Rect(x, y, bw, 56 * K), "START SERVER", 5004, kind == MBtn.Normal ? MBtn.Primary : MBtn.Disabled, false, m_MT14);
            var train = MenuButton(new Rect(x + bw + 10 * K, y, bw, 56 * K), "TREINO + BOTS", 5005, kind == MBtn.Normal ? MBtn.Coral : MBtn.Disabled, false, m_MT14);
            if (host == MClick.Clicked) StartHost(m_MenuBots);
            else if (train == MClick.Clicked) StartHost(Mathf.Max(3, m_MenuBots));
            else if (host == MClick.DisabledClicked || train == MClick.DisabledClicked)
                MenuNotice(!modeOk ? "Escolhe um modo disponível (o " + s_Modes[m_MModeSel].title + " ainda não está pronto)." : "Escolhe um mapa disponível.");
        }

        void StartHost(int bots)
        {
            if (NM != null && NM.IsListening) return;
            if (ConnectionManager.Instance == null) { MenuNotice("Erro: sistema de rede não encontrado na cena."); return; }
            if (string.IsNullOrWhiteSpace(m_Name)) { MenuNotice("Escreve o teu nome antes de criar o servidor."); GUI.FocusControl("mm_name_setup"); return; }
            SaveMenuPrefs();
            if (ConnectionManager.Instance.Host()) m_PendingBots = bots;
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

            // linha de ícones (página 2 do PDF)
            var row = new Rect(Screen.width - 250 * K, 22 * K + (1f - t) * -10f * K, 186 * K, 52 * K);
            DrawSolidPanel(row);
            bool muted = GameSession.Muted;
            if (MenuSpeakerButton(new Rect(row.x + 10 * K, row.y + 6 * K, 52 * K, 40 * K), true, muted, 6100))
                GameSession.Muted = !muted;
            if (MenuSpeakerButton(new Rect(row.x + 66 * K, row.y + 6 * K, 52 * K, 40 * K), false, false, 6101, "-"))
                SetMenuVolume(GameSession.MasterVolume - 0.1f);
            if (MenuSpeakerButton(new Rect(row.x + 122 * K, row.y + 6 * K, 52 * K, 40 * K), false, false, 6102, "+"))
                SetMenuVolume(GameSession.MasterVolume + 0.1f);

            // barra vertical de volume
            var pill = new Rect(Screen.width - 104 * K, 96 * K + (1f - t) * -14f * K, 64 * K, 270 * K);
            DrawSolidPanel(pill);
            var track = new Rect(pill.center.x - 14 * K, pill.y + 18 * K, 28 * K, pill.height - 90 * K);
            float v = muted ? 0f : GameSession.MasterVolume;
            float nv = MenuVerticalSlider(track, v, 6103);
            if (!Mathf.Approximately(nv, v)) SetMenuVolume(nv);
            MText(new Rect(pill.x, track.yMax + 6 * K, pill.width, 22 * K), Mathf.RoundToInt(v * 100f) + "%", m_MT14, muted ? MCoral : MWhite);
            MText(new Rect(pill.x, track.yMax + 26 * K, pill.width, 20 * K), muted ? "MUDO" : "VOLUME", m_MT12, MMuted);

            // mais definições (teclas, sensibilidade…)
            if (MenuButton(new Rect(Screen.width - 250 * K, pill.yMax + 12 * K, 210 * K, 40 * K), "DEFINIÇÕES", 6104, MBtn.Normal, false, m_MT14) == MClick.Clicked)
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
            MText(new Rect(r.x, r.y + 26 * K, r.width, 36 * K), server ? "A INICIAR O SERVIDOR" : "A LIGAR AO SERVIDOR", m_MT24, MWhite);
            // pontos animados
            for (int i = 0; i < 5; i++)
            {
                float p = Mathf.Repeat(Time.unscaledTime * 1.4f - i * 0.18f, 1f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(p - 0.5f) * 2.5f);
                MTex(new Rect(r.center.x - 62 * K + i * 26 * K, r.y + 80 * K, 14 * K, 14 * K), Art.CircleTexture, new Color(MCyan.r, MCyan.g, MCyan.b, 0.25f + 0.75f * a), ScaleMode.StretchToFill);
            }
            var status = ConnectionManager.Instance != null ? ConnectionManager.Instance.Status : "";
            if (string.IsNullOrEmpty(status) && !server) status = "A ligar a " + m_Ip + "...";
            MText(new Rect(r.x + 20 * K, r.y + 104 * K, r.width - 40 * K, 40 * K), status, m_MT14, MMuted, TextAnchor.MiddleCenter, true);
            if (MenuButton(new Rect(r.center.x - 90 * K, r.yMax - 64 * K, 180 * K, 46 * K), "CANCELAR", 7000, MBtn.Coral, false, m_MT17) == MClick.Clicked)
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
                else if (m_MJoinOpen) m_MJoinOpen = false;
                GUI.FocusControl(null);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                if (m_MScreen == MenuScreen.Main && m_MJoinOpen && (typing || m_MFocus == 1)) TryJoin();
                else if (m_MScreen == MenuScreen.Main && !typing) ActivateMainItem(m_MFocus);
                else if (m_MScreen == MenuScreen.Setup && !typing)
                {
                    if (m_MSetupCtx == SetupContext.Join) { SaveMenuPrefs(); BackToMain(); }
                    else if (m_MModeSel >= 0 && TryResolveMode(s_Modes[m_MModeSel], out _) && s_Maps[m_MMapSel].playable) StartHost(m_MenuBots);
                }
                e.Use();
                return;
            }

            if (typing) return;
            if (m_MScreen == MenuScreen.Main)
            {
                if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S) { m_MFocus = (m_MFocus + 1) % 4; e.Use(); }
                else if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W) { m_MFocus = (m_MFocus + 3) % 4; e.Use(); }
            }
            else
            {
                int c = Mathf.Max(0, MenuCharIndexForHero(m_Hero));
                if (e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.D) { SelectMenuCharacter((c + 1) % s_Roster.Length); e.Use(); }
                else if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.A) { SelectMenuCharacter((c + s_Roster.Length - 1) % s_Roster.Length); e.Use(); }
                else if (m_MSetupCtx == SetupContext.Host && (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S))
                { SelectMode((m_MModeSel + 1) % s_Modes.Length); e.Use(); }
                else if (m_MSetupCtx == SetupContext.Host && (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W))
                { SelectMode((m_MModeSel + s_Modes.Length - 1) % s_Modes.Length); e.Use(); }
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

        void SelectMenuCharacter(int menuIndex)
        {
            var b = MenuCharacterBrawler(menuIndex);
            if (b == null) return;
            int prev = MenuCharIndexForHero(m_Hero);
            if (prev == menuIndex) return;
            m_MCharPrev = prev;
            m_MCharT = 0f;
            m_Hero = b.runtimeIndex;
            GameSession.LocalHeroId = m_Hero;
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

        // ================================================================== fundo

        void DrawMenuBackground()
        {
            if (Event.current.type != EventType.Repaint) return;
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var old = GUI.color;
            GUI.color = MDeep;
            GUI.DrawTexture(screen, Texture2D.whiteTexture);

            var bg = MenuTexture("bg_archipelago");
            if (bg != null)
            {
                float t = Time.unscaledTime;
                float zoom = 1.06f + 0.03f * Mathf.Sin(t * 0.05f);
                var pan = new Vector2(Mathf.Sin(t * 0.031f) * 0.012f, Mathf.Cos(t * 0.027f) * 0.01f);
                GUI.color = new Color(0.85f, 0.92f, 1f, 1f);
                GUI.DrawTextureWithTexCoords(screen, bg, CoverUV(screen, bg, zoom, pan));
            }

            // escurecer à esquerda (legibilidade dos painéis) e vinheta
            GUI.color = new Color(0.01f, 0.03f, 0.08f, 0.55f);
            if (m_Vignette != null) GUI.DrawTexture(screen, m_Vignette);

            // nuvens nos cantos, a derivar devagar
            var clouds = MenuTexture("fx_clouds");
            if (clouds != null)
            {
                float t = Time.unscaledTime;
                float cw = Screen.height * 0.95f, chh = cw * 0.56f;
                GUI.color = new Color(1f, 1f, 1f, 0.42f);
                GUI.DrawTexture(new Rect(-cw * 0.45f + Mathf.Sin(t * 0.07f) * 18f, -chh * 0.35f, cw, chh), clouds);
                GUI.color = new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTexture(new Rect(-cw * 0.55f + Mathf.Cos(t * 0.05f) * 22f, Screen.height - chh * 0.6f, cw, chh), clouds);
            }
            GUI.color = old;
        }

        static Rect CoverUV(Rect screen, Texture t, float zoom, Vector2 pan)
        {
            float sa = screen.width / Mathf.Max(1f, screen.height), ta = t.width / (float)Mathf.Max(1, t.height);
            float uw = 1f, uh = 1f;
            if (sa > ta) uh = ta / sa; else uw = sa / ta;
            uw /= zoom; uh /= zoom;
            float cx = Mathf.Clamp(0.5f + pan.x, uw / 2f, 1f - uw / 2f);
            float cy = Mathf.Clamp(0.5f + pan.y, uh / 2f, 1f - uh / 2f);
            return new Rect(cx - uw / 2f, cy - uh / 2f, uw, uh);
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
            var oldColor = style.normal.textColor;
            var oldAnchor = style.alignment;
            var oldWrap = style.wordWrap;
            style.alignment = anchor;
            style.wordWrap = wrap;
            // sombra suave para destacar do fundo
            style.normal.textColor = new Color(0f, 0.02f, 0.06f, 0.6f * c.a * m_MAlpha);
            GUI.Label(new Rect(r.x + 1.5f * K, r.y + 2f * K, r.width, r.height), text, style);
            style.normal.textColor = new Color(c.r, c.g, c.b, c.a * m_MAlpha);
            GUI.Label(r, text, style);
            style.normal.textColor = oldColor;
            style.alignment = oldAnchor;
            style.wordWrap = oldWrap;
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
                try { m_MFont = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 32); }
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
                font = m_MFont,
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

        static Texture2D MakeRoundedFill(int size, int radius)
        {
            return MakeMenuTex(size, (x, y) =>
            {
                float d = RoundedDistance(x, y, size, radius);
                float a = Mathf.Clamp01(0.5f - d);
                float shade = 0.9f + 0.1f * (y / (float)size);   // ligeiro brilho no topo (efeito vidro)
                return new Color(shade, shade, shade, a);
            });
        }

        static Texture2D MakeRoundedStroke(int size, int radius, float width)
        {
            return MakeMenuTex(size, (x, y) =>
            {
                float d = RoundedDistance(x, y, size, radius);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(width + d + 0.5f);
                return new Color(1f, 1f, 1f, a);
            });
        }

        static Texture2D MakeGlow(int size, int falloff)
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
