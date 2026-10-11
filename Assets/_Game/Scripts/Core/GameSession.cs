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
        const string KeyInvertMobileLookY = "fpslan.mobile.invertLookY";
        const string KeyMobileSensitivity = "fpslan.mobile.sensitivity";
        const string KeyMobileButtonScale = "fpslan.mobile.buttonScale";
        const string KeyMobileAimToggle = "fpslan.mobile.aimToggle";
        const string KeyAimSensitivity = "fpslan.aim.sensitivity";
        const string KeyScopeFov = "fpslan.aim.scopeFov";
        const string KeyMasterVolume = "fpslan.audio.masterVolume";

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

        public static bool InvertMobileLookY
        {
            get => PlayerPrefs.GetInt(KeyInvertMobileLookY, 0) != 0;
            set => PlayerPrefs.SetInt(KeyInvertMobileLookY, value ? 1 : 0);
        }

        public static float MobileSensitivity
        {
            get => PlayerPrefs.GetFloat(KeyMobileSensitivity, 1f);
            set => PlayerPrefs.SetFloat(KeyMobileSensitivity, Mathf.Clamp(value, 0.25f, 3f));
        }

        public static float MobileButtonScale
        {
            get => PlayerPrefs.GetFloat(KeyMobileButtonScale, 1f);
            set => PlayerPrefs.SetFloat(KeyMobileButtonScale, Mathf.Clamp(value, 0.7f, 1.4f));
        }

        public static bool MobileAimToggle
        {
            get => PlayerPrefs.GetInt(KeyMobileAimToggle, 0) != 0;
            set => PlayerPrefs.SetInt(KeyMobileAimToggle, value ? 1 : 0);
        }

        public static float AimSensitivity
        {
            get => PlayerPrefs.GetFloat(KeyAimSensitivity, 0.5f);
            set => PlayerPrefs.SetFloat(KeyAimSensitivity, Mathf.Clamp(value, 0.2f, 1f));
        }

        public static float ScopeFieldOfView
        {
            get => PlayerPrefs.GetFloat(KeyScopeFov, 32f);
            set => PlayerPrefs.SetFloat(KeyScopeFov, Mathf.Clamp(value, 20f, 55f));
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyMasterVolume, 1f);
            set
            {
                float volume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(KeyMasterVolume, volume);
                AudioListener.volume = volume;
            }
        }

        public static void ApplyAudioSettings() => AudioListener.volume = MasterVolume;

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
