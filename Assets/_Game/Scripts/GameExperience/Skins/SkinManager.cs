using UnityEngine;

namespace FPSLAN
{
    public enum SkinState
    {
        Locked,
        Unlocked,
        Equipped,
    }

    /// <summary>
    /// Inventário de skins (separado para personagens e pets): desbloquear (compra/regras), equipar e resolver a skin
    /// recebida pela rede (hash do skinId). As skins só mudam a aparência.
    /// </summary>
    public class SkinManager
    {
        readonly GameExperienceContext m_Ctx;

        public SkinManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public SkinDefinition EquippedSkinFor(SkinTarget target, string targetId)
        {
            string id = target == SkinTarget.Pet ? PetSkinId(targetId) : m_Ctx.Profile.EquippedSkin(targetId);
            if (string.IsNullOrEmpty(id) || !m_Ctx.Inventory.IsSkinUnlocked(id)) return null;
            var s = m_Ctx.SkinCatalog.Find(id);
            return s != null && s.target == target && s.targetId == targetId ? s : null;
        }

        string PetSkinId(string petId)
        {
            var p = m_Ctx.Profile.Pet(petId, false);
            return p != null ? p.equippedSkinId : "";
        }

        public SkinState StateOf(SkinDefinition s)
        {
            if (s == null || !m_Ctx.Inventory.IsSkinUnlocked(s.skinId)) return SkinState.Locked;
            var equipped = EquippedSkinFor(s.target, s.targetId);
            return equipped == s ? SkinState.Equipped : SkinState.Unlocked;
        }

        /// <summary>Equipa uma skin desbloqueada (null/"" = aspeto original).</summary>
        public bool Equip(SkinTarget target, string targetId, string skinId)
        {
            if (!string.IsNullOrEmpty(skinId))
            {
                var s = m_Ctx.SkinCatalog.Find(skinId);
                if (s == null || s.target != target || s.targetId != targetId || !m_Ctx.Inventory.IsSkinUnlocked(skinId)) return false;
            }
            if (target == SkinTarget.Pet)
            {
                var p = m_Ctx.Profile.Pet(targetId, true);
                p.equippedSkinId = skinId ?? "";
            }
            else m_Ctx.Profile.SetEquippedSkin(targetId, skinId ?? "");
            m_Ctx.Save();
            return true;
        }

        /// <summary>Compra uma skin com moedas (só se a regra for Coins).</summary>
        public bool TryPurchase(string skinId)
        {
            var s = m_Ctx.SkinCatalog.Find(skinId);
            if (s == null || m_Ctx.Inventory.IsSkinUnlocked(skinId) || !s.unlock.IsPurchasable) return false;
            if (!m_Ctx.Inventory.TrySpend(s.unlock.value)) return false;
            m_Ctx.Inventory.UnlockSkin(skinId);
            m_Ctx.Emit(FeedbackType.SkinUnlocked, "NEW SKIN!", s.displayName + " · " + RarityStyle.LabelOf(s.rarity), RarityStyle.ColorOf(s.rarity));
            m_Ctx.Log("Shop", "Skin purchased: " + s.displayName + " (" + s.unlock.value + " coins)");
            m_Ctx.Save();
            return true;
        }

        public bool IsRuleMet(SkinDefinition s)
        {
            if (s == null || s.unlock == null) return false;
            var r = s.unlock;
            switch (r.method)
            {
                case UnlockMethod.Default: return true;
                case UnlockMethod.PlayerLevel: return m_Ctx.Progression.PlayerLevel >= r.value;
                case UnlockMethod.Achievement:
                case UnlockMethod.Challenge: return m_Ctx.Profile.completedAchievements.Contains(r.refId);
                case UnlockMethod.Quest: return m_Ctx.Profile.questsCompletedTotal >= Mathf.Max(1, r.value);
                case UnlockMethod.Event: return GameEvents.IsActive(r.refId, m_Ctx.Clock.UtcNow);
                default: return false; // Coins/RewardOnly: só por compra ou recompensa
            }
        }

        /// <summary>Desbloqueia as skins cujas regras (nível, conquista, desafio) já estão cumpridas.</summary>
        public int CheckRuleUnlocks()
        {
            int n = 0;
            foreach (var s in m_Ctx.SkinCatalog.skins)
            {
                if (s == null || m_Ctx.Inventory.IsSkinUnlocked(s.skinId) || !IsRuleMet(s)) continue;
                if (!m_Ctx.Inventory.UnlockSkin(s.skinId)) continue;
                n++;
                m_Ctx.Emit(FeedbackType.SkinUnlocked, "NEW SKIN!", s.displayName + " · " + RarityStyle.LabelOf(s.rarity), RarityStyle.ColorOf(s.rarity));
            }
            return n;
        }

        public string UnlockText(SkinDefinition s)
        {
            if (s == null) return "";
            var r = s.unlock;
            switch (r.method)
            {
                case UnlockMethod.Default: return "Included";
                case UnlockMethod.Coins: return r.value + " coins";
                case UnlockMethod.PlayerLevel: return "Player level " + r.value;
                case UnlockMethod.Achievement:
                case UnlockMethod.Challenge:
                {
                    var a = m_Ctx.QuestCatalog.FindAchievement(r.refId);
                    return (r.method == UnlockMethod.Challenge ? "Challenge: " : "Achievement: ") + (a != null ? a.title : r.refId);
                }
                case UnlockMethod.Quest: return "Complete " + r.value + " quests";
                case UnlockMethod.Event: return "Event " + r.refId;
                default: return "Quest/level reward";
            }
        }

        // ------------------------------------------------------------------ resolução para a rede/visual

        /// <summary>Skin recebida pela rede (hash) para um alvo; null se desconhecida ou de outro alvo (usa o aspeto base).</summary>
        public static SkinDefinition Resolve(SkinCatalog catalog, int hash, SkinTarget target, string targetId)
        {
            if (catalog == null || hash == 0) return null;
            var s = catalog.FindByHash(hash);
            if (s == null || s.target != target || s.targetId != targetId) return null;
            return s;
        }
    }
}
