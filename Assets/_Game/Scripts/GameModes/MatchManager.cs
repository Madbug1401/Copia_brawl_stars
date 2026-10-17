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
        Showdown
    }

    /// <summary>
    /// Autoridade do host para ciclo de partida e modos. Deathmatch mantém as regras originais;
    /// Wipeout usa pontuação por equipa; Knockout usa rondas; Showdown é último jogador vivo.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }
        public static GameMode MenuSelectedMode { get; set; } = GameMode.Deathmatch;
        /// <summary>Casual (melhorias e passivas de pet contam) ou Competitiva (tudo normalizado). Escolhido no menu do host.</summary>
        public static MatchRuleset MenuSelectedRuleset { get; set; } = MatchRuleset.Casual;

        public int killLimit = 15;
        public float matchMinutes = 8f;
        public float resultsSeconds = 12f;
        public int wipeoutTeamKillLimit = 10;
        public float knockoutRoundSeconds = 90f;
        public float knockoutIntermissionSeconds = 4f;

        public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Playing);
        public readonly NetworkVariable<GameMode> Mode = new NetworkVariable<GameMode>(GameMode.Deathmatch);
        public readonly NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<FixedString64Bytes> WinnerName = new NetworkVariable<FixedString64Bytes>();
        public readonly NetworkVariable<int> BlueScore = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> RedScore = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> RoundNumber = new NetworkVariable<int>(1);
        public readonly NetworkVariable<bool> IsRoundIntermission = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<MatchRuleset> Ruleset = new NetworkVariable<MatchRuleset>(MatchRuleset.Casual);

        /// <summary>Regras de progressão da partida atual (no menu: as escolhidas).</summary>
        public static MatchRuleset CurrentRuleset => Instance != null && Instance.IsSpawned ? Instance.Ruleset.Value : MenuSelectedRuleset;

        public static bool IsMatchOver => Instance != null && Instance.IsSpawned && Instance.Phase.Value == MatchPhase.Ended;
        public static bool IsCombatActive => Instance == null || !Instance.IsSpawned ||
            (Instance.Phase.Value == MatchPhase.Playing && !Instance.IsRoundIntermission.Value);

        public static bool AreTeammates(PlayerNetwork first, PlayerNetwork second)
        {
            if (first == null || second == null || first == second || Instance == null || !Instance.IsSpawned)
                return false;

            if (Instance.Mode.Value != GameMode.Wipeout && Instance.Mode.Value != GameMode.Knockout)
                return false;

            int team = first.TeamId.Value;
            return team >= 0 && team == second.TeamId.Value;
        }

        public bool CanRespawnDeadPlayers => Mode.Value != GameMode.Knockout && Mode.Value != GameMode.Showdown;

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
            Phase.OnValueChanged += OnPhaseChanged;
            if (IsServer) ServerStartMatch();
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= OnPhaseChanged;
        }

        void OnPhaseChanged(MatchPhase previous, MatchPhase current)
        {
            // Nova partida: os clientes também limpam armadilhas/zonas/projéteis da partida anterior
            if (!IsServer && previous == MatchPhase.Ended && current == MatchPhase.Playing && CombatSystem.Instance != null)
                CombatSystem.Instance.ClearAll();
        }

        void Update()
        {
            if (!IsSpawned || !IsServer) return;

            if (Mode.Value == GameMode.Wipeout || Mode.Value == GameMode.Knockout)
                ServerAssignMissingTeams();

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

            if (Mode.Value == GameMode.Wipeout)
            {
                if (BlueScore.Value == RedScore.Value) ServerEndMatch(null);
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
            Mode.Value = MenuSelectedMode;
            Ruleset.Value = MenuSelectedRuleset;
            Phase.Value = MatchPhase.Playing;
            WinnerName.Value = default;
            BlueScore.Value = 0;
            RedScore.Value = 0;
            RoundNumber.Value = 1;
            IsRoundIntermission.Value = false;
            DestructibleCover.ServerRestoreAll();

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                p.Kills.Value = 0;
                p.Deaths.Value = 0;
                p.TeamId.Value = -1;
                p.Combat.ServerResetSuper();
            }

            if (Mode.Value == GameMode.Wipeout || Mode.Value == GameMode.Knockout)
                ServerAssignMissingTeams();

            // Nova chave de partida: as recompensas desta partida só podem ser creditadas uma vez
            MatchStatsTracker.ServerStartMatch();
            if (CombatSystem.Instance != null) CombatSystem.Instance.ClearAll();

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
            if (!IsServer || !IsSpawned ||
                (Mode.Value != GameMode.Wipeout && Mode.Value != GameMode.Knockout))
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
            MatchStatsTracker.ServerFinishMatch(p => winner != null && p == winner, Mode.Value, Ruleset.Value);
        }

        void ServerEndMatch(int winningTeam)
        {
            Phase.Value = MatchPhase.Ended;
            IsRoundIntermission.Value = false;
            string name = winningTeam == 0 ? "Equipa Azul" : winningTeam == 1 ? "Equipa Vermelha" : "Empate";
            WinnerName.Value = new FixedString64Bytes(name);
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;
            MatchStatsTracker.ServerFinishMatch(p => winningTeam >= 0 && p.TeamId.Value == winningTeam, Mode.Value, Ruleset.Value);
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
            MatchStatsTracker.ServerFinishMatch(null, Mode.Value, Ruleset.Value);
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

            // Objetivo: ronda ganha conta para cada jogador da equipa vencedora
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

            if (Mode.Value == GameMode.Showdown && Phase.Value == MatchPhase.Playing && !IsRoundIntermission.Value)
            {
                // Objetivo: chegar ao top 2
                if (AlivePlayerCount <= 2)
                    foreach (var p in PlayerNetwork.All)
                        if (p != null && p.IsSpawned && !p.Health.IsDead) MatchStatsTracker.ServerOnShowdownTop(p);

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
    }
}
