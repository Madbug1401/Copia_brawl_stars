using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Compensação de latência (só no host).
    ///
    /// Um cliente vê os outros jogadores com atraso (viagem até ele + buffer de interpolação) e o seu tiro ainda
    /// demora a chegar ao host. Sem compensação, o host verificava o tiro contra as posições ATUAIS e o cliente tinha
    /// de apontar à frente do alvo (≈2 m a correr com 200 ms de ping), enquanto o host acertava no que via.
    ///
    /// Com compensação: o cliente envia a hora a que estava a ver os outros (<see cref="PlayerMotor.RenderTime"/>);
    /// enquanto o host testa colisões desse ataque, recua todos os outros jogadores para onde estavam nessa hora
    /// (histórico de 1 s em <see cref="PlayerMotor.TryGetPositionAt"/>) e repõe-nos logo a seguir. Projéteis e cones
    /// continuam a usar o mesmo atraso enquanto voam, por isso o resultado coincide com o ecrã de quem disparou.
    ///
    /// O recuo tem um limite (<see cref="MaxRewind"/>): acima disso, quem tem ping muito alto volta a ter de
    /// antecipar um pouco, em vez de os outros serem atingidos "atrás das paredes" demasiado tarde.
    /// </summary>
    public static class LagCompensation
    {
        /// <summary>Recuo máximo (s). Cobre ping até ~250 ms + buffer de interpolação.</summary>
        public const double MaxRewind = 0.4;

        /// <summary>Testes A/B: "-nolagcomp" na linha de comando do host desliga a compensação.</summary>
        public static readonly bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nolagcomp") < 0;

        static readonly List<(Transform transform, Vector3 position)> s_Saved = new List<(Transform, Vector3)>(16);
        static readonly List<PlayerMotor> s_Leaners = new List<PlayerMotor>(16);
        static int s_Depth;

        /// <summary>Host: atraso a compensar para um ataque de 'shooter' que estava a ver os outros na hora 'viewTime'.</summary>
        public static double LagFor(PlayerNetwork shooter, double viewTime)
        {
            var nm = shooter != null ? shooter.NetworkManager : null;
            if (nm == null || !nm.IsServer || viewTime <= 0) return 0;
            // O host (e os bots dele) vê e testa no mesmo PC: nada a compensar
            if (shooter.OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId) return 0;
            double lag = nm.ServerTime.Time - viewTime;
            if (double.IsNaN(lag) || lag < 0) return 0;
            return lag > MaxRewind ? MaxRewind : lag;
        }

        /// <summary>
        /// Recua todos os jogadores exceto 'shooter' para a hora atual − 'lag'. Tem de ser seguido de <see cref="End"/>.
        /// Devolve falso (e não faz nada) quando não há atraso a compensar.
        /// </summary>
        public static bool Begin(PlayerNetwork shooter, double lag)
        {
            if (!Enabled || lag <= 0.0005 || shooter == null) return false;
            var nm = shooter.NetworkManager;
            if (nm == null || !nm.IsServer) return false;
            if (s_Depth++ > 0) return true; // já recuado (chamadas encaixadas)

            double time = nm.ServerTime.Time - lag;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == shooter || !p.IsSpawned || p.Motor == null) continue;
                if (!p.Motor.TryGetPositionAt(time, out var past)) continue;
                var t = p.transform;
                s_Saved.Add((t, t.position));
                t.position = past;
                // A inclinação (Q/E) também recua: cabeça e ombro onde estavam no ecrã de quem disparou
                p.Motor.LagRewindLean(time);
                s_Leaners.Add(p.Motor);
            }
            Physics.SyncTransforms();
            return true;
        }

        public static void End()
        {
            if (s_Depth <= 0) return;
            if (--s_Depth > 0) return;
            for (int i = s_Saved.Count - 1; i >= 0; i--)
            {
                var (t, pos) = s_Saved[i];
                if (t != null) t.position = pos;
            }
            s_Saved.Clear();
            foreach (var m in s_Leaners) if (m != null) m.LagRestoreLean();
            s_Leaners.Clear();
            Physics.SyncTransforms();
        }
    }
}
