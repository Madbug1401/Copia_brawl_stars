using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Aspeto do emissor de gemas de cada mapa (só apresentação; as regras são as mesmas).</summary>
    public enum GemEmitterStyle
    {
        /// <summary>Cidade Velha: halo e anel no Pelourinho, faíscas a subir junto da coluna.</summary>
        Pelourinho = 0,
        /// <summary>Buracona: brilho por baixo da água, ondulação e bolhas antes da geração, salpico ao emergir.</summary>
        Agua = 1,
    }

    /// <summary>
    /// Apresentação do Pique-Gemas em cada máquina (host e clientes). Lê o estado replicado do
    /// <see cref="GemGrabManager"/> e desenha: aviso do emissor (brilho + partículas a subir junto da coluna),
    /// surgimento, arco até ao chão, flutuação/rotação e efeito de recolha. Não decide nada.
    /// Os efeitos do Blender (partículas, emissão animada) não passam no FBX — são reconstruídos aqui com o Fx.
    /// </summary>
    [RequireComponent(typeof(GemGrabManager))]
    public class GemGrabVisuals : MonoBehaviour
    {
        class View
        {
            public Transform tr;
            public bool landed;
            public float spinOffset;
        }

        static readonly int EmissionId = Shader.PropertyToID("_Emission");

        [Tooltip("Aspeto do emissor deste mapa.")]
        public GemEmitterStyle style = GemEmitterStyle.Pelourinho;
        [Tooltip("Estilo Água: superfície da água (centro) onde aparecem ondulação, bolhas e salpicos.")]
        public Transform waterSurface;
        [Tooltip("Estilo Água: raio da zona de bolhas/ondulação.")]
        public float waterRadius = 2.2f;
        public Color waterColor = new Color(0.45f, 0.95f, 0.95f, 0.9f);
        float m_RippleTimer;

        GemGrabManager m_Mgr;
        readonly Dictionary<ushort, View> m_Views = new Dictionary<ushort, View>();
        readonly HashSet<ushort> m_Seen = new HashSet<ushort>();
        readonly List<ushort> m_Remove = new List<ushort>();
        readonly Stack<Transform> m_Pool = new Stack<Transform>();
        MaterialPropertyBlock m_Mpb;
        float m_ParticleCarry;
        float m_HaloAngle, m_BaseAngle;
        bool m_WasWarning;
        Vector3 m_HaloScale = Vector3.zero;
        Quaternion m_HaloRot, m_BaseRot;

        void Awake()
        {
            m_Mgr = GetComponent<GemGrabManager>();
            m_Mpb = new MaterialPropertyBlock();
        }

        void OnDisable() => ClearAll();

        void ClearAll()
        {
            foreach (var v in m_Views.Values)
                if (v.tr != null) Destroy(v.tr.gameObject);
            m_Views.Clear();
        }

        void LateUpdate()
        {
            var nm = NetworkManager.Singleton;
            if (m_Mgr == null || !m_Mgr.IsSpawned || nm == null || !GemGrabManager.IsActive)
            {
                if (m_Views.Count > 0) ClearAll();
                UpdateEmitter(0f, Time.deltaTime);
                return;
            }

            double now = nm.ServerTime.Time;
            float dt = Time.deltaTime;

            // ---------------- gemas
            m_Seen.Clear();
            var gems = m_Mgr.Gems;
            for (int i = 0; i < gems.Count; i++)
            {
                var g = gems[i];
                m_Seen.Add(g.Id);
                if (!m_Views.TryGetValue(g.Id, out var v))
                {
                    v = new View { tr = Get(), spinOffset = (g.Id * 37) % 360 };
                    m_Views[g.Id] = v;
                    float age = (float)(now - g.StartTime);
                    if (g.Source == 0 && age < 0.3f)
                    {
                        // Surgimento: clarão no ponto de geração e marca no sítio onde vai cair
                        if (style == GemEmitterStyle.Agua) Splash();
                        else
                        {
                            Fx.Light(g.From, m_Mgr.gemColor, 3f, 6f, 0.3f);
                            for (int k = 0; k < 10; k++)
                                Fx.Glow(g.From, Random.insideUnitSphere * 1.6f, Color.Lerp(m_Mgr.gemColor, Color.white, 0.5f), 0.18f, 0.5f);
                        }
                        Fx.Ring(Ground(g.To), m_Mgr.gemColor, 1.4f, Mathf.Max(0.3f, g.Duration));
                    }
                    else if (age >= g.Duration) v.landed = true; // já estava no chão (entrou a meio da partida)
                }
                Place(v, g, now, dt);
            }

            m_Remove.Clear();
            foreach (var kv in m_Views)
                if (!m_Seen.Contains(kv.Key)) m_Remove.Add(kv.Key);
            foreach (var id in m_Remove)
            {
                Release(m_Views[id].tr);
                m_Views.Remove(id);
            }

            // ---------------- emissor
            float toNext = m_Mgr.TimeToNextSpawn;
            float warn = (toNext > 0f && toNext <= m_Mgr.warningSeconds) ? 1f - toNext / m_Mgr.warningSeconds : 0f;
            if (MatchManager.Instance != null && MatchManager.Instance.Phase.Value != MatchPhase.Playing) warn = 0f;
            UpdateEmitter(warn, dt);
            if (warn > 0f)
            {
                if (style == GemEmitterStyle.Agua) EmitWaterWarning(warn, dt);
                else EmitRisingParticles(warn, dt);
            }
            if (warn > 0f && !m_WasWarning && m_Mgr.spawnPoint != null)
                Sfx.PlayAt(Sound.Twirl, m_Mgr.spawnPoint.position, 0.5f);
            m_WasWarning = warn > 0f;
        }

        void Place(View v, GemNetState g, double now, float dt)
        {
            float t = g.Duration <= 0f ? 1f : Mathf.Clamp01((float)((now - g.StartTime) / g.Duration));
            var tr = v.tr;
            if (t < 1f)
            {
                float h = g.Source == 0 ? m_Mgr.arcHeight : 0.9f;
                tr.position = Vector3.Lerp(g.From, g.To, t) + Vector3.up * (h * 4f * t * (1f - t));
                float s = g.Source == 0 ? Mathf.SmoothStep(0f, 1f, t / 0.25f) : 1f;
                tr.localScale = Vector3.one * Mathf.Max(0.01f, s);
                tr.rotation = Quaternion.Euler(0f, v.spinOffset + t * 720f, 0f);
                v.landed = false;
            }
            else
            {
                if (!v.landed)
                {
                    v.landed = true;
                    Fx.Ring(Ground(g.To), m_Mgr.gemColor, 1.1f, 0.35f);
                    Fx.Sparks(g.To, m_Mgr.gemColor, 6, 2f);
                }
                float bob = Mathf.Sin(Time.time * 2.4f + g.Id) * 0.1f;
                tr.position = g.To + Vector3.up * bob;
                tr.localScale = Vector3.one;
                tr.rotation = Quaternion.Euler(0f, v.spinOffset + Time.time * 90f, 0f);
            }
        }

        static Vector3 Ground(Vector3 gemPos)
        {
            if (Physics.Raycast(gemPos + Vector3.up * 0.5f, Vector3.down, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return gemPos + Vector3.down * 0.6f;
        }

        void UpdateEmitter(float warn, float dt)
        {
            float speed = Mathf.Lerp(30f, 420f, warn * warn);
            m_HaloAngle += speed * dt;
            m_BaseAngle -= Mathf.Lerp(20f, 160f, warn) * dt;
            if (m_Mgr == null) return;
            if (m_HaloScale == Vector3.zero)
            {
                // Pose importada (escala/rotação do FBX) guardada uma vez; a animação é aplicada por cima
                m_HaloScale = m_Mgr.emitterHalo != null ? m_Mgr.emitterHalo.localScale : Vector3.one;
                m_HaloRot = m_Mgr.emitterHalo != null ? m_Mgr.emitterHalo.localRotation : Quaternion.identity;
                m_BaseRot = m_Mgr.emitterBase != null ? m_Mgr.emitterBase.localRotation : Quaternion.identity;
            }
            if (m_Mgr.emitterHalo != null)
            {
                m_Mgr.emitterHalo.localRotation = Quaternion.Euler(0f, m_HaloAngle, 0f) * m_HaloRot;
                m_Mgr.emitterHalo.localScale = m_HaloScale * (1f + 0.3f * Mathf.Sin(warn * Mathf.PI));
            }
            if (m_Mgr.emitterBase != null) m_Mgr.emitterBase.localRotation = Quaternion.Euler(0f, m_BaseAngle, 0f) * m_BaseRot;

            var c = Color.Lerp(m_Mgr.emitterIdleEmission, m_Mgr.emitterWarnEmission, warn);
            c *= 0.85f + 0.15f * Mathf.Sin(Time.time * (3f + warn * 14f));
            if (m_Mpb == null) m_Mpb = new MaterialPropertyBlock(); // pode faltar após recarregar scripts em Play
            foreach (var r in m_Mgr.emitterRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(m_Mpb);
                m_Mpb.SetColor(EmissionId, c);
                r.SetPropertyBlock(m_Mpb);
            }
        }

        void EmitRisingParticles(float warn, float dt)
        {
            var origin = m_Mgr.emitterBase != null ? m_Mgr.emitterBase.position :
                (m_Mgr.spawnPoint != null ? new Vector3(m_Mgr.spawnPoint.position.x, 1.5f, m_Mgr.spawnPoint.position.z) : Vector3.up);
            m_ParticleCarry += Mathf.Lerp(18f, 70f, warn) * dt;
            var light = Color.Lerp(m_Mgr.gemColor, Color.white, 0.45f);
            while (m_ParticleCarry >= 1f)
            {
                m_ParticleCarry -= 1f;
                float a = Random.value * Mathf.PI * 2f;
                var p = origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.45f, 0.65f);
                Fx.Glow(p, Vector3.up * Random.Range(2.5f, 4.8f), light, Random.Range(0.1f, 0.2f), Random.Range(1.0f, 1.6f));
            }
        }

        Vector3 SurfacePoint => waterSurface != null ? waterSurface.position :
            (m_Mgr.spawnPoint != null ? m_Mgr.spawnPoint.position : transform.position);

        /// <summary>Estilo Água: ondulação circular e pequenas bolhas na superfície antes da geração (sem jato).</summary>
        void EmitWaterWarning(float warn, float dt)
        {
            var c = SurfacePoint;
            m_RippleTimer -= dt;
            if (m_RippleTimer <= 0f)
            {
                m_RippleTimer = Mathf.Lerp(0.6f, 0.25f, warn);
                // Fx.Ring sobe 5 cm; compensar para ficar rente à água
                Fx.Ring(c + Vector3.down * 0.04f, Art.WithAlpha(Color.Lerp(waterColor, Color.white, 0.4f), 0.7f), Mathf.Lerp(1.2f, 3.2f, warn), 0.9f);
            }
            m_ParticleCarry += Mathf.Lerp(5f, 22f, warn) * dt;
            var bubble = Art.WithAlpha(Color.Lerp(waterColor, Color.white, 0.6f), 0.9f);
            while (m_ParticleCarry >= 1f)
            {
                m_ParticleCarry -= 1f;
                var off = Random.insideUnitCircle * waterRadius * Mathf.Lerp(1f, 0.45f, warn);
                var p = c + new Vector3(off.x, -0.35f, off.y);
                Fx.Glow(p, Vector3.up * Random.Range(0.4f, 0.9f), bubble, Random.Range(0.05f, 0.1f), Random.Range(0.45f, 0.8f));
            }
        }

        /// <summary>Estilo Água: salpico curto quando a gema emerge.</summary>
        void Splash()
        {
            var c = SurfacePoint;
            var foam = new Color(0.92f, 0.98f, 1f, 0.9f);
            Fx.Ring(c + Vector3.down * 0.04f, foam, 2.4f, 0.45f);
            Fx.Debris(c, Color.Lerp(waterColor, Color.white, 0.5f), 10, 4.5f);
            for (int k = 0; k < 8; k++)
                Fx.Puff(c + Random.insideUnitSphere * 0.3f, new Vector3(Random.Range(-1f, 1f), Random.Range(1.5f, 3f), Random.Range(-1f, 1f)),
                    foam, Random.Range(0.25f, 0.45f), Random.Range(0.35f, 0.55f));
            Fx.Light(c + Vector3.up * 0.4f, waterColor, 2.2f, 5f, 0.25f);
            Sfx.PlayAt(Sound.Poof, c, 0.45f);
        }

        /// <summary>Efeito curto de recolha (chamado pelo RPC do host).</summary>
        public void PlayCollect(Vector3 position, ulong playerId, int team)
        {
            var local = PlayerNetwork.Local;
            var who = PlayerNetwork.Find(playerId);
            if (who != null && who != local && who.Visuals != null && who.Visuals.IsConcealedForLocal)
                return; // apanhou escondido num arbusto: sem efeito que revele a posição
            var c = Color.Lerp(m_Mgr.gemColor, Color.white, 0.35f);
            Fx.Sparks(position, c, 14, 4f);
            Fx.Ring(Ground(position), c, 1.8f, 0.3f);
            Fx.Light(position, m_Mgr.gemColor, 2.2f, 4f, 0.2f);
            if (local != null && local.Id == playerId) Sfx.Play2D(Sound.SuperReady, 0.6f);
            else Sfx.PlayAt(Sound.Crate, position, 0.5f);
        }

        // ---------------------------------------------------------------- pool

        Transform Get()
        {
            if (m_Pool.Count > 0)
            {
                var t = m_Pool.Pop();
                t.gameObject.SetActive(true);
                return t;
            }
            GameObject go;
            if (m_Mgr.gemPrefab != null) go = Instantiate(m_Mgr.gemPrefab);
            else
            {
                go = new GameObject(); // recurso se o prefab faltar: cubo rodado
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.SetParent(go.transform, false);
                cube.transform.localScale = Vector3.one * 0.32f;
                cube.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                cube.GetComponent<Renderer>().material.color = m_Mgr.gemColor;
            }
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col); // a recolha é decidida pelo host
            go.name = "Gema (visual)";
            return go.transform;
        }

        void Release(Transform t)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            m_Pool.Push(t);
        }
    }
}
