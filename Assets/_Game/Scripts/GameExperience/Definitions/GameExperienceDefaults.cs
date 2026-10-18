using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Conteúdo inicial da Game Experience (pets, skins, missões, conquistas, recompensas por nível).
    /// Usado pelo gerador de assets do Editor (FPS-LAN → Gerar dados de Game Experience) e como reserva em runtime
    /// se os assets não existirem. Todos os valores são ajustáveis depois nos assets gerados.
    /// </summary>
    public static class GameExperienceDefaults
    {
        // ------------------------------------------------------------------ pets

        public static List<PetDefinition> CreatePets()
        {
            var list = new List<PetDefinition>
            {
                Pet("pet_dog", "Faro", PetSpecies.Dog, PetCategory.Support, PetAbilityType.Detection, 6f, 0.3f, 0.5f, 9.5f,
                    "A keen-nosed dog. Detects nearby enemies and shows their direction.",
                    new Color(0.72f, 0.5f, 0.3f), new Color(0.98f, 0.9f, 0.75f), new Color(0.9f, 0.2f, 0.2f),
                    new UnlockRule { method = UnlockMethod.Default }),
                Pet("pet_cat", "Bigodes", PetSpecies.Cat, PetCategory.Agility, PetAbilityType.Agility, 0.02f, 0.002f, 0.005f, 0.045f,
                    "An agile cat. Gives its owner a small speed boost.",
                    new Color(1f, 0.6f, 0.25f), new Color(1f, 0.95f, 0.9f), new Color(0.3f, 0.8f, 0.4f),
                    new UnlockRule { method = UnlockMethod.PlayerLevel, value = 3 }),
                Pet("pet_pig", "Toucinho", PetSpecies.Pig, PetCategory.Defense, PetAbilityType.Protection, 10f, 1f, 2f, 21f,
                    "A fearless pig. Creates a temporary mud shield when its owner's health is low.",
                    new Color(1f, 0.68f, 0.75f), new Color(0.95f, 0.5f, 0.6f), new Color(0.55f, 0.35f, 0.2f),
                    new UnlockRule { method = UnlockMethod.Quest, value = 3 }),
                Pet("pet_rooster", "Alvorada", PetSpecies.Rooster, PetCategory.Alert, PetAbilityType.Alert, 12f, 0.5f, 1f, 18f,
                    "A watchful rooster. Warns its owner about threats from behind.",
                    new Color(0.98f, 0.97f, 0.92f), new Color(0.95f, 0.25f, 0.2f), new Color(1f, 0.75f, 0.2f),
                    new UnlockRule { method = UnlockMethod.Achievement, refId = "ach_assists25" }),
                Pet("pet_rabbit", "Pipoca", PetSpecies.Rabbit, PetCategory.Mobility, PetAbilityType.Mobility, 0.03f, 0.003f, 0.005f, 0.065f,
                    "A lively rabbit. Helps its owner sprint faster.",
                    new Color(0.88f, 0.88f, 0.9f), new Color(1f, 0.75f, 0.8f), new Color(0.35f, 0.55f, 1f),
                    new UnlockRule { method = UnlockMethod.Coins, value = 600 }),
                Pet("pet_turtle", "Carapaça", PetSpecies.Turtle, PetCategory.Endurance, PetAbilityType.Recovery, 0.10f, 0.01f, 0.02f, 0.21f,
                    "A patient turtle. Helps its owner regenerate health faster.",
                    new Color(0.45f, 0.75f, 0.35f), new Color(0.55f, 0.38f, 0.2f), new Color(0.95f, 0.85f, 0.35f),
                    new UnlockRule { method = UnlockMethod.Challenge, refId = "chal_flawless" }),
            };
            return list;
        }

        static PetDefinition Pet(string id, string name, PetSpecies species, PetCategory category, PetAbilityType ability,
            float baseValue, float perLevel, float milestoneBonus, float max, string description,
            Color primary, Color secondary, Color accent, UnlockRule unlock)
        {
            var p = ScriptableObject.CreateInstance<PetDefinition>();
            p.name = id;
            p.petId = id;
            p.displayName = name;
            p.species = species;
            p.category = category;
            p.ability = ability;
            p.abilityBase = baseValue;
            p.abilityPerLevel = perLevel;
            p.abilityMilestoneBonus = milestoneBonus;
            p.abilityMax = max;
            p.description = description;
            p.primaryColor = primary;
            p.secondaryColor = secondary;
            p.accentColor = accent;
            p.scale = 1f;
            p.unlock = unlock;
            p.milestones = new List<PetMilestone>
            {
                new PetMilestone { level = 3, type = PetMilestoneType.AbilityBoost, description = "Small ability upgrade" },
                new PetMilestone { level = 4, type = PetMilestoneType.Visual, description = "Developed stage (larger, collar)" },
                new PetMilestone { level = 5, type = PetMilestoneType.Animation, description = "New celebration animation (spin)" },
                new PetMilestone { level = 8, type = PetMilestoneType.Visual, description = "Evolved stage + extra accessory" },
                new PetMilestone { level = 10, type = PetMilestoneType.Cosmetic, description = "Special max-level aura" },
            };
            return p;
        }

        // ------------------------------------------------------------------ skins

        public static List<SkinDefinition> CreateSkins()
        {
            return new List<SkinDefinition>
            {
                // Ekko (gunslinger)
                BrawlerSkin("skin_ekko_neon", "Neon Ekko", "gunslinger", Rarity.Rare, Coins(300),
                    new Color(0.15f, 0.85f, 0.95f), new Color(0.15f, 0.12f, 0.3f), new Color(1f, 0.25f, 0.8f), new Color(0.1f, 0.1f, 0.15f), new Color(0.2f, 0.2f, 0.3f),
                    new Color(1f, 0.3f, 0.85f), new Color(0.3f, 1f, 1f), new Color(1f, 0.35f, 0.9f), SkinAccessory.None, SkinEffect.None, SkinAnimation.Swagger),
                BrawlerSkin("skin_ekko_gold", "Golden Ekko", "gunslinger", Rarity.Legendary, new UnlockRule { method = UnlockMethod.PlayerLevel, value = 20 },
                    new Color(1f, 0.8f, 0.25f), new Color(0.35f, 0.25f, 0.1f), new Color(1f, 1f, 0.85f), new Color(0.3f, 0.2f, 0.05f), new Color(0.75f, 0.55f, 0.15f),
                    new Color(1f, 0.85f, 0.3f), new Color(1f, 0.9f, 0.4f), new Color(1f, 0.7f, 0.2f), SkinAccessory.Crown, SkinEffect.Sparkles, SkinAnimation.Swagger),

                // Mr.S (sprayer)
                BrawlerSkin("skin_mrs_toxic", "Toxic Mr.S", "sprayer", Rarity.Epic, Coins(500),
                    new Color(0.35f, 0.9f, 0.3f), new Color(0.25f, 0.12f, 0.35f), new Color(0.7f, 0.3f, 1f), new Color(0.2f, 0.8f, 0.3f), new Color(0.2f, 0.15f, 0.3f),
                    new Color(0.5f, 1f, 0.35f), new Color(0.45f, 1f, 0.4f), new Color(0.75f, 0.35f, 1f), SkinAccessory.Antenna, SkinEffect.Bubbles, SkinAnimation.Bouncy),
                BrawlerSkin("skin_mrs_graffiti", "Graffiti Mr.S", "sprayer", Rarity.Rare, new UnlockRule { method = UnlockMethod.RewardOnly },
                    new Color(1f, 0.85f, 0.2f), new Color(0.2f, 0.3f, 0.75f), new Color(1f, 0.3f, 0.4f), new Color(0.15f, 0.15f, 0.2f), new Color(0.9f, 0.4f, 0.2f),
                    new Color(0.3f, 0.6f, 1f), new Color(1f, 0.45f, 0.75f), new Color(1f, 0.85f, 0.25f), SkinAccessory.Scarf, SkinEffect.None, SkinAnimation.Bouncy),

                // Riot (shotgunner)
                BrawlerSkin("skin_riot_swat", "Riot Squad", "shotgunner", Rarity.Rare, Coins(300),
                    new Color(0.18f, 0.22f, 0.35f), new Color(0.1f, 0.1f, 0.14f), new Color(0.3f, 0.65f, 1f), new Color(0.1f, 0.1f, 0.12f), new Color(0.2f, 0.22f, 0.28f),
                    new Color(0.35f, 0.65f, 1f), new Color(0.55f, 0.8f, 1f), new Color(0.3f, 0.5f, 1f), SkinAccessory.None, SkinEffect.None, SkinAnimation.Swagger),
                BrawlerSkin("skin_riot_lava", "Riot Lava", "shotgunner", Rarity.Epic, Coins(500),
                    new Color(0.25f, 0.12f, 0.1f), new Color(0.15f, 0.08f, 0.06f), new Color(1f, 0.45f, 0.1f), new Color(1f, 0.35f, 0.1f), new Color(0.35f, 0.15f, 0.1f),
                    new Color(1f, 0.5f, 0.1f), new Color(1f, 0.6f, 0.15f), new Color(1f, 0.3f, 0.05f), SkinAccessory.Horns, SkinEffect.Embers, SkinAnimation.Default),

                // Lupus Marinus (artillery)
                BrawlerSkin("skin_lm_sailor", "Sea Wolf", "artillery", Rarity.Rare, Coins(300),
                    new Color(0.95f, 0.95f, 0.98f), new Color(0.15f, 0.22f, 0.45f), new Color(0.2f, 0.4f, 0.85f), new Color(0.3f, 0.25f, 0.2f), new Color(0.2f, 0.25f, 0.4f),
                    new Color(0.95f, 0.3f, 0.25f), new Color(0.4f, 0.7f, 1f), new Color(0.3f, 0.55f, 1f), SkinAccessory.Scarf, SkinEffect.None, SkinAnimation.Default),
                BrawlerSkin("skin_lm_storm", "Captain Storm", "artillery", Rarity.Epic, new UnlockRule { method = UnlockMethod.RewardOnly },
                    new Color(0.3f, 0.32f, 0.4f), new Color(0.12f, 0.12f, 0.18f), new Color(0.55f, 0.85f, 1f), new Color(0.85f, 0.85f, 0.9f), new Color(0.25f, 0.25f, 0.32f),
                    new Color(0.55f, 0.85f, 1f), new Color(0.6f, 0.9f, 1f), new Color(0.5f, 0.75f, 1f), SkinAccessory.Cape, SkinEffect.Bubbles, SkinAnimation.Swagger),

                // N.Cage (trapper)
                BrawlerSkin("skin_ncage_forest", "Forest Hunter", "trapper", Rarity.Rare, Coins(300),
                    new Color(0.3f, 0.55f, 0.25f), new Color(0.3f, 0.25f, 0.15f), new Color(0.85f, 0.75f, 0.35f), new Color(0.35f, 0.22f, 0.12f), new Color(0.45f, 0.32f, 0.18f),
                    new Color(0.6f, 0.9f, 0.3f), new Color(0.55f, 1f, 0.35f), new Color(0.4f, 0.85f, 0.3f), SkinAccessory.None, SkinEffect.Leaves, SkinAnimation.Default),
                BrawlerSkin("skin_ncage_lunar", "Lunar Archer", "trapper", Rarity.Epic, Coins(500),
                    new Color(0.75f, 0.78f, 0.9f), new Color(0.2f, 0.22f, 0.4f), new Color(0.6f, 0.75f, 1f), new Color(0.9f, 0.92f, 1f), new Color(0.35f, 0.38f, 0.55f),
                    new Color(0.75f, 0.85f, 1f), new Color(0.75f, 0.85f, 1f), new Color(0.55f, 0.7f, 1f), SkinAccessory.Halo, SkinEffect.Sparkles, SkinAnimation.Floaty),

                // Lince (sniper)
                BrawlerSkin("skin_lince_snow", "Snow Lince", "sniper", Rarity.Rare, Coins(300),
                    new Color(0.92f, 0.95f, 1f), new Color(0.55f, 0.65f, 0.8f), new Color(0.4f, 0.7f, 1f), new Color(0.85f, 0.88f, 0.95f), new Color(0.65f, 0.72f, 0.85f),
                    new Color(0.6f, 0.85f, 1f), new Color(0.75f, 0.95f, 1f), new Color(0.55f, 0.8f, 1f), SkinAccessory.Scarf, SkinEffect.Snowflakes, SkinAnimation.Default),
                BrawlerSkin("skin_lince_shadow", "Shadow Lince", "sniper", Rarity.Epic, new UnlockRule { method = UnlockMethod.Achievement, refId = "ach_headshots25" },
                    new Color(0.25f, 0.18f, 0.35f), new Color(0.1f, 0.08f, 0.14f), new Color(0.8f, 0.3f, 1f), new Color(0.12f, 0.1f, 0.15f), new Color(0.2f, 0.15f, 0.28f),
                    new Color(0.85f, 0.35f, 1f), new Color(0.8f, 0.4f, 1f), new Color(0.6f, 0.2f, 1f), SkinAccessory.Horns, SkinEffect.Sparkles, SkinAnimation.Floaty),

                // Pets
                PetSkin("skin_dog_firefighter", "Firefighter Faro", "pet_dog", Rarity.Rare, Coins(250), new Color(0.9f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.2f), new Color(0.2f, 0.2f, 0.25f)),
                PetSkin("skin_cat_ninja", "Ninja Bigodes", "pet_cat", Rarity.Epic, Coins(400), new Color(0.15f, 0.15f, 0.2f), new Color(0.35f, 0.35f, 0.4f), new Color(0.95f, 0.2f, 0.25f)),
                PetSkin("skin_pig_golden", "Golden Toucinho", "pet_pig", Rarity.Legendary, new UnlockRule { method = UnlockMethod.PlayerLevel, value = 15 },
                    new Color(1f, 0.82f, 0.3f), new Color(1f, 0.7f, 0.2f), new Color(1f, 1f, 0.8f), SkinEffect.Sparkles),
                PetSkin("skin_rooster_disco", "Disco Alvorada", "pet_rooster", Rarity.Rare, Coins(250), new Color(0.65f, 0.35f, 1f), new Color(0.2f, 0.9f, 1f), new Color(1f, 0.4f, 0.8f)),
                PetSkin("skin_rabbit_lunar", "Lunar Pipoca", "pet_rabbit", Rarity.Epic, Coins(400), new Color(0.8f, 0.85f, 1f), new Color(0.4f, 0.5f, 0.95f), new Color(1f, 0.95f, 0.6f), SkinEffect.Sparkles),
                PetSkin("skin_turtle_coral", "Coral Carapaça", "pet_turtle", Rarity.Rare, Coins(250), new Color(1f, 0.5f, 0.45f), new Color(0.2f, 0.75f, 0.75f), new Color(1f, 0.9f, 0.7f), SkinEffect.Bubbles),
            };
        }

        static UnlockRule Coins(int price) => new UnlockRule { method = UnlockMethod.Coins, value = price };

        static SkinDefinition BrawlerSkin(string id, string name, string brawlerId, Rarity rarity, UnlockRule unlock,
            Color outfit, Color pants, Color accent, Color hair, Color gear, Color weapon, Color vfx, Color superVfx,
            SkinAccessory accessory, SkinEffect effect, SkinAnimation anim)
        {
            var s = ScriptableObject.CreateInstance<SkinDefinition>();
            s.name = id;
            s.skinId = id;
            s.displayName = name;
            s.target = SkinTarget.Brawler;
            s.targetId = brawlerId;
            s.rarity = rarity;
            s.unlock = unlock;
            s.description = RarityStyle.LabelOf(rarity) + " · cosmetic only";
            s.overrideColors = true;
            s.skin = DefaultSkinTone(brawlerId);
            s.outfit = outfit;
            s.pants = pants;
            s.accent = accent;
            s.hair = hair;
            s.gear = gear;
            s.overrideWeaponColor = true;
            s.weaponAccent = weapon;
            s.overrideVfx = true;
            s.primaryVfx = vfx;
            s.superVfx = superVfx;
            s.accessory = accessory;
            s.effect = effect;
            s.animation = anim;
            return s;
        }

        static SkinDefinition PetSkin(string id, string name, string petId, Rarity rarity, UnlockRule unlock, Color primary, Color secondary, Color accent,
            SkinEffect effect = SkinEffect.None)
        {
            var s = ScriptableObject.CreateInstance<SkinDefinition>();
            s.name = id;
            s.skinId = id;
            s.displayName = name;
            s.target = SkinTarget.Pet;
            s.targetId = petId;
            s.rarity = rarity;
            s.unlock = unlock;
            s.description = RarityStyle.LabelOf(rarity) + " · cosmetic only";
            s.overrideColors = false;
            s.overrideWeaponColor = false;
            s.overrideVfx = false;
            s.petPrimary = primary;
            s.petSecondary = secondary;
            s.petAccent = accent;
            s.effect = effect;
            return s;
        }

        static Color DefaultSkinTone(string brawlerId)
        {
            switch (brawlerId)
            {
                case "sprayer": return new Color(0.85f, 0.95f, 0.8f);
                case "trapper": return new Color(0.86f, 0.62f, 0.45f);
                case "sniper": return new Color(0.72f, 0.5f, 0.36f);
                case "artillery": return new Color(0.98f, 0.76f, 0.6f);
                default: return new Color(1f, 0.8f, 0.62f);
            }
        }

        // ------------------------------------------------------------------ missões e conquistas

        public static void FillQuests(QuestCatalog c)
        {
            c.quests = new List<QuestDefinition>
            {
                Quest("daily_play3", "Play three matches", QuestPeriod.Daily, QuestMetric.MatchesPlayed, 3, new RewardBundle { xp = 40, coins = 30 }),
                Quest("daily_elim10", "Eliminate ten enemies", QuestPeriod.Daily, QuestMetric.Eliminations, 10, new RewardBundle { xp = 50, coins = 40 }),
                Quest("daily_pet2", "Play two matches with a pet equipped", QuestPeriod.Daily, QuestMetric.MatchesWithPet, 2, new RewardBundle { xp = 30, coins = 30 }),
                Quest("weekly_elim50", "Get 50 eliminations", QuestPeriod.Weekly, QuestMetric.Eliminations, 50, new RewardBundle { xp = 150, coins = 150 }),
                Quest("weekly_win10", "Win ten matches", QuestPeriod.Weekly, QuestMetric.Wins, 10,
                    new RewardBundle { xp = 200, coins = 150, cosmeticIds = new[] { "emote_vitoria" } }),
                Quest("weekly_brawlers3", "Use three different characters", QuestPeriod.Weekly, QuestMetric.DistinctBrawlers, 3,
                    new RewardBundle { xp = 120, coins = 100, skinIds = new[] { "skin_mrs_graffiti" } }),
            };

            c.achievements = new List<AchievementDefinition>
            {
                Achievement("ach_first_blood", "First elimination", "Eliminate your first enemy.", QuestMetric.Eliminations, 1, false, new RewardBundle { coins = 25 }),
                Achievement("ach_hunter100", "Hunter", "Get 100 eliminations.", QuestMetric.Eliminations, 100, false,
                    new RewardBundle { coins = 300, cosmeticIds = new[] { "titulo_cacador" } }),
                Achievement("ach_assists25", "Team spirit", "Get 25 assists (unlocks Alvorada the rooster).", QuestMetric.Assists, 25, false,
                    new RewardBundle { coins = 100, petIds = new[] { "pet_rooster" } }),
                Achievement("ach_headshots25", "Eagle eye", "Get 25 headshots (unlocks Shadow Lince).", QuestMetric.Headshots, 25, false,
                    new RewardBundle { coins = 100, skinIds = new[] { "skin_lince_shadow" } }),
                Achievement("ach_winner10", "Winner", "Win 10 matches.", QuestMetric.Wins, 10, false, new RewardBundle { coins = 200 }),
                Achievement("ach_level10", "Veteran", "Reach player level 10.", QuestMetric.PlayerLevel, 10, false, new RewardBundle { coins = 150 }),
                Achievement("chal_flawless", "Without a scratch", "Finish a valid match without being eliminated (unlocks Carapaça the turtle).",
                    QuestMetric.FlawlessMatches, 1, true, new RewardBundle { coins = 50, petIds = new[] { "pet_turtle" } }),
                Achievement("chal_objectives10", "Strategist", "Complete 10 match objectives.", QuestMetric.Objectives, 10, true,
                    new RewardBundle { coins = 150, cosmeticIds = new[] { "titulo_estratega" } }),
            };

            c.characterUnlocks = new List<CharacterUnlockDefinition>();
            foreach (var id in new[] { "gunslinger", "artillery", "trapper", "sprayer", "shotgunner", "sniper" })
                c.characterUnlocks.Add(new CharacterUnlockDefinition { brawlerId = id, unlock = new UnlockRule { method = UnlockMethod.Default } });
        }

        static QuestDefinition Quest(string id, string title, QuestPeriod period, QuestMetric metric, int target, RewardBundle reward) =>
            new QuestDefinition { questId = id, title = title, period = period, metric = metric, target = target, reward = reward };

        static AchievementDefinition Achievement(string id, string title, string description, QuestMetric metric, int target, bool challenge, RewardBundle reward) =>
            new AchievementDefinition { achievementId = id, title = title, description = description, metric = metric, target = target, isChallenge = challenge, reward = reward };

        // ------------------------------------------------------------------ recompensas por nível

        public static void FillLevelRewards(ProgressionConfig c)
        {
            c.levelRewards = new List<LevelReward>
            {
                Level(2, new RewardBundle { coins = 50 }),
                Level(3, new RewardBundle { coins = 75 }),
                Level(5, new RewardBundle { coins = 150 }),
                Level(8, new RewardBundle { coins = 200 }),
                Level(10, new RewardBundle { coins = 200, skinIds = new[] { "skin_lm_storm" } }),
                Level(12, new RewardBundle { coins = 250 }),
                Level(15, new RewardBundle { coins = 300 }),
                Level(20, new RewardBundle { coins = 400 }),
                Level(25, new RewardBundle { coins = 500 }),
                Level(30, new RewardBundle { coins = 600 }),
                Level(40, new RewardBundle { coins = 800 }),
                Level(50, new RewardBundle { coins = 1500, cosmeticIds = new[] { "titulo_lenda" } }),
            };
        }

        static LevelReward Level(int level, RewardBundle reward) => new LevelReward { level = level, reward = reward };
    }
}
