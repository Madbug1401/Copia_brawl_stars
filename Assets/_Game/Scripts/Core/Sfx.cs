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
        // Game Experience (acrescentados antes de Count)
        Smg,
        Minigun,
        MinigunSpin,
        FatalShot,
        Snare,
        PoisonHiss,
        LevelUp,
        Unlock,
        Reward,
        Victory,
        QuestComplete,
        PetHappy,
        PetAlert,
        Evolve,
        UiClick,
        Count
    }

    /// <summary>
    /// Clips do catálogo em Resources/Audio, com variações e níveis por efeito.
    /// Os sons ainda sem gravação mantêm o fallback gerado por código.
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;

        static AudioClip[] s_Clips;
        static AudioSource[] s_2D, s_3D;
        static SfxCatalog.Cue[] s_Imported;
        static int[] s_LastVariant;
        static int s_Next2D, s_Next3D;
        static readonly System.Random s_Rng = new System.Random(1234);

        static void Init()
        {
            if (s_Clips != null && s_2D != null && s_2D[0] != null) return;

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

            // Metralhadora compacta: estalo curto e agudo
            s_Clips[(int)Sound.Smg] = MakeLow("smg", 0.09f, t => (Noise() * 0.8f + Sq(t, 520f) * 0.25f) * Env(t, 0.09f, 55f) * 0.55f + Sin(t, 180f - t * 900f) * Env(t, 0.06f, 50f) * 0.35f, t => 6500f);
            // Minigun: rajada grave e cheia
            s_Clips[(int)Sound.Minigun] = MakeLow("minigun", 0.32f, t =>
                (Noise() * 0.7f + Sin(t, 95f) * 0.5f) * Env(t % 0.08f, 0.06f, 70f) * 0.85f + Sq(t, 60f) * Env(t, 0.32f, 6f) * 0.12f, t => 3800f);
            // Rotação da Minigun a acelerar
            s_Clips[(int)Sound.MinigunSpin] = MakeLow("minigunspin", 0.55f, t =>
                (Sq(t, 70f + t * 260f) * 0.35f + Noise() * 0.25f) * Mathf.Clamp01(t * 6f) * Env(t, 0.55f, 2f) * 0.6f, t => 900f + t * 4000f);
            // Tiro fatal: estrondo grave + toque metálico
            s_Clips[(int)Sound.FatalShot] = MakeLow("fatal", 0.9f, t =>
                (Noise() * Env(t, 0.5f, 7f) * 0.8f + Sin(t, 55f) * Env(t, 0.6f, 4f) * 0.9f + Sin(t, 1760f) * Env(t, 0.9f, 3f) * 0.25f), t => Mathf.Max(500f, 9000f * Mathf.Exp(-t * 5f)));
            // Armadilha a fechar: estalo metálico duplo
            s_Clips[(int)Sound.Snare] = Make("snare", 0.25f, t =>
                (Sq(t, 1300f) * Env(t, 0.04f, 120f) + Sq(t, 900f) * Env(Mathf.Max(0f, t - 0.07f), 0.05f, 90f) * (t > 0.07f ? 1f : 0f)) * 0.3f + Noise() * Env(t, 0.25f, 18f) * 0.2f);
            // Veneno: sibilar
            s_Clips[(int)Sound.PoisonHiss] = MakeLow("poison", 0.7f, t => Noise() * Env(t, 0.7f, 3.5f) * 0.4f + Sin(t, 140f + Mathf.Sin(t * 30f) * 20f) * Env(t, 0.5f, 5f) * 0.15f, t => 3000f + Mathf.Sin(t * 12f) * 800f);
            // Progressão
            s_Clips[(int)Sound.LevelUp] = Make("levelup", 0.75f, t =>
            {
                float f = t < 0.12f ? 523f : t < 0.24f ? 659f : t < 0.36f ? 784f : 1047f;
                return (Sin(t, f) * 0.6f + Sin(t, f * 2f) * 0.2f + Sin(t, f * 3f) * 0.08f) * Env(t, 0.75f, 3f) * 0.38f;
            });
            s_Clips[(int)Sound.Unlock] = Make("unlock", 0.6f, t => (Sin(t, 1319f) * Env(t, 0.6f, 6f) + Sin(t, 1976f) * Env(Mathf.Max(0f, t - 0.08f), 0.5f, 6f) * (t > 0.08f ? 1f : 0f) + Sin(t, 2637f) * Env(Mathf.Max(0f, t - 0.16f), 0.4f, 7f) * (t > 0.16f ? 1f : 0f)) * 0.26f);
            s_Clips[(int)Sound.Reward] = Make("reward", 0.35f, t => (Sin(t, t < 0.08f ? 1568f : 2093f) + Sin(t, 3136f) * 0.3f) * Env(t % 0.08f + (t > 0.16f ? 0.02f : 0f), 0.2f, 18f) * 0.3f);
            s_Clips[(int)Sound.Victory] = Make("victory", 1.1f, t =>
            {
                float f = t < 0.15f ? 523f : t < 0.3f ? 659f : t < 0.45f ? 784f : 1047f;
                return (Sq(t, f) * 0.18f + Sin(t, f) * 0.5f + Sin(t, f * 1.5f) * 0.2f) * Env(t, 1.1f, 2.2f) * 0.38f;
            });
            s_Clips[(int)Sound.QuestComplete] = Make("quest", 0.5f, t => (Sin(t, t < 0.12f ? 880f : 1320f) * 0.7f + Sin(t, 2640f) * 0.15f) * Env(t, 0.5f, 5f) * 0.35f);
            s_Clips[(int)Sound.PetHappy] = Make("pethappy", 0.22f, t => Sin(t, 900f + Mathf.Sin(t * 60f) * 250f + t * 900f) * Env(t, 0.22f, 12f) * 0.3f);
            s_Clips[(int)Sound.PetAlert] = Make("petalert", 0.5f, t =>
                (Sq(t, t < 0.12f ? 700f : (t < 0.22f ? 1050f : 820f)) * 0.25f + Sin(t, t < 0.22f ? 1400f : 1640f) * 0.3f) * Env(t, 0.5f, 5f) * 0.4f);
            s_Clips[(int)Sound.Evolve] = MakeLow("evolve", 1.0f, t => (Sin(t, 300f + t * 1200f) * 0.5f + Sin(t, 600f + t * 2400f) * 0.2f + Noise() * 0.1f) * Env(t, 1f, 2.5f) * 0.4f, t => 2000f + t * 6000f);
            // O menu tem um clique curto próprio, independente da recarga real.
            s_Clips[(int)Sound.UiClick] = MakeLow("uiclick", 0.04f, t => Noise() * Env(t, 0.04f, 120f) * 0.25f, t => 2600f);

            s_Imported = new SfxCatalog.Cue[(int)Sound.Count];
            s_LastVariant = new int[(int)Sound.Count];
            for (int i = 0; i < s_LastVariant.Length; i++) s_LastVariant[i] = -1;
            var catalog = Resources.Load<SfxCatalog>("Audio/SfxCatalog");
            if (catalog != null && catalog.cues != null)
                foreach (var cue in catalog.cues)
                {
                    if (cue == null || (int)cue.sound < 0 || cue.sound >= Sound.Count || cue.clips == null || cue.clips.Length == 0) continue;
                    bool valid = true;
                    foreach (var clip in cue.clips) if (clip == null) { valid = false; break; }
                    if (!valid) continue;
                    int index = (int)cue.sound;
                    s_Imported[index] = cue;
                    // As gravações substituem os placeholders sem os deixar em memória.
                    UnityEngine.Object.Destroy(s_Clips[index]);
                    s_Clips[index] = cue.clips[0];
                }

            var go = new GameObject("[Sfx]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_2D = CreatePool(go.transform, "Sfx2D_", 16, false);
            s_3D = CreatePool(go.transform, "Sfx3D_", 32, true);
            s_Next2D = s_Next3D = 0;
        }

        static AudioSource[] CreatePool(Transform parent, string prefix, int count, bool spatial)
        {
            var pool = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var c = new GameObject(prefix + i);
                c.transform.SetParent(parent);
                var src = c.AddComponent<AudioSource>();
                src.spatialBlend = spatial ? 1f : 0f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 2f;
                src.maxDistance = 45f;
                src.dopplerLevel = 0f;
                src.priority = spatial ? 128 : 64;
                src.playOnAwake = false;
                pool[i] = src;
            }
            return pool;
        }

        static AudioClip SelectClip(Sound sound, out float gain, out float pitch)
        {
            int index = (int)sound;
            var cue = s_Imported[index];
            gain = cue != null ? Mathf.Clamp01(cue.volume) : 1f;
            float variation = cue != null ? cue.pitchVariation : 0f;
            pitch = 1f + ((float)s_Rng.NextDouble() * 2f - 1f) * variation;
            if (cue == null) return s_Clips[index];
            int count = cue.clips.Length;
            int next = s_LastVariant[index] < 0 ? s_Rng.Next(count) : s_Rng.Next(Mathf.Max(1, count - 1));
            if (count > 1 && next >= s_LastVariant[index] && s_LastVariant[index] >= 0) next++;
            s_LastVariant[index] = next;
            return cue.clips[next];
        }

        static AudioSource NextVoice(AudioSource[] pool, ref int next)
        {
            // Usar primeiro uma voz livre. Se o pool encher, parar a voz antiga
            // antes de a mover: o som anterior nunca salta para a posição do novo.
            for (int i = 0; i < pool.Length; i++)
            {
                int index = (next + i) % pool.Length;
                if (pool[index].isPlaying) continue;
                next = (index + 1) % pool.Length;
                return pool[index];
            }
            var src = pool[next];
            next = (next + 1) % pool.Length;
            src.Stop();
            return src;
        }

        public static void Play2D(Sound sound, float volume = 1f)
        {
            if ((int)sound < 0 || sound >= Sound.Count || volume <= 0f) return;
            Init();
            var clip = SelectClip(sound, out float gain, out float pitch);
            if (clip == null) return;
            var src = NextVoice(s_2D, ref s_Next2D);
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * gain);
            src.pitch = pitch;
            src.Play();
        }

        public static void PlayAt(Sound sound, Vector3 position, float volume = 1f)
        {
            if ((int)sound < 0 || sound >= Sound.Count || volume <= 0f) return;
            Init();
            var clip = SelectClip(sound, out float gain, out float pitch);
            if (clip == null) return;
            var src = NextVoice(s_3D, ref s_Next3D);
            src.transform.position = position;
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * gain);
            src.pitch = pitch;
            src.Play();
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
