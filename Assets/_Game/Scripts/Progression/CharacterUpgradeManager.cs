using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Melhorias de atributos por Brawler (vida, dano, regeneração, velocidade, recarga, eficiência do Super).
    /// Cada nível custa moedas e um ponto de melhoria (ganho ao subir o Brawler de nível); cada atributo tem um
    /// nível máximo. Os valores efetivos são calculados à parte (BrawlerStatsResolver) — os dados base nunca mudam.
    /// </summary>
    public sealed class CharacterUpgradeManager
    {
        public enum Result { Ok, UnknownBrawler, UnknownStat, MaxLevel, NoPoints, NotEnoughCoins }

        readonly ExperienceContext m_Ctx;
        readonly InventoryManager m_Inventory;
        readonly PlayerProgressionManager m_Progression;

        public CharacterUpgradeManager(ExperienceContext ctx, InventoryManager inventory, PlayerProgressionManager progression)
        {
            m_Ctx = ctx;
            m_Inventory = inventory;
            m_Progression = progression;
        }

        ProgressionSettings S => m_Ctx.Settings;

        public int Level(string brawlerId, UpgradeStat stat) => m_Ctx.profile.Brawler(brawlerId, false)?.Upgrade(stat) ?? 0;

        public int PointsTotal(string brawlerId) => S.UpgradePointsAt(m_Progression.BrawlerLevel(brawlerId));

        public int PointsUsed(string brawlerId) => m_Ctx.profile.Brawler(brawlerId, false)?.TotalUpgrades ?? 0;

        public int PointsAvailable(string brawlerId) => Mathf.Max(0, PointsTotal(brawlerId) - PointsUsed(brawlerId));

        public int NextCost(string brawlerId, UpgradeStat stat)
        {
            var rule = S.Upgrade(stat);
            return rule != null ? rule.CostForLevel(Level(brawlerId, stat) + 1) : 0;
        }

        public Result CanUpgrade(string brawlerId, UpgradeStat stat)
        {
            if (!m_Ctx.catalog.HasBrawler(brawlerId)) return Result.UnknownBrawler;
            var rule = S.Upgrade(stat);
            if (rule == null) return Result.UnknownStat;
            if (Level(brawlerId, stat) >= rule.maxLevel) return Result.MaxLevel;
            if (PointsAvailable(brawlerId) <= 0) return Result.NoPoints;
            if (m_Inventory.Coins < NextCost(brawlerId, stat)) return Result.NotEnoughCoins;
            return Result.Ok;
        }

        public Result TryUpgrade(string brawlerId, UpgradeStat stat)
        {
            var r = CanUpgrade(brawlerId, stat);
            if (r != Result.Ok) return r;
            int cost = NextCost(brawlerId, stat);
            if (!m_Inventory.TrySpend(cost)) return Result.NotEnoughCoins;
            var data = m_Ctx.profile.Brawler(brawlerId, true);
            data.SetUpgrade(stat, data.Upgrade(stat) + 1);
            m_Ctx.MarkDirty();
            var rule = S.Upgrade(stat);
            m_Ctx.Raise(ExperienceEventType.UpgradeBought, (rule != null ? rule.displayName : stat.ToString()) + " nível " + data.Upgrade(stat), "-" + cost + " moedas", brawlerId);
            return Result.Ok;
        }

        /// <summary>Bónus atual de um atributo (fração), já limitado pelas regras casuais.</summary>
        public float Bonus(string brawlerId, UpgradeStat stat)
        {
            var rule = S.Upgrade(stat);
            if (rule == null) return 0f;
            return Mathf.Min(Level(brawlerId, stat) * rule.bonusPerLevel, S.casual.maxBonusPerStat);
        }

        public static string Describe(Result r)
        {
            switch (r)
            {
                case Result.Ok: return "Melhorado!";
                case Result.MaxLevel: return "Nível máximo";
                case Result.NoPoints: return "Sem pontos (sobe o Brawler de nível)";
                case Result.NotEnoughCoins: return "Moedas insuficientes";
                default: return "Indisponível";
            }
        }
    }
}
