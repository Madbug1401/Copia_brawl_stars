using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Música do menu e falas dos personagens (Resources/Audio). O volume geral e o mudo vêm do AudioListener
    /// (GameSession.ApplyAudioSettings), por isso estes sons respeitam as definições de áudio.
    ///  • Music/menu: toca em ciclo enquanto se está nos menus, desvanece ao entrar numa partida.
    ///  • Voices/&lt;personagem&gt;: fala ao escolher o personagem (menu e pausa).
    /// </summary>
    public static class MenuAudio
    {
        const string MusicPath = "Audio/Music/menu";
        const string VoicesPath = "Audio/Voices/";
        const float MusicVolume = 0.55f;
        const float VoiceVolume = 1f;
        const float MusicDuckWhileVoice = 0.45f;   // a música baixa enquanto o personagem fala

        static AudioSource s_Music, s_Voice;
        static Runner s_Runner;
        static bool s_WantMusic;

        /// <summary>brawlerId → ficheiro da fala (os nomes dos ficheiros seguem as chaves do menu).</summary>
        static string VoiceKeyFor(string brawlerId)
        {
            switch (brawlerId)
            {
                case "sniper": return "lince";
                case "gunslinger": return "riot";
                case "trapper": return "lupus";
                case "sprayer": return "mrs";
                case "shotgunner": return "ncage";
                case "artillery": return "ekko";
                default: return brawlerId;
            }
        }

        static void Init()
        {
            if (s_Runner != null) return;
            var go = new GameObject("[MenuAudio]");
            Object.DontDestroyOnLoad(go);
            s_Runner = go.AddComponent<Runner>();
            s_Music = go.AddComponent<AudioSource>();
            s_Music.loop = true;
            s_Music.playOnAwake = false;
            s_Music.spatialBlend = 0f;
            s_Music.volume = 0f;
            s_Music.priority = 0;
            s_Voice = go.AddComponent<AudioSource>();
            s_Voice.playOnAwake = false;
            s_Voice.spatialBlend = 0f;
            s_Voice.volume = VoiceVolume;
        }

        /// <summary>Chamado em cada frame pela UI: true nos menus, false em jogo.</summary>
        public static void SetMenuMusic(bool on)
        {
            if (on == s_WantMusic && s_Runner != null) return;
            Init();
            s_WantMusic = on;
            if (on && !s_Music.isPlaying)
            {
                if (s_Music.clip == null) s_Music.clip = Resources.Load<AudioClip>(MusicPath);
                if (s_Music.clip != null) s_Music.Play();
            }
        }

        public static void PlayVoice(string menuKey)
        {
            if (string.IsNullOrEmpty(menuKey)) return;
            Init();
            var clip = Resources.Load<AudioClip>(VoicesPath + menuKey);
            if (clip == null) return;   // personagem ainda sem fala (ex.: Lupus Marinus)
            s_Voice.Stop();
            s_Voice.clip = clip;
            s_Voice.Play();
        }

        public static void PlayVoiceForBrawler(string brawlerId) => PlayVoice(VoiceKeyFor(brawlerId));

        /// <summary>Desvanecimentos da música (entrar/sair das partidas e enquanto alguém fala).</summary>
        class Runner : MonoBehaviour
        {
            void Update()
            {
                if (s_Music == null) return;
                float target = s_WantMusic ? MusicVolume * (s_Voice != null && s_Voice.isPlaying ? MusicDuckWhileVoice : 1f) : 0f;
                s_Music.volume = Mathf.MoveTowards(s_Music.volume, target, Time.unscaledDeltaTime * (s_WantMusic ? 0.6f : 0.9f));
                if (!s_WantMusic && s_Music.isPlaying && s_Music.volume <= 0.001f) s_Music.Pause();
                else if (s_WantMusic && !s_Music.isPlaying && s_Music.clip != null) s_Music.UnPause();
            }
        }
    }
}
