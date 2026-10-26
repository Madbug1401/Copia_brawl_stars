using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Luneta térmica da Lince (só visual, no PC de quem mira). A mirar, o botão do meio do rato (a roda) liga/desliga
    /// o modo térmico. Ligado: a câmara desenha tudo com o shader Hidden/FPSLAN/Thermal (cenário frio, jogadores quentes),
    /// os arbustos ficam quase transparentes e os jogadores escondidos neles passam a ver-se. A escolha fica guardada.
    /// </summary>
    public static class ThermalScope
    {
        const string Key = "fpslan.aim.thermal";
        public static readonly int ThermalId = Shader.PropertyToID("_Thermal");
        static readonly Color Background = new Color(0.01f, 0.015f, 0.05f);

        static Shader s_Shader;
        static bool s_ShaderLoaded;
        static Camera s_Cam;
        static CameraClearFlags s_Flags;
        static Color s_Bg;

        /// <summary>Modo térmico escolhido (fica guardado entre partidas).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(Key, 0) == 1;
            set => PlayerPrefs.SetInt(Key, value ? 1 : 0);
        }

        /// <summary>A câmara local está agora em visão térmica (a mirar com o modo ligado).</summary>
        public static bool Active { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            s_Cam = null;
        }

        static Shader ThermalShader
        {
            get
            {
                if (!s_ShaderLoaded)
                {
                    s_ShaderLoaded = true;
                    s_Shader = Resources.Load<Shader>("Shaders/FPSLAN_Thermal");
                    if (s_Shader == null) s_Shader = Shader.Find("Hidden/FPSLAN/Thermal");
                    if (s_Shader != null && !s_Shader.isSupported) s_Shader = null;
                }
                return s_Shader;
            }
        }

        /// <summary>Chamado todos os frames pela câmara do jogador local: liga/desliga a visão térmica.</summary>
        public static void Tick(Camera cam, bool scoped)
        {
            bool want = scoped && Enabled && cam != null && ThermalShader != null;
            if (want == Active && cam == s_Cam) return;
            Release();
            if (!want) return;
            s_Cam = cam;
            s_Flags = cam.clearFlags;
            s_Bg = cam.backgroundColor;
            cam.SetReplacementShader(ThermalShader, "RenderType");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            Active = true;
        }

        /// <summary>Volta a câmara ao normal.</summary>
        public static void Release()
        {
            if (Active && s_Cam != null)
            {
                s_Cam.ResetReplacementShader();
                s_Cam.clearFlags = s_Flags;
                s_Cam.backgroundColor = s_Bg;
            }
            Active = false;
            s_Cam = null;
        }
    }
}
