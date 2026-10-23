using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Atributo que pode ser melhorado com a progressão de uma personagem.</summary>
    public enum UpgradeStat
    {
        Health,
        Damage,
        Regeneration,
        Speed,
        Reload,
        SpecialEfficiency,
    }

    /// <summary>
    /// Curva de níveis: XP para passar do nível n ao n+1 = base + linear·(n−1) + quadrática·(n−1)².
    /// Cresce gradualmente; o nível é sempre derivado do XP total (nunca guardado à parte).
    /// </summary>
    [Serializable]
    public class LevelCurve
    {
        [Min(1)] public int maxLevel = 10;
        [Min(1)] public int baseXp = 100;
        [Min(0)] public int linearXp = 25;
        [Min(0f)] public float quadraticXp = 0f;

        /// <summary>XP necessário para passar do nível 'level' ao seguinte (0 no nível máximo).</summary>
        public int XpToNext(int level)
        {
            if (level >= maxLevel || level < 1) return 0;
            int n = level - 1;
            return Mathf.Max(1, Mathf.RoundToInt(baseXp + linearXp * n + quadraticXp * n * n));
        }

        /// <summary>XP total acumulado necessário para atingir 'level'.</summary>
        public long TotalXpForLevel(int level)
        {
            level = Mathf.Clamp(level, 1, maxLevel);
            long total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return total;
        }

        public long MaxTotalXp => TotalXpForLevel(maxLevel);

        public int LevelForXp(long totalXp)
        {
            if (totalXp <= 0) return 1;
            int level = 1;
            long acc = 0;
            while (level < maxLevel)
            {
                long need = XpToNext(level);
                if (acc + need > totalXp) break;
                acc += need;
                level++;
            }
            return level;
        }

        /// <summary>Progresso dentro do nível atual: XP já feito e XP total do nível (0/0 no máximo).</summary>
        public void Progress(long totalXp, out int level, out int intoLevel, out int levelSpan)
        {
            level = LevelForXp(totalXp);
            if (level >= maxLevel)
            {
                intoLevel = 0;
                levelSpan = 0;
                return;
            }
            intoLevel = (int)(totalXp - TotalXpForLevel(level));
            levelSpan = XpToNext(level);
        }

        public float Progress01(long totalXp)
        {
            Progress(totalXp, out _, out int into, out int span);
            return span <= 0 ? 1f : Mathf.Clamp01(into / (float)span);
        }
    }

    /// <summary>Afinação de um atributo melhorável: bónus por nível e limite em partidas casuais.</summary>
    [Serializable]
    public class UpgradeTuning
    {
        public UpgradeStat stat;
        public string label = "";
        [Tooltip("Bónus por nível de melhoria (0.03 = +3%).")]
        public float bonusPerLevel = 0.03f;
        [Tooltip("Bónus máximo permitido em partidas casuais (as competitivas normalizam para 0).")]
        public float casualCap = 0.12f;
    }

    /// <summary>Recompensa atribuída ao atingir um nível de jogador.</summary>
    [Serializable]
    public class LevelReward
    {
        [Min(2)] public int level = 2;
        public RewardBundle reward = new RewardBundle();
    }

    /// <summary>
    /// Todos os valores de progressão (XP, níveis, moedas, melhorias, normalização competitiva).
    /// Fica em Resources/GameExperience/ProgressionConfig.asset; se não existir usa-se uma instância com estes valores.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Progression Config")]
    public class ProgressionConfig : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/ProgressionConfig";

        [Header("XP por partida (valores iniciais)")]
        public int xpParticipation = 50;
        public int xpPerKill = 12;
        public int xpPerAssist = 6;
        public int xpVictory = 75;
        public int xpPerObjective = 15;
        [Tooltip("Multiplicador do XP de eliminações/assistências sobre bots (treino).")]
        [Range(0f, 1f)] public float xpVsBotsMultiplier = 0.5f;
        [Tooltip("Máximo de eliminações contadas para XP por partida (anti-farm).")]
        public int maxKillsCounted = 30;
        public int maxAssistsCounted = 30;
        public int maxObjectivesCounted = 5;

        [Header("Participação válida")]
        [Tooltip("Tempo mínimo em partida (s) para a participação contar.")]
        public float minParticipationSeconds = 60f;
        [Tooltip("Ou esta fração da duração da partida (partidas curtas).")]
        [Range(0f, 1f)] public float minParticipationFraction = 0.4f;
        [Tooltip("Atividade mínima: dano causado OU distância percorrida (m), para não premiar quem fica parado.")]
        public float minDistanceMoved = 15f;

        [Header("XP de personagem e de pet")]
        [Tooltip("Fração do XP da partida que vai para a personagem usada.")]
        [Range(0f, 2f)] public float characterXpShare = 1f;
        public int petXpPerValidMatch = 40;
        public int petXpPerKill = 4;
        public int petXpVictoryBonus = 20;

        [Header("Moedas")]
        public int coinsParticipation = 20;
        public int coinsVictory = 30;
        public int coinsPerKill = 2;
        public int coinsPerObjective = 3;

        [Header("Níveis")]
        public LevelCurve playerLevels = new LevelCurve { maxLevel = 50, baseXp = 100, linearXp = 25, quadraticXp = 0f };
        public LevelCurve characterLevels = new LevelCurve { maxLevel = 10, baseXp = 80, linearXp = 40, quadraticXp = 0f };
        public LevelCurve petLevels = new LevelCurve { maxLevel = 10, baseXp = 60, linearXp = 30, quadraticXp = 0f };

        [Header("Melhorias de atributos")]
        [Min(1)] public int maxUpgradeLevel = 5;
        [Tooltip("Custo em moedas de cada nível de melhoria (índice 0 = nível 1).")]
        public int[] upgradeCosts = { 100, 200, 350, 500, 700 };
        [Tooltip("Níveis de personagem por nível de melhoria: o nível de melhoria n exige nível de personagem ≥ (n−1)·este+1.")]
        [Min(1)] public int characterLevelsPerUpgradeLevel = 2;
        public List<UpgradeTuning> upgrades = new List<UpgradeTuning>
        {
            new UpgradeTuning { stat = UpgradeStat.Health, label = "Max health", bonusPerLevel = 0.03f, casualCap = 0.12f },
            new UpgradeTuning { stat = UpgradeStat.Damage, label = "Damage", bonusPerLevel = 0.02f, casualCap = 0.08f },
            new UpgradeTuning { stat = UpgradeStat.Regeneration, label = "Regeneration", bonusPerLevel = 0.06f, casualCap = 0.25f },
            new UpgradeTuning { stat = UpgradeStat.Speed, label = "Speed", bonusPerLevel = 0.01f, casualCap = 0.04f },
            new UpgradeTuning { stat = UpgradeStat.Reload, label = "Reload", bonusPerLevel = 0.025f, casualCap = 0.10f },
            new UpgradeTuning { stat = UpgradeStat.SpecialEfficiency, label = "Special efficiency", bonusPerLevel = 0.04f, casualCap = 0.15f },
        };

        [Header("Competitive matches")]
        [Tooltip("Competitivo: as melhorias de atributos ficam desativadas (todos com os valores base).")]
        public bool competitiveDisablesUpgrades = true;
        [Tooltip("Competitivo: as habilidades dos pets ficam desativadas (os pets são só cosméticos).")]
        public bool competitiveDisablesPetAbilities = true;

        [Header("Recompensas por nível de jogador")]
        public List<LevelReward> levelRewards = new List<LevelReward>();

        public UpgradeTuning Tuning(UpgradeStat stat)
        {
            foreach (var u in upgrades) if (u != null && u.stat == stat) return u;
            return new UpgradeTuning { stat = stat, label = stat.ToString(), bonusPerLevel = 0f, casualCap = 0f };
        }

        /// <summary>Custo do próximo nível de melhoria (de 'currentLevel' para +1). -1 se já está no máximo.</summary>
        public int UpgradeCost(int currentLevel)
        {
            if (currentLevel >= maxUpgradeLevel || currentLevel < 0) return -1;
            if (upgradeCosts == null || upgradeCosts.Length == 0) return 100 * (currentLevel + 1);
            return upgradeCosts[Mathf.Min(currentLevel, upgradeCosts.Length - 1)];
        }

        /// <summary>Nível de personagem exigido para ter a melhoria no nível 'upgradeLevel'.</summary>
        public int CharacterLevelRequired(int upgradeLevel)
        {
            if (upgradeLevel <= 0) return 1;
            return (upgradeLevel - 1) * Mathf.Max(1, characterLevelsPerUpgradeLevel) + 1;
        }

        /// <summary>Nível de melhoria máximo permitido com este nível de personagem.</summary>
        public int MaxUpgradeLevelFor(int characterLevel)
        {
            int max = 0;
            for (int l = 1; l <= maxUpgradeLevel; l++)
                if (characterLevel >= CharacterLevelRequired(l)) max = l;
            return max;
        }

        public RewardBundle RewardForLevel(int level)
        {
            foreach (var r in levelRewards) if (r != null && r.level == level) return r.reward;
            return null;
        }

        /// <summary>Instância em memória com os valores predefinidos (quando o asset não existe).</summary>
        public static ProgressionConfig CreateDefault()
        {
            var c = CreateInstance<ProgressionConfig>();
            c.name = "ProgressionConfig (predefinido)";
            GameExperienceDefaults.FillLevelRewards(c);
            return c;
        }
    }
}
