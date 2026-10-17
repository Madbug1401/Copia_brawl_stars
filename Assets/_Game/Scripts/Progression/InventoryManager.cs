namespace FPSLAN
{
    /// <summary>O que o jogador tem: moedas, pets, skins e cosméticos (por id estável). Sem duplicados.</summary>
    public sealed class InventoryManager
    {
        readonly ExperienceContext m_Ctx;

        public InventoryManager(ExperienceContext ctx) => m_Ctx = ctx;

        PlayerProfileData Profile => m_Ctx.profile;

        public int Coins => Profile.coins;

        public void AddCoins(int amount)
        {
            if (amount <= 0) return;
            Profile.coins = (int)System.Math.Min(int.MaxValue, (long)Profile.coins + amount);
            m_Ctx.MarkDirty();
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Profile.coins < amount) return false;
            Profile.coins -= amount;
            m_Ctx.MarkDirty();
            return true;
        }

        public bool OwnsPet(string petId)
        {
            var p = Profile.Pet(petId, false);
            return p != null && p.unlocked;
        }

        /// <summary>Desbloqueia um pet. Devolve false se o pet não existe ou já era do jogador.</summary>
        public bool AddPet(string petId)
        {
            if (m_Ctx.catalog.Pet(petId) == null || OwnsPet(petId)) return false;
            var p = Profile.Pet(petId, true);
            p.unlocked = true;
            m_Ctx.MarkDirty();
            return true;
        }

        public bool OwnsSkin(string skinId) => !string.IsNullOrEmpty(skinId) && Profile.unlockedSkins.Contains(skinId);

        public bool AddSkin(string skinId)
        {
            if (m_Ctx.catalog.Skin(skinId) == null || OwnsSkin(skinId)) return false;
            Profile.unlockedSkins.Add(skinId);
            m_Ctx.MarkDirty();
            return true;
        }

        public bool OwnsCosmetic(string id) => !string.IsNullOrEmpty(id) && Profile.unlockedCosmetics.Contains(id);

        public bool AddCosmetic(string id)
        {
            if (m_Ctx.catalog.Cosmetic(id) == null || OwnsCosmetic(id)) return false;
            Profile.unlockedCosmetics.Add(id);
            m_Ctx.MarkDirty();
            return true;
        }

        public int PetsUnlocked
        {
            get
            {
                int n = 0;
                foreach (var p in Profile.pets)
                    if (p != null && p.unlocked && m_Ctx.catalog.Pet(p.petId) != null) n++;
                return n;
            }
        }
    }
}
