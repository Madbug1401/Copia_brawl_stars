using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Onde o perfil é guardado (ficheiro no disco ou memória para testes).</summary>
    public interface IProfileStorage
    {
        bool TryRead(string slot, out string text);
        void Write(string slot, string text);
        bool Exists(string slot);
        void Delete(string slot);
    }

    /// <summary>Ficheiros em Application.persistentDataPath, com escrita atómica (temporário + substituição).</summary>
    public class FileProfileStorage : IProfileStorage
    {
        readonly string m_Directory;

        public FileProfileStorage(string directory)
        {
            m_Directory = directory;
        }

        string PathFor(string slot) => Path.Combine(m_Directory, slot);

        public bool TryRead(string slot, out string text)
        {
            text = null;
            try
            {
                var p = PathFor(slot);
                if (!File.Exists(p)) return false;
                text = File.ReadAllText(p, Encoding.UTF8);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FPS-LAN] Não foi possível ler " + slot + ": " + e.Message);
                return false;
            }
        }

        public void Write(string slot, string text)
        {
            Directory.CreateDirectory(m_Directory);
            var final = PathFor(slot);
            var tmp = final + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            // Substituição atómica: nunca fica um ficheiro meio escrito no lugar do perfil
            if (File.Exists(final)) File.Replace(tmp, final, null);
            else File.Move(tmp, final);
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));

        public void Delete(string slot)
        {
            try
            {
                if (File.Exists(PathFor(slot))) File.Delete(PathFor(slot));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FPS-LAN] Não foi possível apagar " + slot + ": " + e.Message);
            }
        }
    }

    /// <summary>Armazenamento em memória (testes).</summary>
    public class MemoryProfileStorage : IProfileStorage
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool TryRead(string slot, out string text) => Files.TryGetValue(slot, out text);
        public void Write(string slot, string text) => Files[slot] = text;
        public bool Exists(string slot) => Files.ContainsKey(slot);
        public void Delete(string slot) => Files.Remove(slot);
    }

    public enum ProfileLoadResult
    {
        Loaded,
        CreatedNew,
        RecoveredFromBackup,
        ResetAfterCorruption,
        Migrated,
    }

    /// <summary>
    /// Guarda/carrega o perfil local. Formato: envelope JSON { format, version, checksum, payload } em que o checksum
    /// (SHA-256 do payload) deteta ficheiros corrompidos — NÃO é proteção contra batota: um save local é sempre
    /// manipulável. Para progressão competitiva entre computadores, ver IAuthoritativeProgressionStore.
    /// Recuperação: ficheiro principal → cópia .bak → perfil novo (o corrompido fica guardado como .corrupt).
    /// </summary>
    public class SaveManager
    {
        public const int EnvelopeFormat = 1;
        const int MaxProcessedMatches = 200;
        const int MaxClaimKeys = 600;
        const int MaxRewardLog = 40;

        [Serializable]
        class Envelope
        {
            public int format;
            public int version;
            public string checksum;
            public string payload;
        }

        readonly IProfileStorage m_Storage;
        readonly string m_Slot;

        public string Slot => m_Slot;
        public string BackupSlot => m_Slot + ".bak";
        public ProfileLoadResult LastLoadResult { get; private set; }

        public SaveManager(IProfileStorage storage, string profileName = "fpslan_profile")
        {
            m_Storage = storage;
            m_Slot = SanitizeName(profileName) + ".json";
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "fpslan_profile";
            var sb = new StringBuilder();
            foreach (var c in name)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            return sb.Length == 0 ? "fpslan_profile" : sb.ToString();
        }

        // ------------------------------------------------------------------ carregar

        public ProfileData Load()
        {
            if (TryLoadSlot(m_Slot, out var data, out bool migrated))
            {
                LastLoadResult = migrated ? ProfileLoadResult.Migrated : ProfileLoadResult.Loaded;
                return data;
            }

            bool hadMain = m_Storage.Exists(m_Slot);
            if (hadMain)
            {
                // Guarda o ficheiro estragado para diagnóstico antes de o substituir
                if (m_Storage.TryRead(m_Slot, out var broken)) m_Storage.Write(m_Slot + ".corrupt", broken);
                Debug.LogWarning("[FPS-LAN] Perfil corrompido; a tentar a cópia de segurança.");
            }

            if (TryLoadSlot(BackupSlot, out data, out _))
            {
                LastLoadResult = ProfileLoadResult.RecoveredFromBackup;
                Save(data);
                return data;
            }

            data = NewProfile();
            LastLoadResult = hadMain ? ProfileLoadResult.ResetAfterCorruption : ProfileLoadResult.CreatedNew;
            Save(data);
            return data;
        }

        bool TryLoadSlot(string slot, out ProfileData data, out bool migrated)
        {
            data = null;
            migrated = false;
            if (!m_Storage.TryRead(slot, out var text) || string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                var env = JsonUtility.FromJson<Envelope>(text);
                if (env == null || env.format != EnvelopeFormat || string.IsNullOrEmpty(env.payload)) return false;
                if (!string.Equals(env.checksum, Checksum(env.payload), StringComparison.OrdinalIgnoreCase)) return false;
                data = JsonUtility.FromJson<ProfileData>(env.payload);
                if (data == null) return false;
                if (data.version > ProfileData.CurrentVersion) return false; // save de uma versão mais nova do jogo
                if (data.version < ProfileData.CurrentVersion)
                {
                    Migrate(data);
                    migrated = true;
                }
                Sanitize(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FPS-LAN] Save inválido (" + slot + "): " + e.Message);
                data = null;
                return false;
            }
        }

        /// <summary>Migração de versões antigas do save (cada passo sobe uma versão).</summary>
        public static void Migrate(ProfileData data)
        {
            // v0 → v1: versão inicial; garante listas e id do jogador.
            if (data.version < 1)
            {
                data.version = 1;
            }
            // Futuras: if (data.version < 2) { ...; data.version = 2; }
            data.version = ProfileData.CurrentVersion;
        }

        /// <summary>Valida e corrige valores impossíveis (negativos, listas nulas, duplicados, tamanhos).</summary>
        public static void Sanitize(ProfileData d)
        {
            if (string.IsNullOrEmpty(d.playerId)) d.playerId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(d.createdUtc)) d.createdUtc = DateTime.UtcNow.ToString("o");
            d.playerXp = Math.Max(0, d.playerXp);
            d.coins = Math.Max(0, d.coins);
            d.upraFood = Mathf.Clamp(d.upraFood, 0, PetFoodRules.MaxStock);
            d.characters = d.characters ?? new List<CharacterProgress>();
            d.pets = d.pets ?? new List<PetProgress>();
            d.unlockedSkins = Dedup(d.unlockedSkins);
            d.unlockedCosmetics = Dedup(d.unlockedCosmetics);
            d.unlockedCharacters = Dedup(d.unlockedCharacters);
            d.equippedSkins = d.equippedSkins ?? new List<EquippedSkin>();
            d.quests = d.quests ?? new List<QuestProgress>();
            d.completedAchievements = Dedup(d.completedAchievements);
            d.claimedRewardKeys = Trim(Dedup(d.claimedRewardKeys), MaxClaimKeys);
            d.processedMatchIds = Trim(Dedup(d.processedMatchIds), MaxProcessedMatches);
            d.pendingLevelRewards = d.pendingLevelRewards ?? new List<int>();
            d.stats = d.stats ?? new LifetimeStats();
            d.rewardLog = d.rewardLog ?? new List<RewardLogEntry>();
            if (d.rewardLog.Count > MaxRewardLog) d.rewardLog.RemoveRange(0, d.rewardLog.Count - MaxRewardLog);
            d.equippedPetId = d.equippedPetId ?? "";

            var seen = new HashSet<string>();
            d.characters.RemoveAll(c => c == null || string.IsNullOrEmpty(c.brawlerId) || !seen.Add("c:" + c.brawlerId));
            foreach (var c in d.characters)
            {
                c.xp = Math.Max(0, c.xp);
                if (c.upgrades == null || c.upgrades.Length != 6) Array.Resize(ref c.upgrades, 6);
                for (int i = 0; i < c.upgrades.Length; i++) c.upgrades[i] = Mathf.Clamp(c.upgrades[i], 0, 20);
            }
            d.pets.RemoveAll(p => p == null || string.IsNullOrEmpty(p.petId) || !seen.Add("p:" + p.petId));
            foreach (var p in d.pets)
            {
                p.xp = Math.Max(0, p.xp);
                p.equippedSkinId = p.equippedSkinId ?? "";
            }
            d.quests.RemoveAll(q => q == null || string.IsNullOrEmpty(q.questId));
            foreach (var q in d.quests)
            {
                q.progress = Math.Max(0, q.progress);
                q.distinct = q.distinct ?? new List<string>();
            }
        }

        static List<string> Dedup(List<string> list)
        {
            var result = new List<string>();
            if (list == null) return result;
            var set = new HashSet<string>();
            foreach (var s in list)
                if (!string.IsNullOrEmpty(s) && set.Add(s)) result.Add(s);
            return result;
        }

        static List<string> Trim(List<string> list, int max)
        {
            if (list.Count > max) list.RemoveRange(0, list.Count - max);
            return list;
        }

        public static ProfileData NewProfile()
        {
            var d = new ProfileData
            {
                version = ProfileData.CurrentVersion,
                playerId = Guid.NewGuid().ToString("N"),
                createdUtc = DateTime.UtcNow.ToString("o"),
            };
            Sanitize(d);
            return d;
        }

        // ------------------------------------------------------------------ guardar

        public void Save(ProfileData data)
        {
            if (data == null) return;
            Sanitize(data);
            data.version = ProfileData.CurrentVersion;
            data.lastSavedUtc = DateTime.UtcNow.ToString("o");
            var payload = JsonUtility.ToJson(data);
            var env = new Envelope { format = EnvelopeFormat, version = data.version, checksum = Checksum(payload), payload = payload };
            var text = JsonUtility.ToJson(env, true);
            try
            {
                // O ficheiro válido anterior passa a ser a cópia de segurança
                if (m_Storage.TryRead(m_Slot, out var previous) && TryLoadText(previous)) m_Storage.Write(BackupSlot, previous);
                m_Storage.Write(m_Slot, text);
            }
            catch (Exception e)
            {
                Debug.LogError("[FPS-LAN] Falhou a gravação do perfil: " + e.Message);
            }
        }

        static bool TryLoadText(string text)
        {
            try
            {
                var env = JsonUtility.FromJson<Envelope>(text);
                return env != null && env.format == EnvelopeFormat && !string.IsNullOrEmpty(env.payload) &&
                    string.Equals(env.checksum, Checksum(env.payload), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string Checksum(string payload)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(payload ?? ""));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Ponto de migração para armazenamento autoritativo: um serviço (servidor dedicado/backend) guardaria o perfil e
    /// aplicaria os resultados das partidas assinados pelo host. Enquanto a progressão for local, o jogo usa o SaveManager;
    /// uma implementação futura deste contrato substitui a gravação local sem mudar os gestores.
    /// </summary>
    public interface IAuthoritativeProgressionStore
    {
        bool IsAvailable { get; }
        void Upload(ProfileData profile);
        bool TryDownload(string playerId, out ProfileData profile);
    }
}
