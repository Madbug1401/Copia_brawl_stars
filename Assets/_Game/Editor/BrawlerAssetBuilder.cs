using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Gera os dados iniciais dos 4 Brawlers (BrawlerDefinition + AttackDefinitions + Deliveries) e a BrawlerRegistry,
    /// com os pontos de partida da especificação "FPS-LAN — Brawler Specifications".
    /// Depois de gerados, os valores ajustam-se no Inspector. Voltar a gerar SUBSTITUI os valores.
    /// </summary>
    [InitializeOnLoad]
    public static class BrawlerAssetBuilder
    {
        public const string DataRoot = "Assets/_Game/Data";
        public const string BrawlersRoot = DataRoot + "/Brawlers";
        public const string RegistryPath = "Assets/_Game/Resources/BrawlerRegistry.asset";

        static BrawlerAssetBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(RegistryPath)) Build();
            };
        }

        [MenuItem("FPS-LAN/Gerar dados dos Brawlers (substitui valores)", priority = 5)]
        static void BuildMenu()
        {
            if (File.Exists(RegistryPath) && !EditorUtility.DisplayDialog("FPS-LAN",
                    "Voltar a gerar os dados dos 4 Brawlers com os valores iniciais da especificação? Ajustes feitos no Inspector serão substituídos.",
                    "Gerar", "Cancel"))
                return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder(BrawlersRoot);
            EnsureFolder(DataRoot + "/Test");
            EnsureFolder("Assets/_Game/Resources");

            var registry = LoadOrCreate<BrawlerRegistry>(RegistryPath);
            registry.brawlers.Clear();
            registry.brawlers.Add(Gunslinger());
            registry.brawlers.Add(Artillery());
            registry.brawlers.Add(Trapper());
            registry.brawlers.Add(Sprayer());
            registry.brawlers.Add(Shotgunner());
            registry.brawlers.Add(Sniper());
            EditorUtility.SetDirty(registry);

            TestHitscan();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FPS-LAN] Dados dos Brawlers gerados em " + BrawlersRoot + " e registry em " + RegistryPath);
        }

        // =====================================================================================
        // Ekko (id "gunslinger") — Pistoleiro/Metralhador: duas metralhadoras compactas automáticas.
        // Dano por bala com queda pela distância; Especial: Minigun temporária (máx. 10 rajadas × 4 balas × 5 = 200).
        // =====================================================================================
        static BrawlerDefinition Gunslinger()
        {
            const string id = "gunslinger";
            var b = Definition(id);
            b.displayName = "Ekko";
            b.shortName = "";
            b.role = "The Gunslinger · Dual pistols and rapid-fire minigun.";
            b.stats = new BrawlerStats { maxHealth = 120, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            // Rajada de 5 balas alternando as mãos: 12 por bala de perto (60/rajada), 7 no alcance máximo
            var main = Attack<ProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 5; d.interval = 0.06f; d.spreadDegrees = 2.2f; d.fanDegrees = 0f;
                d.speed = 95f; d.range = 26f; d.radius = 0.1f; d.pierce = false; d.gravity = 0f;
            }, e =>
            {
                e.damage = 12; e.nearMultiplier = 1f; e.farMultiplier = 0.6f; e.falloffStartFraction = 0.2f; e.canHeadshot = true; e.headshotMultiplier = 1.25f;
            }, v => { v.color = new Color(1f, 0.85f, 0.35f); v.scale = 0.9f; v.style = ProjectileStyle.Bullet; v.sound = Sound.Smg; v.impactSound = Sound.Hit; });

            // Minigun: cada disparo do modo = 4 balas de 5 (sem headshot), máx. 10 disparos → 200 de dano potencial
            var super = Attack<ProjectileDelivery>(id, "Super", d =>
            {
                d.count = 4; d.interval = 0.07f; d.spreadDegrees = 3.5f; d.fanDegrees = 0f;
                d.speed = 110f; d.range = 30f; d.radius = 0.12f; d.pierce = false; d.gravity = 0f;
            }, e =>
            {
                e.damage = 5; e.nearMultiplier = 1f; e.farMultiplier = 0.7f; e.canHeadshot = false; e.coverDamage = 8;
            }, v => { v.color = new Color(1f, 0.45f, 0.2f); v.scale = 1.1f; v.style = ProjectileStyle.Bullet; v.sound = Sound.Minigun; v.impactSound = Sound.Hit; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.1f, cooldown = 0.42f };
            b.super = new SuperDefinition
            {
                attack = super, chargeRequired = 300f, activation = SuperActivation.Mode,
                mode = new SuperModeSettings
                {
                    label = "Minigun", duration = 3.2f, maxShots = 10, shotCooldown = 0.3f, activationDelay = 0.4f,
                    consumeOnFirstShot = false, cancellable = false,
                },
            };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.DualSMG,
                muzzleOffsets = new[] { new Vector3(0.19f, -0.175f, 0.68f), new Vector3(-0.19f, -0.175f, 0.68f) },
                accentColor = new Color(0.95f, 0.75f, 0.35f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.DualSMG,
                muzzleOffsets = new[] { new Vector3(0.32f, 0.02f, 0.7f), new Vector3(-0.32f, 0.02f, 0.7f) },
                bodyColor = new Color(0.95f, 0.75f, 0.35f),
                look = new CharacterLook
                {
                    skin = new Color(1f, 0.8f, 0.62f), outfit = new Color(0.98f, 0.82f, 0.3f), pants = new Color(0.3f, 0.36f, 0.6f),
                    accent = new Color(0.92f, 0.22f, 0.2f), hair = new Color(0.4f, 0.24f, 0.12f), gear = new Color(0.55f, 0.33f, 0.18f),
                    headGear = HeadGear.WideHat, bodyScale = 1f, headScale = 1f, bulk = 0.95f,
                },
            };
            b.animations = new AnimationSet { attackKick = 0.6f, attackDuration = 0.35f, reloadDuration = 0.35f, superDuration = 0.6f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color, muzzleFlashScale = 0.8f };
            b.sfx = new SFXSet { primary = Sound.Smg, super = Sound.Minigun, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 12f, superUseRange = 16f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Lupus Marinus "L.M" (id "artillery") — Artilheiro/TNT: um molho de TNT por ataque, explode no impacto.
        // 40 de dano no centro (acerto direto) a cair com a distância; Especial: cinco minas com sensor de movimento.
        // =====================================================================================
        static BrawlerDefinition Artillery()
        {
            const string id = "artillery";
            var b = Definition(id);
            b.displayName = "Lupus Marinus";
            b.shortName = "L.M";
            b.role = "The Demolitionist · Arcing TNT and proximity mines.";
            b.stats = new BrawlerStats { maxHealth = 100, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            // TNT: acerto direto/centro = 40 (3 TNT = 120); dano total até 30% do raio, depois desce até 35% na borda
            var main = Attack<ArcProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 1; d.interval = 0f; d.landingSpread = 0f;
                d.speed = 18f; d.range = 18f; d.gravity = 22f; d.minFlightTime = 0.45f; d.maxFlightTime = 1.0f;
                d.radius = 0.22f; d.fuseDelay = 0f; d.hitsPlayers = true;
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.splashRadius = 2.4f; e.splashDamage = 40; e.splashFullRadiusFraction = 0.3f; e.splashEdgeMultiplier = 0.35f; e.coverDamage = 35;
            }, v => { v.color = new Color(1f, 0.45f, 0.2f); v.scale = 1f; v.style = ProjectileStyle.Tnt; v.sound = Sound.Lob; v.impactSound = Sound.Explosion; });

            // Cinco minas: sensor de proximidade, aviso curto, explosão; destruídas por explosões/facadas inimigas
            var super = Attack<PlaceTrapsDelivery>(id, "Super", d =>
            {
                d.count = 5; d.spread = 1.6f; d.range = 13f;
                d.trap = new TrapSettings
                {
                    kind = TrapKind.Explosive, armTime = 0.9f, triggerRadius = 1.7f, warningTime = 0.45f, lifetime = 25f,
                    hiddenFromEnemies = true, color = new Color(1f, 0.45f, 0.2f),
                    destroyedByEnemyExplosions = true, destroyedByEnemyMelee = true, removeOnOwnerDeath = false,
                    maxActivePerOwner = 10, placementClearance = 0.3f,
                };
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.splashRadius = 2.6f; e.splashDamage = 55; e.splashFullRadiusFraction = 0.35f; e.splashEdgeMultiplier = 0.4f;
                e.knockback = 5f; e.knockbackUp = 3f; e.coverDamage = 40;
            }, v => { v.color = new Color(1f, 0.5f, 0.25f); v.scale = 1f; v.sound = Sound.Lob; v.impactSound = Sound.Explosion; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.35f, cooldown = 0.55f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 160f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.DualExplosives,
                muzzleOffsets = new[] { new Vector3(0.22f, -0.12f, 0.42f), new Vector3(-0.22f, -0.12f, 0.42f) },
                accentColor = new Color(1f, 0.45f, 0.2f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.DualExplosives,
                muzzleOffsets = new[] { new Vector3(0.35f, 0.2f, 0.4f), new Vector3(-0.35f, 0.2f, 0.4f) },
                bodyColor = new Color(0.9f, 0.4f, 0.25f),
                look = new CharacterLook
                {
                    skin = new Color(0.98f, 0.76f, 0.6f), outfit = new Color(0.92f, 0.45f, 0.22f), pants = new Color(0.35f, 0.28f, 0.22f),
                    accent = new Color(1f, 0.85f, 0.25f), hair = new Color(0.95f, 0.45f, 0.15f), gear = new Color(0.45f, 0.32f, 0.22f),
                    headGear = HeadGear.Goggles, bodyScale = 0.95f, headScale = 1.08f, bulk = 1.05f,
                },
            };
            b.animations = new AnimationSet { attackKick = 0.6f, attackDuration = 0.5f, reloadDuration = 0.4f, superDuration = 0.7f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color, impactScale = 1.4f };
            b.sfx = new SFXSet { primary = Sound.Lob, super = Sound.Lob, impact = Sound.Explosion };
            b.bot = new BotProfile { preferredRange = 13f, superUseRange = 12f, aimAtFeet = true };
            return Save(b);
        }

        // =====================================================================================
        // N.Cage (id "trapper") — Arqueiro/Controlo: três flechas por ataque (33+34+33 = 100 se todas acertarem).
        // Especial: três armadilhas que imobilizam 2 s e envenenam (16 no total).
        // =====================================================================================
        static BrawlerDefinition Trapper()
        {
            const string id = "trapper";
            var b = Definition(id);
            b.displayName = "N.Cage";
            b.shortName = "";
            b.role = "The Saboteur · Arrows, traps and poison.";
            b.stats = new BrawlerStats { maxHealth = 110, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            // Três flechas com trajetória (ligeira queda); dano exato por flecha; flechas que falham não contam
            var main = Attack<ProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 3; d.interval = 0f; d.spreadDegrees = 0f; d.fanDegrees = 7f;
                d.speed = 55f; d.range = 24f; d.radius = 0.15f; d.pierce = false; d.gravity = 4f;
            }, e =>
            {
                e.damage = 33; e.perShotDamage = new[] { 33, 34, 33 }; e.nearMultiplier = 1f; e.farMultiplier = 1f;
                e.canHeadshot = false; e.splashRadius = 0f; e.splashDamage = 0; e.coverDamage = 10;
            }, v => { v.color = new Color(0.45f, 0.75f, 1f); v.scale = 1.2f; v.style = ProjectileStyle.Arrow; v.sound = Sound.Bow; v.impactSound = Sound.Hit; });

            // Armadilhas de imobilização: prendem 2 s (pode olhar e atacar), 8/s de veneno durante esses 2 s = 16; imunidade de 3 s depois
            var super = Attack<PlaceTrapsDelivery>(id, "Super", d =>
            {
                d.count = 3; d.spread = 1.6f; d.range = 12f;
                d.trap = new TrapSettings
                {
                    kind = TrapKind.Snare, armTime = 0.6f, triggerRadius = 1.1f, warningTime = 0f, lifetime = 30f,
                    hiddenFromEnemies = true, color = new Color(0.45f, 0.9f, 0.35f),
                    destroyedByEnemyExplosions = true, destroyedByEnemyMelee = true, removeOnOwnerDeath = false,
                    maxActivePerOwner = 6, placementClearance = 0.3f,
                };
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.rootDuration = 2f; e.rootImmunity = 3f; e.dotDamagePerSecond = 8f; e.dotDuration = 2f;
            }, v => { v.color = new Color(0.45f, 0.9f, 0.35f); v.scale = 1f; v.sound = Sound.Lob; v.impactSound = Sound.Snare; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.6f, cooldown = 0.6f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 170f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.Bow,
                muzzleOffsets = new[] { new Vector3(0.05f, -0.08f, 0.55f) },
                accentColor = new Color(0.4f, 0.7f, 1f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.Bow,
                muzzleOffsets = new[] { new Vector3(0.1f, 0.05f, 0.7f) },
                bodyColor = new Color(0.4f, 0.7f, 1f),
                look = new CharacterLook
                {
                    skin = new Color(0.86f, 0.62f, 0.45f), outfit = new Color(0.25f, 0.55f, 0.75f), pants = new Color(0.22f, 0.3f, 0.35f),
                    accent = new Color(0.95f, 0.95f, 0.9f), hair = new Color(0.15f, 0.12f, 0.1f), gear = new Color(0.95f, 0.5f, 0.2f),
                    headGear = HeadGear.FoxHood, bodyScale = 1f, headScale = 1f, bulk = 1f,
                },
            };
            b.animations = new AnimationSet { attackKick = 0.7f, attackDuration = 0.45f, reloadDuration = 0.45f, superDuration = 0.6f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color };
            b.sfx = new SFXSet { primary = Sound.Bow, super = Sound.Lob, impact = Sound.Explosion };
            b.bot = new BotProfile { preferredRange = 14f, superUseRange = 10f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Mr.S (id "sprayer") — Especialista em spray: duas latas, cone de curto alcance (4 ticks × 14 = 56; 52 na borda).
        // Especial: duas granadas de veneno que deixam áreas tóxicas durante 12 s (sem acumulação do mesmo atacante).
        // =====================================================================================
        static BrawlerDefinition Sprayer()
        {
            const string id = "sprayer";
            var b = Definition(id);
            b.displayName = "Mr.S";
            b.shortName = "";
            b.role = "The Street Artist · Close-range spray and poison grenades.";
            b.stats = new BrawlerStats { maxHealth = 160, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.4f, sprintSpeed = 9.4f, jumpHeight = 1.3f };

            // Um só volume de dano (as duas latas são visuais); cada alvo conta uma vez por tick e no máximo 4 ticks
            var main = Attack<ConeVolumeDelivery>(id, "Primary", d =>
            {
                d.range = 7.5f; d.angle = 36f; d.duration = 0.6f; d.tickInterval = 0.15f; d.expandTime = 0.12f;
                d.maxTicksPerTarget = 4; d.dualStream = true;
            }, e =>
            {
                e.damage = 14; e.nearMultiplier = 1f; e.farMultiplier = 0.9f; e.canHeadshot = false;
            }, v => { v.color = new Color(0.7f, 0.45f, 1f); v.scale = 1f; v.style = ProjectileStyle.Orb; v.sound = Sound.Spray; v.impactSound = Sound.Hit; });

            // Granadas de veneno: zona verde/roxa de 2,8 m durante 12 s, 8 de dano por segundo a quem estiver dentro
            var super = Attack<ArcProjectileDelivery>(id, "Super", d =>
            {
                d.count = 2; d.interval = 0.12f; d.landingSpread = 2.2f;
                d.speed = 16f; d.range = 16f; d.gravity = 22f; d.minFlightTime = 0.5f; d.maxFlightTime = 1.0f;
                d.radius = 0.2f; d.fuseDelay = 0.25f; d.hitsPlayers = false;
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false; e.splashRadius = 0f; e.splashDamage = 0;
                e.zoneOnImpact = new ZoneSettings
                {
                    enabled = true, radius = 2.8f, duration = 12f, tickInterval = 1f, tickDamage = 8, slowFactor = 1f, slowDuration = 0f,
                    color = new Color(0.45f, 0.95f, 0.3f), secondaryColor = new Color(0.75f, 0.4f, 1f), nonStackingPerAttacker = true,
                };
            }, v => { v.color = new Color(0.5f, 1f, 0.35f); v.scale = 1f; v.style = ProjectileStyle.Grenade; v.sound = Sound.Lob; v.impactSound = Sound.PoisonHiss; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.8f, cooldown = 0.65f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 150f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.DualSprayCans,
                muzzleOffsets = new[] { new Vector3(0.2f, -0.06f, 0.45f), new Vector3(-0.2f, -0.06f, 0.45f) },
                accentColor = new Color(0.78f, 0.5f, 1f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.DualSprayCans,
                muzzleOffsets = new[] { new Vector3(0.3f, 0.05f, 0.55f), new Vector3(-0.3f, 0.05f, 0.55f) },
                bodyColor = new Color(0.78f, 0.5f, 1f),
                look = new CharacterLook
                {
                    skin = new Color(0.85f, 0.95f, 0.8f), outfit = new Color(0.62f, 0.38f, 0.9f), pants = new Color(0.2f, 0.18f, 0.28f),
                    accent = new Color(0.4f, 1f, 0.6f), hair = new Color(0.95f, 0.45f, 0.75f), gear = new Color(0.3f, 0.25f, 0.4f),
                    headGear = HeadGear.GasMask, bodyScale = 0.98f, headScale = 1.05f, bulk = 0.95f,
                },
            };
            b.animations = new AnimationSet { attackKick = 0.3f, attackDuration = 0.6f, reloadDuration = 0.5f, superDuration = 0.8f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color };
            b.sfx = new SFXSet { primary = Sound.Spray, super = Sound.Lob, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 5.5f, superUseRange = 14f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Riot (id "shotgunner") — Caçadeira "pump" (como a Pump Shotgun do Fortnite): 10 bagos hitscan, 6 cada = 60 até 6 m;
        // queda: 10 m 80%, 13 m 60%, 15 m 49%, 18 m 35%, até 20% aos 31 m (alcance máximo). Mínimo de 3 bagos para fazer dano.
        // Especial: o Super de Brasa preservado (rajada que empurra e destrói cobertura). Vida mantém-se (135).
        // =====================================================================================
        static BrawlerDefinition Shotgunner()
        {
            const string id = "shotgunner";
            var b = Definition(id);
            b.displayName = "Riot";
            b.shortName = "";
            b.role = "The Breacher · Close-range shotgun and a powerful blast.";
            b.stats = new BrawlerStats { maxHealth = 135, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.7f, sprintSpeed = 9.7f, jumpHeight = 1.3f };

            var main = Attack<PumpShotgunDelivery>(id, "Primary", d =>
            {
                d.pellets = 10; d.spreadDegrees = 5.5f; d.jitterDegrees = 0.8f; d.range = 31f; d.effectiveRange = 18f;
                d.radius = 0.05f; d.minPelletsToDamage = 3;
            }, e =>
            {
                e.damage = 6; e.nearMultiplier = 1f; e.farMultiplier = 1f; e.falloffStartFraction = 0f; e.canHeadshot = false; e.coverDamage = 4;
                e.falloffCurve = new[]
                {
                    new Vector2(6f, 1f), new Vector2(10f, 0.8f), new Vector2(13f, 0.6f), new Vector2(15f, 0.49f),
                    new Vector2(18f, 0.35f), new Vector2(31f, 0.2f),
                };
            }, v => { v.color = new Color(1f, 0.6f, 0.25f); v.scale = 1f; v.style = ProjectileStyle.Pellet; v.sound = Sound.Shotgun; v.impactSound = Sound.Hit; });

            var super = Attack<ProjectileDelivery>(id, "Super", d =>
            {
                d.count = 11; d.interval = 0f; d.spreadDegrees = 2f; d.fanDegrees = 34f;
                d.speed = 65f; d.range = 15f; d.radius = 0.2f; d.pierce = false;
            }, e =>
            {
                e.damage = 17; e.nearMultiplier = 1.2f; e.farMultiplier = 0.7f; e.canHeadshot = false;
                e.knockback = 9f; e.knockbackUp = 3.5f; e.coverDamage = 120;
            }, v => { v.color = new Color(1f, 0.35f, 0.15f); v.scale = 1.4f; v.style = ProjectileStyle.Pellet; v.sound = Sound.Shotgun; v.impactSound = Sound.Hit; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.5f, cooldown = 0.55f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 230f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.Shotgun,
                muzzleOffsets = new[] { new Vector3(0.17f, -0.16f, 0.7f) },
                accentColor = new Color(1f, 0.45f, 0.25f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.Shotgun,
                muzzleOffsets = new[] { new Vector3(0.3f, 0.05f, 0.9f) },
                bodyColor = new Color(0.95f, 0.38f, 0.32f),
                look = new CharacterLook
                {
                    skin = new Color(1f, 0.82f, 0.68f), outfit = new Color(0.95f, 0.35f, 0.3f), pants = new Color(0.25f, 0.28f, 0.42f),
                    accent = new Color(0.98f, 0.85f, 0.3f), hair = new Color(0.62f, 0.25f, 0.65f), gear = new Color(0.4f, 0.26f, 0.18f),
                    headGear = HeadGear.Bandana, bodyScale = 1.02f, headScale = 1f, bulk = 1.08f,
                },
            };
            b.animations = new AnimationSet { attackKick = 2.0f, attackDuration = 0.45f, reloadDuration = 0.6f, superDuration = 0.6f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color, muzzleFlashScale = 1.8f };
            b.sfx = new SFXSet { primary = Sound.Shotgun, super = Sound.Shotgun, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 6f, superUseRange = 7f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Lince (id "sniper") — Sniper: 55 no corpo; na cabeça 80 a 1 m e mais quanto mais longe, até 150 a 60 m
        // (alcance 60 m); a cadência mais lenta. Vida mantém-se (85).
        // Especial: Tiro fatal — carrega com o tempo; armado, um headshot validado no host elimina de imediato.
        // =====================================================================================
        static BrawlerDefinition Sniper()
        {
            const string id = "sniper";
            var b = Definition(id);
            b.displayName = "Lince";
            b.shortName = "";
            b.role = "The Sharpshooter · Precise shots and a lethal charged shot.";
            b.stats = new BrawlerStats { maxHealth = 85, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.5f, sprintSpeed = 9.5f, jumpHeight = 1.35f };

            var main = Attack<HitscanDelivery>(id, "Primary", d =>
            {
                d.count = 1; d.interval = 0f; d.spreadDegrees = 0.05f; d.fanDegrees = 0f;
                d.range = 60f; d.radius = 0.04f; d.pierce = false;
            }, e =>
            {
                e.damage = 55; e.nearMultiplier = 1f; e.farMultiplier = 1f; e.canHeadshot = true; e.headshotDamage = 80; e.coverDamage = 15;
                e.headshotFarDamage = 150; e.headshotNearDistance = 1f; e.headshotFarDistance = 60f;
            }, v => { v.color = new Color(0.55f, 1f, 0.85f); v.scale = 1.2f; v.style = ProjectileStyle.Bullet; v.sound = Sound.Sniper; v.impactSound = Sound.Hit; });

            // Tiro fatal: o mesmo traço (respeita paredes e linha de visão, sem mira automática); headshot = eliminação
            var super = Attack<HitscanDelivery>(id, "Super", d =>
            {
                d.count = 1; d.interval = 0f; d.spreadDegrees = 0f; d.fanDegrees = 0f;
                d.range = 60f; d.radius = 0.02f; d.pierce = false;
            }, e =>
            {
                e.damage = 55; e.nearMultiplier = 1f; e.farMultiplier = 1f; e.canHeadshot = true; e.headshotDamage = 80; e.executeOnHeadshot = true; e.coverDamage = 30;
            }, v => { v.color = new Color(1f, 0.2f, 0.2f); v.scale = 1.6f; v.style = ProjectileStyle.Bullet; v.sound = Sound.FatalShot; v.impactSound = Sound.Hit; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 2.3f, cooldown = 1.1f };
            b.super = new SuperDefinition
            {
                attack = super, chargeRequired = 400f, passiveChargePerSecond = 1f / 30f, activation = SuperActivation.Mode,
                mode = new SuperModeSettings
                {
                    label = "Fatal Shot", duration = 0f, maxShots = 1, shotCooldown = 0.5f, activationDelay = 0.25f,
                    consumeOnFirstShot = true, cancellable = true,
                },
            };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.Sniper,
                muzzleOffsets = new[] { new Vector3(0.15f, -0.16f, 0.85f) },
                accentColor = new Color(0.45f, 1f, 0.8f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.Sniper,
                muzzleOffsets = new[] { new Vector3(0.3f, 0.05f, 1f) },
                bodyColor = new Color(0.45f, 0.82f, 0.55f),
                look = new CharacterLook
                {
                    skin = new Color(0.72f, 0.5f, 0.36f), outfit = new Color(0.4f, 0.62f, 0.35f), pants = new Color(0.32f, 0.3f, 0.22f),
                    accent = new Color(0.98f, 0.9f, 0.55f), hair = new Color(0.25f, 0.18f, 0.12f), gear = new Color(0.55f, 0.48f, 0.3f),
                    headGear = HeadGear.EarCap, bodyScale = 0.96f, headScale = 1f, bulk = 0.9f,
                },
            };
            b.animations = new AnimationSet { attackKick = 1.6f, attackDuration = 0.5f, reloadDuration = 0.5f, superDuration = 0.8f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color, muzzleFlashScale = 1.3f };
            b.sfx = new SFXSet { primary = Sound.Sniper, super = Sound.FatalShot, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 24f, superUseRange = 40f, aimAtFeet = false, superIsEscape = false };
            return Save(b);
        }

        // Ataque de teste (hitscan) — não pertence a nenhum Brawler; serve para validar o suporte a hitscan.
        static void TestHitscan()
        {
            var dPath = DataRoot + "/Test/Test_Hitscan_Delivery.asset";
            var d = LoadOrCreate<HitscanDelivery>(dPath);
            d.count = 3; d.interval = 0.08f; d.spreadDegrees = 0.5f; d.range = 40f; d.radius = 0.05f;
            EditorUtility.SetDirty(d);

            var a = LoadOrCreate<AttackDefinition>(DataRoot + "/Test/Test_Hitscan.asset");
            a.delivery = d;
            a.effect = new EffectPayload { damage = 10, coverDamage = 25 };
            a.visuals = new AttackVisuals { color = Color.cyan, scale = 1f, sound = Sound.Shot, impactSound = Sound.Hit };
            EditorUtility.SetDirty(a);
        }

        // =====================================================================================
        // Utilitários
        // =====================================================================================

        static BrawlerDefinition Definition(string id)
        {
            EnsureFolder(BrawlersRoot + "/" + id);
            var b = LoadOrCreate<BrawlerDefinition>(BrawlersRoot + "/" + id + "/" + id + ".asset");
            b.brawlerId = id;
            b.available = true;
            b.abilities.Clear();
            return b;
        }

        static AttackDefinition Attack<TDelivery>(string id, string slot, System.Action<TDelivery> delivery, System.Action<EffectPayload> effect, System.Action<AttackVisuals> visuals)
            where TDelivery : AttackDelivery
        {
            var folder = BrawlersRoot + "/" + id;
            var d = LoadOrCreate<TDelivery>(folder + "/" + id + "_" + slot + "_Delivery.asset");
            delivery(d);
            EditorUtility.SetDirty(d);

            var a = LoadOrCreate<AttackDefinition>(folder + "/" + id + "_" + slot + ".asset");
            a.delivery = d;
            a.effect = new EffectPayload();
            effect(a.effect);
            a.visuals = new AttackVisuals();
            visuals(a.visuals);
            EditorUtility.SetDirty(a);
            return a;
        }

        static BrawlerDefinition Save(BrawlerDefinition b)
        {
            EditorUtility.SetDirty(b);
            return b;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (existing is T typed) return typed;
            if (existing != null) AssetDatabase.DeleteAsset(path); // tipo mudou (ex.: outra delivery)
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
