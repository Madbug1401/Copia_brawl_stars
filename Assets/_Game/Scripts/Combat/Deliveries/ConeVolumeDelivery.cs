using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Volume em cone persistente (spray): aplica o efeito em ticks a quem estiver dentro do cone.
    /// O dano por tick usa o multiplicador perto/longe do EffectPayload.
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

        public override float Range => range;

        public override void FireShot(CombatSystem combat, AttackContext ctx, int shotIndex)
        {
            combat.SpawnCone(ctx, this);
        }
    }
}
