using UnityEngine;

namespace FPSLAN
{
    /// <summary>XP, moedas e XP de pet de uma partida, parcela a parcela.</summary>
    public struct MatchXpBreakdown
    {
        public int participation, kills, assists, victory, objectives;
        public int total;
        public int brawlerXp;
        public int petXp;
        public int coins;
    }

    /// <summary>Resultado de somar XP a uma curva de níveis.</summary>
    public struct LevelChange
    {
        public int oldLevel, newLevel;
        public int oldTotal, newTotal;
        public bool LeveledUp => newLevel > oldLevel;
    }

    /// <summary>
    /// Contas de XP (puras, sem estado): quanto vale uma partida e como o XP total se traduz em níveis.
    /// O host usa isto para calcular a recompensa; o cliente volta a limitar os valores recebidos.
    /// </summary>
    public static class XPManager
    {
        /// <summary>A partida conta para progressão? (tempo mínimo e, se pedido, alguma atividade)</summary>
        public static bool IsParticipationValid(XpRewardTable xp, float secondsPlayed, float matchSeconds, bool hadActivity)
        {
            if (xp == null) return false;
            float min = xp.minParticipationSeconds;
            if (matchSeconds > 0f) min = Mathf.Min(min, matchSeconds * xp.minParticipationFraction);
            if (secondsPlayed + 0.01f < min) return false;
            return !xp.requireActivity || hadActivity;
        }

        public static MatchXpBreakdown Compute(ProgressionSettings s, MatchStatLine line)
        {
            var r = new MatchXpBreakdown();
            if (s == null || !line.valid) return r;
            var xp = s.xp;
            int kills = Mathf.Clamp(line.kills, 0, xp.maxKillsCounted);
            int assists = Mathf.Clamp(line.assists, 0, xp.maxKillsCounted);
            int objectives = Mathf.Clamp(line.objectives, 0, xp.maxObjectivesCounted);

            r.participation = Mathf.Max(0, xp.participation);
            r.kills = kills * Mathf.Max(0, xp.kill);
            r.assists = assists * Mathf.Max(0, xp.assist);
            r.victory = line.victory ? Mathf.Max(0, xp.victory) : 0;
            r.objectives = objectives * Mathf.Max(0, xp.objective);
            r.total = r.participation + r.kills + r.assists + r.victory + r.objectives;
            r.brawlerXp = Mathf.RoundToInt(r.total * Mathf.Max(0f, s.brawlerXpShare));

            if (line.usedPet)
                r.petXp = Mathf.Max(0, s.petXp.participation + (line.victory ? s.petXp.victory : 0) + kills * s.petXp.perKill);

            var c = s.coins;
            r.coins = Mathf.Clamp(c.participation + (line.victory ? c.victory : 0) + kills * c.perKill, 0, Mathf.Max(0, c.maxPerMatch));
            return r;
        }

        /// <summary>XP máximo que uma partida pode dar (o cliente rejeita mensagens acima disto).</summary>
        public static int MaxMatchXp(ProgressionSettings s)
        {
            if (s == null) return 0;
            var xp = s.xp;
            return xp.participation + xp.maxKillsCounted * (xp.kill + xp.assist) + xp.victory + xp.maxObjectivesCounted * xp.objective;
        }

        public static LevelChange AddXp(LevelCurve curve, ref int totalXp, int amount)
        {
            var change = new LevelChange { oldTotal = totalXp, oldLevel = curve.LevelForTotal(totalXp) };
            long sum = (long)Mathf.Max(0, totalXp) + Mathf.Max(0, amount);
            totalXp = (int)Mathf.Min(curve.MaxTotalXp, sum);
            change.newTotal = totalXp;
            change.newLevel = curve.LevelForTotal(totalXp);
            return change;
        }

        /// <summary>Progresso 0..1 dentro do nível atual (1 no nível máximo).</summary>
        public static float LevelProgress(LevelCurve curve, int totalXp)
        {
            int level = curve.LevelForTotal(totalXp, out int into, out int next);
            if (level >= curve.maxLevel || next <= 0) return 1f;
            return Mathf.Clamp01(into / (float)next);
        }
    }
}
