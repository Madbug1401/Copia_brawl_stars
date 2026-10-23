using UnityEngine;

namespace FPSLAN
{
    /// <summary>HUD inspirado na referência: retrato/munição/vida, topo ciano e Super circular.</summary>
    public partial class GameUI
    {
        static readonly Color HudCyan = new Color(73f / 255f, 176f / 255f, 182f / 255f);
        static readonly Color HudGlass = new Color(0.035f, 0.09f, 0.14f, 0.78f);
        static readonly Color HudLime = new Color(0.65f, 0.95f, 0.12f);
        GUIStyle m_HudTitle, m_HudNumber, m_HudSmall, m_HudName, m_HudFeed;
        float m_HudStyleScale = -1f;
        Texture2D m_HudGradient;
        readonly Texture2D[] m_HudIcons = new Texture2D[4];

        void EnsureHudStyles(float s)
        {
            if (m_HudTitle != null && Mathf.Approximately(s, m_HudStyleScale)) return;
            m_HudStyleScale = s;
            m_HudTitle = HudStyle(25, s, TextAnchor.MiddleCenter);
            m_HudNumber = HudStyle(26, s, TextAnchor.MiddleCenter);
            m_HudSmall = HudStyle(13, s, TextAnchor.MiddleCenter);
            m_HudName = HudStyle(19, s, TextAnchor.MiddleCenter);
            m_HudFeed = HudStyle(12, s, TextAnchor.MiddleLeft);
            m_HudTitle.font = GameTypography.Title;
            m_HudNumber.font = GameTypography.Numbers;
            m_HudName.font = GameTypography.Body;
        }

        static GUIStyle HudStyle(int size, float s, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        {
            font = MenuTheme.Font, fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s)),
            fontStyle = FontStyle.Bold, alignment = anchor, clipping = TextClipping.Clip,
            padding = new RectOffset(), margin = new RectOffset(), wordWrap = false
        };

        // Sombra local ao HUD: legível contra céu e terreno, sem alterar o tema dos menus.
        void HudLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            var old = GUI.color;
            int oldSize = style.fontSize;
            var oldFont = style.font;
            if (GameTypography.IsNumber(text)) style.font = GameTypography.Numbers;
            // Nomes como LUPUS MARINUS cabem no retrato sem perder o fim do texto.
            if (!string.IsNullOrEmpty(text) && (style == m_HudName || style == m_HudTitle))
            {
                float preferredWidth = style.CalcSize(new GUIContent(text)).x;
                if (preferredWidth > rect.width)
                    style.fontSize = Mathf.Max(8, Mathf.FloorToInt(oldSize * rect.width / preferredWidth));
            }
            GUI.color = Color.white;
            style.normal.textColor = new Color(0.01f, 0.025f, 0.045f, color.a * 0.85f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, style);
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.fontSize = oldSize;
            style.font = oldFont;
            GUI.color = old;
        }

        void ReleaseHudTextures()
        {
            if (m_HudGradient != null) Destroy(m_HudGradient);
            for (int i = 0; i < m_HudIcons.Length; i++)
                if (m_HudIcons[i] != null) Destroy(m_HudIcons[i]);
        }

        void HudPlate(Rect rect, Color fill, Color edge, float border)
        {
            Panel(rect, edge);
            Panel(new Rect(rect.x + border, rect.y + border,
                Mathf.Max(0, rect.width - border * 2), Mathf.Max(0, rect.height - border * 2)), fill);
        }

        static Rect HudHealthFill(Rect track, float health01) =>
            new Rect(track.x, track.y, track.width * Mathf.Clamp01(health01), track.height);

        void DrawHudVitals(PlayerNetwork me, float s)
        {
            var b = me.Brawler;
            float x = 44 * s, bottom = Screen.height - 54 * s;
            float width = Mathf.Min(458 * s, Screen.width * 0.48f);
            var health = new Rect(x, bottom - 50 * s, width, 50 * s);
            HudPlate(health, HudGlass, new Color(0.3f, 0.66f, 0.73f, 0.5f), 1.5f * s);
            float valueWidth = 124 * s;
            var track = new Rect(health.x + valueWidth, health.y + 6 * s,
                Mathf.Max(1, health.width - valueWidth - 8 * s), health.height - 12 * s);
            Panel(track, new Color(0.08f, 0.16f, 0.18f, 0.9f));
            float hp = Mathf.Clamp01(me.Health.Health.Value / (float)Mathf.Max(1, me.Health.MaxHealth));
            var hpColor = hp > 0.5f ? HudLime : hp > 0.25f ? new Color(1f, 0.72f, 0.2f) : new Color(1f, 0.3f, 0.24f);
            var filled = HudHealthFill(track, hp);
            if (filled.width > 0f)
            {
                // Recorte, em vez de largura mínima: 1 PV nunca parece uma barra ainda cheia.
                GUI.BeginGroup(filled);
                Panel(new Rect(0, 0, track.width, track.height), hpColor);
                Fill(new Rect(8 * s, 4 * s, Mathf.Max(0, track.width - 16 * s), 2 * s), new Color(1, 1, 1, 0.22f));
                GUI.EndGroup();
            }
            HudLabel(new Rect(health.x + 6 * s, health.y, valueWidth - 12 * s, health.height),
                me.Health.Health.Value + "/" + me.Health.MaxHealth, m_HudNumber, HudCyan);

            float d = 100 * s;
            var portrait = new Rect(x - 5 * s, health.y - d - 14 * s, d, d);
            Tex(portrait, m_Circle, HudGlass);
            Texture2D face = null;
            if (b != null)
                for (int i = 0; i < s_Roster.Length; i++)
                    if (s_Roster[i].brawlerId == b.brawlerId) { face = MenuTexture("portrait_" + s_Roster[i].key); break; }
            if (face != null)
                Tex(new Rect(portrait.x + 10 * s, portrait.y + 8 * s, d - 20 * s, d - 20 * s), face, Color.white);
            else HudLabel(portrait, b != null ? FirstCharacterOrFallback(b.displayName) : "?", m_HudTitle, Color.white);
            Tex(portrait, m_Ring, new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.5f));
            var name = new Rect(portrait.x - 10 * s, portrait.yMax - 25 * s, portrait.width + 20 * s, 27 * s);
            Panel(name, new Color(0.035f, 0.08f, 0.12f, 0.92f));
            HudLabel(name, b != null ? b.displayName.ToUpperInvariant() : "", m_HudName, Color.white);

            // Mantém o pet e o nível equipados, sem cobrir o nome do personagem.
            var pet = GameExperience.Current.PetCatalog.FindByHash(me.Loadout.Value.petHash);
            if (pet != null)
            {
                var badge = new Rect(portrait.xMax - 17 * s, portrait.y + 1 * s, 29 * s, 29 * s);
                Tex(new Rect(badge.x - 2 * s, badge.y - 2 * s, badge.width + 4 * s, badge.height + 4 * s), m_Circle, HudGlass);
                Tex(badge, m_Circle, pet.primaryColor);
                HudLabel(badge, me.Loadout.Value.petLevel.ToString(), m_HudSmall, Color.white);
            }
            if (pet != null)
                HudLabel(new Rect(portrait.xMax + 14*s, portrait.y + 45*s, health.xMax - portrait.xMax - 24*s, 24*s),
                    me.UpraStatus, m_HudSmall, me.UpraActive ? HudLime : HudCyan);
            DrawHudAmmo(me, new Rect(portrait.xMax + 14 * s, portrait.yMax - 27 * s,
                Mathf.Max(1, health.xMax - portrait.xMax - 28 * s), 16 * s), s);
            HudLabel(new Rect(portrait.xMax + 14 * s, portrait.y + 13 * s,
                Mathf.Max(1, health.xMax - portrait.xMax - 20 * s), 24 * s),
                b != null ? ShortRole(b) : "", m_HudSmall, new Color(0.8f, 0.89f, 0.94f, 0.85f));
        }

        void DrawHudAmmo(PlayerNetwork me, Rect area, float s)
        {
            int count = Mathf.Max(1, me.Brawler != null ? me.Brawler.primary.maxCharges : 3);
            float gap = Mathf.Min(8 * s, area.width / (count * 3f));
            float width = Mathf.Min(55 * s, (area.width - gap * (count - 1)) / count);
            for (int i = 0; i < count; i++)
            {
                var cell = new Rect(area.x + i * (width + gap), area.y, width, area.height);
                Panel(cell, new Color(0.13f, 0.16f, 0.2f, 0.9f));
                float fill = me.Health.IsDead ? 0f : Mathf.Clamp01(me.Combat.Ammo - i);
                if (fill <= 0f) continue;
                GUI.BeginGroup(new Rect(cell.x, cell.y, cell.width * fill, cell.height));
                Panel(new Rect(0, 0, cell.width, cell.height), fill >= 1f ? new Color(0.82f, 0.43f, 0.3f) : new Color(1f, 0.71f, 0.37f));
                GUI.EndGroup();
            }
        }

        // Espaço partilhado com GemGrabHud: os objetivos ficam abaixo do cabeçalho e das estatísticas.
        public static float HudObjectiveTop(float s) => 118 * s;

        void DrawHudHeader(PlayerNetwork me, float s)
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned) return;
            float cx = Screen.width / 2f;
            var top = new Rect(cx - 210 * s, 24 * s, 420 * s, match.IsCoop ? 104 * s : 88 * s);
            HudPlate(top, new Color(0.13f, 0.27f, 0.4f, 0.68f), HudCyan, 4 * s);
            string mode = match.IsCoop ? "IT TAKES TWO" : match.Mode.Value == GameMode.GemGrab ? "GEM RUSH" :
                match.Mode.Value == GameMode.Wipeout ? "WIPEOUT" : match.Mode.Value == GameMode.Knockout ?
                "KNOCKOUT · ROUND " + match.RoundNumber.Value : match.Mode.Value == GameMode.Showdown ? "SHOWDOWN" : "DEATHMATCH";
            float remaining = Mathf.Max(0, match.TimeRemaining);
            string timer = match.Phase.Value == MatchPhase.Playing ? $"{(int)(remaining / 60)}:{(int)(remaining % 60):00}" : "FINISHED";
            if (match.IsCoop && match.Phase.Value == MatchPhase.Playing)
                timer = match.IsRoundIntermission.Value ? $"0:{Mathf.CeilToInt(remaining):00}" : "";
            HudLabel(new Rect(top.x + 10 * s, top.y + 8 * s, top.width - 20 * s, 32 * s), mode, m_HudTitle, Color.white);
            HudLabel(new Rect(top.x, top.y + 42 * s, top.width, 32 * s), timer, m_HudNumber,
                remaining <= 30 && !match.IsCoop ? new Color(1f, 0.56f, 0.4f) : HudCyan);
            if (match.IsCoop)
            {
                string wave = match.CoopStage.Value >= CoopMissionStage.Puzzle ? match.CoopStageTitle :
                    $"WAVE {Mathf.Max(1, match.CoopWave.Value)}/{match.coopTotalWaves} · ENEMIES {match.CoopEnemiesLeft.Value}";
                HudLabel(new Rect(top.x + 8 * s, top.yMax - 26 * s, top.width - 16 * s, 20 * s), wave, m_HudSmall, Color.white);
                DrawCoopHud(me, match, top, s);
                return;
            }

            if (match.Mode.Value == GameMode.GemGrab) return;
            PlayerNetwork leader = null;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && (leader == null || p.Kills.Value > leader.Kills.Value)) leader = p;
            float y = top.yMax + 12 * s;
            DrawHudStat(new Rect(cx - 159 * s, y, 98 * s, 34 * s), 0, me.Kills.Value.ToString(), Color.white, s);
            DrawHudStat(new Rect(cx - 49 * s, y, 98 * s, 34 * s), 1, me.Deaths.Value.ToString(), Color.white, s);
            DrawHudStat(new Rect(cx + 61 * s, y, 98 * s, 34 * s), 2,
                (leader != null ? leader.Kills.Value : 0).ToString(), new Color(1f, 0.85f, 0.4f), s);
            string detail = match.Mode.Value == GameMode.Deathmatch ? "TARGET " + match.killLimit +
                (leader != null && leader.Kills.Value > 0 ? " · LEADER " + leader.DisplayName : "") :
                match.Mode.Value == GameMode.Showdown ? "ALIVE " + match.AlivePlayerCount :
                match.Mode.Value == GameMode.GemGrab ? "" : $"BLUE {match.BlueScore.Value} — {match.RedScore.Value} RED";
            HudLabel(new Rect(top.x - 15 * s, y + 37 * s, top.width + 30 * s, 20 * s), detail, m_HudSmall, Color.white);
        }

        void DrawHudStat(Rect rect, int icon, string value, Color tint, float s)
        {
            Tex(new Rect(rect.x, rect.y, 30 * s, 30 * s), HudIcon(icon), tint);
            HudLabel(new Rect(rect.x + 37 * s, rect.y, rect.width - 37 * s, 30 * s), value, m_HudName, tint);
        }

        void DrawHudFeed(float s)
        {
            float width = Mathf.Min(270 * s, Screen.width * 0.25f);
            float x = Screen.width - width - 22 * s;
            float y = 20 * s;
            foreach (var item in m_KillFeed)
            {
                float alpha = Mathf.Clamp01((6f - (Time.time - item.time)) * 2f);
                var row = new Rect(x, y, width, 34 * s);
                Panel(row, new Color(HudGlass.r, HudGlass.g, HudGlass.b, HudGlass.a * alpha));
                float half = (width - 52 * s) * 0.5f;
                // Colunas fixas e recortadas: nomes compridos não saem do painel.
                HudLabel(new Rect(x + 12 * s, y, half, row.height), string.IsNullOrEmpty(item.killer) ? "" : item.killer,
                    m_HudFeed, Art.WithAlpha(item.killerColor, alpha));
                HudLabel(new Rect(x + half + 15 * s, y, 26 * s, row.height), string.IsNullOrEmpty(item.killer) ? "·" : "»",
                    m_HudName, new Color(HudCyan.r, HudCyan.g, HudCyan.b, alpha));
                HudLabel(new Rect(x + half + 42 * s, y, half, row.height),
                    item.victim + (string.IsNullOrEmpty(item.killer) ? " fell" : ""), m_HudFeed, Art.WithAlpha(item.victimColor, alpha));
                y += 42 * s;
            }
        }

        void DrawHudSuperChrome(Rect rect, float charge, bool ready, bool inMode, float s)
        {
            if (m_HudGradient == null)
                m_HudGradient = MakeMenuTex(128, (x, y) =>
                {
                    float distance = new Vector2(x + 0.5f - 64, y + 0.5f - 64).magnitude;
                    var c = Color.Lerp(new Color(0.5f, 0.89f, 0.25f), new Color(0.02f, 0.65f, 0.73f), x / 127f);
                    c.a = Mathf.Clamp01(63f - distance); return c;
                });
            if (ready)
            {
                float extra = (12f + Mathf.Sin(Time.unscaledTime * 5f) * 3f) * s;
                Tex(new Rect(rect.x - extra, rect.y - extra, rect.width + extra * 2, rect.height + extra * 2),
                    Art.SoftTexture, new Color(HudCyan.r, HudCyan.g, HudCyan.b, 0.35f));
            }
            Tex(rect, m_Circle, HudGlass);
            if (charge > 0f && Event.current.type == EventType.Repaint)
            {
                var fill = new Rect(rect.x + 7 * s, rect.y + 7 * s, rect.width - 14 * s, rect.height - 14 * s);
                var old = GUI.color; GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(new Rect(fill.x, fill.y + fill.height * (1f - charge), fill.width, fill.height * charge),
                    m_HudGradient, new Rect(0, 0, 1, charge));
                GUI.color = old;
            }
            Tex(rect, m_Ring, ready ? HudCyan : new Color(0.5f, 0.73f, 0.81f, 0.3f));
            if (!inMode)
                Tex(new Rect(rect.center.x - 28 * s, rect.center.y - 33 * s, 56 * s, 66 * s), HudIcon(3), Color.white);
        }

        Texture2D HudIcon(int kind)
        {
            if (m_HudIcons[kind] != null) return m_HudIcons[kind];
            Vector2[] polygon = kind == 2 ? new[] { new Vector2(0.12f, 0.74f), new Vector2(0.27f, 0.58f),
                new Vector2(0.5f, 0.87f), new Vector2(0.73f, 0.58f), new Vector2(0.88f, 0.74f),
                new Vector2(0.76f, 0.19f), new Vector2(0.24f, 0.19f) } :
                new[] { new Vector2(0.46f, 0.94f), new Vector2(0.78f, 0.94f), new Vector2(0.56f, 0.59f),
                    new Vector2(0.81f, 0.59f), new Vector2(0.29f, 0.06f), new Vector2(0.42f, 0.45f), new Vector2(0.22f, 0.45f) };
            m_HudIcons[kind] = MakeMenuTex(64, (x, y) =>
            {
                float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
                float dx = u - 0.5f, dy = v - 0.5f, radius = Mathf.Sqrt(dx * dx + dy * dy);
                bool on;
                if (kind == 0)
                    on = Mathf.Abs(radius - 0.32f) < 0.035f || Mathf.Abs(radius - 0.17f) < 0.025f ||
                        (Mathf.Abs(dx) < 0.025f && Mathf.Abs(dy) < 0.46f) || (Mathf.Abs(dy) < 0.025f && Mathf.Abs(dx) < 0.46f);
                else if (kind == 1)
                {
                    bool head = new Vector2(dx, (v - 0.61f) * 0.9f).magnitude < 0.28f ||
                        (Mathf.Abs(dx) < 0.18f && v > 0.25f && v < 0.55f);
                    bool eyes = new Vector2(u - 0.39f, v - 0.62f).magnitude < 0.065f ||
                        new Vector2(u - 0.61f, v - 0.62f).magnitude < 0.065f;
                    on = head && !eyes || (Mathf.Abs(v - 0.2f - dx * 0.34f) < 0.028f ||
                        Mathf.Abs(v - 0.2f + dx * 0.34f) < 0.028f) && Mathf.Abs(dx) < 0.36f;
                }
                else on = HudPointInPolygon(new Vector2(u, v), polygon);
                return new Color(1, 1, 1, on ? 1 : 0);
            });
            return m_HudIcons[kind];
        }

        static bool HudPointInPolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
