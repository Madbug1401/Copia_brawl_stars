using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// XP global do jogador e XP individual de cada personagem: cálculo das recompensas de uma partida (no host)
    /// e aplicação ao perfil (no cliente), com subidas de nível graduais (curvas da ProgressionConfig).
    /// </summary>
    public class XPManager
    {
        readonly GameExperienceContext m_Ctx;

        public XPManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        ProgressionConfig Cfg => m_Ctx.Config;

        // ================================================================== cálculo (host)

        /// <summary>A participação conta? (tempo mínimo em partida e alguma atividade).</summary>
        public static bool IsParticipationValid(ProgressionConfig cfg, float secondsPlayed, float matchSeconds, int damageDealt, float distanceMoved)
        {
            if (cfg == null) return false;
            float needed = Mathf.Min(cfg.minParticipationSeconds, Mathf.Max(5f, matchSeconds * cfg.minParticipationFraction));
            bool timeOk = secondsPlayed >= needed;
            bool active = damageDealt > 0 || distanceMoved >= cfg.minDistanceMoved;
            return timeOk && active;
        }

        /// <summary>Preenche o XP, as moedas e o XP do pet de um resultado (determinístico a partir da configuração).</summary>
        public static void ComputeRewards(ProgressionConfig cfg, ref MatchResultData r)
        {
            r.xpParticipation = r.xpKills = r.xpAssists = r.xpVictory = r.xpObjectives = 0;
            r.coins = 0;
            r.petXp = 0;
            if (cfg == null || !r.participated) return;

            int kills = Mathf.Max(0, (int)r.kills);
            int killsBots = Mathf.Clamp(r.killsOnBots, 0, kills);
            int assists = Mathf.Max(0, (int)r.assists);
            int assistsBots = Mathf.Clamp(r.assistsOnBots, 0, assists);

            // Limites anti-farm: contam primeiro as eliminações sobre humanos
            int killCap = Mathf.Max(0, cfg.maxKillsCounted);
            int humanKills = Mathf.Min(kills - killsBots, killCap);
            int botKills = Mathf.Min(killsBots, killCap - humanKills);
            int assistCap = Mathf.Max(0, cfg.maxAssistsCounted);
            int humanAssists = Mathf.Min(assists - assistsBots, assistCap);
            int botAssists = Mathf.Min(assistsBots, assistCap - humanAssists);
            int objectives = Mathf.Clamp(r.objectives, 0, cfg.maxObjectivesCounted);

            r.xpParticipation = cfg.xpParticipation;
            r.xpKills = humanKills * cfg.xpPerKill + Mathf.RoundToInt(botKills * cfg.xpPerKill * cfg.xpVsBotsMultiplier);
            r.xpAssists = humanAssists * cfg.xpPerAssist + Mathf.RoundToInt(botAssists * cfg.xpPerAssist * cfg.xpVsBotsMultiplier);
            r.xpVictory = r.won && !r.draw ? cfg.xpVictory : 0;
            r.xpObjectives = objectives * cfg.xpPerObjective;

            r.coins = cfg.coinsParticipation + (r.won && !r.draw ? cfg.coinsVictory : 0) +
                      (humanKills + botKills) * cfg.coinsPerKill + objectives * cfg.coinsPerObjective;

            if (r.petHash != 0)
                r.petXp = cfg.petXpPerValidMatch + (humanKills + botKills) * cfg.petXpPerKill + (r.won && !r.draw ? cfg.petXpVictoryBonus : 0);
        }

        // ================================================================== aplicação (perfil local)

        public int PlayerLevel => Cfg.playerLevels.LevelForXp(m_Ctx.Profile.playerXp);

        /// <summary>Soma XP ao jogador. Cada nível novo fica com a recompensa pendente (ecrã Recompensas). Devolve os níveis ganhos.</summary>
        public int AddPlayerXp(int amount, string source)
        {
            if (amount <= 0) return 0;
            var curve = Cfg.playerLevels;
            int before = curve.LevelForXp(m_Ctx.Profile.playerXp);
            m_Ctx.Profile.playerXp = System.Math.Min(curve.MaxTotalXp, m_Ctx.Profile.playerXp + amount);
            int after = curve.LevelForXp(m_Ctx.Profile.playerXp);
            for (int l = before + 1; l <= after; l++)
            {
                var reward = Cfg.RewardForLevel(l);
                if (reward != null && !reward.IsEmpty && !m_Ctx.Rewards.IsClaimed("level:" + l) && !m_Ctx.Profile.pendingLevelRewards.Contains(l))
                    m_Ctx.Profile.pendingLevelRewards.Add(l);
                m_Ctx.Emit(FeedbackType.LevelUp, "NÍVEL " + l + "!", reward != null && !reward.IsEmpty ? "Recompensa disponível" : "Subiste de nível", new Color(1f, 0.85f, 0.25f));
            }
            if (after > before)
            {
                // Novos níveis podem desbloquear pets, skins e conquistas
                m_Ctx.PetUnlocks.CheckAll();
                m_Ctx.Skins.CheckRuleUnlocks();
                m_Ctx.Quests.CheckAchievements();
            }
            return after - before;
        }

        public int CharacterLevel(string brawlerId)
        {
            var c = m_Ctx.Profile.Character(brawlerId, false);
            return Cfg.characterLevels.LevelForXp(c != null ? c.xp : 0);
        }

        /// <summary>Soma XP a uma personagem. Devolve os níveis ganhos.</summary>
        public int AddCharacterXp(string brawlerId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(brawlerId)) return 0;
            var c = m_Ctx.Profile.Character(brawlerId, true);
            var curve = Cfg.characterLevels;
            int before = curve.LevelForXp(c.xp);
            c.xp = System.Math.Min(curve.MaxTotalXp, c.xp + amount);
            int after = curve.LevelForXp(c.xp);
            if (after > before)
            {
                var b = BrawlerRegistry.FindById(brawlerId);
                m_Ctx.Emit(FeedbackType.CharacterLevelUp, (b != null ? b.displayName.ToUpperInvariant() : brawlerId) + " · NÍVEL " + after,
                    "Novas melhorias disponíveis", b != null ? Color.Lerp(b.Tint, Color.white, 0.3f) : Color.white);
            }
            return after - before;
        }
    }
}
