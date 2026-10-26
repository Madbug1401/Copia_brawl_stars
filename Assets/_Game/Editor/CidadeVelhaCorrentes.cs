using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Correntes do Pelourinho com mais detalhe: elos ovais de ferro entrelaçados (cada elo rodado 90° em relação ao
    /// anterior) a seguir a curva da corrente, presos a um olhal com anilha em cada poste.
    /// Substitui só a malha dos objetos PRP_Corrente_0/2/4/6 do prefab PF_CidadeVelha (os colisores, o material e as
    /// flags estáticas ficam iguais). Pode correr-se várias vezes; depois de reconstruir a Cidade Velha a partir do
    /// Blender, volta a correr-se este menu.
    /// </summary>
    public static class CidadeVelhaCorrentes
    {
        const string PrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_CidadeVelha.prefab";
        const string MeshFolder = ProjectSetup.Root + "/Art/Environment/Santiago/CidadeVelha/Correntes";

        // elo (metros): comprimento e largura por fora, raio do arame
        const float LinkLength = 0.13f;
        const float LinkWidth = 0.075f;
        const float Wire = 0.012f;
        const int LoopPoints = 12;      // pontos à volta do elo
        const int TubeSides = 5;        // lados do arame

        // poste (malha do Blender: prisma octogonal r=0,14 até 0,72 m e bola em cima)
        const float PostRadius = 0.14f;
        const float AttachY = 0.62f;    // altura do olhal
        const float Sag = 0.22f;        // quanto a corrente desce a meio

        [MenuItem("FPS-LAN/Mapas/Cidade Velha: correntes do Pelourinho (detalhe)", priority = 45)]
        static void Menu()
        {
            var r = Apply();
            Debug.Log("[Cidade Velha] " + r);
            EditorUtility.DisplayDialog("Cidade Velha", r, "OK");
        }

        public static string Apply()
        {
            GemMapBuilder.EnsureFolder(MeshFolder);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            string result;
            try
            {
                int done = 0, links = 0, tris = 0;
                for (int i = 0; i < 8; i += 2)
                {
                    var chain = FindDeep(root.transform, "PRP_Corrente_" + i);
                    var a = FindDeep(root.transform, "PRP_Poste_" + i);
                    var b = FindDeep(root.transform, "PRP_Poste_" + ((i + 1) % 8));
                    if (chain == null || a == null || b == null) continue;
                    var mf = chain.GetComponent<MeshFilter>();
                    if (mf == null) continue;

                    var mesh = BuildSpan("CV_Corrente_" + i, chain, a.position, b.position, i, out int n);
                    mf.sharedMesh = SaveMesh(mesh);
                    links += n;
                    tris += mf.sharedMesh.triangles.Length / 3;
                    done++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                result = done == 4
                    ? $"Correntes do Pelourinho: {done} vãos, {links} elos, {tris} triângulos."
                    : $"Só foram encontrados {done} de 4 vãos (PRP_Corrente_0/2/4/6 e PRP_Poste_0..7).";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        // ------------------------------------------------------------------ geometria

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
        }

        /// <summary>Um vão: olhal + anilha em cada poste e a corrente entre eles (coordenadas locais do objeto).</summary>
        static Mesh BuildSpan(string name, Transform space, Vector3 postA, Vector3 postB, int seed, out int linkCount)
        {
            var bld = new Builder();
            var flat = postB - postA;
            flat.y = 0f;
            var dir = flat.normalized;

            // anilha à volta de cada poste e olhal virado para o outro poste
            var eyeA = Eye(bld, space, postA, dir);
            var eyeB = Eye(bld, space, postB, -dir);

            // curva: parábola entre os olhais (desce Sag a meio)
            const int samples = 200;
            var pts = new Vector3[samples + 1];
            var acc = new float[samples + 1];
            for (int k = 0; k <= samples; k++)
            {
                float t = k / (float)samples;
                pts[k] = Vector3.Lerp(eyeA, eyeB, t) + Vector3.down * (Sag * 4f * t * (1f - t));
                if (k > 0) acc[k] = acc[k - 1] + Vector3.Distance(pts[k - 1], pts[k]);
            }
            float total = acc[samples];

            // passo entre elos = abertura interior do elo (os elos entrelaçam-se)
            float pitch = LinkLength - 4f * Wire;
            int count = Mathf.Max(2, Mathf.RoundToInt(total / pitch));
            float step = total / count;
            var rng = new System.Random(1512 + seed);
            for (int k = 0; k < count; k++)
            {
                float d = (k + 0.5f) * step;
                SampleCurve(pts, acc, d, out var c, out var tan);
                var up = Vector3.ProjectOnPlane(Vector3.up, tan).normalized;
                var side = Vector3.Cross(tan, up).normalized;
                // elos alternados: um na vertical, o seguinte deitado; pequena torção para não parecer perfeito
                float twist = (k % 2 == 0 ? 0f : 90f) + (float)(rng.NextDouble() * 2.0 - 1.0) * 9f;
                var q = Quaternion.AngleAxis(twist, tan);
                Link(bld, space, c, tan, q * up, step / pitch);
            }
            linkCount = count;

            var m = new Mesh { name = name };
            m.SetVertices(bld.v);
            m.SetNormals(bld.n);
            m.SetUVs(0, bld.uv);
            m.SetTriangles(bld.t, 0);
            m.RecalculateBounds();
            return m;
        }

        static void SampleCurve(Vector3[] pts, float[] acc, float d, out Vector3 pos, out Vector3 tan)
        {
            int i = 1;
            while (i < acc.Length - 1 && acc[i] < d) i++;
            float f = Mathf.InverseLerp(acc[i - 1], acc[i], d);
            pos = Vector3.Lerp(pts[i - 1], pts[i], f);
            tan = (pts[i] - pts[i - 1]).normalized;
        }

        /// <summary>Elo oval (dois semicírculos e dois troços retos) com o eixo maior em 'axis' e o plano com 'planeUp'.</summary>
        static void Link(Builder b, Transform space, Vector3 center, Vector3 axis, Vector3 planeUp, float stretch)
        {
            float c = LinkWidth * 0.5f - Wire;                                    // raio das curvas (eixo do arame)
            float s = Mathf.Max(0f, (LinkLength - LinkWidth) * 0.5f) * stretch;  // meio troço reto
            var loop = new Vector3[LoopPoints];
            int half = LoopPoints / 2;
            for (int i = 0; i < half; i++)
            {
                float a = Mathf.Lerp(-90f, 90f, i / (float)(half - 1)) * Mathf.Deg2Rad;
                loop[i] = center + axis * (s + c * Mathf.Cos(a)) + planeUp * (c * Mathf.Sin(a));
                loop[half + i] = center - axis * (s + c * Mathf.Cos(a)) - planeUp * (c * Mathf.Sin(a));
            }
            var normal = Vector3.Cross(axis, planeUp).normalized;
            Tube(b, space, loop, normal, Wire, true);
        }

        /// <summary>Tubo fechado ao longo de uma curva fechada no plano de normal 'planeNormal'.</summary>
        static void Tube(Builder b, Transform space, Vector3[] loop, Vector3 planeNormal, float r, bool closed)
        {
            int n = loop.Length;
            int start = b.v.Count;
            for (int i = 0; i < n; i++)
            {
                var prev = loop[(i - 1 + n) % n];
                var next = loop[(i + 1) % n];
                var tan = (next - prev).normalized;
                var outward = Vector3.Cross(tan, planeNormal).normalized;
                for (int j = 0; j < TubeSides; j++)
                {
                    float a = j * Mathf.PI * 2f / TubeSides;
                    var nn = outward * Mathf.Cos(a) + planeNormal * Mathf.Sin(a);
                    b.v.Add(space.InverseTransformPoint(loop[i] + nn * r));
                    b.n.Add(space.InverseTransformDirection(nn));
                    b.uv.Add(new Vector2(i / (float)n, j / (float)TubeSides));
                }
            }
            // sentido das faces: o primeiro quadrado tem de olhar para fora (para o lado da normal do vértice)
            var p0 = b.v[start]; var p1 = b.v[start + TubeSides]; var p2 = b.v[start + TubeSides + 1];
            bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), b.n[start] + b.n[start + TubeSides + 1]) < 0f;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int i0 = start + i * TubeSides, i1 = start + ((i + 1) % n) * TubeSides;
                for (int j = 0; j < TubeSides; j++)
                {
                    int j1 = (j + 1) % TubeSides;
                    if (!flip)
                    {
                        b.t.Add(i0 + j); b.t.Add(i1 + j); b.t.Add(i1 + j1);
                        b.t.Add(i0 + j); b.t.Add(i1 + j1); b.t.Add(i0 + j1);
                    }
                    else
                    {
                        b.t.Add(i0 + j); b.t.Add(i1 + j1); b.t.Add(i1 + j);
                        b.t.Add(i0 + j); b.t.Add(i0 + j1); b.t.Add(i1 + j1);
                    }
                }
            }
        }

        /// <summary>Anilha de ferro à volta do poste e olhal virado para 'toward'. Devolve o ponto onde a corrente prende.</summary>
        static Vector3 Eye(Builder b, Transform space, Vector3 post, Vector3 toward)
        {
            var baseY = new Vector3(post.x, post.y + AttachY, post.z);
            // anilha (círculo horizontal encostado ao poste)
            Ring(b, space, baseY, Vector3.up, PostRadius + 0.006f, 0.014f, 16);
            Ring(b, space, baseY + Vector3.up * 0.035f, Vector3.up, PostRadius + 0.002f, 0.007f, 16);
            // olhal: argola vertical no plano da corrente, presa à anilha
            float eyeR = 0.026f, eyeWire = 0.008f;
            var eyeCenter = baseY + toward * (PostRadius + 0.014f + eyeR);
            var side = Vector3.Cross(Vector3.up, toward).normalized;
            Ring(b, space, eyeCenter, side, eyeR, eyeWire, 10);
            // a corrente começa no lado de fora do olhal
            return eyeCenter + toward * (eyeR - Wire);
        }

        static void Ring(Builder b, Transform space, Vector3 center, Vector3 normal, float radius, float wire, int points)
        {
            var u = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            var w = Vector3.Cross(normal, u).normalized;
            var loop = new Vector3[points];
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                loop[i] = center + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius;
            }
            Tube(b, space, loop, normal, wire, true);
        }

        // ------------------------------------------------------------------ utilitários

        static Mesh SaveMesh(Mesh m)
        {
            string path = MeshFolder + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                existing.vertices = m.vertices;
                existing.normals = m.normals;
                existing.uv = m.uv;
                existing.SetTriangles(m.GetTriangles(0), 0);
                existing.RecalculateBounds();
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
