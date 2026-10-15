using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    [Serializable]
    public class BrawlerStats
    {
        public int maxHealth = 100;
        [Tooltip("Segundos sem levar dano nem atacar até começar a regenerar.")]
        public float regenDelay = 3f;
        [Tooltip("Fração da vida máxima regenerada por segundo.")]
        public float regenPercentPerSecond = 0.15f;
    }

    [Serializable]
    public class MovementProfile
    {
        public float walkSpeed = 6.5f;
        public float sprintSpeed = 9.5f;
        public float jumpHeight = 1.3f;
    }

    [Serializable]
    public class PrimaryAttackDefinition
    {
        public AttackDefinition attack;
        [Tooltip("Cargas de ataque (barra de munição).")]
        public int maxCharges = 3;
        [Tooltip("Segundos para recarregar uma carga.")]
        public float reloadTimePerCharge = 1.3f;
        [Tooltip("Tempo mínimo entre ataques.")]
        public float cooldown = 0.5f;
    }

    [Serializable]
    public class SuperDefinition
    {
        public AttackDefinition attack;
        [Tooltip("Dano causado com o ataque principal necessário para carregar o Super.")]
        public float chargeRequired = 200f;
    }

    [Serializable]
    public class MeleeProfile
    {
        [Min(1)] public int damage = 42;
        [Min(0.5f)] public float range = 2.25f;
        [Min(0.05f)] public float radius = 0.32f;
        [Min(0.1f)] public float cooldown = 0.8f;
        [Min(0.1f)] public float animationDuration = 0.3f;
    }

    public enum RigStyle
    {
        Generic,
        DualRevolvers,
        DualExplosives,
        Bow,
        SprayCan,
        Shotgun,
        Sniper,
    }

    /// <summary>Acessório de cabeça do personagem (silhueta reconhecível à distância).</summary>
    public enum HeadGear
    {
        None,
        WideHat,      // chapéu de aba larga
        Goggles,      // óculos de aviador + cabelo espetado
        FoxHood,      // capuz com orelhas de raposa
        GasMask,      // máscara de gás + gorro
        Bandana,      // lenço na cabeça + rabo de cavalo
        EarCap,       // boné com tufos de lince
    }

    /// <summary>Aspeto do corpo em terceira pessoa (rig procedural de primitivas, estilo cartoon).</summary>
    [Serializable]
    public class CharacterLook
    {
        public Color skin = new Color(1f, 0.8f, 0.64f);
        public Color outfit = new Color(0.9f, 0.7f, 0.3f);
        public Color pants = new Color(0.25f, 0.3f, 0.45f);
        public Color accent = new Color(0.9f, 0.2f, 0.2f);
        public Color hair = new Color(0.35f, 0.22f, 0.12f);
        public Color gear = new Color(0.45f, 0.3f, 0.18f);
        public HeadGear headGear = HeadGear.None;
        [Tooltip("Escala geral do corpo (tanques maiores).")]
        public float bodyScale = 1f;
        [Tooltip("Escala da cabeça (proporções chibi).")]
        public float headScale = 1f;
        [Tooltip("Largura do tronco.")]
        public float bulk = 1f;
    }

    [Serializable]
    public class FirstPersonPresentation
    {
        public RigStyle rig = RigStyle.Generic;
        [Tooltip("Prefab opcional (modelos finais). Vazio = rig procedural de primitivas.")]
        public GameObject prefabOverride;
        [Tooltip("Pontos de saída dos ataques relativos à câmara (ex.: dois revólveres).")]
        public Vector3[] muzzleOffsets = { new Vector3(0.17f, -0.14f, 0.55f) };
        public Color accentColor = Color.white;
    }

    [Serializable]
    public class ThirdPersonPresentation
    {
        public RigStyle rig = RigStyle.Generic;
        public GameObject prefabOverride;
        [Tooltip("Pontos de saída dos ataques relativos ao AimPivot.")]
        public Vector3[] muzzleOffsets = { new Vector3(0.32f, 0.02f, 0.7f) };
        public Color bodyColor = Color.white;
        public CharacterLook look = new CharacterLook();
    }

    [Serializable]
    public class AnimationSet
    {
        [Tooltip("Animator para modelos finais (opcional). Vazio = animação procedural.")]
        public RuntimeAnimatorController animatorController;
        public float attackKick = 1f;
        public float attackDuration = 0.35f;
        public float reloadDuration = 0.4f;
        public float superDuration = 0.6f;
        public float bobAmount = 1f;
    }

    [Serializable]
    public class VFXSet
    {
        public Color primaryColor = new Color(1f, 0.85f, 0.35f);
        public Color superColor = new Color(1f, 0.4f, 0.25f);
        public float muzzleFlashScale = 1f;
        public float impactScale = 1f;
    }

    [Serializable]
    public class SFXSet
    {
        public Sound primary = Sound.Shot;
        public Sound super = Sound.SuperShot;
        public Sound impact = Sound.Explosion;
        [Tooltip("Clips finais opcionais; vazio = sons procedurais.")]
        public AudioClip primaryClip;
        public AudioClip superClip;
    }

    [Serializable]
    public class BotProfile
    {
        public float preferredRange = 14f;
        [Tooltip("Distância máxima para usar o Super.")]
        public float superUseRange = 15f;
        [Tooltip("Ataque em arco: apontar para o chão junto ao alvo.")]
        public bool aimAtFeet;
        [Tooltip("O Super serve para fugir (ex.: salto): o bot usa-o de costas para o inimigo quando este se aproxima.")]
        public bool superIsEscape;
    }

    /// <summary>
    /// Definição completa de um Brawler. Todos os valores de jogo e de apresentação vêm daqui;
    /// os sistemas (movimento, combate, UI, bots, visuais) só leem estes dados.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Brawler Definition")]
    public class BrawlerDefinition : ScriptableObject
    {
        [Header("Identidade")]
        [Tooltip("Id técnico estável (ex.: gunslinger).")]
        public string brawlerId = "brawler";
        public string displayName = "Brawler";
        [TextArea] public string role = "";
        public bool available = true;

        [Header("Jogo")]
        public BrawlerStats stats = new BrawlerStats();
        public MovementProfile movement = new MovementProfile();
        public PrimaryAttackDefinition primary = new PrimaryAttackDefinition();
        public SuperDefinition super = new SuperDefinition();
        public MeleeProfile melee = new MeleeProfile();
        public List<AbilityDefinition> abilities = new List<AbilityDefinition>();

        [Header("Apresentação")]
        public FirstPersonPresentation firstPerson = new FirstPersonPresentation();
        public ThirdPersonPresentation thirdPerson = new ThirdPersonPresentation();
        public AnimationSet animations = new AnimationSet();
        public VFXSet vfx = new VFXSet();
        public SFXSet sfx = new SFXSet();

        [Header("IA")]
        public BotProfile bot = new BotProfile();

        /// <summary>Índice na BrawlerRegistry (é o que viaja pela rede).</summary>
        [NonSerialized] public int runtimeIndex;

        public int MaxHealth => stats.maxHealth;
        public Color Tint => thirdPerson.bodyColor;
    }
}
