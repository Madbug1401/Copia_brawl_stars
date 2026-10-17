using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Host: limpa um loadout pedido por um cliente. Índices fora dos dados, skins de outro Brawler/pet, níveis
    /// impossíveis e melhorias acima dos limites são descartados. (A posse dos itens vive na gravação local do
    /// jogador; ver a documentação para o caminho até armazenamento autoritativo.)
    /// </summary>
    public static class LoadoutValidator
    {
        public static PlayerLoadout Validate(PlayerLoadout l, ExperienceCatalog catalog)
        {
            var clean = PlayerLoadout.Empty;
            if (catalog == null) return clean;
            var s = catalog.progression;

            string brawlerId = catalog.BrawlerIdAt(l.BrawlerIndex);
            if (string.IsNullOrEmpty(brawlerId)) return clean;
            clean.BrawlerIndex = l.BrawlerIndex;

            var skin = catalog.SkinAt(l.SkinIndex);
            if (skin != null && skin.target == SkinTarget.Brawler && skin.targetId == brawlerId) clean.SkinIndex = l.SkinIndex;

            var pet = catalog.PetAt(l.PetIndex);
            if (pet != null)
            {
                clean.PetIndex = l.PetIndex;
                clean.PetLevel = (byte)Mathf.Clamp(l.PetLevel, 1, s != null ? s.petCurve.maxLevel : 10);
                var petSkin = catalog.SkinAt(l.PetSkinIndex);
                if (petSkin != null && petSkin.target == SkinTarget.Pet && petSkin.targetId == pet.petId) clean.PetSkinIndex = l.PetSkinIndex;
            }

            int maxBrawlerLevel = s != null ? s.brawlerCurve.maxLevel : 10;
            clean.BrawlerLevel = (byte)Mathf.Clamp(l.BrawlerLevel, 1, maxBrawlerLevel);

            if (s != null)
            {
                int total = 0;
                for (int i = 0; i < BrawlerProgressData.UpgradeCount; i++)
                {
                    var stat = (UpgradeStat)i;
                    var rule = s.Upgrade(stat);
                    int level = rule != null ? Mathf.Clamp(l.GetUpgrade(stat), 0, rule.maxLevel) : 0;
                    clean.SetUpgrade(stat, level);
                    total += level;
                }
                // Mais melhorias do que os pontos deste nível → pedido inválido, sem melhorias
                if (total > s.UpgradePointsAt(clean.BrawlerLevel))
                    for (int i = 0; i < BrawlerProgressData.UpgradeCount; i++) clean.SetUpgrade((UpgradeStat)i, 0);
            }
            return clean;
        }
    }
}
