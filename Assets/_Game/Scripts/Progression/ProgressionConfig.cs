using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Curva de XP: o XP para passar do nível N para N+1 cresce gradualmente.</summary>
    [Serializable]
    public class LevelCurve
    {
        [Min(1)] public int maxLevel = 50;
        [Tooltip("XP para passar do nível 1 ao 2.")]
        [Min(1)] public int baseXp = 150;
        [Tooltip("XP extra por cada nível seguinte.")]
        [Min(0)] public int increment = 40;
        [Tooltip("1 = crescimento linear; >1 = acelera nos níveis altos.")]
        [Min(0.5f)] public float exponent = 1f;

        public LevelCurve() { }
        public LevelCurve(int maxLevel, int baseXp, int increment, float exponent = 1f)
        {
            this.maxLevel = maxLevel;
            this.baseXp = baseXp;
            this.increment = increment;
            this.exponent = exponent;
        }

        /// <summary>XP necessário para passar do nível 'level' ao seguinte (0 no nível máximo).</summary>
        public int XpToNext(int level)
        {
            if (level >= maxLevel) return 0;
            level = Mathf.Max(1, level);
            return Mathf.Max(1, Mathf.RoundToInt(baseXp + increment * Mathf.Pow(level - 1, exponent)));
        }

        /// <summary>XP total acumulado necessário para chegar ao nível 'level'.</summary>
        public int TotalXpForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, maxLevel);
            long total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return (int)Mathf.Min(int.MaxValue, total);
        }

        public int MaxTotalXp => TotalXpForLevel(maxLevel);

        /// <summary>Nível para um XP total, com o XP dentro do nível e o XP necessário para o seguinte.</summary>
        public int LevelForTotal(int totalXp, out int xpIntoLevel, out int xpForNext)
        {
            totalXp = Mathf.Max(0, totalXp);
            int level = 1;
            int remaining = totalXp;
            while (level < maxLevel)
            {
                int need = XpToNext(level);
                if (remaining < need) break;
                remaining -= need;
                level++;
            }
            xpForNext = XpToNext(level);
            xpIntoLevel = level >= maxLevel ? 0 : remaining;
            return level;
        }

        public int LevelForTotal(int totalXp) => LevelForTotal(totalXp, out _, out _);
    }

    [Serializable]
    public class XpRewardTable
    {
        [Tooltip("Participação válida numa partida.")]
        public int participation = 50;
        public int kill = 12;
        public int assist = 6;
        [Tooltip("Bónus de vitória.")]
        public int victory = 75;
        public int objective = 15;
        [Tooltip("Máximo de eliminações/assistências contadas por partida (anti-farm).")]
        public int maxKillsCounted = 25;
        public int maxObjectivesCounted = 6;
        [Tooltip("Segundos mínimos na partida para a participação ser válida...")]
        public float minParticipationSeconds = 60f;
        [Tooltip("...ou esta fração da duração da partida (partidas curtas).")]
        [Range(0f, 1f)] public float minParticipationFraction = 0.4f;
        [Tooltip("Exige atividade (dano ou eliminação) para a participação ser válida (anti-AFK).")]
        public bool requireActivity = true;
        [Tooltip("Janela (s) em que dano causado antes de uma eliminação conta como assistência.")]
        public float assistWindowSeconds = 6f;
    }

    [Serializable]
    public class CoinRewardTable
    {
        public int participation = 20;
        public int victory = 30;
        public int perKill = 2;
        public int maxPerMatch = 120;
    }

    [Serializable]
    public class PetXpTable
    {
        public int participation = 40;
        public int victory = 20;
        public int perKill = 2;
    }

    /// <summary>Uma melhoria de atributo: bónus por nível, nível máximo e custo em moedas.</summary>
    [Serializable]
    public class StatUpgradeRule
    {
        public UpgradeStat stat;
        public string displayName = "";
        [Tooltip("Bónus por nível (fração: 0.02 = +2%).")]
        public float bonusPerLevel = 0.02f;
        [Min(0)] public int maxLevel = 5;
        public int baseCost = 60;
        public int costPerLevel = 40;

        public int CostForLevel(int targetLevel) => baseCost + costPerLevel * Mathf.Max(0, targetLevel - 1);
    }

    /// <summary>O que a progressão pode alterar em cada tipo de partida.</summary>
    [Serializable]
    public class RulesetSettings
    {
        public bool upgradesEnabled = true;
        [Tooltip("Bónus máximo por atributo vindo de melhorias (fração).")]
        [Range(0f, 0.5f)] public float maxBonusPerStat = 0.1f;
        [Tooltip("Habilidades passivas dos pets que afetam o combate (escudo, velocidade, regeneração).")]
        public bool petCombatPassives = true;
        [Tooltip("Habilidades de informação dos pets (radar, alerta).")]
        public bool petInfoPassives = true;
    }

    /// <summary>Recompensa entregue ao chegar a um nível de jogador.</summary>
    [Serializable]
    public class LevelReward
    {
        [Min(2)] public int level = 2;
        public RewardEntry reward = new RewardEntry();
    }

    /// <summary>
    /// Todos os valores de progressão (XP, moedas, curvas, melhorias, regras casual/competitiva).
    /// Classe simples (sem Unity) para poder ser testada fora do editor; vive dentro do asset ProgressionConfig.
    /// </summary>
    [Serializable]
    public class ProgressionSettings
    {
        [Header("XP por partida")]
        public XpRewardTable xp = new XpRewardTable();
        [Tooltip("Fração do XP da partida que vai para o Brawler usado.")]
        [Range(0f, 2f)] public float brawlerXpShare = 1f;
        public PetXpTable petXp = new PetXpTable();
        public CoinRewardTable coins = new CoinRewardTable();

        [Header("Curvas de nível")]
        public LevelCurve playerCurve = new LevelCurve(50, 150, 40);
        public LevelCurve brawlerCurve = new LevelCurve(10, 100, 60);
        public LevelCurve petCurve = new LevelCurve(10, 80, 45);

        [Header("Melhorias de atributos")]
        public List<StatUpgradeRule> upgrades = new List<StatUpgradeRule>();
        [Tooltip("Pontos de melhoria ganhos por cada nível do Brawler acima do 1.")]
        [Min(0)] public int upgradePointsPerBrawlerLevel = 1;

        [Header("Regras por tipo de partida")]
        public RulesetSettings casual = new RulesetSettings();
        public RulesetSettings competitive = new RulesetSettings { upgradesEnabled = false, maxBonusPerStat = 0f, petCombatPassives = false, petInfoPassives = false };

        [Header("Recompensas de nível do jogador")]
        public List<LevelReward> playerLevelRewards = new List<LevelReward>();
        [Tooltip("Moedas por cada subida de nível do jogador (além das recompensas da lista).")]
        public int coinsPerPlayerLevel = 25;

        public RulesetSettings Rules(MatchRuleset ruleset) => ruleset == MatchRuleset.Competitive ? competitive : casual;

        public StatUpgradeRule Upgrade(UpgradeStat stat)
        {
            if (upgrades != null)
                foreach (var u in upgrades)
                    if (u != null && u.stat == stat) return u;
            return null;
        }

        /// <summary>Pontos de melhoria disponíveis para um Brawler neste nível.</summary>
        public int UpgradePointsAt(int brawlerLevel) => Mathf.Max(0, brawlerLevel - 1) * upgradePointsPerBrawlerLevel;

        /// <summary>Valores por omissão do "Game Experience" (os mesmos que o asset gerado).</summary>
        public static ProgressionSettings CreateDefault()
        {
            var s = new ProgressionSettings();
            s.upgrades = new List<StatUpgradeRule>
            {
                new StatUpgradeRule { stat = UpgradeStat.MaxHealth, displayName = "Vida", bonusPerLevel = 0.02f, maxLevel = 5, baseCost = 60, costPerLevel = 40 },
                new StatUpgradeRule { stat = UpgradeStat.Damage, displayName = "Dano", bonusPerLevel = 0.015f, maxLevel = 5, baseCost = 80, costPerLevel = 50 },
                new StatUpgradeRule { stat = UpgradeStat.Regeneration, displayName = "Regeneração", bonusPerLevel = 0.02f, maxLevel = 5, baseCost = 50, costPerLevel = 30 },
                new StatUpgradeRule { stat = UpgradeStat.Speed, displayName = "Velocidade", bonusPerLevel = 0.01f, maxLevel = 5, baseCost = 60, costPerLevel = 40 },
                new StatUpgradeRule { stat = UpgradeStat.Reload, displayName = "Recarga", bonusPerLevel = 0.02f, maxLevel = 5, baseCost = 60, costPerLevel = 40 },
                new StatUpgradeRule { stat = UpgradeStat.SuperEfficiency, displayName = "Eficiência do Super", bonusPerLevel = 0.02f, maxLevel = 5, baseCost = 70, costPerLevel = 45 },
            };
            // Pets e skins com regra "nível de jogador" desbloqueiam sozinhos (PetUnlockManager/SkinManager);
            // aqui ficam só as recompensas extra de nível.
            s.playerLevelRewards = new List<LevelReward>
            {
                new LevelReward { level = 3, reward = new RewardEntry(RewardType.Coins, 100) },
                new LevelReward { level = 12, reward = new RewardEntry(RewardType.Coins, 250) },
                new LevelReward { level = 20, reward = new RewardEntry(RewardType.Coins, 500) },
                new LevelReward { level = 30, reward = new RewardEntry(RewardType.Cosmetic, 1, "title_veteran") },
                new LevelReward { level = 50, reward = new RewardEntry(RewardType.Cosmetic, 1, "title_legend") },
            };
            return s;
        }
    }

    /// <summary>
    /// Asset com os valores de progressão (Assets/_Game/Resources/GameExperience/ProgressionConfig.asset, gerado pelo
    /// menu FPS-LAN → Game Experience). Ajustável no Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Progression Config")]
    public class ProgressionConfig : ScriptableObject
    {
        public ProgressionSettings settings = ProgressionSettings.CreateDefault();
    }
}
