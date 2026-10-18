using System;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Uma fase da tempestade: espera → fecho até <see cref="radius"/> em <see cref="shrink"/> s.</summary>
    [Serializable]
    public struct StormPhase
    {
        public float wait;
        public float shrink;
        public float radius;
        [Tooltip("Dano por segundo a quem estiver fora da zona segura durante esta fase.")]
        public int damagePerSecond;

        public StormPhase(float wait, float shrink, float radius, int dps)
        {
            this.wait = wait; this.shrink = shrink; this.radius = radius; damagePerSecond = dps;
        }
    }

    /// <summary>Entrada da zona final (para os bots): de fora para dentro.</summary>
    [Serializable]
    public struct StormEntrance
    {
        public Vector3 outer, mouth, laneA, laneB, inner;
    }

    /// <summary>
    /// Tempestade de cinza (sobrevivência / Showdown), controlada pelo HOST. O estado replicado é só a hora de início
    /// (<see cref="StartTime"/>) e se está ativa: raio, fase e contagens são calculados igual em todas as máquinas a
    /// partir das fases configuradas, por isso quem entra a meio vê o mesmo círculo. O host aplica o dano por segundo
    /// a quem estiver fora. A zona fecha por fases para o <see cref="center"/>; nas últimas fases (raio ≤
    /// <see cref="ceilingFromRadius"/>) a zona segura é também limitada em altura (<see cref="ceiling"/>), para que só
    /// conte o chão da arena e não o topo de rochas ou da crista. Reiniciar a partida reinicia a tempestade.
    /// </summary>
    public class StormManager : NetworkBehaviour
    {
        public static StormManager Instance { get; private set; }

        [Header("Zona")]
        public Vector3 center = Vector3.zero;
        public float startRadius = 46f;
        public StormPhase[] phases =
        {
            new StormPhase(40f, 30f, 31f, 4),
            new StormPhase(28f, 26f, 21f, 6),
            new StormPhase(24f, 24f, 12.5f, 9),     // a zona passa a estar dentro do vulcão: entra-se pelas 4 rampas
            new StormPhase(20f, 18f, 7.5f, 13),
            new StormPhase(16f, 14f, 4.5f, 18),     // zona final: centro da arena interior (chão de rocha)
        };
        [Tooltip("Altura máxima (acima do centro) da zona segura nas fases finais.")]
        public float ceiling = 3.6f;
        public float ceilingFromRadius = 13f;

        [Header("Modo")]
        [Tooltip("A tempestade só corre em sobrevivência (Showdown). Nos outros modos o mapa joga-se normalmente.")]
        public bool onlyInShowdown = true;

        [Header("Bots")]
        public StormEntrance[] entrances = new StormEntrance[0];
        [Tooltip("Raio do interior do vulcão (para os bots saberem quando já estão dentro).")]
        public float innerRadius = 13f;
        [Tooltip("Raio do pé do vulcão: zonas mais pequenas do que isto obrigam a entrar pelos desfiladeiros.")]
        public float footRadius = 22.5f;

        public readonly NetworkVariable<double> StartTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<bool> Running = new NetworkVariable<bool>(false);

        float m_DamageTick;

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer) ServerRestart();
        }

        bool ModeWantsStorm()
        {
            var mm = MatchManager.Instance;
            if (mm == null || !mm.IsSpawned) return false;
            return !onlyInShowdown || mm.Mode.Value == GameMode.Showdown;
        }

        /// <summary>Host: chamado no início de cada partida (MatchManager.ServerStartMatch).</summary>
        public void ServerRestart()
        {
            if (!IsServer || !IsSpawned) return;
            StartTime.Value = NetworkManager.ServerTime.Time;
            Running.Value = ModeWantsStorm();
        }

        public static bool IsActive => Instance != null && Instance.IsSpawned && Instance.Running.Value &&
            (MatchManager.Instance == null || MatchManager.Instance.Phase.Value == MatchPhase.Playing);

        double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0.0;

        /// <summary>Estado no instante t: fase (−1 = antes de começar a fechar), a fechar?, raio atual, raio alvo,
        /// segundos até à próxima mudança e dano por segundo.</summary>
        public void Evaluate(double t, out int phase, out bool shrinking, out float radius, out float target, out float untilNext, out int dps)
        {
            float local = (float)(t - StartTime.Value);
            float r = startRadius;
            phase = -1;
            shrinking = false;
            dps = 0;
            for (int i = 0; i < phases.Length; i++)
            {
                var ph = phases[i];
                if (local < ph.wait)
                {
                    radius = r; target = ph.radius; untilNext = ph.wait - local;
                    phase = i - 1;
                    dps = i > 0 ? phases[i - 1].damagePerSecond : 0;
                    return;
                }
                local -= ph.wait;
                if (local < ph.shrink)
                {
                    float u = local / Mathf.Max(0.01f, ph.shrink);
                    radius = Mathf.Lerp(r, ph.radius, u); target = ph.radius; untilNext = ph.shrink - local;
                    phase = i; shrinking = true; dps = ph.damagePerSecond;
                    return;
                }
                local -= ph.shrink;
                r = ph.radius;
            }
            var last = phases.Length > 0 ? phases[phases.Length - 1] : new StormPhase(0, 0, startRadius, 10);
            radius = r; target = r; untilNext = 0f; phase = phases.Length - 1; dps = last.damagePerSecond + 6;
        }

        public float CurrentRadius { get { Evaluate(Now, out _, out _, out float r, out _, out _, out _); return r; } }

        /// <summary>Este ponto está dentro da zona segura (inclui o limite de altura nas fases finais)?</summary>
        public bool IsSafe(Vector3 p)
        {
            Evaluate(Now, out _, out _, out float r, out _, out _, out _);
            return IsSafe(p, r);
        }

        public bool IsSafe(Vector3 p, float r)
        {
            var d = p - center;
            float y = d.y;
            d.y = 0f;
            if (d.sqrMagnitude > r * r) return false;
            if (r <= ceilingFromRadius && y > ceiling) return false;
            return true;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || !IsActive) return;
            m_DamageTick += Time.deltaTime;
            if (m_DamageTick < 1f) return;
            m_DamageTick -= 1f;
            Evaluate(Now, out int phase, out _, out float r, out _, out _, out int dps);
            if (dps <= 0) return;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                if (IsSafe(p.transform.position, r)) continue;
                EffectResolver.DamageRaw(p.Health, dps, ulong.MaxValue, false, false);
            }
        }

        // ------------------------------------------------------------------ bots

        /// <summary>
        /// Para onde um bot deve ir para fugir à tempestade (null se não for preciso). Fora do vulcão, quando a
        /// zona final fica lá dentro, segue a entrada mais próxima por fora → boca → faixa → interior; nunca
        /// manda atravessar a encosta. É uma ajuda de navegação, não teleporta ninguém.
        /// </summary>
        public static bool TryGetBotGoal(Vector3 pos, out Vector3 goal)
        {
            goal = default;
            var s = Instance;
            if (!IsActive || s == null) return false;
            s.Evaluate(s.Now, out _, out bool shrinking, out float r, out float target, out float until, out _);
            float soon = shrinking || until < 12f ? target : r;
            var d = pos - s.center;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist < soon * 0.75f && s.IsSafe(pos, r)) return false;     // já está bem dentro

            if (soon < s.footRadius + 2f && dist > s.innerRadius && s.entrances.Length > 0)
            {
                // entrada mais próxima pelo ângulo
                int best = 0;
                float bestAng = 999f;
                for (int i = 0; i < s.entrances.Length; i++)
                {
                    var e = s.entrances[i].mouth - s.center;
                    float a = Vector3.Angle(new Vector3(e.x, 0f, e.z), d);
                    if (a < bestAng) { bestAng = a; best = i; }
                }
                var en = s.entrances[best];
                float rOuter = Flat(en.outer - s.center);
                float rMouth = Flat(en.mouth - s.center);
                // cada etapa conta como feita quando o bot passa o raio dela (ou chega perto do ponto), e nunca volta atrás
                float rLane = Flat(en.laneA - s.center);
                if (dist > rOuter - 0.5f && Flat(pos - en.outer) > 2.5f && bestAng > 12f) { goal = en.outer; return true; }
                if (dist > rMouth + 0.6f && Flat(pos - en.mouth) > 1.5f) { goal = en.mouth; return true; }
                if (dist > rLane + 0.6f)
                {
                    var lane = Flat(pos - en.laneA) < Flat(pos - en.laneB) ? en.laneA : en.laneB;
                    if (Flat(pos - lane) > 1.2f) { goal = lane; return true; }
                }
                goal = dist > Flat(en.inner - s.center) + 0.8f ? en.inner : s.center;
                return true;
            }
            goal = dist > 0.1f ? s.center + d.normalized * Mathf.Min(dist, soon * 0.4f) : s.center;
            return true;
        }

        /// <summary>O bot está (ou vai ficar em breve) fora da zona segura? Então fugir da tempestade passa à frente
        /// de perseguir alvos (continua a apontar e a disparar enquanto foge).</summary>
        public static bool BotMustMove(Vector3 pos)
        {
            var s = Instance;
            if (!IsActive || s == null) return false;
            s.Evaluate(s.Now, out _, out bool shrinking, out float r, out float target, out float until, out _);
            float soon = shrinking || until < 10f ? target : r;
            return !s.IsSafe(pos, Mathf.Max(1.5f, soon - 1.2f));
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
