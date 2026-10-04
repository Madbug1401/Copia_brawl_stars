using UnityEngine;

namespace FPSLAN
{
    /// <summary>Tiro instantâneo (raio). Suporta rajadas, spread e leque.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Hitscan")]
    public class HitscanDelivery : AttackDelivery
    {
        public int count = 1;
        public float interval = 0.1f;
        public float spreadDegrees = 0.5f;
        public float fanDegrees = 0f;
        public float range = 40f;
        public float radius = 0.05f;
        public bool pierce = false;

        public override float Range => range;
        public override int ShotCount => Mathf.Max(1, count);
        public override float ShotInterval => interval;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            var dir = SpreadDirection(ctx, shotIndex, ShotCount, spreadDegrees, fanDegrees);
            combat.TraceHitscan(ctx, dir, range, radius, pierce, shotIndex);
        }
    }
}
