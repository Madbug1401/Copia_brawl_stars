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
                    "Gerar", "Cancelar"))
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
            EditorUtility.SetDirty(registry);

            TestHitscan();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FPS-LAN] Dados dos Brawlers gerados em " + BrawlersRoot + " e registry em " + RegistryPath);
        }

        // =====================================================================================
        // Pistoleiro — referência de design: Colt. Damage dealer, mira/precisão, rajada de 6.
        // =====================================================================================
        static BrawlerDefinition Gunslinger()
        {
            const string id = "gunslinger";
            var b = Definition(id);
            b.displayName = "Dex";
            b.role = "Pistoleiro — rajada de 6 tiros de longo alcance; Super: tempestade de balas que destrói cobertura";
            b.stats = new BrawlerStats { maxHealth = 100, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            var main = Attack<ProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 6; d.interval = 0.065f; d.spreadDegrees = 0.8f; d.fanDegrees = 0f;
                d.speed = 85f; d.range = 30f; d.radius = 0.13f; d.pierce = false;
            }, e =>
            {
                e.damage = 18; e.nearMultiplier = 1f; e.farMultiplier = 1f; e.canHeadshot = true; e.headshotMultiplier = 1.5f;
            }, v => { v.color = new Color(1f, 0.85f, 0.35f); v.scale = 1f; v.sound = Sound.Shot; v.impactSound = Sound.Hit; });

            var super = Attack<ProjectileDelivery>(id, "Super", d =>
            {
                d.count = 12; d.interval = 0.045f; d.spreadDegrees = 1.4f;
                d.speed = 100f; d.range = 40f; d.radius = 0.2f; d.pierce = true;
            }, e =>
            {
                e.damage = 18; e.canHeadshot = true; e.headshotMultiplier = 1.25f; e.coverDamage = 60;
            }, v => { v.color = new Color(1f, 0.35f, 0.25f); v.scale = 1.6f; v.sound = Sound.SuperShot; v.impactSound = Sound.Hit; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.3f, cooldown = 0.5f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 216f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.DualRevolvers,
                muzzleOffsets = new[] { new Vector3(0.2f, -0.13f, 0.5f), new Vector3(-0.2f, -0.13f, 0.5f) },
                accentColor = new Color(0.95f, 0.75f, 0.35f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.DualRevolvers,
                muzzleOffsets = new[] { new Vector3(0.32f, 0.02f, 0.7f), new Vector3(-0.32f, 0.02f, 0.7f) },
                bodyColor = new Color(0.95f, 0.75f, 0.35f),
            };
            b.animations = new AnimationSet { attackKick = 0.8f, attackDuration = 0.4f, reloadDuration = 0.35f, superDuration = 0.6f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color };
            b.sfx = new SFXSet { primary = Sound.Shot, super = Sound.SuperShot, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 18f, superUseRange = 30f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Artilheiro — referência de design: Dynamike. Duas dinamites em arco, atraso, área.
        // =====================================================================================
        static BrawlerDefinition Artillery()
        {
            const string id = "artillery";
            var b = Definition(id);
            b.displayName = "Cardo";
            b.role = "Artilheiro — duas cargas em arco que explodem após um atraso; Super: barril com knockback";
            b.stats = new BrawlerStats { maxHealth = 110, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            var main = Attack<ArcProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 2; d.interval = 0.08f; d.landingSpread = 1.4f;
                d.speed = 18f; d.range = 18f; d.gravity = 22f; d.minFlightTime = 0.45f; d.maxFlightTime = 1.0f;
                d.radius = 0.2f; d.fuseDelay = 0.5f;
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.splashRadius = 2.2f; e.splashDamage = 45; e.coverDamage = 35;
            }, v => { v.color = new Color(1f, 0.45f, 0.2f); v.scale = 1.8f; v.sound = Sound.Lob; v.impactSound = Sound.Explosion; });

            var super = Attack<ArcProjectileDelivery>(id, "Super", d =>
            {
                d.count = 1; d.speed = 16f; d.range = 20f; d.gravity = 22f; d.minFlightTime = 0.55f; d.maxFlightTime = 1.1f;
                d.radius = 0.35f; d.fuseDelay = 0.7f;
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.splashRadius = 4.5f; e.splashDamage = 110; e.knockback = 12f; e.knockbackUp = 6f; e.coverDamage = 200;
            }, v => { v.color = new Color(1f, 0.25f, 0.15f); v.scale = 3.4f; v.sound = Sound.Lob; v.impactSound = Sound.Explosion; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.4f, cooldown = 0.6f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 180f };

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
            };
            b.animations = new AnimationSet { attackKick = 0.6f, attackDuration = 0.5f, reloadDuration = 0.4f, superDuration = 0.7f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color, impactScale = 1.4f };
            b.sfx = new SFXSet { primary = Sound.Lob, super = Sound.Lob, impact = Sound.Explosion };
            b.bot = new BotProfile { preferredRange = 13f, superUseRange = 16f, aimAtFeet = true };
            return Save(b);
        }

        // =====================================================================================
        // Armadilheiro — referência de design: Bo. Três flechas + minas, controlo de território.
        // =====================================================================================
        static BrawlerDefinition Trapper()
        {
            const string id = "trapper";
            var b = Definition(id);
            b.displayName = "Vigia";
            b.role = "Armadilheiro — três flechas explosivas em leque; Super: três minas escondidas";
            b.stats = new BrawlerStats { maxHealth = 125, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.6f, sprintSpeed = 9.6f, jumpHeight = 1.3f };

            var main = Attack<ProjectileDelivery>(id, "Primary", d =>
            {
                d.count = 3; d.interval = 0f; d.spreadDegrees = 0f; d.fanDegrees = 12f;
                d.speed = 50f; d.range = 24f; d.radius = 0.15f; d.pierce = false;
            }, e =>
            {
                e.damage = 30; e.canHeadshot = true; e.headshotMultiplier = 1.25f;
                e.splashRadius = 1.2f; e.splashDamage = 8; e.coverDamage = 10;
            }, v => { v.color = new Color(0.45f, 0.75f, 1f); v.scale = 1.2f; v.sound = Sound.Bow; v.impactSound = Sound.Explosion; });

            var super = Attack<PlaceTrapsDelivery>(id, "Super", d =>
            {
                d.count = 3; d.spread = 1.5f; d.range = 12f;
                d.trap = new TrapSettings { armTime = 0.8f, triggerRadius = 1.8f, warningTime = 0.35f, lifetime = 30f, hiddenFromEnemies = true, color = new Color(0.3f, 0.55f, 1f) };
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
                e.splashRadius = 2.8f; e.splashDamage = 60; e.knockback = 5f; e.knockbackUp = 3f; e.coverDamage = 40;
            }, v => { v.color = new Color(0.35f, 0.6f, 1f); v.scale = 1f; v.sound = Sound.Lob; v.impactSound = Sound.Explosion; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 1.7f, cooldown = 0.55f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 180f };

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
            };
            b.animations = new AnimationSet { attackKick = 0.7f, attackDuration = 0.45f, reloadDuration = 0.45f, superDuration = 0.6f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color };
            b.sfx = new SFXSet { primary = Sound.Bow, super = Sound.Lob, impact = Sound.Explosion };
            b.bot = new BotProfile { preferredRange = 15f, superUseRange = 10f, aimAtFeet = false };
            return Save(b);
        }

        // =====================================================================================
        // Spray — referência de design: Emz. Cone persistente em ticks, dano por distância; aura de slow.
        // =====================================================================================
        static BrawlerDefinition Sprayer()
        {
            const string id = "sprayer";
            var b = Definition(id);
            b.displayName = "Névoa";
            b.role = "Spray — nuvem em cone que causa mais dano à distância; Super: aura que abranda e causa dano";
            b.stats = new BrawlerStats { maxHealth = 120, regenDelay = 3f, regenPercentPerSecond = 0.15f };
            b.movement = new MovementProfile { walkSpeed = 6.4f, sprintSpeed = 9.4f, jumpHeight = 1.3f };

            var main = Attack<ConeVolumeDelivery>(id, "Primary", d =>
            {
                d.range = 10f; d.angle = 32f; d.duration = 0.6f; d.tickInterval = 0.2f; d.expandTime = 0.25f;
            }, e =>
            {
                // Mais dano longe do que perto: recompensa manter distância (controlo de espaço)
                e.damage = 12; e.nearMultiplier = 0.6f; e.farMultiplier = 1.3f; e.canHeadshot = false;
            }, v => { v.color = new Color(0.8f, 0.55f, 1f); v.scale = 1f; v.sound = Sound.Spray; v.impactSound = Sound.Hit; });

            var super = Attack<SelfAuraDelivery>(id, "Super", d =>
            {
                d.zone = new ZoneSettings { enabled = true, radius = 5f, duration = 5f, tickInterval = 0.5f, tickDamage = 8, slowFactor = 0.55f, slowDuration = 0.7f, color = new Color(0.65f, 0.4f, 0.95f) };
            }, e =>
            {
                e.damage = 0; e.canHeadshot = false;
            }, v => { v.color = new Color(0.7f, 0.4f, 1f); v.scale = 1f; v.sound = Sound.SuperShot; v.impactSound = Sound.Hit; });

            b.primary = new PrimaryAttackDefinition { attack = main, maxCharges = 3, reloadTimePerCharge = 2.0f, cooldown = 0.65f };
            b.super = new SuperDefinition { attack = super, chargeRequired = 150f };

            b.firstPerson = new FirstPersonPresentation
            {
                rig = RigStyle.SprayCan,
                muzzleOffsets = new[] { new Vector3(0.18f, -0.1f, 0.45f) },
                accentColor = new Color(0.78f, 0.5f, 1f),
            };
            b.thirdPerson = new ThirdPersonPresentation
            {
                rig = RigStyle.SprayCan,
                muzzleOffsets = new[] { new Vector3(0.3f, 0.05f, 0.55f) },
                bodyColor = new Color(0.78f, 0.5f, 1f),
            };
            b.animations = new AnimationSet { attackKick = 0.3f, attackDuration = 0.6f, reloadDuration = 0.5f, superDuration = 0.8f };
            b.vfx = new VFXSet { primaryColor = main.visuals.color, superColor = super.visuals.color };
            b.sfx = new SFXSet { primary = Sound.Spray, super = Sound.SuperShot, impact = Sound.Hit };
            b.bot = new BotProfile { preferredRange = 7f, superUseRange = 4.5f, aimAtFeet = false };
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
