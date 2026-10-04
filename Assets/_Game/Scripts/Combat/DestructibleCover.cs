using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Cobertura destrutível. Só estes objetos do mapa são NetworkObjects (precisam de estado sincronizado);
    /// o resto da geometria é estática. A vida é decidida no host; ao chegar a 0 o colisor e o render desligam
    /// em todas as máquinas (e em quem entrar depois). É restaurada no início de cada partida.
    /// </summary>
    public class DestructibleCover : NetworkBehaviour
    {
        public int maxHealth = 150;
        public bool restoreOnMatchStart = true;

        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(-1);

        static readonly List<DestructibleCover> s_All = new List<DestructibleCover>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

        Renderer[] m_Renderers;
        Collider[] m_Colliders;
        Color[] m_BaseColors;
        MaterialPropertyBlock m_Block;

        public bool IsDestroyed => IsSpawned && Health.Value == 0;

        void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_Colliders = GetComponentsInChildren<Collider>(true);
            m_Block = new MaterialPropertyBlock();
            m_BaseColors = new Color[m_Renderers.Length];
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var m = m_Renderers[i].sharedMaterial;
                m_BaseColors[i] = m != null && m.HasProperty("_Color") ? m.color : Color.white;
            }
        }

        void OnEnable() { if (!s_All.Contains(this)) s_All.Add(this); }
        void OnDisable() => s_All.Remove(this);

        public override void OnNetworkSpawn()
        {
            if (IsServer && Health.Value < 0) Health.Value = maxHealth;
            Health.OnValueChanged += OnHealthChanged;
            Apply(false);
        }

        public override void OnNetworkDespawn()
        {
            Health.OnValueChanged -= OnHealthChanged;
            // Fora de partida a cobertura volta ao estado normal
            SetAlive(true);
            Tint(1f);
        }

        void OnHealthChanged(int previous, int current)
        {
            Apply(previous > 0 && current < previous);
        }

        void Apply(bool damaged)
        {
            int hp = Health.Value < 0 ? maxHealth : Health.Value;
            bool alive = hp > 0;
            bool wasAlive = m_Colliders.Length == 0 || m_Colliders[0].enabled;
            SetAlive(alive);
            Tint(Mathf.Clamp01(hp / (float)Mathf.Max(1, maxHealth)));

            var center = transform.position + Vector3.up * 0.5f;
            if (wasAlive && !alive) Sfx.PlayAt(Sound.Explosion, center, 1f);
            else if (damaged && alive) Sfx.PlayAt(Sound.Hit, center, 0.5f);
        }

        void SetAlive(bool alive)
        {
            foreach (var c in m_Colliders) if (c != null) c.enabled = alive;
            foreach (var r in m_Renderers) if (r != null) r.enabled = alive;
        }

        void Tint(float health01)
        {
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var r = m_Renderers[i];
                if (r == null) continue;
                var c = Color.Lerp(new Color(0.35f, 0.12f, 0.1f), m_BaseColors[i], 0.35f + 0.65f * health01);
                r.GetPropertyBlock(m_Block);
                m_Block.SetColor("_Color", c);
                m_Block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(m_Block);
            }
        }

        /// <summary>Host: aplicar dano à cobertura.</summary>
        public void ServerDamage(int amount)
        {
            if (!IsServer || !IsSpawned || amount <= 0 || Health.Value <= 0) return;
            Health.Value = Mathf.Max(0, Health.Value - amount);
        }

        public void ServerRestore()
        {
            if (IsServer && IsSpawned) Health.Value = maxHealth;
        }

        public static void ServerRestoreAll()
        {
            foreach (var c in s_All)
                if (c != null && c.restoreOnMatchStart) c.ServerRestore();
        }
    }
}
