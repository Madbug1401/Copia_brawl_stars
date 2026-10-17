using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Atributos que podem ser melhorados por personagem.</summary>
    public enum UpgradeStat : byte
    {
        MaxHealth,
        Damage,
        Regeneration,
        Speed,
        Reload,
        SuperEfficiency,
    }

    /// <summary>
    /// Regras da partida para a progressão: em Casual as melhorias e os pets podem dar pequenas vantagens;
    /// em Competitiva tudo é normalizado (só contam os valores base dos Brawlers).
    /// </summary>
    public enum MatchRuleset : byte
    {
        Casual,
        Competitive,
    }

    public enum RewardType : byte
    {
        PlayerXp,
        Coins,
        BrawlerSkin,
        PetSkin,
        Pet,
        Cosmetic,
    }

    /// <summary>Uma recompensa (missão, nível, conquista). itemId identifica skins/pets/cosméticos.</summary>
    [Serializable]
    public class RewardEntry
    {
        public RewardType type = RewardType.Coins;
        public int amount = 0;
        [Tooltip("Id estável do item (skin, pet ou cosmético). Vazio para XP e moedas.")]
        public string itemId = "";

        public RewardEntry() { }
        public RewardEntry(RewardType type, int amount, string itemId = "")
        {
            this.type = type;
            this.amount = amount;
            this.itemId = itemId ?? "";
        }
    }

    /// <summary>
    /// O que cada jogador leva para a partida (sincronizado pelo host depois de validado): skin, pet e níveis de
    /// melhoria do Brawler atual. Usa índices nos registos (iguais em todas as máquinas da mesma build);
    /// os ids estáveis em texto ficam no ficheiro de gravação.
    /// </summary>
    public struct PlayerLoadout : INetworkSerializeByMemcpy, IEquatable<PlayerLoadout>
    {
        /// <summary>Brawler a que se referem a skin e as melhorias (tem de ser o HeroId atual).</summary>
        public int BrawlerIndex;
        /// <summary>-1 = aspeto original.</summary>
        public short SkinIndex;
        /// <summary>-1 = sem pet.</summary>
        public short PetIndex;
        public short PetSkinIndex;
        public byte PetLevel;
        public byte BrawlerLevel;
        public byte UpHealth, UpDamage, UpRegen, UpSpeed, UpReload, UpSuper;

        public static PlayerLoadout Empty => new PlayerLoadout
        {
            BrawlerIndex = -1,
            SkinIndex = -1,
            PetIndex = -1,
            PetSkinIndex = -1,
            PetLevel = 1,
            BrawlerLevel = 1,
        };

        public bool HasPet => PetIndex >= 0;

        public int GetUpgrade(UpgradeStat stat)
        {
            switch (stat)
            {
                case UpgradeStat.MaxHealth: return UpHealth;
                case UpgradeStat.Damage: return UpDamage;
                case UpgradeStat.Regeneration: return UpRegen;
                case UpgradeStat.Speed: return UpSpeed;
                case UpgradeStat.Reload: return UpReload;
                default: return UpSuper;
            }
        }

        public void SetUpgrade(UpgradeStat stat, int level)
        {
            byte v = (byte)Mathf.Clamp(level, 0, 255);
            switch (stat)
            {
                case UpgradeStat.MaxHealth: UpHealth = v; break;
                case UpgradeStat.Damage: UpDamage = v; break;
                case UpgradeStat.Regeneration: UpRegen = v; break;
                case UpgradeStat.Speed: UpSpeed = v; break;
                case UpgradeStat.Reload: UpReload = v; break;
                default: UpSuper = v; break;
            }
        }

        public int TotalUpgrades => UpHealth + UpDamage + UpRegen + UpSpeed + UpReload + UpSuper;

        public bool Equals(PlayerLoadout o) =>
            BrawlerIndex == o.BrawlerIndex && SkinIndex == o.SkinIndex && PetIndex == o.PetIndex && PetSkinIndex == o.PetSkinIndex &&
            PetLevel == o.PetLevel && BrawlerLevel == o.BrawlerLevel && UpHealth == o.UpHealth && UpDamage == o.UpDamage &&
            UpRegen == o.UpRegen && UpSpeed == o.UpSpeed && UpReload == o.UpReload && UpSuper == o.UpSuper;

        public override bool Equals(object obj) => obj is PlayerLoadout o && Equals(o);
        public override int GetHashCode() => unchecked(BrawlerIndex * 397 ^ SkinIndex * 31 ^ PetIndex * 17 ^ TotalUpgrades);
    }

    /// <summary>Atributos efetivos = base do BrawlerDefinition + melhorias + pet, limitados pelas regras da partida.</summary>
    public struct EffectiveStats
    {
        public int maxHealth;
        public float damageMultiplier;
        public float regenPercentPerSecond;
        public float regenDelay;
        public float speedMultiplier;
        public float sprintMultiplier;
        /// <summary>Multiplica o tempo de recarga por carga (menor = mais rápido).</summary>
        public float reloadTimeMultiplier;
        public float superChargeMultiplier;
        /// <summary>Escudo do pet (Porco): dano absorvido e intervalo de recarga.</summary>
        public int petShield;
        public float petShieldCooldown;
        /// <summary>Raio do radar/alerta dos pets de deteção (só informação para o dono).</summary>
        public float petRadarRadius;
        public float petAlertRadius;

        public static EffectiveStats FromBase(BrawlerDefinition b)
        {
            var s = b != null ? b.stats : new BrawlerStats();
            return new EffectiveStats
            {
                maxHealth = Mathf.Max(1, s.maxHealth),
                damageMultiplier = 1f,
                regenPercentPerSecond = s.regenPercentPerSecond,
                regenDelay = s.regenDelay,
                speedMultiplier = 1f,
                sprintMultiplier = 1f,
                reloadTimeMultiplier = 1f,
                superChargeMultiplier = 1f,
            };
        }
    }

    /// <summary>
    /// Resultado de uma partida para um jogador, calculado e validado pelo host e enviado só ao dono.
    /// MatchKey é único por partida: o cliente ignora mensagens repetidas (reconexões, ecrã de resultados reaberto).
    /// </summary>
    public struct MatchRewardMessage : INetworkSerializeByMemcpy
    {
        public FixedString64Bytes MatchKey;
        public int BrawlerIndex;
        public short PetIndex;
        public byte Valid;
        public byte Victory;
        public byte Ruleset;
        public byte Mode;
        public short Kills, Deaths, Assists, Objectives, Headshots;
        public int Damage;
        public float Seconds;
        public int XpParticipation, XpKills, XpAssists, XpVictory, XpObjectives;
        public int XpTotal;
        public int BrawlerXp;
        public int PetXp;
        public int Coins;
        public byte UsedPet;
    }
}
