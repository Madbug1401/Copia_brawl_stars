using UnityEngine;

namespace FPSLAN
{
    public enum UpgradeResult
    {
        Ok,
        MaxLevel,
        CharacterLevelTooLow,
        NotEnoughCoins,
        UnknownCharacter,
    }

    /// <summary>
    /// Melhorias de atributos por personagem (vida, dano, regeneração, velocidade, recarga, eficiência do especial).
    /// Só guarda níveis; os valores efetivos resultam sempre de base × modificadores (StatResolver), nunca alterando
    /// o BrawlerDefinition. Cada nível exige moedas e um nível mínimo de personagem.
    /// </summary>
    public class CharacterUpgradeManager
    {
        readonly GameExperienceContext m_Ctx;

        public CharacterUpgradeManager(GameExperienceContext ctx) { m_Ctx = ctx; }

        ProgressionConfig Cfg => m_Ctx.Config;

        public int Level(string brawlerId, UpgradeStat stat)
        {
            var c = m_Ctx.Profile.Character(brawlerId, false);
            return c != null ? Mathf.Clamp(c.Upgrade(stat), 0, Cfg.maxUpgradeLevel) : 0;
        }

        public int NextCost(string brawlerId, UpgradeStat stat) => Cfg.UpgradeCost(Level(brawlerId, stat));

        public UpgradeResult CanUpgrade(string brawlerId, UpgradeStat stat)
        {
            if (string.IsNullOrEmpty(brawlerId)) return UpgradeResult.UnknownCharacter;
            int current = Level(brawlerId, stat);
            if (current >= Cfg.maxUpgradeLevel) return UpgradeResult.MaxLevel;
            if (m_Ctx.Progression.CharacterLevel(brawlerId) < Cfg.CharacterLevelRequired(current + 1)) return UpgradeResult.CharacterLevelTooLow;
            if (m_Ctx.Inventory.Coins < Cfg.UpgradeCost(current)) return UpgradeResult.NotEnoughCoins;
            return UpgradeResult.Ok;
        }

        public UpgradeResult TryUpgrade(string brawlerId, UpgradeStat stat)
        {
            var check = CanUpgrade(brawlerId, stat);
            if (check != UpgradeResult.Ok) return check;
            int current = Level(brawlerId, stat);
            if (!m_Ctx.Inventory.TrySpend(Cfg.UpgradeCost(current))) return UpgradeResult.NotEnoughCoins;
            m_Ctx.Profile.Character(brawlerId, true).SetUpgrade(stat, current + 1);
            var b = BrawlerRegistry.FindById(brawlerId);
            m_Ctx.Emit(FeedbackType.UpgradePurchased, Cfg.Tuning(stat).label.ToUpperInvariant() + " · LEVEL " + (current + 1),
                b != null ? b.displayName : brawlerId, new Color(0.5f, 0.9f, 1f));
            m_Ctx.Save();
            return UpgradeResult.Ok;
        }

        /// <summary>Bónus efetivo (fração) de um atributo para a personagem, em casual ou competitivo.</summary>
        public float EffectiveBonus(string brawlerId, UpgradeStat stat, bool competitive)
        {
            int allowed = Mathf.Min(Cfg.maxUpgradeLevel, Cfg.MaxUpgradeLevelFor(m_Ctx.Progression.CharacterLevel(brawlerId)));
            return StatResolver.BonusFor(Cfg, stat, Mathf.Min(Level(brawlerId, stat), allowed), competitive);
        }

        /// <summary>Atributos efetivos (para a UI) com os níveis atuais desta personagem e o pet equipado.</summary>
        public EffectiveModifiers Preview(string brawlerId, bool competitive)
        {
            return StatResolver.Resolve(Cfg, m_Ctx.PetCatalog, GameExperience.BuildLoadoutFor(m_Ctx, brawlerId), competitive);
        }

        public static string Describe(UpgradeResult r)
        {
            switch (r)
            {
                case UpgradeResult.MaxLevel: return "Max level";
                case UpgradeResult.CharacterLevelTooLow: return "Level up this character";
                case UpgradeResult.NotEnoughCoins: return "Not enough coins";
                case UpgradeResult.UnknownCharacter: return "Unknown character";
                default: return "Available";
            }
        }
    }
}
