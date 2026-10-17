using System;
using System.Globalization;

namespace FPSLAN
{
    /// <summary>
    /// Missões diárias e semanais, conquistas, desafios e eventos. O progresso recomeça no início de cada período
    /// (00:00 UTC; semanas a começar à segunda-feira). A recompensa de cada período só pode ser recolhida uma vez.
    /// </summary>
    public sealed class QuestManager
    {
        readonly ExperienceContext m_Ctx;
        readonly RewardManager m_Rewards;

        /// <summary>Valores "de estado" (nível de jogador, melhor nível de pet, pets desbloqueados).</summary>
        public Func<QuestMetric, int> StateProvider;

        public QuestManager(ExperienceContext ctx, RewardManager rewards)
        {
            m_Ctx = ctx;
            m_Rewards = rewards;
        }

        /// <summary>Chave do período atual de uma missão (vazia = evento fora de datas).</summary>
        public string PeriodKey(QuestDefinition q) => PeriodKey(q, m_Ctx.utcNow());

        public static string PeriodKey(QuestDefinition q, DateTime utcNow)
        {
            var day = utcNow.Date;
            switch (q.period)
            {
                case QuestPeriod.Daily: return "D" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case QuestPeriod.Weekly:
                    int offset = ((int)day.DayOfWeek + 6) % 7; // segunda-feira = 0
                    return "W" + day.AddDays(-offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case QuestPeriod.Event:
                    if (!TryDate(q.eventStartUtc, out var start) || !TryDate(q.eventEndUtc, out var end)) return "";
                    if (day < start || day > end) return "";
                    return "E" + q.eventStartUtc;
                default: return "P";
            }
        }

        static bool TryDate(string s, out DateTime date) =>
            DateTime.TryParseExact(s ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);

        public bool IsActive(QuestDefinition q) => !string.IsNullOrEmpty(PeriodKey(q));

        /// <summary>Próximo recomeço (para mostrar "termina em ...").</summary>
        public static DateTime NextReset(QuestPeriod period, DateTime utcNow)
        {
            var day = utcNow.Date;
            if (period == QuestPeriod.Daily) return day.AddDays(1);
            if (period == QuestPeriod.Weekly) return day.AddDays(7 - ((int)day.DayOfWeek + 6) % 7);
            return DateTime.MaxValue;
        }

        /// <summary>Progresso da missão no período atual (cria/recomeça quando o período muda).</summary>
        public QuestProgressData Progress(QuestDefinition q)
        {
            var data = m_Ctx.profile.Quest(q.questId, true);
            string key = PeriodKey(q);
            if (data.periodKey != key)
            {
                // Período novo: recomeça (o que estava por recolher do período anterior perde-se, como num jogo live)
                data.periodKey = key;
                data.progress = 0;
                data.completed = false;
                data.claimed = false;
                data.distinct.Clear();
                m_Ctx.MarkDirty();
            }
            return data;
        }

        public void Refresh()
        {
            foreach (var q in m_Ctx.catalog.quests)
                if (q != null) Progress(q);
            RefreshStateQuests();
        }

        /// <summary>Atualiza missões de estado (nível, pets) — chamar depois de mudanças no perfil.</summary>
        public void RefreshStateQuests()
        {
            if (StateProvider == null) return;
            foreach (var q in m_Ctx.catalog.quests)
            {
                if (q == null || !IsActive(q)) continue;
                if (q.metric != QuestMetric.PlayerLevel && q.metric != QuestMetric.PetLevel && q.metric != QuestMetric.PetsUnlocked) continue;
                var data = Progress(q);
                if (data.completed) continue;
                SetProgress(q, data, StateProvider(q.metric));
            }
        }

        public void OnMatch(MatchResultData result)
        {
            var s = result.stats;
            if (!s.valid) return;
            foreach (var q in m_Ctx.catalog.quests)
            {
                if (q == null || !IsActive(q)) continue;
                if (!string.IsNullOrEmpty(q.requiredBrawlerId) && q.requiredBrawlerId != result.brawlerId) continue;
                var data = Progress(q);
                if (data.completed) continue;
                switch (q.metric)
                {
                    case QuestMetric.MatchesPlayed: Add(q, data, 1); break;
                    case QuestMetric.Eliminations: Add(q, data, s.kills); break;
                    case QuestMetric.Wins: if (s.victory) Add(q, data, 1); break;
                    case QuestMetric.MatchesWithPet: if (s.usedPet) Add(q, data, 1); break;
                    case QuestMetric.Headshots: Add(q, data, s.headshots); break;
                    case QuestMetric.Assists: Add(q, data, s.assists); break;
                    case QuestMetric.DamageDealt: Add(q, data, s.damage); break;
                    case QuestMetric.Objectives: Add(q, data, s.objectives); break;
                    case QuestMetric.DistinctBrawlers:
                        if (!string.IsNullOrEmpty(result.brawlerId) && !data.distinct.Contains(result.brawlerId))
                        {
                            data.distinct.Add(result.brawlerId);
                            SetProgress(q, data, data.distinct.Count);
                        }
                        break;
                }
            }
            RefreshStateQuests();
        }

        void Add(QuestDefinition q, QuestProgressData data, int amount)
        {
            if (amount <= 0) return;
            SetProgress(q, data, (int)Math.Min(int.MaxValue, (long)data.progress + amount));
        }

        void SetProgress(QuestDefinition q, QuestProgressData data, int value)
        {
            int clamped = Math.Max(0, Math.Min(value, q.target));
            if (clamped == data.progress && data.completed == (clamped >= q.target)) return;
            data.progress = clamped;
            m_Ctx.MarkDirty();
            if (!data.completed && data.progress >= q.target)
            {
                data.completed = true;
                m_Ctx.Raise(ExperienceEventType.QuestCompleted, "Missão concluída!", q.title, q.questId);
            }
        }

        public bool CanClaim(QuestDefinition q)
        {
            if (q == null || !IsActive(q)) return false;
            var data = Progress(q);
            return data.completed && !data.claimed;
        }

        /// <summary>Recolhe a recompensa (uma vez por período). Devolve false se não houver nada a recolher.</summary>
        public bool Claim(string questId)
        {
            var q = m_Ctx.catalog.Quest(questId);
            if (!CanClaim(q)) return false;
            var data = Progress(q);
            data.claimed = true;
            m_Ctx.MarkDirty();
            bool granted = m_Rewards.GrantAll(q.rewards, "quest:" + q.questId + ":" + data.periodKey);
            if (granted) m_Ctx.Raise(ExperienceEventType.RewardClaimed, "Recompensa recolhida", q.title, q.questId);
            return granted;
        }

        public int ClaimableCount
        {
            get
            {
                int n = 0;
                foreach (var q in m_Ctx.catalog.quests)
                    if (CanClaim(q)) n++;
                return n;
            }
        }
    }
}
