using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Informação de um mapa usada em runtime: limites jogáveis (para saltos/lançamentos não saírem da arena),
    /// pontos de patrulha dos bots e o ambiente (sol, nevoeiro). Todos os mapas vivem na mesma cena, afastados
    /// uns dos outros; só o mapa ativo (escolhido pelo host) recebe spawns, bots e a câmara do menu.
    /// </summary>
    public class MapInfo : MonoBehaviour
    {
        static readonly List<MapInfo> s_All = new List<MapInfo>();
        static int s_ActiveIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_All.Clear();
            s_ActiveIndex = 0;
        }

        /// <summary>Mapa ativo (ou o primeiro, se o índice pedido não existir nesta cena).</summary>
        public static MapInfo Instance
        {
            get
            {
                foreach (var m in s_All)
                    if (m != null && m.mapIndex == s_ActiveIndex) return m;
                return s_All.Count > 0 ? s_All[0] : null;
            }
        }

        public static int Count => Mathf.Max(1, s_All.Count);

        public string mapName = "Arena";
        [Tooltip("Descrição curta mostrada no menu.")]
        public string tagline = "";
        public int mapIndex;
        [Tooltip("Centro da área jogável (cada mapa está deslocado na cena).")]
        public Vector3 center = Vector3.zero;
        [Tooltip("Meia largura da área jogável (quadrado centrado em 'center').")]
        public float halfSize = 22f;
        [Tooltip("Pontos livres por onde os bots circulam.")]
        public Vector3[] waypoints = new Vector3[0];

        [Header("Ambiente")]
        public Color sunColor = new Color(1f, 0.95f, 0.85f);
        public float sunIntensity = 1.2f;
        public Vector3 sunEuler = new Vector3(50f, -35f, 0f);
        public Color ambientSky = new Color(0.66f, 0.75f, 0.92f);
        public Color ambientEquator = new Color(0.58f, 0.64f, 0.66f);
        public Color ambientGround = new Color(0.36f, 0.4f, 0.3f);
        public Color fogColor = new Color(0.72f, 0.86f, 0.98f);

        void Awake() => s_All.Add(this);

        void Start()
        {
            if (Instance == this) ApplyLook();
        }

        void OnDestroy() => s_All.Remove(this);

        /// <summary>Muda o mapa ativo (host: ao começar a partida; menu: ao escolher; clientes: pelo MatchManager).</summary>
        public static void Activate(int index)
        {
            s_ActiveIndex = index;
            Instance?.ApplyLook();
        }

        /// <summary>Nome do mapa com este índice (para o menu).</summary>
        public static string NameOf(int index)
        {
            foreach (var m in s_All)
                if (m != null && m.mapIndex == index) return m.mapName;
            return Instance != null ? Instance.mapName : "Arena";
        }

        public static string TaglineOf(int index)
        {
            foreach (var m in s_All)
                if (m != null && m.mapIndex == index) return m.tagline;
            return "";
        }

        void ApplyLook()
        {
            var sun = RenderSettings.sun;
            if (sun != null)
            {
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.transform.rotation = Quaternion.Euler(sunEuler);
            }
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fogColor = fogColor;
        }

        public static Vector3 Center => Instance != null ? Instance.center : Vector3.zero;
        public static float HalfSize => Instance != null ? Instance.halfSize : 22f;

        /// <summary>Mantém um ponto dentro da área jogável (com margem).</summary>
        public static Vector3 ClampInside(Vector3 p, float margin = 1.2f)
        {
            var c = Center;
            float h = Mathf.Max(2f, HalfSize - margin);
            p.x = Mathf.Clamp(p.x, c.x - h, c.x + h);
            p.z = Mathf.Clamp(p.z, c.z - h, c.z + h);
            return p;
        }

        /// <summary>Um spawn/objeto está dentro deste mapa?</summary>
        public bool Contains(Vector3 p)
        {
            float h = halfSize + 4f;
            return Mathf.Abs(p.x - center.x) <= h && Mathf.Abs(p.z - center.z) <= h;
        }

        public static Vector3 RandomWaypoint()
        {
            var i = Instance;
            if (i != null && i.waypoints != null && i.waypoints.Length > 0)
                return i.waypoints[Random.Range(0, i.waypoints.Length)];
            var r = Random.insideUnitCircle * HalfSize * 0.75f;
            return Center + new Vector3(r.x, 0f, r.y);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(center + new Vector3(0f, 2f, 0f), new Vector3(halfSize * 2f, 4f, halfSize * 2f));
            if (waypoints == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            foreach (var w in waypoints) Gizmos.DrawSphere(w + Vector3.up * 0.3f, 0.3f);
        }
    }
}
