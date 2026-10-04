using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Vida, morte, respawn e abrandamento. Tudo decidido no servidor (host); os clientes só leem.
    /// </summary>
    public class PlayerHealth : NetworkBehaviour
    {
        public float respawnDelay = 3f;

        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>(false);
        /// <summary>NetworkObjectId de quem matou por último.</summary>
        public readonly NetworkVariable<ulong> LastKiller = new NetworkVariable<ulong>(ulong.MaxValue);

        PlayerNetwork m_Player;
        float m_LastCombatTime;
        float m_RegenCarry;
        float m_RespawnAt;
        bool m_NeedsInitialSpawn;
        float m_ClientRespawnAt;

        public bool IsDead => Dead.Value;
        public int MaxHealth => m_Player.Brawler != null ? m_Player.Brawler.stats.maxHealth : 100;
        public float RespawnRemaining => Mathf.Max(0f, m_ClientRespawnAt - Time.time);

        // Abrandamento vive agora no StatusEffects; mantido aqui por compatibilidade.
        public bool IsSlowed => m_Player.Status != null && m_Player.Status.IsSlowed;
        public float SpeedMultiplier => m_Player.Status != null ? m_Player.Status.SpeedMultiplier : 1f;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            Dead.OnValueChanged += OnDeadChanged;
            Health.OnValueChanged += OnHealthChanged;
            m_Player.HeroId.OnValueChanged += OnHeroChanged;

            if (IsServer)
            {
                Health.Value = MaxHealth;
                Dead.Value = false;
                m_NeedsInitialSpawn = true;
            }

            m_Player.Visuals?.SetDead(Dead.Value, true);
        }

        public override void OnNetworkDespawn()
        {
            Dead.OnValueChanged -= OnDeadChanged;
            Health.OnValueChanged -= OnHealthChanged;
            m_Player.HeroId.OnValueChanged -= OnHeroChanged;
        }

        void OnHeroChanged(int previous, int current)
        {
            if (IsServer && !Dead.Value)
            {
                Health.Value = MaxHealth;
                m_Player.Combat.ServerRefill();
                m_Player.Combat.ServerResetSuper();
            }
        }

        void OnDeadChanged(bool previous, bool current)
        {
            m_Player.Visuals?.SetDead(current, false);
            if (current)
            {
                m_ClientRespawnAt = Time.time + respawnDelay;
                Sfx.PlayAt(Sound.Death, transform.position + Vector3.up, 1f);
            }
        }

        void OnHealthChanged(int previous, int current)
        {
            if (current < previous && m_Player.IsLocalHuman && !Dead.Value)
            {
                Sfx.Play2D(Sound.Hurt, 0.8f);
                GameUI.FlashDamage();
            }
            if (current < previous && m_Player.Bot != null) m_Player.Bot.OnDamaged();
        }

        /// <summary>Servidor: aplica dano. Devolve true se matou.</summary>
        public bool ServerApplyDamage(int amount, ulong attackerId)
        {
            if (!IsServer || Dead.Value || amount <= 0) return false;
            if (MatchManager.IsMatchOver) return false;

            m_LastCombatTime = Time.time;
            Health.Value = Mathf.Max(0, Health.Value - amount);

            if (Health.Value > 0) return false;

            ServerDie(attackerId);
            return true;
        }

        /// <summary>Compatibilidade: delega no StatusEffects.</summary>
        public void ServerApplySlow(float factor, float duration)
        {
            if (m_Player.Status != null) m_Player.Status.ServerApplySlow(factor, duration);
        }

        public void ServerNotifyAttacked() => m_LastCombatTime = Time.time;

        void ServerDie(ulong attackerId)
        {
            Dead.Value = true;
            LastKiller.Value = attackerId;
            m_Player.Status?.ServerClear();
            m_RespawnAt = Time.time + respawnDelay;

            m_Player.Deaths.Value++;
            var killer = PlayerNetwork.Find(attackerId);
            if (killer != null && killer != m_Player) killer.Kills.Value++;

            if (MatchManager.Instance != null) MatchManager.Instance.ServerOnKill(killer, m_Player);
        }

        public void ServerRespawn()
        {
            if (!IsServer) return;

            SpawnPoint.Pick(m_Player, out var pos, out var yaw);
            m_RegenCarry = 0f;
            Health.Value = MaxHealth;
            m_Player.Status?.ServerClear();
            m_Player.Motor.TeleportRpc(pos, yaw);
            m_Player.Combat.ServerRefill();
            Dead.Value = false;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (m_NeedsInitialSpawn)
            {
                m_NeedsInitialSpawn = false;
                ServerRespawn();
                return;
            }

            if (Dead.Value)
            {
                if (Time.time >= m_RespawnAt && !MatchManager.IsMatchOver) ServerRespawn();
                return;
            }

            var stats = m_Player.Brawler.stats;
            if (Health.Value < stats.maxHealth && Time.time - m_LastCombatTime > stats.regenDelay)
            {
                m_RegenCarry += stats.maxHealth * stats.regenPercentPerSecond * Time.deltaTime;
                int add = Mathf.FloorToInt(m_RegenCarry);
                if (add > 0)
                {
                    m_RegenCarry -= add;
                    Health.Value = Mathf.Min(stats.maxHealth, Health.Value + add);
                }
            }
            else if (Health.Value > stats.maxHealth)
            {
                Health.Value = stats.maxHealth;
            }
        }
    }
}
