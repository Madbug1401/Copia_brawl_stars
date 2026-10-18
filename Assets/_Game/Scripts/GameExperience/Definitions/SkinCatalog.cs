using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Lista de skins de personagens e de pets (Resources/GameExperience/SkinCatalog).</summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Game Experience/Skin Catalog")]
    public class SkinCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameExperience/SkinCatalog";

        public List<SkinDefinition> skins = new List<SkinDefinition>();

        public SkinDefinition Find(string skinId)
        {
            if (string.IsNullOrEmpty(skinId)) return null;
            foreach (var s in skins) if (s != null && s.skinId == skinId) return s;
            return null;
        }

        public SkinDefinition FindByHash(int hash)
        {
            if (hash == 0) return null;
            foreach (var s in skins) if (s != null && s.StableHash == hash) return s;
            return null;
        }

        public List<SkinDefinition> ForTarget(SkinTarget target, string targetId)
        {
            var list = new List<SkinDefinition>();
            foreach (var s in skins)
                if (s != null && s.target == target && s.targetId == targetId) list.Add(s);
            return list;
        }

        public static SkinCatalog CreateDefault()
        {
            var c = CreateInstance<SkinCatalog>();
            c.name = "SkinCatalog (predefinido)";
            c.skins.AddRange(GameExperienceDefaults.CreateSkins());
            return c;
        }
    }
}
