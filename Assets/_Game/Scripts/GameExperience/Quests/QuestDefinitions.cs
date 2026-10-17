using System;
using UnityEngine;

namespace FPSLAN
{
    public enum QuestPeriod
    {
        Daily,
        Weekly,
    }

    /// <summary>O que uma missão/conquista conta (sempre a partir de resultados validados pelo host).</summary>
    public enum QuestMetric
    {
        MatchesPlayed,
        Eliminations,
        MatchesWithPet,
        Wins,
        DistinctBrawlers,
        Assists,
        Headshots,
        Objectives,
        DamageDealt,
        /// <summary>Partidas válidas terminadas sem ser eliminado.</summary>
        FlawlessMatches,
        PlayerLevel,
        PetsUnlocked,
        QuestsCompleted,
    }

    /// <summary>Missão diária ou semanal.</summary>
    [Serializable]
    public class QuestDefinition
    {
        [Tooltip("Id estável (ex.: daily_play3).")]
        public string questId = "quest";
        public string title = "";
        public QuestPeriod period = QuestPeriod.Daily;
        public QuestMetric metric = QuestMetric.MatchesPlayed;
        [Min(1)] public int target = 1;
        public RewardBundle reward = new RewardBundle();
    }

    /// <summary>Conquista permanente (ou desafio especial) que pode atribuir recompensas e desbloquear pets.</summary>
    [Serializable]
    public class AchievementDefinition
    {
        [Tooltip("Id estável (ex.: ach_assists25).")]
        public string achievementId = "achievement";
        public string title = "";
        public string description = "";
        [Tooltip("Desafio especial (aparece separado das conquistas).")]
        public bool isChallenge;
        public QuestMetric metric = QuestMetric.Eliminations;
        [Min(1)] public int target = 1;
        public RewardBundle reward = new RewardBundle();
    }

    /// <summary>Personagem com regra de desbloqueio (preparado para personagens futuras; as 6 atuais são Default).</summary>
    [Serializable]
    public class CharacterUnlockDefinition
    {
        public string brawlerId = "";
        public UnlockRule unlock = new UnlockRule();
    }
}
