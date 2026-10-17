using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Host: estatísticas de cada jogador na partida atual (eliminações, assistências, dano, headshots, objetivos,
    /// tempo jogado) e cálculo da recompensa no fim. Só o host decide XP; cada cliente recebe apenas a sua linha.
    /// Cada partida tem uma chave única: o cliente nunca credita a mesma chave duas vezes (reconexões, mensagens
    /// repetidas, ecrã de resultados reaberto). Bots não recebem recompensas.
    /// </summary>
    public static class MatchStatsTracker
    {
        class Line
        {
            public bool isBot;
            public float joinTime;
            public float leaveTime = -1f;
            public int kills, deaths, assists, objectives, headshots, damage;
            public int brawlerIndex = -1;
            public short petIndex = -1;
            public bool usedPet;
            public bool showdownTopObjective;
            /// <summary>Atacante → última vez que causou dano/efeito a este jogador.</summary>
            public readonly Dictionary<ulong, float> lastHitBy = new Dictionary<ulong, float>();
        }

        static readonly Dictionary<ulong, Line> s_Lines = new Dictionary<ulong, Line>();
        static string s_MatchKey = "";
        static float s_MatchStart;
        static bool s_Finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Lines.Clear();
            s_MatchKey = "";
            s_Finished = false;
        }

        public static string MatchKey => s_MatchKey;
        public static bool IsFinished => s_Finished;

        static ProgressionSettings Settings => GameExperienceDatabase.Instance != null ? GameExperienceDatabase.Instance.Settings : ProgressionSettings.CreateDefault();

        public static void ServerStartMatch()
        {
            s_Lines.Clear();
            s_MatchKey = Guid.NewGuid().ToString("N");
            s_MatchStart = Time.time;
            s_Finished = false;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned) ServerOnJoin(p);
        }

        public static void ServerOnJoin(PlayerNetwork p)
        {
            if (p == null) return;
            if (!s_Lines.TryGetValue(p.Id, out var line))
            {
                line = new Line { joinTime = Time.time };
                s_Lines[p.Id] = line;
            }
            line.isBot = p.IsBot;
            line.leaveTime = -1f;
            RefreshLoadout(p, line);
        }

        public static void ServerOnLeave(PlayerNetwork p)
        {
            if (p != null && s_Lines.TryGetValue(p.Id, out var line)) line.leaveTime = Time.time;
        }

        static Line Get(PlayerNetwork p)
        {
            if (p == null) return null;
            if (!s_Lines.TryGetValue(p.Id, out var line))
            {
                line = new Line { joinTime = Time.time, isBot = p.IsBot };
                s_Lines[p.Id] = line;
            }
            return line;
        }

        static void RefreshLoadout(PlayerNetwork p, Line line)
        {
            line.brawlerIndex = p.HeroId.Value;
            var l = p.Loadout.Value;
            line.petIndex = l.PetIndex;
            if (l.HasPet) line.usedPet = true;
        }

        /// <summary>Dano (ou efeito, com dealt = 0) de um jogador a outro. Chamado pelo EffectResolver.</summary>
        public static void ServerOnDamage(PlayerNetwork attacker, PlayerNetwork victim, int dealt, bool headshot)
        {
            if (s_Finished || attacker == null || victim == null || attacker == victim) return;
            if (MatchManager.AreTeammates(attacker, victim)) return;
            var a = Get(attacker);
            var v = Get(victim);
            if (dealt > 0)
            {
                a.damage += dealt;
                if (headshot) a.headshots++;
            }
            v.lastHitBy[attacker.Id] = Time.time;
            RefreshLoadout(attacker, a);
        }

        /// <summary>Morte. Assistências: quem causou dano à vítima na janela configurada (exceto quem matou).</summary>
        public static void ServerOnKill(PlayerNetwork killer, PlayerNetwork victim)
        {
            if (s_Finished || victim == null) return;
            var v = Get(victim);
            v.deaths++;
            bool validKiller = killer != null && killer != victim && !MatchManager.AreTeammates(killer, victim);
            if (validKiller) Get(killer).kills++;

            float window = Settings.xp.assistWindowSeconds;
            foreach (var kv in v.lastHitBy)
            {
                if (validKiller && kv.Key == killer.Id) continue;
                if (Time.time - kv.Value > window) continue;
                var helper = PlayerNetwork.Find(kv.Key);
                if (helper == null || helper == victim || MatchManager.AreTeammates(helper, victim)) continue;
                Get(helper).assists++;
            }
            v.lastHitBy.Clear();
        }

        public static void ServerOnObjective(PlayerNetwork p, int amount = 1)
        {
            if (s_Finished || p == null || amount <= 0) return;
            Get(p).objectives += amount;
        }

        /// <summary>Showdown: chegar ao top 2 conta uma vez como objetivo.</summary>
        public static void ServerOnShowdownTop(PlayerNetwork p)
        {
            if (s_Finished || p == null) return;
            var line = Get(p);
            if (line.showdownTopObjective) return;
            line.showdownTopObjective = true;
            line.objectives++;
        }

        /// <summary>Fim da partida: calcula e envia a recompensa a cada humano ligado. Só corre uma vez por partida.</summary>
        public static void ServerFinishMatch(Func<PlayerNetwork, bool> isWinner, GameMode mode, MatchRuleset ruleset)
        {
            if (s_Finished || string.IsNullOrEmpty(s_MatchKey)) return;
            s_Finished = true;
            var settings = Settings;
            float matchSeconds = Mathf.Max(1f, Time.time - s_MatchStart);

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.IsBot) continue;
                var line = Get(p);
                RefreshLoadout(p, line);
                float played = Mathf.Clamp(Time.time - Mathf.Max(line.joinTime, s_MatchStart), 0f, matchSeconds);
                bool activity = line.damage > 0 || line.kills > 0 || line.assists > 0 || line.objectives > 0;
                bool victory = isWinner != null && isWinner(p);

                var stat = new MatchStatLine
                {
                    valid = XPManager.IsParticipationValid(settings.xp, played, matchSeconds, activity),
                    victory = victory,
                    kills = line.kills,
                    deaths = line.deaths,
                    assists = line.assists,
                    objectives = line.objectives,
                    headshots = line.headshots,
                    damage = line.damage,
                    seconds = played,
                    usedPet = line.usedPet,
                };
                var xp = XPManager.Compute(settings, stat);

                var msg = new MatchRewardMessage
                {
                    MatchKey = new FixedString64Bytes(s_MatchKey),
                    BrawlerIndex = line.brawlerIndex,
                    PetIndex = line.petIndex,
                    Valid = (byte)(stat.valid ? 1 : 0),
                    Victory = (byte)(victory ? 1 : 0),
                    Ruleset = (byte)ruleset,
                    Mode = (byte)mode,
                    Kills = (short)Mathf.Min(short.MaxValue, line.kills),
                    Deaths = (short)Mathf.Min(short.MaxValue, line.deaths),
                    Assists = (short)Mathf.Min(short.MaxValue, line.assists),
                    Objectives = (short)Mathf.Min(short.MaxValue, line.objectives),
                    Headshots = (short)Mathf.Min(short.MaxValue, line.headshots),
                    Damage = line.damage,
                    Seconds = played,
                    XpParticipation = xp.participation,
                    XpKills = xp.kills,
                    XpAssists = xp.assists,
                    XpVictory = xp.victory,
                    XpObjectives = xp.objectives,
                    XpTotal = xp.total,
                    BrawlerXp = xp.brawlerXp,
                    PetXp = xp.petXp,
                    Coins = xp.coins,
                    UsedPet = (byte)(line.usedPet ? 1 : 0),
                };
                p.ServerSendMatchReward(msg);
            }
        }
    }
}
