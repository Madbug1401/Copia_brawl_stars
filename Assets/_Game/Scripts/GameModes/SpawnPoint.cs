using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Ponto de spawn. Escolhe-se o mais afastado dos inimigos vivos (com alguma variação).
    /// Em modos de equipa, se o mapa tiver spawns marcados com equipa, cada jogador só usa os da sua.</summary>
    public class SpawnPoint : MonoBehaviour
    {
        [Tooltip("-1 = qualquer equipa; 0 = azul; 1 = vermelha.")]
        public int team = -1;

        static readonly List<SpawnPoint> s_All = new List<SpawnPoint>();
        static readonly List<KeyValuePair<float, SpawnPoint>> s_Scored = new List<KeyValuePair<float, SpawnPoint>>();

        /// <summary>Spawns ativos na cena.</summary>
        public static IReadOnlyList<SpawnPoint> All => s_All;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_All.Clear(); s_Reserved.Clear(); s_ReservedAt.Clear(); }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        public static void Pick(PlayerNetwork forPlayer, out Vector3 position, out float yaw)
        {
            if (s_All.Count == 0)
            {
                position = new Vector3(0f, 3f, 0f);
                yaw = 0f;
                return;
            }

            // Cooperativo: os dois jogadores reaparecem juntos (no ponto mais perto do parceiro vivo)
            if (MatchManager.IsCoopActive && MatchManager.IsCoopTeam(forPlayer))
            {
                PlayerNetwork mate = null;
                foreach (var p in PlayerNetwork.All)
                    if (p != forPlayer && MatchManager.IsCoopTeam(p) && !p.Health.IsDead) { mate = p; break; }
                if (mate != null)
                {
                    SpawnPoint nearest = null;
                    float best = float.MaxValue;
                    foreach (var sp in s_All)
                    {
                        float d = Vector3.Distance(sp.transform.position, mate.transform.position);
                        if (d < best) { best = d; nearest = sp; }
                    }
                    position = ClearCoopPosition(nearest.transform.position, forPlayer);
                    yaw = nearest.transform.eulerAngles.y;
                    return;
                }
            }
            // Spawns da equipa do jogador (se o mapa os tiver e o modo for por equipas)
            int myTeam = -1;
            if (forPlayer != null && MatchManager.Instance != null && MatchManager.Instance.IsSpawned &&
                MatchManager.IsTeamMode(MatchManager.Instance.Mode.Value))
                myTeam = forPlayer.TeamId.Value;
            bool teamSpawns = false;
            if (myTeam >= 0)
                foreach (var sp in s_All)
                    if (sp.team == myTeam) { teamSpawns = true; break; }

            s_Scored.Clear();
            foreach (var sp in s_All)
            {
                if (teamSpawns && sp.team != myTeam) continue;
                float minDist = 999f;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || p == forPlayer || !p.IsSpawned || p.Health.IsDead) continue;
                    // Cooperativo: os inimigos nascem longe dos jogadores (ignoram-se uns aos outros)
                    if (MatchManager.IsCoopActive && !MatchManager.IsCoopTeam(forPlayer) && !MatchManager.IsCoopTeam(p)) continue;
                    minDist = Mathf.Min(minDist, Vector3.Distance(p.transform.position, sp.transform.position));
                }
                s_Scored.Add(new KeyValuePair<float, SpawnPoint>(minDist, sp));
            }

            s_Scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            int pickFrom = Mathf.Min(3, s_Scored.Count);
            var chosen = s_Scored[Random.Range(0, pickFrom)].Value;

            position = MatchManager.IsCoopActive && MatchManager.IsCoopTeam(forPlayer) ? ClearCoopPosition(chosen.transform.position, forPlayer) : chosen.transform.position;
            yaw = chosen.transform.eulerAngles.y;
        }

        static readonly Dictionary<ulong, Vector3> s_Reserved = new Dictionary<ulong, Vector3>();
        static readonly Dictionary<ulong, float> s_ReservedAt = new Dictionary<ulong, float>();

        static bool ClearForPlayer(Vector3 point, PlayerNetwork self)
        {
            foreach (var player in PlayerNetwork.All)
            {
                if (player == null || player == self || !player.IsSpawned || player.Health.IsDead) continue;
                Vector3 d = point - player.transform.position; d.y = 0;
                if (d.sqrMagnitude < 2.2f * 2.2f) return false;
            }
            foreach (var entry in s_Reserved)
                if (self != null && entry.Key != self.Id && Time.unscaledTime - s_ReservedAt[entry.Key] < 2f &&
                    (entry.Value - point).sqrMagnitude < 2.2f * 2.2f) return false;
            foreach (var col in Physics.OverlapCapsule(point + Vector3.up * 0.55f, point + Vector3.up * 1.45f, 0.35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (col.GetComponentInParent<PlayerNetwork>() != self) return false;
            return true;
        }

        static Vector3 ClearCoopPosition(Vector3 origin, PlayerNetwork self)
        {
            for (int ring = 0; ring <= 3; ring++)
                for (int i = 0; i < (ring == 0 ? 1 : 12); i++)
                {
                    float angle = i * Mathf.PI / 6f;
                    Vector3 candidate = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (ring * 2.5f);
                    if (!Physics.Raycast(candidate + Vector3.up * 2f, Vector3.down, out var ground, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (ground.normal.y < 0.7f || ground.collider.GetComponentInParent<PlayerNetwork>() != null) continue;
                    candidate = ground.point + Vector3.up * 0.04f;
                    if (!ClearForPlayer(candidate, self)) continue;
                    if (self != null) { s_Reserved[self.Id] = candidate; s_ReservedAt[self.Id] = Time.unscaledTime; }
                    return candidate;
                }
            foreach (var sp in s_All)
                if (ClearForPlayer(sp.transform.position, self)) return sp.transform.position;
            // No clear location: defer to the original map spawn, never stack vertically.
            return origin;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = team == 0 ? new Color(0.25f, 0.5f, 1f, 0.9f) : team == 1 ? new Color(1f, 0.3f, 0.25f, 0.9f) : new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.8f));
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward * 1.5f);
        }
    }
}
