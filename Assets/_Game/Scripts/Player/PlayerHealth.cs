using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Vida, morte, respawn e abrandamento. Tudo decidido no servidor (host); os clientes só leem.
    /// A vida máxima e a regeneração vêm dos atributos efetivos (base do Brawler + modificadores de progressão,
    /// calculados no host — ver PlayerNetwork.Effective); o BrawlerDefinition nunca é alterado em jogo.
    /// </summary>
    public class PlayerHealth : NetworkBehaviour
    {
        public float respawnDelay = 3f;
        [Tooltip("Segundos de proteção depois de reaparecer (termina ao atacar).")]
        public float spawnProtection = 2.2f;
        [Tooltip("Janela (s) em que quem causou dano conta como assistência na eliminação.")]
        public float assistWindow = 8f;

        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        public readonly NetworkVariable<bool> Dead = new NetworkVariable<bool>(false);
        /// <summary>NetworkObjectId de quem matou por último.</summary>
        public readonly NetworkVariable<ulong> LastKiller = new NetworkVariable<ulong>(ulong.MaxValue);
        /// <summary>Hora do servidor até à qual o jogador está protegido (escudo de spawn).</summary>
        public readonly NetworkVariable<double> ProtectedUntil = new NetworkVariable<double>(0);
        /// <summary>Escudo temporário (habilidade do pet Porco). Absorve dano antes da vida.</summary>
        public readonly NetworkVariable<int> Shield = new NetworkVariable<int>(0);

        struct DamageRecord
        {
            public ulong attackerId;
            public float time;
        }

        PlayerNetwork m_Player;
        float m_LastCombatTime;
        float m_RegenCarry;
        float m_RespawnAt;
        bool m_NeedsInitialSpawn;
        float m_ClientRespawnAt;
        float m_ShieldUntil;
        bool m_PetShieldUsedThisLife;
        readonly List<DamageRecord> m_DamageLog = new List<DamageRecord>();

        public bool IsDead => Dead.Value;
        public int MaxHealth => m_Player.EffectiveMaxHealth;
        public float RespawnRemaining => Mathf.Max(0f, m_ClientRespawnAt - Time.time);
        public bool IsProtected => IsSpawned && NetworkManager != null && !Dead.Value && NetworkManager.ServerTime.Time < ProtectedUntil.Value;

        // Abrandamento vive agora no StatusEffects; mantido aqui por compatibilidade.
        public bool IsSlowed => m_Player.Status != null && m_Player.Status.IsSlowed;
        public float SpeedMultiplier => m_Player.Status != null ? m_Player.Status.SpeedMultiplier : 1f;
        public bool IsRooted => m_Player.Status != null && m_Player.Status.IsRooted;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            Dead.OnValueChanged += OnDeadChanged;
            Health.OnValueChanged += OnHealthChanged;
            m_Player.HeroId.OnValueChanged += OnHeroChanged;

            if (IsServer)
            {
                m_Player.ServerRefreshEffectiveStats();
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
                m_Player.ServerRefreshEffectiveStats();
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
        public bool ServerApplyDamage(int amount, ulong attackerId) => ServerApplyDamage(amount, attackerId, false, out _);

        /// <summary>
        /// Servidor: aplica dano (o escudo do pet absorve primeiro, exceto se ignoreShield). 'applied' = dano efetivamente
        /// retirado (escudo + vida). Devolve true se matou.
        /// </summary>
        public bool ServerApplyDamage(int amount, ulong attackerId, bool ignoreShield, out int applied)
        {
            applied = 0;
            if (!IsServer || Dead.Value || amount <= 0) return false;
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver) return false;
            if (MatchManager.AreTeammates(PlayerNetwork.Find(attackerId), m_Player)) return false;
            if (IsProtected) return false;

            m_LastCombatTime = Time.time;
            int remaining = amount;
            if (!ignoreShield && Shield.Value > 0)
            {
                int absorbed = Mathf.Min(Shield.Value, remaining);
                Shield.Value -= absorbed;
                remaining -= absorbed;
                applied += absorbed;
            }

            int before = Health.Value;
            Health.Value = Mathf.Max(0, Health.Value - remaining);
            applied += before - Health.Value;
            if (attackerId != m_Player.Id && attackerId != ulong.MaxValue) RecordDamage(attackerId);

            if (Health.Value > 0)
            {
                ServerCheckPetShield();
                return false;
            }

            ServerDie(attackerId);
            return true;
        }

        void RecordDamage(ulong attackerId)
        {
            for (int i = 0; i < m_DamageLog.Count; i++)
            {
                if (m_DamageLog[i].attackerId != attackerId) continue;
                m_DamageLog[i] = new DamageRecord { attackerId = attackerId, time = Time.time };
                return;
            }
            m_DamageLog.Add(new DamageRecord { attackerId = attackerId, time = Time.time });
        }

        /// <summary>Pet Porco: quando a vida desce abaixo do limiar pela primeira vez nesta vida, ganha um escudo temporário.</summary>
        void ServerCheckPetShield()
        {
            var mods = m_Player.Effective.Value;
            if (m_PetShieldUsedThisLife || mods.shieldAmount <= 0) return;
            if (Health.Value > MaxHealth * mods.shieldThreshold) return;
            m_PetShieldUsedThisLife = true;
            Shield.Value = mods.shieldAmount;
            m_ShieldUntil = Time.time + Mathf.Max(0.5f, mods.shieldDuration);
            m_Player.Combat.ServerPetAbilityFeedback(PetAbilityType.Protection);
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
            Shield.Value = 0;
            LastKiller.Value = attackerId;
            m_Player.Status?.ServerClear();
            m_RespawnAt = Time.time + respawnDelay;

            m_Player.Deaths.Value++;
            var killer = PlayerNetwork.Find(attackerId);
            bool validKill = killer != null && killer != m_Player && !MatchManager.AreTeammates(killer, m_Player);
            if (validKill) killer.Kills.Value++;

            // Assistências: quem causou dano há pouco tempo (exceto quem matou e colegas da vítima)
            float now = Time.time;
            foreach (var rec in m_DamageLog)
            {
                if (rec.attackerId == attackerId || now - rec.time > assistWindow) continue;
                var helper = PlayerNetwork.Find(rec.attackerId);
                if (helper == null || helper == m_Player || MatchManager.AreTeammates(helper, m_Player)) continue;
                helper.Assists.Value++;
                MatchStatsTracker.ServerOnAssist(helper, m_Player);
            }
            m_DamageLog.Clear();

            if (validKill) MatchStatsTracker.ServerOnKill(killer, m_Player);
            if (CombatSystem.Instance != null) CombatSystem.Instance.ServerOnOwnerDied(m_Player.Id);
            m_Player.Combat.ServerOnOwnerDied();
            if (MatchManager.Instance != null) MatchManager.Instance.ServerOnKill(killer, m_Player);
        }

        public void ServerRespawn()
        {
            if (!IsServer) return;

            SpawnPoint.Pick(m_Player, out var pos, out var yaw);
            m_Player.ServerRefreshEffectiveStats();
            m_RegenCarry = 0f;
            m_PetShieldUsedThisLife = false;
            m_DamageLog.Clear();
            Shield.Value = 0;
            Health.Value = MaxHealth;
            m_Player.Status?.ServerClear();
            m_Player.Motor.TeleportRpc(pos, yaw);
            m_Player.Combat.ServerRefill();
            ProtectedUntil.Value = NetworkManager.ServerTime.Time + spawnProtection;
            Dead.Value = false;
        }

        /// <summary>Servidor: os atributos mudaram fora de combate (ex.: loadout no spawn): ajusta a vida sem curar a meio de uma luta.</summary>
        public void ServerOnMaxHealthChanged(int previousMax)
        {
            if (!IsServer || Dead.Value) return;
            if (Health.Value >= previousMax || IsProtected) Health.Value = MaxHealth;
            else Health.Value = Mathf.Min(Health.Value, MaxHealth);
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

            if (Shield.Value > 0 && Time.time >= m_ShieldUntil) Shield.Value = 0;

            var stats = m_Player.Brawler.stats;
            var mods = m_Player.Effective.Value;
            int maxHealth = MaxHealth;
            float regenDelay = Mathf.Max(0.5f, stats.regenDelay - mods.regenDelayReduction);
            if (Health.Value < maxHealth && Time.time - m_LastCombatTime > regenDelay)
            {
                m_RegenCarry += maxHealth * stats.regenPercentPerSecond * Mathf.Max(0.1f, mods.regenMultiplier) * Time.deltaTime;
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
