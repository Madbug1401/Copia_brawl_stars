using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Sprays SITA pintados nas paredes/chão (visual local em cada máquina, criado pelo RPC do host).
    /// Mancha de tinta na cor escolhida + logótipo SITA por cima. Limites: 3 por jogador e 30 no mapa; são limpos
    /// quando começa outra partida.
    /// </summary>
    public class SprayDecals : MonoBehaviour
    {
        public const int MaxPerPlayer = 3, MaxTotal = 30;
        public const float Size = 1.25f, PopDelay = 0.28f;

        struct Entry
        {
            public ulong owner;
            public Transform root;
            public float bornAt;
        }

        static SprayDecals s_Instance;
        readonly List<Entry> m_Entries = new List<Entry>();
        string m_MatchId = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Instance = null;

        static SprayDecals Instance
        {
            get
            {
                if (s_Instance != null) return s_Instance;
                s_Instance = new GameObject("[SitaSprays]").AddComponent<SprayDecals>();
                return s_Instance;
            }
        }

        /// <summary>Cria um spray. <paramref name="viewForward"/> orienta o logótipo quando se pinta o chão.</summary>
        public static void Spawn(ulong owner, Vector3 point, Vector3 normal, Vector3 viewForward, int colorIndex)
        {
            var inst = Instance;
            inst.CheckMatch();
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            normal.Normalize();

            // Logótipo direito: nas paredes o "cima" é o mundo; no chão/teto aponta para onde o jogador olhava
            var up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 0.04f) up = Vector3.ProjectOnPlane(viewForward, normal);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            up.Normalize();

            var root = new GameObject("SitaSpray").transform;
            root.SetParent(inst.transform, false);
            root.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(up, normal));
            float roll = Random.Range(-12f, 12f);
            root.Rotate(Vector3.up, roll, Space.Self);
            root.localScale = Vector3.zero;
            var color = Branding.SprayColorOf(colorIndex);
            Branding.Decal(root, Branding.SplatMaterial, Vector3.zero, Size, Size, Vector3.up, color);
            Branding.Decal(root, Branding.SitaMaterial, Vector3.up * 0.006f, Size * 0.86f, Size * 0.86f, Vector3.up, Color.white);

            inst.m_Entries.Add(new Entry { owner = owner, root = root, bornAt = Time.time });
            inst.Trim(owner);
        }

        void CheckMatch()
        {
            var match = MatchManager.Instance;
            string id = match != null && match.IsSpawned ? match.MatchId.Value.ToString() : "";
            if (id == m_MatchId) return;
            m_MatchId = id;
            Clear();
        }

        void Trim(ulong owner)
        {
            int mine = 0;
            for (int i = m_Entries.Count - 1; i >= 0; i--)
            {
                if (m_Entries[i].root == null) { m_Entries.RemoveAt(i); continue; }
                if (m_Entries[i].owner == owner && ++mine > MaxPerPlayer) RemoveAt(i);
            }
            while (m_Entries.Count > MaxTotal) RemoveAt(0);
        }

        void RemoveAt(int i)
        {
            var e = m_Entries[i];
            if (e.root != null)
            {
                Fx.Puff(e.root.position, Vector3.zero, Art.WithAlpha(Color.white, 0.4f), 0.6f, 0.4f);
                Destroy(e.root.gameObject);
            }
            m_Entries.RemoveAt(i);
        }

        void Clear()
        {
            foreach (var e in m_Entries) if (e.root != null) Destroy(e.root.gameObject);
            m_Entries.Clear();
        }

        void Update()
        {
            CheckMatch();
            // "Pop" de entrada depois do jato chegar à parede
            for (int i = 0; i < m_Entries.Count; i++)
            {
                var e = m_Entries[i];
                if (e.root == null) continue;
                float k = (Time.time - e.bornAt - PopDelay) / 0.18f;
                if (k > 1.5f) continue;
                float sc = k <= 0f ? 0f : k < 1f ? Mathf.Sin(k * Mathf.PI * 0.5f) * 1.08f : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((k - 1f) * 2f));
                e.root.localScale = Vector3.one * sc;
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
