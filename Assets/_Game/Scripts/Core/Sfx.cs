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
            s_Clips[(int)Sound.Shot] = Make("shot", 0.11f, t => Noise() * Env(t, 0.11f, 30f) * 0.55f + Sq(t, 180f - t * 900f) * Env(t, 0.06f, 50f) * 0.25f);
            s_Clips[(int)Sound.SuperShot] = Make("super", 0.18f, t => Noise() * Env(t, 0.18f, 18f) * 0.6f + Sq(t, 120f) * Env(t, 0.12f, 25f) * 0.3f);
            s_Clips[(int)Sound.Hit] = Make("hit", 0.07f, t => Sin(t, 1650f) * Env(t, 0.07f, 45f) * 0.45f);
            s_Clips[(int)Sound.HeadHit] = Make("headhit", 0.09f, t => (Sin(t, 2200f) + Sin(t, 3300f) * 0.5f) * Env(t, 0.09f, 35f) * 0.35f);
            s_Clips[(int)Sound.Kill] = Make("kill", 0.28f, t => Sin(t, t < 0.12f ? 880f : 1320f) * Env(t, 0.28f, 10f) * 0.4f);
            s_Clips[(int)Sound.Step] = Make("step", 0.06f, t => (Noise() * 0.5f + Sin(t, 90f)) * Env(t, 0.06f, 60f) * 0.35f);
            s_Clips[(int)Sound.Reload] = Make("reload", 0.05f, t => Sq(t, 700f) * Env(t, 0.05f, 70f) * 0.2f);
            s_Clips[(int)Sound.Death] = Make("death", 0.45f, t => Sin(t, 300f - t * 450f) * Env(t, 0.45f, 6f) * 0.45f);
            s_Clips[(int)Sound.Empty] = Make("empty", 0.04f, t => Sq(t, 300f) * Env(t, 0.04f, 90f) * 0.15f);
            s_Clips[(int)Sound.Hurt] = Make("hurt", 0.12f, t => (Sin(t, 220f) + Noise() * 0.3f) * Env(t, 0.12f, 25f) * 0.35f);

            s_Clips[(int)Sound.Lob] = Make("lob", 0.14f, t => Sin(t, 260f + t * 900f) * Env(t, 0.14f, 20f) * 0.35f + Noise() * Env(t, 0.05f, 60f) * 0.2f);
            s_Clips[(int)Sound.Bow] = Make("bow", 0.12f, t => (Sin(t, 520f - t * 1800f) * 0.5f + Noise() * 0.25f) * Env(t, 0.12f, 30f));
            s_Clips[(int)Sound.Spray] = Make("spray", 0.22f, t => Noise() * Env(t, 0.22f, 9f) * 0.35f);
            s_Clips[(int)Sound.Explosion] = Make("explosion", 0.45f, t => (Noise() * 0.8f + Sin(t, 55f) * 0.6f) * Env(t, 0.45f, 9f) * 0.7f);
            s_Clips[(int)Sound.MineArm] = Make("minearm", 0.08f, t => Sin(t, 1200f) * Env(t, 0.08f, 40f) * 0.25f);

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

        static float Noise() => (float)(s_Rng.NextDouble() * 2.0 - 1.0);
        static float Sin(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Sq(float t, float hz) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));
        static float Env(float t, float dur, float decay) => Mathf.Clamp01(t * 400f) * Mathf.Exp(-decay * t) * (t < dur ? 1f : 0f);
    }
}
