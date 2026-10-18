using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Lista de pets disponíveis (Resources/GameExperience/PetCatalog). Novos pets entram só por dados.</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Pet Catalog")]
    public class PetCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/PetCatalog";

        public List<PetDefinition> pets = new List<PetDefinition>();

        public PetDefinition Find(string petId)
        {
            if (string.IsNullOrEmpty(petId)) return null;
            foreach (var p in pets) if (p != null && p.petId == petId) return p;
            return null;
        }

        public PetDefinition FindByHash(int hash)
        {
            if (hash == 0) return null;
            foreach (var p in pets) if (p != null && p.StableHash == hash) return p;
            return null;
        }

        public static PetCatalog CreateDefault()
        {
            var c = CreateInstance<PetCatalog>();
            c.name = "PetCatalog (predefinido)";
            c.pets.AddRange(GameExperienceDefaults.CreatePets());
            return c;
        }
    }
}
