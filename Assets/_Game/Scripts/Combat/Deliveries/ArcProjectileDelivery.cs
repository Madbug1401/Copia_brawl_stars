using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Projéteis em arco que caem no ponto para onde se aponta (passam por cima de cobertura e de jogadores),
    /// e explodem ao aterrar ou depois de um atraso (fuse).
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Arc Projectile")]
    public class ArcProjectileDelivery : AttackDelivery
    {
        public int count = 1;
        public float interval = 0.08f;
        [Tooltip("Distância lateral entre os pontos de aterragem quando count > 1.")]
        public float landingSpread = 1.4f;
        [Tooltip("Velocidade horizontal de referência (define o tempo de voo).")]
        public float speed = 18f;
        public float range = 18f;
        public float gravity = 22f;
        public float minFlightTime = 0.45f;
        public float maxFlightTime = 1.0f;
        public float radius = 0.2f;
        [Tooltip("Segundos no chão antes de explodir. 0 = explode ao aterrar.")]
        public float fuseDelay = 0.5f;

        public override float Range => range;
        public override int ShotCount => Mathf.Max(1, count);
        public override float ShotInterval => interval;
        public override bool UsesTargetPoint => true;

        public Vector3 TargetFor(AttackContext ctx, int shotIndex)
        {
            float offset = (shotIndex - (ShotCount - 1) * 0.5f) * landingSpread;
            return ctx.target + ctx.Right * offset;
        }

        public Vector3 Velocity(Vector3 from, Vector3 to, out float flightTime)
        {
            return CombatMath.ArcVelocity(from, to, gravity, speed, minFlightTime, maxFlightTime, out flightTime);
        }

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            combat.LaunchArc(ctx, this, TargetFor(ctx, shotIndex), shotIndex);
        }
    }
}
