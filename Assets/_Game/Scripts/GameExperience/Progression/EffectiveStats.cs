using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// O que o dono declara usar nesta vida (skin, pet, níveis). Escrito pelo dono; o host NUNCA confia nos valores:
    /// limita tudo ao permitido pela configuração (StatResolver) antes de afetar o jogo.
    /// </summary>
    public struct PlayerLoadout : INetworkSerializeByMemcpy, IEquatable<PlayerLoadout>
    {
        public int skinHash;
        public int petHash;
        public int petSkinHash;
        public byte petLevel;
        public byte characterLevel;
        public byte upHealth, upDamage, upRegen, upSpeed, upReload, upSuper;

        public int Upgrade(UpgradeStat stat)
        {
            switch (stat)
            {
                case UpgradeStat.Health: return upHealth;
                case UpgradeStat.Damage: return upDamage;
                case UpgradeStat.Regeneration: return upRegen;
                case UpgradeStat.Speed: return upSpeed;
                case UpgradeStat.Reload: return upReload;
                default: return upSuper;
            }
        }

        public void SetUpgrade(UpgradeStat stat, int level)
        {
            byte v = (byte)Mathf.Clamp(level, 0, 255);
            switch (stat)
            {
                case UpgradeStat.Health: upHealth = v; break;
                case UpgradeStat.Damage: upDamage = v; break;
                case UpgradeStat.Regeneration: upRegen = v; break;
                case UpgradeStat.Speed: upSpeed = v; break;
                case UpgradeStat.Reload: upReload = v; break;
                default: upSuper = v; break;
            }
        }

        public bool Equals(PlayerLoadout o) =>
            skinHash == o.skinHash && petHash == o.petHash && petSkinHash == o.petSkinHash && petLevel == o.petLevel &&
            characterLevel == o.characterLevel && upHealth == o.upHealth && upDamage == o.upDamage && upRegen == o.upRegen &&
            upSpeed == o.upSpeed && upReload == o.upReload && upSuper == o.upSuper;

        public override bool Equals(object obj) => obj is PlayerLoadout o && Equals(o);
        public override int GetHashCode() => skinHash ^ (petHash * 31) ^ (petLevel << 8) ^ characterLevel;
    }

    /// <summary>
    /// Modificadores efetivos de um jogador, calculados no host (base do BrawlerDefinition × estes valores).
    /// Sincronizados para todos (UI, movimento do dono). Os dados base nunca são alterados.
    /// </summary>
    public struct EffectiveModifiers : INetworkSerializeByMemcpy, IEquatable<EffectiveModifiers>
    {
        public float healthMultiplier;
        public float damageMultiplier;
        public float regenMultiplier;
        public float regenDelayReduction;
        public float speedMultiplier;
        public float sprintMultiplier;
        /// <summary>Multiplicador do tempo de recarga (menor = mais rápido).</summary>
        public float reloadTimeMultiplier;
        public float superChargeMultiplier;
        public int shieldAmount;
        public float shieldThreshold;
        public float shieldDuration;
        public byte petAbility;
        public float petAbilityValue;
        public bool competitive;

        public static EffectiveModifiers Neutral => new EffectiveModifiers
        {
            healthMultiplier = 1f,
            damageMultiplier = 1f,
            regenMultiplier = 1f,
            regenDelayReduction = 0f,
            speedMultiplier = 1f,
            sprintMultiplier = 1f,
            reloadTimeMultiplier = 1f,
            superChargeMultiplier = 1f,
            shieldAmount = 0,
            shieldThreshold = 0.4f,
            shieldDuration = 4f,
            petAbility = 0,
            petAbilityValue = 0f,
        };

        public PetAbilityType PetAbility => (PetAbilityType)petAbility;

        public bool Equals(EffectiveModifiers o) =>
            healthMultiplier == o.healthMultiplier && damageMultiplier == o.damageMultiplier && regenMultiplier == o.regenMultiplier &&
            regenDelayReduction == o.regenDelayReduction && speedMultiplier == o.speedMultiplier && sprintMultiplier == o.sprintMultiplier &&
            reloadTimeMultiplier == o.reloadTimeMultiplier && superChargeMultiplier == o.superChargeMultiplier && shieldAmount == o.shieldAmount &&
            shieldThreshold == o.shieldThreshold && shieldDuration == o.shieldDuration && petAbility == o.petAbility &&
            petAbilityValue == o.petAbilityValue && competitive == o.competitive;

        public override bool Equals(object obj) => obj is EffectiveModifiers o && Equals(o);
        public override int GetHashCode() => healthMultiplier.GetHashCode() ^ damageMultiplier.GetHashCode() ^ petAbility;
    }

    /// <summary>Combina valores base + progressão, com limites (casual) e normalização (competitivo).</summary>
    public static class StatResolver
    {
        /// <summary>Calcula os modificadores efetivos a partir do loadout declarado (sempre limitado à configuração).</summary>
        public static EffectiveModifiers Resolve(ProgressionConfig cfg, PetCatalog pets, PlayerLoadout loadout, bool competitive)
        {
            var m = EffectiveModifiers.Neutral;
            m.competitive = competitive;
            if (cfg == null) return m;

            bool upgradesOn = !(competitive && cfg.competitiveDisablesUpgrades);
            if (upgradesOn)
            {
                int charLevel = Mathf.Clamp(loadout.characterLevel, 1, cfg.characterLevels.maxLevel);
                int allowed = Mathf.Min(cfg.maxUpgradeLevel, cfg.MaxUpgradeLevelFor(charLevel));
                foreach (UpgradeStat stat in Enum.GetValues(typeof(UpgradeStat)))
                {
                    int level = Mathf.Clamp(loadout.Upgrade(stat), 0, allowed);
                    float bonus = BonusFor(cfg, stat, level, competitive);
                    Apply(ref m, stat, bonus);
                }
            }

            bool petsOn = !(competitive && cfg.competitiveDisablesPetAbilities);
            var pet = pets != null ? pets.FindByHash(loadout.petHash) : null;
            if (petsOn && pet != null)
            {
                float v = pet.AbilityValue(Mathf.Clamp(loadout.petLevel, 1, cfg.petLevels.maxLevel));
                m.petAbility = (byte)pet.ability;
                m.petAbilityValue = v;
                switch (pet.ability)
                {
                    case PetAbilityType.Agility:
                        m.speedMultiplier *= 1f + Mathf.Clamp(v, 0f, 0.1f);
                        break;
                    case PetAbilityType.Mobility:
                        m.sprintMultiplier *= 1f + Mathf.Clamp(v, 0f, 0.12f);
                        break;
                    case PetAbilityType.Protection:
                        m.shieldAmount = Mathf.Clamp(Mathf.RoundToInt(v), 0, 30);
                        m.shieldThreshold = 0.4f;
                        m.shieldDuration = 4f;
                        break;
                    case PetAbilityType.Recovery:
                        m.regenMultiplier *= 1f + Mathf.Clamp(v, 0f, 0.3f);
                        m.regenDelayReduction = 0.3f;
                        break;
                }
            }
            return m;
        }

        /// <summary>Bónus de um atributo para um nível de melhoria (limitado pelo teto casual; 0 em competitivo).</summary>
        public static float BonusFor(ProgressionConfig cfg, UpgradeStat stat, int level, bool competitive)
        {
            if (cfg == null || level <= 0) return 0f;
            if (competitive && cfg.competitiveDisablesUpgrades) return 0f;
            var t = cfg.Tuning(stat);
            float bonus = t.bonusPerLevel * level;
            return Mathf.Clamp(bonus, 0f, Mathf.Max(0f, t.casualCap));
        }

        static void Apply(ref EffectiveModifiers m, UpgradeStat stat, float bonus)
        {
            switch (stat)
            {
                case UpgradeStat.Health: m.healthMultiplier = 1f + bonus; break;
                case UpgradeStat.Damage: m.damageMultiplier = 1f + bonus; break;
                case UpgradeStat.Regeneration: m.regenMultiplier = 1f + bonus; break;
                case UpgradeStat.Speed: m.speedMultiplier = 1f + bonus; break;
                case UpgradeStat.Reload: m.reloadTimeMultiplier = 1f / (1f + bonus); break;
                case UpgradeStat.SpecialEfficiency: m.superChargeMultiplier = 1f + bonus; break;
            }
        }

        /// <summary>Vida máxima efetiva de um Brawler com estes modificadores.</summary>
        public static int MaxHealth(BrawlerDefinition b, EffectiveModifiers m)
        {
            int baseHp = b != null ? b.stats.maxHealth : 100;
            float mult = m.healthMultiplier > 0.01f ? m.healthMultiplier : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(baseHp * mult));
        }
    }

    /// <summary>
    /// Resultado de uma partida para um jogador, calculado e validado no host e enviado só ao dono.
    /// O cliente aplica estes valores tal como chegam (não recalcula nem acrescenta).
    /// </summary>
    public struct MatchResultData : INetworkSerializable
    {
        public FixedString64Bytes matchId;
        public FixedString32Bytes brawlerId;
        public int petHash;
        public byte mode;
        public bool competitive;
        public bool participated;
        public bool won;
        public bool draw;
        public short kills, killsOnBots, assists, assistsOnBots, deaths, objectives, headshots, placement;
        public int damageDealt;
        public short secondsPlayed;
        public int xpParticipation, xpKills, xpAssists, xpVictory, xpObjectives;
        public int coins;
        public int petXp;

        public int XpTotal => xpParticipation + xpKills + xpAssists + xpVictory + xpObjectives;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref matchId);
            s.SerializeValue(ref brawlerId);
            s.SerializeValue(ref petHash);
            s.SerializeValue(ref mode);
            s.SerializeValue(ref competitive);
            s.SerializeValue(ref participated);
            s.SerializeValue(ref won);
            s.SerializeValue(ref draw);
            s.SerializeValue(ref kills);
            s.SerializeValue(ref killsOnBots);
            s.SerializeValue(ref assists);
            s.SerializeValue(ref assistsOnBots);
            s.SerializeValue(ref deaths);
            s.SerializeValue(ref objectives);
            s.SerializeValue(ref headshots);
            s.SerializeValue(ref placement);
            s.SerializeValue(ref damageDealt);
            s.SerializeValue(ref secondsPlayed);
            s.SerializeValue(ref xpParticipation);
            s.SerializeValue(ref xpKills);
            s.SerializeValue(ref xpAssists);
            s.SerializeValue(ref xpVictory);
            s.SerializeValue(ref xpObjectives);
            s.SerializeValue(ref coins);
            s.SerializeValue(ref petXp);
        }
    }
}
