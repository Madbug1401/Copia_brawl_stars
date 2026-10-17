using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Coloca armadilhas (minas ou armadilhas de imobilização) à volta do ponto para onde se aponta.
    /// As posições são decididas e validadas no host (chão firme, espaço livre, dentro do mapa) e enviadas a todos,
    /// por isso as armadilhas são as mesmas em todas as máquinas.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Place Traps")]
    public class PlaceTrapsDelivery : AttackDelivery
    {
        [Tooltip("Número de armadilhas por utilização.")]
        public int count = 3;
        public float spread = 2f;
        public float range = 12f;
        public TrapSettings trap = new TrapSettings();

        public override float Range => range;
        public override bool UsesTargetPoint => true;

        /// <summary>Posição pretendida (antes da validação) da armadilha n.º index.</summary>
        public Vector3 DesiredPosition(AttackContext ctx, int index)
        {
            int n = Mathf.Max(1, count);
            if (n == 1) return ctx.target;
            float baseYaw = Mathf.Atan2(ctx.direction.x, ctx.direction.z);
            float ang = baseYaw + index * Mathf.PI * 2f / n;
            return ctx.target + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * spread;
        }

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            if (ctx.authoritative)
            {
                // Host: valida as posições e cria as armadilhas autoritativas (que avisam as outras máquinas)
                var feet = ctx.shooter != null ? ctx.shooter.transform.position : ctx.origin;
                for (int i = 0; i < Mathf.Max(1, count); i++)
                {
                    if (CombatSystem.TryFindTrapPosition(DesiredPosition(ctx, i), feet, trap, out var pos))
                        combat.ServerSpawnTrap(ctx, this, i, pos);
                }
            }
            else
            {
                // Visual imediato no sítio pretendido; as armadilhas aparecem quando o host confirmar as posições
                combat.TrapPlacementFeedback(ctx, trap);
            }
        }
    }
}
