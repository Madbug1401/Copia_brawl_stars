using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    public enum QuestPeriod : byte
    {
        /// <summary>Recomeça todos os dias (00:00 UTC).</summary>
        Daily,
        /// <summary>Recomeça todas as segundas-feiras (00:00 UTC).</summary>
        Weekly,
        /// <summary>Conquista permanente (uma vez por perfil).</summary>
        Achievement,
        /// <summary>Desafio permanente (uma vez por perfil).</summary>
        Challenge,
        /// <summary>Evento: só conta entre as datas de início e fim.</summary>
        Event,
    }

    /// <summary>O que uma missão conta.</summary>
    public enum QuestMetric : byte
    {
        MatchesPlayed,
        Eliminations,
        Wins,
        MatchesWithPet,
        DistinctBrawlers,
        Headshots,
        Assists,
        DamageDealt,
        Objectives,
        /// <summary>Chegar ao nível de jogador <c>target</c>.</summary>
        PlayerLevel,
        /// <summary>Ter um pet no nível <c>target</c>.</summary>
        PetLevel,
        /// <summary>Ter <c>target</c> pets desbloqueados.</summary>
        PetsUnlocked,
    }

    [Serializable]
    public class QuestDefinition
    {
        [Tooltip("Id estável (gravações). Nunca mudar depois de publicado.")]
        public string questId = "quest";
        public string title = "Missão";
        public string description = "";
        public QuestPeriod period = QuestPeriod.Daily;
        public QuestMetric metric = QuestMetric.MatchesPlayed;
        [Min(1)] public int target = 1;
        [Tooltip("Opcional: só conta partidas com este Brawler (brawlerId).")]
        public string requiredBrawlerId = "";
        public List<RewardEntry> rewards = new List<RewardEntry>();
        [Tooltip("Eventos: início e fim em UTC, formato yyyy-MM-dd.")]
        public string eventStartUtc = "";
        public string eventEndUtc = "";

        public QuestDefinition() { }
        public QuestDefinition(string id, string title, QuestPeriod period, QuestMetric metric, int target, params RewardEntry[] rewards)
        {
            questId = id;
            this.title = title;
            this.period = period;
            this.metric = metric;
            this.target = target;
            this.rewards = new List<RewardEntry>(rewards);
        }

        public bool IsRepeating => period == QuestPeriod.Daily || period == QuestPeriod.Weekly;
    }
}
