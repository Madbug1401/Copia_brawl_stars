using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Estados de combate de um jogador: abrandamento, dano ao longo do tempo e knockback.
    /// Decididos no host. O knockback é enviado ao dono (que controla o movimento) já calculado e limitado,
    /// por isso o cliente nunca decide o resultado — só aplica o impulso que o host enviou.
    /// </summary>
    public class StatusEffects : NetworkBehaviour
    {
        public const float MaxKnockback = 22f;

        public readonly NetworkVariable<double> SlowUntil = new NetworkVariable<double>(0);
        public readonly NetworkVariable<float> SlowFactor = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<bool> HasDot = new NetworkVariable<bool>(false);

        struct Dot
        {
            public float dps;
            public float until;
            public ulong attackerId;
            public bool fromSuper;
            public float carry;
        }

        readonly List<Dot> m_Dots = new List<Dot>();
        PlayerNetwork m_Player;

        public bool IsSlowed => IsSpawned && NetworkManager != null && NetworkManager.ServerTime.Time < SlowUntil.Value;
        public float SpeedMultiplier => IsSlowed ? Mathf.Clamp(SlowFactor.Value, 0.1f, 1f) : 1f;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public void ServerApplySlow(float factor, float duration)
        {
            if (!IsServer || factor >= 1f || duration <= 0f || m_Player.Health.IsDead) return;
            double until = NetworkManager.ServerTime.Time + duration;
            // Mantém o abrandamento mais forte ativo
            if (IsSlowed && SlowFactor.Value < factor && SlowUntil.Value > until) return;
            SlowUntil.Value = System.Math.Max(SlowUntil.Value, until);
            SlowFactor.Value = IsSlowed ? Mathf.Min(SlowFactor.Value, factor) : factor;
        }

        public void ServerApplyDot(float damagePerSecond, float duration, ulong attackerId, bool fromSuper)
        {
            if (!IsServer || damagePerSecond <= 0f || duration <= 0f || m_Player.Health.IsDead) return;
            // Mesmo atacante: renova em vez de acumular
            for (int i = 0; i < m_Dots.Count; i++)
            {
                if (m_Dots[i].attackerId != attackerId) continue;
                var d = m_Dots[i];
                d.dps = Mathf.Max(d.dps, damagePerSecond);
                d.until = Time.time + duration;
                m_Dots[i] = d;
                return;
            }
            m_Dots.Add(new Dot { dps = damagePerSecond, until = Time.time + duration, attackerId = attackerId, fromSuper = fromSuper });
            HasDot.Value = true;
        }

        public void ServerApplyKnockback(Vector3 impulse)
        {
            if (!IsServer || m_Player.Health.IsDead) return;
            impulse = Vector3.ClampMagnitude(impulse, MaxKnockback);
            KnockbackRpc(impulse);
        }

        [Rpc(SendTo.Owner)]
        void KnockbackRpc(Vector3 impulse)
        {
            m_Player.Motor.ApplyImpulse(Vector3.ClampMagnitude(impulse, MaxKnockback));
        }

        public void ServerClear()
        {
            if (!IsServer) return;
            m_Dots.Clear();
            HasDot.Value = false;
            SlowUntil.Value = 0;
            SlowFactor.Value = 1f;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || m_Dots.Count == 0) return;
            if (m_Player.Health.IsDead)
            {
                ServerClear();
                return;
            }

            float now = Time.time;
            for (int i = m_Dots.Count - 1; i >= 0; i--)
            {
                var d = m_Dots[i];
                d.carry += d.dps * Time.deltaTime;
                int whole = Mathf.FloorToInt(d.carry);
                if (whole > 0)
                {
                    d.carry -= whole;
                    EffectResolver.DamageRaw(m_Player.Health, whole, d.attackerId, d.fromSuper, false);
                }
                if (now >= d.until || m_Player.Health.IsDead) m_Dots.RemoveAt(i);
                else m_Dots[i] = d;
            }
            if (m_Dots.Count == 0) HasDot.Value = false;
        }
    }
}
