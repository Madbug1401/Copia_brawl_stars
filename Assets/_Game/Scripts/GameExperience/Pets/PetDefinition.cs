using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    public enum PetCategory
    {
        Support,
        Agility,
        Defense,
        Alert,
        Mobility,
        Endurance,
    }

    /// <summary>Habilidade passiva de um pet (pequena, para não dar vantagens desproporcionadas).</summary>
    public enum PetAbilityType : byte
    {
        None,
        /// <summary>Cão: deteta adversários próximos (direção no HUD).</summary>
        Detection,
        /// <summary>Gato: pequeno bónus de velocidade.</summary>
        Agility,
        /// <summary>Porco: escudo temporário quando a vida desce.</summary>
        Protection,
        /// <summary>Galo: avisa de ameaças fora do campo de visão.</summary>
        Alert,
        /// <summary>Coelho: bónus de velocidade ao correr (sprint).</summary>
        Mobility,
        /// <summary>Tartaruga: regenera mais depressa.</summary>
        Recovery,
    }

    /// <summary>Espécie (forma do modelo cartoon). Novas espécies: acrescentar aqui e no PetVisualBuilder.</summary>
    public enum PetSpecies
    {
        Dog,
        Cat,
        Pig,
        Rooster,
        Rabbit,
        Turtle,
    }

    public enum PetMilestoneType
    {
        AbilityBoost,
        Animation,
        Visual,
        Cosmetic,
    }

    /// <summary>Recompensa de evolução num nível do pet.</summary>
    [Serializable]
    public class PetMilestone
    {
        [Range(1, 10)] public int level = 3;
        public PetMilestoneType type = PetMilestoneType.AbilityBoost;
        public string description = "";
    }

    /// <summary>
    /// Definição de dados de um pet. Para acrescentar um pet novo basta criar um asset destes e juntá-lo ao PetCatalog
    /// (com uma espécie existente, ou uma nova no PetVisualBuilder). O petId é estável: é o que vai para o save e para a rede.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Pet Definition")]
    public class PetDefinition : ScriptableObject
    {
        [Header("Identidade")]
        [Tooltip("Id técnico estável (ex.: pet_dog). Nunca mudar.")]
        public string petId = "pet";
        public string displayName = "Pet";
        public PetCategory category;
        [TextArea] public string description = "";
        public PetSpecies species;

        [Header("Habilidade (escala com o nível)")]
        public PetAbilityType ability;
        [Tooltip("Valor no nível 1 (raio em m, fração de bónus, ou pontos de escudo, conforme a habilidade).")]
        public float abilityBase;
        public float abilityPerLevel;
        [Tooltip("Bónus extra a partir do nível 3 (\"pequena melhoria na habilidade\").")]
        public float abilityMilestoneBonus;
        [Tooltip("Valor máximo (limite de equilíbrio).")]
        public float abilityMax;
        [TextArea] public string abilityDescription = "";

        [Header("Aspeto (cartoon)")]
        public Color primaryColor = Color.white;
        public Color secondaryColor = Color.gray;
        public Color accentColor = Color.red;
        [Tooltip("Escala do modelo no estágio evoluído.")]
        public float scale = 1f;

        [Header("Desbloqueio")]
        public UnlockRule unlock = new UnlockRule();

        [Header("Evolução")]
        public List<PetMilestone> milestones = new List<PetMilestone>();

        public int StableHash => StableId.Hash(petId);

        public float AbilityValue(int level)
        {
            level = Mathf.Clamp(level, 1, 10);
            float v = abilityBase + abilityPerLevel * (level - 1) + (level >= 3 ? abilityMilestoneBonus : 0f);
            return abilityMax > 0f ? Mathf.Min(v, abilityMax) : v;
        }

        /// <summary>Estágio visual: 0 = jovem (1–3), 1 = desenvolvido (4–7), 2 = evoluído (8–10).</summary>
        public static int StageFor(int level) => level >= 8 ? 2 : (level >= 4 ? 1 : 0);

        public static string StageName(int stage)
        {
            switch (stage)
            {
                case 2: return "Evolved";
                case 1: return "Developed";
                default: return "Young";
            }
        }

        public static string CategoryLabel(PetCategory c)
        {
            switch (c)
            {
                case PetCategory.Support: return "Support";
                case PetCategory.Agility: return "Agility";
                case PetCategory.Defense: return "Defense";
                case PetCategory.Alert: return "Alert";
                case PetCategory.Mobility: return "Mobility";
                default: return "Resistance";
            }
        }

        /// <summary>Texto curto do efeito no nível indicado.</summary>
        public string AbilityText(int level)
        {
            float v = AbilityValue(level);
            switch (ability)
            {
                case PetAbilityType.Detection: return "Detects enemies within " + v.ToString("0.0") + " m (direction on screen)";
                case PetAbilityType.Agility: return "+" + (v * 100f).ToString("0.0") + "% speed";
                case PetAbilityType.Protection: return "Shield of " + Mathf.RoundToInt(v) + " health when health is low";
                case PetAbilityType.Alert: return "Warns of threats behind you within " + v.ToString("0") + " m";
                case PetAbilityType.Mobility: return "+" + (v * 100f).ToString("0.0") + "% sprint speed";
                case PetAbilityType.Recovery: return "+" + (v * 100f).ToString("0") + "% regeneration";
                default: return "—";
            }
        }
    }
}
