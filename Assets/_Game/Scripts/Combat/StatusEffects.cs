using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Estados de combate de um jogador: abrandamento, imobilização, dano ao longo do tempo e knockback.
    /// Decididos no host. O knockback é enviado ao dono (que controla o movimento) já calculado e limitado,
    /// por isso o cliente nunca decide o resultado — só aplica o impulso que o host enviou.
    /// </summary>
    public class StatusEffects : NetworkBehaviour
    {
        public const float MaxKnockback = 22f;

        public readonly NetworkVariable<double> SlowUntil = new NetworkVariable<double>(0);
        public readonly NetworkVariable<float> SlowFactor = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<bool> HasDot = new NetworkVariable<bool>(false);
        /// <summary>Hora do servidor até à qual o jogador está imobilizado (armadilha).</summary>
        public readonly NetworkVariable<double> RootedUntil = new NetworkVariable<double>(0);

        struct Dot
        {
            public float dps;
            public float until;
            public ulong attackerId;
            public bool fromSuper;
            public float carry;
            /// <summary>Dano que falta aplicar (garante o total exato: dps × duração).</summary>
            public float remaining;
        }

        readonly List<Dot> m_Dots = new List<Dot>();
        readonly Dictionary<ulong, float> m_ZoneTickAt = new Dictionary<ulong, float>();
        PlayerNetwork m_Player;
        double m_RootImmuneUntil;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

        public bool IsSlowed => IsSpawned && NetworkManager != null && Now < SlowUntil.Value;
        public float SpeedMultiplier => IsSlowed ? Mathf.Clamp(SlowFactor.Value, 0.1f, 1f) : 1f;
        public bool IsRooted => IsSpawned && NetworkManager != null && Now < RootedUntil.Value;
        public float RootRemaining => IsRooted ? (float)(RootedUntil.Value - Now) : 0f;
        /// <summary>Servidor: pode ser imobilizado agora? (não está preso nem imune)</summary>
        public bool ServerCanBeRooted => IsServer && !m_Player.Health.IsDead && !IsRooted && Now >= m_RootImmuneUntil;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public void ServerApplySlow(float factor, float duration)
        {
            if (!IsServer || factor >= 1f || duration <= 0f || m_Player.Health.IsDead) return;
            double until = Now + duration;
            // Mantém o abrandamento mais forte ativo
            if (IsSlowed && SlowFactor.Value < factor && SlowUntil.Value > until) return;
            SlowUntil.Value = System.Math.Max(SlowUntil.Value, until);
            SlowFactor.Value = IsSlowed ? Mathf.Min(SlowFactor.Value, factor) : factor;
        }

        /// <summary>
        /// Imobiliza (não anda nem salta; pode olhar e atacar). Não prolonga uma imobilização ativa e, depois de acabar,
        /// o jogador fica imune durante 'immunityAfter' segundos — várias armadilhas não o prendem indefinidamente.
        /// </summary>
        public bool ServerApplyRoot(float duration, float immunityAfter)
        {
            if (!IsServer || duration <= 0f || !ServerCanBeRooted) return false;
            double now = Now;
            RootedUntil.Value = now + duration;
            m_RootImmuneUntil = now + duration + Mathf.Max(0f, immunityAfter);
            return true;
        }

        /// <summary>Zonas que não acumulam: devolve false se este atacante já aplicou um tick há menos de 'interval' s.</summary>
        public bool ServerTryZoneTick(ulong attackerId, float interval)
        {
            if (!IsServer) return false;
            float now = Time.time;
            if (m_ZoneTickAt.TryGetValue(attackerId, out float last) && now - last < Mathf.Max(0.05f, interval) * 0.9f) return false;
            m_ZoneTickAt[attackerId] = now;
            return true;
        }

        public void ServerApplyDot(float damagePerSecond, float duration, ulong attackerId, bool fromSuper)
        {
            if (!IsServer || damagePerSecond <= 0f || duration <= 0f || m_Player.Health.IsDead) return;
            float total = damagePerSecond * duration;
            // Mesmo atacante: renova em vez de acumular
            for (int i = 0; i < m_Dots.Count; i++)
            {
                if (m_Dots[i].attackerId != attackerId) continue;
                var d = m_Dots[i];
                d.dps = Mathf.Max(d.dps, damagePerSecond);
                d.until = Time.time + duration;
                d.remaining = Mathf.Max(d.remaining, total);
                m_Dots[i] = d;
                return;
            }
            m_Dots.Add(new Dot { dps = damagePerSecond, until = Time.time + duration, attackerId = attackerId, fromSuper = fromSuper, remaining = total });
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
            m_ZoneTickAt.Clear();
            HasDot.Value = false;
            SlowUntil.Value = 0;
            SlowFactor.Value = 1f;
            RootedUntil.Value = 0;
            m_RootImmuneUntil = 0;
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
                bool expired = now >= d.until;
                int whole = DotMath.Step(ref d.remaining, ref d.carry, d.dps, Time.deltaTime, expired);
                if (whole > 0) EffectResolver.DamageRaw(m_Player.Health, whole, d.attackerId, d.fromSuper, false);
                // o dano pode ter matado o jogador → ServerClear() já esvaziou a lista
                if (i >= m_Dots.Count) break;
                if (expired || DotMath.IsDone(d.remaining, d.carry) || m_Player.Health.IsDead) m_Dots.RemoveAt(i);
                else m_Dots[i] = d;
            }
            if (m_Dots.Count == 0) HasDot.Value = false;
        }
    }

    /// <summary>Contabilidade do dano ao longo do tempo: inteiros por frame e, no fim, o resto — total exato = dps × duração.</summary>
    public static class DotMath
    {
        /// <summary>Avança 'dt' segundos; devolve o dano inteiro a aplicar agora. No fim (expired) aplica o que faltar.</summary>
        public static int Step(ref float remaining, ref float carry, float dps, float dt, bool expired)
        {
            float step = expired ? remaining : Mathf.Min(remaining, dps * Mathf.Max(0f, dt));
            remaining -= step;
            carry += step;
            int whole = expired ? Mathf.RoundToInt(carry) : Mathf.FloorToInt(carry);
            if (whole > 0) carry -= whole;
            return Mathf.Max(0, whole);
        }

        public static bool IsDone(float remaining, float carry) => remaining <= 0.0001f && carry < 0.5f;
    }
}
