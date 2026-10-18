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
            var panel = VR(1020, 106, 350, 106);
            if (MenuButton(panel, "", 7200, MBtn.Normal, false, m_MT17) == MClick.Clicked)
                ProgressionUI.Open(ProgressionScreen.Profile);
            MText(new Rect(panel.x + 22 * K, panel.y + 14 * K, panel.width - 44 * K, 30 * K), m_Name, m_MT20, MWhite, TextAnchor.MiddleLeft);
            var ctx = GameExperience.Current;
            ctx.Config.playerLevels.Progress(ctx.Profile.playerXp, out _, out int into, out int span);
            var bar = new Rect(panel.x + 22 * K, panel.y + 64 * K, panel.width - 44 * K, 12 * K);
            MFill(bar, new Color(0.02f, 0.05f, 0.1f, 0.9f));
            MFill(new Rect(bar.x, bar.y, bar.width * (span > 0 ? into / (float)span : 1f), bar.height), MCyan);
        }
    }
}
