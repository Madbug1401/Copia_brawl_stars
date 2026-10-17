using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Lista de pets disponíveis (Resources/GameExperience/PetCatalog). Novos pets entram só por dados.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Pet Catalog")]
    public class PetCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/PetCatalog";

        public List<PetDefinition> pets = new List<PetDefinition>();

        public PetDefinition Find(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            foreach (var p in pets) if (p != null && p.petId == petId) return p;
            return null;
        }

        public PetDefinition FindByHash(int hash)
        {
            if (hash == 0) return null;
            foreach (var p in pets) if (p != null && p.StableHash == hash) return p;
            return null;
        }

        public static PetCatalog CreateDefault()
        {
            var c = CreateInstance<PetCatalog>();
            c.name = "PetCatalog (predefinido)";
            c.pets.AddRange(GameExperienceDefaults.CreatePets());
            return c;
        }
    }

    /// <summary>Lista de skins de personagens e de pets (Resources/GameExperience/SkinCatalog).</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Skin Catalog")]
    public class SkinCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/SkinCatalog";

        public List<SkinDefinition> skins = new List<SkinDefinition>();

        public SkinDefinition Find(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            foreach (var s in skins) if (s != null && s.skinId == skinId) return s;
            return null;
        }

        public SkinDefinition FindByHash(int hash)
        {
            if (hash == 0) return null;
            foreach (var s in skins) if (s != null && s.StableHash == hash) return s;
            return null;
        }

        public List<SkinDefinition> ForTarget(SkinTarget target, string targetId)
        {
            var list = new List<SkinDefinition>();
            foreach (var s in skins)
                if (s != null && s.target == target && s.targetId == targetId) list.Add(s);
            return list;
        }

        public static SkinCatalog CreateDefault()
        {
            var c = CreateInstance<SkinCatalog>();
            c.name = "SkinCatalog (predefinido)";
            c.skins.AddRange(GameExperienceDefaults.CreateSkins());
            return c;
        }
    }

    /// <summary>Missões (diárias/semanais), conquistas, desafios e regras de desbloqueio de personagens.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Quest Catalog")]
    public class QuestCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/QuestCatalog";

        public List<QuestDefinition> quests = new List<QuestDefinition>();
        public List<AchievementDefinition> achievements = new List<AchievementDefinition>();
        public List<CharacterUnlockDefinition> characterUnlocks = new List<CharacterUnlockDefinition>();

        public QuestDefinition FindQuest(string questId)
        {
            foreach (var q in quests) if (q != null && q.questId == questId) return q;
            return null;
        }

        public AchievementDefinition FindAchievement(string id)
        {
            foreach (var a in achievements) if (a != null && a.achievementId == id) return a;
            return null;
        }

        public UnlockRule CharacterRule(string brawlerId)
        {
            foreach (var c in characterUnlocks) if (c != null && c.brawlerId == brawlerId) return c.unlock;
            return null;
        }

        public static QuestCatalog CreateDefault()
        {
            var c = CreateInstance<QuestCatalog>();
            c.name = "QuestCatalog (predefinido)";
            GameExperienceDefaults.FillQuests(c);
            return c;
        }
    }
}
