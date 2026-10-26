using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Caçadeira "pump": vários bagos hitscan num cone circular, todos no mesmo instante.
    /// O dano de cada bago segue a curva de queda do ataque (EffectPayload.falloffCurve) e um jogador só sofre dano
    /// se for atingido por pelo menos minPelletsToDamage bagos do mesmo disparo. A soma é aplicada de uma vez no host.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Pump Shotgun")]
    public class PumpShotgunDelivery : AttackDelivery
    {
        [Min(1)] public int pellets = 10;
        [Tooltip("Abertura do cone (graus, do centro à borda).")]
        public float spreadDegrees = 5.5f;
        [Tooltip("Variação aleatória de cada bago (graus), igual em todas as máquinas (seed).")]
        public float jitterDegrees = 0.8f;
        [Tooltip("Alcance máximo (depois disto o bago não faz nada).")]
        public float range = 31f;
        [Tooltip("Distância até onde a caçadeira ainda é útil (UI e bots).")]
        public float effectiveRange = 18f;
        public float radius = 0.05f;
        [Tooltip("Bagos mínimos no mesmo jogador para haver dano.")]
        [Min(1)] public int minPelletsToDamage = 3;

        public override float Range => range;
        public override float EffectiveRange => Mathf.Min(effectiveRange, range);
        public override int ShotCount => Mathf.Max(1, pellets);
        public override float ShotInterval => 0f;

        /// <summary>Direção do bago n.º index: 1 no centro, depois anéis (1/3 interior, resto exterior), rodados pela seed.</summary>
        public Vector3 PelletDirection(AttackContext ctx, int index)
        {
            int n = ShotCount;
            var rng = ctx.RngFor(1000);
            float rot = (float)rng.NextDouble() * 360f;
            float r, ang;
            if (index == 0) { r = 0f; ang = 0f; }
            else
            {
                int rest = n - 1;
                int inner = Mathf.Max(1, rest / 3);
                if (index <= inner) { r = 0.5f; ang = rot + (index - 1) * 360f / inner; }
                else { int outer = Mathf.Max(1, rest - inner); r = 1f; ang = rot + 20f + (index - 1 - inner) * 360f / outer; }
            }
            var j = ctx.RngFor(index);
            float jx = (float)(j.NextDouble() * 2.0 - 1.0) * jitterDegrees;
            float jy = (float)(j.NextDouble() * 2.0 - 1.0) * jitterDegrees;
            float yaw = Mathf.Cos(ang * Mathf.Deg2Rad) * r * spreadDegrees + jx;
            float pitch = Mathf.Sin(ang * Mathf.Deg2Rad) * r * spreadDegrees + jy;
            var dir = ctx.direction.sqrMagnitude > 0.0001f ? ctx.direction.normalized : Vector3.forward;
            return Quaternion.LookRotation(dir) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            combat.TracePellet(ctx, PelletDirection(ctx, shotIndex), range, radius, shotIndex, ShotCount, minPelletsToDamage);
        }
    }
}
