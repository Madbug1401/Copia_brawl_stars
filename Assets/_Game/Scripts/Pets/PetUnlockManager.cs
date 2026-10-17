namespace FPSLAN
{
    /// <summary>Estado de um pet no inventário.</summary>
    public enum PetState : byte
    {
        Locked,
        Unlocked,
        Equipped,
        /// <summary>Subiu de fase e o jogador ainda não viu a evolução.</summary>
        Evolving,
    }

    /// <summary>
    /// Desbloqueio de pets: por omissão, nível de jogador e moedas aqui; missões, conquistas, desafios e eventos
    /// chegam como recompensas (RewardManager), por isso nunca há duplicados.
    /// </summary>
    public sealed class PetUnlockManager
    {
        readonly ExperienceContext m_Ctx;
        readonly InventoryManager m_Inventory;
        readonly PlayerProgressionManager m_Progression;

        public PetUnlockManager(ExperienceContext ctx, InventoryManager inventory, PlayerProgressionManager progression)
        {
            m_Ctx = ctx;
            m_Inventory = inventory;
            m_Progression = progression;
        }

        public bool IsUnlocked(string petId) => m_Inventory.OwnsPet(petId);

        public bool CanBuy(string petId, out string reason)
        {
            var pet = m_Ctx.catalog.Pet(petId);
            reason = "";
            if (pet == null) { reason = "Pet desconhecido"; return false; }
            if (IsUnlocked(petId)) { reason = "Já desbloqueado"; return false; }
            if (pet.unlock.method != UnlockMethod.Coins) { reason = pet.unlock.Describe(); return false; }
            if (m_Inventory.Coins < pet.unlock.value) { reason = "Moedas insuficientes"; return false; }
            return true;
        }

        public bool TryBuy(string petId)
        {
            if (!CanBuy(petId, out _)) return false;
            var pet = m_Ctx.catalog.Pet(petId);
            if (!m_Inventory.TrySpend(pet.unlock.value)) return false;
            m_Inventory.AddPet(petId);
            m_Ctx.Raise(ExperienceEventType.PetUnlocked, "Novo pet: " + pet.displayName, pet.description, petId);
            return true;
        }

        /// <summary>Desbloqueios automáticos (por omissão e por nível de jogador). Chamar ao carregar e ao subir de nível.</summary>
        public void CheckAutomaticUnlocks()
        {
            int level = m_Progression.PlayerLevel;
            foreach (var pet in m_Ctx.catalog.pets)
            {
                if (pet == null || IsUnlocked(pet.petId)) continue;
                bool unlock = pet.unlock.method == UnlockMethod.Default ||
                              (pet.unlock.method == UnlockMethod.PlayerLevel && level >= pet.unlock.value);
                if (!unlock) continue;
                m_Inventory.AddPet(pet.petId);
                if (pet.unlock.method != UnlockMethod.Default)
                    m_Ctx.Raise(ExperienceEventType.PetUnlocked, "Novo pet: " + pet.displayName, pet.description, pet.petId);
            }
        }
    }
}
