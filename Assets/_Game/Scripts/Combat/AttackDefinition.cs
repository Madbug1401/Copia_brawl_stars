using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Um ataque = forma de entrega (AttackDelivery) + efeito (EffectPayload) + visual/som.
    /// O mesmo AttackDefinition serve para ataque principal, Super ou habilidades futuras.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Attack Definition")]
    public class AttackDefinition : ScriptableObject
    {
        public AttackDelivery delivery;
        public EffectPayload effect = new EffectPayload();
        public AttackVisuals visuals = new AttackVisuals();

        public float Range => delivery != null ? delivery.Range : 0f;
    }
}
