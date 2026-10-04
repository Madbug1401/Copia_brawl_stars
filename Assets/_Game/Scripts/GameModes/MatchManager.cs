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

    /// <summary>
    /// Deathmatch (todos contra todos): primeiro a chegar ao limite de kills ou quem tiver mais kills quando o tempo acaba.
    /// Depois de uns segundos de resultado, a partida recomeça sozinha.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public int killLimit = 15;
        public float matchMinutes = 8f;
        public float resultsSeconds = 8f;

        public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Playing);
        public readonly NetworkVariable<double> PhaseEndTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<FixedString64Bytes> WinnerName = new NetworkVariable<FixedString64Bytes>();

        public static bool IsMatchOver => Instance != null && Instance.IsSpawned && Instance.Phase.Value == MatchPhase.Ended;

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

            double now = NetworkManager.ServerTime.Time;
            if (now < PhaseEndTime.Value) return;

            if (Phase.Value == MatchPhase.Playing) ServerEndMatch(TopPlayer());
            else ServerStartMatch();
        }

        void ServerStartMatch()
        {
            Phase.Value = MatchPhase.Playing;
            WinnerName.Value = default;
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + matchMinutes * 60f;
            DestructibleCover.ServerRestoreAll();

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                p.Kills.Value = 0;
                p.Deaths.Value = 0;
                p.Combat.ServerResetSuper();
                p.Health.ServerRespawn();
            }
        }

        void ServerEndMatch(PlayerNetwork winner)
        {
            Phase.Value = MatchPhase.Ended;
            WinnerName.Value = new FixedString64Bytes(winner != null ? winner.DisplayName : "Ninguém");
            PhaseEndTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;
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

        /// <summary>Servidor: chamado pelo PlayerHealth quando alguém morre.</summary>
        public void ServerOnKill(PlayerNetwork killer, PlayerNetwork victim)
        {
            var killerName = killer != null && killer != victim ? killer.DisplayName : "";
            KillFeedRpc(new FixedString64Bytes(killerName), new FixedString64Bytes(victim != null ? victim.DisplayName : "?"));

            if (Phase.Value == MatchPhase.Playing && killer != null && killer != victim && killer.Kills.Value >= killLimit)
                ServerEndMatch(killer);
        }

        [Rpc(SendTo.Everyone)]
        void KillFeedRpc(FixedString64Bytes killer, FixedString64Bytes victim)
        {
            GameUI.AddKillFeed(killer.ToString(), victim.ToString());
        }
    }
}
