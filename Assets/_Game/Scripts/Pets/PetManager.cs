namespace FPSLAN
{
    /// <summary>Pet equipado (um por jogador) e skin de cada pet.</summary>
    public sealed class PetManager
    {
        readonly ExperienceContext m_Ctx;
        readonly InventoryManager m_Inventory;
        readonly PetProgressionManager m_Progression;

        public PetManager(ExperienceContext ctx, InventoryManager inventory, PetProgressionManager progression)
        {
            m_Ctx = ctx;
            m_Inventory = inventory;
            m_Progression = progression;
        }

        public string EquippedPetId => m_Inventory.OwnsPet(m_Ctx.profile.equippedPetId) ? m_Ctx.profile.equippedPetId : "";

        public bool Equip(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return Unequip();
            if (!m_Inventory.OwnsPet(petId)) return false;
            if (m_Ctx.profile.equippedPetId == petId) return true;
            m_Ctx.profile.equippedPetId = petId;
            m_Ctx.MarkDirty();
            return true;
        }

        public bool Unequip()
        {
            if (string.IsNullOrEmpty(m_Ctx.profile.equippedPetId)) return true;
            m_Ctx.profile.equippedPetId = "";
            m_Ctx.MarkDirty();
            return true;
        }

        public PetState StateOf(string petId)
        {
            if (!m_Inventory.OwnsPet(petId)) return PetState.Locked;
            if (m_Progression.HasUnseenEvolution(petId)) return PetState.Evolving;
            return EquippedPetId == petId ? PetState.Equipped : PetState.Unlocked;
        }

        public string EquippedSkin(string petId)
        {
            var data = m_Ctx.profile.Pet(petId, false);
            if (data == null || string.IsNullOrEmpty(data.equippedSkin)) return "";
            return m_Inventory.OwnsSkin(data.equippedSkin) ? data.equippedSkin : "";
        }

        /// <summary>Equipa uma skin de pet ("" = aspeto original). Só skins desse pet e já desbloqueadas.</summary>
        public bool EquipSkin(string petId, string skinId)
        {
            var data = m_Ctx.profile.Pet(petId, false);
            if (data == null || !data.unlocked) return false;
            if (!string.IsNullOrEmpty(skinId))
            {
                var skin = m_Ctx.catalog.Skin(skinId);
                if (skin == null || skin.target != SkinTarget.Pet || skin.targetId != petId || !m_Inventory.OwnsSkin(skinId)) return false;
            }
            data.equippedSkin = skinId ?? "";
            m_Ctx.MarkDirty();
            return true;
        }
    }
}
