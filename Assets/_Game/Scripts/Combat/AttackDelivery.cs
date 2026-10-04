using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Forma de entrega de um ataque. Para uma mecânica nova basta criar uma subclasse;
    /// o CombatSystem trata do agendamento das rajadas e da resolução de efeitos.
    /// </summary>
    public abstract class AttackDelivery : ScriptableObject
    {
        public abstract float Range { get; }
        public virtual int ShotCount => 1;
        public virtual float ShotInterval => 0f;
        /// <summary>Usa o ponto-alvo (onde o jogador aponta) em vez de só a direção (arcos, armadilhas).</summary>
        public virtual bool UsesTargetPoint => false;

        /// <summary>Executa um disparo. Corre no host (autoritativo) e em todas as máquinas (visual).</summary>
        public abstract void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex);

        /// <summary>Direção com spread determinístico (seed) e leque.</summary>
        protected static Vector3 SpreadDirection(AttackContext ctx, int shotIndex, int count, float spreadDegrees, float fanDegrees)
        {
            var rng = ctx.RngFor(shotIndex);
            float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * spreadDegrees;
            float pitch = (float)(rng.NextDouble() * 2.0 - 1.0) * spreadDegrees * 0.5f;
            if (fanDegrees > 0f && count > 1)
                yaw += Mathf.Lerp(-fanDegrees * 0.5f, fanDegrees * 0.5f, shotIndex / (float)(count - 1));
            var dir = ctx.direction.sqrMagnitude > 0.0001f ? ctx.direction.normalized : Vector3.forward;
            return Quaternion.LookRotation(dir) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }
    }
}
