using UnityEngine;

namespace FPSLAN
{
    /// <summary>HUD dos emotes de marca: roda de sprays SITA (manter [Y]) e faixa "a alimentar com UPRA".</summary>
    public partial class GameUI
    {
        void DrawHudEmotes(PlayerNetwork me, float s)
        {
            if (me == null || me.Health.IsDead) return;
            if (me.SprayWheelOpen) DrawSprayWheel(me, s);
            else
            {
                var hint = me.SprayHint;
                if (!string.IsNullOrEmpty(hint))
                    HudLabel(new Rect(0, Screen.height * 0.5f + 70 * s, Screen.width, 24 * s), hint, m_HudSmall, new Color(1f, 0.85f, 0.4f));
            }
            DrawFeedingBanner(me, s);
        }

        void DrawSprayWheel(PlayerNetwork me, float s)
        {
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            float R = 150f * s;
            var colors = Branding.SprayColors;
            int sel = me.SprayWheelSelection;

            // Fundo
            float bg = R * 2f + 150f * s;
            Tex(new Rect(cx - bg / 2f, cy - bg / 2f, bg, bg), m_Circle, new Color(0.02f, 0.03f, 0.06f, 0.62f));
            Tex(new Rect(cx - R - 4 * s, cy - R - 4 * s, (R + 4 * s) * 2f, (R + 4 * s) * 2f), m_Ring, new Color(1f, 1f, 1f, 0.12f));

            for (int i = 0; i < colors.Length; i++)
            {
                float a = i * (360f / colors.Length) * Mathf.Deg2Rad;
                var p = new Vector2(cx + Mathf.Sin(a) * R, cy - Mathf.Cos(a) * R);
                bool on = i == sel;
                float size = (on ? 104f : 78f) * s;
                if (on) Tex(new Rect(p.x - size * 0.62f, p.y - size * 0.62f, size * 1.24f, size * 1.24f), m_Circle, new Color(1f, 1f, 1f, 0.22f));
                Tex(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), Branding.PaintSplat, colors[i].color);
                float ls = size * 0.86f;
                Tex(new Rect(p.x - ls / 2f, p.y - ls / 2f, ls, ls), Branding.SitaStencil, Color.white);
            }

            // Centro: logótipo + cor escolhida
            float logo = 150f * s;
            Tex(new Rect(cx - logo / 2f, cy - logo / 2f - 14 * s, logo, logo), Branding.SitaStencil, Color.white);
            string title = sel >= 0 ? colors[sel].name : "ESCOLHE A COR";
            HudLabel(new Rect(cx - 160 * s, cy + 28 * s, 320 * s, 24 * s), title, m_HudSmall, sel >= 0 ? Color.Lerp(colors[sel].color, Color.white, 0.25f) : Color.white);

            // Ponteiro do rato
            var v = me.SprayWheelVector;
            float d = 10f * s;
            Tex(new Rect(cx + v.x * R * 0.55f - d / 2f, cy - v.y * R * 0.55f - d / 2f, d, d), m_Circle, new Color(1f, 1f, 1f, 0.9f));

            string key = GameInputSettings.BindingLabel(GameAction.Emote);
            string foot = me.SprayCooldownRemaining > 0f ? "SPRAY READY IN " + Mathf.CeilToInt(me.SprayCooldownRemaining) + "s"
                : sel >= 0 ? "RELEASE [" + key + "] TO SPRAY" : "MOVE THE MOUSE TO PICK A COLOR · RELEASE TO CANCEL";
            HudLabel(new Rect(0, cy + R + 64 * s, Screen.width, 22 * s), foot, m_HudSmall, new Color(1f, 1f, 1f, 0.85f));
            HudLabel(new Rect(0, cy + R + 86 * s, Screen.width, 20 * s), "SITA · DÁ COR AO TEU JOGO", m_HudSmall, new Color(1f, 0.85f, 0.25f, 0.85f));
        }

        void DrawFeedingBanner(PlayerNetwork me, float s)
        {
            float t = me.FeedAnimTime;
            if (t < 0f && !me.FeedPending) return;
            var pet = GameExperience.Current.PetCatalog.FindByHash(me.Loadout.Value.petHash);
            if (pet == null) return;
            float w = 420f * s, h = 74f * s;
            var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.70f, w, h);
            HudPlate(r, new Color(0.06f, 0.12f, 0.22f, 0.85f), Branding.UpraBlue, 3f * s);
            float icon = h - 8 * s;
            Tex(new Rect(r.x + 6 * s, r.y + 4 * s, icon, icon), Branding.UpraSack, Color.white);
            float x = r.x + icon + 16 * s, tw = r.xMax - x - 12 * s;
            HudLabel(new Rect(x, r.y + 8 * s, tw, 26 * s), Branding.UpraFormula(pet.species).ToUpperInvariant(), m_HudTitle, Color.white);
            HudLabel(new Rect(x, r.y + 34 * s, tw, 16 * s), "FEEDING " + pet.displayName.ToUpperInvariant() + " · " + PetFoodRules.BoostText(pet.ability).ToUpperInvariant(), m_HudSmall, HudLime);
            var bar = new Rect(x, r.yMax - 16 * s, tw, 8 * s);
            Panel(bar, new Color(0f, 0f, 0f, 0.5f));
            Panel(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(Mathf.Max(0f, t) / PetFoodRules.FeedAnimDuration), bar.height), new Color(0.95f, 0.75f, 0.3f));
        }
    }
}
