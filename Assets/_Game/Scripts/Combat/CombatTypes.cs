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
        [Tooltip("Fração do alcance com o multiplicador 'perto'; daí até ao alcance máximo desce até 'longe' (0 = desce desde o início).")]
        [Range(0f, 0.95f)] public float falloffStartFraction = 0f;
        public bool canHeadshot = true;
        public float headshotMultiplier = 1.5f;
        [Tooltip("Dano fixo de um headshot (antes da distância). 0 = damage × headshotMultiplier.")]
        public int headshotDamage = 0;
        [Tooltip("Dano por índice de disparo numa rajada (vazio = 'damage' para todos). Garante totais exatos (ex.: 33+34+33 = 100).")]
        public int[] perShotDamage = new int[0];
        [Tooltip("Um headshot válido elimina de imediato (tiro fatal). No corpo aplica o dano normal.")]
        public bool executeOnHeadshot = false;

        [Header("Splash (explosão no impacto)")]
        public float splashRadius = 0f;
        public int splashDamage = 0;
        [Tooltip("Fração do raio com dano total; daí até à borda o dano desce até splashEdgeMultiplier.")]
        [Range(0f, 1f)] public float splashFullRadiusFraction = 1f;
        [Tooltip("Multiplicador do dano de splash na borda do raio (1 = sem redução).")]
        [Range(0f, 1f)] public float splashEdgeMultiplier = 1f;

        [Header("Estados")]
        [Tooltip("1 = sem abrandamento; 0.5 = metade da velocidade.")]
        public float slowFactor = 1f;
        public float slowDuration = 0f;
        public float dotDamagePerSecond = 0f;
        public float dotDuration = 0f;
        [Tooltip("Imobiliza o alvo (não anda nem salta; pode olhar e atacar).")]
        public float rootDuration = 0f;
        [Tooltip("Depois de uma imobilização, o alvo fica imune a novas durante este tempo (evita cadeias).")]
        public float rootImmunity = 2.5f;

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
            float start = range * Mathf.Clamp(falloffStartFraction, 0f, 0.95f);
            return Mathf.Lerp(nearMultiplier, farMultiplier, Mathf.Clamp01((distance - start) / Mathf.Max(0.01f, range - start)));
        }

        /// <summary>Dano base do disparo n.º shotIndex de uma rajada (antes da distância).</summary>
        public int BaseDamageForShot(int shotIndex)
        {
            if (perShotDamage != null && shotIndex >= 0 && shotIndex < perShotDamage.Length) return Mathf.Max(0, perShotDamage[shotIndex]);
            return damage;
        }

        /// <summary>Dano de um impacto direto (distância percorrida, headshot e índice do disparo).</summary>
        public int DirectDamage(int shotIndex, float distance, float range, bool headshot)
        {
            float dm = DistanceMultiplier(distance, range);
            if (headshot && canHeadshot)
            {
                if (headshotDamage > 0) return Mathf.RoundToInt(headshotDamage * dm);
                return Mathf.RoundToInt(BaseDamageForShot(shotIndex) * dm * headshotMultiplier);
            }
            return Mathf.RoundToInt(BaseDamageForShot(shotIndex) * dm);
        }

        /// <summary>Multiplicador do splash a 'distance' metros do centro da explosão (1 no centro).</summary>
        public float SplashMultiplier(float distance)
        {
            if (splashRadius <= 0.01f) return 0f;
            float full = splashRadius * Mathf.Clamp01(splashFullRadiusFraction);
            if (distance <= full) return 1f;
            float t = Mathf.Clamp01(Mathf.InverseLerp(full, splashRadius, distance));
            return Mathf.Lerp(1f, Mathf.Clamp01(splashEdgeMultiplier), t);
        }

        public int SplashDamageAt(float distance) => Mathf.RoundToInt(splashDamage * SplashMultiplier(distance));

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
        [Tooltip("Cor das partículas dentro da zona (ex.: bolhas roxas no veneno verde).")]
        public Color secondaryColor = new Color(0.75f, 0.45f, 1f);
        [Tooltip("Zonas do mesmo atacante não acumulam: no máximo um tick por intervalo, mesmo com várias sobrepostas.")]
        public bool nonStackingPerAttacker = true;
    }

    public enum TrapKind
    {
        /// <summary>Mina: deteta, avisa e explode com o EffectPayload do ataque.</summary>
        Explosive,
        /// <summary>Armadilha de imobilização: prende quem a pisa e aplica os estados do EffectPayload.</summary>
        Snare,
    }

    /// <summary>Armadilha colocada no chão (mina ou armadilha de imobilização). Decidida no host.</summary>
    [Serializable]
    public class TrapSettings
    {
        public TrapKind kind = TrapKind.Explosive;
        public float armTime = 0.8f;
        public float triggerRadius = 1.8f;
        public float warningTime = 0.35f;
        public float lifetime = 30f;
        [Tooltip("Os inimigos veem a armadilha muito discreta.")]
        public bool hiddenFromEnemies = true;
        public Color color = new Color(0.3f, 0.55f, 1f);

        [Header("Regras")]
        [Tooltip("Explosões inimigas destroem a armadilha (sem a detonar).")]
        public bool destroyedByEnemyExplosions = true;
        [Tooltip("A facada inimiga destrói a armadilha.")]
        public bool destroyedByEnemyMelee = true;
        [Tooltip("As armadilhas desaparecem quando o dono é eliminado.")]
        public bool removeOnOwnerDeath = false;
        [Tooltip("Máximo de armadilhas ativas do mesmo dono (as mais antigas desaparecem).")]
        [Min(1)] public int maxActivePerOwner = 10;
        [Tooltip("Espaço livre exigido à volta da armadilha (não pode ficar dentro de paredes, água ou fora do mapa).")]
        public float placementClearance = 0.3f;
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
        // Acrescentados no fim (valores serializados estáveis)
        Tnt,
        Grenade,
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
        /// <summary>Host: segundos a recuar os outros jogadores ao testar este ataque (compensação de latência).</summary>
        public double lag;
        public Vector3 origin;
        public Vector3 direction;
        public Vector3 target;
        public int seed;
        /// <summary>Aspeto dos projéteis lançados por este contexto (ex.: bombas do salto). Auto = o do ataque.</summary>
        public ProjectileStyle styleOverride = ProjectileStyle.Auto;
        /// <summary>Cor dos efeitos deste ataque definida pela skin do atacante (só visual).</summary>
        public bool hasColorOverride;
        public Color colorOverride = Color.white;

        public EffectPayload Effect => attack.effect;
        public float Range => attack.Range;
        /// <summary>Cor usada pelos efeitos visuais (skin do atacante ou cor do ataque).</summary>
        public Color VisualColor => hasColorOverride ? colorOverride : attack.visuals.color;

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
