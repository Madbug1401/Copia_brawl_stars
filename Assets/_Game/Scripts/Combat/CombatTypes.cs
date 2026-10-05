using System;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>O que acontece a quem é atingido. Partilhado por todas as formas de entrega.</summary>
    [Serializable]
    public class EffectPayload
    {
        [Header("Dano direto")]
        public int damage = 10;
        [Tooltip("Multiplicador de dano perto (0) e longe (alcance máximo).")]
        public float nearMultiplier = 1f;
        public float farMultiplier = 1f;
        public bool canHeadshot = true;
        public float headshotMultiplier = 1.5f;

        [Header("Splash (explosão no impacto)")]
        public float splashRadius = 0f;
        public int splashDamage = 0;

        [Header("Estados")]
        [Tooltip("1 = sem abrandamento; 0.5 = metade da velocidade.")]
        public float slowFactor = 1f;
        public float slowDuration = 0f;
        public float dotDamagePerSecond = 0f;
        public float dotDuration = 0f;

        [Header("Knockback")]
        public float knockback = 0f;
        public float knockbackUp = 0f;

        [Header("Cobertura")]
        public int coverDamage = 0;

        [Header("Zona criada no impacto")]
        public ZoneSettings zoneOnImpact = new ZoneSettings();

        public float DistanceMultiplier(float distance, float range)
        {
            if (range <= 0.01f) return nearMultiplier;
            return Mathf.Lerp(nearMultiplier, farMultiplier, Mathf.Clamp01(distance / range));
        }

        public bool HasSplash => splashRadius > 0.01f && (splashDamage > 0 || knockback > 0f || coverDamage > 0 || slowDuration > 0f);
    }

    /// <summary>Área persistente: dano por tick + abrandamento.</summary>
    [Serializable]
    public class ZoneSettings
    {
        public bool enabled;
        public float radius = 3f;
        public float duration = 4f;
        public float tickInterval = 0.5f;
        public int tickDamage = 5;
        public float slowFactor = 1f;
        public float slowDuration = 0.6f;
        public Color color = new Color(0.6f, 0.9f, 0.4f);
    }

    /// <summary>Armadilha (mina): arma, deteta um inimigo, avisa e explode com o EffectPayload do ataque.</summary>
    [Serializable]
    public class TrapSettings
    {
        public float armTime = 0.8f;
        public float triggerRadius = 1.8f;
        public float warningTime = 0.35f;
        public float lifetime = 30f;
        [Tooltip("Os inimigos veem a mina muito discreta.")]
        public bool hiddenFromEnemies = true;
        public Color color = new Color(0.3f, 0.55f, 1f);
    }

    /// <summary>Aspeto do projétil em voo (Auto = escolhido pela forma de entrega).</summary>
    public enum ProjectileStyle
    {
        Auto,
        Bullet,
        Pellet,
        Arrow,
        Dynamite,
        Barrel,
        Orb,
        Bomb,
    }

    [Serializable]
    public class AttackVisuals
    {
        public Color color = new Color(1f, 0.85f, 0.35f);
        public float scale = 1f;
        public ProjectileStyle style = ProjectileStyle.Auto;
        public Sound sound = Sound.Shot;
        public Sound impactSound = Sound.Explosion;
    }

    /// <summary>
    /// Mensagem/estado de um ataque: a intenção (origem, direção, ponto-alvo, seed) é a mesma em todas as máquinas,
    /// por isso os visuais coincidem com o que o host resolve.
    /// </summary>
    public class AttackContext
    {
        public PlayerNetwork shooter;
        public ulong attackerId;
        public AttackDefinition attack;
        public bool isSuper;
        public bool authoritative;
        public Vector3 origin;
        public Vector3 direction;
        public Vector3 target;
        public int seed;
        /// <summary>Aspeto dos projéteis lançados por este contexto (ex.: bombas do salto). Auto = o do ataque.</summary>
        public ProjectileStyle styleOverride = ProjectileStyle.Auto;

        public EffectPayload Effect => attack.effect;
        public float Range => attack.Range;

        /// <summary>Gerador determinístico por disparo (igual em todas as máquinas).</summary>
        public System.Random RngFor(int shotIndex) => new System.Random(unchecked(seed * 486187739 + shotIndex * 16777619 + 7));

        public Vector3 Right
        {
            get
            {
                var r = Vector3.Cross(Vector3.up, direction);
                return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
            }
        }
    }
}
