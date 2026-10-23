using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Atribui pacotes de recompensas de forma idempotente: cada recompensa tem uma chave única
    /// (ex.: "quest:daily_play3:D:2026-10-17", "level:10", "ach:ach_assists25"); uma chave já reclamada é ignorada,
    /// por isso reconexões, mensagens repetidas ou cliques duplos não dão recompensas a dobrar.
    /// </summary>
    public class RewardManager
    {
        readonly GameExperienceContext m_Ctx;

        public RewardManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public bool IsClaimed(string key) => !string.IsNullOrEmpty(key) && m_Ctx.Profile.claimedRewardKeys.Contains(key);

        /// <summary>Concede 'bundle' uma única vez por 'key'. Devolve false se a chave já tinha sido usada.</summary>
        public bool Grant(string key, RewardBundle bundle, string source)
        {
            if (string.IsNullOrEmpty(key) || IsClaimed(key)) return false;
            m_Ctx.Profile.claimedRewardKeys.Add(key);
            if (bundle == null || bundle.IsEmpty) return true;

            var inv = m_Ctx.Inventory;
            if (bundle.coins > 0)
            {
                inv.AddCoins(bundle.coins);
                m_Ctx.Emit(FeedbackType.CoinsEarned, "+" + bundle.coins + " COINS", source, new Color(1f, 0.85f, 0.25f));
            }
            if (bundle.petIds != null)
                foreach (var id in bundle.petIds)
                    if (inv.UnlockPet(id))
                    {
                        var def = m_Ctx.PetCatalog.Find(id);
                        m_Ctx.Emit(FeedbackType.PetUnlocked, "NEW PET!", def != null ? def.displayName : id, new Color(0.5f, 1f, 0.6f));
                    }
            if (bundle.skinIds != null)
                foreach (var id in bundle.skinIds)
                    if (inv.UnlockSkin(id))
                    {
                        var def = m_Ctx.SkinCatalog.Find(id);
                        m_Ctx.Emit(FeedbackType.SkinUnlocked, "NEW SKIN!", def != null ? def.displayName : id,
                            def != null ? RarityStyle.ColorOf(def.rarity) : Color.white);
                    }
            if (bundle.cosmeticIds != null)
                foreach (var id in bundle.cosmeticIds)
                    if (inv.UnlockCosmetic(id))
                        m_Ctx.Emit(FeedbackType.RewardClaimed, "NEW COSMETIC", id, new Color(0.8f, 0.7f, 1f));
            // XP de recompensa entra pelo caminho normal (pode subir de nível)
            if (bundle.xp > 0) m_Ctx.Progression.AddPlayerXp(bundle.xp, source);

            m_Ctx.Log(source, bundle.Describe(m_Ctx.SkinCatalog, m_Ctx.PetCatalog));
            return true;
        }

        // ------------------------------------------------------------------ recompensas de nível (reclamar no ecrã de recompensas)

        public bool HasPendingLevelRewards => m_Ctx.Profile.pendingLevelRewards.Count > 0;

        public bool ClaimLevelReward(int level)
        {
            if (!m_Ctx.Profile.pendingLevelRewards.Contains(level)) return false;
            m_Ctx.Profile.pendingLevelRewards.Remove(level);
            var bundle = m_Ctx.Config.RewardForLevel(level);
            bool granted = Grant("level:" + level, bundle, "Level " + level);
            if (granted) m_Ctx.Emit(FeedbackType.RewardClaimed, "LEVEL REWARD " + level, bundle != null ? bundle.Describe(m_Ctx.SkinCatalog, m_Ctx.PetCatalog) : "", new Color(1f, 0.85f, 0.3f));
            m_Ctx.PetUnlocks.CheckAll();
            m_Ctx.Skins.CheckRuleUnlocks();
            m_Ctx.Save();
            return granted;
        }

        public int ClaimAllLevelRewards()
        {
            int n = 0;
            var pending = m_Ctx.Profile.pendingLevelRewards.ToArray();
            foreach (var level in pending)
                if (ClaimLevelReward(level)) n++;
            return n;
        }
    }
}
