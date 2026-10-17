using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Visual de arbusto feito de folhas (malha procedural, sem assets): cúpulas cobertas de folhas em forma de
    /// amêndoa, gordinhas e com nervura central, à volta de um núcleo escuro que tapa os buracos.
    /// Cada <see cref="seed"/> dá um arbusto diferente: paleta, tamanho/forma das folhas, abertura, cúpulas e cor
    /// de cada folha. A malha é gerada ao ativar (no editor e no build); a regra de esconder continua no <see cref="Bush"/>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class LeafBush : MonoBehaviour
    {
        [Tooltip("Pegada no chão (X/Z) e altura (Y), em metros, no espaço local.")]
        public Vector3 size = new Vector3(3f, 1.5f, 3f);

        [Tooltip("Muda este número para obter outra variação do arbusto.")]
        public int seed = 1;

        [Tooltip("Quantidade de folhas (1 = normal).")]
        [Range(0.4f, 2f)] public float density = 1f;

        [Tooltip("-1 = escolhida pela seed; 0 = verde fresco, 1 = verde profundo, 2 = lima, 3 = verde-água.")]
        [Range(-1, 3)] public int palette = -1;

        Mesh m_Mesh;
        bool m_Dirty = true;

        void OnEnable() => Rebuild();
        void OnValidate() => m_Dirty = true;

        void Update()
        {
            if (m_Dirty) Rebuild();
        }

        void OnDestroy()
        {
            if (m_Mesh == null) return;
            if (Application.isPlaying) Destroy(m_Mesh);
            else DestroyImmediate(m_Mesh);
        }

        [ContextMenu("Regenerar")]
        public void Rebuild()
        {
            m_Dirty = false;
            if (m_Mesh == null)
            {
                m_Mesh = new Mesh { name = "LeafBush", hideFlags = HideFlags.DontSave };
                m_Mesh.indexFormat = IndexFormat.UInt32;
            }
            Generate(m_Mesh, size, seed, density, palette);
            var mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = m_Mesh;
        }

        // ------------------------------------------------------------------ geração

        // (núcleo, folha escura, folha clara)
        static readonly Color[][] s_Palettes =
        {
            new[] { new Color(0.10f, 0.30f, 0.10f), new Color(0.24f, 0.52f, 0.16f), new Color(0.58f, 0.84f, 0.30f) }, // verde fresco
            new[] { new Color(0.08f, 0.25f, 0.11f), new Color(0.16f, 0.44f, 0.17f), new Color(0.42f, 0.72f, 0.28f) }, // verde profundo
            new[] { new Color(0.16f, 0.32f, 0.07f), new Color(0.34f, 0.58f, 0.12f), new Color(0.72f, 0.88f, 0.32f) }, // lima
            new[] { new Color(0.07f, 0.26f, 0.16f), new Color(0.17f, 0.47f, 0.27f), new Color(0.50f, 0.80f, 0.40f) }, // verde-água
        };

        const int LeafRings = 5;      // anéis ao longo da folha
        const int LeafSides = 8;      // pontos na secção (inclui o topo = nervura)
        const int MaxLeavesPerDome = 400;

        struct Dome
        {
            public Vector3 c, r;
            public float phase;
        }

        public static void Generate(Mesh mesh, Vector3 size, int seed, float density, int paletteIndex)
        {
            var rng = new System.Random(seed);
            System.Func<float, float, float> R = (a, b) => a + (b - a) * (float)rng.NextDouble();

            // Mais verde fresco/lima (como a referência); verde-água é o mais raro
            double pr = rng.NextDouble();
            int rolled = pr < 0.38 ? 0 : pr < 0.62 ? 2 : pr < 0.86 ? 1 : 3;
            var pal = s_Palettes[paletteIndex >= 0 ? Mathf.Clamp(paletteIndex, 0, s_Palettes.Length - 1) : rolled];
            Color core = pal[0] * 0.8f, dark = pal[1], light = pal[2];
            var tint = new Color(R(-0.04f, 0.04f), R(-0.03f, 0.03f), R(-0.03f, 0.03f), 0f);

            // Personalidade do arbusto
            float leafLen = 0.5f * R(0.88f, 1.15f);
            float widR = R(0.56f, 0.68f);
            float thickR = R(0.2f, 0.26f);
            float crease = R(0.35f, 0.55f);
            float lift = R(28f, 46f) * Mathf.Deg2Rad;
            float curl = R(-0.14f, -0.02f);
            float lump = R(0.04f, 0.09f);
            density = Mathf.Clamp(density, 0.1f, 4f);

            float sx = Mathf.Max(0.5f, size.x), sy = Mathf.Max(0.4f, size.y), sz = Mathf.Max(0.5f, size.z);
            const float step = 1.7f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(sx / step));
            int nz = Mathf.Max(1, Mathf.RoundToInt(sz / step));
            var domes = new List<Dome>(nx * nz);
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    float cx = -sx * 0.5f + (ix + 0.5f) * sx / nx + R(-0.15f, 0.15f);
                    float cz = -sz * 0.5f + (iz + 0.5f) * sz / nz + R(-0.15f, 0.15f);
                    float rx = Mathf.Max(0.85f, sx / nx * 0.5f * 1.3f) * R(0.92f, 1.08f);
                    float rz = Mathf.Max(0.85f, sz / nz * 0.5f * 1.3f) * R(0.92f, 1.08f);
                    float cy = -0.3f * R(0.8f, 1.2f);
                    float top = sy * R(0.88f, 1.12f);
                    domes.Add(new Dome { c = new Vector3(cx, cy, cz), r = new Vector3(rx, top - cy, rz), phase = R(0f, 100f) });
                }

            var V = new List<Vector3>(40000);
            var C = new List<Color>(40000);
            var T = new List<int>(70000);

            // Núcleo escuro de cada cúpula (só a parte acima do chão)
            foreach (var d in domes)
            {
                const int m = 10, n = 6;
                float yc = Mathf.Max(-1f, (-d.c.y - 0.1f) / d.r.y);
                float phMax = Mathf.Acos(Mathf.Clamp(yc, -1f, 1f));
                int b0 = V.Count;
                for (int a = 0; a <= n; a++)
                {
                    float phi = phMax * a / n;
                    for (int b = 0; b < m; b++)
                    {
                        float th = 2f * Mathf.PI * b / m;
                        var dir = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                        V.Add(d.c + Vector3.Scale(dir, d.r) * 0.82f);
                        C.Add(core);
                    }
                }
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < m; b++)
                    {
                        int i0 = b0 + a * m + b, i1 = b0 + a * m + (b + 1) % m, i2 = i0 + m, i3 = i1 + m;
                        T.Add(i0); T.Add(i1); T.Add(i2);
                        T.Add(i1); T.Add(i3); T.Add(i2);
                    }
            }

            // Folhas: distribuídas por área ao longo da cúpula (sequência de Fibonacci)
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            const int K = 48;
            var ys = new float[K + 1];
            var cum = new float[K + 1];
            for (int k = 0; k < domes.Count; k++)
            {
                var d = domes[k];
                float a_ = Mathf.Sqrt(d.r.x * d.r.z), c_ = d.r.y;
                float y0 = Mathf.Max(-1f, (-d.c.y - 0.05f) / d.r.y);
                for (int q = 0; q <= K; q++) ys[q] = 1f - (1f - y0) * q / K;
                cum[0] = 0f;
                for (int q = 0; q < K; q++)
                {
                    float d0 = Mathf.Sqrt(a_ * a_ * ys[q] * ys[q] + c_ * c_ * (1f - ys[q] * ys[q]));
                    float d1 = Mathf.Sqrt(a_ * a_ * ys[q + 1] * ys[q + 1] + c_ * c_ * (1f - ys[q + 1] * ys[q + 1]));
                    cum[q + 1] = cum[q] + (d0 + d1) * 0.5f * (1f - y0) / K;
                }
                float area = 2f * Mathf.PI * a_ * cum[K];
                int count = Mathf.Min(MaxLeavesPerDome, (int)(area / (leafLen * leafLen * widR * 0.75f) * 1.6f * density));

                int qi = 0;
                for (int i = 0; i < count; i++)
                {
                    float tq = (i + 0.5f) / count * cum[K];
                    while (qi < K - 1 && cum[qi + 1] < tq) qi++;
                    float f = (tq - cum[qi]) / Mathf.Max(1e-6f, cum[qi + 1] - cum[qi]);
                    float y = Mathf.Lerp(ys[qi], ys[qi + 1], f);
                    float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                    float ang = i * golden + d.phase + R(-0.25f, 0.25f);
                    var dir = new Vector3(rr * Mathf.Cos(ang), y, rr * Mathf.Sin(ang));
                    float bump = 1f + lump * Mathf.Sin(3f * ang + d.phase) * Mathf.Sin(4f * y + d.phase);
                    var p = d.c + Vector3.Scale(dir, d.r) * bump;
                    if (p.y < -0.05f || InsideOther(domes, k, p)) continue;

                    // Orientação: a folha nasce dentro da cúpula e aponta para fora/baixo, como escamas
                    var nrm = new Vector3(dir.x / d.r.x, dir.y / d.r.y, dir.z / d.r.z).normalized;
                    Vector3 t;
                    if (Mathf.Abs(nrm.y) > 0.97f)
                    {
                        float a2 = R(0f, 2f * Mathf.PI);
                        t = new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2));
                    }
                    else t = (Vector3.down - nrm * Vector3.Dot(Vector3.down, nrm)).normalized;
                    float roll = R(-45f, 45f) * Mathf.Deg2Rad;
                    var bn = Vector3.Cross(nrm, t);
                    t = (t * Mathf.Cos(roll) + bn * Mathf.Sin(roll)).normalized;
                    float la = Mathf.Min(48f * Mathf.Deg2Rad, lift * R(0.75f, 1.25f) * (0.55f + 0.45f * Mathf.Max(0f, y)));
                    var L = (t * Mathf.Cos(la) + nrm * Mathf.Sin(la)).normalized;
                    var N = (nrm - L * Vector3.Dot(L, nrm)).normalized;
                    var W = Vector3.Cross(N, L);

                    float ln = leafLen * R(0.85f, 1.15f) * (1.12f - 0.25f * Mathf.Max(0f, y));
                    float wd = ln * widR * R(0.9f, 1.1f), th = ln * thickR;
                    var basePos = p - L * (ln * 0.32f) - nrm * 0.03f;

                    // Cor: folhas viradas para cima mais claras (sol), variação individual
                    float tc = Mathf.Pow((float)rng.NextDouble(), 0.8f) * 0.75f + 0.35f * Mathf.Max(0f, nrm.y) - 0.1f;
                    float j = R(-0.02f, 0.02f);
                    var col = Color.Lerp(dark, light, Mathf.Clamp01(tc)) + tint;
                    col = new Color(Mathf.Clamp01(col.r + j), Mathf.Clamp01(col.g + j), Mathf.Clamp01(col.b + j), 1f);

                    AddLeaf(V, C, T, basePos, L, W, N, ln, wd, th, crease, curl, col);
                }
            }

            mesh.Clear();
            mesh.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(V);
            mesh.SetColors(C);
            mesh.SetTriangles(T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        static bool InsideOther(List<Dome> domes, int skip, Vector3 p)
        {
            for (int k = 0; k < domes.Count; k++)
            {
                if (k == skip) continue;
                var q = p - domes[k].c;
                var r = domes[k].r * 0.96f;
                float x = q.x / r.x, y = q.y / r.y, z = q.z / r.z;
                if (x * x + y * y + z * z < 1f) return true;
            }
            return false;
        }

        /// <summary>Uma folha: amêndoa fechada (topo inchado com sulco no meio, fundo achatado), ponta curvada.</summary>
        static void AddLeaf(List<Vector3> V, List<Color> C, List<int> T, Vector3 basePos, Vector3 L, Vector3 W, Vector3 N,
            float len, float wid, float thick, float crease, float curl, Color col)
        {
            var vein = new Color(col.r * 0.78f, col.g * 0.78f, col.b * 0.78f, 1f);
            int vb = V.Count;
            for (int k = 0; k <= LeafRings; k++)
            {
                float u = k / (float)LeafRings;
                float w = wid * 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.85f))), 0.6f);
                float tt = thick * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 0.5f);
                float bend = curl * len * u * u;
                var center = basePos + L * (u * len);
                for (int j = 0; j < LeafSides; j++)
                {
                    float a = 2f * Mathf.PI * j / LeafSides;
                    float cc = Mathf.Cos(a), ss = Mathf.Sin(a);
                    float up = ss >= 0f
                        ? Mathf.Pow(ss, 0.7f) * tt * (1f - crease * Mathf.Exp(-cc * cc / 0.06f))
                        : ss * tt * 0.35f;
                    V.Add(center + W * (w * cc) + N * (up + bend));
                    C.Add(ss > 0.9f ? vein : col);
                }
            }
            for (int k = 0; k < LeafRings; k++)
                for (int j = 0; j < LeafSides; j++)
                {
                    int i0 = vb + k * LeafSides + j, i1 = vb + k * LeafSides + (j + 1) % LeafSides;
                    int i2 = i0 + LeafSides, i3 = i1 + LeafSides;
                    T.Add(i0); T.Add(i1); T.Add(i2);
                    T.Add(i1); T.Add(i3); T.Add(i2);
                }
        }
    }
}
