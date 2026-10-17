using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Gravação via JsonUtility (formato da gravação local).</summary>
    public sealed class UnityJsonProfileSerializer : IProfileSerializer
    {
        public string Serialize(PlayerProfileData profile) => JsonUtility.ToJson(profile, true);
        public PlayerProfileData Deserialize(string text) => JsonUtility.FromJson<PlayerProfileData>(text);
    }

    /// <summary>
    /// Ponto de entrada da progressão do jogador local: carrega o perfil, liga os managers (XP, melhorias, pets,
    /// skins, inventário, missões, recompensas) e grava as alterações. A UI e o PlayerNetwork falam com isto.
    /// Só corre do lado de cada jogador (o perfil é local); as recompensas de partida chegam do host.
    /// </summary>
    public static class GameExperience
    {
        static ExperienceContext s_Ctx;
        static SaveManager s_Save;
        static float s_LastSaveTime;

        public static InventoryManager Inventory { get; private set; }
        public static RewardManager Rewards { get; private set; }
        public static PlayerProgressionManager Progression { get; private set; }
        public static CharacterUpgradeManager Upgrades { get; private set; }
        public static QuestManager Quests { get; private set; }
        public static PetProgressionManager PetProgression { get; private set; }
        public static PetUnlockManager PetUnlocks { get; private set; }
        public static PetManager Pets { get; private set; }
        public static SkinManager Skins { get; private set; }

        public static SaveLoadStatus LoadStatus { get; private set; }

        /// <summary>Resultado da última partida creditada (ecrã de resultados).</summary>
        public static MatchProgressReport LastReport { get; private set; }
        public static bool HasUnseenReport { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Ctx = null;
            s_Save = null;
            LastReport = default;
            HasUnseenReport = false;
        }

        public static bool IsReady => s_Ctx != null;

        public static ExperienceContext Context
        {
            get
            {
                EnsureInit();
                return s_Ctx;
            }
        }

        public static PlayerProfileData Profile => Context.profile;
        public static ExperienceCatalog Catalog => Context.catalog;

        public static void EnsureInit()
        {
            if (s_Ctx != null) return;
            var catalog = GameExperienceDatabase.Catalog;
            s_Save = new SaveManager(Application.persistentDataPath, new UnityJsonProfileSerializer()) { Catalog = catalog };
            var profile = s_Save.Load(() => DateTime.UtcNow, out var status);
            LoadStatus = status;
            if (status == SaveLoadStatus.RecoveredFromBackup)
                Debug.LogWarning("[FPS-LAN] Perfil recuperado da cópia de segurança: " + s_Save.LastError);
            else if (status == SaveLoadStatus.ResetCorrupt)
                Debug.LogWarning("[FPS-LAN] Perfil ilegível; criado um novo (o antigo ficou guardado como .corrupt): " + s_Save.LastError);

            Build(profile, catalog);
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        /// <summary>Liga os managers a um perfil (também usado pelos testes de integração).</summary>
        public static ExperienceContext Build(PlayerProfileData profile, ExperienceCatalog catalog)
        {
            s_Ctx = new ExperienceContext(profile, catalog);
            Inventory = new InventoryManager(s_Ctx);
            Rewards = new RewardManager(s_Ctx, Inventory);
            Progression = new PlayerProgressionManager(s_Ctx, Rewards);
            Rewards.Progression = Progression;
            Upgrades = new CharacterUpgradeManager(s_Ctx, Inventory, Progression);
            PetProgression = new PetProgressionManager(s_Ctx, Rewards);
            PetUnlocks = new PetUnlockManager(s_Ctx, Inventory, Progression);
            Pets = new PetManager(s_Ctx, Inventory, PetProgression);
            Skins = new SkinManager(s_Ctx, Inventory, Progression);
            Quests = new QuestManager(s_Ctx, Rewards);
            Progression.Pets = PetProgression;
            Progression.Quests = Quests;
            Quests.StateProvider = metric =>
            {
                switch (metric)
                {
                    case QuestMetric.PlayerLevel: return Progression.PlayerLevel;
                    case QuestMetric.PetLevel: return PetProgression.BestLevel;
                    case QuestMetric.PetsUnlocked: return Inventory.PetsUnlocked;
                    default: return 0;
                }
            };
            Progression.OnPlayerLevelChanged = _ =>
            {
                PetUnlocks.CheckAutomaticUnlocks();
                Skins.CheckAutomaticUnlocks();
            };

            PetUnlocks.CheckAutomaticUnlocks();
            Skins.CheckAutomaticUnlocks();
            Quests.Refresh();
            // Primeiro arranque: equipa o pet inicial para o jogador ver a funcionalidade
            if (string.IsNullOrEmpty(profile.equippedPetId) && profile.stats.matches == 0)
                foreach (var pet in catalog.pets)
                    if (pet != null && Inventory.OwnsPet(pet.petId)) { Pets.Equip(pet.petId); break; }
            // Os desbloqueios iniciais não precisam de notificação
            s_Ctx.events.Clear();
            return s_Ctx;
        }

        static void OnQuitting() => Save(true);

        /// <summary>Grava se houver alterações (com um pequeno intervalo mínimo, exceto se forçado).</summary>
        public static void Save(bool force = false)
        {
            if (s_Ctx == null || s_Save == null || !s_Ctx.dirty) return;
            if (!force && Time.unscaledTime - s_LastSaveTime < 0.5f) return;
            s_LastSaveTime = Time.unscaledTime;
            if (s_Save.Save(s_Ctx.profile)) s_Ctx.dirty = false;
            else Debug.LogWarning("[FPS-LAN] Não foi possível gravar o perfil: " + s_Save.LastError);
        }

        /// <summary>Depois de uma mudança feita no menu: grava e atualiza o loadout na partida, se estiver numa.</summary>
        public static void Commit()
        {
            Save(true);
            if (PlayerNetwork.Local != null) PlayerNetwork.Local.SubmitLocalLoadout();
        }

        public static List<ExperienceEvent> DrainEvents()
        {
            var list = new List<ExperienceEvent>();
            if (s_Ctx == null) return list;
            list.AddRange(s_Ctx.events);
            s_Ctx.events.Clear();
            return list;
        }

        // ------------------------------------------------------------------ loadout para a rede

        public static PlayerLoadout BuildLoadout(int heroIndex)
        {
            EnsureInit();
            var catalog = s_Ctx.catalog;
            var l = PlayerLoadout.Empty;
            string brawlerId = catalog.BrawlerIdAt(heroIndex);
            if (string.IsNullOrEmpty(brawlerId)) return l;
            l.BrawlerIndex = heroIndex;
            l.BrawlerLevel = (byte)Mathf.Clamp(Progression.BrawlerLevel(brawlerId), 1, 255);
            for (int i = 0; i < BrawlerProgressData.UpgradeCount; i++)
                l.SetUpgrade((UpgradeStat)i, Upgrades.Level(brawlerId, (UpgradeStat)i));
            l.SkinIndex = (short)catalog.SkinIndex(Skins.Equipped(brawlerId));

            string petId = Pets.EquippedPetId;
            int petIndex = catalog.PetIndex(petId);
            if (petIndex >= 0)
            {
                l.PetIndex = (short)petIndex;
                l.PetLevel = (byte)Mathf.Clamp(PetProgression.Level(petId), 1, 255);
                l.PetSkinIndex = (short)catalog.SkinIndex(Pets.EquippedSkin(petId));
            }
            return l;
        }

        // ------------------------------------------------------------------ recompensas de partida

        /// <summary>Cliente: recompensa enviada pelo host. Creditada uma vez por chave de partida.</summary>
        public static void OnMatchReward(MatchRewardMessage msg)
        {
            EnsureInit();
            var catalog = s_Ctx.catalog;
            var result = new MatchResultData
            {
                matchKey = msg.MatchKey.ToString(),
                brawlerId = catalog.BrawlerIdAt(msg.BrawlerIndex),
                petId = catalog.PetAt(msg.PetIndex)?.petId ?? "",
                ruleset = (MatchRuleset)msg.Ruleset,
                stats = new MatchStatLine
                {
                    valid = msg.Valid != 0,
                    victory = msg.Victory != 0,
                    kills = msg.Kills,
                    deaths = msg.Deaths,
                    assists = msg.Assists,
                    objectives = msg.Objectives,
                    headshots = msg.Headshots,
                    damage = msg.Damage,
                    seconds = msg.Seconds,
                    usedPet = msg.UsedPet != 0,
                },
                xp = new MatchXpBreakdown
                {
                    participation = msg.XpParticipation,
                    kills = msg.XpKills,
                    assists = msg.XpAssists,
                    victory = msg.XpVictory,
                    objectives = msg.XpObjectives,
                    total = msg.XpTotal,
                    brawlerXp = msg.BrawlerXp,
                    petXp = msg.PetXp,
                    coins = msg.Coins,
                },
            };

            var report = Progression.ApplyMatch(result);
            if (report.duplicate) return;
            LastReport = report;
            HasUnseenReport = true;
            Save(true);
            if (result.stats.victory) Sfx.Play2D(Sound.Victory, 0.8f);
        }
    }
}
