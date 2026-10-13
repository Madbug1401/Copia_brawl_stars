using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// UI do MVP em IMGUI (zero assets): menu Host/Join/Treino, bots, HUD, barras de vida sobre os inimigos,
    /// placar, kill feed e pausa com troca de personagem. Substituir por uGUI/UI Toolkit na fase de polish.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        static GameUI s_Instance;

        public static bool GameplayInputAllowed => s_Instance != null && s_Instance.m_InGame && !s_Instance.m_Paused;

        bool m_InGame;
        bool m_Paused;

        string m_Name;
        string m_Ip;
        int m_Hero;
        int m_MenuBots;
        int m_PendingBots;
        List<string> m_LocalIps;

        float m_HitMarkerTimer;
        bool m_HitMarkerHead;
        bool m_HitMarkerKill;
        float m_DamageFlash;

        readonly List<(string text, float time)> m_KillFeed = new List<(string, float)>();
        readonly Dictionary<ulong, bool> m_Visible = new Dictionary<ulong, bool>();
        float m_NextVisCheck;

        GUIStyle m_Title, m_Label, m_Big, m_Small, m_Center, m_Button, m_Field, m_Tiny, m_TinyCenter;
        Texture2D m_White;

        static NetworkManager NM => NetworkManager.Singleton;

        void Awake()
        {
            s_Instance = this;
            m_Name = GameSession.LocalName;
            m_Ip = GameSession.LastIp;
            m_Hero = BrawlerRegistry.Sanitize(GameSession.LocalHeroId);
            m_MenuBots = PlayerPrefs.GetInt("fpslan.bots", 0);
            MatchManager.MenuSelectedMode = (GameMode)Mathf.Clamp(PlayerPrefs.GetInt("fpslan.mode", 0), 0, 2);
            BotManager.Difficulty = (BotDifficulty)PlayerPrefs.GetInt("fpslan.botdiff", (int)BotDifficulty.Normal);
            m_LocalIps = ConnectionManager.GetLocalIPv4();
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        public static void ShowHitMarker(bool headshot, bool killed)
        {
            if (s_Instance == null) return;
            s_Instance.m_HitMarkerTimer = killed ? 0.35f : 0.18f;
            s_Instance.m_HitMarkerHead = headshot;
            s_Instance.m_HitMarkerKill = killed;
        }

        public static void FlashDamage()
        {
            if (s_Instance != null) s_Instance.m_DamageFlash = 0.35f;
        }

        public static void AddKillFeed(string killer, string victim)
        {
            if (s_Instance == null) return;
            var text = string.IsNullOrEmpty(killer) ? victim + " morreu" : killer + "  ▸  " + victim;
            s_Instance.m_KillFeed.Add((text, Time.time));
            if (s_Instance.m_KillFeed.Count > 5) s_Instance.m_KillFeed.RemoveAt(0);
        }

        void Update()
        {
            m_InGame = NM != null && NM.IsListening && PlayerNetwork.Local != null;

            if (!m_InGame) m_Paused = false;
            else if (InputReader.PausePressed) m_Paused = !m_Paused;

            bool lockCursor = GameplayInputAllowed && Application.isFocused;
            Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !lockCursor;

            // Bots pedidos no menu: adicionados assim que o host está pronto
            if (m_PendingBots > 0 && m_InGame && NM.IsServer)
            {
                if (BotManager.AddBot()) m_PendingBots--;
                else m_PendingBots = 0;
            }

            m_HitMarkerTimer -= Time.deltaTime;
            m_DamageFlash -= Time.deltaTime;
            m_KillFeed.RemoveAll(k => Time.time - k.time > 6f);

            if (m_InGame && Time.time >= m_NextVisCheck)
            {
                m_NextVisCheck = Time.time + 0.15f;
                m_Visible.Clear();
                var me = PlayerNetwork.Local;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || p == me || !p.IsSpawned || p.Health.IsDead) continue;
                    m_Visible[p.Id] = CombatMath.HasLineOfSight(me, p);
                }
            }
        }

        // ---------------- IMGUI ----------------

        void EnsureStyles()
        {
            if (m_Title != null) return;

            m_White = Texture2D.whiteTexture;
            float s = Scale;

            m_Title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(44 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            m_Title.normal.textColor = new Color(1f, 0.82f, 0.3f);
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * s) };
            m_Label.normal.textColor = Color.white;
            m_Big = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(30 * s), fontStyle = FontStyle.Bold };
            m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(14 * s) };
            m_Tiny = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(12 * s), fontStyle = FontStyle.Bold };
            m_TinyCenter = new GUIStyle(m_Tiny) { alignment = TextAnchor.MiddleCenter };
            m_Center = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold };
            m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(20 * s), alignment = TextAnchor.MiddleLeft };
        }

        float Scale => Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);

        void OnGUI()
        {
            EnsureStyles();

            if (!m_InGame)
            {
                DrawMainMenu();
                return;
            }

            DrawOverheadBars();
            DrawHud();

            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned && match.Phase.Value == MatchPhase.Ended)
                DrawMatchResults(match);
            else if (InputReader.ScoreboardHeld)
                DrawScoreboard();

            if (m_Paused) DrawPauseMenu();
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, m_White);
            GUI.color = old;
        }

        void DrawHeroPicker(float s, ref int selected, bool applyNow)
        {
            GUILayout.BeginHorizontal();
            foreach (var b in BrawlerRegistry.All)
            {
                if (b == null) continue;
                int idx = b.runtimeIndex;
                GUI.enabled = b.available;
                bool isSel = selected == idx;
                var old = GUI.backgroundColor;
                if (isSel) GUI.backgroundColor = b.Tint;
                if (GUILayout.Toggle(isSel, b.displayName, m_Button, GUILayout.Height(46 * s)) && !isSel && b.available)
                {
                    selected = idx;
                    GameSession.LocalHeroId = idx;
                    if (applyNow && PlayerNetwork.Local != null) PlayerNetwork.Local.ChangeHero(idx);
                }
                GUI.backgroundColor = old;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            var sel = BrawlerRegistry.Get(selected);
            GUILayout.Label(sel != null ? sel.role : "", m_Small);
        }

        void DrawBotControls(float s, bool live)
        {
            GUILayout.BeginHorizontal();
            int count = live ? BotManager.BotCount : m_MenuBots;
            GUILayout.Label("Bots: " + count, m_Label, GUILayout.Width(110 * s));
            if (GUILayout.Button("−", m_Button, GUILayout.Width(44 * s), GUILayout.Height(34 * s)))
            {
                if (live) BotManager.RemoveBot();
                else m_MenuBots = Mathf.Max(0, m_MenuBots - 1);
            }
            if (GUILayout.Button("+", m_Button, GUILayout.Width(44 * s), GUILayout.Height(34 * s)))
            {
                if (live) BotManager.AddBot();
                else m_MenuBots = Mathf.Min(BotManager.MaxTotalPlayers - 1, m_MenuBots + 1);
            }
            GUILayout.Space(10 * s);
            if (GUILayout.Button("Dificuldade: " + BotManager.DifficultyLabel(BotManager.Difficulty), m_Button, GUILayout.Height(34 * s)))
            {
                BotManager.Difficulty = (BotDifficulty)(((int)BotManager.Difficulty + 1) % 3);
                PlayerPrefs.SetInt("fpslan.botdiff", (int)BotManager.Difficulty);
                BotManager.ApplyDifficulty();
            }
            GUILayout.EndHorizontal();
        }

        void DrawMainMenu()
        {
            float s = Scale;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));

            float w = 560 * s, h = 700 * s;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(area, new Color(0.08f, 0.09f, 0.12f, 0.92f));

            GUILayout.BeginArea(new Rect(area.x + 24 * s, area.y + 14 * s, area.width - 48 * s, area.height - 28 * s));
            GUILayout.Label("FPS-LAN", m_Title, GUILayout.Height(56 * s));
            GUILayout.Label("Deathmatch LAN · até 4 jogadores + bots", m_Center);
            GUILayout.Space(10 * s);

            GUILayout.Label("Nome", m_Small);
            m_Name = GUILayout.TextField(m_Name ?? "", 16, m_Field, GUILayout.Height(36 * s));

            GUILayout.Space(8 * s);
            GUILayout.Label("Personagem", m_Small);
            DrawHeroPicker(s, ref m_Hero, false);

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
            GUILayout.Space(6 * s);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("HOST", m_Button, GUILayout.Height(48 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = m_MenuBots;
            }
            if (GUILayout.Button("TREINO (com bots)", m_Button, GUILayout.Height(48 * s)))
            {
                SaveMenuPrefs();
                if (ConnectionManager.Instance != null && ConnectionManager.Instance.Host()) m_PendingBots = Mathf.Max(3, m_MenuBots);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10 * s);
            GUILayout.Label("IP do host", m_Small);
            GUILayout.BeginHorizontal();
            m_Ip = GUILayout.TextField(m_Ip ?? "", 40, m_Field, GUILayout.Height(40 * s));
            if (GUILayout.Button("JOIN", m_Button, GUILayout.Width(120 * s), GUILayout.Height(40 * s)))
            {
                SaveMenuPrefs();
                ConnectionManager.Instance?.Join(m_Ip);
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            if (busy && !m_InGame)
            {
                GUILayout.Space(6 * s);
                if (GUILayout.Button("Cancelar", m_Button, GUILayout.Height(34 * s)))
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
            GUILayout.Label("O teu IP na rede: " + ips + "   (porta " + ConnectionManager.DefaultPort + ")", m_Small);
            GUILayout.EndArea();
        }

        void CycleGameMode(int delta)
        {
            int count = 3;
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

        /// <summary>Nome + barra de vida por cima dos outros jogadores visíveis (estilo hero shooter arcade).</summary>
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

                var world = p.transform.position + Vector3.up * 2.25f;
                var sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f || sp.z > 45f) continue;

                float k = Mathf.Clamp(1.4f - sp.z / 40f, 0.55f, 1.2f) * s;
                float bw = 84f * k, bh = 9f * k;
                float x = sp.x - bw / 2f, y = Screen.height - sp.y;

                float hp01 = Mathf.Clamp01(p.Health.Health.Value / (float)Mathf.Max(1, p.Health.MaxHealth));
                Fill(new Rect(x - 2, y - 2, bw + 4, bh + 4), new Color(0, 0, 0, 0.65f));
                Fill(new Rect(x, y, bw * hp01, bh), Color.Lerp(new Color(0.95f, 0.2f, 0.2f), new Color(1f, 0.45f, 0.3f), hp01));
                if (p.Health.IsSlowed) Fill(new Rect(x, y + bh - 2, bw, 2), new Color(0.6f, 0.4f, 1f));

                var label = p.DisplayName + " · " + p.Brawler.displayName;
                GUI.Label(new Rect(sp.x - 120, y - 20 * k, 240, 18 * k), label, m_TinyCenter);
            }
        }

        void DrawHud()
        {
            var me = PlayerNetwork.Local;
            if (me == null) return;
            float s = Scale;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;

            if (m_DamageFlash > 0f)
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.8f, 0f, 0f, m_DamageFlash * 0.5f));
            if (me.Health.IsSlowed && !me.Health.IsDead)
                Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.45f, 0.2f, 0.8f, 0.12f));

            if (me.Health.IsDead)
            {
                DrawDeathOverlay(me);
            }
            else
            {
                float g = 5 * s, l = 9 * s, t = 2 * s;
                var cc = new Color(1f, 1f, 1f, 0.9f);
                Fill(new Rect(cx - t / 2, cy - g - l, t, l), cc);
                Fill(new Rect(cx - t / 2, cy + g, t, l), cc);
                Fill(new Rect(cx - g - l, cy - t / 2, l, t), cc);
                Fill(new Rect(cx + g, cy - t / 2, l, t), cc);
                Fill(new Rect(cx - 1, cy - 1, 2, 2), cc);

                if (m_HitMarkerTimer > 0f)
                {
                    var hc = m_HitMarkerKill ? new Color(1f, 0.25f, 0.2f) : (m_HitMarkerHead ? new Color(1f, 0.85f, 0.2f) : Color.white);
                    float d = 14 * s, sz = 7 * s;
                    var m = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
                    Fill(new Rect(cx - t / 2, cy - d - sz, t, sz), hc);
                    Fill(new Rect(cx - t / 2, cy + d, t, sz), hc);
                    Fill(new Rect(cx - d - sz, cy - t / 2, sz, t), hc);
                    Fill(new Rect(cx + d, cy - t / 2, sz, t), hc);
                    GUI.matrix = m;
                }

                if (me.Health.IsSlowed)
                    GUI.Label(new Rect(cx - 100, cy + 40 * s, 200, 24 * s), "ABRANDADO", m_TinyCenter);
            }

            // Vida
            float bw = 320 * s, bh = 26 * s;
            var hpRect = new Rect(30 * s, Screen.height - 70 * s, bw, bh);
            Fill(new Rect(hpRect.x - 3, hpRect.y - 3, hpRect.width + 6, hpRect.height + 6), new Color(0, 0, 0, 0.6f));
            float hp01 = Mathf.Clamp01(me.Health.Health.Value / (float)me.Health.MaxHealth);
            var hpColor = Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.3f, 0.9f, 0.4f), hp01);
            Fill(new Rect(hpRect.x, hpRect.y, hpRect.width * hp01, hpRect.height), hpColor);
            GUI.Label(new Rect(hpRect.x + 8 * s, hpRect.y - 1, bw, bh), me.Health.Health.Value + " / " + me.Health.MaxHealth, m_Label);
            GUI.Label(new Rect(hpRect.x, hpRect.y - 30 * s, bw * 2, 28 * s), me.Brawler.displayName + "  —  " + me.Brawler.role, m_Small);

            // Munição (cargas)
            int maxCharges = me.Brawler.primary.maxCharges;
            float ammo = me.Combat.Ammo;
            float segW = 90 * s, segH = 14 * s, gap = 6 * s;
            float totalW = maxCharges * segW + (maxCharges - 1) * gap;
            float ax = cx - totalW / 2f, ay = Screen.height - 60 * s;
            for (int i = 0; i < maxCharges; i++)
            {
                var r = new Rect(ax + i * (segW + gap), ay, segW, segH);
                Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0, 0, 0, 0.6f));
                float fill = Mathf.Clamp01(ammo - i);
                Fill(new Rect(r.x, r.y, r.width * fill, r.height), fill >= 1f ? new Color(1f, 0.75f, 0.2f) : new Color(0.6f, 0.45f, 0.15f));
            }

            // Super
            float sup = me.Combat.SuperCharge.Value;
            var supRect = new Rect(Screen.width - 30 * s - 220 * s, Screen.height - 70 * s, 220 * s, bh);
            Fill(new Rect(supRect.x - 3, supRect.y - 3, supRect.width + 6, supRect.height + 6), new Color(0, 0, 0, 0.6f));
            bool ready = me.Combat.SuperReady;
            var supColor = ready ? Color.Lerp(new Color(1f, 0.6f, 0.1f), new Color(1f, 0.9f, 0.3f), Mathf.PingPong(Time.time * 3f, 1f)) : new Color(0.9f, 0.55f, 0.15f);
            Fill(new Rect(supRect.x, supRect.y, supRect.width * sup, supRect.height), supColor);
            GUI.Label(new Rect(supRect.x + 8 * s, supRect.y - 1, supRect.width, bh), ready ? "SUPER PRONTO  [Botão dir. / E]" : "SUPER " + Mathf.RoundToInt(sup * 100) + "%", m_Small);

            // Topo: tempo + K/D
            var match = MatchManager.Instance;
            if (match != null && match.IsSpawned)
            {
                float tr = match.TimeRemaining;
                string time = match.Phase.Value == MatchPhase.Playing ? $"{(int)(tr / 60)}:{(int)(tr % 60):00}" : "FIM";
                var top = new Rect(cx - 170 * s, 12 * s, 340 * s, 44 * s);
                Fill(top, new Color(0, 0, 0, 0.5f));
                string modeScore = match.Mode.Value == GameMode.Deathmatch
                    ? $"K {me.Kills.Value}  ·  D {me.Deaths.Value}    (meta {match.killLimit})"
                    : $"AZUL {match.BlueScore.Value}  -  {match.RedScore.Value} VERMELHA";
                string modeName = match.Mode.Value == GameMode.Wipeout ? "WIPEOUT" :
                    match.Mode.Value == GameMode.Knockout ? $"KNOCKOUT · RONDA {match.RoundNumber.Value}" : "DEATHMATCH";
                GUI.Label(top, $"{modeName}   {time}   {modeScore}", m_Center);
            }

            float ky = 14 * s;
            foreach (var k in m_KillFeed)
            {
                var r = new Rect(Screen.width - 400 * s, ky, 380 * s, 28 * s);
                Fill(r, new Color(0, 0, 0, 0.5f));
                GUI.Label(new Rect(r.x + 8 * s, r.y, r.width, r.height), k.text, m_Label);
                ky += 32 * s;
            }

            GUI.Label(new Rect(16 * s, 12 * s, 400 * s, 24 * s), "Tab: placar · Esc: menu / personagem / bots", m_Small);
        }

        void DrawDeathOverlay(PlayerNetwork me)
        {
            float s = Scale;
            var killer = PlayerNetwork.Find(me.Health.LastKiller.Value);
            string who = killer != null && killer != me ? "Eliminado por " + killer.DisplayName + " (" + killer.Brawler.displayName + ")" : "Morreste";
            var r = new Rect(0, Screen.height * 0.3f, Screen.width, 60 * s);
            GUI.Label(r, who, new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter });
            if (!MatchManager.IsMatchOver)
                GUI.Label(new Rect(0, r.yMax, Screen.width, 40 * s), "A reaparecer em " + Mathf.CeilToInt(me.Health.RespawnRemaining) + "...  (Esc para trocar de personagem)", m_Center);
        }

        void DrawScoreboard()
        {
            float s = Scale;
            var players = PlayerNetwork.All.Where(p => p != null && p.IsSpawned)
                .OrderByDescending(p => p.Kills.Value).ThenBy(p => p.Deaths.Value).ToList();

            float w = 600 * s, rowH = 38 * s;
            float h = 90 * s + rowH * players.Count;
            var area = new Rect((Screen.width - w) / 2f, Screen.height * 0.15f, w, h);
            Fill(area, new Color(0.05f, 0.06f, 0.09f, 0.88f));

            GUI.Label(new Rect(area.x, area.y + 8 * s, w, 40 * s), "PLACAR", new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter });
            float y = area.y + 52 * s;
            DrawRow(area.x, y, w, "Jogador", "Personagem", "K", "D", m_Small, Color.clear);
            y += 26 * s;
            foreach (var p in players)
            {
                var bg = p == PlayerNetwork.Local ? new Color(1f, 0.8f, 0.3f, 0.15f) : new Color(1f, 1f, 1f, 0.04f);
                var name = p.IsBot ? p.DisplayName + " (bot)" : p.DisplayName;
                DrawRow(area.x, y, w, name, p.Brawler.displayName, p.Kills.Value.ToString(), p.Deaths.Value.ToString(), m_Label, bg);
                y += rowH;
            }
        }

        void DrawRow(float x, float y, float w, string a, string b, string k, string d, GUIStyle style, Color bg)
        {
            float s = Scale;
            if (bg.a > 0f) Fill(new Rect(x + 10 * s, y, w - 20 * s, 34 * s), bg);
            GUI.Label(new Rect(x + 24 * s, y + 4 * s, w * 0.45f, 30 * s), a, style);
            GUI.Label(new Rect(x + w * 0.5f, y + 4 * s, w * 0.28f, 30 * s), b, style);
            GUI.Label(new Rect(x + w * 0.8f, y + 4 * s, 60 * s, 30 * s), k, style);
            GUI.Label(new Rect(x + w * 0.9f, y + 4 * s, 60 * s, 30 * s), d, style);
        }

        void DrawMatchResults(MatchManager match)
        {
            float s = Scale;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(0, Screen.height * 0.04f, Screen.width, 60 * s), "VENCEDOR: " + match.WinnerName.Value.ToString(), new GUIStyle(m_Title) { fontSize = Mathf.RoundToInt(40 * s) });
            GUI.Label(new Rect(0, Screen.height * 0.04f + 56 * s, Screen.width, 30 * s), "Nova partida em " + Mathf.CeilToInt(match.TimeRemaining) + "s", m_Center);
            DrawScoreboard();
        }

        void DrawPauseMenu()
        {
            float s = Scale;
            bool host = NM != null && NM.IsServer;
            float w = 560 * s, h = (host ? 470 : 390) * s;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(area, new Color(0.06f, 0.07f, 0.1f, 0.96f));
            GUILayout.BeginArea(new Rect(area.x + 20 * s, area.y + 14 * s, area.width - 40 * s, area.height - 28 * s));
            GUILayout.Label("PAUSA", new GUIStyle(m_Big) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(6 * s);

            GUILayout.Label("Personagem (troca já)", m_Small);
            var me = PlayerNetwork.Local;
            int current = me != null ? me.HeroId.Value : m_Hero;
            int sel = current;
            DrawHeroPicker(s, ref sel, true);
            m_Hero = sel;

            GUILayout.Space(8 * s);
            GUILayout.Label("Sensibilidade do rato: " + GameSession.Sensitivity.ToString("0.0"), m_Label);
            float sens = GUILayout.HorizontalSlider(GameSession.Sensitivity, 0.2f, 8f);
            if (!Mathf.Approximately(sens, GameSession.Sensitivity)) GameSession.Sensitivity = sens;

            if (host)
            {
                GUILayout.Space(12 * s);
                DrawBotControls(s, true);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Continuar", m_Button, GUILayout.Height(42 * s))) m_Paused = false;
            GUILayout.Space(6 * s);
            var leaveLabel = host ? "Terminar partida (host)" : "Sair da partida";
            if (GUILayout.Button(leaveLabel, m_Button, GUILayout.Height(42 * s)))
            {
                m_Paused = false;
                PlayerPrefs.Save();
                ConnectionManager.Instance?.Leave();
            }
            GUILayout.EndArea();
        }
    }
}
