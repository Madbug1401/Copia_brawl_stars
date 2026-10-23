using UnityEngine;

namespace FPSLAN
{
    /// <summary>Team identity is relative to the viewer, independent of a character's skin.</summary>
    public sealed class TeamPresentation : MonoBehaviour
    {
        public static readonly Color Ally = new Color(0.286f, 0.69f, 0.714f);
        public static readonly Color Enemy = new Color(1f, 0.34f, 0.27f);
        PlayerNetwork m_Player;
        LineRenderer m_Ring;
        Material m_Material;

        void Awake()
        {
            m_Player = GetComponent<PlayerNetwork>();
            var go = new GameObject("Team marker");
            go.transform.SetParent(transform, false);
            m_Ring = go.AddComponent<LineRenderer>();
            m_Ring.useWorldSpace = false;
            m_Ring.loop = true;
            m_Ring.widthMultiplier = 0.065f;
            m_Ring.positionCount = 40;
            m_Ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Ring.receiveShadows = false;
            m_Material = new Material(Shader.Find("Sprites/Default"));
            m_Ring.sharedMaterial = m_Material;
            for (int i = 0; i < 40; i++)
            {
                float angle = i * Mathf.PI * 2f / 40;
                m_Ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.66f, 0.09f, Mathf.Sin(angle) * 0.66f));
            }
        }

        void LateUpdate()
        {
            var match = MatchManager.Instance;
            var viewer = PlayerNetwork.Local;
            bool visible = match != null && match.IsSpawned && MatchManager.IsTeamMode(match.Mode.Value) &&
                m_Player != null && m_Player.IsSpawned && viewer != null && viewer != m_Player &&
                !m_Player.Health.IsDead && (m_Player.Visuals == null || !m_Player.Visuals.IsConcealedForLocal);
            m_Ring.enabled = visible;
            if (!visible) return;
            var c = MatchManager.AreTeammates(viewer, m_Player) ? Ally : Enemy;
            m_Ring.startColor = m_Ring.endColor = c;
        }
        void OnDestroy() { if (m_Material != null) Destroy(m_Material); }
    }
}
