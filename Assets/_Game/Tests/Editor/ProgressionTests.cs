using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace FPSLAN.Tests
{
    /// <summary>XP, níveis, persistência, melhorias, pets, skins, missões e recompensas (sem rede nem cena).</summary>
    public class ProgressionTests
    {
        static readonly DateTime Day0 = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc); // quarta-feira

        MemoryProfileStorage m_Storage;
        FixedClock m_Clock;

        [SetUp]
        public void SetUp()
        {
            m_Storage = new MemoryProfileStorage();
            m_Clock = new FixedClock(Day0);
        }

        GameExperienceContext NewCtx(string profile = "test") =>
            new GameExperienceContext(ProgressionConfig.CreateDefault(), PetCatalog.CreateDefault(), SkinCatalog.CreateDefault(), QuestCatalog.CreateDefault(),
                m_Storage, profile, m_Clock);

        static MatchResultData Result(ProgressionConfig cfg, string matchId, string brawler = "sniper", short kills = 3, short assists = 2, bool won = true,
            short objectives = 1, short deaths = 1, int petHash = 0, bool participated = true, short killsOnBots = 0)
        {
            var r = new MatchResultData
            {
                matchId = new FixedString64Bytes(matchId),
                brawlerId = new FixedString32Bytes(brawler),
                petHash = petHash,
                participated = participated,
                won = won,
                kills = kills,
                killsOnBots = killsOnBots,
                assists = assists,
                deaths = deaths,
                objectives = objectives,
                headshots = 1,
                damageDealt = 250,
                secondsPlayed = 300,
            };
            XPManager.ComputeRewards(cfg, ref r);
            return r;
        }

        // ------------------------------------------------------------------ níveis

        [Test]
        public void LevelCurvesHaveRequestedCapsAndGrowGradually()
        {
            var cfg = ProgressionConfig.CreateDefault();
            Assert.AreEqual(50, cfg.playerLevels.maxLevel);
            Assert.AreEqual(10, cfg.characterLevels.maxLevel);
            Assert.AreEqual(10, cfg.petLevels.maxLevel);
            foreach (var curve in new[] { cfg.playerLevels, cfg.characterLevels, cfg.petLevels })
            {
                for (int l = 2; l < curve.maxLevel; l++) Assert.Greater(curve.XpToNext(l), curve.XpToNext(l - 1), "cresce gradualmente");
                for (int l = 1; l <= curve.maxLevel; l++)
                {
                    long total = curve.TotalXpForLevel(l);
                    Assert.AreEqual(l, curve.LevelForXp(total), "limite do nível " + l);
                    if (total > 0) Assert.AreEqual(l - 1, curve.LevelForXp(total - 1));
                }
                Assert.AreEqual(curve.maxLevel, curve.LevelForXp(long.MaxValue / 4));
            }
        }

        // ------------------------------------------------------------------ XP por partida

        [Test]
        public void MatchXpUsesConfiguredValues()
        {
            var cfg = ProgressionConfig.CreateDefault();
            var r = Result(cfg, "m1", kills: 3, assists: 2, won: true, objectives: 1);
            Assert.AreEqual(50, r.xpParticipation);
            Assert.AreEqual(36, r.xpKills);
            Assert.AreEqual(12, r.xpAssists);
            Assert.AreEqual(75, r.xpVictory);
            Assert.AreEqual(15, r.xpObjectives);
            Assert.AreEqual(188, r.XpTotal);
            Assert.Greater(r.coins, 0);
        }

        [Test]
        public void InvalidParticipationGivesNothing()
        {
            var cfg = ProgressionConfig.CreateDefault();
            var r = Result(cfg, "m1", participated: false);
            Assert.AreEqual(0, r.XpTotal);
            Assert.AreEqual(0, r.coins);
        }

        [Test]
        public void BotKillsAreDiscountedAndKillsCapped()
        {
            var cfg = ProgressionConfig.CreateDefault();
            var r = Result(cfg, "m1", kills: 2, killsOnBots: 2, assists: 0, won: false, objectives: 0);
            Assert.AreEqual(Mathf.RoundToInt(2 * cfg.xpPerKill * cfg.xpVsBotsMultiplier), r.xpKills);
            var capped = Result(cfg, "m2", kills: 500, assists: 0, won: false, objectives: 0);
            Assert.AreEqual(cfg.maxKillsCounted * cfg.xpPerKill, capped.xpKills);
        }

        [Test]
        public void ParticipationNeedsTimeAndActivity()
        {
            var cfg = ProgressionConfig.CreateDefault();
            Assert.IsTrue(XPManager.IsParticipationValid(cfg, 120f, 300f, 10, 0f));
            Assert.IsTrue(XPManager.IsParticipationValid(cfg, 120f, 300f, 0, 50f));
            Assert.IsFalse(XPManager.IsParticipationValid(cfg, 120f, 300f, 0, 0f), "parado e sem dano");
            Assert.IsFalse(XPManager.IsParticipationValid(cfg, 10f, 300f, 100, 100f), "pouco tempo");
            Assert.IsTrue(XPManager.IsParticipationValid(cfg, 12f, 20f, 100, 0f), "partida curta: fração da duração");
        }

        [Test]
        public void MatchResultIsAppliedOnlyOnce()
        {
            var ctx = NewCtx();
            var r = Result(ctx.Config, "match-A");
            var report = ctx.Progression.ApplyMatchResult(r);
            Assert.IsFalse(report.duplicate);
            long xp = ctx.Profile.playerXp;
            int coins = ctx.Profile.coins;
            Assert.AreEqual(188, xp);
            Assert.Greater(ctx.Profile.Character("sniper", false).xp, 0);

            var again = ctx.Progression.ApplyMatchResult(r);
            Assert.IsTrue(again.duplicate);
            Assert.AreEqual(xp, ctx.Profile.playerXp);
            Assert.AreEqual(coins, ctx.Profile.coins);

            // Mesmo depois de reiniciar o jogo (perfil recarregado do disco)
            var reloaded = NewCtx();
            Assert.IsTrue(reloaded.Progression.ApplyMatchResult(r).duplicate);
            Assert.AreEqual(xp, reloaded.Profile.playerXp);
        }

        [Test]
        public void ReportShowsBeforeAfterAndUnlocks()
        {
            var ctx = NewCtx();
            var report = ctx.Progression.ApplyMatchResult(Result(ctx.Config, "m1", kills: 10, assists: 5, objectives: 3));
            Assert.AreEqual(0, report.playerXpBefore);
            Assert.AreEqual(ctx.Profile.playerXp, report.playerXpAfter);
            Assert.Greater(report.levelAfter, report.levelBefore, "subiu de nível");
            Assert.IsTrue(report.unlocks.Exists(e => e.type == FeedbackType.LevelUp));
            Assert.AreEqual("sniper", report.brawlerId);
        }

        // ------------------------------------------------------------------ persistência

        [Test]
        public void SaveAndLoadRoundTrip()
        {
            var ctx = NewCtx();
            ctx.Inventory.AddCoins(1234);
            ctx.Progression.AddPlayerXp(500, "teste");
            ctx.Profile.Character("gunslinger", true).SetUpgrade(UpgradeStat.Damage, 2);
            ctx.Inventory.UnlockSkin("skin_ekko_neon");
            ctx.Skins.Equip(SkinTarget.Brawler, "gunslinger", "skin_ekko_neon");
            ctx.Save();

            var loaded = new SaveManager(m_Storage, "test").Load();
            Assert.AreEqual(ctx.Profile.playerId, loaded.playerId);
            Assert.AreEqual(ctx.Profile.coins, loaded.coins);
            Assert.AreEqual(ctx.Profile.playerXp, loaded.playerXp);
            Assert.AreEqual(2, loaded.Character("gunslinger", false).Upgrade(UpgradeStat.Damage));
            Assert.AreEqual("skin_ekko_neon", loaded.EquippedSkin("gunslinger"));
            Assert.Contains("pet_dog", loaded.pets.ConvertAll(p => p.unlocked ? p.petId : ""));
        }

        [Test]
        public void CorruptedSaveRecoversFromBackup()
        {
            var saves = new SaveManager(m_Storage, "slot");
            var first = SaveManager.NewProfile();
            first.coins = 100;
            saves.Save(first);
            var second = SaveManager.NewProfile();
            second.playerId = first.playerId;
            second.coins = 200;
            saves.Save(second); // o primeiro passa a ser a cópia de segurança

            m_Storage.Files[saves.Slot] = "{ isto não é json";
            var loaded = new SaveManager(m_Storage, "slot").Load();
            Assert.AreEqual(100, loaded.coins);
            Assert.IsTrue(m_Storage.Exists(saves.Slot + ".corrupt"), "guarda o ficheiro estragado");
        }

        [Test]
        public void TamperedChecksumIsDetected()
        {
            var saves = new SaveManager(m_Storage, "slot");
            var p = SaveManager.NewProfile();
            p.coins = 10;
            saves.Save(p);
            m_Storage.Files[saves.Slot] = m_Storage.Files[saves.Slot].Replace("\\\"coins\\\":10", "\\\"coins\\\":999999");
            var mgr = new SaveManager(m_Storage, "slot");
            var loaded = mgr.Load();
            Assert.AreNotEqual(999999, loaded.coins);
            Assert.AreNotEqual(ProfileLoadResult.Loaded, mgr.LastLoadResult);
        }

        [Test]
        public void BothFilesCorruptedStartsFreshProfile()
        {
            var saves = new SaveManager(m_Storage, "slot");
            m_Storage.Files[saves.Slot] = "lixo";
            m_Storage.Files[saves.BackupSlot] = "mais lixo";
            var mgr = new SaveManager(m_Storage, "slot");
            var loaded = mgr.Load();
            Assert.AreEqual(ProfileLoadResult.ResetAfterCorruption, mgr.LastLoadResult);
            Assert.AreEqual(0, loaded.coins);
            Assert.IsFalse(string.IsNullOrEmpty(loaded.playerId));
        }

        [Test]
        public void OldVersionIsMigratedAndInvalidValuesSanitized()
        {
            var p = new ProfileData { version = 0, playerId = "", coins = -50, playerXp = -10 };
            p.characters.Add(new CharacterProgress { brawlerId = "sniper", xp = -5, upgrades = new[] { 99, 1 } });
            SaveManager.Migrate(p);
            SaveManager.Sanitize(p);
            Assert.AreEqual(ProfileData.CurrentVersion, p.version);
            Assert.AreEqual(0, p.coins);
            Assert.AreEqual(0, p.playerXp);
            Assert.IsFalse(string.IsNullOrEmpty(p.playerId));
            Assert.AreEqual(6, p.characters[0].upgrades.Length);
            Assert.LessOrEqual(p.characters[0].upgrades[0], 20);
        }

        // ------------------------------------------------------------------ melhorias e equilíbrio

        [Test]
        public void UpgradesNeedCoinsAndCharacterLevel()
        {
            var ctx = NewCtx();
            Assert.AreEqual(UpgradeResult.NotEnoughCoins, ctx.Upgrades.TryUpgrade("sniper", UpgradeStat.Health));
            ctx.Inventory.AddCoins(10000);
            Assert.AreEqual(UpgradeResult.Ok, ctx.Upgrades.TryUpgrade("sniper", UpgradeStat.Health));
            Assert.AreEqual(1, ctx.Upgrades.Level("sniper", UpgradeStat.Health));
            Assert.AreEqual(UpgradeResult.CharacterLevelTooLow, ctx.Upgrades.TryUpgrade("sniper", UpgradeStat.Health), "nível 2 exige nível 3 de personagem");
            ctx.XP.AddCharacterXp("sniper", 100000);
            for (int i = 0; i < 10; i++) ctx.Upgrades.TryUpgrade("sniper", UpgradeStat.Health);
            Assert.AreEqual(ctx.Config.maxUpgradeLevel, ctx.Upgrades.Level("sniper", UpgradeStat.Health));
            Assert.AreEqual(UpgradeResult.MaxLevel, ctx.Upgrades.CanUpgrade("sniper", UpgradeStat.Health));
        }

        [Test]
        public void CompetitiveNormalizesAndCasualIsCapped()
        {
            var cfg = ProgressionConfig.CreateDefault();
            var pets = PetCatalog.CreateDefault();
            var maxed = new PlayerLoadout { characterLevel = 10, petHash = pets.Find("pet_cat").StableHash, petLevel = 10 };
            foreach (UpgradeStat s in Enum.GetValues(typeof(UpgradeStat))) maxed.SetUpgrade(s, 255);

            var comp = StatResolver.Resolve(cfg, pets, maxed, true);
            Assert.AreEqual(1f, comp.healthMultiplier);
            Assert.AreEqual(1f, comp.damageMultiplier);
            Assert.AreEqual(1f, comp.speedMultiplier);
            Assert.AreEqual(1f, comp.reloadTimeMultiplier);
            Assert.AreEqual(1f, comp.superChargeMultiplier);
            Assert.AreEqual(PetAbilityType.None, comp.PetAbility, "competitivo: pets só cosméticos");

            var casual = StatResolver.Resolve(cfg, pets, maxed, false);
            Assert.LessOrEqual(casual.healthMultiplier, 1f + cfg.Tuning(UpgradeStat.Health).casualCap + 1e-4f);
            Assert.LessOrEqual(casual.damageMultiplier, 1f + cfg.Tuning(UpgradeStat.Damage).casualCap + 1e-4f);
            Assert.Greater(casual.healthMultiplier, 1f);
            Assert.LessOrEqual(casual.speedMultiplier, 1.1f, "velocidade com pet e melhoria continua contida");
            var b = ScriptableObject.CreateInstance<BrawlerDefinition>();
            b.stats.maxHealth = 100;
            Assert.LessOrEqual(StatResolver.MaxHealth(b, casual), 112);
            UnityEngine.Object.DestroyImmediate(b);
        }

        [Test]
        public void ClientCannotClaimUpgradesAboveItsCharacterLevel()
        {
            var cfg = ProgressionConfig.CreateDefault();
            var l = new PlayerLoadout { characterLevel = 1 };
            l.SetUpgrade(UpgradeStat.Damage, 5);
            var m = StatResolver.Resolve(cfg, null, l, false);
            Assert.AreEqual(1f + cfg.Tuning(UpgradeStat.Damage).bonusPerLevel * 1, m.damageMultiplier, 1e-4f, "nível 1 só permite melhoria 1");
        }

        // ------------------------------------------------------------------ pets

        [Test]
        public void SixPetsWithStableIdsAndCategories()
        {
            var pets = PetCatalog.CreateDefault();
            var expected = new Dictionary<string, PetCategory>
            {
                { "pet_dog", PetCategory.Support }, { "pet_cat", PetCategory.Agility }, { "pet_pig", PetCategory.Defense },
                { "pet_rooster", PetCategory.Alert }, { "pet_rabbit", PetCategory.Mobility }, { "pet_turtle", PetCategory.Endurance },
            };
            Assert.AreEqual(6, pets.pets.Count);
            foreach (var kv in expected)
            {
                var def = pets.Find(kv.Key);
                Assert.IsNotNull(def, kv.Key);
                Assert.AreEqual(kv.Value, def.category);
                Assert.AreSame(def, pets.FindByHash(def.StableHash));
                Assert.IsFalse(string.IsNullOrEmpty(def.displayName));
            }
        }

        [Test]
        public void PetUnlockMethods()
        {
            var ctx = NewCtx();
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_dog"), "inicial");
            Assert.AreEqual("pet_dog", ctx.Profile.equippedPetId);
            Assert.IsFalse(ctx.Inventory.IsPetUnlocked("pet_cat"));

            // Subir de nível
            ctx.Progression.AddPlayerXp((int)ctx.Config.playerLevels.TotalXpForLevel(3), "teste");
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_cat"));

            // Comprar com moedas
            Assert.IsFalse(ctx.PetUnlocks.TryPurchase("pet_rabbit"));
            int before = ctx.Inventory.Coins;
            ctx.Inventory.AddCoins(600);
            Assert.IsTrue(ctx.PetUnlocks.TryPurchase("pet_rabbit"));
            Assert.AreEqual(before, ctx.Inventory.Coins, "gastou as 600 moedas");
            Assert.IsFalse(ctx.PetUnlocks.TryPurchase("pet_rabbit"), "não compra duas vezes");

            // Desafio (terminar uma partida sem morrer) → tartaruga
            Assert.IsFalse(ctx.Inventory.IsPetUnlocked("pet_turtle"));
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "flawless", deaths: 0));
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_turtle"));

            // Conquista (25 assistências) → galo
            ctx.Profile.stats.assists = 24;
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "assists", assists: 1));
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_rooster"));

            // Missões (3 concluídas) → porco
            Assert.IsFalse(ctx.Inventory.IsPetUnlocked("pet_pig"));
            ctx.Profile.questsCompletedTotal = 3;
            ctx.PetUnlocks.CheckAll();
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_pig"));
        }

        [Test]
        public void PetEvolutionStagesAndMilestones()
        {
            for (int l = 1; l <= 3; l++) Assert.AreEqual(0, PetDefinition.StageFor(l));
            for (int l = 4; l <= 7; l++) Assert.AreEqual(1, PetDefinition.StageFor(l));
            for (int l = 8; l <= 10; l++) Assert.AreEqual(2, PetDefinition.StageFor(l));

            var def = PetCatalog.CreateDefault().Find("pet_dog");
            Assert.Greater(def.AbilityValue(3), def.AbilityValue(2) + def.abilityPerLevel * 0.99f, "melhoria no nível 3");
            Assert.IsFalse(PetProgressionManager.HasMilestone(def, 4, PetMilestoneType.Animation));
            Assert.IsTrue(PetProgressionManager.HasMilestone(def, 5, PetMilestoneType.Animation));
            Assert.IsTrue(PetProgressionManager.HasMilestone(def, 8, PetMilestoneType.Visual));
            Assert.IsTrue(PetProgressionManager.HasMilestone(def, 10, PetMilestoneType.Cosmetic));
            Assert.LessOrEqual(def.AbilityValue(10), def.abilityMax);
        }

        [Test]
        public void PetGainsXpInValidMatchesAndEvolves()
        {
            var ctx = NewCtx();
            var dog = ctx.PetCatalog.Find("pet_dog");
            var events = new List<FeedbackEvent>();
            ctx.Feedback += events.Add;
            for (int i = 0; i < 40; i++)
                ctx.Progression.ApplyMatchResult(Result(ctx.Config, "pm" + i, petHash: dog.StableHash));
            Assert.Greater(ctx.PetProgression.Level("pet_dog"), 4);
            Assert.IsTrue(events.Exists(e => e.type == FeedbackType.PetEvolved));

            // Pet bloqueado (o coelho só se compra) não ganha XP
            var rabbit = ctx.PetCatalog.Find("pet_rabbit");
            Assert.IsFalse(ctx.Inventory.IsPetUnlocked("pet_rabbit"));
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "rabbit", petHash: rabbit.StableHash));
            Assert.AreEqual(0, ctx.PetProgression.Xp("pet_rabbit"));
        }

        [Test]
        public void OnlyOnePetEquippedAndStates()
        {
            var ctx = NewCtx();
            ctx.Inventory.UnlockPet("pet_cat");
            Assert.IsTrue(ctx.Pets.Equip("pet_cat"));
            Assert.AreEqual("pet_cat", ctx.Profile.equippedPetId);
            Assert.AreEqual(PetState.Unlocked, ctx.Pets.StateOf("pet_dog"));
            Assert.AreEqual(PetState.Evolving, ctx.Pets.StateOf("pet_cat"));
            Assert.AreEqual(PetState.Locked, ctx.Pets.StateOf("pet_pig"));
            Assert.IsFalse(ctx.Pets.Equip("pet_pig"), "bloqueado");
            ctx.Pets.Unequip();
            Assert.IsNull(ctx.Pets.EquippedPet);
        }

        // ------------------------------------------------------------------ skins

        [Test]
        public void SkinsAreCosmeticOnly()
        {
            var forbidden = new[] { "damage", "health", "speed", "hp", "armor", "reload", "range" };
            foreach (var f in typeof(SkinDefinition).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                Assert.AreNotEqual(typeof(float), f.FieldType, "campo numérico de jogo: " + f.Name);
                foreach (var w in forbidden) StringAssert.DoesNotContain(w, f.Name.ToLowerInvariant(), "campo proibido: " + f.Name);
            }
        }

        [Test]
        public void SkinRaritiesAndStableIds()
        {
            var skins = SkinCatalog.CreateDefault();
            var rarities = new HashSet<Rarity>();
            var ids = new HashSet<string>();
            foreach (var s in skins.skins)
            {
                rarities.Add(s.rarity);
                Assert.IsTrue(ids.Add(s.skinId), "id repetido " + s.skinId);
                Assert.AreSame(s, skins.FindByHash(s.StableHash));
            }
            Assert.IsTrue(rarities.Contains(Rarity.Rare) && rarities.Contains(Rarity.Epic) && rarities.Contains(Rarity.Legendary));
            Assert.Greater(skins.ForTarget(SkinTarget.Pet, "pet_dog").Count, 0, "inventário separado para pets");
            Assert.Greater(skins.ForTarget(SkinTarget.Brawler, "gunslinger").Count, 0);
        }

        [Test]
        public void SkinPurchaseEquipAndNetworkResolve()
        {
            var ctx = NewCtx();
            Assert.IsFalse(ctx.Skins.Equip(SkinTarget.Brawler, "gunslinger", "skin_ekko_neon"), "bloqueada");
            Assert.IsFalse(ctx.Skins.TryPurchase("skin_ekko_neon"), "sem moedas");
            ctx.Inventory.AddCoins(300);
            Assert.IsTrue(ctx.Skins.TryPurchase("skin_ekko_neon"));
            Assert.AreEqual(0, ctx.Inventory.Coins);
            Assert.IsTrue(ctx.Skins.Equip(SkinTarget.Brawler, "gunslinger", "skin_ekko_neon"));
            var skin = ctx.Skins.EquippedSkinFor(SkinTarget.Brawler, "gunslinger");
            Assert.AreEqual(SkinState.Equipped, ctx.Skins.StateOf(skin));
            Assert.IsFalse(ctx.Skins.Equip(SkinTarget.Brawler, "sniper", "skin_ekko_neon"), "skin de outra personagem");

            // O que viaja pela rede: o hash no loadout resolve para a mesma skin noutra máquina
            var loadout = GameExperience.BuildLoadoutFor(ctx, "gunslinger");
            Assert.AreSame(skin, SkinManager.Resolve(ctx.SkinCatalog, loadout.skinHash, SkinTarget.Brawler, "gunslinger"));
            Assert.IsNull(SkinManager.Resolve(ctx.SkinCatalog, loadout.skinHash, SkinTarget.Brawler, "sniper"), "não se aplica a outra personagem");
            Assert.IsNull(SkinManager.Resolve(ctx.SkinCatalog, 12345, SkinTarget.Brawler, "gunslinger"), "hash desconhecido = aspeto original");
        }

        // ------------------------------------------------------------------ missões e recompensas

        [Test]
        public void DailyQuestCompletesAndClaimsOnce()
        {
            var ctx = NewCtx();
            var q = ctx.QuestCatalog.FindQuest("daily_play3");
            for (int i = 0; i < 3; i++) ctx.Progression.ApplyMatchResult(Result(ctx.Config, "d" + i));
            Assert.IsTrue(ctx.Quests.Progress(q).completed);
            int coins = ctx.Inventory.Coins;
            Assert.IsTrue(ctx.Quests.Claim(q));
            Assert.AreEqual(coins + q.reward.coins, ctx.Inventory.Coins);
            Assert.IsFalse(ctx.Quests.Claim(q), "não reclama duas vezes");
            Assert.AreEqual(coins + q.reward.coins, ctx.Inventory.Coins);
        }

        [Test]
        public void DuplicateMatchDoesNotAdvanceQuests()
        {
            var ctx = NewCtx();
            var q = ctx.QuestCatalog.FindQuest("daily_play3");
            var r = Result(ctx.Config, "same");
            for (int i = 0; i < 5; i++) ctx.Progression.ApplyMatchResult(r);
            Assert.AreEqual(1, ctx.Quests.Progress(q).progress);
        }

        [Test]
        public void QuestsResetEachPeriodAndUnclaimedAreDelivered()
        {
            var ctx = NewCtx();
            var q = ctx.QuestCatalog.FindQuest("daily_play3");
            for (int i = 0; i < 3; i++) ctx.Progression.ApplyMatchResult(Result(ctx.Config, "x" + i));
            int coins = ctx.Inventory.Coins;
            m_Clock.Now = Day0.AddDays(1);
            ctx.Quests.RefreshPeriods();
            Assert.AreEqual(0, ctx.Quests.Progress(q).progress, "novo dia");
            Assert.AreEqual(coins + q.reward.coins, ctx.Inventory.Coins, "recompensa não reclamada foi entregue");
            ctx.Quests.RefreshPeriods();
            Assert.AreEqual(coins + q.reward.coins, ctx.Inventory.Coins, "só uma vez");
            Assert.AreNotEqual(QuestManager.PeriodKey(QuestPeriod.Daily, Day0), QuestManager.PeriodKey(QuestPeriod.Daily, m_Clock.Now));
            Assert.AreEqual(QuestManager.PeriodKey(QuestPeriod.Weekly, Day0), QuestManager.PeriodKey(QuestPeriod.Weekly, m_Clock.Now), "mesma semana");
        }

        [Test]
        public void WeeklyDistinctCharactersAndPetMatches()
        {
            var ctx = NewCtx();
            var brawlers = ctx.QuestCatalog.FindQuest("weekly_brawlers3");
            var pets = ctx.QuestCatalog.FindQuest("daily_pet2");
            int dog = ctx.PetCatalog.Find("pet_dog").StableHash;
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "a", brawler: "sniper", petHash: dog));
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "b", brawler: "sniper"));
            Assert.AreEqual(1, ctx.Quests.Progress(brawlers).progress, "a mesma personagem conta uma vez");
            Assert.AreEqual(1, ctx.Quests.Progress(pets).progress);
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "c", brawler: "gunslinger", petHash: dog));
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "d", brawler: "trapper"));
            Assert.IsTrue(ctx.Quests.Progress(brawlers).completed);
            Assert.IsTrue(ctx.Quests.Progress(pets).completed);
            Assert.IsTrue(ctx.Quests.Claim(brawlers));
            Assert.IsTrue(ctx.Inventory.IsSkinUnlocked("skin_mrs_graffiti"), "skin como recompensa");
        }

        [Test]
        public void RewardsAreIdempotentByKey()
        {
            var ctx = NewCtx();
            var bundle = new RewardBundle { coins = 10, petIds = new[] { "pet_pig" } };
            Assert.IsTrue(ctx.Rewards.Grant("evento:1", bundle, "teste"));
            Assert.IsFalse(ctx.Rewards.Grant("evento:1", bundle, "teste"));
            Assert.AreEqual(10, ctx.Inventory.Coins);
            Assert.IsTrue(ctx.Inventory.IsPetUnlocked("pet_pig"));
        }

        [Test]
        public void LevelRewardsArePendingUntilClaimed()
        {
            var ctx = NewCtx();
            ctx.Progression.AddPlayerXp((int)ctx.Config.playerLevels.TotalXpForLevel(5), "teste");
            CollectionAssert.IsSupersetOf(ctx.Profile.pendingLevelRewards, new[] { 2, 3, 5 });
            int coins = ctx.Inventory.Coins;
            int claimed = ctx.Rewards.ClaimAllLevelRewards();
            Assert.AreEqual(3, claimed);
            Assert.AreEqual(coins + 50 + 75 + 150, ctx.Inventory.Coins);
            Assert.AreEqual(0, ctx.Profile.pendingLevelRewards.Count);
            Assert.IsFalse(ctx.Rewards.ClaimLevelReward(2));
        }

        [Test]
        public void FeedbackEventsForProgression()
        {
            var ctx = NewCtx();
            var events = new List<FeedbackType>();
            ctx.Feedback += e => events.Add(e.type);
            ctx.Progression.ApplyMatchResult(Result(ctx.Config, "fb", kills: 5));
            CollectionAssert.Contains(events, FeedbackType.LevelUp);
            CollectionAssert.Contains(events, FeedbackType.Victory);
            CollectionAssert.Contains(events, FeedbackType.AchievementUnlocked, "primeira eliminação");
        }
    }
}
