using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Relógio injetável (missões diárias/semanais; testes usam um relógio fixo).</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public class FixedClock : IClock
    {
        public DateTime Now;
        public FixedClock(DateTime utc) { Now = utc; }
        public DateTime UtcNow => Now;
    }

    public enum FeedbackType
    {
        LevelUp,
        CharacterLevelUp,
        CharacterUnlocked,
        PetUnlocked,
        PetLevelUp,
        PetEvolved,
        SkinUnlocked,
        QuestCompleted,
        AchievementUnlocked,
        RewardClaimed,
        CoinsEarned,
        UpgradePurchased,
        Victory,
        Headshot,
        SpecialReady,
        Info,
    }

    /// <summary>Evento de feedback visual/sonoro para a interface (subida de nível, desbloqueios, missões...).</summary>
    public struct FeedbackEvent
    {
        public FeedbackType type;
        public string title;
        public string subtitle;
        public Color color;

        public FeedbackEvent(FeedbackType type, string title, string subtitle, Color color)
        {
            this.type = type;
            this.title = title;
            this.subtitle = subtitle;
            this.color = color;
        }
    }

    /// <summary>
    /// Junta os sistemas de Game Experience (configuração, catálogos, perfil e gestores) com dependências explícitas,
    /// para poderem ser testados sem cena. Em jogo usa-se GameExperience.Current.
    /// </summary>
    public sealed class GameExperienceContext
    {
        public ProgressionConfig Config { get; }
        public PetCatalog PetCatalog { get; }
        public SkinCatalog SkinCatalog { get; }
        public QuestCatalog QuestCatalog { get; }
        public SaveManager Saves { get; }
        public IClock Clock { get; }
        public ProfileData Profile { get; private set; }

        public InventoryManager Inventory { get; }
        public RewardManager Rewards { get; }
        public XPManager XP { get; }
        public PlayerProgressionManager Progression { get; }
        public CharacterUpgradeManager Upgrades { get; }
        public PetManager Pets { get; }
        public PetProgressionManager PetProgression { get; }
        public PetUnlockManager PetUnlocks { get; }
        public SkinManager Skins { get; }
        public QuestManager Quests { get; }

        /// <summary>Feedback para a interface (toasts, sons). Também fica em RecentFeedback.</summary>
        public event Action<FeedbackEvent> Feedback;
        /// <summary>O perfil mudou (equipar, comprar, recompensas): a UI e o loadout de rede atualizam-se.</summary>
        public event Action ProfileChanged;

        public readonly List<FeedbackEvent> RecentFeedback = new List<FeedbackEvent>();
        public bool SuppressFeedback { get; set; }

        public GameExperienceContext(ProgressionConfig config, PetCatalog pets, SkinCatalog skins, QuestCatalog quests,
            IProfileStorage storage, string profileName, IClock clock)
        {
            Config = config != null ? config : ProgressionConfig.CreateDefault();
            PetCatalog = pets != null ? pets : PetCatalog.CreateDefault();
            SkinCatalog = skins != null ? skins : SkinCatalog.CreateDefault();
            QuestCatalog = quests != null ? quests : QuestCatalog.CreateDefault();
            Clock = clock ?? new SystemClock();
            Saves = new SaveManager(storage ?? new MemoryProfileStorage(), profileName);
            Profile = Saves.Load();

            Inventory = new InventoryManager(this);
            Rewards = new RewardManager(this);
            XP = new XPManager(this);
            Progression = new PlayerProgressionManager(this);
            Upgrades = new CharacterUpgradeManager(this);
            Pets = new PetManager(this);
            PetProgression = new PetProgressionManager(this);
            PetUnlocks = new PetUnlockManager(this);
            Skins = new SkinManager(this);
            Quests = new QuestManager(this);

            // Conteúdo inicial e desbloqueios por regras (sem feedback no arranque)
            SuppressFeedback = true;
            EnsureStarterContent();
            SuppressFeedback = false;
            Save();
        }

        void EnsureStarterContent()
        {
            foreach (var b in BrawlerIds())
                if (Progression.IsCharacterUnlockedByRule(b)) Inventory.UnlockCharacter(b);
            PetUnlocks.CheckAll();
            Skins.CheckRuleUnlocks();
            Quests.RefreshPeriods();
            if (string.IsNullOrEmpty(Profile.equippedPetId))
            {
                // O primeiro pet desbloqueado fica equipado (o jogador pode retirar no menu)
                foreach (var p in PetCatalog.pets)
                    if (p != null && Inventory.IsPetUnlocked(p.petId))
                    {
                        Profile.equippedPetId = p.petId;
                        break;
                    }
            }
        }

        /// <summary>Ids das personagens conhecidas (registry se existir, senão as das regras de desbloqueio).</summary>
        public IEnumerable<string> BrawlerIds()
        {
            var seen = new HashSet<string>();
            foreach (var b in BrawlerRegistry.AllQuiet)
                if (b != null && seen.Add(b.brawlerId)) yield return b.brawlerId;
            foreach (var c in QuestCatalog.characterUnlocks)
                if (c != null && !string.IsNullOrEmpty(c.brawlerId) && seen.Add(c.brawlerId)) yield return c.brawlerId;
        }

        public void Emit(FeedbackType type, string title, string subtitle, Color color)
        {
            if (SuppressFeedback) return;
            var e = new FeedbackEvent(type, title, subtitle, color);
            RecentFeedback.Add(e);
            if (RecentFeedback.Count > 30) RecentFeedback.RemoveAt(0);
            Feedback?.Invoke(e);
        }

        public void Log(string source, string text)
        {
            Profile.rewardLog.Add(new RewardLogEntry { utc = Clock.UtcNow.ToString("o"), source = source, text = text });
            if (Profile.rewardLog.Count > 40) Profile.rewardLog.RemoveAt(0);
        }

        public void Save()
        {
            Saves.Save(Profile);
            ProfileChanged?.Invoke();
        }

        /// <summary>Repõe um perfil novo (menu "apagar progresso" e testes).</summary>
        public void ResetProfile()
        {
            Profile = SaveManager.NewProfile();
            SuppressFeedback = true;
            EnsureStarterContent();
            SuppressFeedback = false;
            Save();
        }
    }

    /// <summary>Acesso global em runtime (catálogos em Resources, perfil em Application.persistentDataPath).</summary>
    public static class GameExperience
    {
        static GameExperienceContext s_Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Current = null;

        public static GameExperienceContext Current => s_Current ?? (s_Current = CreateRuntime());

        /// <summary>Substitui o contexto (testes).</summary>
        public static void Override(GameExperienceContext context) => s_Current = context;

        static GameExperienceContext CreateRuntime()
        {
            var cfg = Resources.Load<ProgressionConfig>(ProgressionConfig.ResourcePath);
            var pets = Resources.Load<PetCatalog>(PetCatalog.ResourcePath);
            var skins = Resources.Load<SkinCatalog>(SkinCatalog.ResourcePath);
            var quests = Resources.Load<QuestCatalog>(QuestCatalog.ResourcePath);
            if (cfg == null || pets == null || skins == null || quests == null)
                Debug.LogWarning("[FPS-LAN] Dados de Game Experience em falta em Resources/GameExperience; a usar valores predefinidos. " +
                                 "Menu FPS-LAN → Gerar dados de Game Experience.");
            var storage = new FileProfileStorage(Application.persistentDataPath);
            return new GameExperienceContext(cfg, pets, skins, quests, storage, ProfileNameFromCommandLine(), new SystemClock());
        }

        /// <summary>"-fpslan-profile nome" permite perfis separados (ex.: host e cliente no mesmo PC).</summary>
        static string ProfileNameFromCommandLine()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-fpslan-profile") return "fpslan_profile_" + args[i + 1];
            }
            catch { }
            return "fpslan_profile";
        }

        /// <summary>Loadout de rede do jogador local para uma personagem (skin, pet, níveis e melhorias).</summary>
        public static PlayerLoadout BuildLoadout(string brawlerId) => BuildLoadoutFor(Current, brawlerId);

        public static PlayerLoadout BuildLoadoutFor(GameExperienceContext ctx, string brawlerId)
        {
            var l = new PlayerLoadout();
            var skin = ctx.Skins.EquippedSkinFor(SkinTarget.Brawler, brawlerId);
            l.skinHash = skin != null ? skin.StableHash : 0;
            var pet = ctx.Pets.EquippedPet;
            if (pet != null)
            {
                l.petHash = pet.StableHash;
                l.petLevel = (byte)Mathf.Clamp(ctx.PetProgression.Level(pet.petId), 1, 255);
                var petSkin = ctx.Skins.EquippedSkinFor(SkinTarget.Pet, pet.petId);
                l.petSkinHash = petSkin != null ? petSkin.StableHash : 0;
            }
            l.characterLevel = (byte)Mathf.Clamp(ctx.Progression.CharacterLevel(brawlerId), 1, 255);
            foreach (UpgradeStat stat in Enum.GetValues(typeof(UpgradeStat)))
                l.SetUpgrade(stat, ctx.Upgrades.Level(brawlerId, stat));
            return l;
        }
    }
}
