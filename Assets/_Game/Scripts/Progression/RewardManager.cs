using System.Collections.Generic;

namespace FPSLAN
{
    /// <summary>
    /// Entrega recompensas (XP, moedas, pets, skins, cosméticos). Cada recompensa única tem uma chave: se a chave já
    /// foi entregue, nada acontece (sem duplicados por repetições, reconexões ou cliques repetidos).
    /// Itens que o jogador já tem são trocados por moedas.
    /// </summary>
    public sealed class RewardManager
    {
        public const int MaxGrantedKeys = 4000;

        readonly ExperienceContext m_Ctx;
        readonly InventoryManager m_Inventory;

        /// <summary>Ligado pelo GameExperience (XP de jogador passa pela progressão para tratar subidas de nível).</summary>
        public PlayerProgressionManager Progression { get; set; }

        public RewardManager(ExperienceContext ctx, InventoryManager inventory)
        {
            m_Ctx = ctx;
            m_Inventory = inventory;
        }

        public bool WasGranted(string key) => !string.IsNullOrEmpty(key) && m_Ctx.profile.grantedRewardKeys.Contains(key);

        /// <summary>Entrega uma lista de recompensas com uma chave única. Devolve false se já tinha sido entregue.</summary>
        public bool GrantAll(IList<RewardEntry> rewards, string uniqueKey)
        {
            if (WasGranted(uniqueKey)) return false;
            MarkGranted(uniqueKey);
            if (rewards != null)
                foreach (var r in rewards) Grant(r, null);
            return true;
        }

        /// <summary>Entrega uma recompensa. Com chave: só uma vez por perfil.</summary>
        public bool Grant(RewardEntry reward, string uniqueKey)
        {
            if (reward == null) return false;
            if (WasGranted(uniqueKey)) return false;
            if (!string.IsNullOrEmpty(uniqueKey)) MarkGranted(uniqueKey);

            var catalog = m_Ctx.catalog;
            switch (reward.type)
            {
                case RewardType.PlayerXp:
                    if (Progression != null) Progression.AddPlayerXp(reward.amount);
                    return true;

                case RewardType.Coins:
                    if (reward.amount <= 0) return false;
                    m_Inventory.AddCoins(reward.amount);
                    m_Ctx.Raise(ExperienceEventType.CoinsGained, "+" + reward.amount + " moedas");
                    return true;

                case RewardType.Pet:
                {
                    var pet = catalog.Pet(reward.itemId);
                    if (pet == null) return false;
                    if (m_Inventory.AddPet(reward.itemId))
                    {
                        m_Ctx.Raise(ExperienceEventType.PetUnlocked, "Novo pet: " + pet.displayName, pet.description, pet.petId);
                        return true;
                    }
                    return CompensateDuplicate(pet.displayName);
                }

                case RewardType.BrawlerSkin:
                case RewardType.PetSkin:
                {
                    var skin = catalog.Skin(reward.itemId);
                    if (skin == null) return false;
                    if (m_Inventory.AddSkin(reward.itemId))
                    {
                        m_Ctx.Raise(ExperienceEventType.SkinUnlocked, "Nova skin: " + skin.displayName, SkinDefinition.RarityName(skin.rarity), skin.skinId);
                        return true;
                    }
                    return CompensateDuplicate(skin.displayName);
                }

                case RewardType.Cosmetic:
                {
                    var c = catalog.Cosmetic(reward.itemId);
                    if (c == null) return false;
                    if (m_Inventory.AddCosmetic(reward.itemId))
                    {
                        m_Ctx.Raise(ExperienceEventType.CosmeticUnlocked, "Desbloqueado: " + c.displayName, "", c.cosmeticId);
                        return true;
                    }
                    return CompensateDuplicate(c.displayName);
                }
            }
            return false;
        }

        bool CompensateDuplicate(string name)
        {
            int coins = m_Ctx.catalog.duplicateCoinValue;
            if (coins <= 0) return false;
            m_Inventory.AddCoins(coins);
            m_Ctx.Raise(ExperienceEventType.CoinsGained, "+" + coins + " moedas", name + " já era teu");
            return true;
        }

        void MarkGranted(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var keys = m_Ctx.profile.grantedRewardKeys;
            keys.Add(key);
            if (keys.Count > MaxGrantedKeys) keys.RemoveRange(0, keys.Count - MaxGrantedKeys);
            m_Ctx.MarkDirty();
        }

        public static string Describe(RewardEntry r, ExperienceCatalog catalog)
        {
            if (r == null) return "";
            switch (r.type)
            {
                case RewardType.PlayerXp: return r.amount + " XP";
                case RewardType.Coins: return r.amount + " moedas";
                case RewardType.Pet: return "Pet: " + (catalog?.Pet(r.itemId)?.displayName ?? r.itemId);
                case RewardType.BrawlerSkin:
                case RewardType.PetSkin: return "Skin: " + (catalog?.Skin(r.itemId)?.displayName ?? r.itemId);
                default: return catalog?.Cosmetic(r.itemId)?.displayName ?? r.itemId;
            }
        }
    }
}
