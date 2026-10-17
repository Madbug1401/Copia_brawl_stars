namespace FPSLAN
{
    /// <summary>
    /// XP e níveis dos pets (1–10), fases visuais (1–3 jovem, 4–7 desenvolvido, 8–10 evoluído) e recompensas
    /// de nível (3, 5, 8, 10), entregues uma única vez.
    /// </summary>
    public sealed class PetProgressionManager
    {
        readonly ExperienceContext m_Ctx;
        readonly RewardManager m_Rewards;

        public PetProgressionManager(ExperienceContext ctx, RewardManager rewards)
        {
            m_Ctx = ctx;
            m_Rewards = rewards;
        }

        LevelCurve Curve => m_Ctx.Settings.petCurve;

        public int Xp(string petId) => m_Ctx.profile.Pet(petId, false)?.xp ?? 0;
        public int Level(string petId) => Curve.LevelForTotal(Xp(petId));
        public int Stage(string petId) => PetDefinition.StageForLevel(Level(petId));
        public float LevelProgress(string petId) => XPManager.LevelProgress(Curve, Xp(petId));

        public int BestLevel
        {
            get
            {
                int best = 0;
                foreach (var p in m_Ctx.profile.pets)
                    if (p != null && p.unlocked) best = System.Math.Max(best, Curve.LevelForTotal(p.xp));
                return best;
            }
        }

        /// <summary>Soma XP a um pet desbloqueado. Trata subidas de nível, evolução e recompensas.</summary>
        public LevelChange AddXp(string petId, int amount)
        {
            var rules = m_Ctx.catalog.Pet(petId);
            var data = m_Ctx.profile.Pet(petId, false);
            if (rules == null || data == null || !data.unlocked || amount <= 0) return default;

            int total = data.xp;
            var change = XPManager.AddXp(Curve, ref total, amount);
            data.xp = total;
            m_Ctx.MarkDirty();

            for (int level = change.oldLevel + 1; level <= change.newLevel; level++)
            {
                m_Ctx.Raise(ExperienceEventType.PetLevelUp, rules.displayName + " nível " + level, "", petId);
                foreach (var r in rules.levelRewards)
                    if (r != null && r.level == level && !data.claimedLevels.Contains(level))
                        m_Rewards.Grant(r.reward, "pet:" + petId + ":" + level + ":" + r.reward.type + ":" + r.reward.itemId);
                if (!data.claimedLevels.Contains(level)) data.claimedLevels.Add(level);
            }

            int oldStage = PetDefinition.StageForLevel(change.oldLevel);
            int newStage = PetDefinition.StageForLevel(change.newLevel);
            if (newStage > oldStage)
                m_Ctx.Raise(ExperienceEventType.PetEvolved, rules.displayName + " evoluiu!", "Fase: " + PetDefinition.StageName(newStage), petId);
            return change;
        }

        /// <summary>O jogador ainda não viu a animação da nova fase.</summary>
        public bool HasUnseenEvolution(string petId)
        {
            var data = m_Ctx.profile.Pet(petId, false);
            return data != null && data.unlocked && data.seenStage < Stage(petId);
        }

        public void MarkEvolutionSeen(string petId)
        {
            var data = m_Ctx.profile.Pet(petId, false);
            if (data == null) return;
            int stage = Stage(petId);
            if (data.seenStage != stage)
            {
                data.seenStage = stage;
                m_Ctx.MarkDirty();
            }
        }
    }
}
