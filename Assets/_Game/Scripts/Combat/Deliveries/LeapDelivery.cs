using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Salto de fuga: o jogador é lançado em arco para o ponto para onde aponta e deixa bombas no sítio de onde saiu.
    /// O salto é movimento (decidido por quem controla o jogador, como o resto do movimento); as bombas são resolvidas
    /// no host como qualquer outro ataque.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Leap")]
    public class LeapDelivery : AttackDelivery
    {
        [Tooltip("Distância máxima do salto.")]
        public float range = 13f;
        [Tooltip("Duração do voo (s).")]
        public float flightTime = 0.8f;
        [Header("Bombas deixadas para trás")]
        public int bombCount = 4;
        public float bombRing = 1.4f;
        public float bombGravity = 24f;
        public float bombFuse = 0.45f;
        public float bombRadius = 0.2f;

        public override float Range => range;
        public override bool UsesTargetPoint => true;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            ctx.styleOverride = ProjectileStyle.Bomb;
            var ground = CombatMath.GroundBelow(ctx.origin, ctx.shooter);
            var start = ctx.origin + Vector3.down * 0.4f;
            float baseYaw = Mathf.Atan2(ctx.direction.x, ctx.direction.z);
            int n = Mathf.Max(1, bombCount);
            for (int i = 0; i < n; i++)
            {
                float a = baseYaw + (i + 0.5f) * Mathf.PI * 2f / n;
                var target = ground + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (n > 1 ? bombRing : 0f);
                combat.LaunchArcFrom(ctx, start, target, bombGravity, 5f, 0.35f, 0.55f, bombRadius, bombFuse);
            }
        }

        public override void OnOwnerFire(PlayerNetwork shooter, AttackContext ctx)
        {
            if (shooter == null || shooter.Motor == null) return;
            var target = MapInfo.ClampInside(ctx.target);
            var flat = target - shooter.transform.position;
            flat.y = 0f;
            if (flat.magnitude > range) target = shooter.transform.position + flat.normalized * range + Vector3.up * (target.y - shooter.transform.position.y);
            shooter.Motor.Launch(target, flightTime);
        }
    }
}
