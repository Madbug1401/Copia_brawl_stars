using UnityEngine;

namespace FPSLAN
{
    /// <summary>Coloca armadilhas (minas) à volta do ponto para onde se aponta.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Place Traps")]
    public class PlaceTrapsDelivery : AttackDelivery
    {
        public int count = 3;
        public float spread = 2f;
        public float range = 12f;
        public TrapSettings trap = new TrapSettings();

        public override float Range => range;
        public override bool UsesTargetPoint => true;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            float baseYaw = Mathf.Atan2(ctx.direction.x, ctx.direction.z);
            for (int i = 0; i < count; i++)
            {
                float ang = baseYaw + i * Mathf.PI * 2f / Mathf.Max(1, count);
                var offset = count > 1 ? new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * spread : Vector3.zero;
                combat.SpawnTrap(ctx, ctx.target + offset, trap);
            }
        }
    }
}
