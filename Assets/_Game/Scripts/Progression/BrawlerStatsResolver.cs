using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Atributos efetivos = valores base do BrawlerDefinition + melhorias + passiva do pet, limitados pelas regras da
    /// partida (casual/competitiva). Nunca altera o BrawlerDefinition: devolve uma cópia calculada.
    /// </summary>
    public static class BrawlerStatsResolver
    {
        /// <summary>Cálculo puro (testável): base + loadout + regras.</summary>
        public static EffectiveStats Resolve(BrawlerStats baseStats, PlayerLoadout loadout, ProgressionSettings settings, MatchRuleset ruleset, PetPassive petPassive)
        {
            var s = baseStats ?? new BrawlerStats();
            var stats = new EffectiveStats
            {
                maxHealth = Mathf.Max(1, s.maxHealth),
                damageMultiplier = 1f,
                regenPercentPerSecond = s.regenPercentPerSecond,
                regenDelay = s.regenDelay,
                speedMultiplier = 1f,
                sprintMultiplier = 1f,
                reloadTimeMultiplier = 1f,
                superChargeMultiplier = 1f,
            };
            if (settings == null) return stats;

            var rules = settings.Rules(ruleset);
            if (rules.upgradesEnabled)
            {
                stats.maxHealth = Mathf.RoundToInt(stats.maxHealth * (1f + Bonus(settings, rules, loadout, UpgradeStat.MaxHealth)));
                stats.damageMultiplier = 1f + Bonus(settings, rules, loadout, UpgradeStat.Damage);
                stats.regenPercentPerSecond *= 1f + Bonus(settings, rules, loadout, UpgradeStat.Regeneration);
                float speed = Bonus(settings, rules, loadout, UpgradeStat.Speed);
                stats.speedMultiplier = 1f + speed;
                // Recarga: +10% de melhoria = cada carga demora 1/1.1 do tempo
                stats.reloadTimeMultiplier = 1f / (1f + Bonus(settings, rules, loadout, UpgradeStat.Reload));
                stats.superChargeMultiplier = 1f + Bonus(settings, rules, loadout, UpgradeStat.SuperEfficiency);
            }

            if (petPassive != null && loadout.HasPet)
            {
                int level = Mathf.Clamp(loadout.PetLevel, 1, 10);
                float v = petPassive.ValueAt(level);
                if (petPassive.IsCombat && rules.petCombatPassives)
                {
                    switch (petPassive.type)
                    {
                        case PetPassiveType.Mobility: stats.speedMultiplier *= 1f + v; break;
                        case PetPassiveType.Sprint: stats.sprintMultiplier *= 1f + v; break;
                        case PetPassiveType.Shield:
                            stats.petShield = Mathf.RoundToInt(v);
                            stats.petShieldCooldown = Mathf.Max(5f, petPassive.cooldown);
                            break;
                        case PetPassiveType.Recovery:
                            stats.regenPercentPerSecond *= 1f + v;
                            stats.regenDelay = Mathf.Max(1.5f, stats.regenDelay - v * 2f);
                            break;
                    }
                }
                else if (petPassive.IsInfo && rules.petInfoPassives)
                {
                    if (petPassive.type == PetPassiveType.EnemyRadar) stats.petRadarRadius = v;
                    else if (petPassive.type == PetPassiveType.Alert) stats.petAlertRadius = v;
                }
            }
            return stats;
        }

        static float Bonus(ProgressionSettings settings, RulesetSettings rules, PlayerLoadout loadout, UpgradeStat stat)
        {
            var rule = settings.Upgrade(stat);
            if (rule == null) return 0f;
            int level = Mathf.Clamp(loadout.GetUpgrade(stat), 0, rule.maxLevel);
            return Mathf.Min(level * rule.bonusPerLevel, rules.maxBonusPerStat);
        }

        /// <summary>Em jogo: lê o Brawler, o loadout sincronizado e as regras da partida.</summary>
        public static EffectiveStats Resolve(PlayerNetwork player)
        {
            if (player == null) return EffectiveStats.FromBase(null);
            var b = player.Brawler;
            var loadout = player.Loadout.Value;
            // O loadout só conta se for do Brawler atual (troca de Brawler a meio → valores base até chegar o novo)
            if (b == null || loadout.BrawlerIndex != b.runtimeIndex)
            {
                var pet = loadout;
                loadout = PlayerLoadout.Empty;
                loadout.PetIndex = pet.PetIndex;
                loadout.PetLevel = pet.PetLevel;
            }
            var db = GameExperienceDatabase.Instance;
            var settings = db != null ? db.Settings : null;
            var petDef = db != null ? db.PetAt(loadout.PetIndex) : null;
            return Resolve(b != null ? b.stats : null, loadout, settings, MatchManager.CurrentRuleset, petDef != null ? petDef.rules.passive : null);
        }
    }
}
