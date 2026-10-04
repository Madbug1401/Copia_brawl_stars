using UnityEngine;

namespace FPSLAN
{
    /// <summary>Habilidade extra (gadgets — futuro). Placeholder para manter a arquitetura preparada.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Ability Definition")]
    public class AbilityDefinition : ScriptableObject
    {
        public string abilityId;
        public string displayName;
        public AttackDefinition attack;
        public float cooldown = 10f;
        public int uses = 2;
    }
}
