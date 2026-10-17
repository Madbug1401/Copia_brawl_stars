using UnityEngine;

namespace FPSLAN
{
    /// <summary>Dados de uma partida já validados, prontos a creditar no perfil.</summary>
    public struct MatchResultData
    {
        public string matchKey;
        public string brawlerId;
        public string petId;
        public MatchRuleset ruleset;
        public MatchStatLine stats;
        public MatchXpBreakdown xp;
    }

    /// <summary>O que mudou com uma partida (para o ecrã de resultados).</summary>
    public struct MatchProgressReport
    {
        public bool applied;
        public bool duplicate;
        public MatchResultData result;
        public LevelChange player;
        public LevelChange brawler;
        public LevelChange pet;
        public int coins;
    }

    /// <summary>
    /// Nível de jogador (1–50) e nível de cada Brawler (1–10). Credita partidas uma única vez por chave e
    /// limita os valores recebidos aos máximos das regras (nunca confia cegamente na mensagem).
    /// </summary>
    public sealed class PlayerProgressionManager
    {
        public const int MaxProcessedMatches = 200;

        readonly ExperienceContext m_Ctx;
        readonly RewardManager m_Rewards;

        public PetProgressionManager Pets { get; set; }
        public QuestManager Quests { get; set; }
        /// <summary>Chamado depois de subir de nível (desbloqueios automáticos de pets/skins).</summary>
        public System.Action<int> OnPlayerLevelChanged;

        public PlayerProgressionManager(ExperienceContext ctx, RewardManager rewards)
        {
            m_Ctx = ctx;
            m_Rewards = rewards;
        }

        ProgressionSettings S => m_Ctx.Settings;
        PlayerProfileData Profile => m_Ctx.profile;

        public int PlayerLevel => S.playerCurve.LevelForTotal(Profile.playerXp);
        public int PlayerXp => Profile.playerXp;

        public int BrawlerXp(string brawlerId) => Profile.Brawler(brawlerId, false)?.xp ?? 0;
        public int BrawlerLevel(string brawlerId) => S.brawlerCurve.LevelForTotal(BrawlerXp(brawlerId));

        public bool WasProcessed(string matchKey) => !string.IsNullOrEmpty(matchKey) && Profile.processedMatches.Contains(matchKey);

        public LevelChange AddPlayerXp(int amount)
        {
            int total = Profile.playerXp;
            var change = XPManager.AddXp(S.playerCurve, ref total, amount);
            Profile.playerXp = total;
            m_Ctx.MarkDirty();
            for (int level = change.oldLevel + 1; level <= change.newLevel; level++) OnReachedLevel(level);
            if (change.LeveledUp) OnPlayerLevelChanged?.Invoke(change.newLevel);
            return change;
        }

        void OnReachedLevel(int level)
        {
            m_Ctx.Raise(ExperienceEventType.PlayerLevelUp, "Nível " + level + "!", "Subiste de nível de jogador");
            if (S.coinsPerPlayerLevel > 0) m_Rewards.Grant(new RewardEntry(RewardType.Coins, S.coinsPerPlayerLevel), "plevelcoins:" + level);
            foreach (var lr in S.playerLevelRewards)
                if (lr != null && lr.level == level && !Profile.claimedPlayerLevelRewards.Contains(level))
                    m_Rewards.Grant(lr.reward, "plevel:" + level + ":" + lr.reward.type + ":" + lr.reward.itemId);
            if (!Profile.claimedPlayerLevelRewards.Contains(level)) Profile.claimedPlayerLevelRewards.Add(level);
        }

        public LevelChange AddBrawlerXp(string brawlerId, int amount)
        {
            var data = Profile.Brawler(brawlerId, true);
            if (data == null) return default;
            int total = data.xp;
            var change = XPManager.AddXp(S.brawlerCurve, ref total, amount);
            data.xp = total;
            m_Ctx.MarkDirty();
            if (change.LeveledUp)
                m_Ctx.Raise(ExperienceEventType.BrawlerLevelUp, "Brawler nível " + change.newLevel, "Ganhaste pontos de melhoria", brawlerId);
            return change;
        }

        /// <summary>Limita os valores de uma recompensa recebida aos máximos possíveis pelas regras.</summary>
        public MatchXpBreakdown Sanitize(MatchXpBreakdown xp, MatchStatLine stats)
        {
            if (!stats.valid) return default;
            int maxTotal = XPManager.MaxMatchXp(S);
            xp.participation = Mathf.Clamp(xp.participation, 0, S.xp.participation);
            xp.kills = Mathf.Clamp(xp.kills, 0, S.xp.maxKillsCounted * S.xp.kill);
            xp.assists = Mathf.Clamp(xp.assists, 0, S.xp.maxKillsCounted * S.xp.assist);
            xp.victory = Mathf.Clamp(xp.victory, 0, S.xp.victory);
            xp.objectives = Mathf.Clamp(xp.objectives, 0, S.xp.maxObjectivesCounted * S.xp.objective);
            xp.total = Mathf.Clamp(xp.participation + xp.kills + xp.assists + xp.victory + xp.objectives, 0, maxTotal);
            xp.brawlerXp = Mathf.Clamp(xp.brawlerXp, 0, Mathf.CeilToInt(maxTotal * Mathf.Max(0f, S.brawlerXpShare)));
            int maxPet = S.petXp.participation + S.petXp.victory + S.xp.maxKillsCounted * S.petXp.perKill;
            xp.petXp = stats.usedPet ? Mathf.Clamp(xp.petXp, 0, maxPet) : 0;
            xp.coins = Mathf.Clamp(xp.coins, 0, S.coins.maxPerMatch);
            return xp;
        }

        /// <summary>Credita uma partida (uma vez por chave). Devolve o que mudou.</summary>
        public MatchProgressReport ApplyMatch(MatchResultData result)
        {
            var report = new MatchProgressReport { result = result };
            if (string.IsNullOrEmpty(result.matchKey)) return report;
            if (WasProcessed(result.matchKey))
            {
                report.duplicate = true;
                return report;
            }
            Profile.processedMatches.Add(result.matchKey);
            if (Profile.processedMatches.Count > MaxProcessedMatches)
                Profile.processedMatches.RemoveRange(0, Profile.processedMatches.Count - MaxProcessedMatches);
            m_Ctx.MarkDirty();

            var stats = result.stats;
            result.xp = Sanitize(result.xp, stats);
            report.result = result;
            report.applied = true;

            if (stats.valid)
            {
                var st = Profile.stats;
                st.matches++;
                if (stats.victory) st.wins++;
                st.kills += Mathf.Max(0, stats.kills);
                st.deaths += Mathf.Max(0, stats.deaths);
                st.assists += Mathf.Max(0, stats.assists);
                st.headshots += Mathf.Max(0, stats.headshots);
                st.damage += Mathf.Max(0, stats.damage);

                bool knownBrawler = m_Ctx.catalog.HasBrawler(result.brawlerId);
                if (knownBrawler) Profile.Brawler(result.brawlerId, true).matchesPlayed++;

                report.player = AddPlayerXp(result.xp.total);
                if (knownBrawler) report.brawler = AddBrawlerXp(result.brawlerId, result.xp.brawlerXp);
                if (Pets != null && stats.usedPet && !string.IsNullOrEmpty(result.petId))
                    report.pet = Pets.AddXp(result.petId, result.xp.petXp);
                if (result.xp.coins > 0)
                {
                    m_Rewards.Grant(new RewardEntry(RewardType.Coins, result.xp.coins), "matchcoins:" + result.matchKey);
                    report.coins = result.xp.coins;
                }
            }

            Quests?.OnMatch(result);
            return report;
        }
    }
}
