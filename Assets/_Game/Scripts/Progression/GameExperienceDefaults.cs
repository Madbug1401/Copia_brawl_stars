using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Conteúdo inicial do "Game Experience" (6 pets, skins, missões, cosméticos). O gerador do editor grava isto
    /// como assets; em runtime serve de reserva se os assets não existirem. Depois de gerados, ajustar no Inspector.
    /// </summary>
    public static class GameExperienceDefaults
    {
        // ------------------------------------------------------------------ catálogo puro (testável)

        public static ExperienceCatalog CreateCatalog(IEnumerable<string> brawlerIds = null)
        {
            var c = new ExperienceCatalog
            {
                progression = ProgressionSettings.CreateDefault(),
                pets = PetRulesList(),
                skins = Skins(),
                quests = Quests(),
                cosmetics = Cosmetics(),
            };
            if (brawlerIds != null) c.brawlerIds.AddRange(brawlerIds);
            else c.brawlerIds.AddRange(new[] { "gunslinger", "artillery", "trapper", "sprayer", "shotgunner", "sniper" });
            return c;
        }

        public static readonly string[] PetIds = { "pet_dog", "pet_cat", "pet_pig", "pet_rooster", "pet_rabbit", "pet_turtle" };

        public static List<PetRules> PetRulesList()
        {
            return new List<PetRules>
            {
                Pet("pet_dog", "Cão", "Fareja inimigos: mostra a direção de inimigos visíveis por perto.",
                    new PetPassive { type = PetPassiveType.EnemyRadar, description = "Radar de inimigos", baseValue = 8f, perLevel = 0.7f, maxValue = 14f },
                    new UnlockRule(UnlockMethod.Default)),
                Pet("pet_cat", "Gato", "Ágil: um pouco mais de velocidade a andar.",
                    new PetPassive { type = PetPassiveType.Mobility, description = "+velocidade a andar", baseValue = 0.02f, perLevel = 0.0025f, maxValue = 0.04f },
                    new UnlockRule(UnlockMethod.PlayerLevel, 2)),
                Pet("pet_pig", "Porco", "Protetor: escudo pequeno que absorve dano e recarrega.",
                    new PetPassive { type = PetPassiveType.Shield, description = "Escudo temporário", baseValue = 8f, perLevel = 0.9f, maxValue = 16f, cooldown = 20f },
                    new UnlockRule(UnlockMethod.Coins, 600)),
                Pet("pet_rooster", "Galo", "Sentinela: canta quando um inimigo se aproxima pelas costas.",
                    new PetPassive { type = PetPassiveType.Alert, description = "Alerta de aproximação", baseValue = 10f, perLevel = 0.7f, maxValue = 16f },
                    new UnlockRule(UnlockMethod.PlayerLevel, 5)),
                Pet("pet_rabbit", "Coelho", "Veloz: um pouco mais de velocidade a correr.",
                    new PetPassive { type = PetPassiveType.Sprint, description = "+velocidade a correr", baseValue = 0.03f, perLevel = 0.0035f, maxValue = 0.06f },
                    new UnlockRule(UnlockMethod.Mission, 0, "w_brawlers3")),
                Pet("pet_turtle", "Tartaruga", "Resistente: regeneração um pouco mais forte e mais cedo.",
                    new PetPassive { type = PetPassiveType.Recovery, description = "+regeneração", baseValue = 0.1f, perLevel = 0.011f, maxValue = 0.2f },
                    new UnlockRule(UnlockMethod.PlayerLevel, 10)),
            };
        }

        static PetRules Pet(string id, string name, string description, PetPassive passive, UnlockRule unlock)
        {
            string shortId = id.Replace("pet_", "");
            return new PetRules
            {
                petId = id,
                displayName = name,
                description = description,
                passive = passive,
                unlock = unlock,
                levelRewards = new List<PetLevelReward>
                {
                    new PetLevelReward { level = 3, reward = new RewardEntry(RewardType.Coins, 60), label = "60 moedas" },
                    new PetLevelReward { level = 5, reward = new RewardEntry(RewardType.PetSkin, 1, "skin_pet_" + shortId + "_alt"), label = "Skin de pet" },
                    new PetLevelReward { level = 8, reward = new RewardEntry(RewardType.Coins, 150), label = "150 moedas" },
                    new PetLevelReward { level = 10, reward = new RewardEntry(RewardType.Cosmetic, 1, "badge_" + shortId + "_master"), label = "Emblema de mestre" },
                },
            };
        }

        public static List<SkinDefinition> Skins()
        {
            var list = new List<SkinDefinition>
            {
                // Ekko
                BrawlerSkin("skin_gunslinger_desert", "Ekko Deserto", SkinRarity.Rara, "gunslinger", new UnlockRule(UnlockMethod.Coins, 300),
                    Look(new Color(0.95f, 0.78f, 0.6f), new Color(0.85f, 0.7f, 0.45f), new Color(0.55f, 0.4f, 0.28f), new Color(0.95f, 0.55f, 0.2f), new Color(0.3f, 0.2f, 0.12f), new Color(0.5f, 0.35f, 0.2f), HeadGear.WideHat), new Color(0.95f, 0.6f, 0.25f)),
                BrawlerSkin("skin_gunslinger_neon", "Ekko Néon", SkinRarity.Epica, "gunslinger", new UnlockRule(UnlockMethod.PlayerLevel, 8),
                    Look(new Color(0.9f, 0.75f, 0.65f), new Color(0.15f, 0.12f, 0.25f), new Color(0.1f, 0.1f, 0.15f), new Color(0.2f, 1f, 0.9f), new Color(0.95f, 0.2f, 0.75f), new Color(0.25f, 0.25f, 0.35f), HeadGear.Goggles), new Color(0.2f, 1f, 0.9f)),
                // Mr.S
                BrawlerSkin("skin_sprayer_street", "Mr.S Grafiter", SkinRarity.Comum, "sprayer", new UnlockRule(UnlockMethod.Coins, 150),
                    Look(new Color(0.8f, 0.62f, 0.5f), new Color(0.95f, 0.85f, 0.2f), new Color(0.2f, 0.25f, 0.4f), new Color(0.2f, 0.6f, 1f), new Color(0.15f, 0.1f, 0.08f), new Color(0.3f, 0.3f, 0.3f), HeadGear.Bandana), new Color(1f, 0.85f, 0.2f)),
                BrawlerSkin("skin_sprayer_toxic", "Mr.S Tóxico", SkinRarity.Epica, "sprayer", new UnlockRule(UnlockMethod.PlayerLevel, 15),
                    Look(new Color(0.7f, 0.9f, 0.6f), new Color(0.25f, 0.45f, 0.2f), new Color(0.12f, 0.15f, 0.1f), new Color(0.6f, 1f, 0.2f), new Color(0.5f, 0.2f, 0.7f), new Color(0.2f, 0.25f, 0.15f), HeadGear.GasMask), new Color(0.55f, 1f, 0.25f)),
                // Riot
                BrawlerSkin("skin_shotgunner_riotblue", "Riot Antimotim", SkinRarity.Rara, "shotgunner", new UnlockRule(UnlockMethod.Coins, 300),
                    Look(new Color(0.95f, 0.78f, 0.65f), new Color(0.2f, 0.3f, 0.55f), new Color(0.12f, 0.14f, 0.2f), new Color(0.95f, 0.95f, 1f), new Color(0.15f, 0.12f, 0.1f), new Color(0.2f, 0.22f, 0.28f), HeadGear.Goggles), new Color(0.5f, 0.75f, 1f)),
                BrawlerSkin("skin_shotgunner_gold", "Riot Dourado", SkinRarity.Lendaria, "shotgunner", new UnlockRule(UnlockMethod.PlayerLevel, 40),
                    Look(new Color(1f, 0.82f, 0.68f), new Color(1f, 0.8f, 0.25f), new Color(0.35f, 0.25f, 0.1f), new Color(1f, 1f, 0.85f), new Color(0.95f, 0.75f, 0.2f), new Color(0.6f, 0.45f, 0.15f), HeadGear.Bandana), new Color(1f, 0.85f, 0.3f), true),
                // L.M
                BrawlerSkin("skin_artillery_sailor", "L.M Marinheiro", SkinRarity.Rara, "artillery", new UnlockRule(UnlockMethod.Coins, 300),
                    Look(new Color(0.98f, 0.8f, 0.66f), new Color(0.95f, 0.95f, 1f), new Color(0.15f, 0.22f, 0.45f), new Color(0.2f, 0.35f, 0.85f), new Color(0.3f, 0.2f, 0.12f), new Color(0.15f, 0.22f, 0.45f), HeadGear.WideHat), new Color(0.3f, 0.5f, 1f)),
                BrawlerSkin("skin_artillery_pirate", "L.M Pirata", SkinRarity.Epica, "artillery", new UnlockRule(UnlockMethod.Coins, 700),
                    Look(new Color(0.9f, 0.7f, 0.55f), new Color(0.55f, 0.12f, 0.12f), new Color(0.15f, 0.12f, 0.1f), new Color(0.95f, 0.8f, 0.3f), new Color(0.1f, 0.08f, 0.08f), new Color(0.3f, 0.2f, 0.12f), HeadGear.Bandana), new Color(0.95f, 0.75f, 0.3f)),
                // N.Cage
                BrawlerSkin("skin_trapper_jungle", "N.Cage Selva", SkinRarity.Rara, "trapper", new UnlockRule(UnlockMethod.Coins, 300),
                    Look(new Color(0.8f, 0.6f, 0.45f), new Color(0.3f, 0.5f, 0.25f), new Color(0.3f, 0.25f, 0.15f), new Color(0.95f, 0.75f, 0.2f), new Color(0.2f, 0.15f, 0.1f), new Color(0.45f, 0.3f, 0.15f), HeadGear.FoxHood), new Color(0.6f, 0.9f, 0.3f)),
                BrawlerSkin("skin_trapper_shadow", "N.Cage Sombra", SkinRarity.Epica, "trapper", new UnlockRule(UnlockMethod.Achievement, 0, "ach_kills100"),
                    Look(new Color(0.6f, 0.55f, 0.6f), new Color(0.12f, 0.1f, 0.18f), new Color(0.08f, 0.08f, 0.1f), new Color(0.75f, 0.3f, 1f), new Color(0.9f, 0.9f, 0.95f), new Color(0.2f, 0.15f, 0.25f), HeadGear.FoxHood), new Color(0.75f, 0.35f, 1f)),
                // Lince
                BrawlerSkin("skin_sniper_arctic", "Lince Ártica", SkinRarity.Rara, "sniper", new UnlockRule(UnlockMethod.Coins, 300),
                    Look(new Color(0.95f, 0.85f, 0.8f), new Color(0.9f, 0.95f, 1f), new Color(0.6f, 0.65f, 0.75f), new Color(0.3f, 0.7f, 1f), new Color(0.95f, 0.95f, 1f), new Color(0.55f, 0.6f, 0.7f), HeadGear.EarCap), new Color(0.6f, 0.85f, 1f)),
                BrawlerSkin("skin_sniper_ghost", "Lince Fantasma", SkinRarity.Lendaria, "sniper", new UnlockRule(UnlockMethod.PlayerLevel, 25),
                    Look(new Color(0.75f, 0.85f, 0.95f), new Color(0.2f, 0.25f, 0.35f), new Color(0.12f, 0.14f, 0.2f), new Color(0.5f, 1f, 1f), new Color(0.85f, 0.95f, 1f), new Color(0.3f, 0.35f, 0.45f), HeadGear.EarCap), new Color(0.5f, 1f, 1f), true),
            };

            // Skins de pet: uma por pet (recompensa do nível 5) + especiais
            list.Add(PetSkin("skin_pet_dog_alt", "Cão Malhado", SkinRarity.Rara, "pet_dog", new Color(0.95f, 0.95f, 0.92f), new Color(0.2f, 0.18f, 0.16f), new Color(0.2f, 0.5f, 1f)));
            list.Add(PetSkin("skin_pet_cat_alt", "Gato Smoking", SkinRarity.Rara, "pet_cat", new Color(0.15f, 0.15f, 0.18f), new Color(0.95f, 0.95f, 0.95f), new Color(0.9f, 0.2f, 0.25f)));
            list.Add(PetSkin("skin_pet_pig_alt", "Porco Lamacento", SkinRarity.Rara, "pet_pig", new Color(0.6f, 0.42f, 0.3f), new Color(0.95f, 0.65f, 0.7f), new Color(0.3f, 0.6f, 0.3f)));
            list.Add(PetSkin("skin_pet_rooster_alt", "Galo Real", SkinRarity.Rara, "pet_rooster", new Color(0.2f, 0.25f, 0.6f), new Color(0.95f, 0.9f, 0.8f), new Color(1f, 0.75f, 0.2f)));
            list.Add(PetSkin("skin_pet_rabbit_alt", "Coelho da Neve", SkinRarity.Rara, "pet_rabbit", new Color(0.97f, 0.97f, 1f), new Color(0.8f, 0.88f, 1f), new Color(0.4f, 0.7f, 1f)));
            list.Add(PetSkin("skin_pet_turtle_alt", "Tartaruga Coral", SkinRarity.Rara, "pet_turtle", new Color(0.5f, 0.8f, 0.6f), new Color(1f, 0.5f, 0.45f), new Color(1f, 0.85f, 0.3f)));
            var golden = PetSkin("skin_pet_dog_golden", "Cão Dourado", SkinRarity.Lendaria, "pet_dog", new Color(1f, 0.8f, 0.3f), new Color(1f, 0.95f, 0.7f), new Color(0.9f, 0.2f, 0.2f));
            golden.unlock = new UnlockRule(UnlockMethod.Achievement, 0, "ach_headshots25");
            golden.sparkle = true;
            list.Add(golden);
            var witch = PetSkin("skin_pet_cat_witch", "Gato Bruxo", SkinRarity.Epica, "pet_cat", new Color(0.25f, 0.15f, 0.35f), new Color(1f, 0.55f, 0.15f), new Color(0.55f, 1f, 0.3f));
            witch.unlock = new UnlockRule(UnlockMethod.Event, 0, "evt_halloween");
            list.Add(witch);
            return list;
        }

        static CharacterLook Look(Color skin, Color outfit, Color pants, Color accent, Color hair, Color gear, HeadGear headGear) =>
            new CharacterLook { skin = skin, outfit = outfit, pants = pants, accent = accent, hair = hair, gear = gear, headGear = headGear };

        static SkinDefinition BrawlerSkin(string id, string name, SkinRarity rarity, string brawlerId, UnlockRule unlock, CharacterLook look, Color weaponAccent, bool sparkle = false)
        {
            return new SkinDefinition
            {
                skinId = id, displayName = name, rarity = rarity, target = SkinTarget.Brawler, targetId = brawlerId,
                unlock = unlock, look = look, weaponAccent = weaponAccent, sparkle = sparkle,
            };
        }

        static SkinDefinition PetSkin(string id, string name, SkinRarity rarity, string petId, Color main, Color secondary, Color accent)
        {
            return new SkinDefinition
            {
                skinId = id, displayName = name, rarity = rarity, target = SkinTarget.Pet, targetId = petId,
                unlock = new UnlockRule(UnlockMethod.RewardOnly), petMain = main, petSecondary = secondary, petAccent = accent,
            };
        }

        public static List<QuestDefinition> Quests()
        {
            var evt = new QuestDefinition("evt_halloween_elims", "Noite das Abóboras", QuestPeriod.Event, QuestMetric.Eliminations, 15,
                new RewardEntry(RewardType.PetSkin, 1, "skin_pet_cat_witch"), new RewardEntry(RewardType.Coins, 100))
            {
                description = "Evento: 15 eliminações entre 25/10 e 02/11.",
                eventStartUtc = "2026-10-25",
                eventEndUtc = "2026-11-02",
            };
            return new List<QuestDefinition>
            {
                // Diárias
                new QuestDefinition("d_play3", "Joga 3 partidas", QuestPeriod.Daily, QuestMetric.MatchesPlayed, 3, new RewardEntry(RewardType.PlayerXp, 60), new RewardEntry(RewardType.Coins, 30)),
                new QuestDefinition("d_elims10", "Faz 10 eliminações", QuestPeriod.Daily, QuestMetric.Eliminations, 10, new RewardEntry(RewardType.PlayerXp, 80), new RewardEntry(RewardType.Coins, 40)),
                new QuestDefinition("d_pet2", "Joga 2 partidas com um pet", QuestPeriod.Daily, QuestMetric.MatchesWithPet, 2, new RewardEntry(RewardType.PlayerXp, 50), new RewardEntry(RewardType.Coins, 25)),
                // Semanais
                new QuestDefinition("w_elims50", "Faz 50 eliminações", QuestPeriod.Weekly, QuestMetric.Eliminations, 50, new RewardEntry(RewardType.PlayerXp, 300), new RewardEntry(RewardType.Coins, 150)),
                new QuestDefinition("w_wins10", "Vence 10 partidas", QuestPeriod.Weekly, QuestMetric.Wins, 10, new RewardEntry(RewardType.PlayerXp, 400), new RewardEntry(RewardType.Coins, 200)),
                new QuestDefinition("w_brawlers3", "Joga com 3 Brawlers diferentes", QuestPeriod.Weekly, QuestMetric.DistinctBrawlers, 3, new RewardEntry(RewardType.PlayerXp, 200), new RewardEntry(RewardType.Pet, 1, "pet_rabbit")),
                // Conquistas
                new QuestDefinition("ach_kills100", "Caçador: 100 eliminações", QuestPeriod.Achievement, QuestMetric.Eliminations, 100, new RewardEntry(RewardType.BrawlerSkin, 1, "skin_trapper_shadow")),
                new QuestDefinition("ach_headshots25", "Olho de lince: 25 headshots", QuestPeriod.Achievement, QuestMetric.Headshots, 25, new RewardEntry(RewardType.PetSkin, 1, "skin_pet_dog_golden")),
                new QuestDefinition("ach_level10", "Veterano: nível 10", QuestPeriod.Achievement, QuestMetric.PlayerLevel, 10, new RewardEntry(RewardType.Coins, 300)),
                // Desafios
                new QuestDefinition("chl_pet5", "Treinador: um pet no nível 5", QuestPeriod.Challenge, QuestMetric.PetLevel, 5, new RewardEntry(RewardType.Cosmetic, 1, "badge_pet_trainer")),
                new QuestDefinition("chl_pets3", "Colecionador: 3 pets", QuestPeriod.Challenge, QuestMetric.PetsUnlocked, 3, new RewardEntry(RewardType.Coins, 300)),
                // Evento futuro
                evt,
            };
        }

        public static List<CosmeticDefinition> Cosmetics()
        {
            var list = new List<CosmeticDefinition>
            {
                new CosmeticDefinition("title_veteran", "Título: Veterano", SkinRarity.Rara),
                new CosmeticDefinition("title_legend", "Título: Lenda", SkinRarity.Lendaria),
                new CosmeticDefinition("badge_pet_trainer", "Emblema: Treinador", SkinRarity.Rara),
            };
            foreach (var id in PetIds)
                list.Add(new CosmeticDefinition("badge_" + id.Replace("pet_", "") + "_master", "Emblema: Mestre " + id.Replace("pet_", ""), SkinRarity.Epica));
            return list;
        }

        // ------------------------------------------------------------------ aspeto dos pets (cartoon, primitivas)

        public struct PetLook
        {
            public Color main, secondary, accent;
            public PetGait gait;
            public float hopHeight, walkSpeed, runSpeed;
            public List<PetPart> parts;
        }

        static PetPart P(string n, PetShape s, PetColorSlot c, PetPartRole r, float x, float y, float z, float sx, float sy, float sz, float ex = 0f, float ey = 0f, float ez = 0f, int min = 1, int max = 3) =>
            new PetPart(n, s, c, r, new Vector3(x, y, z), new Vector3(sx, sy, sz), new Vector3(ex, ey, ez), min, max);

        /// <summary>Peças de evolução comuns: coleira (fase 2+) e coroa pequena (fase 3).</summary>
        static void Evolution(List<PetPart> parts, float neckY, float neckZ, float headTopY, float headZ, float neckSize)
        {
            parts.Add(P("collar", PetShape.Cylinder, PetColorSlot.Accent, PetPartRole.Detail, 0f, neckY, neckZ, neckSize, 0.025f, neckSize, 15f, 0f, 0f, 2));
            parts.Add(P("tag", PetShape.Sphere, PetColorSlot.Gold, PetPartRole.Detail, 0f, neckY - 0.04f, neckZ + neckSize * 0.5f, 0.05f, 0.05f, 0.02f, 0f, 0f, 0f, 2));
            parts.Add(P("crown", PetShape.Cylinder, PetColorSlot.Gold, PetPartRole.Head, 0f, headTopY, headZ, 0.12f, 0.03f, 0.12f, 0f, 0f, 0f, 3));
            for (int i = 0; i < 3; i++)
            {
                float a = (i - 1) * 0.045f;
                parts.Add(P("crownTip" + i, PetShape.Cube, PetColorSlot.Gold, PetPartRole.Head, a, headTopY + 0.04f, headZ, 0.025f, 0.05f, 0.025f, 0f, 45f, 0f, 3));
            }
        }

        static void Legs(List<PetPart> parts, PetColorSlot color, float x, float zFront, float zBack, float h, float w)
        {
            parts.Add(P("legFL", PetShape.Cylinder, color, PetPartRole.LegFrontLeft, -x, h * 0.5f, zFront, w, h * 0.5f, w));
            parts.Add(P("legFR", PetShape.Cylinder, color, PetPartRole.LegFrontRight, x, h * 0.5f, zFront, w, h * 0.5f, w));
            parts.Add(P("legBL", PetShape.Cylinder, color, PetPartRole.LegBackLeft, -x, h * 0.5f, zBack, w, h * 0.5f, w));
            parts.Add(P("legBR", PetShape.Cylinder, color, PetPartRole.LegBackRight, x, h * 0.5f, zBack, w, h * 0.5f, w));
        }

        static void Eyes(List<PetPart> parts, float x, float y, float z, float size)
        {
            parts.Add(P("eyeL", PetShape.Sphere, PetColorSlot.Eye, PetPartRole.Head, -x, y, z, size, size * 1.15f, size * 0.6f));
            parts.Add(P("eyeR", PetShape.Sphere, PetColorSlot.Eye, PetPartRole.Head, x, y, z, size, size * 1.15f, size * 0.6f));
            parts.Add(P("shineL", PetShape.Sphere, PetColorSlot.White, PetPartRole.Head, -x + size * 0.18f, y + size * 0.2f, z + size * 0.25f, size * 0.35f, size * 0.35f, size * 0.2f));
            parts.Add(P("shineR", PetShape.Sphere, PetColorSlot.White, PetPartRole.Head, x + size * 0.18f, y + size * 0.2f, z + size * 0.25f, size * 0.35f, size * 0.35f, size * 0.2f));
        }

        public static PetLook LookFor(string petId)
        {
            var parts = new List<PetPart>();
            var look = new PetLook { gait = PetGait.Trot, hopHeight = 0.06f, walkSpeed = 6.5f, runSpeed = 11f, parts = parts };
            switch (petId)
            {
                case "pet_cat":
                    look.main = new Color(0.98f, 0.62f, 0.25f); look.secondary = new Color(1f, 0.92f, 0.8f); look.accent = new Color(0.3f, 0.6f, 1f);
                    parts.Add(P("body", PetShape.Capsule, PetColorSlot.Main, PetPartRole.Body, 0f, 0.27f, 0f, 0.22f, 0.24f, 0.22f, 90f));
                    parts.Add(P("belly", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Body, 0f, 0.23f, 0.05f, 0.18f, 0.14f, 0.3f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.46f, 0.24f, 0.29f, 0.26f, 0.26f));
                    parts.Add(P("muzzle", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Head, 0f, 0.42f, 0.35f, 0.13f, 0.09f, 0.08f));
                    parts.Add(P("nose", PetShape.Sphere, PetColorSlot.Nose, PetPartRole.Head, 0f, 0.45f, 0.39f, 0.04f, 0.03f, 0.03f));
                    parts.Add(P("earL", PetShape.Cube, PetColorSlot.Main, PetPartRole.EarLeft, -0.09f, 0.6f, 0.22f, 0.08f, 0.08f, 0.03f, 0f, 0f, 45f));
                    parts.Add(P("earR", PetShape.Cube, PetColorSlot.Main, PetPartRole.EarRight, 0.09f, 0.6f, 0.22f, 0.08f, 0.08f, 0.03f, 0f, 0f, 45f));
                    Eyes(parts, 0.065f, 0.49f, 0.35f, 0.06f);
                    Legs(parts, PetColorSlot.Main, 0.07f, 0.12f, -0.12f, 0.18f, 0.06f);
                    parts.Add(P("tail", PetShape.Capsule, PetColorSlot.Main, PetPartRole.Tail, 0f, 0.42f, -0.26f, 0.045f, 0.16f, 0.045f, -25f));
                    parts.Add(P("tailTip", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Tail, 0f, 0.57f, -0.33f, 0.06f, 0.06f, 0.06f));
                    Evolution(parts, 0.38f, 0.17f, 0.6f, 0.24f, 0.17f);
                    look.walkSpeed = 7f;
                    break;

                case "pet_pig":
                    look.main = new Color(1f, 0.68f, 0.72f); look.secondary = new Color(1f, 0.82f, 0.85f); look.accent = new Color(0.35f, 0.55f, 1f);
                    look.gait = PetGait.Waddle;
                    parts.Add(P("body", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Body, 0f, 0.28f, 0f, 0.4f, 0.34f, 0.46f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.4f, 0.24f, 0.3f, 0.28f, 0.26f));
                    parts.Add(P("snout", PetShape.Cylinder, PetColorSlot.Nose, PetPartRole.Head, 0f, 0.37f, 0.38f, 0.13f, 0.03f, 0.1f, 90f));
                    parts.Add(P("nostrilL", PetShape.Sphere, PetColorSlot.Dark, PetPartRole.Head, -0.025f, 0.37f, 0.41f, 0.025f, 0.035f, 0.01f));
                    parts.Add(P("nostrilR", PetShape.Sphere, PetColorSlot.Dark, PetPartRole.Head, 0.025f, 0.37f, 0.41f, 0.025f, 0.035f, 0.01f));
                    parts.Add(P("earL", PetShape.Cube, PetColorSlot.Secondary, PetPartRole.EarLeft, -0.1f, 0.53f, 0.2f, 0.08f, 0.07f, 0.02f, 20f, 0f, 30f));
                    parts.Add(P("earR", PetShape.Cube, PetColorSlot.Secondary, PetPartRole.EarRight, 0.1f, 0.53f, 0.2f, 0.08f, 0.07f, 0.02f, 20f, 0f, -30f));
                    Eyes(parts, 0.07f, 0.46f, 0.34f, 0.05f);
                    Legs(parts, PetColorSlot.Main, 0.1f, 0.12f, -0.12f, 0.12f, 0.08f);
                    parts.Add(P("tail", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Tail, 0f, 0.36f, -0.24f, 0.06f, 0.06f, 0.06f));
                    Evolution(parts, 0.36f, 0.14f, 0.55f, 0.24f, 0.2f);
                    look.walkSpeed = 6f;
                    break;

                case "pet_rooster":
                    look.main = new Color(0.98f, 0.95f, 0.88f); look.secondary = new Color(0.9f, 0.35f, 0.15f); look.accent = new Color(0.95f, 0.15f, 0.15f);
                    look.gait = PetGait.Waddle;
                    parts.Add(P("body", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Body, 0f, 0.3f, 0f, 0.28f, 0.28f, 0.34f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.52f, 0.12f, 0.18f, 0.2f, 0.18f));
                    parts.Add(P("beak", PetShape.Cube, PetColorSlot.Beak, PetPartRole.Head, 0f, 0.51f, 0.22f, 0.05f, 0.05f, 0.07f, 45f));
                    parts.Add(P("wattle", PetShape.Sphere, PetColorSlot.Accent, PetPartRole.Head, 0f, 0.46f, 0.2f, 0.04f, 0.06f, 0.04f));
                    parts.Add(P("comb1", PetShape.Sphere, PetColorSlot.Accent, PetPartRole.Head, 0f, 0.63f, 0.16f, 0.05f, 0.07f, 0.05f));
                    parts.Add(P("comb2", PetShape.Sphere, PetColorSlot.Accent, PetPartRole.Head, 0f, 0.65f, 0.11f, 0.05f, 0.08f, 0.05f));
                    parts.Add(P("comb3", PetShape.Sphere, PetColorSlot.Accent, PetPartRole.Head, 0f, 0.63f, 0.06f, 0.05f, 0.06f, 0.05f));
                    Eyes(parts, 0.06f, 0.55f, 0.19f, 0.04f);
                    parts.Add(P("wingL", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.WingLeft, -0.14f, 0.32f, -0.01f, 0.06f, 0.18f, 0.24f));
                    parts.Add(P("wingR", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.WingRight, 0.14f, 0.32f, -0.01f, 0.06f, 0.18f, 0.24f));
                    parts.Add(P("tail1", PetShape.Capsule, PetColorSlot.Secondary, PetPartRole.Tail, 0f, 0.45f, -0.2f, 0.05f, 0.14f, 0.05f, -35f));
                    parts.Add(P("tail2", PetShape.Capsule, PetColorSlot.Dark, PetPartRole.Tail, -0.05f, 0.42f, -0.2f, 0.04f, 0.12f, 0.04f, -30f, 0f, 20f));
                    parts.Add(P("tail3", PetShape.Capsule, PetColorSlot.Dark, PetPartRole.Tail, 0.05f, 0.42f, -0.2f, 0.04f, 0.12f, 0.04f, -30f, 0f, -20f));
                    parts.Add(P("legL", PetShape.Cylinder, PetColorSlot.Beak, PetPartRole.LegFrontLeft, -0.06f, 0.08f, 0f, 0.035f, 0.08f, 0.035f));
                    parts.Add(P("legR", PetShape.Cylinder, PetColorSlot.Beak, PetPartRole.LegFrontRight, 0.06f, 0.08f, 0f, 0.035f, 0.08f, 0.035f));
                    Evolution(parts, 0.42f, 0.1f, 0.7f, 0.11f, 0.15f);
                    look.walkSpeed = 6.2f;
                    break;

                case "pet_rabbit":
                    look.main = new Color(0.85f, 0.75f, 0.65f); look.secondary = new Color(1f, 0.97f, 0.95f); look.accent = new Color(0.4f, 0.85f, 0.45f);
                    look.gait = PetGait.Hop; look.hopHeight = 0.22f; look.runSpeed = 12f;
                    parts.Add(P("body", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Body, 0f, 0.24f, -0.02f, 0.3f, 0.28f, 0.36f));
                    parts.Add(P("belly", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Body, 0f, 0.22f, 0.06f, 0.2f, 0.2f, 0.22f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.42f, 0.16f, 0.26f, 0.24f, 0.24f));
                    parts.Add(P("muzzle", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Head, 0f, 0.39f, 0.27f, 0.11f, 0.08f, 0.07f));
                    parts.Add(P("nose", PetShape.Sphere, PetColorSlot.Nose, PetPartRole.Head, 0f, 0.41f, 0.3f, 0.035f, 0.03f, 0.03f));
                    parts.Add(P("earL", PetShape.Capsule, PetColorSlot.Main, PetPartRole.EarLeft, -0.06f, 0.62f, 0.12f, 0.06f, 0.14f, 0.035f, -10f, 0f, 8f));
                    parts.Add(P("earR", PetShape.Capsule, PetColorSlot.Main, PetPartRole.EarRight, 0.06f, 0.62f, 0.12f, 0.06f, 0.14f, 0.035f, -10f, 0f, -8f));
                    parts.Add(P("earInL", PetShape.Capsule, PetColorSlot.Nose, PetPartRole.EarLeft, -0.06f, 0.62f, 0.135f, 0.035f, 0.1f, 0.02f, -10f, 0f, 8f));
                    parts.Add(P("earInR", PetShape.Capsule, PetColorSlot.Nose, PetPartRole.EarRight, 0.06f, 0.62f, 0.135f, 0.035f, 0.1f, 0.02f, -10f, 0f, -8f));
                    Eyes(parts, 0.065f, 0.45f, 0.26f, 0.055f);
                    parts.Add(P("footL", PetShape.Capsule, PetColorSlot.Secondary, PetPartRole.LegBackLeft, -0.09f, 0.05f, -0.04f, 0.07f, 0.09f, 0.07f, 90f));
                    parts.Add(P("footR", PetShape.Capsule, PetColorSlot.Secondary, PetPartRole.LegBackRight, 0.09f, 0.05f, -0.04f, 0.07f, 0.09f, 0.07f, 90f));
                    parts.Add(P("tail", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Tail, 0f, 0.28f, -0.22f, 0.11f, 0.11f, 0.11f));
                    Evolution(parts, 0.33f, 0.1f, 0.56f, 0.16f, 0.16f);
                    break;

                case "pet_turtle":
                    look.main = new Color(0.55f, 0.82f, 0.45f); look.secondary = new Color(0.4f, 0.55f, 0.25f); look.accent = new Color(0.95f, 0.6f, 0.2f);
                    look.gait = PetGait.Crawl; look.walkSpeed = 5.5f; look.runSpeed = 10f;
                    parts.Add(P("shell", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Body, 0f, 0.2f, 0f, 0.44f, 0.3f, 0.5f));
                    parts.Add(P("shellRim", PetShape.Cylinder, PetColorSlot.Main, PetPartRole.Body, 0f, 0.13f, 0f, 0.46f, 0.02f, 0.52f));
                    parts.Add(P("spot1", PetShape.Sphere, PetColorSlot.Dark, PetPartRole.Body, 0f, 0.35f, 0f, 0.14f, 0.04f, 0.14f));
                    parts.Add(P("spot2", PetShape.Sphere, PetColorSlot.Dark, PetPartRole.Body, -0.12f, 0.3f, 0.1f, 0.1f, 0.04f, 0.1f, 0f, 0f, 30f));
                    parts.Add(P("spot3", PetShape.Sphere, PetColorSlot.Dark, PetPartRole.Body, 0.12f, 0.3f, -0.1f, 0.1f, 0.04f, 0.1f, 0f, 0f, -30f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.24f, 0.32f, 0.2f, 0.18f, 0.2f));
                    Eyes(parts, 0.055f, 0.28f, 0.4f, 0.045f);
                    Legs(parts, PetColorSlot.Main, 0.16f, 0.15f, -0.15f, 0.12f, 0.09f);
                    parts.Add(P("tail", PetShape.Capsule, PetColorSlot.Main, PetPartRole.Tail, 0f, 0.14f, -0.27f, 0.04f, 0.06f, 0.04f, -70f));
                    Evolution(parts, 0.22f, 0.25f, 0.35f, 0.32f, 0.14f);
                    break;

                default: // pet_dog
                    look.main = new Color(0.78f, 0.52f, 0.3f); look.secondary = new Color(1f, 0.9f, 0.75f); look.accent = new Color(0.95f, 0.25f, 0.25f);
                    parts.Add(P("body", PetShape.Capsule, PetColorSlot.Main, PetPartRole.Body, 0f, 0.3f, 0f, 0.27f, 0.26f, 0.27f, 90f));
                    parts.Add(P("belly", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Body, 0f, 0.25f, 0.06f, 0.2f, 0.16f, 0.32f));
                    parts.Add(P("head", PetShape.Sphere, PetColorSlot.Main, PetPartRole.Head, 0f, 0.5f, 0.27f, 0.3f, 0.28f, 0.28f));
                    parts.Add(P("snout", PetShape.Sphere, PetColorSlot.Secondary, PetPartRole.Head, 0f, 0.45f, 0.4f, 0.15f, 0.11f, 0.15f));
                    parts.Add(P("nose", PetShape.Sphere, PetColorSlot.Eye, PetPartRole.Head, 0f, 0.48f, 0.47f, 0.05f, 0.04f, 0.04f));
                    parts.Add(P("earL", PetShape.Capsule, PetColorSlot.Dark, PetPartRole.EarLeft, -0.14f, 0.5f, 0.25f, 0.06f, 0.1f, 0.04f, 0f, 0f, 20f));
                    parts.Add(P("earR", PetShape.Capsule, PetColorSlot.Dark, PetPartRole.EarRight, 0.14f, 0.5f, 0.25f, 0.06f, 0.1f, 0.04f, 0f, 0f, -20f));
                    Eyes(parts, 0.07f, 0.54f, 0.39f, 0.06f);
                    Legs(parts, PetColorSlot.Main, 0.09f, 0.14f, -0.14f, 0.2f, 0.07f);
                    parts.Add(P("tail", PetShape.Capsule, PetColorSlot.Main, PetPartRole.Tail, 0f, 0.44f, -0.28f, 0.045f, 0.1f, 0.045f, -40f));
                    Evolution(parts, 0.42f, 0.2f, 0.66f, 0.27f, 0.19f);
                    break;
            }
            return look;
        }

        // ------------------------------------------------------------------ assets em memória (reserva)

        public static PetDefinition CreatePetDefinition(PetRules rules)
        {
            var def = ScriptableObject.CreateInstance<PetDefinition>();
            FillPetDefinition(def, rules);
            return def;
        }

        public static void FillPetDefinition(PetDefinition def, PetRules rules)
        {
            var look = LookFor(rules.petId);
            def.name = rules.petId;
            def.rules = rules;
            def.mainColor = look.main;
            def.secondaryColor = look.secondary;
            def.accentColor = look.accent;
            def.parts = look.parts;
            def.gait = look.gait;
            def.hopHeight = look.hopHeight;
            def.walkSpeed = look.walkSpeed;
            def.runSpeed = look.runSpeed;
        }

        public static GameExperienceDatabase CreateDatabase()
        {
            var db = ScriptableObject.CreateInstance<GameExperienceDatabase>();
            db.name = "GameExperienceDatabase (omissão)";
            db.progression = ScriptableObject.CreateInstance<ProgressionConfig>();
            db.progression.settings = ProgressionSettings.CreateDefault();
            foreach (var rules in PetRulesList()) db.pets.Add(CreatePetDefinition(rules));
            db.skins = Skins();
            db.quests = Quests();
            db.cosmetics = Cosmetics();
            return db;
        }
    }
}
