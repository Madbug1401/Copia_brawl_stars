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
        const string KeyAimSensitivity = "fpslan.aim.sensitivity";
        const string KeyScopeFov = "fpslan.aim.scopeFov";
        const string KeyPadEnabled = "fpslan.pad.enabled";
        const string KeyPadLook = "fpslan.pad.look";
        const string KeyPadInvertY = "fpslan.pad.invertY";
        const string KeyMasterVolume = "fpslan.audio.masterVolume";
        const string KeyMuted = "fpslan.audio.muted";

        public static string LocalName
        {
            get => PlayerPrefs.GetString(KeyName, "Player" + Random.Range(10, 99));
            set => PlayerPrefs.SetString(KeyName, Sanitize(value));
        }

        public static int LocalHeroId
        {
            get => PlayerPrefs.GetInt(KeyHero, 0);
            set => PlayerPrefs.SetInt(KeyHero, value);
        }

        public static float Sensitivity
        {
            get => GetClampedFloat(KeySens, 2f, 0.2f, 10f);
            set => PlayerPrefs.SetFloat(KeySens, Mathf.Clamp(value, 0.2f, 10f));
        }

        public static float AimSensitivity
        {
            get => GetClampedFloat(KeyAimSensitivity, 0.5f, 0.2f, 1f);
            set => PlayerPrefs.SetFloat(KeyAimSensitivity, Mathf.Clamp(value, 0.2f, 1f));
        }

        /// <summary>Limites do zoom da luneta (campo de visão em graus): menos graus = mais zoom.</summary>
        public const float ScopeMinFov = 10f, ScopeMaxFov = 55f, ScopeDefaultFov = 32f;
        /// <summary>Campo de visão normal da 1.ª pessoa (para mostrar o zoom como "4.0x").</summary>
        public const float NormalFov = 85f;

        public static float ScopeFieldOfView
        {
            get => GetClampedFloat(KeyScopeFov, ScopeDefaultFov, ScopeMinFov, ScopeMaxFov);
            set => PlayerPrefs.SetFloat(KeyScopeFov, Mathf.Clamp(value, ScopeMinFov, ScopeMaxFov));
        }

        /// <summary>Roda do rato na luneta: cada passo para a frente aproxima ~18%, para trás afasta. Fica guardado.</summary>
        public static void ZoomScope(float notches)
        {
            if (Mathf.Approximately(notches, 0f)) return;
            ScopeFieldOfView = ScopeFieldOfView * Mathf.Pow(0.82f, notches);
        }

        /// <summary>Ampliação da luneta em relação à vista normal (ex.: 3.5x).</summary>
        public static float ScopeMagnification =>
            Mathf.Tan(NormalFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(ScopeFieldOfView * 0.5f * Mathf.Deg2Rad);

        /// <summary>Comando PlayStation ativo (definições ▸ controlos).</summary>
        public static bool ControllerEnabled
        {
            get => PlayerPrefs.GetInt(KeyPadEnabled, 1) == 1;
            set => PlayerPrefs.SetInt(KeyPadEnabled, value ? 1 : 0);
        }

        /// <summary>Sensibilidade do stick direito (1 = 200°/s no máximo).</summary>
        public static float ControllerLookSensitivity
        {
            get => GetClampedFloat(KeyPadLook, 1f, 0.3f, 3f);
            set => PlayerPrefs.SetFloat(KeyPadLook, Mathf.Clamp(value, 0.3f, 3f));
        }

        public static bool ControllerInvertY
        {
            get => PlayerPrefs.GetInt(KeyPadInvertY, 0) == 1;
            set => PlayerPrefs.SetInt(KeyPadInvertY, value ? 1 : 0);
        }

        public static float MasterVolume
        {
            get => GetClampedFloat(KeyMasterVolume, 1f, 0f, 1f);
            set
            {
                float volume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(KeyMasterVolume, volume);
                if (volume > 0.001f) PlayerPrefs.SetInt(KeyMuted, 0);   // mexer no volume tira o mudo
                ApplyAudioSettings();
            }
        }

        /// <summary>Silenciar sem perder o volume escolhido (botão de mudo do menu).</summary>
        public static bool Muted
        {
            get => PlayerPrefs.GetInt(KeyMuted, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(KeyMuted, value ? 1 : 0);
                ApplyAudioSettings();
            }
        }

        public static void ApplyAudioSettings() => AudioListener.volume = Muted ? 0f : MasterVolume;

        static float GetClampedFloat(string key, float fallback, float minimum, float maximum)
        {
            float value = PlayerPrefs.GetFloat(key, fallback);
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                Debug.LogWarning("Preferência numérica inválida '" + key + "'; será usada a predefinição.");
                value = fallback;
                PlayerPrefs.SetFloat(key, fallback);
            }
            return Mathf.Clamp(value, minimum, maximum);
        }

        public static string LastIp
        {
            get => PlayerPrefs.GetString(KeyIp, "127.0.0.1");
            set => PlayerPrefs.SetString(KeyIp, value);
        }

        /// <summary>Nomes curtos e só com caracteres de 1–2 bytes, para caberem num FixedString64Bytes.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Player";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.Trim())
            {
                if (c < 32 || c >= 0x800) continue;
                sb.Append(c);
                if (sb.Length >= 16) break;
            }
            return sb.Length == 0 ? "Player" : sb.ToString();
        }
    }
}
