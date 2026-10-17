using System;
using System.Collections.Generic;
using System.IO;

namespace FPSLAN
{
    /// <summary>Converte o perfil de/para texto. Em jogo: JsonUtility; nos testes pode ser outro.</summary>
    public interface IProfileSerializer
    {
        string Serialize(PlayerProfileData profile);
        PlayerProfileData Deserialize(string text);
    }

    public enum SaveLoadStatus
    {
        Loaded,
        CreatedNew,
        RecoveredFromBackup,
        /// <summary>Ficheiro principal e cópia ilegíveis: perfil novo (o ficheiro estragado é guardado à parte).</summary>
        ResetCorrupt,
    }

    /// <summary>
    /// Gravação local versionada do perfil (JSON em persistentDataPath). Escrita segura: grava num ficheiro
    /// temporário, confirma que se lê de volta, guarda a versão anterior como .bak e só então substitui.
    /// Ao carregar: valida e repara valores; se o principal estiver estragado usa o .bak; se ambos estiverem,
    /// cria um perfil novo e guarda o estragado como .corrupt (nunca apaga dados do jogador).
    ///
    /// Não guarda credenciais. Caminho para armazenamento autoritativo: ver FRAGOSO_GAME_EXPERIENCE.md.
    /// </summary>
    public sealed class SaveManager
    {
        public const string DefaultFileName = "fpslan_profile.json";

        readonly string m_Directory;
        readonly string m_FileName;
        readonly IProfileSerializer m_Serializer;

        public SaveManager(string directory, IProfileSerializer serializer, string fileName = DefaultFileName)
        {
            m_Directory = directory;
            m_Serializer = serializer;
            m_FileName = string.IsNullOrEmpty(fileName) ? DefaultFileName : fileName;
        }

        public string FilePath => Path.Combine(m_Directory, m_FileName);
        public string BackupPath => FilePath + ".bak";
        public string TempPath => FilePath + ".tmp";

        public string LastError { get; private set; } = "";
        /// <summary>Opcional: dados usados para limitar XP/melhorias ao validar.</summary>
        public ExperienceCatalog Catalog { get; set; }

        public PlayerProfileData Load(Func<DateTime> utcNow, out SaveLoadStatus status)
        {
            utcNow = utcNow ?? (() => DateTime.UtcNow);
            LastError = "";

            bool mainExists = File.Exists(FilePath);
            if (mainExists && TryRead(FilePath, out var profile))
            {
                status = SaveLoadStatus.Loaded;
                return Prepare(profile, utcNow);
            }

            if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup))
            {
                status = SaveLoadStatus.RecoveredFromBackup;
                if (mainExists) PreserveCorrupt(FilePath, utcNow());
                var recovered = Prepare(backup, utcNow);
                Save(recovered, utcNow);
                return recovered;
            }

            if (mainExists)
            {
                PreserveCorrupt(FilePath, utcNow());
                status = SaveLoadStatus.ResetCorrupt;
            }
            else status = SaveLoadStatus.CreatedNew;

            var fresh = CreateNew(utcNow());
            Save(fresh, utcNow);
            return fresh;
        }

        PlayerProfileData Prepare(PlayerProfileData p, Func<DateTime> utcNow)
        {
            p = Migrate(p);
            Validate(p, utcNow(), Catalog);
            return p;
        }

        bool TryRead(string path, out PlayerProfileData profile)
        {
            profile = null;
            try
            {
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return false;
                profile = m_Serializer.Deserialize(text);
                return profile != null && profile.version > 0;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                profile = null;
                return false;
            }
        }

        void PreserveCorrupt(string path, DateTime now)
        {
            try
            {
                var target = path + ".corrupt-" + now.ToString("yyyyMMddHHmmss");
                if (!File.Exists(target)) File.Copy(path, target);
            }
            catch (Exception e) { LastError = e.Message; }
        }

        /// <summary>Grava de forma segura. Devolve false (e LastError) se não conseguir.</summary>
        public bool Save(PlayerProfileData profile, Func<DateTime> utcNow = null)
        {
            if (profile == null) return false;
            try
            {
                Directory.CreateDirectory(m_Directory);
                profile.version = PlayerProfileData.CurrentVersion;
                profile.lastSavedUtc = (utcNow ?? (() => DateTime.UtcNow))().ToString("o");
                var text = m_Serializer.Serialize(profile);

                File.WriteAllText(TempPath, text);
                // Confirma que o que foi escrito se lê de volta antes de substituir o ficheiro bom
                var check = m_Serializer.Deserialize(File.ReadAllText(TempPath));
                if (check == null || check.playerId != profile.playerId) throw new IOException("Verificação da gravação falhou.");

                if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, true);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TempPath, FilePath);
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch { }
                return false;
            }
        }

        public static PlayerProfileData CreateNew(DateTime utcNow)
        {
            return new PlayerProfileData
            {
                version = PlayerProfileData.CurrentVersion,
                playerId = Guid.NewGuid().ToString("N"),
                createdUtc = utcNow.ToString("o"),
                lastSavedUtc = utcNow.ToString("o"),
            };
        }

        /// <summary>Atualiza gravações antigas para a versão atual (acrescentar casos ao mudar a estrutura).</summary>
        public static PlayerProfileData Migrate(PlayerProfileData p)
        {
            if (p == null) return null;
            // v1 é a primeira versão. Exemplo para o futuro:
            // if (p.version < 2) { ...converter campos...; p.version = 2; }
            if (p.version > PlayerProfileData.CurrentVersion)
            {
                // Gravação de uma versão mais recente do jogo: lê o que conhece, sem apagar nada
            }
            p.version = PlayerProfileData.CurrentVersion;
            return p;
        }

        /// <summary>Repara valores impossíveis (negativos, listas nulas, duplicados, equipados que não existem).</summary>
        public static void Validate(PlayerProfileData p, DateTime utcNow, ExperienceCatalog catalog = null)
        {
            if (p == null) return;
            if (string.IsNullOrEmpty(p.playerId)) p.playerId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(p.createdUtc)) p.createdUtc = utcNow.ToString("o");
            p.coins = Math.Max(0, p.coins);
            p.playerXp = Math.Max(0, p.playerXp);
            var settings = catalog?.progression;
            if (settings != null) p.playerXp = Math.Min(p.playerXp, settings.playerCurve.MaxTotalXp);

            p.brawlers = p.brawlers ?? new List<BrawlerProgressData>();
            p.pets = p.pets ?? new List<PetProgressData>();
            p.unlockedSkins = Dedupe(p.unlockedSkins);
            p.unlockedCosmetics = Dedupe(p.unlockedCosmetics);
            p.quests = p.quests ?? new List<QuestProgressData>();
            p.claimedPlayerLevelRewards = p.claimedPlayerLevelRewards ?? new List<int>();
            p.processedMatches = Dedupe(p.processedMatches);
            p.grantedRewardKeys = Dedupe(p.grantedRewardKeys);
            p.stats = p.stats ?? new LifetimeStats();
            p.equippedPetId = p.equippedPetId ?? "";

            // Brawlers: um registo por id, XP e melhorias dentro dos limites
            var seen = new HashSet<string>();
            for (int i = p.brawlers.Count - 1; i >= 0; i--)
            {
                var b = p.brawlers[i];
                if (b == null || string.IsNullOrEmpty(b.brawlerId) || !seen.Add(b.brawlerId)) { p.brawlers.RemoveAt(i); continue; }
                b.xp = Math.Max(0, b.xp);
                if (settings != null) b.xp = Math.Min(b.xp, settings.brawlerCurve.MaxTotalXp);
                b.equippedSkin = b.equippedSkin ?? "";
                if (b.upgrades == null || b.upgrades.Length != BrawlerProgressData.UpgradeCount) b.SetUpgrade(UpgradeStat.MaxHealth, b.Upgrade(UpgradeStat.MaxHealth));
                for (int s = 0; s < BrawlerProgressData.UpgradeCount; s++)
                {
                    int max = settings?.Upgrade((UpgradeStat)s)?.maxLevel ?? 99;
                    b.upgrades[s] = Math.Max(0, Math.Min(b.upgrades[s], max));
                }
                if (settings != null)
                {
                    int points = settings.UpgradePointsAt(settings.brawlerCurve.LevelForTotal(b.xp));
                    if (b.TotalUpgrades > points)
                        for (int s = 0; s < BrawlerProgressData.UpgradeCount; s++) b.upgrades[s] = 0;
                }
                if (!string.IsNullOrEmpty(b.equippedSkin) && !p.unlockedSkins.Contains(b.equippedSkin)) b.equippedSkin = "";
            }

            seen.Clear();
            for (int i = p.pets.Count - 1; i >= 0; i--)
            {
                var pet = p.pets[i];
                if (pet == null || string.IsNullOrEmpty(pet.petId) || !seen.Add(pet.petId)) { p.pets.RemoveAt(i); continue; }
                pet.xp = Math.Max(0, pet.xp);
                if (settings != null) pet.xp = Math.Min(pet.xp, settings.petCurve.MaxTotalXp);
                pet.claimedLevels = pet.claimedLevels ?? new List<int>();
                pet.equippedSkin = pet.equippedSkin ?? "";
                pet.seenStage = Math.Max(1, Math.Min(3, pet.seenStage));
                if (!string.IsNullOrEmpty(pet.equippedSkin) && !p.unlockedSkins.Contains(pet.equippedSkin)) pet.equippedSkin = "";
            }

            var equipped = p.Pet(p.equippedPetId, false);
            if (equipped == null || !equipped.unlocked) p.equippedPetId = "";

            seen.Clear();
            for (int i = p.quests.Count - 1; i >= 0; i--)
            {
                var q = p.quests[i];
                if (q == null || string.IsNullOrEmpty(q.questId) || !seen.Add(q.questId)) { p.quests.RemoveAt(i); continue; }
                q.progress = Math.Max(0, q.progress);
                q.periodKey = q.periodKey ?? "";
                q.distinct = Dedupe(q.distinct);
                if (q.claimed) q.completed = true;
            }
        }

        static List<string> Dedupe(List<string> list)
        {
            var result = new List<string>();
            if (list == null) return result;
            var seen = new HashSet<string>();
            foreach (var s in list)
                if (!string.IsNullOrEmpty(s) && seen.Add(s)) result.Add(s);
            return result;
        }
    }
}
