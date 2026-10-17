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
        [Tooltip("Segundos de proteção depois de reaparecer (termina ao atacar).")]
        public float spawnProtection = 2.2f;

        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>(false);
        /// <summary>NetworkObjectId de quem matou por último.</summary>
        public readonly NetworkVariable<ulong> LastKiller = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>Hora do servidor até à qual o jogador está protegido (escudo de spawn).</summary>
        public readonly NetworkVariable<double> ProtectedUntil = new NetworkVariable<double>(0);
        /// <summary>Escudo do pet Porco (pontos atuais; 0 = sem escudo). Decidido no servidor.</summary>
        public readonly NetworkVariable<int> PetShield = new NetworkVariable<int>(0);

        PlayerNetwork m_Player;
        float m_LastCombatTime;
        float m_RegenCarry;
        float m_RespawnAt;
        bool m_NeedsInitialSpawn;
        float m_ClientRespawnAt;
        int m_LastMaxHealth;
        float m_ShieldReadyAt;
        EffectiveStats m_Stats;
        int m_StatsFrame = -1;

        public bool IsDead => Dead.Value;

        /// <summary>Atributos efetivos (base do Brawler + melhorias + pet, conforme as regras da partida).</summary>
        public EffectiveStats Stats
        {
            get
            {
                if (m_StatsFrame != Time.frameCount)
                {
                    m_StatsFrame = Time.frameCount;
                    m_Stats = BrawlerStatsResolver.Resolve(m_Player);
                }
                return m_Stats;
            }
        }

        public int MaxHealth => m_Player.Brawler != null ? Stats.maxHealth : 100;
        public float RespawnRemaining => Mathf.Max(0f, m_ClientRespawnAt - Time.time);
        public bool IsProtected => IsSpawned && NetworkManager != null && !Dead.Value && NetworkManager.ServerTime.Time < ProtectedUntil.Value;

        // Abrandamento vive agora no StatusEffects; mantido aqui por compatibilidade.
        public bool IsSlowed => m_Player.Status != null && m_Player.Status.IsSlowed;
        public float SpeedMultiplier => (m_Player.Status != null ? m_Player.Status.SpeedMultiplier : 1f) * Stats.speedMultiplier;
        /// <summary>Bónus extra a correr (pet Coelho).</summary>
        public float SprintMultiplier => Stats.sprintMultiplier;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            Dead.OnValueChanged += OnDeadChanged;
            Health.OnValueChanged += OnHealthChanged;
            m_Player.HeroId.OnValueChanged += OnHeroChanged;

            if (IsServer)
            {
                m_LastMaxHealth = MaxHealth;
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
                m_StatsFrame = -1;
                m_LastMaxHealth = MaxHealth;
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
            if (current < previous && !Dead.Value) m_Player.Visuals?.OnHurt(previous - current);
        }

        /// <summary>Servidor: aplica dano. Devolve true se matou.</summary>
        public bool ServerApplyDamage(int amount, ulong attackerId)
        {
            if (!IsServer || Dead.Value || amount <= 0) return false;
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver) return false;
            if (MatchManager.AreTeammates(PlayerNetwork.Find(attackerId), m_Player)) return false;
            if (IsProtected) return false;

            m_LastCombatTime = Time.time;

            // Escudo do pet absorve primeiro (e só recarrega depois do intervalo)
            if (PetShield.Value > 0)
            {
                int absorbed = Mathf.Min(PetShield.Value, amount);
                PetShield.Value -= absorbed;
                amount -= absorbed;
                if (PetShield.Value <= 0) m_ShieldReadyAt = Time.time + Mathf.Max(5f, Stats.petShieldCooldown);
                if (amount <= 0) return false;
            }

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

        public void ServerNotifyAttacked()
        {
            m_LastCombatTime = Time.time;
            // Atacar termina a proteção de spawn
            if (ProtectedUntil.Value > 0) ProtectedUntil.Value = 0;
        }

        void ServerDie(ulong attackerId)
        {
            Dead.Value = true;
            ProtectedUntil.Value = 0;
            LastKiller.Value = attackerId;
            m_Player.Status?.ServerClear();
            m_RespawnAt = Time.time + respawnDelay;

            m_Player.Deaths.Value++;
            var killer = PlayerNetwork.Find(attackerId);
            if (killer != null && killer != m_Player && !MatchManager.AreTeammates(killer, m_Player))
                killer.Kills.Value++;
            PetShield.Value = 0;
            m_ShieldReadyAt = Time.time + Mathf.Max(5f, Stats.petShieldCooldown);

            // Antes do MatchManager: a morte pode terminar a partida e a eliminação tem de contar
            MatchStatsTracker.ServerOnKill(killer, m_Player);

            if (MatchManager.Instance != null) MatchManager.Instance.ServerOnKill(killer, m_Player);
        }

        public void ServerRespawn()
        {
            if (!IsServer) return;

            SpawnPoint.Pick(m_Player, out var pos, out var yaw);
            m_RegenCarry = 0f;
            m_StatsFrame = -1;
            m_LastMaxHealth = MaxHealth;
            Health.Value = MaxHealth;
            PetShield.Value = Stats.petShield;
            m_Player.Status?.ServerClear();
            m_Player.Motor.TeleportRpc(pos, yaw);
            m_Player.Combat.ServerRefill();
            ProtectedUntil.Value = NetworkManager.ServerTime.Time + spawnProtection;
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
                bool noRespawnMode = MatchManager.Instance != null && MatchManager.Instance.IsSpawned &&
                    !MatchManager.Instance.CanRespawnDeadPlayers;
                if (Time.time >= m_RespawnAt && !MatchManager.IsMatchOver && !noRespawnMode) ServerRespawn();
                return;
            }

            var stats = Stats;
            int maxHealth = stats.maxHealth;

            // Vida máxima mudou (loadout/regras): mantém a percentagem de vida, sem cura grátis
            if (m_LastMaxHealth > 0 && maxHealth != m_LastMaxHealth)
            {
                float ratio = Health.Value / (float)m_LastMaxHealth;
                Health.Value = Mathf.Clamp(Mathf.FloorToInt(ratio * maxHealth), 1, maxHealth);
            }
            m_LastMaxHealth = maxHealth;

            // Escudo do pet: recarrega depois do intervalo
            if (stats.petShield <= 0) { if (PetShield.Value != 0) PetShield.Value = 0; }
            else if (PetShield.Value < stats.petShield && Time.time >= m_ShieldReadyAt && Time.time - m_LastCombatTime > 3f)
                PetShield.Value = stats.petShield;
            else if (PetShield.Value > stats.petShield) PetShield.Value = stats.petShield;

            if (Health.Value < maxHealth && Time.time - m_LastCombatTime > stats.regenDelay)
            {
                m_RegenCarry += maxHealth * stats.regenPercentPerSecond * Time.deltaTime;
                int add = Mathf.FloorToInt(m_RegenCarry);
                if (add > 0)
                {
                    m_RegenCarry -= add;
                    Health.Value = Mathf.Min(maxHealth, Health.Value + add);
                }
            }
            else if (Health.Value > maxHealth)
            {
                Health.Value = maxHealth;
            }
        }
    }
}
