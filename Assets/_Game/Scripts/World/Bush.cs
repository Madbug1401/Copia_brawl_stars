using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Arbusto: atravessa-se a andar e os tiros passam por ele, mas quem está lá dentro fica invisível para
    /// os adversários que estejam longe — até atacar ou levar dano (fica revelado uns instantes).
    /// O arbusto em que o jogador local está fica translúcido para ele.
    /// </summary>
    public class Bush : MonoBehaviour
    {
        /// <summary>Distância a que um adversário vê quem está num arbusto.</summary>
        public const float RevealDistance = 3.4f;

        static readonly List<Bush> s_All = new List<Bush>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        [Tooltip("Meia largura em X e Z (retângulo, no espaço local do arbusto).")]
        public Vector2 halfExtents = new Vector2(1.5f, 1.5f);
        public float height = 1.6f;

        Renderer[] m_Renderers;
        float m_Fade = 1f, m_Applied = 1f;

        void Awake() => m_Renderers = GetComponentsInChildren<Renderer>(true);
        void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        void OnDisable() => s_All.Remove(this);

        public bool Contains(Vector3 feet)
        {
            var l = transform.InverseTransformPoint(feet);
            var s = transform.lossyScale;
            // InverseTransformPoint divide pela escala; voltamos a metros
            float x = l.x * s.x, z = l.z * s.z, y = l.y * s.y;
            return Mathf.Abs(x) <= halfExtents.x && Mathf.Abs(z) <= halfExtents.y && y > -0.6f && y < height;
        }

        public static Bush Find(Vector3 feet)
        {
            for (int i = 0; i < s_All.Count; i++)
                if (s_All[i] != null && s_All[i].Contains(feet)) return s_All[i];
            return null;
        }

        /// <summary>O alvo está escondido para um observador nesta posição?</summary>
        public static bool Conceals(PlayerNetwork target, Vector3 observerFeet)
        {
            if (target == null || !target.IsSpawned) return false;
            var vis = target.Visuals;
            if (vis != null && Time.time < vis.RevealedUntil) return false;
            var feet = target.transform.position;
            var bush = Find(feet);
            if (bush == null) return false;
            return (observerFeet - feet).sqrMagnitude > RevealDistance * RevealDistance;
        }

        void Update()
        {
            // Translúcido quando o jogador local está dentro ou encostado
            var me = PlayerNetwork.Local;
            float target = 1f;
            if (me != null && me.IsSpawned && !me.Health.IsDead)
            {
                var l = transform.InverseTransformPoint(me.transform.position);
                var s = transform.lossyScale;
                float dx = Mathf.Abs(l.x * s.x) - halfExtents.x, dz = Mathf.Abs(l.z * s.z) - halfExtents.y;
                if (dx < 0.8f && dz < 0.8f) target = 0.35f;
            }
            // luneta térmica: as folhas quase não tapam (vê-se o calor de quem está lá dentro)
            if (ThermalScope.Active) target = 0.2f;
            m_Fade = Mathf.MoveTowards(m_Fade, target, Time.deltaTime * (ThermalScope.Active ? 12f : 3f));
            if (Mathf.Abs(m_Fade - m_Applied) > 0.01f)
            {
                m_Applied = m_Fade;
                foreach (var r in m_Renderers) Art.SetFloat(r, Art.FadeId, m_Fade);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(halfExtents.x * 2f, height, halfExtents.y * 2f));
        }
    }
}
