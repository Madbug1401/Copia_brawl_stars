using UnityEngine;

namespace FPSLAN
{
    /// <summary>Projéteis em linha reta (rajadas sequenciais ou leque simultâneo), com queda opcional (flechas).</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Projectile")]
    public class ProjectileDelivery : AttackDelivery
    {
        public int count = 1;
        [Tooltip("Segundos entre projéteis. 0 = todos ao mesmo tempo.")]
        public float interval = 0.07f;
        public float spreadDegrees = 1f;
        public float fanDegrees = 0f;
        public float speed = 80f;
        public float range = 30f;
        public float radius = 0.14f;
        [Tooltip("Atravessa jogadores.")]
        public bool pierce = false;
        [Tooltip("Gravidade aplicada ao projétil (m/s²). 0 = linha reta. Dá uma trajetória visível (flechas).")]
        public float gravity = 0f;

        public override float Range => range;
        public override int ShotCount => Mathf.Max(1, count);
        public override float ShotInterval => interval;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            var dir = SpreadDirection(ctx, shotIndex, ShotCount, spreadDegrees, fanDegrees);
            combat.LaunchProjectile(ctx, dir, speed, range, radius, pierce, shotIndex, gravity);
        }
    }
}
