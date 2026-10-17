using UnityEngine;

namespace FPSLAN
{
    public enum SkinTarget
    {
        Brawler,
        Pet,
    }

    /// <summary>Acessório extra de uma skin (puramente visual).</summary>
    public enum SkinAccessory
    {
        None,
        Cape,
        Crown,
        Halo,
        Scarf,
        Horns,
        Antenna,
    }

    /// <summary>Efeito de partículas contínuo de uma skin (puramente visual).</summary>
    public enum SkinEffect
    {
        None,
        Sparkles,
        Embers,
        Snowflakes,
        Leaves,
        Bubbles,
    }

    /// <summary>Estilo de animação de uma skin (só muda a amplitude/ritmo da animação procedural).</summary>
    public enum SkinAnimation
    {
        Default,
        Bouncy,
        Swagger,
        Floaty,
    }

    /// <summary>
    /// Skin de uma personagem ou de um pet. SÓ muda a aparência (cores, roupa, acessórios, arma, partículas,
    /// animação, efeitos): não existe nenhum campo de dano, vida ou velocidade — por desenho, não pode dar vantagem.
    /// O skinId é estável (save e rede usam o seu hash).
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Skin Definition")]
    public class SkinDefinition : ScriptableObject
    {
        [Header("Identidade")]
        [Tooltip("Id técnico estável (ex.: skin_gunslinger_neon). Nunca mudar.")]
        public string skinId = "skin";
        public string displayName = "Skin";
        public SkinTarget target = SkinTarget.Brawler;
        [Tooltip("brawlerId ou petId a que a skin pertence.")]
        public string targetId = "";
        public Rarity rarity = Rarity.Common;
        [TextArea] public string description = "";

        [Header("Desbloqueio")]
        public UnlockRule unlock = new UnlockRule { method = UnlockMethod.Coins, value = 300 };

        [Header("Cores do corpo / roupa (Brawler)")]
        public bool overrideColors = true;
        public Color skin = new Color(1f, 0.8f, 0.64f);
        public Color outfit = Color.white;
        public Color pants = Color.gray;
        public Color accent = Color.red;
        public Color hair = new Color(0.3f, 0.2f, 0.1f);
        public Color gear = new Color(0.4f, 0.3f, 0.2f);
        public bool overrideHeadGear;
        public HeadGear headGear = HeadGear.None;

        [Header("Arma e efeitos")]
        public bool overrideWeaponColor = true;
        public Color weaponAccent = Color.white;
        [Tooltip("Muda a cor dos disparos, rastos e explosões deste jogador.")]
        public bool overrideVfx = true;
        public Color primaryVfx = new Color(1f, 0.85f, 0.35f);
        public Color superVfx = new Color(1f, 0.4f, 0.25f);

        [Header("Extras")]
        public SkinAccessory accessory = SkinAccessory.None;
        public SkinEffect effect = SkinEffect.None;
        public SkinAnimation animation = SkinAnimation.Default;

        [Header("Cores do pet (skins de pet)")]
        public Color petPrimary = Color.white;
        public Color petSecondary = Color.gray;
        public Color petAccent = Color.red;

        public int StableHash => StableId.Hash(skinId);

        /// <summary>Aspeto do corpo com esta skin aplicada sobre o aspeto base do Brawler.</summary>
        public CharacterLook ApplyTo(CharacterLook baseLook)
        {
            var b = baseLook ?? new CharacterLook();
            var l = new CharacterLook
            {
                skin = b.skin, outfit = b.outfit, pants = b.pants, accent = b.accent, hair = b.hair, gear = b.gear,
                headGear = b.headGear, bodyScale = b.bodyScale, headScale = b.headScale, bulk = b.bulk,
            };
            if (overrideColors)
            {
                l.skin = skin;
                l.outfit = outfit;
                l.pants = pants;
                l.accent = accent;
                l.hair = hair;
                l.gear = gear;
            }
            if (overrideHeadGear) l.headGear = headGear;
            return l;
        }
    }
}
