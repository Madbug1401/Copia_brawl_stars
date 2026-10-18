using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Modo IT TAKES TWO (cooperativo a dois). Os dois jogadores (equipa 0) enfrentam ondas de bots (equipa 1).
    ///  • Ondas: abrem caminho até ao puzzle das duas placas e à Gem dourada (CoopStory).
    ///  • Ninguém reaparece sozinho: quem cai fica no chão até o parceiro manter a interação, imóvel e sem sofrer dano.
    ///  • Se os dois estiverem no chão ao mesmo tempo, perdem os dois. Limpar uma onda levanta e cura toda a gente.
    ///  • Laço: perto um do outro, os dois curam devagar e fazem mais dano. Separados, ficam mais fracos.
    ///  • A jogar sozinho, o host recebe um bot aliado como parceiro (sai quando entra um 2.º humano).
    /// Tudo decidido no host; os clientes só leem as NetworkVariables para o HUD.
    /// </summary>
    public partial class MatchManager
    {
        [Header("IT TAKES TWO (cooperativo)")]
        public int coopTotalWaves = 5;
        public float coopFirstWaveDelay = 14f;
        public float coopIntermissionSeconds = 12f;
        [Tooltip("Segundos que o parceiro tem de ficar perto para levantar quem caiu.")]
        public float coopReviveSeconds = 3f;
        public float coopReviveRadius = 2.8f;
        [Range(0.1f, 1f)] public float coopReviveHealthPercent = 0.5f;
        [Tooltip("Distância máxima entre os dois para o Laço estar ativo.")]
        public float coopBondRadius = 10f;
        public float coopBondDamageMultiplier = 1.25f;
        public float coopBondRegenPercentPerSecond = 0.03f;

        public readonly NetworkVariable<int> CoopWave = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> CoopEnemiesLeft = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> CoopVictory = new NetworkVariable<bool>(false);

        public bool IsCoop => IsSpawned && Mode.Value == GameMode.ItTakesTwo;
        public static bool IsCoopActive => Instance != null && Instance.IsCoop;

        ulong m_CoopAllyId = ulong.MaxValue;
        double m_CoopAllyCheckAt;
        readonly Dictionary<ulong, float> m_CoopRegenCarry = new Dictionary<ulong, float>();
        static readonly List<PlayerNetwork> s_CoopTmp = new List<PlayerNetwork>();

        // ================================================================== consultas (servidor e clientes)

        /// <summary>Equipa dos jogadores (humanos + bot aliado).</summary>
        public static bool IsCoopTeam(PlayerNetwork p) => p != null && p.IsSpawned && p.TeamId.Value == 0;

        static float FlatDistance(PlayerNetwork a, PlayerNetwork b)
        {
            var d = a.transform.position - b.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>Há um parceiro vivo perto o suficiente para o Laço?</summary>
        public bool HasBondPartner(PlayerNetwork p)
        {
            if (!IsCoop || !IsCoopTeam(p) || p.Health.IsDead) return false;
            foreach (var o in PlayerNetwork.All)
                if (o != p && IsCoopTeam(o) && !o.Health.IsDead && FlatDistance(p, o) <= coopBondRadius)
                    return true;
            return false;
        }

        /// <summary>Servidor: dano com o bónus do Laço (só a equipa dos jogadores).</summary>
        public static int ScaleDamage(PlayerNetwork attacker, int amount)
        {
            var m = Instance;
            if (m == null || !m.IsCoop || !IsCoopTeam(attacker)) return amount;
            return m.HasBondPartner(attacker) ? Mathf.RoundToInt(amount * m.coopBondDamageMultiplier) : amount;
        }

        /// <summary>Parceiro caído mais próximo (para o bot aliado ir levantar e para o HUD).</summary>
        public static PlayerNetwork FindDownedTeammate(PlayerNetwork self, float maxDistance = 60f)
        {
            if (!IsCoopActive || !IsCoopTeam(self)) return null;
            PlayerNetwork best = null;
            float bestD = maxDistance;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == self || !IsCoopTeam(p) || !p.Health.IsDead) continue;
                float d = FlatDistance(self, p);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        // ================================================================== ciclo (servidor)

        void ServerCoopAssignTeams()
        {
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                int team = !p.IsBot || p.Id == m_CoopAllyId ? 0 : 1;
                if (p.TeamId.Value != team) p.TeamId.Value = team;
            }
        }

        void ServerCoopStart()
        {
            ServerCoopDespawnEnemies(false);
            CoopWave.Value = 0;
            CoopEnemiesLeft.Value = 0;
            CoopVictory.Value = false;
            m_CoopRegenCarry.Clear();
            ServerCoopStoryReset();
            CombatSystem.Instance?.ClearAll();
            ServerCoopAssignTeams();

            double now = NetworkManager.ServerTime.Time;
            IsRoundIntermission.Value = true;
            PhaseEndTime.Value = now + Mathf.Max(14f, coopFirstWaveDelay);
            m_CoopAllyCheckAt = now + 1.5;

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                p.Health.ServerRespawn();
                p.Combat.ServerRefill();
            }
        }

        void ServerCoopUpdate()
        {
            double now = NetworkManager.ServerTime.Time;
            ServerCoopAssignTeams();

            if (Phase.Value == MatchPhase.Ended)
            {
                if (now >= PhaseEndTime.Value) ServerStartMatch();
                return;
            }

            ServerCoopPartner(now);
            ServerCoopRevives();
            ServerCoopBondRegen();

            int team = 0, teamAlive = 0;
            foreach (var p in PlayerNetwork.All)
                if (IsCoopTeam(p)) { team++; if (!p.Health.IsDead) teamAlive++; }
            if (team > 0 && teamAlive == 0) { ServerCoopEnd(false); return; }

            if (CoopStage.Value >= CoopMissionStage.Puzzle)
            {
                ServerCoopObjectiveUpdate();
                return;
            }

            if (IsRoundIntermission.Value)
            {
                if (now < PhaseEndTime.Value) return;
                ServerCoopStartWave(CoopWave.Value + 1);
                return;
            }

            int enemies = 0;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.TeamId.Value == 1 && !p.Health.IsDead) enemies++;
            }

            if (CoopEnemiesLeft.Value != enemies) CoopEnemiesLeft.Value = enemies;

            if (enemies == 0) ServerCoopWaveCleared();
        }

        void ServerCoopStartWave(int wave)
        {
            ServerCoopDespawnEnemies(true);
            CoopWave.Value = wave;
            CoopStage.Value = wave == 1 ? CoopMissionStage.Infiltration : CoopMissionStage.Resistance;

            var difficulty = wave <= 2 ? BotDifficulty.Facil : wave <= 4 ? BotDifficulty.Normal : BotDifficulty.Dificil;
            int room = BotManager.MaxTotalPlayers - PlayerNetwork.All.Count;
            int count = Mathf.Clamp(wave + 1, 1, Mathf.Max(1, room));
            int spawned = 0;
            for (int i = 0; i < count; i++)
                if (BotManager.SpawnBot(-1, difficulty) != null) spawned++;

            ServerCoopAssignTeams();
            CoopEnemiesLeft.Value = spawned;
            IsRoundIntermission.Value = false;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time;
            CoopAnnounceRpc(new FixedString64Bytes("ONDA " + wave + " — " + spawned + " inimigos!"), false);
        }

        void ServerCoopWaveCleared()
        {
            ServerCoopDespawnEnemies(true);

            // Recompensa: quem estava no chão levanta-se e todos ficam com a vida cheia
            foreach (var p in PlayerNetwork.All)
            {
                if (!IsCoopTeam(p)) continue;
                if (p.Health.IsDead) p.Health.ServerReviveInPlace(1f);
                else p.Health.Health.Value = p.Health.MaxHealth;
                p.Combat.ServerRefill();
            }

            if (CoopWave.Value >= coopTotalWaves)
            {
                ServerCoopBeginPuzzle();
                return;
            }

            IsRoundIntermission.Value = true;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + coopIntermissionSeconds;
            ServerCoopSetStory(Mathf.Clamp(CoopWave.Value, 1, 4));
            CoopAnnounceRpc(new FixedString64Bytes("ONDA " + CoopWave.Value + " LIMPA!"), true);
        }

        void ServerCoopEnd(bool victory)
        {
            CoopVictory.Value = victory;
            Phase.Value = MatchPhase.Ended;
            IsRoundIntermission.Value = false;
            WinnerName.Value = new FixedString64Bytes(victory ? "Vitória a dois" : "Derrota");
            ServerCoopSetStory(victory ? 8 : 9);
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + Mathf.Max(resultsSeconds, 14f);
        }

        /// <summary>Sozinho? Dá um bot parceiro. Entrou um 2.º humano? O bot parceiro sai.</summary>
        void ServerCoopPartner(double now)
        {
            if (now < m_CoopAllyCheckAt) return;
            m_CoopAllyCheckAt = now + 1.0;
            ServerCoopAssignStoryRoles();

            int humans = 0;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && !p.IsBot) humans++;

            var ally = PlayerNetwork.Find(m_CoopAllyId);
            if (ally == null) m_CoopAllyId = ulong.MaxValue;

            if (humans >= 2 && ally != null)
            {
                m_CoopAllyId = ulong.MaxValue;
                ally.NetworkObject.Despawn(true);
            }
            else if (humans == 1 && ally == null)
            {
                var bot = BotManager.SpawnBot(CoopPartnerHero(), BotDifficulty.Normal, "Parceiro");
                if (bot != null)
                {
                    m_CoopAllyId = bot.Id;
                    ServerCoopAssignTeams();
                }
            }
            else if (ally != null && ally.HeroId.Value != CoopPartnerHero())
                ally.ChangeHero(CoopPartnerHero());
        }

        void ServerCoopRevives()
        {
            float dt = Time.deltaTime;
            foreach (var down in PlayerNetwork.All)
            {
                if (down == null || !down.IsSpawned) continue;
                var hp = down.Health;
                if (!IsCoopTeam(down) || !hp.IsDead)
                {
                    if (hp.ReviveProgress.Value != 0f) hp.ReviveProgress.Value = 0f;
                    continue;
                }

                bool helped = false;
                foreach (var h in PlayerNetwork.All)
                    if (h != down && IsCoopTeam(h) && !h.Health.IsDead && FlatDistance(h, down) <= coopReviveRadius &&
                        Mathf.Abs(h.transform.position.y - down.transform.position.y) < 2f &&
                        CoopIsHelping(h) && !h.Motor.IsMoving && Time.time - h.Health.LastDamageTime > 0.4f &&
                        CombatMath.HasLineOfSight(h, down))
                    {
                        helped = true;
                        break;
                    }

                float step = dt / Mathf.Max(0.1f, coopReviveSeconds);
                float progress = helped ? hp.ReviveProgress.Value + step : 0f;
                if (progress >= 1f)
                {
                    hp.ServerReviveInPlace(coopReviveHealthPercent);
                    CoopAnnounceRpc(new FixedString64Bytes(down.DisplayName + " levantou-se!"), true);
                    continue;
                }
                if (!Mathf.Approximately(progress, hp.ReviveProgress.Value)) hp.ReviveProgress.Value = progress;
            }
        }

        void ServerCoopBondRegen()
        {
            float dt = Time.deltaTime;
            foreach (var p in PlayerNetwork.All)
            {
                if (!IsCoopTeam(p) || p.Health.IsDead) continue;
                int max = p.Health.MaxHealth;
                if (p.Health.Health.Value >= max || !HasBondPartner(p)) continue;

                m_CoopRegenCarry.TryGetValue(p.Id, out float carry);
                carry += max * coopBondRegenPercentPerSecond * dt;
                int add = Mathf.FloorToInt(carry);
                if (add > 0)
                {
                    carry -= add;
                    p.Health.Health.Value = Mathf.Min(max, p.Health.Health.Value + add);
                }
                m_CoopRegenCarry[p.Id] = carry;
            }
        }

        /// <summary>Despawn dos bots inimigos (todos, ou só os que já caíram). O bot parceiro fica.</summary>
        void ServerCoopDespawnEnemies(bool onlyDead)
        {
            s_CoopTmp.Clear();
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.IsBot && p.Id != m_CoopAllyId && (!onlyDead || p.Health.IsDead))
                    s_CoopTmp.Add(p);
            foreach (var p in s_CoopTmp)
                if (p != null && p.IsSpawned) p.NetworkObject.Despawn(true);
            s_CoopTmp.Clear();
        }

        [Rpc(SendTo.Everyone)]
        void CoopAnnounceRpc(FixedString64Bytes text, bool good)
        {
            GameUI.Toast(text.ToString(), good ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.75f, 0.3f));
        }
    }
}
