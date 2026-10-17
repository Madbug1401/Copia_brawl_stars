using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Todos os dados do "Game Experience": progressão, pets, skins, missões e cosméticos.
    /// Fica em Resources/GameExperience/GameExperienceDatabase.asset (gerado pelo menu FPS-LAN → Game Experience).
    /// Se o asset não existir, usa os valores por omissão em memória (o jogo funciona na mesma).
    /// A ordem das listas de pets e skins não deve mudar depois de publicada: os índices viajam pela rede
    /// (acrescentar sempre no fim).
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Database")]
    public class GameExperienceDatabase : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/GameExperienceDatabase";

        public ProgressionConfig progression;
        public List<PetDefinition> pets = new List<PetDefinition>();
        public List<SkinDefinition> skins = new List<SkinDefinition>();
        public List<QuestDefinition> quests = new List<QuestDefinition>();
        public List<CosmeticDefinition> cosmetics = new List<CosmeticDefinition>();
        [Tooltip("Moedas dadas quando uma recompensa única (pet/skin) já era do jogador.")]
        public int duplicateCoinValue = 100;

        static GameExperienceDatabase s_Instance;
        static ExperienceCatalog s_Catalog;
        static ProgressionSettings s_FallbackSettings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Instance = null;
            s_Catalog = null;
            s_FallbackSettings = null;
        }

        public static GameExperienceDatabase Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<GameExperienceDatabase>(ResourcePath);
                    if (s_Instance == null)
                    {
                        s_Instance = GameExperienceDefaults.CreateDatabase();
                        s_Instance.hideFlags = HideFlags.DontSave;
                    }
                    s_Catalog = null;
                }
                return s_Instance;
            }
        }

        public ProgressionSettings Settings
        {
            get
            {
                if (progression != null && progression.settings != null) return progression.settings;
                return s_FallbackSettings ?? (s_FallbackSettings = ProgressionSettings.CreateDefault());
            }
        }

        /// <summary>Vista simples para os managers (cache).</summary>
        public static ExperienceCatalog Catalog
        {
            get
            {
                var db = Instance;
                if (s_Catalog == null) s_Catalog = db.BuildCatalog();
                return s_Catalog;
            }
        }

        public ExperienceCatalog BuildCatalog()
        {
            var c = new ExperienceCatalog
            {
                progression = Settings,
                skins = skins,
                quests = quests,
                cosmetics = cosmetics,
                duplicateCoinValue = duplicateCoinValue,
            };
            foreach (var p in pets) c.pets.Add(p != null ? p.rules : null);
            foreach (var b in BrawlerRegistry.All) c.brawlerIds.Add(b != null ? b.brawlerId : "");
            return c;
        }

        public PetDefinition PetAt(int index) => index >= 0 && index < pets.Count ? pets[index] : null;

        public PetDefinition Pet(string petId)
        {
            foreach (var p in pets)
                if (p != null && p.PetId == petId) return p;
            return null;
        }

        public SkinDefinition SkinAt(int index) => index >= 0 && index < skins.Count ? skins[index] : null;
    }
}
