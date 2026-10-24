using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Informação do mapa usada em runtime: limites jogáveis (para saltos/lançamentos não saírem da arena)
    /// e pontos de patrulha dos bots. Colocado na cena pelo gerador do mapa.
    /// </summary>
    public class MapInfo : MonoBehaviour
    {
        public static MapInfo Instance { get; private set; }

        public string mapName = "Arena";
        [Tooltip("Meia largura da área jogável (quadrado centrado na origem).")]
        public float halfSize = 22f;
        [Tooltip("Abaixo desta altura (m) um jogador é levado para um ponto de spawn (ex.: caiu num buraco).")]
        public float fallRecoveryY = -20f;
        [Tooltip("Pontos livres por onde os bots circulam.")]
        public Vector3[] waypoints = new Vector3[0];
        [Tooltip("Zonas jogáveis fora do quadrado (ex.: interiores de edifícios). Só contam X e Z.")]
        public Bounds[] extraAreas = new Bounds[0];

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static float HalfSize => Instance != null ? Instance.halfSize : 22f;

        /// <summary>Altura abaixo da qual o PlayerMotor recupera o jogador para um spawn (por defeito -20 m).</summary>
        public static float FallRecoveryY => Instance != null ? Instance.fallRecoveryY : -20f;

        /// <summary>Mantém um ponto dentro da área jogável (com margem).</summary>
        public static Vector3 ClampInside(Vector3 p, float margin = 1.2f)
        {
            var mi = Instance;
            if (mi != null && mi.extraAreas != null)
                foreach (var b in mi.extraAreas)
                    if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z) return p;
            float h = Mathf.Max(2f, HalfSize - margin);
            p.x = Mathf.Clamp(p.x, -h, h);
            p.z = Mathf.Clamp(p.z, -h, h);
            return p;
        }

        public static Vector3 RandomWaypoint()
        {
            var i = Instance;
            if (i != null && i.waypoints != null && i.waypoints.Length > 0)
                return i.waypoints[Random.Range(0, i.waypoints.Length)];
            var r = Random.insideUnitCircle * HalfSize * 0.75f;
            return new Vector3(r.x, 0f, r.y);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, 2f, 0f), new Vector3(halfSize * 2f, 4f, halfSize * 2f));
            if (waypoints == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
            foreach (var w in waypoints) Gizmos.DrawSphere(w + Vector3.up * 0.3f, 0.3f);
        }
    }
}
