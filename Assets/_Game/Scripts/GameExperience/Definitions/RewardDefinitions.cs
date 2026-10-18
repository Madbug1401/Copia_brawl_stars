using System;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Raridade de skins e cosméticos.</summary>
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>Forma de desbloquear um item (pet, skin, personagem).</summary>
    public enum UnlockMethod
    {
        /// <summary>Desbloqueado desde o início.</summary>
        Default,
        /// <summary>Ao atingir o nível de jogador 'value'.</summary>
        PlayerLevel,
        /// <summary>Ao completar 'value' missões (refId vazio) ou a missão refId.</summary>
        Quest,
        /// <summary>Ao ganhar a conquista refId.</summary>
        Achievement,
        /// <summary>Comprado com 'value' moedas do jogo.</summary>
        Coins,
        /// <summary>Ao concluir o desafio especial refId.</summary>
        Challenge,
        /// <summary>Durante o evento refId (futuro).</summary>
        Event,
        /// <summary>Só por recompensa (missões, níveis, conquistas que o atribuam).</summary>
        RewardOnly,
    }

    /// <summary>Regra de desbloqueio configurável por dados.</summary>
    [Serializable]
    public class UnlockRule
    {
        public UnlockMethod method = UnlockMethod.Default;
        [Tooltip("Nível, n.º de missões ou preço em moedas, conforme o método.")]
        public int value;
        [Tooltip("Id da conquista/desafio/evento/missão, conforme o método.")]
        public string refId = "";

        public bool IsPurchasable => method == UnlockMethod.Coins && value > 0;
    }

    /// <summary>Pacote de recompensas (XP, moedas, skins, pets, cosméticos).</summary>
    [Serializable]
    public class RewardBundle
    {
        public int xp;
        public int coins;
        public string[] skinIds = new string[0];
        public string[] petIds = new string[0];
        public string[] cosmeticIds = new string[0];

        public bool IsEmpty =>
            xp <= 0 && coins <= 0 &&
            (skinIds == null || skinIds.Length == 0) &&
            (petIds == null || petIds.Length == 0) &&
            (cosmeticIds == null || cosmeticIds.Length == 0);

        public string Describe(SkinCatalog skins = null, PetCatalog pets = null)
        {
            var sb = new System.Text.StringBuilder();
            void Add(string s)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(s);
            }
            if (xp > 0) Add("+" + xp + " XP");
            if (coins > 0) Add("+" + coins + " coins");
            if (petIds != null)
                foreach (var p in petIds)
                {
                    var def = pets != null ? pets.Find(p) : null;
                    Add("Pet: " + (def != null ? def.displayName : p));
                }
            if (skinIds != null)
                foreach (var s in skinIds)
                {
                    var def = skins != null ? skins.Find(s) : null;
                    Add("Skin: " + (def != null ? def.displayName : s));
                }
            if (cosmeticIds != null)
                foreach (var c in cosmeticIds) Add("Cosmetic: " + c);
            return sb.Length == 0 ? "—" : sb.ToString();
        }
    }

    public static class StableId
    {
        /// <summary>Hash FNV-1a de 32 bits de um id (estável entre máquinas e versões). Nunca devolve 0 (0 = "nenhum").</summary>
        public static int Hash(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in id)
                {
                    h ^= c;
                    h *= 16777619;
                }
                int r = (int)h;
                return r == 0 ? 1 : r;
            }
        }
    }

    public static class RarityStyle
    {
        public static Color ColorOf(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return new Color(0.35f, 0.7f, 1f);
                case Rarity.Epic: return new Color(0.78f, 0.42f, 1f);
                case Rarity.Legendary: return new Color(1f, 0.78f, 0.2f);
                default: return new Color(0.75f, 0.8f, 0.85f);
            }
        }

        public static string LabelOf(Rarity r)
        {
            switch (r)
            {
                case Rarity.Rare: return "Rare";
                case Rarity.Epic: return "Epic";
                case Rarity.Legendary: return "Legendary";
                default: return "Common";
            }
        }
    }
}
