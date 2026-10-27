using UnityEngine;

namespace FPSLAN
{
    /// <summary>Embedded typography also works on computers without the fonts installed.</summary>
    public static class GameTypography
    {
        static Font s_Body, s_Title, s_Numbers, s_Display;
        public static Font Body => s_Body != null ? s_Body : (s_Body = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Arial" }, 24));
        public static Font Title => s_Title != null ? s_Title : (s_Title = Resources.Load<Font>("Fonts/GoblinOne-Regular") ?? Body);
        public static Font Numbers => s_Numbers != null ? s_Numbers : (s_Numbers = Resources.Load<Font>("Fonts/LeagueGothic-Regular") ?? Body);
        /// <summary>Só algarismos e sinais, com os sufixos "HP"/"XP" permitidos ("120", "+35 XP"). Sem alocações:
        /// é chamado para cada texto desenhado no HUD.</summary>
        public static bool IsNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            bool digit = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if ((ch == 'H' || ch == 'X') && i + 1 < text.Length && text[i + 1] == 'P') { i++; continue; }
                if (char.IsLetter(ch)) return false;
                if (char.IsDigit(ch)) digit = true;
            }
            return digit;
        }
        // Add a licensed HKModular font here when the project owner supplies it.
        public static Font Display => s_Display != null ? s_Display : (s_Display = Resources.Load<Font>("Fonts/HKModular") ?? Body);
    }
}
