using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace FPSLAN
{
    /// <summary>
    /// Benchmark reprodutível de uma partida, só por linha de comando (sem o argumento não faz nada):
    ///   "FPS-LAN.exe" -benchmark fogo 5 60 [-benchmarkQuality 3] [-benchmarkRes 1920x1080] [-benchmarkSeed 12345]
    ///   [-benchmarkOverride antiAliasing=0,pixelLightCount=2,shadows=1,shadowDistance=40,shadowResolution=1,lodBias=1]
    ///   [-benchmarkOut C:\caminho\resultado.txt]
    /// Cria uma partida local no mapa (pradaria | santiago | sal | fogo) com N bots, aquece 10 s, mede durante S segundos
    /// (tempos de frame, percentis, alocações do GC, memória) e fecha o jogo. Serve para comparar versões e definições
    /// com as mesmas condições, num build de release, como o jogador o corre. O VSync fica desligado durante a medição
    /// (com VSync os tempos ficam presos a múltiplos de 16,7 ms e escondem o custo real); nada é gravado nas preferências.
    /// A vista é uma câmara própria que dá uma volta lenta ao centro do mapa: os spawns aleatórios (dentro das casas ou
    /// na praça) mudavam o que se via e faziam variar o resultado até 40% entre corridas iguais.
    /// </summary>
    public class BenchmarkRunner : MonoBehaviour
    {
        const float WarmupSeconds = 10f;

        string m_Map;
        int m_Bots;
        float m_Seconds;
        int m_Quality = -1;
        string m_Out;
        int m_Width, m_Height;
        int m_Seed = 12345;
        string m_Override = "";
        Camera m_Camera;
        Vector3 m_OrbitCenter;
        float m_OrbitRadius, m_OrbitStart;
        const float OrbitHeight = 7f;
        static Camera[] s_Cameras = new Camera[8];

        /// <summary>O jogo foi aberto em modo benchmark (nada deve ser gravado nas preferências).</summary>
        public static bool IsRequested => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-benchmark") >= 0;
        float m_LoadSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-benchmark");
            if (i < 0 || Application.isEditor) return;
            var go = new GameObject("BenchmarkRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<BenchmarkRunner>();
            // Posicionais opcionais: um token começado por '-' já é a opção seguinte, não o valor
            string Positional(int k) => i + k < args.Length && !args[i + k].StartsWith("-") ? args[i + k] : null;
            runner.m_Map = Positional(1) ?? "santiago";
            runner.m_Bots = int.TryParse(Positional(2), out int bots) ? Mathf.Clamp(bots, 0, 7) : 5;
            runner.m_Seconds = float.TryParse(Positional(3), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 5f, 600f) : 60f;
            int q = System.Array.IndexOf(args, "-benchmarkQuality");
            if (q >= 0 && q + 1 < args.Length && int.TryParse(args[q + 1], out int quality)) runner.m_Quality = quality;
            int r = System.Array.IndexOf(args, "-benchmarkRes");
            if (r >= 0 && r + 1 < args.Length)
            {
                var wh = args[r + 1].ToLowerInvariant().Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) { runner.m_Width = w; runner.m_Height = h; }
            }
            int sd = System.Array.IndexOf(args, "-benchmarkSeed");
            if (sd >= 0 && sd + 1 < args.Length && int.TryParse(args[sd + 1], out int seed)) runner.m_Seed = seed;
            int ov = System.Array.IndexOf(args, "-benchmarkOverride");
            if (ov >= 0 && ov + 1 < args.Length) runner.m_Override = args[ov + 1];
            int o = System.Array.IndexOf(args, "-benchmarkOut");
            runner.m_Out = o >= 0 && o + 1 < args.Length ? args[o + 1] : Path.Combine(Application.persistentDataPath, "benchmark.txt");
        }

        IEnumerator Start()
        {
            Debug.Log($"[Benchmark] mapa={m_Map} bots={m_Bots} duração={m_Seconds}s → {m_Out}");
            while (ConnectionManager.Instance == null) yield return null;
            yield return null;
            float loadStart = Time.realtimeSinceStartup;
            // Semente fixa: spawns, decisões dos bots e dispersão dos tiros repetem-se entre corridas, para que duas
            // versões sejam comparadas no mesmo cenário (sem isto a câmara aponta para sítios diferentes a cada corrida).
            Random.InitState(m_Seed);
            if (!ConnectionManager.Instance.HostMap(m_Map))
            {
                Finish("ERRO: não foi possível criar a partida no mapa '" + m_Map + "': " + ConnectionManager.Instance.Status);
                yield break;
            }

            float timeout = Time.realtimeSinceStartup + 60f;
            while (!(NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && PlayerNetwork.Local != null))
            {
                if (Time.realtimeSinceStartup > timeout) { Finish("ERRO: a partida não arrancou em 60 s."); yield break; }
                yield return null;
            }
            m_LoadSeconds = Time.realtimeSinceStartup - loadStart;
            Random.InitState(m_Seed);
            for (int b = 0; b < m_Bots; b++) { BotManager.AddBot(b % 6); yield return null; }
            if (m_Quality >= 0) QualitySettings.SetQualityLevel(Mathf.Clamp(m_Quality, 0, QualitySettings.names.Length - 1), true);
            ApplyOverrides();
            if (m_Width > 0 && m_Height > 0) Screen.SetResolution(m_Width, m_Height, FullScreenMode.Windowed);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            SetupOrbitCamera();
            yield return new WaitForSecondsRealtime(WarmupSeconds);
            m_OrbitStart = Time.realtimeSinceStartup;

            var frameMs = new List<float>(Mathf.CeilToInt(m_Seconds * 300f));
            long gcSum = 0, gcMax = 0;
            int gcFrames = 0;
            int gen0 = System.GC.CollectionCount(0);
            using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                yield return null;
                float end = Time.realtimeSinceStartup + m_Seconds;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    frameMs.Add(Time.unscaledDeltaTime * 1000f);
                    if (gc.Valid)
                    {
                        long a = gc.LastValue;
                        gcSum += a; gcFrames++;
                        if (a > gcMax) gcMax = a;
                    }
                }
            }
            gen0 = System.GC.CollectionCount(0) - gen0;
            Finish(Report(frameMs, gcSum, gcMax, gcFrames, gen0));
        }

        /// <summary>Muda uma definição de qualidade de cada vez, para medir quanto custa (nada é gravado).</summary>
        void ApplyOverrides()
        {
            foreach (var pair in m_Override.Split(','))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) continue;
                switch (kv[0].Trim())
                {
                    case "antiAliasing": QualitySettings.antiAliasing = (int)v; break;
                    case "pixelLightCount": QualitySettings.pixelLightCount = (int)v; break;
                    case "shadows": QualitySettings.shadows = (ShadowQuality)(int)v; break;
                    case "shadowDistance": QualitySettings.shadowDistance = v; break;
                    case "shadowResolution": QualitySettings.shadowResolution = (ShadowResolution)(int)v; break;
                    case "lodBias": QualitySettings.lodBias = v; break;
                    default: Debug.LogWarning("[Benchmark] definição desconhecida: " + kv[0]); break;
                }
            }
        }

        void SetupOrbitCamera()
        {
            // Centro e tamanho do mapa pelos renderers estáticos da cena
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in FindObjectsByType<MeshRenderer>())
            {
                if (!r.isPartOfStaticBatch && !r.gameObject.isStatic) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            m_OrbitCenter = any ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : Vector3.zero;
            m_OrbitRadius = any ? Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.45f, 8f, 40f) : 15f;

            var source = PlayerNetwork.Local != null && PlayerNetwork.Local.Visuals != null ? PlayerNetwork.Local.Visuals.PlayerCamera : null;
            var go = new GameObject("BenchmarkCamera");
            DontDestroyOnLoad(go);
            m_Camera = go.AddComponent<Camera>();
            if (source != null) m_Camera.CopyFrom(source);
            m_Camera.targetTexture = null;
            m_Camera.depth = 100f;
            go.tag = "MainCamera";   // LavaFx, InteriorLightCulling, etc. usam Camera.main: têm de seguir esta vista
            PoseOrbit(0f);
        }

        void PoseOrbit(float t01)
        {
            if (m_Camera == null) return;
            float a = t01 * Mathf.PI * 2f;
            m_Camera.transform.position = m_OrbitCenter + new Vector3(Mathf.Cos(a) * m_OrbitRadius, OrbitHeight, Mathf.Sin(a) * m_OrbitRadius);
            m_Camera.transform.LookAt(m_OrbitCenter + Vector3.up * 1.5f);
        }

        void LateUpdate()
        {
            if (m_Camera == null) return;
            // Só a câmara do benchmark desenha a cena (a do jogador e a das armas ficam desligadas).
            // Buffer reutilizado: Camera.allCameras alocaria um array por frame dentro da medição.
            if (s_Cameras.Length < Camera.allCamerasCount) s_Cameras = new Camera[Camera.allCamerasCount * 2];
            int count = Camera.GetAllCameras(s_Cameras);
            for (int c = 0; c < count; c++)
                if (s_Cameras[c] != m_Camera && s_Cameras[c].enabled) s_Cameras[c].enabled = false;
            float t = m_OrbitStart > 0f ? (Time.realtimeSinceStartup - m_OrbitStart) / Mathf.Max(1f, m_Seconds) : 0f;
            PoseOrbit(Mathf.Repeat(t, 1f));
        }

        string Report(List<float> frameMs, long gcSum, long gcMax, int gcFrames, int gen0)
        {
            var sorted = frameMs.ToArray();
            System.Array.Sort(sorted);
            double total = 0;
            int over16 = 0, over33 = 0;
            foreach (var ms in frameMs) { total += ms; if (ms > 16.67f) over16++; if (ms > 33.34f) over33++; }
            int n = Mathf.Max(1, sorted.Length);
            float P(double p) => sorted.Length == 0 ? 0f : sorted[Mathf.Min(sorted.Length - 1, (int)(p * sorted.Length))];

            var sb = new StringBuilder();
            sb.AppendLine("CHIEFS & GEMS — benchmark");
            sb.AppendLine($"versão={Application.version} semente={m_Seed} mapa={m_Map} cena={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} jogadores={PlayerNetwork.All.Count}");
            sb.AppendLine($"qualidade={QualitySettings.names[QualitySettings.GetQualityLevel()]}{(m_Override.Length > 0 ? " [" + m_Override + "]" : "")} " +
                          $"aa={QualitySettings.antiAliasing} luzes={QualitySettings.pixelLightCount} sombras={QualitySettings.shadows}/{QualitySettings.shadowResolution}/{QualitySettings.shadowDistance}m resolução={Screen.width}x{Screen.height} vsync={QualitySettings.vSyncCount} alvoFps={Application.targetFrameRate}");
            sb.AppendLine($"cpu={SystemInfo.processorType} ({SystemInfo.processorCount} threads) gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType} ram={SystemInfo.systemMemorySize}MB");
            sb.AppendLine($"vista=órbita centro=({m_OrbitCenter.x:F0},{m_OrbitCenter.z:F0}) raio={m_OrbitRadius:F0}m altura={OrbitHeight}m");
            sb.AppendLine($"carregamento_do_mapa={m_LoadSeconds:F2}s (do pedido de partida até o jogador local existir)");
            sb.AppendLine($"frames={sorted.Length} duração={total / 1000.0:F1}s fps_médio={sorted.Length / (total / 1000.0):F1}");
            sb.AppendLine($"frame_ms médio={total / n:F2} p50={P(0.50):F2} p95={P(0.95):F2} p99={P(0.99):F2} máx={P(1.0):F2}");
            sb.AppendLine($"frames>16,7ms={100.0 * over16 / n:F1}% frames>33,3ms={100.0 * over33 / n:F1}%");
            // Alguns contadores só existem em builds de desenvolvimento: a zero aparecem como indisponíveis, não como 0.
            sb.AppendLine(gcFrames > 0 && gcMax > 0
                ? $"gc_bytes_por_frame médio={gcSum / (double)gcFrames:F0} máx={gcMax} recolhas_gen0={gen0}"
                : $"gc_bytes_por_frame=indisponível neste build recolhas_gen0={gen0}");
            sb.AppendLine($"memória: managed_heap={Mb(Profiler.GetMonoUsedSizeLong())} total_alocada={Mb(Profiler.GetTotalAllocatedMemoryLong())} " +
                          $"gráfica={Mb(Profiler.GetAllocatedMemoryForGraphicsDriver())}");
            return sb.ToString();
        }

        static string Mb(long bytes) => bytes > 0 ? (bytes / 1048576.0).ToString("F0") + "MB" : "indisponível";

        void Finish(string report)
        {
            Debug.Log("[Benchmark]\n" + report);
            try
            {
                var dir = Path.GetDirectoryName(m_Out);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(m_Out, report);
            }
            catch (System.Exception e) { Debug.LogError("[Benchmark] não foi possível escrever " + m_Out + ": " + e.Message); }
            Application.Quit(report.StartsWith("ERRO") ? 1 : 0);
        }
    }
}
