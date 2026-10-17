using System;
using System.Collections.Generic;

namespace FPSLAN
{
    /// <summary>
    /// Ficheiro de gravação local do jogador (JSON). Só ids estáveis (texto), nunca índices de registos, para
    /// sobreviver a mudanças na ordem dos dados. Não guarda credenciais nem dados pessoais além do nome escolhido.
    /// Ao mudar a estrutura: subir <see cref="CurrentVersion"/> e acrescentar a migração em SaveManager.Migrate.
    /// </summary>
    [Serializable]
    public class PlayerProfileData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>Id aleatório do perfil (não é uma conta nem uma credencial).</summary>
        public string playerId = "";
        public string createdUtc = "";
        public string lastSavedUtc = "";

        public int playerXp;
        public int coins;

        public List<BrawlerProgressData> brawlers = new List<BrawlerProgressData>();

        public List<PetProgressData> pets = new List<PetProgressData>();
        public string equippedPetId = "";

        /// <summary>Skins desbloqueadas (Brawlers e pets), por id estável.</summary>
        public List<string> unlockedSkins = new List<string>();
        public List<string> unlockedCosmetics = new List<string>();

        public List<QuestProgressData> quests = new List<QuestProgressData>();
        public List<int> claimedPlayerLevelRewards = new List<int>();

        /// <summary>Partidas já creditadas (impede XP duplicado em reconexões / mensagens repetidas).</summary>
        public List<string> processedMatches = new List<string>();
        /// <summary>Recompensas únicas já entregues (missões, níveis, pets): chave → nunca entregar duas vezes.</summary>
        public List<string> grantedRewardKeys = new List<string>();

        public LifetimeStats stats = new LifetimeStats();

        public BrawlerProgressData Brawler(string brawlerId, bool create)
        {
            if (string.IsNullOrEmpty(brawlerId)) return null;
            foreach (var b in brawlers)
                if (b != null && b.brawlerId == brawlerId) return b;
            if (!create) return null;
            var n = new BrawlerProgressData { brawlerId = brawlerId };
            brawlers.Add(n);
            return n;
        }

        public PetProgressData Pet(string petId, bool create)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            foreach (var p in pets)
                if (p != null && p.petId == petId) return p;
            if (!create) return null;
            var n = new PetProgressData { petId = petId };
            pets.Add(n);
            return n;
        }

        public QuestProgressData Quest(string questId, bool create)
        {
            if (string.IsNullOrEmpty(questId)) return null;
            foreach (var q in quests)
                if (q != null && q.questId == questId) return q;
            if (!create) return null;
            var n = new QuestProgressData { questId = questId };
            quests.Add(n);
            return n;
        }
    }

    [Serializable]
    public class BrawlerProgressData
    {
        public string brawlerId = "";
        public int xp;
        /// <summary>Nível de cada melhoria, pela ordem de <see cref="UpgradeStat"/>.</summary>
        public int[] upgrades = new int[UpgradeCount];
        public string equippedSkin = "";
        public int matchesPlayed;

        public const int UpgradeCount = 6;

        public int Upgrade(UpgradeStat stat)
        {
            int i = (int)stat;
            return upgrades != null && i < upgrades.Length ? upgrades[i] : 0;
        }

        public void SetUpgrade(UpgradeStat stat, int level)
        {
            if (upgrades == null || upgrades.Length != UpgradeCount)
            {
                var n = new int[UpgradeCount];
                if (upgrades != null) Array.Copy(upgrades, n, Math.Min(upgrades.Length, UpgradeCount));
                upgrades = n;
            }
            upgrades[(int)stat] = Math.Max(0, level);
        }

        public int TotalUpgrades
        {
            get
            {
                int t = 0;
                if (upgrades != null) foreach (var u in upgrades) t += Math.Max(0, u);
                return t;
            }
        }
    }

    [Serializable]
    public class PetProgressData
    {
        public string petId = "";
        public bool unlocked;
        public int xp;
        public string equippedSkin = "";
        /// <summary>Níveis cujas recompensas já foram entregues.</summary>
        public List<int> claimedLevels = new List<int>();
        /// <summary>Última fase visual vista pelo jogador (para mostrar a animação de evolução uma vez).</summary>
        public int seenStage = 1;
    }

    [Serializable]
    public class QuestProgressData
    {
        public string questId = "";
        /// <summary>Período a que o progresso pertence (ex.: "D2026-10-17", "W2026-42"); muda → recomeça.</summary>
        public string periodKey = "";
        public int progress;
        public bool completed;
        public bool claimed;
        /// <summary>Valores distintos já contados (ex.: Brawlers diferentes).</summary>
        public List<string> distinct = new List<string>();
    }

    [Serializable]
    public class LifetimeStats
    {
        public int matches;
        public int wins;
        public int kills;
        public int deaths;
        public int assists;
        public int headshots;
        public long damage;
    }

    /// <summary>Linha de estatísticas de uma partida, como o host a enviou (já validada).</summary>
    public struct MatchStatLine
    {
        public bool valid;
        public bool victory;
        public int kills, deaths, assists, objectives, headshots, damage;
        public float seconds;
        public bool usedPet;
    }
}
