using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Jatos de lava ocasionais em pontos fixos (marcadores JET_* do mapa). Perigo real, com aviso:
    /// 1,6 s de borbulhar, anel a brilhar e a superfície a inchar → 1,1 s de explosão de lava (gotas e salpicos
    /// irregulares em leque, como uma fonte de lava a rebentar) que fere quem estiver a menos de
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

        [Header("Explosão (visual)")]
        [Tooltip("Gotas lançadas logo no rebentamento.")]
        public int burstBlobs = 34;
        [Tooltip("Gotas por segundo durante o resto do jato (a diminuir).")]
        public float blobsPerSecond = 60f;
        public float blobGravity = 15f;
        public int maxBlobs = 160;

        static readonly Color Hot = new Color(1f, 0.5f, 0.1f);
        static readonly Color BlobHot = new Color(1f, 0.5f, 0.11f);
        static readonly Color BlobWarm = new Color(0.95f, 0.26f, 0.04f);
        static readonly Color BlobCool = new Color(0.22f, 0.05f, 0.03f);
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        readonly HashSet<long> m_Hit = new HashSet<long>();
        Transform[] m_Domes = new Transform[0];
        int[] m_LastCycle = new int[0];
        float[] m_NextWarnFx = new float[0];
        float[] m_EmitCarry = new float[0];

        // gotas de lava (pool): posição, velocidade, tamanho, vida
        class Blob
        {
            public Transform t;
            public MeshRenderer r;
            public Vector3 v;
            public float size, age, life, floorY, stretch;
            public bool alive;
        }
        readonly List<Blob> m_Blobs = new List<Blob>();
        MaterialPropertyBlock m_Mpb;
        Mesh[] m_BlobMeshes = new Mesh[0];
        float[] m_TongueSeed = new float[0];

        /// <summary>Esfera deformada com ruído (gota/salpico irregular). O ruído depende só da posição do vértice,
        /// por isso as costuras da esfera continuam fechadas.</summary>
        static Mesh LumpyMesh(Mesh src, int seed, float amount)
        {
            var m = Object.Instantiate(src);
            m.name = "GotaLava_" + seed;
            var v = m.vertices;
            float o = seed * 17.31f;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i] * 2.3f;
                float n = Mathf.PerlinNoise(p.x + o, p.y * 1.3f + o) * 0.5f + Mathf.PerlinNoise(p.z + o * 0.7f, p.x * 1.7f + 3.1f) * 0.5f;
                n += 0.35f * Mathf.PerlinNoise(p.y * 3.1f + 7f, p.z * 2.7f + o);
                v[i] *= 1f + amount * (n - 0.6f) * 2f;
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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
            m_Mpb = new MaterialPropertyBlock();
            var sphere = Art.PrimitiveMesh(PrimitiveType.Sphere);
            m_BlobMeshes = new[] { LumpyMesh(sphere, 1, 0.32f), LumpyMesh(sphere, 2, 0.4f), LumpyMesh(sphere, 3, 0.28f), LumpyMesh(sphere, 4, 0.45f) };
            m_TongueSeed = new float[vents.Length];
            m_Domes = new Transform[vents.Length];
            m_LastCycle = new int[vents.Length];
            m_NextWarnFx = new float[vents.Length];
            m_EmitCarry = new float[vents.Length];
            for (int i = 0; i < vents.Length; i++)
            {
                m_LastCycle[i] = -1;
                // "bolha" da superfície que incha antes de rebentar
                var go = new GameObject("Jato_Cupula_" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_BlobMeshes[i % m_BlobMeshes.Length];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = columnMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                m_Domes[i] = go.transform;
            }
            for (int i = 0; i < maxBlobs; i++)
            {
                var go = new GameObject("Gota_Lava");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m_BlobMeshes[i % m_BlobMeshes.Length];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = columnMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                m_Blobs.Add(new Blob { t = go.transform, r = r });
            }
        }

        /// <summary>Lança uma gota a partir do respiradouro: fonte com um lóbulo principal inclinado e salpicos baixos.</summary>
        void SpawnBlob(Vector3 at, float strength, int vent)
        {
            Blob b = null;
            for (int k = 0; k < m_Blobs.Count; k++) if (!m_Blobs[k].alive) { b = m_Blobs[k]; break; }
            if (b == null) return;
            // direção do lóbulo principal muda de jato para jato (como na foto, a lava rebenta torta)
            float lobe = (vent * 2.3f + Mathf.Floor(Time.time / 13f)) % (Mathf.PI * 2f);
            var lean = new Vector3(Mathf.Cos(lobe), 0f, Mathf.Sin(lobe)) * 0.9f;
            bool splash = Random.value < 0.35f;            // salpicos baixos que se espalham pela superfície
            var dir = splash
                ? new Vector3(Random.Range(-1f, 1f), Random.Range(0.25f, 0.6f), Random.Range(-1f, 1f))
                : new Vector3(Random.Range(-0.45f, 0.45f), 1f, Random.Range(-0.45f, 0.45f)) + lean * Random.Range(0.1f, 0.6f);
            float speed = (splash ? Random.Range(2.2f, 4.2f) : Random.Range(4.5f, 8.8f)) * strength;
            b.v = dir.normalized * speed;
            b.size = splash ? Random.Range(0.06f, 0.16f) : Mathf.Pow(Random.value, 1.8f) * 0.3f + 0.07f;
            b.size *= Mathf.Lerp(0.75f, 1.1f, strength);
            b.age = 0f;
            b.life = Random.Range(1.1f, 1.8f);
            b.floorY = at.y - 0.05f;
            b.stretch = Random.Range(0.16f, 0.32f);
            b.alive = true;
            b.t.position = at + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0f, 0.15f), Random.Range(-0.25f, 0.25f));
            b.t.gameObject.SetActive(true);
        }

        void UpdateBlobs(float dt)
        {
            for (int k = 0; k < m_Blobs.Count; k++)
            {
                var b = m_Blobs[k];
                if (!b.alive) continue;
                b.age += dt;
                b.v.y -= blobGravity * dt;
                b.v *= 1f - 0.25f * dt;                     // um pouco de arrasto: a lava é pesada e espessa
                var p = b.t.position + b.v * dt;
                float u = b.age / b.life;
                if (u >= 1f || (p.y <= b.floorY && b.v.y < 0f))
                {
                    if (p.y <= b.floorY + 0.4f && b.size > 0.2f && Random.value < 0.5f)
                        Fx.Glow(new Vector3(p.x, b.floorY + 0.06f, p.z), Vector3.up * 0.5f, Hot, 0.1f, 0.35f);
                    b.alive = false;
                    b.t.gameObject.SetActive(false);
                    continue;
                }
                b.t.position = p;
                // gota esticada na direção do movimento, a encolher e a arrefecer (amarelo → laranja → vermelho escuro)
                float spd = b.v.magnitude;
                if (spd > 0.05f) b.t.rotation = Quaternion.LookRotation(b.v / spd);
                float s = b.size * (u < 0.7f ? 1f : Mathf.Lerp(1f, 0.35f, (u - 0.7f) / 0.3f));
                b.t.localScale = new Vector3(s, s * 0.85f, s * (1f + spd * b.stretch));
                var c = u < 0.35f ? Color.Lerp(BlobHot, BlobWarm, u / 0.35f) : Color.Lerp(BlobWarm, BlobCool, (u - 0.35f) / 0.65f);
                m_Mpb.SetColor(ColorId, c);
                m_Mpb.SetColor(EmissionId, c * Mathf.Lerp(1.35f, 0.1f, u));
                b.r.SetPropertyBlock(m_Mpb);
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
            float dt = Time.deltaTime;
            for (int i = 0; i < vents.Length; i++)
            {
                var v = vents[i];
                if (v == null) continue;
                int st = State(i, now, out int cycle, out float u);
                if (!playing) st = 0;
                var dome = i < m_Domes.Length ? m_Domes[i] : null;

                if (st == 1 && Time.time >= m_NextWarnFx[i])        // aviso: anel e bolhas a crescer
                {
                    m_NextWarnFx[i] = Time.time + Mathf.Lerp(0.45f, 0.18f, u);
                    Fx.Ring(v.position + Vector3.up * 0.03f, Hot, radius * 2f * Mathf.Lerp(0.6f, 1.1f, u), 0.4f);
                    Fx.Glow(v.position + Random.insideUnitSphere * 0.4f, Vector3.up * Random.Range(0.5f, 1.4f), Hot, 0.12f, 0.6f);
                }
                // a superfície incha nos últimos instantes do aviso (cúpula de lava) e rebenta
                if (dome != null)
                {
                    bool swell = st == 1 && u > 0.45f;
                    bool tongue = st == 2 && u < 0.55f;          // língua de lava que sobe e cai (como na foto)
                    bool show = swell || tongue;
                    if (dome.gameObject.activeSelf != show) dome.gameObject.SetActive(show);
                    if (swell)
                    {
                        float g = (u - 0.45f) / 0.55f;
                        float w = Mathf.Lerp(0.3f, 1.25f, g) * (1f + 0.06f * Mathf.Sin(Time.time * 30f));
                        dome.position = v.position + Vector3.up * (0.05f + 0.18f * g);
                        dome.rotation = Quaternion.identity;
                        dome.localScale = new Vector3(w, w * Mathf.Lerp(0.25f, 0.6f, g), w);
                        m_Mpb.SetColor(ColorId, BlobWarm);
                        m_Mpb.SetColor(EmissionId, BlobWarm * 0.9f);
                        dome.GetComponent<MeshRenderer>().SetPropertyBlock(m_Mpb);
                    }
                    else if (tongue)
                    {
                        if (u < 0.05f) m_TongueSeed[i] = Random.Range(0f, 360f);
                        float g = u / 0.55f;
                        float rise = Mathf.Sin(Mathf.Clamp01(g) * Mathf.PI);       // sobe e volta a cair
                        float h = 0.4f + 2.4f * rise;
                        float w = Mathf.Lerp(1.1f, 0.35f, g);
                        dome.rotation = Quaternion.Euler(Mathf.Sin(m_TongueSeed[i]) * 14f, m_TongueSeed[i] + Time.time * 40f, Mathf.Cos(m_TongueSeed[i]) * 14f);
                        dome.position = v.position + Vector3.up * (h * 0.42f);
                        dome.localScale = new Vector3(w, h * 0.5f, w * 0.75f);
                        var c = Color.Lerp(BlobHot, BlobWarm, g);
                        m_Mpb.SetColor(ColorId, c);
                        m_Mpb.SetColor(EmissionId, c * 1.3f);
                        dome.GetComponent<MeshRenderer>().SetPropertyBlock(m_Mpb);
                    }
                }
                if (st == 2)
                {
                    if (m_LastCycle[i] != cycle)
                    {
                        m_LastCycle[i] = cycle;
                        m_EmitCarry[i] = 0f;
                        for (int k = 0; k < burstBlobs; k++) SpawnBlob(v.position, Random.Range(0.85f, 1.1f), i);
                        Fx.Light(v.position + Vector3.up, Hot, 4f, 7f, active + 0.4f);
                        Fx.Sparks(v.position + Vector3.up * 0.5f, Hot, 14, 6f);
                        Sfx.PlayAt(Sound.Explosion, v.position, 0.35f);
                    }
                    // fonte a perder força ao longo do jato
                    m_EmitCarry[i] += dt * blobsPerSecond * (1f - u);
                    while (m_EmitCarry[i] >= 1f)
                    {
                        m_EmitCarry[i] -= 1f;
                        SpawnBlob(v.position, Mathf.Lerp(1f, 0.55f, u), i);
                    }
                    if (Random.value < 0.35f)
                        Fx.Smoke(v.position + Vector3.up * Random.Range(0.6f, 2f), new Vector3(Random.Range(-0.3f, 0.3f), 1.2f, Random.Range(-0.3f, 0.3f)), 1.2f, 1.6f, 0.25f);
                    if (IsServer) ServerDamage(i, cycle, v.position);
                }
            }
            UpdateBlobs(dt);
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
