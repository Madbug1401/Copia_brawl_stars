namespace FPSLAN
{
    /// <summary>
    /// Skins de Brawlers: desbloqueio (moedas, nível de jogador; missões/conquistas/eventos chegam como recompensas)
    /// e skin equipada por Brawler. Puramente cosmético. As skins de pet equipam-se no PetManager.
    /// </summary>
    public sealed class SkinManager
    {
        readonly ExperienceContext m_Ctx;
        readonly InventoryManager m_Inventory;
        readonly PlayerProgressionManager m_Progression;

        public SkinManager(ExperienceContext ctx, InventoryManager inventory, PlayerProgressionManager progression)
        {
            m_Ctx = ctx;
            m_Inventory = inventory;
            m_Progression = progression;
        }

        public bool Owns(string skinId) => m_Inventory.OwnsSkin(skinId);

        public bool CanBuy(string skinId, out string reason)
        {
            var skin = m_Ctx.catalog.Skin(skinId);
            reason = "";
            if (skin == null) { reason = "Skin desconhecida"; return false; }
            if (Owns(skinId)) { reason = "Já desbloqueada"; return false; }
            if (skin.unlock.method != UnlockMethod.Coins) { reason = skin.unlock.Describe(); return false; }
            if (m_Inventory.Coins < skin.unlock.value) { reason = "Moedas insuficientes"; return false; }
            return true;
        }

        public bool TryBuy(string skinId)
        {
            if (!CanBuy(skinId, out _)) return false;
            var skin = m_Ctx.catalog.Skin(skinId);
            if (!m_Inventory.TrySpend(skin.unlock.value)) return false;
            m_Inventory.AddSkin(skinId);
            m_Ctx.Raise(ExperienceEventType.SkinUnlocked, "Nova skin: " + skin.displayName, SkinDefinition.RarityName(skin.rarity), skinId);
            return true;
        }

        public void CheckAutomaticUnlocks()
        {
            int level = m_Progression.PlayerLevel;
            foreach (var skin in m_Ctx.catalog.skins)
            {
                if (skin == null || Owns(skin.skinId)) continue;
                bool unlock = skin.unlock.method == UnlockMethod.Default ||
                              (skin.unlock.method == UnlockMethod.PlayerLevel && level >= skin.unlock.value);
                if (!unlock) continue;
                m_Inventory.AddSkin(skin.skinId);
                if (skin.unlock.method != UnlockMethod.Default)
                    m_Ctx.Raise(ExperienceEventType.SkinUnlocked, "Nova skin: " + skin.displayName, SkinDefinition.RarityName(skin.rarity), skin.skinId);
            }
        }

        public string Equipped(string brawlerId)
        {
            var data = m_Ctx.profile.Brawler(brawlerId, false);
            if (data == null || string.IsNullOrEmpty(data.equippedSkin)) return "";
            return Owns(data.equippedSkin) ? data.equippedSkin : "";
        }

        /// <summary>Equipa uma skin no Brawler ("" = aspeto original). Só skins desse Brawler e desbloqueadas.</summary>
        public bool Equip(string brawlerId, string skinId)
        {
            if (!m_Ctx.catalog.HasBrawler(brawlerId)) return false;
            if (!string.IsNullOrEmpty(skinId))
            {
                var skin = m_Ctx.catalog.Skin(skinId);
                if (skin == null || skin.target != SkinTarget.Brawler || skin.targetId != brawlerId || !Owns(skinId)) return false;
            }
            var data = m_Ctx.profile.Brawler(brawlerId, true);
            data.equippedSkin = skinId ?? "";
            m_Ctx.MarkDirty();
            return true;
        }
    }
}
