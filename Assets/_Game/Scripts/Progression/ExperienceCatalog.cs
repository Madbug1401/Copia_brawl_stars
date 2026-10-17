using System.Collections.Generic;

namespace FPSLAN
{
    /// <summary>
    /// Vista simples (sem Unity) dos dados do "Game Experience": regras de progressão, pets, skins, missões e
    /// cosméticos. Os managers trabalham sobre isto, por isso podem ser testados fora do editor.
    /// Em jogo é construída a partir do GameExperienceDatabase (ScriptableObject); a ordem das listas é a mesma
    /// (os índices de pets/skins viajam pela rede).
    /// </summary>
    public sealed class ExperienceCatalog
    {
        public ProgressionSettings progression = ProgressionSettings.CreateDefault();
        public List<PetRules> pets = new List<PetRules>();
        public List<SkinDefinition> skins = new List<SkinDefinition>();
        public List<QuestDefinition> quests = new List<QuestDefinition>();
        public List<CosmeticDefinition> cosmetics = new List<CosmeticDefinition>();
        /// <summary>Ids técnicos dos Brawlers, pela ordem da BrawlerRegistry.</summary>
        public List<string> brawlerIds = new List<string>();

        /// <summary>Moedas dadas quando uma recompensa única (pet/skin) já era do jogador.</summary>
        public int duplicateCoinValue = 100;

        public PetRules Pet(string id)
        {
            int i = PetIndex(id);
            return i >= 0 ? pets[i] : null;
        }

        public int PetIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < pets.Count; i++)
                if (pets[i] != null && pets[i].petId == id) return i;
            return -1;
        }

        public PetRules PetAt(int index) => index >= 0 && index < pets.Count ? pets[index] : null;

        public SkinDefinition Skin(string id)
        {
            int i = SkinIndex(id);
            return i >= 0 ? skins[i] : null;
        }

        public int SkinIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < skins.Count; i++)
                if (skins[i] != null && skins[i].skinId == id) return i;
            return -1;
        }

        public SkinDefinition SkinAt(int index) => index >= 0 && index < skins.Count ? skins[index] : null;

        public QuestDefinition Quest(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var q in quests)
                if (q != null && q.questId == id) return q;
            return null;
        }

        public CosmeticDefinition Cosmetic(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in cosmetics)
                if (c != null && c.cosmeticId == id) return c;
            return null;
        }

        public bool HasBrawler(string id) => !string.IsNullOrEmpty(id) && brawlerIds.Contains(id);

        public string BrawlerIdAt(int index) => index >= 0 && index < brawlerIds.Count ? brawlerIds[index] : "";

        public int BrawlerIndex(string id) => string.IsNullOrEmpty(id) ? -1 : brawlerIds.IndexOf(id);

        /// <summary>Skins de um Brawler ou pet.</summary>
        public List<SkinDefinition> SkinsFor(SkinTarget target, string targetId)
        {
            var list = new List<SkinDefinition>();
            foreach (var s in skins)
                if (s != null && s.target == target && s.targetId == targetId) list.Add(s);
            return list;
        }
    }
}
