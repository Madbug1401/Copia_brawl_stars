using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Uma gema no mapa. Enquanto <see cref="IsAvailable"/> é falso está em voo (arco do gerador ou queda).</summary>
    public struct GemNetState : INetworkSerializable, IEquatable<GemNetState>
    {
        public ushort Id;
        public Vector3 From;
        public Vector3 To;
        public double StartTime;
        public float Duration;
        /// <summary>0 = saiu do gerador · 1 = largada por um jogador que morreu.</summary>
        public byte Source;

        public bool IsAvailable(double now) => now >= StartTime + Duration;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Id);
            s.SerializeValue(ref From);
            s.SerializeValue(ref To);
            s.SerializeValue(ref StartTime);
            s.SerializeValue(ref Duration);
            s.SerializeValue(ref Source);
        }

        public bool Equals(GemNetState o) => Id == o.Id && StartTime.Equals(o.StartTime) && To == o.To;
    }

    /// <summary>Quantas gemas um jogador transporta.</summary>
    public struct GemCarrier : INetworkSerializable, IEquatable<GemCarrier>
    {
        public ulong PlayerId;
        public int Count;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref PlayerId);
            s.SerializeValue(ref Count);
        }

        public bool Equals(GemCarrier o) => PlayerId == o.PlayerId && Count == o.Count;
    }

    /// <summary>
    /// Pique-Gemas. O HOST decide tudo: quando nasce cada gema, onde cai, quem a apanha, quantas cada jogador leva,
    /// o que cai quando alguém morre e a contagem de vitória. O estado vive em NetworkLists, por isso quem entra
    /// a meio recebe as gemas e os portadores atuais. Os clientes só desenham (arco, flutuação, brilho, partículas).
    /// As animações do Blender são só demonstração — aqui o movimento é calculado a partir do estado do host.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class GemGrabManager : NetworkBehaviour
    {
        public static GemGrabManager Instance { get; private set; }

        [Header("Regras (host)")]
        public float firstSpawnDelay = 4f;
        public float spawnInterval = 7f;
        public int maxGemsOnField = 20;
        public int winGems = 10;
        public float holdSeconds = 15f;
        [Tooltip("Distância horizontal máxima entre o jogador e a gema para a apanhar.")]
        public float pickupRadius = 1.2f;
        public float pickupHeight = 2.2f;

        [Header("Gerador")]
        public Transform spawnPoint;
        public Transform[] landingPoints = new Transform[0];
        public float arcSeconds = 1.1f;
        public float arcHeight = 2.2f;
        [Tooltip("Segundos de aviso (brilho + partículas) antes de cada gema.")]
        public float warningSeconds = 1.6f;

        [Header("Queda ao morrer")]
        public float dropArcSeconds = 0.6f;
        public float dropRadius = 1.8f;
        [Tooltip("Altura a que a gema flutua acima do chão.")]
        public float gemFloatHeight = 0.6f;
        [Tooltip("Desnível máximo (m) acima dos pés de referência para uma gema largada/deslocada poder pousar.")]
        public float maxGroundRise = 0.45f;
        [Tooltip("Desnível máximo (m) abaixo dos pés de referência (evita gemas no fundo de buracos/água).")]
        public float maxGroundDrop = 1.0f;

        [Header("Visual (clientes)")]
        public GameObject gemPrefab;
        public Transform emitterHalo;
        public Transform emitterBase;
        public Renderer[] emitterRenderers = new Renderer[0];
        public Color gemColor = new Color(0.72f, 0.42f, 1f);
        public Color emitterIdleEmission = new Color(0.25f, 0.12f, 0.45f);
        public Color emitterWarnEmission = new Color(1.1f, 0.7f, 1.6f);

        public NetworkList<GemNetState> Gems;
        public NetworkList<GemCarrier> Carriers;
        public readonly NetworkVariable<double> NextSpawnTime = new NetworkVariable<double>(0);
        /// <summary>Equipa em contagem para vencer (-1 = nenhuma).</summary>
        public readonly NetworkVariable<int> CountdownTeam = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<double> CountdownEnd = new NetworkVariable<double>(0);

        public static bool IsActive => Instance != null && Instance.IsSpawned && MatchManager.Instance != null &&
            MatchManager.Instance.IsSpawned && MatchManager.Instance.Mode.Value == GameMode.GemGrab;

        ushort m_NextId = 1;
        int m_PendingMirror = -1;
        int[] m_Mirror = new int[0];
        readonly Dictionary<ulong, Vector3> m_LastPos = new Dictionary<ulong, Vector3>();
        // Hora (servidor) a partir da qual cada jogador pode voltar a apanhar gemas depois de reaparecer:
        // a posição do cliente só chega ao host um pouco depois do teletransporte para o spawn.
        readonly Dictionary<ulong, double> m_PickupAllowedAt = new Dictionary<ulong, double>();
        readonly HashSet<ulong> m_WasDead = new HashSet<ulong>();
        [Tooltip("Segundos sem apanhar gemas depois de reaparecer (evita usar a posição antiga do cliente).")]
        public float respawnPickupGrace = 0.75f;
        readonly List<ulong> m_Tmp = new List<ulong>();
        readonly Collider[] m_Overlap = new Collider[16];
        GemGrabVisuals m_Visuals;

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        void Awake()
        {
            Instance = this;
            // NetworkList tem de ser criada antes do spawn (fora do inicializador de campo: evita fugas no editor)
            Gems = new NetworkList<GemNetState>();
            Carriers = new NetworkList<GemCarrier>();
            m_Visuals = GetComponent<GemGrabVisuals>();
            if (m_Visuals == null) m_Visuals = gameObject.AddComponent<GemGrabVisuals>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        bool m_Bound;

        /// <summary>
        /// Liga o gerador ao emissor do mapa em jogo (pacote carregado pelo MapLoader ou mapa da cena dedicada).
        /// Campos já preenchidos no Inspector têm prioridade. Na arena de arranque cria-se um emissor no totem.
        /// </summary>
        void BindToMap()
        {
            // O emissor do mapa em jogo manda; se o mapa mudar (outro pacote carregado), volta a ligar-se.
            var cfg = GemEmitterConfig.Current;
            if (cfg != null && cfg == m_BoundConfig) return;
            if (cfg == null)
            {
                if (m_Bound) return;
                if (spawnPoint != null && landingPoints != null && landingPoints.Length > 0) { m_Bound = true; return; }
                cfg = GemEmitterConfig.EnsureFallback();
                if (cfg == null) return;
            }
            cfg.ApplyTo(this);
            BuildMirrorTable();
            m_BoundConfig = cfg;
            m_Bound = true;
        }

        GemEmitterConfig m_BoundConfig;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            BindToMap();
            BuildMirrorTable();
            if (IsServer) ServerResetRound();
        }

        // ================================================================== consultas (todas as máquinas)

        public int CarriedBy(ulong playerId)
        {
            if (Carriers == null) return 0;
            for (int i = 0; i < Carriers.Count; i++)
                if (Carriers[i].PlayerId == playerId) return Carriers[i].Count;
            return 0;
        }

        public int TeamTotal(int team)
        {
            int total = 0;
            if (Carriers == null) return 0;
            for (int i = 0; i < Carriers.Count; i++)
            {
                var p = PlayerNetwork.Find(Carriers[i].PlayerId);
                if (p != null && p.TeamId.Value == team) total += Carriers[i].Count;
            }
            return total;
        }

        public float CountdownRemaining => CountdownTeam.Value < 0 || NetworkManager == null ? 0f :
            Mathf.Max(0f, (float)(CountdownEnd.Value - NetworkManager.ServerTime.Time));

        /// <summary>Segundos até à próxima gema (negativo = já passou).</summary>
        public float TimeToNextSpawn => NetworkManager == null ? 99f : (float)(NextSpawnTime.Value - NetworkManager.ServerTime.Time);

        // ================================================================== host

        /// <summary>Host: limpa gemas, portadores e temporizadores (início/reinício de partida).</summary>
        public void ServerResetRound()
        {
            if (!IsServer || !IsSpawned) return;
            Gems.Clear();
            Carriers.Clear();
            m_LastPos.Clear();
            m_PickupAllowedAt.Clear();
            m_WasDead.Clear();
            m_PendingMirror = -1;
            CountdownTeam.Value = -1;
            CountdownEnd.Value = 0;
            NextSpawnTime.Value = Now + firstSpawnDelay;
            WriteScores();
        }

        void Update()
        {
            if (IsSpawned) BindToMap();
            if (!IsSpawned || !IsServer) return;

            var match = MatchManager.Instance;
            if (!IsActive)
            {
                if (Gems.Count > 0 || Carriers.Count > 0) ServerResetRound();
                return;
            }
            if (match.Phase.Value != MatchPhase.Playing) return; // ecrã de resultados: congela

            double now = Now;
            TrackPlayers();

            if (now >= NextSpawnTime.Value)
            {
                if (Gems.Count < maxGemsOnField) ServerSpawnFromGenerator(now);
                NextSpawnTime.Value = now + spawnInterval;
            }

            ServerPickups(now);
            WriteScores();
            ServerCountdown(now);
        }

        void TrackPlayers()
        {
            double now = Now;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (p.Health.IsDead) { m_WasDead.Add(p.Id); continue; } // morto: não atualiza a última posição
                if (m_WasDead.Remove(p.Id)) m_PickupAllowedAt[p.Id] = now + respawnPickupGrace;
                m_LastPos[p.Id] = p.transform.position;
            }

            // Portadores que saíram da partida largam as gemas onde foram vistos pela última vez
            for (int i = Carriers.Count - 1; i >= 0; i--)
            {
                var c = Carriers[i];
                if (PlayerNetwork.Find(c.PlayerId) != null) continue;
                Carriers.RemoveAt(i);
                if (c.Count > 0 && m_LastPos.TryGetValue(c.PlayerId, out var last)) ServerDropGems(last, c.Count);
                m_LastPos.Remove(c.PlayerId);
            }
        }

        void ServerSpawnFromGenerator(double now)
        {
            if (spawnPoint == null || landingPoints == null || landingPoints.Length == 0) return;
            int land = NextLandingIndex();
            Vector3 to = FreeSpotNear(landingPoints[land].position);
            AddGem(spawnPoint.position, to, now, arcSeconds, 0);
        }

        /// <summary>Alterna pontos espelhados (lado azul / lado vermelho) para as duas equipas terem o mesmo acesso.</summary>
        int NextLandingIndex()
        {
            if (m_Mirror.Length != landingPoints.Length) BuildMirrorTable();
            if (m_PendingMirror >= 0)
            {
                int m = m_PendingMirror;
                m_PendingMirror = -1;
                return m;
            }
            // Ponto ainda sem gema, ao acaso
            int start = UnityEngine.Random.Range(0, landingPoints.Length);
            int pick = start;
            for (int k = 0; k < landingPoints.Length; k++)
            {
                int i = (start + k) % landingPoints.Length;
                if (!Occupied(landingPoints[i].position, 0.5f)) { pick = i; break; }
            }
            if (m_Mirror[pick] != pick) m_PendingMirror = m_Mirror[pick];
            return pick;
        }

        /// <summary>Para cada aterragem, a aterragem espelhada pelo plano entre as duas zonas de spawn.</summary>
        void BuildMirrorTable()
        {
            int n = landingPoints != null ? landingPoints.Length : 0;
            m_Mirror = new int[n];
            Vector3 blue = Vector3.zero, red = Vector3.zero;
            int nb = 0, nr = 0;
            foreach (var sp in SpawnPoint.All)
            {
                if (sp.team == 0) { blue += sp.transform.position; nb++; }
                else if (sp.team == 1) { red += sp.transform.position; nr++; }
            }
            Vector3 axis = Vector3.forward, center = Vector3.zero;
            if (nb > 0 && nr > 0)
            {
                blue /= nb; red /= nr;
                axis = red - blue; axis.y = 0f; axis.Normalize();
                center = (blue + red) * 0.5f;
            }
            for (int i = 0; i < n; i++)
            {
                var p = landingPoints[i].position;
                var d = p - center;
                var mirrored = p - 2f * Vector3.Dot(d, axis) * axis;
                int best = i;
                float bestD = 0.6f;
                for (int j = 0; j < n; j++)
                {
                    float dist = Vector3.Distance(landingPoints[j].position, mirrored);
                    if (dist < bestD) { bestD = dist; best = j; }
                }
                m_Mirror[i] = best;
            }
        }

        bool Occupied(Vector3 p, float r)
        {
            for (int i = 0; i < Gems.Count; i++)
            {
                var g = Gems[i];
                var d = g.To - p;
                d.y = 0f;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>Se já há uma gema no ponto, procura um lugar livre e válido à volta (anéis de 0.55 m).</summary>
        Vector3 FreeSpotNear(Vector3 p)
        {
            if (!Occupied(p, 0.45f)) return p;
            for (int ring = 1; ring <= 3; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = (k / 8f + ring * 0.07f) * Mathf.PI * 2f;
                    var c = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.55f * ring);
                    if (!Occupied(c, 0.45f) && ValidGemSpot(p.y - gemFloatHeight, p, c, out var ok)) return ok;
                }
            return p; // empilha (nunca dentro da pedra: é o próprio marcador)
        }

        /// <summary>
        /// Um sítio é válido se tem chão firme quase à mesma altura de quem lá anda (nem em cima de muros/rochas,
        /// nem no fundo de um buraco ou da água), não está dentro de colisores — incluindo barreiras que só bloqueiam
        /// o movimento (layer Ignore Raycast, ex.: borda da Buracona) — e se vê a partir de <paramref name="sightFrom"/>.
        /// </summary>
        /// <param name="refFeetY">Altura dos pés de referência (jogador que largou as gemas ou chão do ponto de aterragem).</param>
        bool ValidGemSpot(float refFeetY, Vector3 sightFrom, Vector3 candidate, out Vector3 result)
        {
            result = candidate;
            candidate = MapInfo.ClampInside(candidate, 1.0f);
            var top = new Vector3(candidate.x, refFeetY + 2.5f, candidate.z);
            if (!Physics.Raycast(top, Vector3.down, out var hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.7f || IsPlayerCollider(hit.collider)) return false;
            if (hit.point.y > refFeetY + maxGroundRise) return false;   // em cima de bancas/muros/rochas: não
            if (hit.point.y < refFeetY - maxGroundDrop) return false;   // fundo de buracos / água: não
            var pos = hit.point + Vector3.up * gemFloatHeight;
            int n = Physics.OverlapSphereNonAlloc(pos, 0.3f, m_Overlap, BlockMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!IsPlayerCollider(m_Overlap[i])) return false;
            if (Physics.Linecast(sightFrom, pos, out var block, BlockMask, QueryTriggerInteraction.Ignore) &&
                !IsPlayerCollider(block.collider))
                return false;
            result = pos;
            return true;
        }

        /// <summary>Tudo o que bloqueia o movimento, incluindo barreiras de Ignore Raycast (que deixam passar tiros).</summary>
        static int BlockMask => Physics.DefaultRaycastLayers | (1 << 2);

        static bool IsPlayerCollider(Collider c) => c != null && c.GetComponentInParent<PlayerNetwork>() != null;

        void AddGem(Vector3 from, Vector3 to, double now, float duration, byte source)
        {
            Gems.Add(new GemNetState
            {
                Id = m_NextId,
                From = from,
                To = to,
                StartTime = now,
                Duration = duration,
                Source = source
            });
            m_NextId = (ushort)(m_NextId == ushort.MaxValue ? 1 : m_NextId + 1);
        }

        void ServerPickups(double now)
        {
            // Para cada gema disponível, só o jogador vivo mais próximo (dentro do raio) a apanha → sem duplicados
            for (int i = Gems.Count - 1; i >= 0; i--)
            {
                var g = Gems[i];
                if (!g.IsAvailable(now)) continue;
                PlayerNetwork best = null;
                float bestD = pickupRadius * pickupRadius;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || !p.IsSpawned || p.Health.IsDead || p.TeamId.Value < 0) continue;
                    if (m_PickupAllowedAt.TryGetValue(p.Id, out var allowedAt) && now < allowedAt) continue;
                    var d = p.transform.position - g.To;
                    if (Mathf.Abs(d.y + gemFloatHeight) > pickupHeight) continue;
                    d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq <= bestD) { bestD = sq; best = p; }
                }
                if (best == null) continue;
                Gems.RemoveAt(i);
                AddCarried(best.Id, 1);
                CollectedRpc(g.To, best.Id, best.TeamId.Value);
            }
        }

        void AddCarried(ulong playerId, int amount)
        {
            for (int i = 0; i < Carriers.Count; i++)
            {
                if (Carriers[i].PlayerId != playerId) continue;
                var c = Carriers[i];
                c.Count = Mathf.Max(0, c.Count + amount);
                Carriers[i] = c;
                return;
            }
            if (amount > 0) Carriers.Add(new GemCarrier { PlayerId = playerId, Count = amount });
        }

        /// <summary>Host: chamado pelo MatchManager quando um jogador morre.</summary>
        public void ServerOnPlayerDied(PlayerNetwork victim)
        {
            if (!IsServer || !IsActive || victim == null) return;
            for (int i = 0; i < Carriers.Count; i++)
            {
                if (Carriers[i].PlayerId != victim.Id) continue;
                int count = Carriers[i].Count;
                Carriers.RemoveAt(i);
                if (count > 0) ServerDropGems(victim.transform.position, count);
                return;
            }
        }

        void ServerDropGems(Vector3 feet, int count)
        {
            double now = Now;
            var origin = feet + Vector3.up * 1.0f;      // a gema sai do peito de quem morreu
            for (int k = 0; k < count; k++)
            {
                Vector3 to = Vector3.zero;
                bool found = false;
                for (int tries = 0; tries < 10 && !found; tries++)
                {
                    float a = (k / (float)Mathf.Max(1, count) + tries * 0.13f) * Mathf.PI * 2f;
                    float r = Mathf.Lerp(0.6f, dropRadius, UnityEngine.Random.value);
                    var c = feet + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    found = !Occupied(c, 0.35f) && ValidGemSpot(feet.y, origin, c, out to);
                }
                if (!found)
                {
                    // Junto aos pés (se válido) ou, em último caso, no ponto de aterragem mais próximo (sempre acessível)
                    if (!ValidGemSpot(feet.y, origin, feet, out to)) to = NearestLanding(feet);
                }
                AddGem(origin, to, now, dropArcSeconds, 1);
            }
        }

        Vector3 NearestLanding(Vector3 p)
        {
            if (landingPoints == null || landingPoints.Length == 0) return p + Vector3.up * gemFloatHeight;
            Transform best = landingPoints[0];
            float bd = float.MaxValue;
            foreach (var l in landingPoints)
            {
                float d = (l.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = l; }
            }
            return best.position;
        }

        void WriteScores()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned || match.Mode.Value != GameMode.GemGrab) return;
            int blue = TeamTotal(0), red = TeamTotal(1);
            if (match.BlueScore.Value != blue) match.BlueScore.Value = blue;
            if (match.RedScore.Value != red) match.RedScore.Value = red;
        }

        void ServerCountdown(double now)
        {
            int blue = TeamTotal(0), red = TeamTotal(1);
            int leader = -1;
            if (blue >= winGems && blue > red) leader = 0;
            else if (red >= winGems && red > blue) leader = 1;

            if (leader != CountdownTeam.Value)
            {
                // A equipa perdeu o requisito (ou passou outra à frente): a contagem recomeça
                CountdownTeam.Value = leader;
                CountdownEnd.Value = leader >= 0 ? now + holdSeconds : 0;
                return;
            }
            if (leader >= 0 && now >= CountdownEnd.Value)
            {
                CountdownTeam.Value = -1;
                MatchManager.Instance.ServerEndMatchForTeam(leader);
            }
        }

        [Rpc(SendTo.Everyone)]
        void CollectedRpc(Vector3 position, ulong playerId, int team)
        {
            if (m_Visuals != null) m_Visuals.PlayCollect(position, playerId, team);
        }
    }
}
