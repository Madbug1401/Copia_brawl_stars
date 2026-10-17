using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Volume em cone persistente (spray): aplica o efeito em ticks a quem estiver dentro do cone.
    /// O dano por tick usa o multiplicador perto/longe do EffectPayload. Cada alvo é avaliado uma vez por tick
    /// (vários colisores ou partículas não multiplicam o dano) e nunca recebe mais do que maxTicksPerTarget.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Deliveries/Cone Volume")]
    public class ConeVolumeDelivery : AttackDelivery
    {
        public float range = 10f;
        [Tooltip("Abertura total do cone em graus.")]
        public float angle = 32f;
        [Tooltip("Quanto tempo o volume fica ativo.")]
        public float duration = 0.6f;
        public float tickInterval = 0.2f;
        [Tooltip("Tempo até o spray chegar ao alcance máximo.")]
        public float expandTime = 0.25f;
        [Tooltip("Máximo de ticks de dano por alvo neste ataque (0 = sem limite). Dano total máx. = damage × este valor.")]
        public int maxTicksPerTarget = 0;
        [Tooltip("Visual: o spray sai alternadamente dos dois canos (ex.: duas latas). O dano continua a ser um só volume.")]
        public bool dualStream = false;

        public override float Range => range;

        /// <summary>Ticks que cabem na duração (o primeiro sai quando o spray chega ao alcance).</summary>
        public int ScheduledTicks
        {
            get
            {
                float first = FirstTickTime;
                float step = Mathf.Max(0.05f, tickInterval);
                int n = first < duration ? Mathf.FloorToInt((duration - first) / step + 0.0001f) + 1 : 0;
                return maxTicksPerTarget > 0 ? Mathf.Min(n, maxTicksPerTarget) : n;
            }
        }

        public float FirstTickTime => Mathf.Min(Mathf.Max(0.05f, tickInterval), Mathf.Max(0.01f, expandTime));

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            combat.SpawnCone(ctx, this);
        }
    }
}
