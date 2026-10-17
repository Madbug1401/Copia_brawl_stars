using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Habilidade passiva de um pet. As de combate desligam-se nas partidas competitivas.</summary>
    public enum PetPassiveType : byte
    {
        None,
        /// <summary>Cão: mostra a direção de inimigos visíveis por perto (informação).</summary>
        EnemyRadar,
        /// <summary>Gato: um pouco mais de velocidade a andar.</summary>
        Mobility,
        /// <summary>Porco: escudo pequeno que absorve dano e recarrega.</summary>
        Shield,
        /// <summary>Galo: alerta quando um inimigo se aproxima pelas costas (informação).</summary>
        Alert,
        /// <summary>Coelho: um pouco mais de velocidade a correr.</summary>
        Sprint,
        /// <summary>Tartaruga: regeneração um pouco mais forte e mais cedo.</summary>
        Recovery,
    }

    [Serializable]
    public class PetPassive
    {
        public PetPassiveType type = PetPassiveType.None;
        [Tooltip("Texto mostrado no menu.")]
        public string description = "";
        [Tooltip("Valor no nível 1 (fração para velocidade/regeneração, pontos de escudo, metros para radar/alerta).")]
        public float baseValue;
        [Tooltip("Valor acrescentado por nível acima do 1.")]
        public float perLevel;
        [Tooltip("Limite máximo do valor (evita vantagens desproporcionadas).")]
        public float maxValue;
        [Tooltip("Escudo: segundos para recarregar depois de partido.")]
        public float cooldown = 20f;

        /// <summary>Afeta números de combate (vida, velocidade, regeneração).</summary>
        public bool IsCombat => type == PetPassiveType.Mobility || type == PetPassiveType.Shield ||
                                type == PetPassiveType.Sprint || type == PetPassiveType.Recovery;

        public bool IsInfo => type == PetPassiveType.EnemyRadar || type == PetPassiveType.Alert;

        public float ValueAt(int level)
        {
            float v = baseValue + perLevel * Mathf.Max(0, level - 1);
            return maxValue > 0f ? Mathf.Min(v, maxValue) : v;
        }
    }

    /// <summary>Recompensa entregue quando um pet chega a um nível (3, 5, 8, 10).</summary>
    [Serializable]
    public class PetLevelReward
    {
        [Range(2, 10)] public int level = 3;
        public RewardEntry reward = new RewardEntry();
        [Tooltip("Texto curto mostrado na árvore de evolução.")]
        public string label = "";
    }

    /// <summary>Regras de jogo de um pet (sem visuais): usadas pelos managers e pelos testes.</summary>
    [Serializable]
    public class PetRules
    {
        [Tooltip("Id estável (gravações e rede). Nunca mudar depois de publicado.")]
        public string petId = "pet";
        public string displayName = "Pet";
        [TextArea] public string description = "";
        public PetPassive passive = new PetPassive();
        public UnlockRule unlock = new UnlockRule();
        public List<PetLevelReward> levelRewards = new List<PetLevelReward>();
    }

    public enum PetShape : byte { Sphere, Capsule, Cube, Cylinder }

    public enum PetColorSlot : byte { Main, Secondary, Accent, Eye, Dark, White, Nose, Gold, Beak }

    /// <summary>Papel de uma peça na animação procedural.</summary>
    public enum PetPartRole : byte
    {
        Body,
        Head,
        Tail,
        EarLeft,
        EarRight,
        LegFrontLeft,
        LegFrontRight,
        LegBackLeft,
        LegBackRight,
        WingLeft,
        WingRight,
        Detail,
    }

    /// <summary>Forma de andar (animação procedural).</summary>
    public enum PetGait : byte { Trot, Hop, Waddle, Crawl }

    /// <summary>Uma peça do modelo cartoon do pet (primitiva). Peças por fase permitem a evolução visual.</summary>
    [Serializable]
    public class PetPart
    {
        public string name = "part";
        public PetShape shape = PetShape.Sphere;
        public PetColorSlot color = PetColorSlot.Main;
        public PetPartRole role = PetPartRole.Body;
        public Vector3 position;
        public Vector3 scale = Vector3.one * 0.2f;
        public Vector3 euler;
        [Tooltip("Primeira fase em que a peça aparece (1 jovem, 2 desenvolvido, 3 evoluído).")]
        [Range(1, 3)] public int minStage = 1;
        [Range(1, 3)] public int maxStage = 3;

        public PetPart() { }
        public PetPart(string name, PetShape shape, PetColorSlot color, PetPartRole role, Vector3 position, Vector3 scale, Vector3 euler = default, int minStage = 1, int maxStage = 3)
        {
            this.name = name;
            this.shape = shape;
            this.color = color;
            this.role = role;
            this.position = position;
            this.scale = scale;
            this.euler = euler;
            this.minStage = minStage;
            this.maxStage = maxStage;
        }
    }

    /// <summary>
    /// Definição de um pet (dados + aspeto). Para criar um pet novo: criar um asset destes (ou duplicar um),
    /// dar-lhe um petId novo e acrescentá-lo à lista do GameExperienceDatabase — nenhum código muda.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Pet Definition")]
    public class PetDefinition : ScriptableObject
    {
        public PetRules rules = new PetRules();

        [Header("Aspeto (cartoon, primitivas)")]
        public Color mainColor = new Color(0.85f, 0.6f, 0.35f);
        public Color secondaryColor = new Color(1f, 0.9f, 0.75f);
        public Color accentColor = new Color(0.9f, 0.25f, 0.25f);
        public List<PetPart> parts = new List<PetPart>();
        [Tooltip("Escala do modelo em cada fase (jovem 1–3, desenvolvido 4–7, evoluído 8–10).")]
        public float[] stageScale = { 0.72f, 0.88f, 1.04f };

        [Header("Comportamento")]
        public PetGait gait = PetGait.Trot;
        [Tooltip("Distância a que segue o dono.")]
        public float followDistance = 1.7f;
        [Tooltip("Desvio para o lado (fica ao lado do dono, não à frente da mira).")]
        public float sideOffset = 0.9f;
        public float walkSpeed = 6.5f;
        public float runSpeed = 11f;
        [Tooltip("Altura dos saltinhos (coelho).")]
        public float hopHeight = 0.12f;

        public string PetId => rules != null ? rules.petId : name;
        public string DisplayName => rules != null ? rules.displayName : name;

        /// <summary>Fase visual pelo nível: 1–3 jovem, 4–7 desenvolvido, 8–10 evoluído.</summary>
        public static int StageForLevel(int level) => level >= 8 ? 3 : level >= 4 ? 2 : 1;

        public static string StageName(int stage) => stage >= 3 ? "Evoluído" : stage == 2 ? "Desenvolvido" : "Jovem";

        public float ScaleForStage(int stage)
        {
            if (stageScale == null || stageScale.Length == 0) return 1f;
            return stageScale[Mathf.Clamp(stage - 1, 0, stageScale.Length - 1)];
        }

        public Color ColorFor(PetColorSlot slot, Color main, Color secondary, Color accent)
        {
            switch (slot)
            {
                case PetColorSlot.Main: return main;
                case PetColorSlot.Secondary: return secondary;
                case PetColorSlot.Accent: return accent;
                case PetColorSlot.Eye: return new Color(0.08f, 0.08f, 0.1f);
                case PetColorSlot.Dark: return Color.Lerp(main, Color.black, 0.55f);
                case PetColorSlot.Nose: return new Color(0.95f, 0.5f, 0.55f);
                case PetColorSlot.Gold: return new Color(1f, 0.8f, 0.2f);
                case PetColorSlot.Beak: return new Color(1f, 0.7f, 0.15f);
                default: return Color.white;
            }
        }
    }
}
