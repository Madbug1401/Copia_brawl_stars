using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Plataforma de salto: quem a pisa é lançado em arco até ao destino. O lançamento é movimento do dono
    /// (PlayerMotor), por isso não precisa de rede; os outros veem o salto pela posição sincronizada.
    /// </summary>
    public class JumpPad : MonoBehaviour
    {
        static readonly List<JumpPad> s_All = new List<JumpPad>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        public Vector3 target;
        public float flightTime = 1.2f;
        public float radius = 1.1f;
        public Color color = new Color(0.3f, 0.95f, 1f);

        Transform m_Arrow;
        readonly HashSet<ulong> m_Occupants = new HashSet<ulong>();
        readonly List<ulong> m_Seen = new List<ulong>();
        float m_Pulse;

        void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        void OnDisable() => s_All.Remove(this);

        void Start()
        {
            var t = transform.Find("Arrow");
            m_Arrow = t;
        }

        /// <summary>Plataforma sob estes pés (ou null).</summary>
        public static JumpPad Find(Vector3 feet)
        {
            for (int i = 0; i < s_All.Count; i++)
            {
                var p = s_All[i];
                if (p == null) continue;
                var d = feet - p.transform.position;
                if (d.y < -0.3f || d.y > 0.6f) continue;
                d.y = 0f;
                if (d.sqrMagnitude <= p.radius * p.radius) return p;
            }
            return null;
        }

        void Update()
        {
            m_Pulse += Time.deltaTime;
            if (m_Arrow != null)
                m_Arrow.localPosition = new Vector3(0f, 0.18f + Mathf.Abs(Mathf.Sin(m_Pulse * 3f)) * 0.12f, 0f);

            // Efeito para toda a gente quando alguém pisa (detetado localmente pelas posições)
            m_Seen.Clear();
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                var d = p.transform.position - transform.position;
                if (d.y < -0.3f || d.y > 0.8f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                m_Seen.Add(p.Id);
                if (!m_Occupants.Contains(p.Id))
                {
                    Sfx.PlayAt(Sound.Launch, transform.position, 1f);
                    Fx.Ring(transform.position, color, radius * 3f, 0.4f);
                    for (int i = 0; i < 8; i++)
                        Fx.Glow(transform.position + Random.insideUnitSphere * 0.5f, Vector3.up * Random.Range(4f, 8f), color, 0.18f, 0.5f);
                }
            }
            m_Occupants.Clear();
            foreach (var id in m_Seen) m_Occupants.Add(id);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, radius);
            // Pré-visualização do arco (gravidade do PlayerMotor = 24)
            var a = transform.position;
            var delta = target - a;
            float T = Mathf.Max(0.2f, flightTime);
            var v = new Vector3(delta.x / T, (delta.y + 0.5f * 24f * T * T) / T, delta.z / T);
            var prev = a;
            for (int i = 1; i <= 20; i++)
            {
                float t = T * i / 20f;
                var p = a + v * t + 0.5f * Vector3.down * 24f * t * t;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
