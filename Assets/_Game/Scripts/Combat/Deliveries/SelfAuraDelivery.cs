using UnityEngine;

namespace FPSLAN
{
    /// <summary>Zona à volta do próprio jogador que o acompanha enquanto ativa.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Self Aura")]
    public class SelfAuraDelivery : AttackDelivery
    {
        public ZoneSettings zone = new ZoneSettings { enabled = true, radius = 5f, duration = 5f, tickInterval = 0.5f, tickDamage = 8, slowFactor = 0.55f, slowDuration = 0.7f };

        public override float Range => zone.radius;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            combat.SpawnZone(ctx, ctx.shooter != null ? ctx.shooter.transform.position : ctx.origin, zone, ctx.shooter);
        }
    }
}
