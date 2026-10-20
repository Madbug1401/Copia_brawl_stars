using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Constrói malhas orgânicas (tubos varridos ao longo de uma curva, com secção elíptica, e elipsoides) com cor por
    /// vértice, para usar com os materiais cartoon "VC". Uma só malha por mão/antebraço dá uma silhueta contínua com um
    /// único contorno, em vez de muitas primitivas cada uma com o seu contorno (aspeto de manequim articulado).
    /// </summary>
    public class OrganicMesh
    {
        readonly List<Vector3> m_V = new List<Vector3>();
        readonly List<Vector3> m_N = new List<Vector3>();
        readonly List<Color> m_C = new List<Color>();
        readonly List<int> m_T = new List<int>();

        public bool IsEmpty => m_V.Count == 0;

        /// <summary>Um ponto do tubo: posição, meia-largura (ao longo do eixo lateral) e meia-espessura, e cor.</summary>
        public struct Ring
        {
            public Vector3 p;
            public float rx, ry;
            public Color c;
            public Ring(Vector3 p, float rx, float ry, Color c) { this.p = p; this.rx = rx; this.ry = ry; this.c = c; }
            public Ring(Vector3 p, float r, Color c) : this(p, r, r, c) { }
        }

        /// <summary>
        /// Tubo pelos anéis dados. 'side' orienta a largura (rx) da secção; capStart/capEnd fecham as pontas com uma
        /// calote arredondada (senão ficam abertas, como uma manga).
        /// </summary>
        public void Tube(IList<Ring> rings, Vector3 side, bool capStart, bool capEnd, int sides = 12, int capRings = 3, float capLength = 1f)
        {
            int n = rings.Count;
            if (n < 2) return;
            var tan = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var a = rings[Mathf.Max(0, i - 1)].p;
                var b = rings[Mathf.Min(n - 1, i + 1)].p;
                var t = b - a;
                // anéis repetidos (mudança de cor): procura vizinhos diferentes
                for (int k = 2; t.sqrMagnitude < 1e-10f && k < n; k++)
                    t = rings[Mathf.Min(n - 1, i + k)].p - rings[Mathf.Max(0, i - k)].p;
                tan[i] = t.sqrMagnitude > 1e-12f ? t.normalized : (i > 0 ? tan[i - 1] : Vector3.up);
            }

            Vector3 prevS = Vector3.zero;
            int first = -1, prevRing = -1;
            var sAxis = new Vector3[n];
            var bAxis = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var T = tan[i];
                var S = side - T * Vector3.Dot(side, T);
                if (S.sqrMagnitude < 1e-6f) S = prevS - T * Vector3.Dot(prevS, T);
                if (S.sqrMagnitude < 1e-6f) S = Vector3.Cross(T, Mathf.Abs(T.y) < 0.9f ? Vector3.up : Vector3.right);
                S.Normalize();
                prevS = S;
                sAxis[i] = S;
                bAxis[i] = Vector3.Cross(T, S);
            }

            // calote inicial
            if (capStart) prevRing = Cap(rings[0], -tan[0], sAxis[0], bAxis[0], sides, capRings, capLength, out first);

            for (int i = 0; i < n; i++)
            {
                // a normal inclina-se com a variação do raio (num cone aponta um pouco para trás; num degrau plano,
                // como a bainha de uma manga, fica ao longo do eixo) — senão a orientação dos triângulos fica ambígua
                var ra = rings[Mathf.Max(0, i - 1)];
                var rb = rings[Mathf.Min(n - 1, i + 1)];
                float dr = (rb.rx + rb.ry - ra.rx - ra.ry) * 0.5f;
                float ds = Mathf.Abs(Vector3.Dot(rb.p - ra.p, tan[i]));
                float slope = Mathf.Abs(dr) < 1e-6f ? 0f : dr / Mathf.Max(ds, 1e-5f);
                float w = -slope / Mathf.Sqrt(1f + slope * slope);
                int ring = AddRing(rings[i].p, sAxis[i], bAxis[i], rings[i].rx, rings[i].ry, rings[i].c, sides, tan[i], w);
                if (prevRing >= 0) Bridge(prevRing, ring, sides);
                prevRing = ring;
            }

            // calote final
            if (capEnd)
            {
                var last = rings[n - 1];
                float rc = (last.rx + last.ry) * 0.5f * capLength;
                int ring = prevRing;
                for (int k = 1; k < capRings; k++)
                {
                    float phi = k / (float)capRings * Mathf.PI * 0.5f;
                    float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                    int r2 = AddRing(last.p + tan[n - 1] * (sn * rc), sAxis[n - 1], bAxis[n - 1], last.rx * cs, last.ry * cs, last.c, sides, tan[n - 1], sn);
                    Bridge(ring, r2, sides);
                    ring = r2;
                }
                int tip = AddVertex(last.p + tan[n - 1] * rc, tan[n - 1], last.c);
                for (int j = 0; j < sides; j++) Tri(ring + j, ring + (j + 1) % sides, tip);
            }
        }

        /// <summary>Calote (meia esfera achatada) antes do primeiro anel. Devolve o último anel da calote.</summary>
        int Cap(Ring r, Vector3 outward, Vector3 S, Vector3 B, int sides, int capRings, float capLength, out int tipIndex)
        {
            float rc = (r.rx + r.ry) * 0.5f * capLength;
            tipIndex = AddVertex(r.p + outward * rc, outward, r.c);
            int prev = -1;
            for (int k = capRings - 1; k >= 1; k--)
            {
                float phi = k / (float)capRings * Mathf.PI * 0.5f;
                float cs = Mathf.Cos(phi), sn = Mathf.Sin(phi);
                int ring = AddRing(r.p + outward * (sn * rc), S, B, r.rx * cs, r.ry * cs, r.c, sides, -outward, -sn);
                if (prev < 0) for (int j = 0; j < sides; j++) Tri(tipIndex, ring + (j + 1) % sides, ring + j);
                else Bridge(prev, ring, sides);
                prev = ring;
            }
            return prev;
        }

        /// <summary>Elipsoide com raios 'radii' (eixos locais de 'rot').</summary>
        public void Ellipsoid(Vector3 center, Quaternion rot, Vector3 radii, Color c, int seg = 10)
        {
            int lat = Mathf.Max(3, seg / 2 + 1), lon = seg;
            int start = m_V.Count;
            for (int i = 0; i <= lat; i++)
            {
                float th = i / (float)lat * Mathf.PI;
                for (int j = 0; j < lon; j++)
                {
                    float ph = j / (float)lon * Mathf.PI * 2f;
                    var u = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    var p = Vector3.Scale(u, radii);
                    var nrm = new Vector3(u.x / Mathf.Max(radii.x, 1e-5f), u.y / Mathf.Max(radii.y, 1e-5f), u.z / Mathf.Max(radii.z, 1e-5f));
                    AddVertex(center + rot * p, (rot * nrm).normalized, c);
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = start + i * lon + j, b = start + i * lon + (j + 1) % lon;
                    int d = a + lon, e = b + lon;
                    Quad(a, b, e, d);
                }
        }

        int AddRing(Vector3 p, Vector3 S, Vector3 B, float rx, float ry, Color c, int sides, Vector3 T, float tangentWeight)
        {
            int start = m_V.Count;
            rx = Mathf.Max(rx, 1e-5f); ry = Mathf.Max(ry, 1e-5f);
            for (int j = 0; j < sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                var radial = (S * (ca / rx) + B * (sa / ry)).normalized;
                var nrm = (radial * Mathf.Sqrt(Mathf.Max(0f, 1f - tangentWeight * tangentWeight)) + T * tangentWeight).normalized;
                AddVertex(p + S * (ca * rx) + B * (sa * ry), nrm, c);
            }
            return start;
        }

        int AddVertex(Vector3 p, Vector3 n, Color c)
        {
            m_V.Add(p); m_N.Add(n); m_C.Add(c);
            return m_V.Count - 1;
        }

        void Bridge(int a, int b, int sides)
        {
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                Quad(a + j, a + j1, b + j1, b + j);
            }
        }

        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>Triângulo virado para fora (compara com as normais dos vértices: não depende da ordem dos anéis).</summary>
        void Tri(int a, int b, int c)
        {
            var f = Vector3.Cross(m_V[b] - m_V[a], m_V[c] - m_V[a]);
            if (f.sqrMagnitude < 1e-16f) { m_T.Add(a); m_T.Add(b); m_T.Add(c); return; }
            if (Vector3.Dot(f, m_N[a] + m_N[b] + m_N[c]) < 0f) { m_T.Add(a); m_T.Add(c); m_T.Add(b); }
            else { m_T.Add(a); m_T.Add(b); m_T.Add(c); }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (m_V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(m_V);
            m.SetNormals(m_N);
            m.SetColors(m_C);
            m.SetTriangles(m_T, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Cria um filho com esta malha (material cartoon com cor por vértice, com ou sem contorno).</summary>
        public Transform Attach(Transform parent, string name, bool outline)
        {
            var mesh = ToMesh(name);
            var t = AttachShared(parent, name, mesh, outline);
            t.gameObject.AddComponent<OwnedMesh>().mesh = mesh;   // malha só deste objeto: destrói-se com ele
            return t;
        }

        /// <summary>Cria um filho que usa uma malha já existente (partilhada; não é destruída com o objeto).</summary>
        public static Transform AttachShared(Transform parent, string name, Mesh mesh, bool outline)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = outline ? Art.ToonOutlineVC : Art.ToonVC;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // ------------------------------------------------------------------ curvas

        /// <summary>
        /// Suaviza uma linha de anéis com Catmull-Rom ('steps' por troço). Raios interpolados; a cor muda de forma seca
        /// (anel repetido) quando muda entre dois anéis de controlo.
        /// </summary>
        public static List<Ring> Smooth(IList<Ring> ctrl, int steps)
        {
            var o = new List<Ring>();
            int n = ctrl.Count;
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = ctrl[Mathf.Max(0, i - 1)].p;
                var p1 = ctrl[i].p;
                var p2 = ctrl[i + 1].p;
                var p3 = ctrl[Mathf.Min(n - 1, i + 2)].p;
                if (i == 0) p0 = p1 - (p2 - p1);
                if (i + 2 > n - 1) p3 = p2 + (p2 - p1);
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    var p = CatmullRom(p0, p1, p2, p3, t);
                    float k = Mathf.SmoothStep(0f, 1f, t);
                    var c = ctrl[i].c;
                    if (s == 0 && i > 0 && ctrl[i - 1].c != ctrl[i].c) o.Add(new Ring(p, ctrl[i].rx, ctrl[i].ry, ctrl[i - 1].c));
                    o.Add(new Ring(p, Mathf.Lerp(ctrl[i].rx, ctrl[i + 1].rx, k), Mathf.Lerp(ctrl[i].ry, ctrl[i + 1].ry, k), c));
                }
            }
            var last = ctrl[n - 1];
            if (n > 1 && ctrl[n - 2].c != last.c) o.Add(new Ring(last.p, last.rx, last.ry, ctrl[n - 2].c));
            o.Add(last);
            return o;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
