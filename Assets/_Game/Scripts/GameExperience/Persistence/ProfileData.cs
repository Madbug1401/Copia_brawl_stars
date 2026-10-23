using System;
using System.Collections.Generic;

namespace FPSLAN
{
    /// <summary>
    /// Progressão guardada localmente (protótipo). Formato versionado; os níveis são sempre derivados do XP.
    /// Não guarda credenciais nem dados sensíveis.
    /// </summary>
    [Serializable]
    public class ProfileData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string playerId = "";
        public string createdUtc = "";
        public string lastSavedUtc = "";

        public long playerXp;
        public int coins;
        public int upraFood;
        public bool upraIntroduced;

        public List<CharacterProgress> characters = new List<CharacterProgress>();
        public List<PetProgress> pets = new List<PetProgress>();
        public string equippedPetId = "";

        public List<string> unlockedSkins = new List<string>();
        public List<EquippedSkin> equippedSkins = new List<EquippedSkin>();
        public List<string> unlockedCosmetics = new List<string>();
        public List<string> unlockedCharacters = new List<string>();

        public List<QuestProgress> quests = new List<QuestProgress>();
        public List<string> completedAchievements = new List<string>();
        public int questsCompletedTotal;

        /// <summary>Chaves de recompensas já reclamadas (missão+período, nível, conquista) — impede duplicados.</summary>
        public List<string> claimedRewardKeys = new List<string>();
        /// <summary>Níveis de jogador atingidos cuja recompensa ainda não foi reclamada.</summary>
        public List<int> pendingLevelRewards = new List<int>();
        /// <summary>Ids das partidas já recompensadas (as mais recentes) — impede XP repetido.</summary>
        public List<string> processedMatchIds = new List<string>();

        public LifetimeStats stats = new LifetimeStats();
        public List<RewardLogEntry> rewardLog = new List<RewardLogEntry>();

        public CharacterProgress Character(string brawlerId, bool create)
        {
            foreach (var c in characters) if (c != null && c.brawlerId == brawlerId) return c;
            if (!create || string.IsNullOrEmpty(brawlerId)) return null;
            var n = new CharacterProgress { brawlerId = brawlerId };
            characters.Add(n);
            return n;
        }

        public PetProgress Pet(string petId, bool create)
        {
            foreach (var p in pets) if (p != null && p.petId == petId) return p;
            if (!create || string.IsNullOrEmpty(petId)) return null;
            var n = new PetProgress { petId = petId };
            pets.Add(n);
            return n;
        }

        public string EquippedSkin(string targetId)
        {
            foreach (var e in equippedSkins) if (e != null && e.targetId == targetId) return e.skinId;
            return "";
        }

        public void SetEquippedSkin(string targetId, string skinId)
        {
            foreach (var e in equippedSkins)
                if (e != null && e.targetId == targetId)
                {
                    e.skinId = skinId ?? "";
                    return;
                }
            equippedSkins.Add(new EquippedSkin { targetId = targetId, skinId = skinId ?? "" });
        }
    }

    [Serializable]
    public class CharacterProgress
    {
        public string brawlerId = "";
        public long xp;
        public int matches;
        /// <summary>Nível de melhoria por atributo, na ordem de UpgradeStat.</summary>
        public int[] upgrades = new int[6];

        public int Upgrade(UpgradeStat stat)
        {
            int i = (int)stat;
            return upgrades != null && i < upgrades.Length ? upgrades[i] : 0;
        }

        public void SetUpgrade(UpgradeStat stat, int level)
        {
            if (upgrades == null || upgrades.Length < 6) Array.Resize(ref upgrades, 6);
            upgrades[(int)stat] = level;
        }
    }

    [Serializable]
    public class PetProgress
    {
        public string petId = "";
        public bool unlocked;
        public long xp;
        public int matches;
        public string equippedSkinId = "";
    }

    [Serializable]
    public class EquippedSkin
    {
        public string targetId = "";
        public string skinId = "";
    }

    [Serializable]
    public class QuestProgress
    {
        public string questId = "";
        public string periodKey = "";
        public int progress;
        public bool completed;
        public bool claimed;
        /// <summary>Valores distintos já contados (ex.: personagens usadas esta semana).</summary>
        public List<string> distinct = new List<string>();
    }

    [Serializable]
    public class LifetimeStats
    {
        public int matches;
        public int validMatches;
        public int wins;
        public int kills;
        public int assists;
        public int deaths;
        public int headshots;
        public int objectives;
        public long damageDealt;
        public int flawlessMatches;
        public int competitiveMatches;
        public int matchesWithPet;
    }

    [Serializable]
    public class RewardLogEntry
    {
        public string utc = "";
        public string source = "";
        public string text = "";
    }
}
