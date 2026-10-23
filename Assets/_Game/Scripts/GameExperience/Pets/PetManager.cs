using UnityEngine;

namespace FPSLAN
{
    /// <summary>Estado de um pet no inventário (para a interface).</summary>
    public enum PetState
    {
        Locked,
        Unlocked,
        Equipped,
        /// <summary>Equipado e ainda a evoluir (abaixo do nível máximo).</summary>
        Evolving,
    }

    /// <summary>Pet equipado (só um de cada vez) e estado de cada pet no inventário.</summary>
    public class PetManager
    {
        readonly GameExperienceContext m_Ctx;

        public PetManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public PetDefinition EquippedPet
        {
            get
            {
                var id = m_Ctx.Profile.equippedPetId;
                if (string.IsNullOrEmpty(id) || !m_Ctx.Inventory.IsPetUnlocked(id)) return null;
                return m_Ctx.PetCatalog.Find(id);
            }
        }

        public PetState StateOf(string petId)
        {
            if (!m_Ctx.Inventory.IsPetUnlocked(petId)) return PetState.Locked;
            if (m_Ctx.Profile.equippedPetId != petId) return PetState.Unlocked;
            return m_Ctx.PetProgression.Level(petId) < m_Ctx.Config.petLevels.maxLevel ? PetState.Evolving : PetState.Equipped;
        }

        public static string StateLabel(PetState s)
        {
            switch (s)
            {
                case PetState.Locked: return "Locked";
                case PetState.Equipped: return "Equipped";
                case PetState.Evolving: return "Equipped · evolving";
                default: return "Unlocked";
            }
        }

        /// <summary>Equipa um pet desbloqueado (substitui o anterior: só um de cada vez).</summary>
        public bool Equip(string petId)
        {
            if (!m_Ctx.Inventory.IsPetUnlocked(petId)) return false;
            if (m_Ctx.Profile.equippedPetId == petId) return true;
            m_Ctx.Profile.equippedPetId = petId;
            m_Ctx.Save();
            return true;
        }

        public void Unequip()
        {
            if (string.IsNullOrEmpty(m_Ctx.Profile.equippedPetId)) return;
            m_Ctx.Profile.equippedPetId = "";
            m_Ctx.Save();
        }
    }

    /// <summary>XP e níveis dos pets (1–10), estágios visuais e recompensas de evolução.</summary>
    public class PetProgressionManager
    {
        readonly GameExperienceContext m_Ctx;

        public PetProgressionManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        public long Xp(string petId)
        {
            var p = m_Ctx.Profile.Pet(petId, false);
            return p != null ? p.xp : 0;
        }

        public int Level(string petId) => m_Ctx.Config.petLevels.LevelForXp(Xp(petId));

        public int Stage(string petId) => PetDefinition.StageFor(Level(petId));

        /// <summary>Soma XP a um pet desbloqueado. Devolve os níveis ganhos (com feedback de subida e de evolução).</summary>
        public int AddXp(string petId, int amount)
        {
            if (amount <= 0 || !m_Ctx.Inventory.IsPetUnlocked(petId)) return 0;
            var def = m_Ctx.PetCatalog.Find(petId);
            var p = m_Ctx.Profile.Pet(petId, true);
            var curve = m_Ctx.Config.petLevels;
            int before = curve.LevelForXp(p.xp);
            p.xp = System.Math.Min(curve.MaxTotalXp, p.xp + amount);
            int after = curve.LevelForXp(p.xp);
            if (after <= before) return 0;

            string name = def != null ? def.displayName : petId;
            var color = def != null ? Color.Lerp(def.primaryColor, Color.white, 0.3f) : Color.white;
            if (PetDefinition.StageFor(after) > PetDefinition.StageFor(before))
                m_Ctx.Emit(FeedbackType.PetEvolved, name.ToUpperInvariant() + " EVOLVED!", "Stage " + PetDefinition.StageName(PetDefinition.StageFor(after)), color);
            else
                m_Ctx.Emit(FeedbackType.PetLevelUp, name.ToUpperInvariant() + " · LEVEL " + after, "", color);

            if (def != null)
                foreach (var m in def.milestones)
                    if (m != null && m.level > before && m.level <= after)
                        m_Ctx.Emit(FeedbackType.PetEvolved, "EVOLUTION OF " + name.ToUpperInvariant(), m.description, color);
            return after - before;
        }

        /// <summary>O pet já desbloqueou a recompensa de evolução deste tipo? (animação no 5, visual no 8, cosmético no 10)</summary>
        public static bool HasMilestone(PetDefinition def, int level, PetMilestoneType type)
        {
            if (def == null) return false;
            foreach (var m in def.milestones)
                if (m != null && m.type == type && level >= m.level) return true;
            return false;
        }
    }

    /// <summary>Desbloqueio de pets: nível, missões, conquistas, moedas, desafios e eventos.</summary>
    public class PetUnlockManager
    {
        readonly GameExperienceContext m_Ctx;

        public PetUnlockManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        /// <summary>A regra do pet está cumprida? (compras e eventos não desbloqueiam automaticamente)</summary>
        public bool IsRuleMet(PetDefinition def)
        {
            if (def == null || def.unlock == null) return false;
            var r = def.unlock;
            var p = m_Ctx.Profile;
            switch (r.method)
            {
                case UnlockMethod.Default: return true;
                case UnlockMethod.PlayerLevel: return m_Ctx.Progression.PlayerLevel >= r.value;
                case UnlockMethod.Quest:
                    return string.IsNullOrEmpty(r.refId) ? p.questsCompletedTotal >= Mathf.Max(1, r.value) : IsQuestEverCompleted(r.refId);
                case UnlockMethod.Achievement:
                case UnlockMethod.Challenge:
                    return p.completedAchievements.Contains(r.refId);
                case UnlockMethod.Event: return GameEvents.IsActive(r.refId, m_Ctx.Clock.UtcNow);
                default: return false;
            }
        }

        bool IsQuestEverCompleted(string questId)
        {
            foreach (var q in m_Ctx.Profile.quests) if (q.questId == questId && q.completed) return true;
            return false;
        }

        /// <summary>Progresso do desbloqueio para a interface (ex.: "Nível 2/3").</summary>
        public string ProgressText(PetDefinition def)
        {
            if (def == null) return "";
            var r = def.unlock;
            switch (r.method)
            {
                case UnlockMethod.Default: return "Available from the start";
                case UnlockMethod.PlayerLevel: return "Reach level " + r.value + " (current: " + m_Ctx.Progression.PlayerLevel + ")";
                case UnlockMethod.Quest:
                    return string.IsNullOrEmpty(r.refId)
                        ? "Complete " + r.value + " quests (" + Mathf.Min(m_Ctx.Profile.questsCompletedTotal, r.value) + "/" + r.value + ")"
                        : "Complete quest " + r.refId;
                case UnlockMethod.Achievement:
                {
                    var a = m_Ctx.QuestCatalog.FindAchievement(r.refId);
                    return "Achievement: " + (a != null ? a.title + " (" + m_Ctx.Quests.AchievementProgress(a) + "/" + a.target + ")" : r.refId);
                }
                case UnlockMethod.Challenge:
                {
                    var a = m_Ctx.QuestCatalog.FindAchievement(r.refId);
                    return "Challenge: " + (a != null ? a.title + " — " + a.description : r.refId);
                }
                case UnlockMethod.Coins: return "Buy for " + r.value + " coins";
                case UnlockMethod.Event: return "Event: " + r.refId + " (coming soon)";
                default: return "Special reward";
            }
        }

        /// <summary>Desbloqueia todos os pets cujas regras já estão cumpridas. Devolve quantos foram desbloqueados.</summary>
        public int CheckAll()
        {
            int n = 0;
            foreach (var def in m_Ctx.PetCatalog.pets)
            {
                if (def == null || m_Ctx.Inventory.IsPetUnlocked(def.petId) || !IsRuleMet(def)) continue;
                if (!m_Ctx.Inventory.UnlockPet(def.petId)) continue;
                n++;
                m_Ctx.Emit(FeedbackType.PetUnlocked, "NEW PET!", def.displayName + " · " + PetDefinition.CategoryLabel(def.category),
                    Color.Lerp(def.primaryColor, Color.white, 0.3f));
                m_Ctx.Log("Pet", "Unlocked: " + def.displayName);
            }
            return n;
        }

        /// <summary>Compra um pet com moedas (só se a regra for Coins).</summary>
        public bool TryPurchase(string petId)
        {
            var def = m_Ctx.PetCatalog.Find(petId);
            if (def == null || m_Ctx.Inventory.IsPetUnlocked(petId) || !def.unlock.IsPurchasable) return false;
            if (!m_Ctx.Inventory.TrySpend(def.unlock.value)) return false;
            m_Ctx.Inventory.UnlockPet(petId);
            m_Ctx.Emit(FeedbackType.PetUnlocked, "NEW PET!", def.displayName, Color.Lerp(def.primaryColor, Color.white, 0.3f));
            m_Ctx.Log("Shop", "Pet purchased: " + def.displayName + " (" + def.unlock.value + " coins)");
            m_Ctx.Save();
            return true;
        }
    }

    /// <summary>Eventos temáticos futuros (pets/skins de evento). Sem eventos ativos nesta versão.</summary>
    public static class GameEvents
    {
        public static bool IsActive(string eventId, System.DateTime utcNow) => false;
    }
}
