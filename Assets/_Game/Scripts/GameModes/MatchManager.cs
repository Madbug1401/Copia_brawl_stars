using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    public enum MatchPhase : byte
    {
        Playing,
        Ended
    }

    public enum GameMode : byte
    {
        Deathmatch,
        Wipeout,
        Knockout,
        Showdown,
        ItTakesTwo,   // cooperativo a dois contra ondas de bots (MatchManager.Coop.cs)
        GemGrab   // Pique-Gemas: equipas recolhem gemas do gerador (ver GemGrabManager)
    }

    /// <summary>
    /// Autoridade do host para ciclo de partida e modos. Deathmatch mantém as regras originais;
    /// Wipeout usa pontuação por equipa; Knockout usa rondas; Showdown é último jogador vivo;
    /// GemGrab (Pique-Gemas) delega gemas e contagem de vitória no GemGrabManager da cena.
    /// </summary>
    public partial class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }
        public static GameMode MenuSelectedMode { get; set; } = GameMode.Deathmatch;
        /// <summary>Partida competitiva (melhorias normalizadas, pets cosméticos) escolhida no menu pelo host.</summary>
        public static bool MenuSelectedCompetitive { get; set; }

        public int killLimit = 15;
        public float matchMinutes = 8f;
        public float resultsSeconds = 8f;
        public int wipeoutTeamKillLimit = 10;
        public float knockoutRoundSeconds = 90f;
        public float knockoutIntermissionSeconds = 4f;

        [Header("Modo fixo da cena (mapas dedicados, ex.: Pique-Gemas)")]
        [Tooltip("Se ativo, a partida usa sempre 'sceneMode' em vez do modo escolhido no menu.")]
        public bool overrideMenuMode;
        public GameMode sceneMode = GameMode.GemGrab;

        public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Playing);
        public readonly NetworkVariable<GameMode> Mode = new NetworkVariable<GameMode>(GameMode.Deathmatch);
        public readonly NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<FixedString64Bytes> WinnerName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<int> BlueScore = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> RedScore = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> RoundNumber = new NetworkVariable<int>(1);
        public readonly NetworkVariable<bool> IsRoundIntermission = new NetworkVariable<bool>(false);
        /// <summary>Casual (melhorias limitadas ativas) ou competitiva (atributos normalizados).</summary>
        public readonly NetworkVariable<bool> Competitive = new NetworkVariable<bool>(false);
        /// <summary>Id único da partida atual (as recompensas são aplicadas uma vez por id).</summary>
        public readonly NetworkVariable<FixedString64Bytes> MatchId = new NetworkVariable<FixedString64Bytes>();

        // Servidor: recompensas e objetivos
        bool m_RewardsSent;
        float m_NextActivitySample;
        readonly System.Collections.Generic.List<ulong> m_EliminationOrder = new System.Collections.Generic.List<ulong>();

        public static bool IsMatchOver => Instance != null && Instance.IsSpawned && Instance.Phase.Value == MatchPhase.Ended;
        public static bool IsCombatActive => Instance == null || !Instance.IsSpawned ||
            (Instance.Phase.Value == MatchPhase.Playing && !Instance.IsRoundIntermission.Value);

        public static bool IsTeamMode(GameMode mode) =>
            mode == GameMode.Wipeout || mode == GameMode.Knockout || mode == GameMode.GemGrab || mode == GameMode.ItTakesTwo;

        public static bool AreTeammates(PlayerNetwork first, PlayerNetwork second)
        {
            if (first == null || second == null || first == second || Instance == null || !Instance.IsSpawned)
                return false;

            if (!IsTeamMode(Instance.Mode.Value))
                return false;

            int team = first.TeamId.Value;
            return team >= 0 && team == second.TeamId.Value;
        }

        public bool CanRespawnDeadPlayers => Mode.Value != GameMode.Knockout && Mode.Value != GameMode.Showdown &&
            Mode.Value != GameMode.ItTakesTwo;   // no cooperativo é o parceiro que levanta

        public int AlivePlayerCount
        {
            get
            {
                int alive = 0;
                foreach (var p in PlayerNetwork.All)
                    if (p != null && p.IsSpawned && !p.Health.IsDead) alive++;
                return alive;
            }
        }

        public float TimeRemaining
        {
            get
            {
                if (!IsSpawned || NetworkManager == null) return 0f;
                return Mathf.Max(0f, (float)(PhaseEndTime.Value - NetworkManager.ServerTime.Time));
            }
        }

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) ServerStartMatch();
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (Mode.Value == GameMode.ItTakesTwo)
            {
                ServerCoopUpdate();
                return;
            }

            if (IsTeamMode(Mode.Value))
                ServerAssignMissingTeams();

            if (Phase.Value == MatchPhase.Playing && Time.time >= m_NextActivitySample)
            {
                m_NextActivitySample = Time.time + 1f;
                MatchStatsTracker.ServerSample();
            }

            double now = NetworkManager.ServerTime.Time;
            if (now < PhaseEndTime.Value) return;

            if (Phase.Value == MatchPhase.Ended)
            {
                ServerStartMatch();
                return;
            }

            if (Mode.Value == GameMode.Knockout)
            {
                if (IsRoundIntermission.Value)
                {
                    if (BlueScore.Value >= 2 || RedScore.Value >= 2)
                    {
                        ServerEndMatch(BlueScore.Value > RedScore.Value ? 0 : 1);
                        return;
                    }

                    if (RoundNumber.Value >= 3)
                    {
                        if (BlueScore.Value == RedScore.Value) ServerEndMatch(-1);
                        else ServerEndMatch(BlueScore.Value > RedScore.Value ? 0 : 1);
                        return;
                    }

                    RoundNumber.Value++;
                    ServerStartKnockoutRound();
                    return;
                }

                ServerResolveKnockoutRoundByHealth();
                return;
            }

            if (Mode.Value == GameMode.Wipeout || Mode.Value == GameMode.GemGrab)
            {
                // GemGrab: no fim do tempo ganha quem tiver mais gemas (Blue/RedScore = gemas da equipa)
                if (BlueScore.Value == RedScore.Value)
                {
                    if (Mode.Value == GameMode.GemGrab) ServerEndMatch(-1);
                    else ServerEndMatch(null);
                }
                else ServerEndMatch(BlueScore.Value > RedScore.Value ? 0 : 1);
            }
            else if (Mode.Value == GameMode.Showdown)
            {
                ServerResolveShowdown();
            }
            else
            {
                ServerEndMatch(TopPlayer());
            }
        }

        void ServerStartMatch()
        {
            Mode.Value = overrideMenuMode ? sceneMode : MenuSelectedMode;

            Competitive.Value = MenuSelectedCompetitive;
            MatchId.Value = new FixedString64Bytes(System.Guid.NewGuid().ToString("N"));
            Phase.Value = MatchPhase.Playing;
            WinnerName.Value = default;
            BlueScore.Value = 0;
            RedScore.Value = 0;
            RoundNumber.Value = 1;
            IsRoundIntermission.Value = false;
            m_RewardsSent = false;
            m_EliminationOrder.Clear();
            DestructibleCover.ServerRestoreAll();
            MatchStatsTracker.ServerBeginMatch(NetworkManager.ServerTime.Time);

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                p.Kills.Value = 0;
                p.Deaths.Value = 0;
                p.Assists.Value = 0;
                p.TeamId.Value = -1;
                p.Combat.ServerResetSuper();
                // Casual/competitivo pode ter mudado: os atributos efetivos são recalculados
                p.ServerRefreshEffectiveStats();
            }

            if (Mode.Value == GameMode.ItTakesTwo)
            {
                ServerCoopStart();
                return;
            }

            if (IsTeamMode(Mode.Value))
                ServerAssignMissingTeams();

            // Gemas e temporizadores do Pique-Gemas recomeçam do zero em cada partida
            if (GemGrabManager.Instance != null) GemGrabManager.Instance.ServerResetRound();

            if (Mode.Value == GameMode.Knockout)
                ServerStartKnockoutRound();
            else
            {
                PhaseEndTime.Value = NetworkManager.ServerTime.Time + matchMinutes * 60f;
                foreach (var p in PlayerNetwork.All)
                    if (p != null && p.IsSpawned) p.Health.ServerRespawn();
            }
        }

        void ServerStartKnockoutRound()
        {
            Phase.Value = MatchPhase.Playing;
            IsRoundIntermission.Value = false;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + knockoutRoundSeconds;
            DestructibleCover.ServerRestoreAll();

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                p.Health.ServerRespawn();
                p.Combat.ServerRefill();
                p.Combat.ServerResetSuper();
            }
        }

        public void ServerAssignMissingTeams()
        {
            if (IsServer && IsSpawned && Mode.Value == GameMode.ItTakesTwo)
            {
                ServerCoopAssignTeams();
                return;
            }

            if (!IsServer || !IsSpawned || !IsTeamMode(Mode.Value))
                return;

            int blue = 0, red = 0;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.TeamId.Value == 0) blue++;
                else if (p.TeamId.Value == 1) red++;
            }

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.TeamId.Value >= 0) continue;
                p.TeamId.Value = blue <= red ? 0 : 1;
                if (p.TeamId.Value == 0) blue++;
                else red++;
            }
        }

        void ServerEndMatch(PlayerNetwork winner)
        {
            Phase.Value = MatchPhase.Ended;
            IsRoundIntermission.Value = false;
            WinnerName.Value = new FixedString64Bytes(winner != null ? winner.DisplayName : "Ninguém");
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;
            ServerSendRewards(p => winner != null && p == winner, winner == null);
        }

        /// <summary>Servidor: termina a partida com vitória de uma equipa (0 azul, 1 vermelha, -1 empate).</summary>
        public void ServerEndMatchForTeam(int winningTeam)
        {
            if (!IsServer || Phase.Value != MatchPhase.Playing) return;
            ServerEndMatch(winningTeam);
        }

        void ServerEndMatch(int winningTeam)
        {
            Phase.Value = MatchPhase.Ended;
            IsRoundIntermission.Value = false;
            string name = winningTeam == 0 ? "Equipa Azul" : winningTeam == 1 ? "Equipa Vermelha" : "Empate";
            WinnerName.Value = new FixedString64Bytes(name);
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;
            ServerSendRewards(p => winningTeam >= 0 && p.TeamId.Value == winningTeam, winningTeam < 0);
        }

        // ------------------------------------------------------------------ recompensas (autoridade do host)

        /// <summary>
        /// Host: calcula o resultado de cada jogador humano (participação, XP, moedas, XP do pet) e envia-o só ao dono.
        /// Uma vez por partida; o cliente ainda ignora ids de partida repetidos. Bots não recebem progressão.
        /// </summary>
        void ServerSendRewards(System.Func<PlayerNetwork, bool> isWinner, bool draw)
        {
            if (!IsServer || m_RewardsSent) return;
            m_RewardsSent = true;
            double now = NetworkManager.ServerTime.Time;
            var gx = GameExperience.Current;
            string matchId = MatchId.Value.ToString();
            bool showdown = Mode.Value == GameMode.Showdown;
            var placements = showdown ? ShowdownPlacements() : null;

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.IsBot) continue;
                int placement = 0;
                if (placements != null && placements.TryGetValue(p.Id, out var place))
                {
                    placement = place;
                    if (place <= 3) MatchStatsTracker.ServerOnObjective(p); // Showdown: terminar no top 3
                }
                var result = MatchStatsTracker.ServerBuildResult(p, matchId, Mode.Value, Competitive.Value, isWinner(p) && !draw, draw,
                    placement, now, gx.Config, gx.PetCatalog);
                MatchResultRpc(result, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
            }
        }

        /// <summary>Showdown: 1 = último vivo; os restantes pela ordem inversa de eliminação.</summary>
        System.Collections.Generic.Dictionary<ulong, int> ShowdownPlacements()
        {
            var map = new System.Collections.Generic.Dictionary<ulong, int>();
            int place = 1;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && !p.Health.IsDead) map[p.Id] = place++;
            for (int i = m_EliminationOrder.Count - 1; i >= 0; i--)
                if (!map.ContainsKey(m_EliminationOrder[i])) map[m_EliminationOrder[i]] = place++;
            return map;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void MatchResultRpc(MatchResultData result, RpcParams rpcParams = default)
        {
            var report = GameExperience.Current.Progression.ApplyMatchResult(result);
            ProgressionUI.ShowMatchReport(report);
        }

        PlayerNetwork TopPlayer()
        {
            PlayerNetwork best = null;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (best == null || p.Kills.Value > best.Kills.Value ||
                    (p.Kills.Value == best.Kills.Value && p.Deaths.Value < best.Deaths.Value))
                    best = p;
            }
            return best;
        }

        void ServerResolveShowdown()
        {
            PlayerNetwork best = null;
            int bestHealth = -1, bestKills = -1;
            bool tied = false;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                int health = p.Health.Health.Value;
                int kills = p.Kills.Value;
                if (health > bestHealth || (health == bestHealth && kills > bestKills))
                {
                    best = p;
                    bestHealth = health;
                    bestKills = kills;
                    tied = false;
                }
                else if (health == bestHealth && kills == bestKills)
                {
                    tied = true;
                }
            }

            if (best == null) ServerEndDraw();
            else if (AlivePlayerCount == 1 || !tied) ServerEndMatch(best);
            else ServerEndDraw();
        }

        PlayerNetwork LastAlivePlayer()
        {
            PlayerNetwork last = null;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                if (last != null) return null;
                last = p;
            }
            return last;
        }

        void ServerEndDraw()
        {
            Phase.Value = MatchPhase.Ended;
            IsRoundIntermission.Value = false;
            WinnerName.Value = new FixedString64Bytes("Empate");
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;
            ServerSendRewards(p => false, true);
        }

        int AliveCountForTeam(int team)
        {
            int count = 0;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.TeamId.Value == team && !p.Health.IsDead) count++;
            return count;
        }

        int RemainingHealthForTeam(int team)
        {
            int total = 0;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.TeamId.Value == team && !p.Health.IsDead)
                    total += Mathf.Max(0, p.Health.Health.Value);
            return total;
        }

        void ServerResolveKnockoutRoundByHealth()
        {
            int blueAlive = AliveCountForTeam(0);
            int redAlive = AliveCountForTeam(1);
            if (blueAlive > 0 && redAlive == 0) ServerEndKnockoutRound(0);
            else if (redAlive > 0 && blueAlive == 0) ServerEndKnockoutRound(1);
            else
            {
                int blueHealth = RemainingHealthForTeam(0);
                int redHealth = RemainingHealthForTeam(1);
                if (blueHealth > redHealth) ServerEndKnockoutRound(0);
                else if (redHealth > blueHealth) ServerEndKnockoutRound(1);
                else ServerEndKnockoutRound(-1);
            }
        }

        void ServerCheckKnockoutRoundEnd()
        {
            if (Mode.Value != GameMode.Knockout || Phase.Value != MatchPhase.Playing || IsRoundIntermission.Value) return;
            int blueAlive = AliveCountForTeam(0);
            int redAlive = AliveCountForTeam(1);
            if (blueAlive == 0 && redAlive > 0) ServerEndKnockoutRound(1);
            else if (redAlive == 0 && blueAlive > 0) ServerEndKnockoutRound(0);
            else if (blueAlive == 0 && redAlive == 0) ServerEndKnockoutRound(-1);
        }

        void ServerEndKnockoutRound(int winningTeam)
        {
            if (IsRoundIntermission.Value || Phase.Value != MatchPhase.Playing) return;

            if (winningTeam == 0) BlueScore.Value++;
            else if (winningTeam == 1) RedScore.Value++;

            // Objetivo: ronda ganha conta para cada membro da equipa vencedora
            if (winningTeam >= 0)
                foreach (var p in PlayerNetwork.All)
                    if (p != null && p.IsSpawned && p.TeamId.Value == winningTeam) MatchStatsTracker.ServerOnObjective(p);

            if (BlueScore.Value >= 2 || RedScore.Value >= 2)
            {
                ServerEndMatch(BlueScore.Value > RedScore.Value ? 0 : 1);
                return;
            }

            if (RoundNumber.Value >= 3)
            {
                if (BlueScore.Value == RedScore.Value) ServerEndMatch(-1);
                else ServerEndMatch(BlueScore.Value > RedScore.Value ? 0 : 1);
                return;
            }

            IsRoundIntermission.Value = true;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + knockoutIntermissionSeconds;
        }

        /// <summary>Servidor: chamado pelo PlayerHealth quando alguém morre.</summary>
        public void ServerOnKill(PlayerNetwork killer, PlayerNetwork victim)
        {
            var killerName = killer != null && killer != victim ? killer.DisplayName : "";
            KillFeedRpc(new FixedString64Bytes(killerName), new FixedString64Bytes(victim != null ? victim.DisplayName : "?"));

            if (victim != null && Mode.Value == GameMode.Showdown && !m_EliminationOrder.Contains(victim.Id)) m_EliminationOrder.Add(victim.Id);
            // Objetivo: primeira eliminação da partida
            bool validKill = killer != null && killer != victim && !AreTeammates(killer, victim);
            if (validKill && Phase.Value == MatchPhase.Playing && !IsRoundIntermission.Value && MatchStatsTracker.ServerTryFirstBlood())
            {
                MatchStatsTracker.ServerOnObjective(killer);
                FirstBloodRpc(new FixedString64Bytes(killer.DisplayName));
            }
            // Cooperativo: a vitória/derrota é avaliada por ondas no ServerCoopUpdate
            if (Mode.Value == GameMode.ItTakesTwo) return;
            // Pique-Gemas: quem morre larga as gemas que transportava (decidido no host)
            if (Mode.Value == GameMode.GemGrab)
            {
                if (GemGrabManager.Instance != null) GemGrabManager.Instance.ServerOnPlayerDied(victim);
                return; // as mortes não decidem a partida neste modo
            }

            if (Mode.Value == GameMode.Showdown && Phase.Value == MatchPhase.Playing && !IsRoundIntermission.Value)
            {
                if (AlivePlayerCount == 1) ServerEndMatch(LastAlivePlayer());
                else if (AlivePlayerCount == 0) ServerEndDraw();
                return;
            }

            if (AreTeammates(killer, victim))
            {
                ServerCheckKnockoutRoundEnd();
                return;
            }

            if (Phase.Value != MatchPhase.Playing || IsRoundIntermission.Value || killer == null || killer == victim)
            {
                ServerCheckKnockoutRoundEnd();
                return;
            }

            if (Mode.Value == GameMode.Wipeout)
            {
                if (killer.TeamId.Value == 0) BlueScore.Value++;
                else if (killer.TeamId.Value == 1) RedScore.Value++;

                if (BlueScore.Value >= wipeoutTeamKillLimit) ServerEndMatch(0);
                else if (RedScore.Value >= wipeoutTeamKillLimit) ServerEndMatch(1);
            }
            else if (Mode.Value == GameMode.Knockout)
            {
                ServerCheckKnockoutRoundEnd();
            }
            else if (killer.Kills.Value >= killLimit)
            {
                ServerEndMatch(killer);
            }
        }

        [Rpc(SendTo.Everyone)]
        void KillFeedRpc(FixedString64Bytes killer, FixedString64Bytes victim)
        {
            GameUI.AddKillFeed(killer.ToString(), victim.ToString());
        }

        [Rpc(SendTo.Everyone)]
        void FirstBloodRpc(FixedString64Bytes killer)
        {
            GameUI.Toast("PRIMEIRA ELIMINAÇÃO · " + killer.ToString().ToUpperInvariant(), new Color(1f, 0.45f, 0.3f));
        }
    }
}
