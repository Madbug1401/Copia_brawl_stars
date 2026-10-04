using UnityEngine;

namespace FPSLAN
{
    /// <summary>Preferências locais do jogador (nome, personagem, sensibilidade, último IP).</summary>
    public static class GameSession
    {
        const string KeyName = "fpslan.name";
        const string KeyHero = "fpslan.hero";
        const string KeySens = "fpslan.sens";
        const string KeyIp = "fpslan.ip";

        public static string LocalName
        {
            get => PlayerPrefs.GetString(KeyName, "Jogador" + Random.Range(10, 99));
            set => PlayerPrefs.SetString(KeyName, Sanitize(value));
        }

        public static int LocalHeroId
        {
            get => PlayerPrefs.GetInt(KeyHero, 0);
            set => PlayerPrefs.SetInt(KeyHero, value);
        }

        public static float Sensitivity
        {
            get => PlayerPrefs.GetFloat(KeySens, 2f);
            set => PlayerPrefs.SetFloat(KeySens, Mathf.Clamp(value, 0.2f, 10f));
        }

        public static string LastIp
        {
            get => PlayerPrefs.GetString(KeyIp, "127.0.0.1");
            set => PlayerPrefs.SetString(KeyIp, value);
        }

        /// <summary>Nomes curtos e só com caracteres de 1–2 bytes, para caberem num FixedString64Bytes.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Jogador";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.Trim())
            {
                if (c < 32 || c >= 0x800) continue;
                sb.Append(c);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? "Jogador" : sb.ToString();
        }
    }
}
