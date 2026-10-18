using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>O que uma partida deu ao jogador (ecrã de resultados pós-partida).</summary>
    public class MatchRewardReport
    {
        public string matchId = "";
        public bool duplicate;
        public bool participated;
        public bool won, draw, competitive;
        public string brawlerId = "";
        public MatchResultData data;

        public int xpGained;
        public int coins;
        public long playerXpBefore, playerXpAfter;
        public int levelBefore, levelAfter;
        public long characterXpBefore, characterXpAfter;
        public int characterLevelBefore, characterLevelAfter;

        public string petId = "";
        public int petXp;
        public int upraFood;
        public int petLevelBefore, petLevelAfter, petStageBefore, petStageAfter;

        /// <summary>Desbloqueios e conquistas desta partida (texto para a interface).</summary>
        public readonly List<FeedbackEvent> unlocks = new List<FeedbackEvent>();
        public float receivedAt;
    }

    /// <summary>
    /// Aplica ao perfil os resultados de partida validados pelo host: XP de jogador, personagem e pet, moedas,
    /// missões, conquistas e desbloqueios. Cada partida (matchId) só é aplicada uma vez.
    /// </summary>
    public class PlayerProgressionManager
    {
        readonly GameExperienceContext m_Ctx;

        public PlayerProgressionManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public MatchRewardReport LastReport { get; private set; }

        ProgressionConfig Cfg => m_Ctx.Config;

        public int PlayerLevel => m_Ctx.XP.PlayerLevel;
        public long PlayerXp => m_Ctx.Profile.playerXp;
        public int CharacterLevel(string brawlerId) => m_Ctx.XP.CharacterLevel(brawlerId);

        public int AddPlayerXp(int amount, string source) => m_Ctx.XP.AddPlayerXp(amount, source);

        public bool IsMatchProcessed(string matchId) => !string.IsNullOrEmpty(matchId) && m_Ctx.Profile.processedMatchIds.Contains(matchId);

        public bool IsCharacterUnlockedByRule(string brawlerId)
        {
            var rule = m_Ctx.QuestCatalog.CharacterRule(brawlerId);
            if (rule == null || rule.method == UnlockMethod.Default) return true;
            if (rule.method == UnlockMethod.PlayerLevel) return PlayerLevel >= rule.value;
            if (rule.method == UnlockMethod.Achievement) return m_Ctx.Profile.completedAchievements.Contains(rule.refId);
            return false;
        }

        /// <summary>Personagens com regra de desbloqueio por nível/conquista (preparado para personagens futuras).</summary>
        public void CheckCharacterUnlocks()
        {
            foreach (var id in m_Ctx.BrawlerIds())
            {
                if (m_Ctx.Inventory.IsCharacterUnlocked(id) || !IsCharacterUnlockedByRule(id)) continue;
                m_Ctx.Inventory.UnlockCharacter(id);
                var b = BrawlerRegistry.FindById(id);
                m_Ctx.Emit(FeedbackType.CharacterUnlocked, "NEW CHARACTER!", b != null ? b.displayName : id, new Color(1f, 0.7f, 0.3f));
            }
        }

        /// <summary>Aplica um resultado do host. Repetições (reinício, reconexão, ecrã de resultados) são ignoradas.</summary>
        public MatchRewardReport ApplyMatchResult(MatchResultData r)
        {
            string matchId = r.matchId.ToString();
            var report = new MatchRewardReport
            {
                matchId = matchId,
                data = r,
                brawlerId = r.brawlerId.ToString(),
                participated = r.participated,
                won = r.won,
                draw = r.draw,
                competitive = r.competitive,
                receivedAt = Time.realtimeSinceStartup,
            };
            var profile = m_Ctx.Profile;
            if (string.IsNullOrEmpty(matchId) || IsMatchProcessed(matchId))
            {
                report.duplicate = true;
                return report;
            }
            profile.processedMatchIds.Add(matchId);

            void Collect(FeedbackEvent e)
            {
                if (e.type != FeedbackType.CoinsEarned) report.unlocks.Add(e);
            }
            m_Ctx.Feedback += Collect;
            try
            {
                Apply(r, report);
            }
            finally
            {
                m_Ctx.Feedback -= Collect;
            }

            m_Ctx.Save();
            LastReport = report;
            return report;
        }

        void Apply(MatchResultData r, MatchRewardReport report)
        {
            var profile = m_Ctx.Profile;
            var stats = profile.stats;
            string brawlerId = report.brawlerId;

            stats.matches++;
            report.playerXpBefore = report.playerXpAfter = profile.playerXp;
            report.levelBefore = report.levelAfter = PlayerLevel;
            var character = profile.Character(brawlerId, !string.IsNullOrEmpty(brawlerId));
            report.characterXpBefore = report.characterXpAfter = character != null ? character.xp : 0;
            report.characterLevelBefore = report.characterLevelAfter = CharacterLevel(brawlerId);
            if (!r.participated) return; // partida não válida: só fica registada (sem XP)

            stats.validMatches++;
            report.upraFood = m_Ctx.PetFood.Add(PetFoodRules.FoodPerMatch);
            stats.kills += Mathf.Max(0, (int)r.kills);
            stats.assists += Mathf.Max(0, (int)r.assists);
            stats.deaths += Mathf.Max(0, (int)r.deaths);
            stats.headshots += Mathf.Max(0, (int)r.headshots);
            stats.objectives += Mathf.Max(0, (int)r.objectives);
            stats.damageDealt += Mathf.Max(0, r.damageDealt);
            if (r.won && !r.draw) stats.wins++;
            if (r.deaths == 0) stats.flawlessMatches++;
            if (r.competitive) stats.competitiveMatches++;
            if (character != null) character.matches++;

            // XP global e da personagem
            int xp = Mathf.Max(0, r.XpTotal);
            report.xpGained = xp;
            m_Ctx.XP.AddPlayerXp(xp, "Match");
            m_Ctx.XP.AddCharacterXp(brawlerId, Mathf.RoundToInt(xp * Cfg.characterXpShare));
            report.playerXpAfter = profile.playerXp;
            report.levelAfter = PlayerLevel;
            report.characterXpAfter = character != null ? character.xp : 0;
            report.characterLevelAfter = CharacterLevel(brawlerId);

            // Moedas
            if (r.coins > 0) m_Ctx.Inventory.AddCoins(r.coins);
            report.coins = Mathf.Max(0, r.coins);

            // Pet: só ganha XP se estiver desbloqueado (o host confirmou que estava equipado na partida)
            var pet = m_Ctx.PetCatalog.FindByHash(r.petHash);
            if (pet != null && m_Ctx.Inventory.IsPetUnlocked(pet.petId))
            {
                stats.matchesWithPet++;
                report.petId = pet.petId;
                report.petXp = Mathf.Max(0, r.petXp);
                report.petLevelBefore = m_Ctx.PetProgression.Level(pet.petId);
                report.petStageBefore = PetDefinition.StageFor(report.petLevelBefore);
                m_Ctx.PetProgression.AddXp(pet.petId, report.petXp);
                var pp = profile.Pet(pet.petId, true);
                pp.matches++;
                report.petLevelAfter = m_Ctx.PetProgression.Level(pet.petId);
                report.petStageAfter = PetDefinition.StageFor(report.petLevelAfter);
            }

            if (r.won && !r.draw) m_Ctx.Emit(FeedbackType.Victory, "VICTORY!", "+" + Cfg.xpVictory + " victory XP", new Color(1f, 0.85f, 0.25f));

            // Missões, conquistas e desbloqueios por regras
            m_Ctx.Quests.OnMatch(r, pet != null);
            m_Ctx.Quests.CheckAchievements();
            m_Ctx.PetUnlocks.CheckAll();
            m_Ctx.Skins.CheckRuleUnlocks();
            CheckCharacterUnlocks();
        }
    }
}
