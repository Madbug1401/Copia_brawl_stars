using UnityEngine;

namespace FPSLAN
{
    public enum AttackKind
    {
        Burst, // N projéteis em sequência (interval > 0) ou em leque simultâneo (interval == 0)
        Lob,   // projétil em arco que cai onde se aponta e explode
        Mines, // lança minas no chão à frente
        Aura,  // zona à volta do próprio jogador
    }

    /// <summary>Área persistente: dano por tick + abrandamento, ou mina que explode por proximidade.</summary>
    public class ZoneSpec
    {
        public float radius = 3f;
        public float duration = 4f;
        public float tickInterval = 0.5f;
        public int tickDamage = 5;
        public float slowFactor = 1f;     // 1 = sem abrandamento; 0.5 = metade da velocidade
        public float slowDuration = 0.6f;

        public bool isMine;
        public float armTime = 0.6f;
        public float triggerRadius = 1.6f;
        public int mineDamage = 35;
        public float mineExplosionRadius = 2.5f;

        public Color color = new Color(0.6f, 0.9f, 0.4f);
    }

    public class AttackSpec
    {
        public AttackKind kind = AttackKind.Burst;
        public int projectiles = 6;
        public float interval = 0.07f;
        public float spreadDegrees = 1.2f;
        public float fanDegrees = 0f;
        public float speed = 75f;
        public float range = 32f;
        public int damage = 9;
        public float radius = 0.12f;
        public bool pierce = false;
        public float visualScale = 1f;
        public Color color = new Color(1f, 0.85f, 0.35f);
        public Sound sound = Sound.Shot;

        // Explosão no impacto (Burst e Lob)
        public float explosionRadius = 0f;
        public int explosionDamage = 0;

        // Lob
        public float gravity = 22f;
        public float minFlightTime = 0.35f;
        public float maxFlightTime = 0.95f;
        public AttackSpec split;   // estilhaços ao explodir
        public int splitCount = 0;
        public ZoneSpec zone;      // zona criada ao explodir (Lob) / aura (Aura) / minas (Mines)

        // Mines
        public int mineCount = 3;
        public float mineSpread = 1.8f;
    }

    public class HeroDefinition
    {
        public int id;
        public string displayName;
        public string role;
        public bool available = true;

        public int maxHealth = 100;
        public float walkSpeed = 6.5f;
        public float sprintSpeed = 9.5f;
        public float jumpHeight = 1.3f;

        public int maxAmmo = 3;
        public float reloadTimePerAmmo = 1.3f;
        public float attackCooldown = 0.45f;
        public float superDamageRequired = 140f;

        public float regenDelay = 3f;
        public float regenPercentPerSecond = 0.15f;

        public float botPreferredRange = 14f;

        public Color bodyTint = Color.white;
        public AttackSpec main = new AttackSpec();
        public AttackSpec super = new AttackSpec();
    }

    /// <summary>
    /// [DEPRECADO — substituído por BrawlerDefinition/BrawlerRegistry na Fase 1. Mantido como referência até à validação;
    /// já não é lido pelo jogo. Só o ProjectileSystem legado ainda usa estes tipos.]
    /// Personagens originais inspirados em arquétipos de hero shooters arcade. Nomes provisórios — mudar aqui.
    /// Toda a afinação de jogo dos personagens está neste ficheiro.
    /// </summary>
    public static class HeroCatalog
    {
        public static readonly HeroDefinition[] All =
        {
            // ---------------------------------------------------------------- Dex — pistoleiro
            new HeroDefinition
            {
                id = 0,
                displayName = "Dex",
                role = "Pistoleiro — rajadas de longo alcance",
                maxHealth = 100,
                walkSpeed = 6.5f,
                sprintSpeed = 9.5f,
                maxAmmo = 3,
                reloadTimePerAmmo = 1.3f,
                attackCooldown = 0.5f,
                superDamageRequired = 140f,
                botPreferredRange = 16f,
                bodyTint = new Color(0.95f, 0.75f, 0.35f),
                main = new AttackSpec
                {
                    kind = AttackKind.Burst, projectiles = 6, interval = 0.065f, spreadDegrees = 1.0f,
                    speed = 80f, range = 32f, damage = 9, radius = 0.14f,
                    color = new Color(1f, 0.85f, 0.35f), sound = Sound.Shot,
                },
                super = new AttackSpec
                {
                    kind = AttackKind.Burst, projectiles = 12, interval = 0.045f, spreadDegrees = 1.6f,
                    speed = 95f, range = 45f, damage = 10, radius = 0.2f, pierce = true, visualScale = 1.6f,
                    color = new Color(1f, 0.35f, 0.25f), sound = Sound.SuperShot,
                },
            },

            // ---------------------------------------------------------------- Cardo — granadeiro
            new HeroDefinition
            {
                id = 1,
                displayName = "Cardo",
                role = "Granadeiro — granadas em arco que rebentam em estilhaços",
                maxHealth = 85,
                walkSpeed = 6.6f,
                sprintSpeed = 9.6f,
                maxAmmo = 3,
                reloadTimePerAmmo = 1.5f,
                attackCooldown = 0.6f,
                superDamageRequired = 120f,
                botPreferredRange = 13f,
                bodyTint = new Color(0.45f, 0.85f, 0.4f),
                main = new AttackSpec
                {
                    kind = AttackKind.Lob, projectiles = 1, speed = 20f, range = 22f, radius = 0.2f,
                    damage = 0, explosionRadius = 1.4f, explosionDamage = 16, visualScale = 2.2f,
                    gravity = 22f, minFlightTime = 0.35f, maxFlightTime = 0.9f,
                    color = new Color(0.5f, 0.95f, 0.4f), sound = Sound.Lob,
                    splitCount = 6,
                    split = new AttackSpec
                    {
                        kind = AttackKind.Burst, projectiles = 1, speed = 28f, range = 4.5f, damage = 8, radius = 0.14f,
                        color = new Color(0.75f, 1f, 0.5f), visualScale = 0.8f,
                    },
                },
                super = new AttackSpec
                {
                    kind = AttackKind.Lob, projectiles = 1, speed = 20f, range = 24f, radius = 0.25f,
                    damage = 0, explosionRadius = 2f, explosionDamage = 15, visualScale = 3.2f,
                    gravity = 22f, minFlightTime = 0.4f, maxFlightTime = 1.0f,
                    color = new Color(0.3f, 0.85f, 0.3f), sound = Sound.Lob,
                    zone = new ZoneSpec
                    {
                        radius = 3.6f, duration = 4.5f, tickInterval = 0.5f, tickDamage = 6,
                        slowFactor = 0.5f, slowDuration = 0.7f, color = new Color(0.3f, 0.75f, 0.25f),
                    },
                },
            },

            // ---------------------------------------------------------------- Vigia — arqueiro
            new HeroDefinition
            {
                id = 2,
                displayName = "Vigia",
                role = "Arqueiro — 3 flechas explosivas em leque; Super: minas",
                maxHealth = 90,
                walkSpeed = 6.7f,
                sprintSpeed = 9.7f,
                maxAmmo = 3,
                reloadTimePerAmmo = 1.4f,
                attackCooldown = 0.55f,
                superDamageRequired = 130f,
                botPreferredRange = 15f,
                bodyTint = new Color(0.4f, 0.7f, 1f),
                main = new AttackSpec
                {
                    kind = AttackKind.Burst, projectiles = 3, interval = 0f, spreadDegrees = 0f, fanDegrees = 12f,
                    speed = 48f, range = 26f, damage = 12, radius = 0.15f, visualScale = 1.2f,
                    explosionRadius = 1.3f, explosionDamage = 6,
                    color = new Color(0.45f, 0.75f, 1f), sound = Sound.Bow,
                },
                super = new AttackSpec
                {
                    kind = AttackKind.Mines, mineCount = 3, mineSpread = 2f, range = 12f,
                    color = new Color(0.35f, 0.6f, 1f), sound = Sound.Lob,
                    zone = new ZoneSpec
                    {
                        isMine = true, duration = 25f, armTime = 0.7f, triggerRadius = 1.7f,
                        mineDamage = 38, mineExplosionRadius = 2.6f, color = new Color(0.3f, 0.55f, 1f),
                    },
                },
            },

            // ---------------------------------------------------------------- Névoa — spray
            new HeroDefinition
            {
                id = 3,
                displayName = "Névoa",
                role = "Spray em cone que atravessa inimigos; Super: aura que abranda",
                maxHealth = 110,
                walkSpeed = 6.3f,
                sprintSpeed = 9.3f,
                maxAmmo = 3,
                reloadTimePerAmmo = 1.2f,
                attackCooldown = 0.5f,
                superDamageRequired = 110f,
                botPreferredRange = 6f,
                bodyTint = new Color(0.78f, 0.5f, 1f),
                main = new AttackSpec
                {
                    kind = AttackKind.Burst, projectiles = 5, interval = 0.03f, spreadDegrees = 0f, fanDegrees = 30f,
                    speed = 15f, range = 9.5f, damage = 8, radius = 0.55f, pierce = true, visualScale = 5f,
                    color = new Color(0.8f, 0.55f, 1f), sound = Sound.Spray,
                },
                super = new AttackSpec
                {
                    kind = AttackKind.Aura,
                    color = new Color(0.7f, 0.4f, 1f), sound = Sound.SuperShot,
                    zone = new ZoneSpec
                    {
                        radius = 4.5f, duration = 5f, tickInterval = 0.5f, tickDamage = 5,
                        slowFactor = 0.55f, slowDuration = 0.7f, color = new Color(0.65f, 0.4f, 0.95f),
                    },
                },
            },
        };

        public static HeroDefinition Get(int id)
        {
            if (id < 0 || id >= All.Length || !All[id].available) return All[0];
            return All[id];
        }

        public static int RandomAvailableId()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                var h = All[Random.Range(0, All.Length)];
                if (h.available) return h.id;
            }
            return 0;
        }
    }
}
