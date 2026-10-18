using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Vida da lava (só apresentação, cada máquina gera a sua — não muda nada no jogo):
    /// bolhas que crescem e rebentam, gotas que saltam em arco e voltam à lava (ou somem ao tocar no chão),
    /// brasas, fumo localizado e luzes de calor a pulsar. Tudo com pooling e limites fixos, e só perto da câmara,
    /// para o custo não crescer com o tamanho do mapa. Os perigos reais são o <see cref="LavaArea"/> e os
    /// <see cref="LavaJets"/> (com aviso).
    /// </summary>
    public class LavaFx : MonoBehaviour
    {
        public Material bubbleMaterial;
        public Material dropMaterial;
        public Transform[] heatLights = new Transform[0];
        [Header("Orçamento")]
        public int maxBubbles = 14;
        public int maxDrops = 36;
        public int maxLights = 4;
        public float range = 32f;
        public float bubbleRate = 7f;      // por segundo, perto da câmara
        public float emberRate = 5f;
        public float smokeRate = 0.8f;

        class Bubble { public Transform t; public float t0, life, size; public bool active; }
        class Drop { public Transform t; public Vector3 v; public float surf; public bool active; }

        readonly List<Bubble> m_Bubbles = new List<Bubble>();
        readonly List<Drop> m_Drops = new List<Drop>();
        readonly List<Light> m_Lights = new List<Light>();
        float m_BubbleCarry, m_EmberCarry, m_SmokeCarry;
        static readonly Color Ember = new Color(1f, 0.5f, 0.12f);

        void Start()
        {
            var sphere = Art.PrimitiveMesh(PrimitiveType.Sphere);
            for (int i = 0; i < maxBubbles; i++) m_Bubbles.Add(new Bubble { t = Make("Bolha", sphere, bubbleMaterial) });
            for (int i = 0; i < maxDrops; i++) m_Drops.Add(new Drop { t = Make("Gota", sphere, dropMaterial) });
            for (int i = 0; i < heatLights.Length && i < maxLights; i++)
            {
                if (heatLights[i] == null) continue;
                var l = heatLights[i].gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.45f, 0.15f);
                l.range = 7f;
                l.intensity = 1.4f;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.Auto;
                m_Lights.Add(l);
            }
        }

        Transform Make(string n, Mesh mesh, Material mat)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.SetActive(false);
            return go.transform;
        }

        bool RandomLavaPoint(Vector3 around, out Vector3 p)
        {
            p = default;
            var area = LavaArea.Instance;
            if (area == null || area.LavaCells.Count == 0) return false;
            var cells = area.LavaCells;
            for (int k = 0; k < 6; k++)
            {
                var c = cells[Random.Range(0, cells.Count)];
                if ((c - around).sqrMagnitude > range * range) continue;
                p = c + new Vector3(Random.Range(-0.18f, 0.18f), 0f, Random.Range(-0.18f, 0.18f));
                return true;
            }
            return false;
        }

        void Update()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float dt = Time.deltaTime, now = Time.time;
            var eye = cam.transform.position;

            // ---- bolhas
            m_BubbleCarry += bubbleRate * dt;
            while (m_BubbleCarry >= 1f)
            {
                m_BubbleCarry -= 1f;
                var b = m_Bubbles.Find(x => !x.active);
                if (b == null || !RandomLavaPoint(eye, out var p)) break;
                b.active = true;
                b.t0 = now;
                b.life = Random.Range(0.7f, 1.4f);
                b.size = Random.Range(0.18f, 0.42f);
                b.t.position = p;
                b.t.gameObject.SetActive(true);
            }
            foreach (var b in m_Bubbles)
            {
                if (!b.active) continue;
                float u = (now - b.t0) / b.life;
                if (u >= 1f)
                {
                    Pop(b.t.position, b.size);
                    b.active = false;
                    b.t.gameObject.SetActive(false);
                    continue;
                }
                float s = b.size * Mathf.SmoothStep(0.1f, 1f, u);
                b.t.localScale = new Vector3(s, s * 0.65f, s);
            }

            // ---- gotas (balísticas; voltam à lava ou desaparecem ao tocar no chão)
            foreach (var d in m_Drops)
            {
                if (!d.active) continue;
                d.v += Physics.gravity * dt;
                var p = d.t.position + d.v * dt;
                d.t.position = p;
                bool inLava = LavaArea.Instance != null && LavaArea.Instance.IsLava(p, out float surf);
                if ((d.v.y < 0f && p.y <= d.surf) || (!inLava && p.y < d.surf + 0.05f) || p.y < d.surf - 1f)
                {
                    if (inLava) Fx.Glow(new Vector3(p.x, d.surf + 0.02f, p.z), Vector3.up * 0.3f, Ember, 0.12f, 0.25f);
                    d.active = false;
                    d.t.gameObject.SetActive(false);
                }
            }

            // ---- brasas e fumo localizado
            m_EmberCarry += emberRate * dt;
            while (m_EmberCarry >= 1f)
            {
                m_EmberCarry -= 1f;
                if (RandomLavaPoint(eye, out var p))
                    Fx.Glow(p + Vector3.up * 0.1f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.8f, 1.6f), Random.Range(-0.3f, 0.3f)),
                            Ember, Random.Range(0.06f, 0.12f), Random.Range(0.8f, 1.6f));
            }
            m_SmokeCarry += smokeRate * dt;
            while (m_SmokeCarry >= 1f)
            {
                m_SmokeCarry -= 1f;
                if (RandomLavaPoint(eye, out var p))
                    Fx.Smoke(p + Vector3.up * 0.3f, new Vector3(0f, 0.7f, 0f), Random.Range(0.9f, 1.4f), Random.Range(2.5f, 3.5f), 0.22f);
            }

            // ---- luz de calor a pulsar (moderada)
            for (int i = 0; i < m_Lights.Count; i++)
                m_Lights[i].intensity = 1.3f + 0.35f * Mathf.Sin(now * (1.1f + i * 0.17f) + i * 1.9f) + 0.1f * Mathf.PerlinNoise(now * 2f, i);
        }

        void Pop(Vector3 p, float size)
        {
            Fx.Ring(p + Vector3.up * 0.02f, Ember, size * 2.2f, 0.35f);
            int n = Random.Range(2, 5);
            for (int k = 0; k < n; k++)
            {
                var d = m_Drops.Find(x => !x.active);
                if (d == null) break;
                float a = Random.Range(0f, Mathf.PI * 2f);
                float sp = Random.Range(0.6f, 1.6f);
                d.v = new Vector3(Mathf.Cos(a) * sp, Random.Range(2.2f, 3.6f), Mathf.Sin(a) * sp);
                d.surf = p.y;
                d.active = true;
                float s = Random.Range(0.06f, 0.11f);
                d.t.localScale = Vector3.one * s;
                d.t.position = p + Vector3.up * 0.05f;
                d.t.gameObject.SetActive(true);
            }
            if (Random.value < 0.5f) Fx.Smoke(p + Vector3.up * 0.2f, Vector3.up * 0.6f, 0.5f, 1.2f, 0.25f);
        }
    }
}
