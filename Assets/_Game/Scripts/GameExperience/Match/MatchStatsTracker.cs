using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Estatísticas de partida guardadas só no host (dano, headshots, objetivos, eliminações sobre bots, tempo e
    /// atividade). No fim da partida o host constrói daqui o resultado autoritativo de cada jogador humano.
    /// </summary>
    public static class MatchStatsTracker
    {
        class Entry
        {
            public double joinTime;
            public int damageDealt, headshots, objectives, killsOnBots, assistsOnBots;
            public Vector3 lastPos;
            public bool hasPos;
            public float moved;
        }

        static readonly Dictionary<ulong, Entry> s_Entries = new Dictionary<ulong, Entry>();
        static double s_MatchStart;
        static bool s_FirstBloodTaken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Entries.Clear();
            s_MatchStart = 0;
            s_FirstBloodTaken = false;
        }

        public static double MatchStart => s_MatchStart;

        public static void ServerBeginMatch(double now)
        {
            s_Entries.Clear();
            s_MatchStart = now;
            s_FirstBloodTaken = false;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned) ServerRegister(p, now);
        }

        /// <summary>Jogador entrou (ou reentrou após desligar-se): a participação conta a partir de agora.</summary>
        public static void ServerRegister(PlayerNetwork p, double now)
        {
            if (p == null) return;
            if (!s_Entries.ContainsKey(p.Id)) s_Entries[p.Id] = new Entry { joinTime = now };
        }

        public static void ServerForget(PlayerNetwork p)
        {
            if (p != null) s_Entries.Remove(p.Id);
        }

        static Entry Get(PlayerNetwork p)
        {
            if (p == null) return null;
            if (!s_Entries.TryGetValue(p.Id, out var e))
            {
                e = new Entry { joinTime = s_MatchStart };
                s_Entries[p.Id] = e;
            }
            return e;
        }

        public static void ServerOnDamage(PlayerNetwork attacker, PlayerNetwork victim, int amount, bool headshot)
        {
            if (attacker == null || attacker == victim || amount <= 0) return;
            var e = Get(attacker);
            e.damageDealt += amount;
            if (headshot) e.headshots++;
        }

        public static void ServerOnKill(PlayerNetwork killer, PlayerNetwork victim)
        {
            if (killer == null || victim == null || killer == victim) return;
            if (victim.IsBot) Get(killer).killsOnBots++;
        }

        public static void ServerOnAssist(PlayerNetwork helper, PlayerNetwork victim)
        {
            if (helper == null || victim == null) return;
            if (victim.IsBot) Get(helper).assistsOnBots++;
        }

        public static void ServerOnObjective(PlayerNetwork p, int count = 1)
        {
            if (p == null || count <= 0) return;
            Get(p).objectives += count;
        }

        /// <summary>Primeira eliminação da partida (objetivo). Devolve true só uma vez por partida.</summary>
        public static bool ServerTryFirstBlood()
        {
            if (s_FirstBloodTaken) return false;
            s_FirstBloodTaken = true;
            return true;
        }

        /// <summary>Amostra de atividade (distância percorrida) — chamada ~1×/s pelo MatchManager.</summary>
        public static void ServerSample()
        {
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                var e = Get(p);
                var pos = p.transform.position;
                if (e.hasPos)
                {
                    float d = Vector3.Distance(pos, e.lastPos);
                    if (d < 30f) e.moved += d; // ignora teletransportes (respawn)
                }
                e.lastPos = pos;
                e.hasPos = true;
            }
        }

        /// <summary>Resultado autoritativo de um jogador (XP e moedas calculados com a configuração do host).</summary>
        public static MatchResultData ServerBuildResult(PlayerNetwork p, string matchId, GameMode mode, bool competitive, bool won, bool draw,
            int placement, double now, ProgressionConfig cfg, PetCatalog pets)
        {
            var e = Get(p);
            float seconds = Mathf.Max(0f, (float)(now - e.joinTime));
            float matchSeconds = Mathf.Max(1f, (float)(now - s_MatchStart));
            var brawler = p.Brawler;
            var loadout = p.Loadout.Value;
            var pet = pets != null ? pets.FindByHash(loadout.petHash) : null;

            var r = new MatchResultData
            {
                matchId = new FixedString64Bytes(matchId ?? ""),
                brawlerId = new FixedString32Bytes(brawler != null ? brawler.brawlerId : ""),
                petHash = pet != null ? pet.StableHash : 0,
                mode = (byte)mode,
                competitive = competitive,
                participated = !p.IsBot && XPManager.IsParticipationValid(cfg, seconds, matchSeconds, e.damageDealt, e.moved),
                won = won,
                draw = draw,
                kills = (short)Mathf.Clamp(p.Kills.Value, 0, short.MaxValue),
                killsOnBots = (short)Mathf.Clamp(e.killsOnBots, 0, short.MaxValue),
                assists = (short)Mathf.Clamp(p.Assists.Value, 0, short.MaxValue),
                assistsOnBots = (short)Mathf.Clamp(e.assistsOnBots, 0, short.MaxValue),
                deaths = (short)Mathf.Clamp(p.Deaths.Value, 0, short.MaxValue),
                objectives = (short)Mathf.Clamp(e.objectives, 0, short.MaxValue),
                headshots = (short)Mathf.Clamp(e.headshots, 0, short.MaxValue),
                placement = (short)Mathf.Clamp(placement, 0, short.MaxValue),
                damageDealt = Mathf.Max(0, e.damageDealt),
                secondsPlayed = (short)Mathf.Clamp(Mathf.RoundToInt(seconds), 0, short.MaxValue),
            };
            XPManager.ComputeRewards(cfg, ref r);
            return r;
        }
    }
}
