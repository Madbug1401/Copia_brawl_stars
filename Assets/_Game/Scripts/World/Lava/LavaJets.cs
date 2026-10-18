using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Jatos de lava ocasionais em pontos fixos (marcadores JET_* do mapa). Perigo real, com aviso:
    /// 1,6 s de borbulhar e anel a brilhar → 1,1 s de coluna de lava que fere quem estiver a menos de
    /// <see cref="radius"/> m → pausa. O horário é determinístico a partir de <see cref="Epoch"/> (hora do servidor),
    /// por isso todas as máquinas veem o mesmo sem mensagens extra e quem entra a meio fica sincronizado.
    /// Só o HOST aplica dano (uma vez por jato por jogador). Reiniciar a partida reinicia o horário.
    /// </summary>
    public class LavaJets : NetworkBehaviour
    {
        public static LavaJets Instance { get; private set; }

        public Transform[] vents = new Transform[0];
        public float firstDelay = 8f;
        public float period = 13f;
        public float warning = 1.6f;
        public float active = 1.1f;
        public float radius = 1.4f;
        public int damage = 30;
        public Material columnMaterial;

        public readonly NetworkVariable<double> Epoch = new NetworkVariable<double>(0);

        static readonly Color Hot = new Color(1f, 0.5f, 0.1f);
        readonly HashSet<long> m_Hit = new HashSet<long>();
        Transform[] m_Columns = new Transform[0];
        int[] m_LastCycle = new int[0];
        float[] m_NextWarnFx = new float[0];

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

        /// <summary>Host: nova partida → horário dos jatos recomeça (sem jatos nos primeiros segundos).</summary>
        public void ServerRestart()
        {
            if (!IsServer || !IsSpawned) return;
            Epoch.Value = NetworkManager.ServerTime.Time;
            m_Hit.Clear();
        }

        void Start()
        {
            var cyl = Art.PrimitiveMesh(PrimitiveType.Cylinder);
            m_Columns = new Transform[vents.Length];
            m_LastCycle = new int[vents.Length];
            m_NextWarnFx = new float[vents.Length];
            for (int i = 0; i < vents.Length; i++)
            {
                m_LastCycle[i] = -1;
                var go = new GameObject("Jato_" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = cyl;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = columnMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                go.SetActive(false);
                m_Columns[i] = go.transform;
            }
        }

        float Offset(int i) => firstDelay + i * 1.7f;
        float Period(int i) => period + (i % 3) * 1.3f;

        /// <summary>Fase do jato i: 0 parado, 1 aviso, 2 ativo. 'cycle' identifica cada disparo; 'u' = progresso 0..1.</summary>
        public int State(int i, double now, out int cycle, out float u)
        {
            cycle = -1;
            u = 0f;
            if (Epoch.Value <= 0) return 0;
            double local = now - Epoch.Value - Offset(i);
            if (local < 0) return 0;
            float per = Period(i);
            cycle = (int)(local / per);
            float t = (float)(local - cycle * per);
            if (t < warning) { u = t / warning; return 1; }
            if (t < warning + active) { u = (t - warning) / active; return 2; }
            return 0;
        }

        void Update()
        {
            if (!IsSpawned || NetworkManager == null) return;
            bool playing = MatchManager.Instance == null || MatchManager.Instance.Phase.Value == MatchPhase.Playing;
            double now = NetworkManager.ServerTime.Time;
            for (int i = 0; i < vents.Length; i++)
            {
                var v = vents[i];
                if (v == null) continue;
                int st = State(i, now, out int cycle, out float u);
                if (!playing) st = 0;
                var col = i < m_Columns.Length ? m_Columns[i] : null;

                if (st == 1 && Time.time >= m_NextWarnFx[i])        // aviso: anel e bolhas a crescer
                {
                    m_NextWarnFx[i] = Time.time + Mathf.Lerp(0.45f, 0.18f, u);
                    Fx.Ring(v.position + Vector3.up * 0.03f, Hot, radius * 2f * Mathf.Lerp(0.6f, 1.1f, u), 0.4f);
                    Fx.Glow(v.position + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(0.5f, 1.4f), Hot, 0.12f, 0.6f);
                }
                if (st == 2)
                {
                    if (m_LastCycle[i] != cycle)
                    {
                        m_LastCycle[i] = cycle;
                        Fx.Light(v.position + Vector3.up, Hot, 4f, 7f, active);
                        Fx.Sparks(v.position + Vector3.up * 0.5f, Hot, 14, 6f);
                        Sfx.PlayAt(Sound.Explosion, v.position, 0.35f);
                    }
                    if (col != null)
                    {
                        if (!col.gameObject.activeSelf) col.gameObject.SetActive(true);
                        float h = 3.2f * Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) + 0.2f;
                        col.position = v.position + Vector3.up * (h * 0.5f);
                        col.localScale = new Vector3(0.55f, h * 0.5f, 0.55f);
                    }
                    if (Random.value < 0.5f)
                        Fx.Glow(v.position + Vector3.up * Random.Range(0.5f, 3f), new Vector3(Random.Range(-1f, 1f), Random.Range(0.5f, 2f), Random.Range(-1f, 1f)), Hot, 0.14f, 0.7f);
                    if (IsServer) ServerDamage(i, cycle, v.position);
                }
                else if (col != null && col.gameObject.activeSelf) col.gameObject.SetActive(false);
            }
        }

        void ServerDamage(int vent, int cycle, Vector3 at)
        {
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                var d = p.transform.position - at;
                if (d.y > 3.5f || d.y < -1f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius) continue;
                long key = ((long)vent << 48) ^ ((long)cycle << 24) ^ (long)(p.Id & 0xFFFFFF);
                if (!m_Hit.Add(key)) continue;
                EffectResolver.DamageRaw(p.Health, damage, ulong.MaxValue, false, false);
            }
            if (m_Hit.Count > 4096) m_Hit.Clear();
        }
    }
}
