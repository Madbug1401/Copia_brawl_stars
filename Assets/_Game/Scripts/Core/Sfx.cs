using System;
using UnityEngine;

namespace FPSLAN
{
    public enum Sound
    {
        Shot,
        SuperShot,
        Hit,
        HeadHit,
        Kill,
        Step,
        Reload,
        Death,
        Empty,
        Hurt,
        Lob,
        Bow,
        Spray,
        Explosion,
        MineArm,
        Shotgun,
        Sniper,
        Launch,
        Respawn,
        SuperReady,
        Poof,
        Twirl,
        Crate,
        Melee,
        Count
    }

    /// <summary>
    /// Sons placeholder gerados por código (sem assets). Substituir por clips reais na fase de game feel.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;

        static AudioClip[] s_Clips;
        static AudioSource s_2D;
        static AudioSource[] s_3D;
        static int s_Next;
        static readonly System.Random s_Rng = new System.Random(1234);

        static void Init()
        {
            if (s_Clips != null && s_2D != null) return;

            s_Clips = new AudioClip[(int)Sound.Count];
            // Revólver: estalo seco + "thump" grave
            s_Clips[(int)Sound.Shot] = Make("shot", 0.16f, t => Noise() * Env(t, 0.16f, 34f) * 0.5f + Sin(t, 150f - t * 500f) * Env(t, 0.1f, 30f) * 0.45f + Sq(t, 900f) * Env(t, 0.015f, 200f) * 0.15f);
            s_Clips[(int)Sound.SuperShot] = MakeLow("super", 0.35f, t => Noise() * Env(t, 0.35f, 10f) * 0.8f + Sin(t, 90f - t * 120f) * Env(t, 0.3f, 9f) * 0.6f, t => 5000f - t * 9000f);
            s_Clips[(int)Sound.Hit] = Make("hit", 0.08f, t => (Sin(t, 1650f) + Sin(t, 2475f) * 0.3f) * Env(t, 0.08f, 40f) * 0.45f);
            s_Clips[(int)Sound.HeadHit] = Make("headhit", 0.12f, t => (Sin(t, 2200f) + Sin(t, 3300f) * 0.5f) * Env(t, 0.12f, 28f) * 0.4f);
            s_Clips[(int)Sound.Kill] = Make("kill", 0.42f, t => (Sin(t, t < 0.1f ? 880f : (t < 0.2f ? 1175f : 1760f)) + Sin(t, 440f) * 0.3f) * Env(t, 0.42f, 6f) * 0.38f);
            s_Clips[(int)Sound.Step] = MakeLow("step", 0.07f, t => (Noise() * 0.7f + Sin(t, 85f)) * Env(t, 0.07f, 55f) * 0.4f, t => 1400f);
            s_Clips[(int)Sound.Reload] = Make("reload", 0.09f, t => (Sq(t, 700f) * Env(t, 0.03f, 90f) + Sq(t, 1100f) * Env(Mathf.Max(0f, t - 0.05f), 0.03f, 90f) * (t > 0.05f ? 1f : 0f)) * 0.18f);
            s_Clips[(int)Sound.Death] = Make("death", 0.5f, t => (Sin(t, 420f - t * 600f) + Sin(t, 210f - t * 300f) * 0.5f) * Env(t, 0.5f, 5f) * 0.4f);
            s_Clips[(int)Sound.Empty] = Make("empty", 0.05f, t => Sq(t, 300f) * Env(t, 0.05f, 90f) * 0.15f);
            s_Clips[(int)Sound.Hurt] = MakeLow("hurt", 0.16f, t => (Sin(t, 200f - t * 200f) + Noise() * 0.4f) * Env(t, 0.16f, 20f) * 0.45f, t => 2200f);

            s_Clips[(int)Sound.Lob] = Make("lob", 0.2f, t => Sin(t, 220f + t * 1400f) * Env(t, 0.2f, 14f) * 0.3f + Noise() * Env(t, 0.06f, 50f) * 0.25f);
            s_Clips[(int)Sound.Bow] = Make("bow", 0.22f, t => (Sin(t, 330f + Mathf.Sin(t * 120f) * 20f) * Env(t, 0.22f, 16f) * 0.45f + Noise() * Env(t, 0.04f, 60f) * 0.35f));
            s_Clips[(int)Sound.Spray] = MakeLow("spray", 0.3f, t => Noise() * Env(t, 0.3f, 6f) * 0.45f, t => 6000f + Mathf.Sin(t * 60f) * 1500f);
            s_Clips[(int)Sound.Explosion] = MakeLow("explosion", 0.9f, t => (Noise() * 0.9f + Sin(t, 70f - t * 40f) * 0.9f) * Env(t, 0.9f, 4.5f) * 0.85f, t => Mathf.Max(250f, 4000f * Mathf.Exp(-t * 6f)));
            s_Clips[(int)Sound.MineArm] = Make("minearm", 0.12f, t => Sin(t, t < 0.06f ? 1200f : 1600f) * Env(t, 0.12f, 20f) * 0.28f);

            s_Clips[(int)Sound.Shotgun] = MakeLow("shotgun", 0.4f, t => (Noise() * 1.0f + Sin(t, 65f - t * 60f) * 0.8f) * Env(t, 0.4f, 9f) * 0.8f, t => Mathf.Max(600f, 7000f * Mathf.Exp(-t * 10f)));
            s_Clips[(int)Sound.Sniper] = MakeLow("sniper", 0.7f, t => (Noise() * Env(t, 0.7f, 6f) * 0.7f + Sq(t, 1300f) * Env(t, 0.02f, 150f) * 0.3f + Sin(t, 110f) * Env(t, 0.2f, 14f) * 0.5f), t => Mathf.Max(400f, 9000f * Mathf.Exp(-t * 7f)));
            s_Clips[(int)Sound.Launch] = MakeLow("launch", 0.45f, t => (Noise() * 0.6f + Sin(t, 200f + t * 900f) * 0.5f) * Env(t, 0.45f, 5f) * 0.5f, t => 800f + t * 6000f);
            s_Clips[(int)Sound.Respawn] = Make("respawn", 0.55f, t => (Sin(t, t < 0.12f ? 523f : (t < 0.24f ? 659f : (t < 0.36f ? 784f : 1047f))) * 0.6f + Sin(t, 1568f) * 0.15f) * Env(t, 0.55f, 4f) * 0.32f);
            s_Clips[(int)Sound.SuperReady] = Make("superready", 0.5f, t => (Sin(t, t < 0.15f ? 988f : 1319f) + Sin(t, t < 0.15f ? 1976f : 2637f) * 0.3f) * Env(t, 0.5f, 5f) * 0.35f);
            s_Clips[(int)Sound.Poof] = MakeLow("poof", 0.35f, t => Noise() * Env(t, 0.35f, 9f) * 0.5f, t => 1500f);
            s_Clips[(int)Sound.Twirl] = Make("twirl", 0.3f, t => Sq(t, 2000f) * Env(t % 0.06f, 0.012f, 300f) * 0.12f);
            s_Clips[(int)Sound.Crate] = MakeLow("crate", 0.4f, t => (Noise() * 0.9f + Sq(t, 140f - t * 100f) * 0.3f) * Env(t, 0.4f, 9f) * 0.7f, t => 2500f);
            s_Clips[(int)Sound.Melee] = MakeLow("melee", 0.24f, t => Noise() * Env(t, 0.24f, 15f) * 0.55f + Sin(t, 420f - t * 1100f) * Env(t, 0.2f, 12f) * 0.45f, t => 5000f - t * 16000f);

            var go = new GameObject("[Sfx]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_2D = go.AddComponent<AudioSource>();
            s_2D.spatialBlend = 0f;
            s_2D.playOnAwake = false;

            s_3D = new AudioSource[16];
            for (int i = 0; i < s_3D.Length; i++)
            {
                var c = new GameObject("Sfx3D_" + i);
                c.transform.SetParent(go.transform);
                var src = c.AddComponent<AudioSource>();
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 45f;
                src.playOnAwake = false;
                s_3D[i] = src;
            }
        }

        public static void Play2D(Sound sound, float volume = 1f)
        {
            Init();
            var clip = s_Clips[(int)sound];
            if (clip != null) s_2D.PlayOneShot(clip, volume);
        }

        public static void PlayAt(Sound sound, Vector3 position, float volume = 1f)
        {
            Init();
            var clip = s_Clips[(int)sound];
            if (clip == null) return;
            var src = s_3D[s_Next];
            s_Next = (s_Next + 1) % s_3D.Length;
            src.transform.position = position;
            src.pitch = 0.95f + (float)s_Rng.NextDouble() * 0.1f;
            src.PlayOneShot(clip, volume);
        }

        static AudioClip Make(string name, float duration, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Clip com filtro passa-baixo (corte variável no tempo) — sons mais "cheios" (explosões, passos).</summary>
        static AudioClip MakeLow(string name, float duration, Func<float, float> f, Func<float, float> cutoff)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = f(t);
                float fc = Mathf.Clamp(cutoff(t), 40f, Rate * 0.45f);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * fc / Rate);
                y += a * (x - y);
                data[i] = Mathf.Clamp(y * 1.3f, -1f, 1f);
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(s_Rng.NextDouble() * 2.0 - 1.0);
        static float Sin(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Sq(float t, float hz) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));
        static float Env(float t, float dur, float decay) => Mathf.Clamp01(t * 400f) * Mathf.Exp(-decay * t) * (t < dur ? 1f : 0f);
    }
}
