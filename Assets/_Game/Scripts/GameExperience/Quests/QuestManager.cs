using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Missões diárias/semanais e conquistas/desafios. O progresso só vem de resultados validados pelo host;
    /// cada recompensa tem a chave missão+período, por isso nunca é atribuída duas vezes.
    /// Períodos em UTC: diário "D:aaaa-mm-dd", semanal "W:aaaa-mm-dd" (segunda-feira da semana).
    /// </summary>
    public class QuestManager
    {
        readonly GameExperienceContext m_Ctx;

        public QuestManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public static string PeriodKey(QuestPeriod period, DateTime utc)
        {
            var d = utc.Date;
            if (period == QuestPeriod.Daily) return "D:" + d.ToString("yyyy-MM-dd");
            int sinceMonday = ((int)d.DayOfWeek + 6) % 7;
            return "W:" + d.AddDays(-sinceMonday).ToString("yyyy-MM-dd");
        }

        public static TimeSpan TimeUntilReset(QuestPeriod period, DateTime utc)
        {
            var d = utc.Date;
            DateTime next;
            if (period == QuestPeriod.Daily) next = d.AddDays(1);
            else
            {
                int sinceMonday = ((int)d.DayOfWeek + 6) % 7;
                next = d.AddDays(7 - sinceMonday);
            }
            return next - utc;
        }

        /// <summary>Estado atual (período corrente) de uma missão.</summary>
        public QuestProgress Progress(QuestDefinition q)
        {
            string key = PeriodKey(q.period, m_Ctx.Clock.UtcNow);
            foreach (var p in m_Ctx.Profile.quests)
                if (p.questId == q.questId && p.periodKey == key) return p;
            var n = new QuestProgress { questId = q.questId, periodKey = key };
            m_Ctx.Profile.quests.Add(n);
            return n;
        }

        /// <summary>
        /// Remove entradas de períodos antigos (as missões recomeçam a cada dia/semana). Uma missão concluída e não
        /// reclamada no período anterior é entregue automaticamente (com a chave desse período: nunca em duplicado).
        /// </summary>
        public void RefreshPeriods()
        {
            var now = m_Ctx.Clock.UtcNow;
            var list = m_Ctx.Profile.quests;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                var def = m_Ctx.QuestCatalog.FindQuest(p.questId);
                if (def != null && p.periodKey == PeriodKey(def.period, now)) continue;
                if (def != null && p.completed && !p.claimed)
                    m_Ctx.Rewards.Grant("quest:" + def.questId + ":" + p.periodKey, def.reward, "Missão: " + def.title);
                list.RemoveAt(i);
            }
            foreach (var q in m_Ctx.QuestCatalog.quests) if (q != null) Progress(q);
        }

        public IEnumerable<QuestDefinition> Active(QuestPeriod period)
        {
            foreach (var q in m_Ctx.QuestCatalog.quests)
                if (q != null && q.period == period) yield return q;
        }

        /// <summary>Atualiza o progresso com um resultado de partida (já validado e sem duplicados).</summary>
        public void OnMatch(MatchResultData r, bool withPet)
        {
            RefreshPeriods();
            if (!r.participated) return;
            string brawlerId = r.brawlerId.ToString();
            foreach (var q in m_Ctx.QuestCatalog.quests)
            {
                if (q == null) continue;
                var p = Progress(q);
                if (p.completed) continue;
                switch (q.metric)
                {
                    case QuestMetric.MatchesPlayed: p.progress += 1; break;
                    case QuestMetric.Eliminations: p.progress += Mathf.Max(0, (int)r.kills); break;
                    case QuestMetric.MatchesWithPet: if (withPet) p.progress += 1; break;
                    case QuestMetric.Wins: if (r.won && !r.draw) p.progress += 1; break;
                    case QuestMetric.Assists: p.progress += Mathf.Max(0, (int)r.assists); break;
                    case QuestMetric.Headshots: p.progress += Mathf.Max(0, (int)r.headshots); break;
                    case QuestMetric.Objectives: p.progress += Mathf.Max(0, (int)r.objectives); break;
                    case QuestMetric.DamageDealt: p.progress += Mathf.Max(0, r.damageDealt); break;
                    case QuestMetric.FlawlessMatches: if (r.deaths == 0) p.progress += 1; break;
                    case QuestMetric.DistinctBrawlers:
                        if (!string.IsNullOrEmpty(brawlerId) && !p.distinct.Contains(brawlerId)) p.distinct.Add(brawlerId);
                        p.progress = p.distinct.Count;
                        break;
                }
                if (p.progress >= q.target)
                {
                    p.progress = q.target;
                    p.completed = true;
                    m_Ctx.Profile.questsCompletedTotal++;
                    m_Ctx.Emit(FeedbackType.QuestCompleted, "MISSÃO CONCLUÍDA!", q.title, new Color(0.45f, 1f, 0.55f));
                }
            }
        }

        public bool CanClaim(QuestDefinition q)
        {
            var p = Progress(q);
            return p.completed && !p.claimed;
        }

        /// <summary>Reclama a recompensa de uma missão concluída (uma vez por período).</summary>
        public bool Claim(QuestDefinition q)
        {
            if (q == null) return false;
            var p = Progress(q);
            if (!p.completed || p.claimed) return false;
            p.claimed = true;
            bool granted = m_Ctx.Rewards.Grant("quest:" + q.questId + ":" + p.periodKey, q.reward, "Missão: " + q.title);
            if (granted) m_Ctx.Emit(FeedbackType.RewardClaimed, "RECOMPENSA RECEBIDA", q.reward.Describe(m_Ctx.SkinCatalog, m_Ctx.PetCatalog), new Color(1f, 0.85f, 0.3f));
            m_Ctx.PetUnlocks.CheckAll();
            m_Ctx.Skins.CheckRuleUnlocks();
            m_Ctx.Save();
            return granted;
        }

        // ------------------------------------------------------------------ conquistas e desafios

        public int AchievementProgress(AchievementDefinition a)
        {
            var s = m_Ctx.Profile.stats;
            switch (a.metric)
            {
                case QuestMetric.MatchesPlayed: return s.validMatches;
                case QuestMetric.Eliminations: return s.kills;
                case QuestMetric.MatchesWithPet: return s.matchesWithPet;
                case QuestMetric.Wins: return s.wins;
                case QuestMetric.Assists: return s.assists;
                case QuestMetric.Headshots: return s.headshots;
                case QuestMetric.Objectives: return s.objectives;
                case QuestMetric.DamageDealt: return (int)Math.Min(int.MaxValue, s.damageDealt);
                case QuestMetric.FlawlessMatches: return s.flawlessMatches;
                case QuestMetric.PlayerLevel: return m_Ctx.Progression.PlayerLevel;
                case QuestMetric.QuestsCompleted: return m_Ctx.Profile.questsCompletedTotal;
                case QuestMetric.PetsUnlocked:
                {
                    int n = 0;
                    foreach (var p in m_Ctx.Profile.pets) if (p.unlocked) n++;
                    return n;
                }
                default: return 0;
            }
        }

        public bool IsAchieved(AchievementDefinition a) => a != null && m_Ctx.Profile.completedAchievements.Contains(a.achievementId);

        /// <summary>Marca as conquistas atingidas e entrega as recompensas (automático, uma vez).</summary>
        public int CheckAchievements()
        {
            int n = 0;
            foreach (var a in m_Ctx.QuestCatalog.achievements)
            {
                if (a == null || IsAchieved(a) || AchievementProgress(a) < a.target) continue;
                m_Ctx.Profile.completedAchievements.Add(a.achievementId);
                n++;
                m_Ctx.Emit(FeedbackType.AchievementUnlocked, a.isChallenge ? "DESAFIO CONCLUÍDO!" : "CONQUISTA!", a.title, new Color(1f, 0.75f, 0.35f));
                m_Ctx.Rewards.Grant("ach:" + a.achievementId, a.reward, (a.isChallenge ? "Desafio: " : "Conquista: ") + a.title);
            }
            if (n > 0)
            {
                m_Ctx.PetUnlocks.CheckAll();
                m_Ctx.Skins.CheckRuleUnlocks();
            }
            return n;
        }
    }
}
