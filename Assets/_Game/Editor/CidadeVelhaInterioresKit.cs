using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Construtor de malhas para os interiores da Cidade Velha: junta primitivas (caixas, cilindros, tornos, esferas,
    /// prismas) em baldes por material, com UV projetada no mundo, cor por vértice e uma oclusão de ambiente simples
    /// "cozida" nos vértices (mais escuro junto ao chão). No fim cria uma malha por material.
    /// Também guarda colisores (caixas, podendo ser rodadas) e luzes, no mesmo referencial local.
    /// </summary>
    public class InteriorKit
    {
        class Buf
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public float uvScale = 0.5f;
            public bool ao = true;
        }

        public struct ColBox
        {
            public string name;
            public Vector3 center;
            public Vector3 size;
            public Quaternion rot;
            /// <summary>Bloqueia o movimento mas deixa passar tiros (ex.: grades de janela): vai para a layer Ignore Raycast.</summary>
            public bool shotThrough;
        }

        public struct LightDef
        {
            public string name;
            public Vector3 pos;
            public Color color;
            public float range, intensity;
            public LightShadows shadows;
        }

        readonly Dictionary<Material, Buf> m_Bufs = new Dictionary<Material, Buf>();
        readonly List<Material> m_Order = new List<Material>();
        readonly Stack<Matrix4x4> m_Stack = new Stack<Matrix4x4>();
        Matrix4x4 m_M = Matrix4x4.identity;
        Matrix4x4 m_N = Matrix4x4.identity;
        bool m_Mirror;
        public readonly List<ColBox> Cols = new List<ColBox>();
        public readonly List<LightDef> Lights = new List<LightDef>();

        /// <summary>Cor atual (multiplica a textura).</summary>
        public Color C = Color.white;
        /// <summary>Pisos (altura no mundo + retângulo XZ do mundo) para a oclusão junto ao chão.</summary>
        public readonly List<KeyValuePair<float, Rect>> Floors = new List<KeyValuePair<float, Rect>>();

        public void Floor(float y, float x0, float z0, float x1, float z1)
        {
            Floors.Add(new KeyValuePair<float, Rect>(y, Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(z0, z1), Mathf.Max(x0, x1), Mathf.Max(z0, z1))));
        }
        /// <summary>Multiplicador de oclusão (0 = sem oclusão).</summary>
        public float AoStrength = 1f;

        public Matrix4x4 Matrix => m_M;

        public void Setup(Material m, float uvScale, bool ao = true)
        {
            var b = B(m);
            b.uvScale = uvScale;
            b.ao = ao;
        }

        Buf B(Material m)
        {
            if (!m_Bufs.TryGetValue(m, out var b))
            {
                b = new Buf();
                m_Bufs[m] = b;
                m_Order.Add(m);
            }
            return b;
        }

        // ------------------------------------------------------------------ transformações

        public void Push(Vector3 pos, float yaw = 0f, float pitch = 0f, float roll = 0f)
        {
            Push(Matrix4x4.TRS(pos, Quaternion.Euler(pitch, yaw, roll), Vector3.one));
        }

        public void Push(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            Push(Matrix4x4.TRS(pos, rot, scale));
        }

        public void Push(Matrix4x4 m)
        {
            m_Stack.Push(m_M);
            m_M = m_M * m;
            m_N = m_M.inverse.transpose;
            m_Mirror = m_M.determinant < 0f;
        }

        public void Pop()
        {
            m_M = m_Stack.Pop();
            m_N = m_M.inverse.transpose;
            m_Mirror = m_M.determinant < 0f;
        }

        // ------------------------------------------------------------------ escrita de vértices

        Color Shade(Vector3 wp, Vector3 wn, Buf b)
        {
            var col = C;
            if (!b.ao || AoStrength <= 0f) return col;
            float f = float.MinValue;
            var xz = new Vector2(wp.x, wp.z);
            foreach (var fl in Floors)
                if (fl.Key <= wp.y + 0.02f && fl.Key > f && fl.Value.Contains(xz)) f = fl.Key;
            float k = 1f;
            if (f > float.MinValue)
            {
                float d = wp.y - f;
                k = Mathf.Lerp(0.58f, 1f, Mathf.SmoothStep(0f, 1f, d / 0.6f));
            }
            if (wn.y < -0.5f) k *= 0.8f;            // faces de baixo
            k = Mathf.Lerp(1f, k, AoStrength);
            return new Color(col.r * k, col.g * k, col.b * k, col.a);
        }

        Vector2 Uv(Vector3 wp, Vector3 wn, float s)
        {
            float ax = Mathf.Abs(wn.x), ay = Mathf.Abs(wn.y), az = Mathf.Abs(wn.z);
            if (ay >= ax && ay >= az) return new Vector2(wp.x * s, wp.z * s);
            if (ax >= az) return new Vector2(wp.z * s, wp.y * s);
            return new Vector2(wp.x * s, wp.y * s);
        }

        int Vert(Buf b, Vector3 lp, Vector3 ln)
        {
            var wp = m_M.MultiplyPoint3x4(lp);
            var wn = m_N.MultiplyVector(ln).normalized;
            b.v.Add(wp);
            b.n.Add(wn);
            b.uv.Add(Uv(wp, wn, b.uvScale));
            b.c.Add(Shade(wp, wn, b));
            return b.v.Count - 1;
        }

        /// <summary>Polígono convexo plano (vértices em ordem). O lado visível é escolhido por <paramref name="outward"/>.</summary>
        public void Poly(Material m, Vector3[] p, Vector3 outward)
        {
            if (p.Length < 3) return;
            var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (n.sqrMagnitude < 1e-12f)
                for (int i = 2; i < p.Length && n.sqrMagnitude < 1e-12f; i++) n = Vector3.Cross(p[1] - p[0], p[i] - p[0]);
            bool flip = Vector3.Dot(n, outward) < 0f;
            n = (flip ? -n : n).normalized;
            bool rev = flip ^ m_Mirror;
            var b = B(m);
            int s = b.v.Count;
            for (int i = 0; i < p.Length; i++) Vert(b, p[rev ? p.Length - 1 - i : i], n);
            for (int i = 1; i < p.Length - 1; i++)
            {
                b.t.Add(s);
                b.t.Add(s + i);
                b.t.Add(s + i + 1);
            }
        }

        /// <summary>Quadrilátero com normais por vértice (superfícies suaves). Ordem no sentido dos ponteiros visto de fora.</summary>
        void QuadN(Buf b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector3 na, Vector3 n1, Vector3 n2, Vector3 nd)
        {
            int s = b.v.Count;
            Vert(b, a, na);
            Vert(b, c1, n1);
            Vert(b, c2, n2);
            Vert(b, d, nd);
            if (m_Mirror)
            {
                b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1);
                b.t.Add(s); b.t.Add(s + 3); b.t.Add(s + 2);
            }
            else
            {
                b.t.Add(s); b.t.Add(s + 1); b.t.Add(s + 2);
                b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 3);
            }
        }

        // ------------------------------------------------------------------ primitivas

        public void Box(Material m, Vector3 a, Vector3 b)
        {
            var mn = Vector3.Min(a, b);
            var mx = Vector3.Max(a, b);
            Vector3[] c =
            {
                new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z),
                new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z),
            };
            Poly(m, new[] { c[0], c[3], c[2], c[1] }, Vector3.back);
            Poly(m, new[] { c[5], c[6], c[7], c[4] }, Vector3.forward);
            Poly(m, new[] { c[4], c[7], c[3], c[0] }, Vector3.left);
            Poly(m, new[] { c[1], c[2], c[6], c[5] }, Vector3.right);
            Poly(m, new[] { c[3], c[7], c[6], c[2] }, Vector3.up);
            Poly(m, new[] { c[4], c[0], c[1], c[5] }, Vector3.down);
        }

        /// <summary>Caixa pelo centro da base (x/z) e altura a partir de y0.</summary>
        public void BoxB(Material m, Vector3 baseCenter, Vector3 size)
        {
            var a = new Vector3(baseCenter.x - size.x * 0.5f, baseCenter.y, baseCenter.z - size.z * 0.5f);
            Box(m, a, a + size);
        }

        public void BoxC(Material m, Vector3 center, Vector3 size)
        {
            Box(m, center - size * 0.5f, center + size * 0.5f);
        }

        /// <summary>Superfície de revolução em torno do eixo Y local. perfil = (raio, altura).</summary>
        public void Lathe(Material m, Vector3 baseP, Vector2[] prof, int seg, bool smooth = true, bool capBottom = true, bool capTop = true)
        {
            var b = B(m);
            for (int i = 0; i < prof.Length - 1; i++)
            {
                var p0 = prof[i];
                var p1 = prof[i + 1];
                float dr = p1.x - p0.x, dy = p1.y - p0.y;
                // normal do perfil (para fora): (dy, -dr) normalizado
                var pn = new Vector2(dy, -dr);
                if (pn.sqrMagnitude < 1e-10f) continue;
                pn.Normalize();
                for (int s = 0; s < seg; s++)
                {
                    float a0 = s * Mathf.PI * 2f / seg, a1 = (s + 1) * Mathf.PI * 2f / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var v00 = baseP + d0 * p0.x + Vector3.up * p0.y;
                    var v01 = baseP + d1 * p0.x + Vector3.up * p0.y;
                    var v10 = baseP + d0 * p1.x + Vector3.up * p1.y;
                    var v11 = baseP + d1 * p1.x + Vector3.up * p1.y;
                    Vector3 n0, n1;
                    if (smooth)
                    {
                        n0 = (d0 * pn.x + Vector3.up * pn.y).normalized;
                        n1 = (d1 * pn.x + Vector3.up * pn.y).normalized;
                    }
                    else
                    {
                        var dm = (d0 + d1).normalized;
                        n0 = n1 = (dm * pn.x + Vector3.up * pn.y).normalized;
                    }
                    // vista de fora: v00 → v10 → v11 → v01 (sentido horário)
                    QuadN(b, v00, v10, v11, v01, n0, n0, n1, n1);
                }
            }
            if (capBottom && prof[0].x > 0.001f) Disc(m, baseP + Vector3.up * prof[0].y, prof[0].x, seg, false);
            var last = prof[prof.Length - 1];
            if (capTop && last.x > 0.001f) Disc(m, baseP + Vector3.up * last.y, last.x, seg, true);
        }

        public void Disc(Material m, Vector3 c, float r, int seg, bool up)
        {
            var p = new Vector3[seg];
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                p[s] = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            Poly(m, p, up ? Vector3.up : Vector3.down);
        }

        public void Cyl(Material m, Vector3 baseP, float r, float h, int seg, bool smooth = true, bool caps = true)
        {
            Lathe(m, baseP, new[] { new Vector2(r, 0f), new Vector2(r, h) }, seg, smooth, caps, caps);
        }

        public void Cone(Material m, Vector3 baseP, float r0, float r1, float h, int seg, bool smooth = true, bool caps = true)
        {
            Lathe(m, baseP, new[] { new Vector2(r0, 0f), new Vector2(r1, h) }, seg, smooth, caps, caps);
        }

        /// <summary>Cilindro entre dois pontos (locais).</summary>
        public void Rod(Material m, Vector3 a, Vector3 b, float r, int seg = 6, bool caps = true)
        {
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Push(a, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one);
            Cyl(m, Vector3.zero, r, len, seg, true, caps);
            Pop();
        }

        public void Sphere(Material m, Vector3 c, float r, int seg = 8, int rings = 5)
        {
            var prof = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float t = Mathf.PI * i / rings;
                prof[i] = new Vector2(Mathf.Sin(t) * r, -Mathf.Cos(t) * r);
            }
            prof[0].x = 0f;
            prof[rings].x = 0f;
            Lathe(m, c, prof, seg, true, false, false);
        }

        public void Ellipsoid(Material m, Vector3 c, Vector3 radii, int seg = 8, int rings = 5)
        {
            Push(c, Quaternion.identity, radii);
            Sphere(m, Vector3.zero, 1f, seg, rings);
            Pop();
        }

        /// <summary>Toro no plano XZ local (eixo Y).</summary>
        public void Torus(Material m, Vector3 c, float R, float r, int segU = 16, int segV = 6, float a0 = 0f, float a1 = Mathf.PI * 2f)
        {
            var b = B(m);
            for (int i = 0; i < segU; i++)
            {
                float u0 = Mathf.Lerp(a0, a1, i / (float)segU), u1 = Mathf.Lerp(a0, a1, (i + 1) / (float)segU);
                Vector3 d0 = new Vector3(Mathf.Cos(u0), 0f, Mathf.Sin(u0)), d1 = new Vector3(Mathf.Cos(u1), 0f, Mathf.Sin(u1));
                for (int j = 0; j < segV; j++)
                {
                    float v0 = j * Mathf.PI * 2f / segV, v1 = (j + 1) * Mathf.PI * 2f / segV;
                    Vector3 n00 = d0 * Mathf.Cos(v0) + Vector3.up * Mathf.Sin(v0);
                    Vector3 n01 = d0 * Mathf.Cos(v1) + Vector3.up * Mathf.Sin(v1);
                    Vector3 n10 = d1 * Mathf.Cos(v0) + Vector3.up * Mathf.Sin(v0);
                    Vector3 n11 = d1 * Mathf.Cos(v1) + Vector3.up * Mathf.Sin(v1);
                    var p00 = c + d0 * R + n00 * r;
                    var p01 = c + d0 * R + n01 * r;
                    var p10 = c + d1 * R + n10 * r;
                    var p11 = c + d1 * R + n11 * r;
                    // escolhe a ordem pela normal
                    var cr = Vector3.Cross(p01 - p00, p11 - p00);
                    if (Vector3.Dot(cr, n00 + n11) >= 0f) QuadN(b, p00, p01, p11, p10, n00, n01, n11, n10);
                    else QuadN(b, p00, p10, p11, p01, n00, n10, n11, n01);
                }
            }
        }

        /// <summary>Prisma: polígono convexo no plano XY local, extrudido de z0 a z1.</summary>
        public void Prism(Material m, Vector2[] poly, float z0, float z1)
        {
            int n = poly.Length;
            var f0 = new Vector3[n];
            var f1 = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                f0[i] = new Vector3(poly[i].x, poly[i].y, z0);
                f1[i] = new Vector3(poly[i].x, poly[i].y, z1);
            }
            Poly(m, f0, Vector3.back);
            Poly(m, f1, Vector3.forward);
            var ctr = Vector2.zero;
            foreach (var p in poly) ctr += p;
            ctr /= n;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var mid = (poly[i] + poly[j]) * 0.5f - ctr;
                Poly(m, new[] { f0[i], f0[j], f1[j], f1[i] }, new Vector3(mid.x, mid.y, 0f));
            }
        }

        /// <summary>Arco (anel parcial) no plano XY local: centro, raios, ângulos em graus (0 = +X), extrudido de z0 a z1.</summary>
        public void ArchRing(Material m, Vector2 c, float rIn, float rOut, float deg0, float deg1, int seg, float z0, float z1)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = Mathf.Deg2Rad * Mathf.Lerp(deg0, deg1, i / (float)seg);
                float a1 = Mathf.Deg2Rad * Mathf.Lerp(deg0, deg1, (i + 1) / (float)seg);
                var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                Prism(m, new[] { c + d0 * rIn, c + d0 * rOut, c + d1 * rOut, c + d1 * rIn }, z0, z1);
            }
        }

        /// <summary>Painel em arco de volta perfeita (retângulo + meio círculo) no plano XY, extrudido.</summary>
        public void ArchPanel(Material m, float cx, float y0, float half, float spring, float z0, float z1, int seg = 12)
        {
            var poly = new List<Vector2> { new Vector2(cx - half, y0) };
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.PI - Mathf.PI * i / seg;
                poly.Add(new Vector2(cx + Mathf.Cos(a) * half, spring + Mathf.Sin(a) * half));
            }
            poly.Add(new Vector2(cx + half, y0));
            Prism(m, poly.ToArray(), z0, z1);
        }

        /// <summary>Quad horizontal (sombra de contacto) — sem oclusão.</summary>
        public void FlatQuad(Material m, Vector3 c, float sx, float sz, float yaw = 0f)
        {
            Push(c, yaw);
            Poly(m, new[]
            {
                new Vector3(-sx * 0.5f, 0f, -sz * 0.5f), new Vector3(-sx * 0.5f, 0f, sz * 0.5f),
                new Vector3(sx * 0.5f, 0f, sz * 0.5f), new Vector3(sx * 0.5f, 0f, -sz * 0.5f),
            }, Vector3.up);
            Pop();
        }

        // ------------------------------------------------------------------ colisores e luzes

        public void Col(string name, Vector3 a, Vector3 b, bool shotThrough = false)
        {
            var mn = Vector3.Min(a, b);
            var mx = Vector3.Max(a, b);
            var lc = (mn + mx) * 0.5f;
            Cols.Add(new ColBox { name = name, center = m_M.MultiplyPoint3x4(lc), size = mx - mn, rot = m_M.rotation, shotThrough = shotThrough });
        }

        /// <summary>Colisor rodado: centro e rotação locais.</summary>
        public void ColRot(string name, Vector3 center, Vector3 size, Quaternion rot)
        {
            Cols.Add(new ColBox { name = name, center = m_M.MultiplyPoint3x4(center), size = size, rot = m_M.rotation * rot });
        }

        public void Light(string name, Vector3 p, Color c, float range, float intensity, LightShadows sh)
        {
            Lights.Add(new LightDef { name = name, pos = m_M.MultiplyPoint3x4(p), color = c, range = range, intensity = intensity, shadows = sh });
        }

        // ------------------------------------------------------------------ saída

        public int Build(Transform parent, string prefix, System.Func<Mesh, Mesh> save, System.Func<Material, ShadowCastingMode> shadows)
        {
            int tris = 0;
            foreach (var mat in m_Order)
            {
                var b = m_Bufs[mat];
                if (b.t.Count == 0) continue;
                var mesh = new Mesh { name = prefix + "_" + mat.name.Replace("M_CVI_", "").Replace("M_CV_", "") };
                if (b.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(b.v);
                mesh.SetNormals(b.n);
                mesh.SetUVs(0, b.uv);
                mesh.SetColors(b.c);
                mesh.SetTriangles(b.t, 0);
                mesh.RecalculateBounds();
                mesh = save(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows(mat);
                r.receiveShadows = true;
                tris += b.t.Count / 3;
            }
            return tris;
        }
    }
}
