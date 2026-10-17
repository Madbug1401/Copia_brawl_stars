namespace FPSLAN
{
    /// <summary>Moedas do jogo e itens desbloqueados (personagens, pets, skins, cosméticos).</summary>
    public class InventoryManager
    {
        readonly GameExperienceContext m_Ctx;

        public InventoryManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        ProfileData P => m_Ctx.Profile;

        public int Coins => P.coins;

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            P.coins = System.Math.Min(int.MaxValue - 1, P.coins + amount);
        }

        /// <summary>Gasta moedas se houver saldo. Devolve false (sem alterar nada) se não houver.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || P.coins < amount) return false;
            P.coins -= amount;
            return true;
        }

        // ------------------------------------------------------------------ personagens

        public bool IsCharacterUnlocked(string brawlerId) => P.unlockedCharacters.Contains(brawlerId);

        public bool UnlockCharacter(string brawlerId)
        {
            if (string.IsNullOrEmpty(brawlerId) || P.unlockedCharacters.Contains(brawlerId)) return false;
            P.unlockedCharacters.Add(brawlerId);
            P.Character(brawlerId, true);
            return true;
        }

        // ------------------------------------------------------------------ pets

        public bool IsPetUnlocked(string petId)
        {
            var p = P.Pet(petId, false);
            return p != null && p.unlocked;
        }

        public bool UnlockPet(string petId)
        {
            if (m_Ctx.PetCatalog.Find(petId) == null) return false;
            var p = P.Pet(petId, true);
            if (p.unlocked) return false;
            p.unlocked = true;
            return true;
        }

        // ------------------------------------------------------------------ skins e cosméticos

        public bool IsSkinUnlocked(string skinId) => !string.IsNullOrEmpty(skinId) && P.unlockedSkins.Contains(skinId);

        public bool UnlockSkin(string skinId)
        {
            if (m_Ctx.SkinCatalog.Find(skinId) == null || P.unlockedSkins.Contains(skinId)) return false;
            P.unlockedSkins.Add(skinId);
            return true;
        }

        public bool HasCosmetic(string id) => P.unlockedCosmetics.Contains(id);

        public bool UnlockCosmetic(string id)
        {
            if (string.IsNullOrEmpty(id) || P.unlockedCosmetics.Contains(id)) return false;
            P.unlockedCosmetics.Add(id);
            return true;
        }
    }
}
