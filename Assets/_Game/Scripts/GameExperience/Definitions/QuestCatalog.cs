using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
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
