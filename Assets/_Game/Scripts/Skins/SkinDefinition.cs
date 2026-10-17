using System;
using UnityEngine;

namespace FPSLAN
{
    public enum SkinRarity : byte
    {
        Comum,
        Rara,
        Epica,
        Lendaria,
    }

    public enum SkinTarget : byte
    {
        Brawler,
        Pet,
    }

    /// <summary>
    /// Skin cosmética de um Brawler ou de um pet. Só tem campos de aspeto: nunca altera dano, vida, velocidade nem o
    /// tamanho do corpo (a escala do Brawler fica sempre a do BrawlerDefinition, para não mudar a leitura do alvo).
    /// </summary>
    [Serializable]
    public class SkinDefinition
    {
        [Tooltip("Id estável (gravações e rede). Nunca mudar depois de publicado.")]
        public string skinId = "skin";
        public string displayName = "Skin";
        public SkinRarity rarity = SkinRarity.Comum;
        public SkinTarget target = SkinTarget.Brawler;
        [Tooltip("brawlerId (ex.: gunslinger) ou petId (ex.: pet_dog) a que a skin pertence.")]
        public string targetId = "";
        public UnlockRule unlock = new UnlockRule(UnlockMethod.Coins, 300);

        [Header("Brawler")]
        [Tooltip("Cores e acessório de cabeça (a escala do corpo é ignorada de propósito).")]
        public CharacterLook look = new CharacterLook();
        [Tooltip("Cor de destaque das armas e dos efeitos.")]
        public Color weaponAccent = Color.white;

        [Header("Pet")]
        public Color petMain = Color.white;
        public Color petSecondary = Color.white;
        public Color petAccent = Color.white;

        [Header("Extra")]
        [Tooltip("Brilho/partículas leves (skins lendárias).")]
        public bool sparkle;

        public static string RarityName(SkinRarity r)
        {
            switch (r)
            {
                case SkinRarity.Rara: return "Rara";
                case SkinRarity.Epica: return "Épica";
                case SkinRarity.Lendaria: return "Lendária";
                default: return "Comum";
            }
        }

        public static Color RarityColor(SkinRarity r)
        {
            switch (r)
            {
                case SkinRarity.Rara: return new Color(0.35f, 0.7f, 1f);
                case SkinRarity.Epica: return new Color(0.75f, 0.4f, 1f);
                case SkinRarity.Lendaria: return new Color(1f, 0.78f, 0.2f);
                default: return new Color(0.8f, 0.85f, 0.9f);
            }
        }

        /// <summary>Aspeto final: cores e acessório da skin, proporções sempre do Brawler base.</summary>
        public CharacterLook ApplyTo(CharacterLook baseLook)
        {
            if (baseLook == null) baseLook = new CharacterLook();
            if (look == null) return baseLook;
            return new CharacterLook
            {
                skin = look.skin,
                outfit = look.outfit,
                pants = look.pants,
                accent = look.accent,
                hair = look.hair,
                gear = look.gear,
                headGear = look.headGear,
                bodyScale = baseLook.bodyScale,
                headScale = baseLook.headScale,
                bulk = baseLook.bulk,
            };
        }
    }

    /// <summary>Cosmético simples (títulos, molduras). Sem efeito no jogo.</summary>
    [Serializable]
    public class CosmeticDefinition
    {
        public string cosmeticId = "cosmetic";
        public string displayName = "Cosmético";
        public SkinRarity rarity = SkinRarity.Comum;

        public CosmeticDefinition() { }
        public CosmeticDefinition(string id, string name, SkinRarity rarity)
        {
            cosmeticId = id;
            displayName = name;
            this.rarity = rarity;
        }
    }
}
