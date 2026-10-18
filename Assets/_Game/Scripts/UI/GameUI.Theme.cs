using UnityEngine;

namespace FPSLAN
{
    public partial class GameUI
    {
        public static void DrawSharedMenuBackground()
        {
            MenuTheme.Ensure();
            if (s_Instance != null && !s_Instance.m_InGame)
            {
                s_Instance.EnsureMenuStyles();
                s_Instance.m_MAlpha = 1f;
                s_Instance.DrawMenuBackground();
            }
            else MenuTheme.Line(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.05f, 0.1f, 0.82f));
        }

        void DrawProgressionHub()
        {
            var panel = VR(940, 104, 430, 502);
            DrawTechPanel(panel, true);
            MText(new Rect(panel.x + 24 * K, panel.y + 20 * K, panel.width - 48 * K, 36 * K), "PROGRESSÃO", m_MT24, MWhite, TextAnchor.MiddleLeft);
            ProgressionUI.DrawHeaderStatus(new Rect(panel.x + 24 * K, panel.y + 66 * K, panel.width - 48 * K, 34 * K));
            DrawTechLine(new Rect(panel.x + 24 * K, panel.y + 116 * K, panel.width - 48 * K, 2 * K), true);
            var labels = new[] { "PERFIL", "PERSONAGENS", "NÍVEIS", "SKINS", "PETS", "MISSÕES", "RECOMPENSAS", "DEFINIÇÕES" };
            var screens = new[] { ProgressionScreen.Profile, ProgressionScreen.Characters, ProgressionScreen.Levels, ProgressionScreen.Skins, ProgressionScreen.Pets, ProgressionScreen.Quests, ProgressionScreen.Rewards };
            float w = (panel.width - 60 * K) / 2f;
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(panel.x + 24 * K + (i % 2) * (w + 12 * K), panel.y + 146 * K + (i / 2) * 78 * K, w, 58 * K);
                if (MenuButton(r, labels[i], 7200 + i, MBtn.Normal, false, m_MT14) != MClick.Clicked) continue;
                if (i == 7) OpenSettings(); else ProgressionUI.Open(screens[i]);
            }
        }
    }
}
