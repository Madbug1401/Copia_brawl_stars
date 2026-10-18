using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Ponto de spawn. Escolhe-se o mais afastado dos inimigos vivos (com alguma variação).</summary>
    public class SpawnPoint : MonoBehaviour
    {
        static readonly List<SpawnPoint> s_All = new List<SpawnPoint>();
        static readonly List<KeyValuePair<float, SpawnPoint>> s_Scored = new List<KeyValuePair<float, SpawnPoint>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();

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
                    position = nearest.transform.position;
                    yaw = nearest.transform.eulerAngles.y;
                    return;
                }
            }

            s_Scored.Clear();
            foreach (var sp in s_All)
            {
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

            position = chosen.transform.position;
            yaw = chosen.transform.eulerAngles.y;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.8f));
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward * 1.5f);
        }
    }
}
