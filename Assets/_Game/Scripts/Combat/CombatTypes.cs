using System;
using System.Collections.Generic;
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
        [Tooltip("Dano fixo na cabeça (antes do multiplicador de distância). 0 = usa damage × headshotMultiplier.")]
        public int headshotDamage = 0;
        [Tooltip("Acerto válido na cabeça elimina de imediato (validado no host). No corpo causa o dano normal.")]
        public bool headshotExecutes = false;

        [Header("Limite por alvo")]
        [Tooltip("Dano máximo que UM ataque (todos os projéteis/ticks/explosões) pode causar a cada alvo. 0 = sem limite.")]
        public int maxTotalDamagePerTarget = 0;

        [Header("Splash (explosão no impacto)")]
        public float splashRadius = 0f;
        public int splashDamage = 0;
        [Tooltip("Multiplicador do dano de splash na borda da explosão (1 = sem redução; 0.35 = 35% na borda).")]
        public float splashEdgeMultiplier = 1f;

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

    /// <summary>Como zonas iguais se somam no mesmo alvo.</summary>
    public enum ZoneStacking
    {
        /// <summary>Cada zona aplica os seus ticks (comportamento original).</summary>
        Stack,
        /// <summary>Várias zonas do mesmo atacante sobrepostas contam como uma só (no máximo um tick por intervalo).</summary>
        OnePerAttacker,
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
        [Tooltip("Cor secundária (borda/partículas). Alfa 0 = usa a cor principal.")]
        public Color secondaryColor = new Color(0f, 0f, 0f, 0f);
        public ZoneStacking stacking = ZoneStacking.Stack;
    }

    /// <summary>O que acontece quando uma armadilha dispara.</summary>
    public enum TrapEffect
    {
        /// <summary>Mina: aviso curto e explosão com o EffectPayload do ataque.</summary>
        Explode,
        /// <summary>Armadilha de imobilização: prende o alvo no lugar e aplica veneno.</summary>
        Snare,
    }

    /// <summary>Armadilha (mina/laço): arma, deteta um inimigo e dispara. Resolvida no host; os clientes recebem os eventos.</summary>
    [Serializable]
    public class TrapSettings
    {
        public TrapEffect effect = TrapEffect.Explode;
        public float armTime = 0.8f;
        public float triggerRadius = 1.8f;
        public float warningTime = 0.35f;
        public float lifetime = 30f;
        [Tooltip("Os inimigos veem a mina muito discreta.")]
        public bool hiddenFromEnemies = true;
        public Color color = new Color(0.3f, 0.55f, 1f);

        [Header("Regras")]
        [Tooltip("Máximo de armadilhas ativas deste tipo por jogador (as mais antigas desaparecem). 0 = sem limite.")]
        public int maxActivePerOwner = 0;
        [Tooltip("Explosões inimigas no raio destroem a armadilha.")]
        public bool destroyedByEnemyExplosions = true;
        [Tooltip("Uma facada inimiga perto desativa a armadilha.")]
        public bool defusedByEnemyMelee = true;

        [Header("Imobilização (Snare)")]
        public float rootDuration = 5f;
        [Tooltip("Dano total de veneno ao longo da imobilização.")]
        public int poisonTotalDamage = 40;
        [Tooltip("Segundos de imunidade depois de ser solto (impede imobilização em cadeia).")]
        public float rootImmunityAfter = 2.5f;
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
        /// <summary>Multiplicador de dano do atacante (melhorias de progressão; 1 em partidas competitivas).</summary>
        public float damageMultiplier = 1f;

        Dictionary<ulong, int> m_DamageDealt;

        /// <summary>
        /// Host: limita o dano deste ataque a cada alvo (EffectPayload.maxTotalDamagePerTarget). Garante, por exemplo,
        /// que as três flechas somam no máximo 100 e que partículas/colisores repetidos não multiplicam o dano.
        /// </summary>
        public int ClampToBudget(ulong victimId, int amount)
        {
            int cap = attack != null && attack.effect != null ? attack.effect.maxTotalDamagePerTarget : 0;
            if (cap <= 0 || amount <= 0) return amount;
            if (m_DamageDealt == null) m_DamageDealt = new Dictionary<ulong, int>();
            m_DamageDealt.TryGetValue(victimId, out int dealt);
            int allowed = Mathf.Clamp(cap - dealt, 0, amount);
            m_DamageDealt[victimId] = dealt + allowed;
            return allowed;
        }

        public int DamageDealtTo(ulong victimId) =>
            m_DamageDealt != null && m_DamageDealt.TryGetValue(victimId, out int d) ? d : 0;

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
