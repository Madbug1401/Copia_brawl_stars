using System;
using System.Collections.Generic;
using UnityEngine;
using static FPSLAN.ArcadeGUI;

namespace FPSLAN
{
    public enum ProgressionScreen
    {
        None,
        Profile,
        Characters,
        Levels,
        Skins,
        Pets,
        Quests,
        Rewards,
    }

    /// <summary>
    /// Ecrãs de progressão (IMGUI, estética arcade): perfil, personagens e atributos, progressão de níveis, inventário
    /// de skins, seleção e evolução de pets, missões, recompensas e resultados pós-partida. Também mostra os avisos de
    /// feedback (subida de nível, desbloqueios, missão concluída, vitória...) com som.
    /// Criado pelo GameUI (sem alterações à cena).
    /// </summary>
    public class ProgressionUI : MonoBehaviour
    {
        static ProgressionUI s_Instance;

        public static bool IsOpen => s_Instance != null && (s_Instance.m_Screen != ProgressionScreen.None || s_Instance.m_ShowReport);

        ProgressionScreen m_Screen = ProgressionScreen.None;
        BrawlerPreview m_Preview;

        // Seleções
        int m_Brawler = -1;
        bool m_SkinsForPets;
        string m_SkinTarget = "";
        string m_SelectedSkin = "";
        string m_SelectedPet = "";
        float m_UpraBoughtAt = -10f;
        Vector2 m_UpraBuyFrom, m_UpraBuyTo;
        bool m_PetEvolution;
        bool m_ConfirmReset;
        Vector2 m_Scroll1, m_Scroll2;
        string m_Message = "";
        float m_MessageTime;

        // Resultados pós-partida
        MatchRewardReport m_Report;
        float m_ReportShownAt;
        bool m_ShowReport;

        // Avisos de feedback
        struct Banner
        {
            public FeedbackEvent e;
            public float time;
        }

        readonly List<Banner> m_Banners = new List<Banner>();
        readonly Queue<FeedbackEvent> m_Queue = new Queue<FeedbackEvent>();
        float m_LastSound = -10f;
        GameExperienceContext m_Ctx;

        static readonly string[] TabLabels = { "PROFILE", "CHARACTERS", "LEVELS", "SKINS", "PETS", "QUESTS", "REWARDS" };
        static readonly ProgressionScreen[] Tabs =
        {
            ProgressionScreen.Profile, ProgressionScreen.Characters, ProgressionScreen.Levels, ProgressionScreen.Skins,
            ProgressionScreen.Pets, ProgressionScreen.Quests, ProgressionScreen.Rewards,
        };

        // ================================================================== API

        public static void Open(ProgressionScreen screen)
        {
            if (s_Instance == null) return;
            s_Instance.m_Screen = screen;
            s_Instance.m_ShowReport = false;
            s_Instance.m_ConfirmReset = false;
            s_Instance.m_Ctx.Quests.RefreshPeriods();
        }

        public static void Close()
        {
            if (s_Instance == null) return;
            s_Instance.m_Screen = ProgressionScreen.None;
            s_Instance.m_ShowReport = false;
            s_Instance.m_Preview?.Hide();
        }

        /// <summary>Resultado da partida chegou do host (já aplicado ao perfil).</summary>
        public static void ShowMatchReport(MatchRewardReport report)
        {
            if (s_Instance == null || report == null || report.duplicate) return;
            s_Instance.m_Report = report;
            s_Instance.m_ReportShownAt = Time.realtimeSinceStartup;
        }

        /// <summary>Há resultados de progressão para esta partida? (o ecrã de resultados do GameUI desenha-os)</summary>
        public static bool HasReportFor(string matchId) =>
            s_Instance != null && s_Instance.m_Report != null && !string.IsNullOrEmpty(matchId) && s_Instance.m_Report.matchId == matchId;

        public static void DrawReportPanel(Rect r)
        {
            if (s_Instance != null && s_Instance.m_Report != null) s_Instance.DrawReport(r, s_Instance.m_Report);
        }

        // ================================================================== ciclo de vida

        void Awake()
        {
            s_Instance = this;
            useGUILayout = false;   // só usa GUI.* (sem GUILayout): o Unity deixa de enviar o evento Layout
            m_Ctx = GameExperience.Current;
            m_Ctx.Feedback += OnFeedback;
            m_Preview = new BrawlerPreview(new Vector3(80f, -400f, 0f));
        }

        void OnDestroy()
        {
            if (m_Ctx != null) m_Ctx.Feedback -= OnFeedback;
            if (s_Instance == this) s_Instance = null;
            m_Preview?.Dispose();
        }

        void OnFeedback(FeedbackEvent e)
        {
            m_Queue.Enqueue(e);
            if (Time.unscaledTime - m_LastSound < 0.25f) return;
            m_LastSound = Time.unscaledTime;
            Sfx.Play2D(SoundFor(e.type), 0.75f);
        }

        static Sound SoundFor(FeedbackType t)
        {
            switch (t)
            {
                case FeedbackType.LevelUp:
                case FeedbackType.CharacterLevelUp: return Sound.LevelUp;
                case FeedbackType.CharacterUnlocked:
                case FeedbackType.PetUnlocked:
                case FeedbackType.SkinUnlocked: return Sound.Unlock;
                case FeedbackType.PetLevelUp: return Sound.PetHappy;
                case FeedbackType.PetEvolved: return Sound.Evolve;
                case FeedbackType.QuestCompleted:
                case FeedbackType.AchievementUnlocked: return Sound.QuestComplete;
                case FeedbackType.Victory: return Sound.Victory;
                case FeedbackType.Headshot: return Sound.HeadHit;
                case FeedbackType.SpecialReady: return Sound.SuperReady;
                default: return Sound.Reward;
            }
        }

        void Update()
        {
            // Avisos: até 3 visíveis, 2,8 s cada
            float now = Time.unscaledTime;
            m_Banners.RemoveAll(b => now - b.time > 2.8f);
            while (m_Banners.Count < 3 && m_Queue.Count > 0) m_Banners.Add(new Banner { e = m_Queue.Dequeue(), time = now });

            UpdatePreview();
            if (m_Preview != null) m_Preview.Tick(Time.unscaledDeltaTime);
        }

        void UpdatePreview()
        {
            var ctx = m_Ctx;
            switch (m_Screen)
            {
                case ProgressionScreen.Characters:
                {
                    var b = BrawlerRegistry.Get(SelectedBrawlerIndex());
                    var pet = ctx.Pets.EquippedPet;
                    m_Preview.Show(SelectedBrawlerIndex(), b != null ? ctx.Skins.EquippedSkinFor(SkinTarget.Brawler, b.brawlerId) : null,
                        pet, pet != null ? ctx.PetProgression.Level(pet.petId) : 1, pet != null ? ctx.Skins.EquippedSkinFor(SkinTarget.Pet, pet.petId) : null);
                    break;
                }
                case ProgressionScreen.Skins:
                {
                    var skin = ctx.SkinCatalog.Find(m_SelectedSkin);
                    if (m_SkinsForPets)
                    {
                        var pet = ctx.PetCatalog.Find(m_SkinTarget);
                        m_Preview.ShowPet(pet, pet != null ? Mathf.Max(1, ctx.PetProgression.Level(pet.petId)) : 1, skin);
                    }
                    else
                    {
                        var b = BrawlerRegistry.FindById(m_SkinTarget);
                        m_Preview.Show(b != null ? b.runtimeIndex : 0, skin);
                    }
                    break;
                }
                case ProgressionScreen.Pets:
                {
                    var pet = ctx.PetCatalog.Find(m_SelectedPet);
                    m_Preview.ShowPet(pet, pet != null ? ctx.PetProgression.Level(pet.petId) : 1, pet != null ? ctx.Skins.EquippedSkinFor(SkinTarget.Pet, pet.petId) : null);
                    break;
                }
                default:
                    m_Preview.Hide();
                    break;
            }
        }

        int SelectedBrawlerIndex()
        {
            if (m_Brawler < 0)
            {
                var me = PlayerNetwork.Local;
                m_Brawler = me != null ? me.HeroId.Value : BrawlerRegistry.Sanitize(GameSession.LocalHeroId);
            }
            return BrawlerRegistry.Sanitize(m_Brawler);
        }

        void Say(string text)
        {
            m_Message = text;
            m_MessageTime = Time.unscaledTime;
        }

        // ================================================================== OnGUI

        void OnGUI()
        {
            GUI.depth = -10;
            GUI.skin = MenuTheme.Skin;
            EnsureStyles();
            if (m_Screen != ProgressionScreen.None) DrawFrame();
            else if (m_ShowReport && m_Report != null) DrawReportOverlay();
            DrawBanners();
        }

        void DrawFrame()
        {
            float s = Scale;
            GameUI.DrawSharedMenuBackground();
            float w = Mathf.Min(1240 * s, UnityEngine.Screen.width - 24 * s);
            float h = Mathf.Min(800 * s, UnityEngine.Screen.height - 24 * s);
            var area = new Rect((UnityEngine.Screen.width - w) / 2f, (UnityEngine.Screen.height - h) / 2f, w, h);
            InkPanel(area, PanelDark, 4f);

            Outlined(new Rect(area.x + 20 * s, area.y + 8 * s, 420 * s, 46 * s), ScreenTitle(m_Screen), Big, MenuTheme.White, 2f);
            DrawHeaderStatus(new Rect(area.xMax - 560 * s, area.y + 12 * s, 400 * s, 40 * s));
            if (Btn(new Rect(area.xMax - 140 * s, area.y + 12 * s, 122 * s, 40 * s), "BACK", new Color(0.4f, 0.46f, 0.6f))) Close();

            // Separadores (com contadores de recompensas por reclamar)
            float tabW = (area.width - 40 * s - 6 * 6 * s) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var r = new Rect(area.x + 20 * s + i * (tabW + 6 * s), area.y + 62 * s, tabW, 38 * s);
                bool sel = m_Screen == Tabs[i];
                if (Btn(r, TabLabels[i], sel ? PanelBlue : new Color(0.22f, 0.24f, 0.34f), true, ButtonSmall)) Open(Tabs[i]);
                int badge = TabBadge(Tabs[i]);
                if (badge > 0)
                {
                    float d = 22 * s;
                    var br = new Rect(r.xMax - d * 0.7f, r.y - d * 0.35f, d, d);
                    Tex(br, Art.CircleTexture, new Color(1f, 0.3f, 0.25f));
                    Outlined(br, badge.ToString(), TinyCenter, Color.white, 1f);
                }
            }

            var content = new Rect(area.x + 20 * s, area.y + 112 * s, area.width - 40 * s, area.height - 128 * s);
            switch (m_Screen)
            {
                case ProgressionScreen.Profile: DrawProfile(content); break;
                case ProgressionScreen.Characters: DrawCharacters(content); break;
                case ProgressionScreen.Levels: DrawLevels(content); break;
                case ProgressionScreen.Skins: DrawSkins(content); break;
                case ProgressionScreen.Pets: DrawPets(content); break;
                case ProgressionScreen.Quests: DrawQuests(content); break;
                case ProgressionScreen.Rewards: DrawRewards(content); break;
            }

            if (!string.IsNullOrEmpty(m_Message) && Time.unscaledTime - m_MessageTime < 3f)
                Outlined(new Rect(area.x, area.yMax - 30 * s, area.width, 26 * s), m_Message, SmallCenter, new Color(1f, 0.9f, 0.6f), 1.2f);
        }

        static string ScreenTitle(ProgressionScreen s)
        {
            switch (s)
            {
                case ProgressionScreen.Profile: return "PLAYER PROFILE";
                case ProgressionScreen.Characters: return "CHARACTERS · STATS";
                case ProgressionScreen.Levels: return "LEVEL PROGRESSION";
                case ProgressionScreen.Skins: return "SKIN COLLECTION";
                case ProgressionScreen.Pets: return "COMPANIONS";
                case ProgressionScreen.Quests: return "QUESTS";
                case ProgressionScreen.Rewards: return "REWARDS";
                default: return "";
            }
        }

        int TabBadge(ProgressionScreen screen)
        {
            if (screen == ProgressionScreen.Quests)
            {
                int n = 0;
                foreach (var q in m_Ctx.QuestCatalog.quests) if (q != null && m_Ctx.Quests.CanClaim(q)) n++;
                return n;
            }
            if (screen == ProgressionScreen.Rewards) return m_Ctx.Profile.pendingLevelRewards.Count;
            return 0;
        }

        /// <summary>Nível, barra de XP e moedas (cabeçalho dos ecrãs e menu principal).</summary>
        public static void DrawHeaderStatus(Rect r)
        {
            EnsureStyles();
            var ctx = GameExperience.Current;
            float s = Scale;
            int level = ctx.Progression.PlayerLevel;
            Badge(new Rect(r.x, r.y, r.height, r.height), level.ToString(), new Color(0.35f, 0.55f, 1f));
            var curve = ctx.Config.playerLevels;
            curve.Progress(ctx.Profile.playerXp, out _, out int into, out int span);
            var barRect = new Rect(r.x + r.height + 8 * s, r.y + r.height * 0.22f, Mathf.Max(50 * s, r.width - r.height - 112 * s), r.height * 0.56f);
            Bar(barRect, span > 0 ? into / (float)span : 1f, new Color(0.35f, 0.8f, 1f), span > 0 ? into + " / " + span + " XP" : "MAX LEVEL");
            var coinRect = new Rect(barRect.xMax + 14 * s, r.y + r.height * 0.18f, r.height * 0.64f, r.height * 0.64f);
            Tex(coinRect, Art.CircleTexture, Ink);
            Tex(new Rect(coinRect.x + 2 * s, coinRect.y + 2 * s, coinRect.width - 4 * s, coinRect.height - 4 * s), Art.CircleTexture, Gold);
            Outlined(new Rect(coinRect.xMax + 6 * s, r.y, Mathf.Max(30 * s, r.xMax - coinRect.xMax - 6 * s), r.height), ctx.Inventory.Coins.ToString(), Label, Gold, 1.5f);
        }

        // ================================================================== PERFIL

        void DrawProfile(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var p = ctx.Profile;
            var left = new Rect(c.x, c.y, c.width * 0.42f, c.height);
            InkPanel(left, PanelMid, 2f);
            float y = left.y + 14 * s;
            Outlined(new Rect(left.x + 16 * s, y, left.width - 32 * s, 34 * s), GameSession.LocalName.ToUpperInvariant(), Big, Color.white, 2f);
            y += 36 * s;
            Outlined(new Rect(left.x + 16 * s, y, left.width - 32 * s, 20 * s), "ID " + (p.playerId.Length > 8 ? p.playerId.Substring(0, 8) : p.playerId) + " · local profile", Tiny, Muted, 1f);
            y += 28 * s;
            int level = ctx.Progression.PlayerLevel;
            Badge(new Rect(left.x + 16 * s, y, 70 * s, 70 * s), level.ToString(), new Color(0.35f, 0.55f, 1f));
            var curve = ctx.Config.playerLevels;
            curve.Progress(p.playerXp, out _, out int into, out int span);
            Outlined(new Rect(left.x + 100 * s, y + 2 * s, 300 * s, 24 * s), level >= curve.maxLevel ? "MAX LEVEL" : "LEVEL " + level + " → " + (level + 1), Label, Color.white, 1.5f);
            Bar(new Rect(left.x + 100 * s, y + 32 * s, left.width - 120 * s, 22 * s), span > 0 ? into / (float)span : 1f, new Color(0.35f, 0.8f, 1f),
                span > 0 ? into + " / " + span + " XP" : p.playerXp + " XP");
            y += 84 * s;
            Outlined(new Rect(left.x + 16 * s, y, 400 * s, 22 * s), "Total XP: " + p.playerXp + "   ·   Coins: " + p.coins, Small, Gold, 1f);
            y += 34 * s;

            var st = p.stats;
            string[,] rows =
            {
                { "Matches", st.matches.ToString() }, { "Valid matches", st.validMatches.ToString() }, { "Wins", st.wins.ToString() },
                { "Eliminations", st.kills.ToString() }, { "Assists", st.assists.ToString() }, { "Deaths", st.deaths.ToString() },
                { "Headshots", st.headshots.ToString() }, { "Objectives", st.objectives.ToString() }, { "Total damage", st.damageDealt.ToString() },
                { "Competitive matches", st.competitiveMatches.ToString() }, { "Quests completed", p.questsCompletedTotal.ToString() },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                var rr = new Rect(left.x + 16 * s, y, left.width - 32 * s, 24 * s);
                if (i % 2 == 0) Panel(rr, new Color(1f, 1f, 1f, 0.05f));
                Outlined(new Rect(rr.x + 8 * s, rr.y, rr.width, rr.height), rows[i, 0], Small, Muted, 1f);
                Outlined(new Rect(rr.x, rr.y, rr.width - 8 * s, rr.height), rows[i, 1], SmallRight, Color.white, 1f);
                y += 25 * s;
            }

            y = left.yMax - 50 * s;
            float bw = (left.width - 48 * s) / 2f;
            if (Btn(new Rect(left.x + 16 * s, y, bw, 36 * s), "LAST MATCH", PanelBlue, ctx.Progression.LastReport != null, ButtonSmall))
            {
                m_Report = ctx.Progression.LastReport;
                m_ReportShownAt = Time.realtimeSinceStartup;
                m_Screen = ProgressionScreen.None;
                m_ShowReport = true;
            }
            if (!m_ConfirmReset)
            {
                if (Btn(new Rect(left.x + 32 * s + bw, y, bw, 36 * s), "RESET PROGRESS", new Color(0.55f, 0.25f, 0.25f), true, ButtonSmall)) m_ConfirmReset = true;
            }
            else if (Btn(new Rect(left.x + 32 * s + bw, y, bw, 36 * s), "CONFIRM RESET", Bad, true, ButtonSmall))
            {
                ctx.ResetProfile();
                m_ConfirmReset = false;
                Say("Progress reset. New profile created.");
            }

            // Conquistas e desafios
            var right = new Rect(left.xMax + 16 * s, c.y, c.width - left.width - 16 * s, c.height);
            InkPanel(right, PanelMid, 2f);
            Outlined(new Rect(right.x + 16 * s, right.y + 10 * s, right.width, 28 * s), "ACHIEVEMENTS & CHALLENGES", Label, Gold, 1.5f);
            var list = ctx.QuestCatalog.achievements;
            var view = new Rect(right.x + 10 * s, right.y + 44 * s, right.width - 20 * s, right.height - 54 * s);
            float rowH = 64 * s;
            m_Scroll1 = GUI.BeginScrollView(view, m_Scroll1, new Rect(0, 0, view.width - 20 * s, list.Count * (rowH + 6 * s)));
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                var rr = new Rect(0, i * (rowH + 6 * s), view.width - 20 * s, rowH);
                bool done = ctx.Quests.IsAchieved(a);
                Panel(rr, done ? new Color(0.25f, 0.45f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.06f));
                Outlined(new Rect(rr.x + 10 * s, rr.y + 4 * s, rr.width * 0.65f, 22 * s), (a.isChallenge ? "CHALLENGE · " : "") + a.title, Label, done ? Gold : Color.white, 1.2f);
                Outlined(new Rect(rr.x + 10 * s, rr.y + 26 * s, rr.width * 0.62f, 34 * s), a.description, Tiny, Muted, 1f);
                int prog = Mathf.Min(ctx.Quests.AchievementProgress(a), a.target);
                Bar(new Rect(rr.xMax - rr.width * 0.33f, rr.y + 8 * s, rr.width * 0.31f, 20 * s), prog / (float)a.target, done ? Good : new Color(0.95f, 0.7f, 0.25f), prog + "/" + a.target);
                Outlined(new Rect(rr.xMax - rr.width * 0.33f, rr.y + 32 * s, rr.width * 0.31f, 26 * s), a.reward.Describe(ctx.SkinCatalog, ctx.PetCatalog), Tiny, Gold, 1f);
            }
            GUI.EndScrollView();
        }

        // ================================================================== PERSONAGENS

        void DrawCharacters(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            int selected = SelectedBrawlerIndex();
            var all = BrawlerRegistry.All;

            // Lista
            var list = new Rect(c.x, c.y, 290 * s, c.height);
            float cardH = Mathf.Min(78 * s, (list.height - 8 * s * (all.Count - 1)) / Mathf.Max(1, all.Count));
            int i = 0;
            foreach (var b in all)
            {
                if (b == null) continue;
                var r = new Rect(list.x, list.y + i * (cardH + 8 * s), list.width, cardH);
                bool sel = b.runtimeIndex == selected;
                bool unlocked = ctx.Inventory.IsCharacterUnlocked(b.brawlerId);
                InkPanel(r, sel ? Color.Lerp(b.Tint, Color.white, 0.12f) : Art.Darken(b.Tint, unlocked ? 0.5f : 0.25f), sel ? 3f : 2f);
                int lvl = ctx.Progression.CharacterLevel(b.brawlerId);
                Badge(new Rect(r.x + 8 * s, r.y + (cardH - 44 * s) / 2f, 44 * s, 44 * s), lvl.ToString(), Art.Darken(b.Tint, 0.75f));
                Outlined(new Rect(r.x + 60 * s, r.y + 6 * s, r.width - 66 * s, 24 * s), b.displayName, Label, Color.white, 1.5f);
                Outlined(new Rect(r.x + 60 * s, r.y + 28 * s, r.width - 66 * s, 18 * s), unlocked ? ShortRole(b) : "LOCKED", Tiny, new Color(1f, 1f, 1f, 0.85f), 1f);
                var cp = ctx.Profile.Character(b.brawlerId, false);
                Bar(new Rect(r.x + 60 * s, r.yMax - 18 * s, r.width - 72 * s, 10 * s), ctx.Config.characterLevels.Progress01(cp != null ? cp.xp : 0), Gold, "");
                if (Hit(r)) m_Brawler = b.runtimeIndex;
                i++;
            }

            var brawler = BrawlerRegistry.Get(selected);
            if (brawler == null) return;
            string id = brawler.brawlerId;

            // Pré-visualização
            var pv = new Rect(list.xMax + 14 * s, c.y, 300 * s, c.height);
            InkPanel(pv, PanelMid, 2f);
            var tex = new Rect(pv.x + 6 * s, pv.y + 6 * s, pv.width - 12 * s, pv.height - 150 * s);
            Tex(new Rect(tex.x + tex.width * 0.05f, tex.y + tex.height * 0.2f, tex.width * 0.9f, tex.height * 0.75f), Art.CircleTexture, Art.WithAlpha(brawler.Tint, 0.18f));
            if (m_Preview.Texture != null) GUI.DrawTexture(tex, m_Preview.Texture, ScaleMode.ScaleToFit, true);
            var skin = ctx.Skins.EquippedSkinFor(SkinTarget.Brawler, id);
            var pet = ctx.Pets.EquippedPet;
            Outlined(new Rect(pv.x + 10 * s, pv.yMax - 140 * s, pv.width - 20 * s, 20 * s), "Skin: " + (skin != null ? skin.displayName : "Original"), Small, Color.white, 1f);
            Outlined(new Rect(pv.x + 10 * s, pv.yMax - 118 * s, pv.width - 20 * s, 20 * s), "Pet: " + (pet != null ? pet.displayName + " (lv " + ctx.PetProgression.Level(pet.petId) + ")" : "none"), Small, Color.white, 1f);
            var me = PlayerNetwork.Local;
            bool current = me != null ? me.HeroId.Value == selected : GameSession.LocalHeroId == selected;
            bool locked = me != null && MatchManager.Instance != null && (MatchManager.Instance.IsCoop || MatchManager.Instance.Mode.Value == GameMode.GemGrab);
            if (Btn(new Rect(pv.x + 10 * s, pv.yMax - 90 * s, pv.width - 20 * s, 36 * s), current ? "SELECTED" : locked ? "LOCKED DURING MATCH" : "SELECT TO PLAY", current ? new Color(0.3f, 0.5f, 0.3f) : Good,
                    !locked && !current && ctx.Inventory.IsCharacterUnlocked(id)))
            {
                GameSession.LocalHeroId = selected;
                if (me != null) me.ChangeHero(selected);
                Prefs.Save();
            }
            if (Btn(new Rect(pv.x + 10 * s, pv.yMax - 46 * s, pv.width - 20 * s, 34 * s), "CHARACTER SKINS", PanelBlue, true, ButtonSmall))
            {
                m_SkinsForPets = false;
                m_SkinTarget = id;
                m_SelectedSkin = skin != null ? skin.skinId : "";
                Open(ProgressionScreen.Skins);
            }

            // Detalhes e atributos
            var d = new Rect(pv.xMax + 14 * s, c.y, c.xMax - pv.xMax - 14 * s, c.height);
            InkPanel(d, PanelMid, 2f);
            float y = d.y + 10 * s;
            Outlined(new Rect(d.x + 16 * s, y, d.width - 32 * s, 34 * s), brawler.displayName.ToUpperInvariant(), Big, Color.Lerp(brawler.Tint, Color.white, 0.35f), 2f);
            y += 34 * s;
            Outlined(new Rect(d.x + 16 * s, y, d.width - 32 * s, 40 * s), brawler.role, Wrap, Muted, 1f);
            y += 46 * s;
            var cpSel = ctx.Profile.Character(id, false);
            int clvl = ctx.Progression.CharacterLevel(id);
            ctx.Config.characterLevels.Progress(cpSel != null ? cpSel.xp : 0, out _, out int into, out int span);
            Outlined(new Rect(d.x + 16 * s, y, 220 * s, 24 * s), "LEVEL " + clvl + " / " + ctx.Config.characterLevels.maxLevel, Label, Gold, 1.5f);
            Bar(new Rect(d.x + 200 * s, y + 2 * s, d.width - 216 * s, 20 * s), span > 0 ? into / (float)span : 1f, Gold, span > 0 ? into + " / " + span + " XP" : "MAX LEVEL");
            y += 34 * s;

            var casual = ctx.Upgrades.Preview(id, false);
            var comp = ctx.Upgrades.Preview(id, true);
            // Cabeçalho da tabela
            float colLabel = d.x + 16 * s, colBase = d.x + d.width * 0.3f, colCasual = d.x + d.width * 0.44f, colComp = d.x + d.width * 0.58f, colPips = d.x + d.width * 0.71f;
            Outlined(new Rect(colLabel, y, 200 * s, 20 * s), "STAT", Tiny, Muted, 1f);
            Outlined(new Rect(colBase, y, colCasual - colBase - 8*s, 20 * s), "BASE", Tiny, Muted, 1f);
            Outlined(new Rect(colCasual, y, colComp - colCasual - 8*s, 20 * s), "CASUAL", Tiny, Muted, 1f);
            Outlined(new Rect(colComp, y, colPips - colComp - 8*s, 20 * s), "COMPETITIVE", Tiny, Muted, 1f);
            Outlined(new Rect(colPips, y, 200 * s, 20 * s), "UPGRADE", Tiny, Muted, 1f);
            y += 22 * s;

            foreach (UpgradeStat stat in Enum.GetValues(typeof(UpgradeStat)))
            {
                var rr = new Rect(d.x + 10 * s, y, d.width - 20 * s, 50 * s);
                Panel(rr, new Color(1f, 1f, 1f, 0.05f));
                var tuning = ctx.Config.Tuning(stat);
                Outlined(new Rect(colLabel, y + 4 * s, colBase - colLabel, 22 * s), tuning.label, Small, Color.white, 1f);
                Outlined(new Rect(colLabel, y + 24 * s, colBase - colLabel, 20 * s), "+" + (tuning.bonusPerLevel * 100f).ToString("0.#") + "%/level · max " + (tuning.casualCap * 100f).ToString("0") + "%",
                    Tiny, Muted, 1f);
                Outlined(new Rect(colBase, y + 12 * s, 120 * s, 24 * s), FormatStat(brawler, stat, EffectiveModifiers.Neutral), Small, Color.white, 1f);
                Outlined(new Rect(colCasual, y + 12 * s, 120 * s, 24 * s), FormatStat(brawler, stat, casual), Small, Good, 1f);
                Outlined(new Rect(colComp, y + 12 * s, 120 * s, 24 * s), FormatStat(brawler, stat, comp), Small, Muted, 1f);

                int lv = ctx.Upgrades.Level(id, stat);
                for (int k = 0; k < ctx.Config.maxUpgradeLevel; k++)
                {
                    var pip = new Rect(colPips + k * 16 * s, y + 8 * s, 12 * s, 12 * s);
                    Fill(pip, k < lv ? Gold : new Color(1f, 1f, 1f, 0.15f));
                }
                var check = ctx.Upgrades.CanUpgrade(id, stat);
                int cost = ctx.Upgrades.NextCost(id, stat);
                string label = check == UpgradeResult.Ok ? "UPGRADE · " + cost : (check == UpgradeResult.NotEnoughCoins ? cost + " coins" : CharacterUpgradeManager.Describe(check));
                if (check == UpgradeResult.CharacterLevelTooLow)
                    label = "Requires level " + ctx.Config.CharacterLevelRequired(lv + 1);
                if (Btn(new Rect(colPips, y + 24 * s, rr.xMax - colPips - 6 * s, 22 * s), label, check == UpgradeResult.Ok ? new Color(0.3f, 0.65f, 1f) : new Color(0.3f, 0.3f, 0.38f),
                        check == UpgradeResult.Ok, ButtonSmall))
                {
                    var res = ctx.Upgrades.TryUpgrade(id, stat);
                    Say(res == UpgradeResult.Ok ? tuning.label + " upgraded!" : CharacterUpgradeManager.Describe(res));
                }
                y += 54 * s;
            }
            Outlined(new Rect(d.x + 16 * s, d.yMax - 52 * s, d.width - 32 * s, 46 * s),
                "Effective stats include upgrades and pets. Competitive matches use base stats. " +
                "Upgrades apply at your next spawn.", Wrap, Muted, 1f);
        }

        static string ShortRole(BrawlerDefinition b)
        {
            var role = b.role ?? "";
            int dash = role.IndexOf('—');
            if (dash < 0) dash = role.IndexOf('·');
            return dash > 0 ? role.Substring(0, dash).Trim() : role;
        }

        static string FormatStat(BrawlerDefinition b, UpgradeStat stat, EffectiveModifiers m)
        {
            switch (stat)
            {
                case UpgradeStat.Health: return StatResolver.MaxHealth(b, m) + " HP";
                case UpgradeStat.Damage: return Mathf.RoundToInt(100f * (m.damageMultiplier > 0f ? m.damageMultiplier : 1f)) + "%";
                case UpgradeStat.Regeneration: return (b.stats.regenPercentPerSecond * 100f * (m.regenMultiplier > 0f ? m.regenMultiplier : 1f)).ToString("0.#") + "%/s";
                case UpgradeStat.Speed: return (b.movement.walkSpeed * (m.speedMultiplier > 0f ? m.speedMultiplier : 1f)).ToString("0.00") + " m/s";
                case UpgradeStat.Reload: return (b.primary.reloadTimePerCharge * (m.reloadTimeMultiplier > 0f ? m.reloadTimeMultiplier : 1f)).ToString("0.00") + " s";
                default: return Mathf.RoundToInt(100f * (m.superChargeMultiplier > 0f ? m.superChargeMultiplier : 1f)) + "%";
            }
        }

        // ================================================================== NÍVEIS

        void DrawLevels(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var curve = ctx.Config.playerLevels;
            int level = ctx.Progression.PlayerLevel;
            int cols = 10;
            int rows = Mathf.CeilToInt(curve.maxLevel / (float)cols);
            float gap = 6 * s;
            float cw = (c.width - gap * (cols - 1)) / cols;
            float ch = Mathf.Min(84 * s, (c.height - 110 * s - gap * (rows - 1)) / rows);
            for (int l = 1; l <= curve.maxLevel; l++)
            {
                int idx = l - 1;
                var r = new Rect(c.x + (idx % cols) * (cw + gap), c.y + (idx / cols) * (ch + gap), cw, ch);
                bool reached = l <= level;
                bool isCurrent = l == level;
                var reward = ctx.Config.RewardForLevel(l);
                bool pending = ctx.Profile.pendingLevelRewards.Contains(l);
                var bg = isCurrent ? new Color(0.95f, 0.7f, 0.2f, 0.95f) : reached ? new Color(0.25f, 0.5f, 0.3f, 0.95f) : new Color(0.15f, 0.16f, 0.26f, 0.95f);
                InkPanel(r, bg, isCurrent ? 3f : 1.5f);
                Outlined(new Rect(r.x, r.y + 4 * s, r.width, 26 * s), l.ToString(), BigCenter, Color.white, 1.5f);
                if (reward != null && !reward.IsEmpty)
                {
                    string txt = reward.skinIds != null && reward.skinIds.Length > 0 ? "SKIN" : reward.cosmeticIds != null && reward.cosmeticIds.Length > 0 ? "TITLE" : "+" + reward.coins;
                    Outlined(new Rect(r.x, r.yMax - 26 * s, r.width, 20 * s), txt, TinyCenter, pending ? new Color(1f, 0.4f, 0.35f) : Gold, 1f);
                    if (pending && Hit(r)) Open(ProgressionScreen.Rewards);
                }
            }
            float y = c.y + rows * (ch + gap) + 10 * s;
            curve.Progress(ctx.Profile.playerXp, out _, out int into, out int span);
            Outlined(new Rect(c.x, y, c.width, 26 * s), level >= curve.maxLevel ? "MAX LEVEL REACHED" :
                "LEVEL " + level + " · need " + (span - into) + " XP to level " + (level + 1), Label, Color.white, 1.5f);
            Bar(new Rect(c.x, y + 30 * s, c.width, 24 * s), span > 0 ? into / (float)span : 1f, new Color(0.35f, 0.8f, 1f), span > 0 ? into + " / " + span + " XP" : "");
            int next = -1;
            for (int l = level + 1; l <= curve.maxLevel; l++)
            {
                var rw = ctx.Config.RewardForLevel(l);
                if (rw != null && !rw.IsEmpty) { next = l; break; }
            }
            if (next > 0)
                Outlined(new Rect(c.x, y + 60 * s, c.width, 24 * s), "Next reward: level " + next + " — " + ctx.Config.RewardForLevel(next).Describe(ctx.SkinCatalog, ctx.PetCatalog), Small, Gold, 1f);
        }

        // ================================================================== SKINS

        void DrawSkins(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            // Personagens / pets
            if (Btn(new Rect(c.x, c.y, 200 * s, 34 * s), "CHARACTERS", !m_SkinsForPets ? PanelBlue : new Color(0.22f, 0.24f, 0.34f), true, ButtonSmall))
            {
                m_SkinsForPets = false;
                m_SkinTarget = "";
            }
            if (Btn(new Rect(c.x + 208 * s, c.y, 200 * s, 34 * s), "PETS", m_SkinsForPets ? PanelBlue : new Color(0.22f, 0.24f, 0.34f), true, ButtonSmall))
            {
                m_SkinsForPets = true;
                m_SkinTarget = "";
            }

            // Alvos
            var targets = new List<KeyValuePair<string, string>>();
            if (m_SkinsForPets) foreach (var p in ctx.PetCatalog.pets) { if (p != null) targets.Add(new KeyValuePair<string, string>(p.petId, p.displayName)); }
            else foreach (var b in BrawlerRegistry.All) { if (b != null) targets.Add(new KeyValuePair<string, string>(b.brawlerId, b.ShortName)); }
            if (targets.Count == 0) return;
            if (string.IsNullOrEmpty(m_SkinTarget) || !targets.Exists(t => t.Key == m_SkinTarget))
            {
                m_SkinTarget = targets[0].Key;
                var eq = ctx.Skins.EquippedSkinFor(m_SkinsForPets ? SkinTarget.Pet : SkinTarget.Brawler, m_SkinTarget);
                m_SelectedSkin = eq != null ? eq.skinId : "";
            }
            float tw = (c.width - 6 * s * (targets.Count - 1)) / targets.Count;
            for (int i = 0; i < targets.Count; i++)
            {
                var r = new Rect(c.x + i * (tw + 6 * s), c.y + 42 * s, tw, 32 * s);
                if (Btn(r, targets[i].Value, targets[i].Key == m_SkinTarget ? new Color(0.95f, 0.65f, 0.2f) : new Color(0.28f, 0.3f, 0.42f), true, ButtonSmall))
                {
                    m_SkinTarget = targets[i].Key;
                    var eq = ctx.Skins.EquippedSkinFor(m_SkinsForPets ? SkinTarget.Pet : SkinTarget.Brawler, m_SkinTarget);
                    m_SelectedSkin = eq != null ? eq.skinId : "";
                }
            }

            var target = m_SkinsForPets ? SkinTarget.Pet : SkinTarget.Brawler;
            var skins = ctx.SkinCatalog.ForTarget(target, m_SkinTarget);
            var equipped = ctx.Skins.EquippedSkinFor(target, m_SkinTarget);

            // Lista de skins (Original + catálogo)
            var listRect = new Rect(c.x, c.y + 84 * s, c.width * 0.5f, c.height - 84 * s);
            float cardH = 86 * s, gap = 8 * s;
            int count = skins.Count + 1;
            for (int i = 0; i < count; i++)
            {
                var skin = i == 0 ? null : skins[i - 1];
                var r = new Rect(listRect.x, listRect.y + i * (cardH + gap), listRect.width, cardH);
                if (r.yMax > listRect.yMax) break;
                string sid = skin != null ? skin.skinId : "";
                bool sel = sid == m_SelectedSkin;
                var rarity = skin != null ? RarityStyle.ColorOf(skin.rarity) : Muted;
                InkPanel(r, sel ? Color.Lerp(PanelBlue, rarity, 0.25f) : PanelMid, sel ? 3f : 2f);
                Fill(new Rect(r.x, r.y, 8 * s, r.height), rarity);
                Outlined(new Rect(r.x + 18 * s, r.y + 8 * s, r.width - 30 * s, 26 * s), skin != null ? skin.displayName : "Original", Label, Color.white, 1.5f);
                Outlined(new Rect(r.x + 18 * s, r.y + 34 * s, r.width - 30 * s, 20 * s), skin != null ? RarityStyle.LabelOf(skin.rarity).ToUpperInvariant() : "DEFAULT", Tiny, rarity, 1f);
                string state;
                Color stateColor;
                if (skin == null) { state = equipped == null ? "EQUIPPED" : "AVAILABLE"; stateColor = equipped == null ? Gold : Good; }
                else
                {
                    var st = ctx.Skins.StateOf(skin);
                    state = st == SkinState.Equipped ? "EQUIPPED" : st == SkinState.Unlocked ? "UNLOCKED" : "LOCKED · " + ctx.Skins.UnlockText(skin);
                    stateColor = st == SkinState.Equipped ? Gold : st == SkinState.Unlocked ? Good : Muted;
                }
                Outlined(new Rect(r.x + 18 * s, r.y + 56 * s, r.width - 30 * s, 22 * s), state, Small, stateColor, 1f);
                if (Hit(r)) m_SelectedSkin = sid;
            }

            // Pré-visualização + ações
            var pv = new Rect(listRect.xMax + 14 * s, listRect.y, c.xMax - listRect.xMax - 14 * s, listRect.height);
            InkPanel(pv, PanelMid, 2f);
            var tex = new Rect(pv.x + 10 * s, pv.y + 6 * s, pv.width - 20 * s, pv.height - 170 * s);
            if (m_Preview.Texture != null) GUI.DrawTexture(tex, m_Preview.Texture, ScaleMode.ScaleToFit, true);
            var selSkin = ctx.SkinCatalog.Find(m_SelectedSkin);
            float y = pv.yMax - 160 * s;
            Outlined(new Rect(pv.x + 14 * s, y, pv.width - 28 * s, 30 * s), selSkin != null ? selSkin.displayName.ToUpperInvariant() : "ORIGINAL LOOK", Big, selSkin != null ? RarityStyle.ColorOf(selSkin.rarity) : Color.white, 2f);
            y += 32 * s;
            Outlined(new Rect(pv.x + 14 * s, y, pv.width - 28 * s, 22 * s), selSkin != null ? selSkin.description : "The original look. Skins never change damage, health or speed.", Small, Muted, 1f);
            y += 30 * s;
            float bw = (pv.width - 40 * s) / 2f;
            if (selSkin == null)
            {
                if (Btn(new Rect(pv.x + 14 * s, y, pv.width - 28 * s, 40 * s), equipped == null ? "EQUIPPED" : "USE ORIGINAL", Good, equipped != null))
                    ctx.Skins.Equip(target, m_SkinTarget, "");
            }
            else
            {
                var st = ctx.Skins.StateOf(selSkin);
                if (st == SkinState.Locked)
                {
                    bool canBuy = selSkin.unlock.IsPurchasable;
                    bool affordable = canBuy && ctx.Inventory.Coins >= selSkin.unlock.value;
                    if (Btn(new Rect(pv.x + 14 * s, y, pv.width - 28 * s, 40 * s), canBuy ? "BUY · " + selSkin.unlock.value + " COINS" : ctx.Skins.UnlockText(selSkin), Gold, affordable))
                    {
                        if (ctx.Skins.TryPurchase(selSkin.skinId)) Say("Skin unlocked: " + selSkin.displayName);
                        else Say("Not enough coins.");
                    }
                }
                else if (Btn(new Rect(pv.x + 14 * s, y, pv.width - 28 * s, 40 * s), st == SkinState.Equipped ? "EQUIPPED" : "EQUIP", Good, st != SkinState.Equipped))
                {
                    ctx.Skins.Equip(target, m_SkinTarget, selSkin.skinId);
                    Say("Skin equipped. Other players see it immediately.");
                }
            }
            Outlined(new Rect(pv.x + 14 * s, pv.yMax - 40 * s, pv.width - 28 * s, 34 * s), "Rare · Epic · Legendary: colors, accessories, weapons, effects and animations. Cosmetic only.", Wrap, Muted, 1f);
        }

        // ================================================================== PETS

        void DrawPets(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var pets = ctx.PetCatalog.pets;
            if (pets.Count == 0) return;
            if (string.IsNullOrEmpty(m_SelectedPet) || ctx.PetCatalog.Find(m_SelectedPet) == null)
            {
                var eq = ctx.Pets.EquippedPet;
                m_SelectedPet = eq != null ? eq.petId : pets[0].petId;
            }

            if (Btn(new Rect(c.x, c.y, 200 * s, 34 * s), "SELECTION", !m_PetEvolution ? PanelBlue : new Color(0.22f, 0.24f, 0.34f), true, ButtonSmall)) m_PetEvolution = false;
            if (Btn(new Rect(c.x + 208 * s, c.y, 200 * s, 34 * s), "EVOLUTION", m_PetEvolution ? PanelBlue : new Color(0.22f, 0.24f, 0.34f), true, ButtonSmall)) m_PetEvolution = true;

            DrawUpraStock(new Rect(c.x + 422 * s, c.y, c.width - 422 * s, 34 * s));

            // Grelha de pets
            var grid = new Rect(c.x, c.y + 44 * s, c.width * 0.42f, c.height - 44 * s);
            int cols = 2;
            float gap = 8 * s;
            int rows = Mathf.CeilToInt(pets.Count / (float)cols);
            float cw = (grid.width - gap) / cols;
            float ch = Mathf.Min(150 * s, (grid.height - gap * (rows - 1)) / rows);
            for (int i = 0; i < pets.Count; i++)
            {
                var def = pets[i];
                if (def == null) continue;
                var r = new Rect(grid.x + (i % cols) * (cw + gap), grid.y + (i / cols) * (ch + gap), cw, ch);
                var state = ctx.Pets.StateOf(def.petId);
                bool sel = def.petId == m_SelectedPet;
                var bg = state == PetState.Locked ? new Color(0.13f, 0.13f, 0.18f, 0.95f) : Color.Lerp(PanelMid, def.primaryColor, 0.25f);
                InkPanel(r, sel ? Color.Lerp(bg, Color.white, 0.12f) : bg, sel ? 3f : 2f);
                Tex(new Rect(r.x + 10 * s, r.y + 10 * s, 52 * s, 52 * s), Art.CircleTexture, state == PetState.Locked ? new Color(0.3f, 0.3f, 0.35f) : def.primaryColor);
                Outlined(new Rect(r.x + 10 * s, r.y + 10 * s, 52 * s, 52 * s), def.displayName.Substring(0, 1), BigCenter, Color.white, 1.5f);
                Outlined(new Rect(r.x + 70 * s, r.y + 10 * s, r.width - 76 * s, 24 * s), def.displayName, Label, Color.white, 1.5f);
                Outlined(new Rect(r.x + 70 * s, r.y + 34 * s, r.width - 76 * s, 20 * s), SpeciesLabel(def.species) + " · " + PetDefinition.CategoryLabel(def.category), Tiny, Muted, 1f);
                var stateColor = state == PetState.Locked ? Muted : state == PetState.Unlocked ? Good : Gold;
                Outlined(new Rect(r.x + 10 * s, r.y + 70 * s, r.width - 20 * s, 20 * s), PetManager.StateLabel(state).ToUpperInvariant(), Small, stateColor, 1f);
                if (state != PetState.Locked)
                {
                    int lv = ctx.PetProgression.Level(def.petId);
                    Bar(new Rect(r.x + 10 * s, r.yMax - 30 * s, r.width - 20 * s, 18 * s), ctx.Config.petLevels.Progress01(ctx.PetProgression.Xp(def.petId)), def.accentColor,
                        "LV " + lv + " · " + PetDefinition.StageName(PetDefinition.StageFor(lv)).ToUpperInvariant());
                }
                if (Hit(r)) m_SelectedPet = def.petId;
            }

            var pet = ctx.PetCatalog.Find(m_SelectedPet);
            if (pet == null) return;
            var right = new Rect(grid.xMax + 14 * s, c.y + 44 * s, c.xMax - grid.xMax - 14 * s, c.height - 44 * s);
            InkPanel(right, PanelMid, 2f);
            if (m_PetEvolution) DrawPetEvolution(right, pet);
            else DrawPetDetail(right, pet);
        }

        void DrawUpraStock(Rect r)
        {
            float s = Scale;
            float buyWidth = Mathf.Min(230 * s, r.width * 0.5f);
            float icon = r.height + 14 * s;
            var iconRect = new Rect(r.x, r.y - 8 * s, icon, icon);
            // Contador "salta" quando chegam sacos novos
            float since = Time.unscaledTime - m_UpraBoughtAt;
            float pop = since > 0.45f && since < 0.75f ? 1f + 0.25f * Mathf.Sin((since - 0.45f) / 0.3f * Mathf.PI) : 1f;
            var ir = new Rect(iconRect.center.x - icon * pop / 2f, iconRect.center.y - icon * pop / 2f, icon * pop, icon * pop);
            Tex(ir, Branding.UpraSack, Color.white);
            float tx = iconRect.xMax + 6 * s, tw = r.width - buyWidth - icon - 14 * s;
            Outlined(new Rect(tx, r.y - 2 * s, tw, 22 * s), "UPRA × " + m_Ctx.PetFood.Stock, Label, Gold, 1f);
            Outlined(new Rect(tx, r.y + 17 * s, tw, 14 * s), "RAÇÕES UPR ANIMAL · +2 PER MATCH", Tiny, Muted, 1f);
            bool affordable = m_Ctx.Inventory.Coins >= PetFoodRules.PackCost && m_Ctx.PetFood.Stock <= PetFoodRules.MaxStock - PetFoodRules.PackSize;
            var btn = new Rect(r.xMax - buyWidth, r.y, buyWidth, r.height);
            if (Btn(btn, "     BUY 3 SACKS · " + PetFoodRules.PackCost + " COINS", Branding.UpraBlue, affordable, ButtonSmall) && m_Ctx.PetFood.BuyPack())
            {
                m_UpraBoughtAt = Time.unscaledTime;
                m_UpraBuyFrom = btn.center;
                m_UpraBuyTo = iconRect.center;
                Sfx.Play2D(Sound.Reward, 0.5f);
                Say("+3 UPRA! Feed your pet in a match with [" + GameInputSettings.BindingLabel(GameAction.FeedPet) + "].");
            }
            Tex(new Rect(btn.x + 6 * s, btn.y + 2 * s, btn.height - 4 * s, btn.height - 4 * s), Branding.UpraSack, Color.white);

            // Sacos a voar do botão para o contador
            for (int i = 0; i < PetFoodRules.PackSize; i++)
            {
                float k = (since - i * 0.08f) / 0.45f;
                if (k <= 0f || k >= 1f) continue;
                var p = Vector2.Lerp(m_UpraBuyFrom, m_UpraBuyTo, k) + Vector2.up * (-Mathf.Sin(k * Mathf.PI) * 60f * s);
                float sz = Mathf.Lerp(30f, 22f, k) * s;
                Tex(new Rect(p.x - sz / 2f, p.y - sz / 2f, sz, sz), Branding.UpraSack, Color.white);
            }
        }

        /// <summary>Cartão UPRA do pet: saco ilustrado, fórmula da espécie e o bónus do pet ao comer.</summary>
        void DrawUpraCard(Rect r, PetDefinition pet)
        {
            float s = Scale;
            Fill(r, new Color(0.07f, 0.16f, 0.32f, 0.92f));
            Fill(new Rect(r.x, r.y, 5 * s, r.height), Branding.UpraBlue);
            float icon = r.height - 8 * s;
            Tex(new Rect(r.x + 8 * s, r.y + 4 * s, icon, icon), Branding.UpraSack, Color.white);
            float x = r.x + icon + 16 * s, w = r.xMax - x - 8 * s;
            Outlined(new Rect(x, r.y + 4 * s, w, 22 * s), Branding.UpraFormula(pet.species).ToUpperInvariant(), Label, Color.white, 1.2f);
            Outlined(new Rect(x, r.y + 25 * s, w, 16 * s), Branding.UpraFormulaTagline(pet.species), Tiny, new Color(0.6f, 0.8f, 1f), 1f);
            Outlined(new Rect(x, r.y + 42 * s, w, 18 * s), "UPRA boost: " + PetFoodRules.BoostText(pet.species) + " · " + (int)PetFoodRules.BoostDuration + "s", Small, Good, 1f);
            Outlined(new Rect(x, r.y + 63 * s, w, 16 * s), "[" + GameInputSettings.BindingLabel(GameAction.FeedPet) + "] in match · pet eats 2s, you stand still", Tiny, Muted, 1f);
            Outlined(new Rect(x, r.y + 80 * s, w, 16 * s), (int)PetFoodRules.Cooldown + "s cooldown · 3 meals per match · all modes", Tiny, Muted, 1f);
        }

        void DrawPetDetail(Rect r, PetDefinition pet)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var state = ctx.Pets.StateOf(pet.petId);
            int lv = Mathf.Max(1, ctx.PetProgression.Level(pet.petId));
            var tex = new Rect(r.x + 10 * s, r.y + 6 * s, r.width * 0.45f, r.height * 0.6f);
            if (m_Preview.Texture != null) GUI.DrawTexture(tex, m_Preview.Texture, ScaleMode.ScaleToFit, true);
            if (Btn(new Rect(tex.x + tex.width * 0.1f, tex.yMax + 4 * s, tex.width * 0.8f, 30 * s),
                    m_Preview.IsFeeding ? "YUM!" : "FEED WITH UPRA", Branding.UpraBlue, !m_Preview.IsFeeding, ButtonSmall))
                m_Preview.PlayFeed();
            Outlined(new Rect(tex.x, tex.yMax + 38 * s, tex.width, 16 * s), Branding.UpraSlogan, Tiny, new Color(0.55f, 0.75f, 1f), 1f);
            float x = tex.xMax + 10 * s, w = r.xMax - x - 14 * s;
            float y = r.y + 14 * s;
            Outlined(new Rect(x, y, w, 34 * s), pet.displayName.ToUpperInvariant(), Big, Color.Lerp(pet.primaryColor, Color.white, 0.35f), 2f);
            y += 36 * s;
            Outlined(new Rect(x, y, w, 22 * s), SpeciesLabel(pet.species) + " · " + PetDefinition.CategoryLabel(pet.category), Label, Gold, 1.2f);
            y += 28 * s;
            Outlined(new Rect(x, y, w, 60 * s), pet.description, Wrap, Muted, 1f);
            y += 64 * s;
            Outlined(new Rect(x, y, w, 20 * s), "ABILITY (LV " + lv + ")", Tiny, Muted, 1f);
            y += 20 * s;
            Outlined(new Rect(x, y, w, 44 * s), pet.AbilityText(lv), Wrap, Color.white, 1f);
            y += 46 * s;
            if (lv < 10)
            {
                Outlined(new Rect(x, y, w, 40 * s), "Lv " + (lv + 1) + ": " + pet.AbilityText(lv + 1), Wrap, Good, 1f);
                y += 42 * s;
            }
            DrawUpraCard(new Rect(x - 4 * s, y, w + 8 * s, 104 * s), pet);

            float by = r.yMax - 128 * s;
            if (state != PetState.Locked)
            {
                ctx.Config.petLevels.Progress(ctx.PetProgression.Xp(pet.petId), out _, out int into, out int span);
                Bar(new Rect(r.x + 14 * s, by, r.width - 28 * s, 22 * s), span > 0 ? into / (float)span : 1f, pet.accentColor,
                    "LEVEL " + lv + " · " + (span > 0 ? into + " / " + span + " XP" : "MAX LEVEL"));
            }
            else Outlined(new Rect(r.x + 14 * s, by, r.width - 28 * s, 24 * s), "How to unlock: " + ctx.PetUnlocks.ProgressText(pet), Small, Gold, 1f);

            var btn = new Rect(r.x + 14 * s, r.yMax - 92 * s, r.width - 28 * s, 42 * s);
            if (state == PetState.Locked)
            {
                bool buy = pet.unlock.IsPurchasable;
                if (Btn(btn, buy ? "BUY · " + pet.unlock.value + " COINS" : "LOCKED", Gold, buy && ctx.Inventory.Coins >= pet.unlock.value))
                {
                    if (ctx.PetUnlocks.TryPurchase(pet.petId)) Say(pet.displayName + " unlocked!");
                }
            }
            else if (state == PetState.Unlocked)
            {
                if (Btn(btn, "EQUIP", Good))
                {
                    ctx.Pets.Equip(pet.petId);
                    Say(pet.displayName + " joins you in the arena.");
                }
            }
            else if (Btn(btn, "UNEQUIP PET", new Color(0.55f, 0.35f, 0.3f)))
            {
                ctx.Pets.Unequip();
                Say("No pet equipped.");
            }
            if (Btn(new Rect(r.x + 14 * s, r.yMax - 44 * s, r.width - 28 * s, 34 * s), "PET SKINS", PanelBlue, true, ButtonSmall))
            {
                m_SkinsForPets = true;
                m_SkinTarget = pet.petId;
                var eq = ctx.Skins.EquippedSkinFor(SkinTarget.Pet, pet.petId);
                m_SelectedSkin = eq != null ? eq.skinId : "";
                Open(ProgressionScreen.Skins);
            }
        }

        void DrawPetEvolution(Rect r, PetDefinition pet)
        {
            float s = Scale;
            var ctx = m_Ctx;
            int lv = ctx.Inventory.IsPetUnlocked(pet.petId) ? Mathf.Max(1, ctx.PetProgression.Level(pet.petId)) : 0;
            Outlined(new Rect(r.x + 16 * s, r.y + 10 * s, r.width, 32 * s), "EVOLUTION OF " + pet.displayName.ToUpperInvariant(), Big, Gold, 2f);
            var tex = new Rect(r.xMax - r.width * 0.36f, r.y + 40 * s, r.width * 0.34f, r.height * 0.42f);
            if (m_Preview.Texture != null) GUI.DrawTexture(tex, m_Preview.Texture, ScaleMode.ScaleToFit, true);

            // Faixas dos estágios + nós dos níveis
            float x0 = r.x + 24 * s, x1 = tex.x - 20 * s;
            float ty = r.y + 120 * s;
            string[] stages = { "YOUNG (1–3)", "DEVELOPED (4–7)", "EVOLVED (8–10)" };
            int[] stageStart = { 1, 4, 8 };
            int[] stageEnd = { 3, 7, 10 };
            Color[] stageColors = { new Color(0.4f, 0.7f, 0.95f), new Color(0.5f, 0.85f, 0.45f), new Color(1f, 0.75f, 0.3f) };
            float step = (x1 - x0) / 9f;
            for (int k = 0; k < 3; k++)
            {
                float a = x0 + (stageStart[k] - 1) * step - step * 0.45f, b = x0 + (stageEnd[k] - 1) * step + step * 0.45f;
                Panel(new Rect(a, ty - 46 * s, b - a, 30 * s), Art.WithAlpha(stageColors[k], lv >= stageStart[k] ? 0.9f : 0.3f));
                Outlined(new Rect(a, ty - 46 * s, b - a, 30 * s), stages[k], TinyCenter, Color.white, 1f);
            }
            Fill(new Rect(x0, ty + 14 * s, x1 - x0, 6 * s), new Color(1f, 1f, 1f, 0.2f));
            if (lv > 1) Fill(new Rect(x0, ty + 14 * s, (lv - 1) * step, 6 * s), Gold);
            for (int l = 1; l <= 10; l++)
            {
                float cx = x0 + (l - 1) * step;
                var node = new Rect(cx - 17 * s, ty, 34 * s, 34 * s);
                Badge(node, l.ToString(), l <= lv ? (l == lv ? Gold : Good) : new Color(0.25f, 0.27f, 0.35f));
            }

            // Recompensas de evolução
            float y = ty + 56 * s;
            foreach (var m in pet.milestones)
            {
                if (m == null) continue;
                bool reached = lv >= m.level;
                var row = new Rect(r.x + 16 * s, y, x1 - r.x - 16 * s, 36 * s);
                Panel(row, reached ? new Color(0.25f, 0.45f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.05f));
                Outlined(new Rect(row.x + 10 * s, row.y, 80 * s, row.height), "LV " + m.level, Label, reached ? Gold : Muted, 1.2f);
                Outlined(new Rect(row.x + 90 * s, row.y, row.width - 100 * s, row.height), MilestoneLabel(m.type) + " · " + m.description, Small, Color.white, 1f);
                y += 42 * s;
            }

            if (lv > 0)
            {
                ctx.Config.petLevels.Progress(ctx.PetProgression.Xp(pet.petId), out _, out int into, out int span);
                Bar(new Rect(r.x + 16 * s, r.yMax - 44 * s, r.width - 32 * s, 24 * s), span > 0 ? into / (float)span : 1f, pet.accentColor,
                    span > 0 ? "LEVEL " + lv + " · " + into + " / " + span + " XP · earn XP in valid matches with this pet equipped" : "MAX LEVEL");
            }
            else Outlined(new Rect(r.x + 16 * s, r.yMax - 44 * s, r.width - 32 * s, 24 * s), "Locked · " + ctx.PetUnlocks.ProgressText(pet), Small, Gold, 1f);
        }

        static string MilestoneLabel(PetMilestoneType t)
        {
            switch (t)
            {
                case PetMilestoneType.AbilityBoost: return "Ability";
                case PetMilestoneType.Animation: return "Animation";
                case PetMilestoneType.Visual: return "Visual";
                default: return "Cosmetic";
            }
        }

        static string SpeciesLabel(PetSpecies s)
        {
            switch (s)
            {
                case PetSpecies.Dog: return "Dog";
                case PetSpecies.Cat: return "Cat";
                case PetSpecies.Pig: return "Pig";
                case PetSpecies.Rooster: return "Rooster";
                case PetSpecies.Rabbit: return "Rabbit";
                default: return "Turtle";
            }
        }

        // ================================================================== MISSÕES

        void DrawQuests(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var now = ctx.Clock.UtcNow;
            float colW = (c.width - 16 * s) / 2f;
            DrawQuestColumn(new Rect(c.x, c.y, colW, c.height * 0.62f), QuestPeriod.Daily, "DAILY · reset in " + Duration(QuestManager.TimeUntilReset(QuestPeriod.Daily, now)));
            DrawQuestColumn(new Rect(c.x + colW + 16 * s, c.y, colW, c.height * 0.62f), QuestPeriod.Weekly, "WEEKLY · reset in " + Duration(QuestManager.TimeUntilReset(QuestPeriod.Weekly, now)));

            // Desafios especiais
            var ch = new Rect(c.x, c.y + c.height * 0.62f + 14 * s, c.width, c.height * 0.38f - 14 * s);
            InkPanel(ch, PanelMid, 2f);
            Outlined(new Rect(ch.x + 14 * s, ch.y + 8 * s, ch.width, 26 * s), "SPECIAL CHALLENGES", Label, Gold, 1.5f);
            float y = ch.y + 40 * s;
            foreach (var a in ctx.QuestCatalog.achievements)
            {
                if (a == null || !a.isChallenge) continue;
                var row = new Rect(ch.x + 10 * s, y, ch.width - 20 * s, 44 * s);
                if (row.yMax > ch.yMax) break;
                bool done = ctx.Quests.IsAchieved(a);
                Panel(row, done ? new Color(0.25f, 0.45f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.05f));
                Outlined(new Rect(row.x + 10 * s, row.y + 2 * s, row.width * 0.6f, 22 * s), a.title, Label, done ? Gold : Color.white, 1.2f);
                Outlined(new Rect(row.x + 10 * s, row.y + 22 * s, row.width * 0.6f, 20 * s), a.description, Tiny, Muted, 1f);
                int prog = Mathf.Min(ctx.Quests.AchievementProgress(a), a.target);
                Bar(new Rect(row.xMax - row.width * 0.36f, row.y + 10 * s, row.width * 0.34f, 22 * s), prog / (float)a.target, done ? Good : Gold, done ? "COMPLETED" : prog + "/" + a.target);
                y += 50 * s;
            }
        }

        void DrawQuestColumn(Rect r, QuestPeriod period, string title)
        {
            float s = Scale;
            var ctx = m_Ctx;
            InkPanel(r, PanelMid, 2f);
            Outlined(new Rect(r.x + 14 * s, r.y + 8 * s, r.width, 26 * s), title, Label, Gold, 1.5f);
            float y = r.y + 42 * s;
            foreach (var q in ctx.Quests.Active(period))
            {
                var p = ctx.Quests.Progress(q);
                var row = new Rect(r.x + 10 * s, y, r.width - 20 * s, 84 * s);
                if (row.yMax > r.yMax) break;
                Panel(row, p.claimed ? new Color(0.2f, 0.32f, 0.22f, 0.9f) : p.completed ? new Color(0.3f, 0.5f, 0.25f, 0.95f) : new Color(1f, 1f, 1f, 0.06f));
                Outlined(new Rect(row.x + 10 * s, row.y + 4 * s, row.width - 20 * s, 24 * s), q.title, Label, Color.white, 1.2f);
                Bar(new Rect(row.x + 10 * s, row.y + 32 * s, row.width * 0.6f, 20 * s), p.progress / (float)Mathf.Max(1, q.target), p.completed ? Good : new Color(0.35f, 0.75f, 1f),
                    Mathf.Min(p.progress, q.target) + " / " + q.target);
                Outlined(new Rect(row.x + 10 * s, row.y + 56 * s, row.width * 0.62f, 22 * s), q.reward.Describe(ctx.SkinCatalog, ctx.PetCatalog), Tiny, Gold, 1f);
                var btn = new Rect(row.xMax - row.width * 0.33f, row.y + 30 * s, row.width * 0.31f, 40 * s);
                if (p.claimed) Outlined(btn, "CLAIMED ✓", Center, Good, 1.2f);
                else if (Btn(btn, p.completed ? "CLAIM" : "IN PROGRESS", p.completed ? Gold : new Color(0.3f, 0.3f, 0.38f), p.completed, ButtonSmall))
                {
                    if (ctx.Quests.Claim(q)) Say("Reward claimed: " + q.reward.Describe(ctx.SkinCatalog, ctx.PetCatalog));
                }
                y += 92 * s;
            }
        }

        // ================================================================== RECOMPENSAS

        void DrawRewards(Rect c)
        {
            float s = Scale;
            var ctx = m_Ctx;
            var left = new Rect(c.x, c.y, c.width * 0.5f - 8 * s, c.height);
            InkPanel(left, PanelMid, 2f);
            Outlined(new Rect(left.x + 14 * s, left.y + 8 * s, left.width, 26 * s), "UNCLAIMED LEVEL REWARDS", Label, Gold, 1.5f);
            var pending = new List<int>(ctx.Profile.pendingLevelRewards);
            pending.Sort();
            float y = left.y + 44 * s;
            if (pending.Count == 0)
                Outlined(new Rect(left.x + 14 * s, y, left.width - 28 * s, 60 * s), "No rewards to claim. Level up to earn coins, skins and titles.", Wrap, Muted, 1f);
            foreach (int level in pending)
            {
                var row = new Rect(left.x + 10 * s, y, left.width - 20 * s, 52 * s);
                if (row.yMax > left.yMax - 56 * s) break;
                Panel(row, new Color(0.35f, 0.3f, 0.15f, 0.9f));
                var rw = ctx.Config.RewardForLevel(level);
                Outlined(new Rect(row.x + 10 * s, row.y, 120 * s, row.height), "LEVEL " + level, Label, Gold, 1.5f);
                Outlined(new Rect(row.x + 120 * s, row.y, row.width - 260 * s, row.height), rw != null ? rw.Describe(ctx.SkinCatalog, ctx.PetCatalog) : "", Small, Color.white, 1f);
                if (Btn(new Rect(row.xMax - 130 * s, row.y + 8 * s, 120 * s, 36 * s), "CLAIM", Gold, true, ButtonSmall)) ctx.Rewards.ClaimLevelReward(level);
                y += 58 * s;
            }
            if (Btn(new Rect(left.x + 14 * s, left.yMax - 50 * s, left.width - 28 * s, 40 * s), "CLAIM ALL", Good, pending.Count > 0))
                Say(ctx.Rewards.ClaimAllLevelRewards() + " rewards claimed.");

            var right = new Rect(left.xMax + 16 * s, c.y, c.width - left.width - 16 * s, c.height);
            InkPanel(right, PanelMid, 2f);
            Outlined(new Rect(right.x + 14 * s, right.y + 8 * s, right.width, 26 * s), "HISTORY", Label, Gold, 1.5f);
            var log = ctx.Profile.rewardLog;
            var view = new Rect(right.x + 10 * s, right.y + 40 * s, right.width - 20 * s, right.height * 0.62f - 40 * s);
            float rowH = 42 * s;
            m_Scroll2 = GUI.BeginScrollView(view, m_Scroll2, new Rect(0, 0, view.width - 20 * s, Mathf.Max(1, log.Count) * rowH));
            for (int i = 0; i < log.Count; i++)
            {
                var e = log[log.Count - 1 - i];
                var row = new Rect(0, i * rowH, view.width - 20 * s, rowH - 4 * s);
                Panel(row, new Color(1f, 1f, 1f, 0.05f));
                Outlined(new Rect(row.x + 8 * s, row.y + 1 * s, row.width, 20 * s), LegacyProfileText.English(e.source), Small, Gold, 1f);
                Outlined(new Rect(row.x + 8 * s, row.y + 19 * s, row.width, 20 * s), LegacyProfileText.English(e.text), Tiny, Color.white, 1f);
            }
            GUI.EndScrollView();

            float cy = right.y + right.height * 0.62f + 8 * s;
            Outlined(new Rect(right.x + 14 * s, cy, right.width, 24 * s), "COSMETICS & TITLES", Label, Gold, 1.5f);
            var cos = ctx.Profile.unlockedCosmetics;
            Outlined(new Rect(right.x + 14 * s, cy + 28 * s, right.width - 28 * s, right.yMax - cy - 36 * s),
                cos.Count == 0 ? "None yet. Weekly quests, achievements and level 50 award cosmetics." : LegacyProfileText.English(string.Join("  ·  ", cos.ToArray())), Wrap, Color.white, 1f);
        }

        // ================================================================== RESULTADOS PÓS-PARTIDA

        void DrawReportOverlay()
        {
            float s = Scale;
            Fill(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), new Color(0.02f, 0.02f, 0.08f, 0.75f));
            float w = Mathf.Min(560 * s, UnityEngine.Screen.width - 30 * s), h = Mathf.Min(640 * s, UnityEngine.Screen.height - 30 * s);
            var r = new Rect((UnityEngine.Screen.width - w) / 2f, (UnityEngine.Screen.height - h) / 2f, w, h);
            DrawReport(r, m_Report);
            if (Btn(new Rect(r.x + r.width / 2f - 80 * s, r.yMax + 6 * s - 50 * s, 160 * s, 38 * s), "CLOSE", new Color(0.4f, 0.46f, 0.6f))) m_ShowReport = false;
        }

        void DrawReport(Rect r, MatchRewardReport rep)
        {
            float s = Scale;
            EnsureStyles();
            var ctx = m_Ctx;
            InkPanel(r, PanelDark, 3f);
            float y = r.y + 10 * s;
            Outlined(new Rect(r.x, y, r.width, 34 * s), rep.won && !rep.draw ? "VICTORY!" : rep.draw ? "DRAW" : "MATCH FINISHED", BigCenter, rep.won ? Gold : Color.white, 2f);
            y += 34 * s;
            var b = BrawlerRegistry.FindById(rep.brawlerId);
            Outlined(new Rect(r.x, y, r.width, 22 * s), (b != null ? b.displayName : rep.brawlerId) + (rep.competitive ? " · COMPETITIVE" : " · CASUAL"), SmallCenter,
                b != null ? Color.Lerp(b.Tint, Color.white, 0.4f) : Color.white, 1f);
            y += 28 * s;

            if (!rep.participated)
            {
                Outlined(new Rect(r.x + 20 * s, y, r.width - 40 * s, 60 * s), "Not enough participation: no XP awarded this match.", Wrap, new Color(1f, 0.6f, 0.5f), 1f);
                return;
            }

            // XP recebido e decomposição
            Outlined(new Rect(r.x, y, r.width, 40 * s), "+" + rep.xpGained + " XP", Title, new Color(0.45f, 0.85f, 1f), 2.5f);
            y += 42 * s;
            var d = rep.data;
            string breakdown = "Participation " + d.xpParticipation + " · Eliminations " + d.xpKills + " · Assists " + d.xpAssists +
                               " · Victory " + d.xpVictory + " · Objectives " + d.xpObjectives;
            Outlined(new Rect(r.x + 12 * s, y, r.width - 24 * s, 20 * s), breakdown, TinyCenter, Muted, 1f);
            y += 26 * s;

            // Barra animada do nível de jogador (passa pelos níveis ganhos)
            float start = rep == m_Report ? m_ReportShownAt : rep.receivedAt;
            float t = Mathf.Clamp01((Time.realtimeSinceStartup - start - 0.3f) / 1.8f);
            t = 1f - (1f - t) * (1f - t);
            long animXp = rep.playerXpBefore + (long)((rep.playerXpAfter - rep.playerXpBefore) * t);
            var curve = ctx.Config.playerLevels;
            curve.Progress(animXp, out int lvl, out int into, out int span);
            Badge(new Rect(r.x + 16 * s, y, 48 * s, 48 * s), lvl.ToString(), new Color(0.35f, 0.55f, 1f));
            Badge(new Rect(r.xMax - 64 * s, y, 48 * s, 48 * s), Mathf.Min(lvl + 1, curve.maxLevel).ToString(), new Color(0.3f, 0.32f, 0.42f));
            Bar(new Rect(r.x + 74 * s, y + 12 * s, r.width - 148 * s, 24 * s), span > 0 ? into / (float)span : 1f, new Color(0.35f, 0.8f, 1f),
                span > 0 ? into + " / " + span + " XP" : "MAX LEVEL");
            y += 54 * s;
            Outlined(new Rect(r.x, y, r.width, 20 * s), "Total XP: " + animXp + (rep.levelAfter > rep.levelBefore ? "   ·   REACHED LEVEL " + rep.levelAfter + "!" : ""), SmallCenter,
                rep.levelAfter > rep.levelBefore ? Gold : Color.white, 1f);
            y += 28 * s;

            // Personagem e pet
            long charAnim = rep.characterXpBefore + (long)((rep.characterXpAfter - rep.characterXpBefore) * t);
            ctx.Config.characterLevels.Progress(charAnim, out int clv, out int cin, out int cspan);
            Outlined(new Rect(r.x + 16 * s, y, 200 * s, 22 * s), (b != null ? b.ShortName : "Character") + " · LV " + clv, Small, Color.white, 1f);
            Bar(new Rect(r.x + 200 * s, y + 2 * s, r.width - 216 * s, 18 * s), cspan > 0 ? cin / (float)cspan : 1f, Gold, cspan > 0 ? cin + " / " + cspan : "MAX");
            y += 26 * s;
            if (!string.IsNullOrEmpty(rep.petId))
            {
                var pet = ctx.PetCatalog.Find(rep.petId);
                string petText = (pet != null ? pet.displayName : rep.petId) + " +" + rep.petXp + " XP · LV " + rep.petLevelBefore +
                                 (rep.petLevelAfter > rep.petLevelBefore ? " → " + rep.petLevelAfter : "") +
                                 (rep.petStageAfter > rep.petStageBefore ? " · EVOLVED: " + PetDefinition.StageName(rep.petStageAfter).ToUpperInvariant() : "");
                Outlined(new Rect(r.x + 16 * s, y, r.width - 32 * s, 22 * s), "Pet: " + petText, Small, new Color(0.7f, 1f, 0.7f), 1f);
                y += 26 * s;
            }
            Outlined(new Rect(r.x + 16 * s, y, r.width - 32 * s, 22 * s), "Coins: +" + rep.coins + "   ·   UPRA: +" + rep.upraFood, Small, Gold, 1f);
            y += 28 * s;

            // Estatísticas da partida
            string stats = "Elim. " + d.kills + "  ·  Assists " + d.assists + "  ·  Deaths " + d.deaths + "  ·  Damage " + d.damageDealt +
                           "  ·  Headshots " + d.headshots + "  ·  Objectives " + d.objectives + (d.placement > 0 ? "  ·  Placement " + d.placement + "º" : "");
            Panel(new Rect(r.x + 12 * s, y, r.width - 24 * s, 28 * s), new Color(1f, 1f, 1f, 0.06f));
            Outlined(new Rect(r.x + 12 * s, y, r.width - 24 * s, 28 * s), stats, TinyCenter, Color.white, 1f);
            y += 36 * s;

            // Desbloqueios
            if (rep.unlocks.Count > 0)
            {
                Outlined(new Rect(r.x + 16 * s, y, r.width, 22 * s), "UNLOCKED THIS MATCH", Small, Gold, 1f);
                y += 24 * s;
                foreach (var u in rep.unlocks)
                {
                    if (y > r.yMax - 60 * s) break;
                    Outlined(new Rect(r.x + 24 * s, y, r.width - 48 * s, 20 * s), "• " + u.title + (string.IsNullOrEmpty(u.subtitle) ? "" : " — " + u.subtitle), Tiny, u.color, 1f);
                    y += 20 * s;
                }
            }
        }

        // ================================================================== avisos

        void DrawBanners()
        {
            if (m_Banners.Count == 0) return;
            float s = Scale;
            float w = Mathf.Min(560 * s, UnityEngine.Screen.width - 40 * s);
            float y = 84 * s;
            float now = Time.unscaledTime;
            foreach (var b in m_Banners)
            {
                float age = now - b.time;
                float slide = age < 0.2f ? 1f - age / 0.2f : 0f;
                float alpha = Mathf.Clamp01((2.8f - age) * 2.5f);
                var r = new Rect((UnityEngine.Screen.width - w) / 2f + slide * 60 * s, y, w, 60 * s);
                var c = b.e.color;
                InkPanel(r, new Color(0.08f, 0.09f, 0.18f, 0.92f * alpha), 3f);
                Fill(new Rect(r.x, r.y, 10 * s, r.height), Art.WithAlpha(c, alpha));
                float pop = age < 0.12f ? Mathf.Lerp(1.25f, 1f, age / 0.12f) : 1f;
                var title = new Rect(r.x + 16 * s, r.y + 4 * s - (pop - 1f) * 20 * s, r.width - 24 * s, 30 * s * pop);
                Outlined(title, b.e.title, Big, Art.WithAlpha(c, alpha), 2f);
                if (!string.IsNullOrEmpty(b.e.subtitle))
                    Outlined(new Rect(r.x + 18 * s, r.y + 34 * s, r.width - 24 * s, 22 * s), b.e.subtitle, Small, new Color(1f, 1f, 1f, alpha), 1f);
                y += 66 * s;
            }
        }
    }
}
