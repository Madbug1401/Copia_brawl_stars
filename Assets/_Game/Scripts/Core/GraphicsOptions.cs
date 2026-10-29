using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Opções gráficas do jogador (DEFINIÇÕES → GRAPHICS): nível de qualidade, resolução, ecrã inteiro/janela
    /// e contador de FPS. Ficam guardadas neste PC e aplicam-se ao arrancar o jogo.
    /// </summary>
    public static class GraphicsOptions
    {
        const string KeyQuality = "fpslan.gfx.quality";
        const string KeyWidth = "fpslan.gfx.width";
        const string KeyHeight = "fpslan.gfx.height";
        const string KeyWindowed = "fpslan.gfx.windowed";
        const string KeyShowFps = "fpslan.gfx.showfps";
        const string KeyShowPing = "fpslan.hud.showping";

        /// <summary>Resoluções abaixo disto ficam ilegíveis com a interface atual.</summary>
        const int MinWidth = 640, MinHeight = 360;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyOnStartup()
        {
            if (PlayerPrefs.HasKey(KeyQuality))
                QualitySettings.SetQualityLevel(ClampQuality(PlayerPrefs.GetInt(KeyQuality)), true);
            else if (!Application.isEditor && !BenchmarkRunner.IsRequested)   // o benchmark não grava preferências
            {
                // Primeiro arranque: em vez de Ultra (13 FPS num portátil com gráfica integrada), um nível adequado
                // ao hardware. Fica guardado como escolha e o jogador muda-o em SETTINGS → GRAPHICS.
                Quality = RecommendedQuality(SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize,
                    SystemInfo.systemMemorySize, QualitySettings.names);
            }
            ApplyDisplay();
        }

        /// <summary>
        /// Nível inicial pelo hardware. Gráficas integradas (Intel HD/UHD/Iris, AMD "Radeon Graphics"/Vega das APUs)
        /// ficam em Low ou Medium; dedicadas em Medium, High ou Very High consoante a memória gráfica. Com pouca RAM
        /// nunca passa de Medium. Medido num i7-4600U + Intel HD 4400, 1280×720: Ultra 13 FPS, High 23, Low 35.
        /// </summary>
        public static int RecommendedQuality(string gpuName, int vramMb, int ramMb, string[] levels)
        {
            string gpu = (gpuName ?? "").ToLowerInvariant();
            // Intel Arc e Radeon RX (incluindo RX Vega 56/64) são placas dedicadas
            bool dedicatedName = gpu.Contains(" arc") || gpu.Contains(" rx");
            bool integrated = !dedicatedName && (gpu.Contains("intel") || gpu.Contains("vega") ||
                              (gpu.Contains("radeon") && gpu.Contains("graphics"))) ||
                              vramMb > 0 && vramMb < 1024;
            string level;
            if (integrated) level = vramMb >= 2048 && ramMb >= 12000 ? "Medium" : "Low";
            else if (vramMb >= 6144) level = "Very High";
            else if (vramMb >= 3072) level = "High";
            else level = "Medium";
            if (ramMb > 0 && ramMb < 6000 && (level == "High" || level == "Very High")) level = "Medium";
            int index = levels != null ? System.Array.IndexOf(levels, level) : -1;
            // Nomes diferentes (níveis renomeados): cai para o meio da lista em vez do máximo.
            return index >= 0 ? index : Mathf.Max(0, (levels != null ? levels.Length : 1) / 2 - 1);
        }

        // ------------------------------------------------------------------ qualidade

        public static string[] QualityNames => QualitySettings.names;

        public static int Quality
        {
            get => QualitySettings.GetQualityLevel();
            set
            {
                value = ClampQuality(value);
                QualitySettings.SetQualityLevel(value, true);
                PlayerPrefs.SetInt(KeyQuality, value);
                PlayerPrefs.Save();
            }
        }

        static int ClampQuality(int i) => Mathf.Clamp(i, 0, Mathf.Max(0, QualitySettings.names.Length - 1));

        // ------------------------------------------------------------------ resolução e modo de ecrã

        public static bool Windowed => PlayerPrefs.GetInt(KeyWindowed, 0) == 1;

        /// <summary>Resolução do monitor (a que o jogo usa por defeito).</summary>
        public static Vector2Int Native
        {
            get
            {
                var d = Display.main;
                int w = d != null && d.systemWidth > 0 ? d.systemWidth : Screen.currentResolution.width;
                int h = d != null && d.systemHeight > 0 ? d.systemHeight : Screen.currentResolution.height;
                return new Vector2Int(Mathf.Max(MinWidth, w), Mathf.Max(MinHeight, h));
            }
        }

        public static bool HasSavedResolution => PlayerPrefs.HasKey(KeyWidth) && PlayerPrefs.HasKey(KeyHeight);

        public static Vector2Int SavedResolution => HasSavedResolution
            ? new Vector2Int(PlayerPrefs.GetInt(KeyWidth), PlayerPrefs.GetInt(KeyHeight))
            : Native;

        /// <summary>Resoluções disponíveis (sem repetir taxas de atualização), da menor para a maior.</summary>
        public static List<Vector2Int> AvailableResolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= MinWidth && v.y >= MinHeight && !list.Contains(v)) list.Add(v);
            }
            // Algumas placas (ou o Editor) devolvem poucas opções: junta tamanhos 16:9 comuns até à nativa.
            var native = Native;
            Vector2Int[] common = { new Vector2Int(854, 480), new Vector2Int(1024, 576), new Vector2Int(1280, 720), new Vector2Int(1366, 768), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) };
            foreach (var c in common)
                if (c.x <= native.x && c.y <= native.y && !list.Contains(c)) list.Add(c);
            if (!list.Contains(native)) list.Add(native);
            var saved = SavedResolution;
            if (!list.Contains(saved)) list.Add(saved);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static void SetDisplay(Vector2Int resolution, bool windowed)
        {
            PlayerPrefs.SetInt(KeyWidth, Mathf.Max(MinWidth, resolution.x));
            PlayerPrefs.SetInt(KeyHeight, Mathf.Max(MinHeight, resolution.y));
            PlayerPrefs.SetInt(KeyWindowed, windowed ? 1 : 0);
            PlayerPrefs.Save();
            ApplyDisplay();
        }

        /// <summary>
        /// Ecrã inteiro em janela sem bordas (Alt+Tab rápido), a desenhar na resolução escolhida e esticado
        /// para o monitor: baixar a resolução dá FPS em PCs fracos. No Editor não mexe na janela.
        /// </summary>
        public static void ApplyDisplay()
        {
            if (Application.isEditor) return;
            // Linha de comando ("-screen-fullscreen 0", testes) manda, se não houver escolha guardada
            if (!HasSavedResolution && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-screen-fullscreen") >= 0) return;
            var r = SavedResolution;
            Screen.SetResolution(r.x, r.y, Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
        }

        // ------------------------------------------------------------------ FPS

        /// <summary>Mostrar o ping (ou HOST) no canto do HUD durante o jogo. Ligado por defeito.</summary>
        public static bool ShowPing
        {
            get => PlayerPrefs.GetInt(KeyShowPing, 1) == 1;
            set { PlayerPrefs.SetInt(KeyShowPing, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(KeyShowFps, 0) == 1;
            set { PlayerPrefs.SetInt(KeyShowFps, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static float s_Fps = 60f;
        static int s_FpsFrame = -1;

        /// <summary>FPS suavizado (atualiza uma vez por frame, mesmo chamado várias vezes no OnGUI).</summary>
        public static float Fps
        {
            get
            {
                if (s_FpsFrame != Time.frameCount)
                {
                    s_FpsFrame = Time.frameCount;
                    float now = 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime);
                    s_Fps = Mathf.Lerp(s_Fps, now, 0.05f);
                }
                return s_Fps;
            }
        }
    }
}
