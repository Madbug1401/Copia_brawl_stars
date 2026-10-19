using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Paisagem à volta da Cidade Velha (Pelourinho), fora da zona jogável:
    /// do lado −X (lado "D") o chão de areia acaba num muro de pedra e numa praia de seixos escuros que desce até ao
    /// mar (como a baía da Cidade Velha); os barcos passam para esse mar. Do lado oposto (+X, lado "C") ficam as
    /// montanhas: os montes que estavam do lado do mar são espelhados para lá e junta-se uma cordilheira maior.
    /// Só mexe no cenário de fundo do prefab PF_CidadeVelha (nada jogável, sem colisores). Pode correr-se várias
    /// vezes; depois de reconstruir a Cidade Velha a partir do Blender, volta a correr-se este menu.
    /// </summary>
    public static class CidadeVelhaCosta
    {
        const string PrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_CidadeVelha.prefab";
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Santiago/CidadeVelha";
        const string CostaFolder = ArtRoot + "/Costa";

        // praia (coordenadas do mundo; o prefab está na origem)
        const float BeachTopX = -44f;      // fim da areia / muro
        const float BeachEndX = -72f;      // seixos até aqui (já debaixo de água)
        const float PromenadeX = -28f;     // a partir daqui (para −X) o chão é calçada, como a marginal da foto
        const float BeachZ = 84f;
        const float TopY = -0.22f;         // topo do chão de areia
        const float WaterY = -1.1f;        // superfície do mar (BG_Mar)
        const float BottomY = -1.9f;

        [MenuItem("FPS-LAN/Mapas/Cidade Velha: mar de pedras e montanhas", priority = 42)]
        static void Menu()
        {
            var r = Apply();
            Debug.Log("[Cidade Velha] " + r);
            EditorUtility.DisplayDialog("Cidade Velha", r, "OK");
        }

        public static string Apply()
        {
            var log = new StringBuilder();
            GemMapBuilder.EnsureFolder(CostaFolder);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var cen = FindDeep(root.transform, "Cenario") ?? root.transform;
                var old = cen.Find("Costa_Seixos");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var costa = new GameObject("Costa_Seixos").transform;
                costa.SetParent(cen, false);

                var rng = new System.Random(1462);
                var matSeixos = SeixosMaterial();
                var matMuro = AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + "/Materials/M_CV_Pedra_Muro.mat");

                // 1) areia termina no muro: a caixa GRD_Terra passa a ir só de BeachTopX a +80
                var terra = FindDeep(cen, "GRD_Terra");
                if (terra != null)
                {
                    var mf = terra.GetComponent<MeshFilter>();
                    var b = mf.sharedMesh.bounds;
                    var box = SaveMesh(BoxMesh("CV_Terra_Costa", new Vector3(PromenadeX, b.min.y, b.min.z), new Vector3(b.max.x, b.max.y, b.max.z), 0.25f));
                    mf.sharedMesh = box;
                    // do lado do mar já não há areia: calçada de basalto até ao muro da praia
                    var calc = AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + "/Materials/M_CV_Calcada_Basalto.mat");
                    var prom = SaveMesh(BoxMesh("CV_Calcada_Marginal", new Vector3(BeachTopX, b.min.y, b.min.z), new Vector3(PromenadeX, b.max.y, b.max.z), 0.5f));
                    Add(costa, "Calcada_Marginal", prom, calc != null ? calc : terra.GetComponent<MeshRenderer>().sharedMaterial, ShadowCastingMode.Off);
                    log.AppendLine($"Areia só de x={PromenadeX} a x={b.max.x:0}; do lado do mar, calçada de x={BeachTopX} a x={PromenadeX}.");
                }

                // 2) encosta da praia (base) + seixos
                Add(costa, "Praia_Base", SaveMesh(BeachBase(rng)), matSeixos, ShadowCastingMode.Off);
                int pebbles = 0;
                const int chunks = 6;
                for (int c = 0; c < chunks; c++)
                {
                    float z0 = -BeachZ + c * (2f * BeachZ / chunks), z1 = z0 + 2f * BeachZ / chunks;
                    var mesh = Pebbles("Seixos_" + c, rng, z0, z1, out int n);
                    pebbles += n;
                    Add(costa, "Seixos_" + c, SaveMesh(mesh), matSeixos, ShadowCastingMode.Off);
                }
                log.AppendLine($"Praia de seixos: {pebbles} pedras em {chunks} malhas, de x={BeachTopX} até ao mar.");

                // 3) muro de pedra ao longo da praia (como o da marginal da Cidade Velha)
                if (matMuro != null)
                {
                    Add(costa, "Muro_Marginal", SaveMesh(SeaWall(rng)), matMuro, ShadowCastingMode.On);
                    log.AppendLine("Muro de pedra ao longo da praia.");
                }

                // 4) barcos para o mar de seixos
                int boats = 0;
                for (int i = 0; i < 8; i++)
                {
                    var bt = FindDeep(cen, "BG_Bote_" + i);
                    if (bt == null) continue;
                    float z = -38f + i * 26f + (float)rng.NextDouble() * 6f;
                    float x = -66f - (float)rng.NextDouble() * 12f;
                    var rr = bt.GetComponent<Renderer>();
                    var offset = rr != null ? rr.bounds.center - bt.position : Vector3.zero;
                    bt.position = new Vector3(x, bt.position.y, z) - new Vector3(offset.x, 0f, offset.z);
                    bt.rotation = Quaternion.AngleAxis(70f + (float)rng.NextDouble() * 50f, Vector3.up) * bt.rotation;
                    boats++;
                }
                log.AppendLine($"Barcos no mar do lado da praia: {boats}.");

                // 5) montanhas do lado oposto: espelha os montes que estavam do lado do mar e junta uma cordilheira
                int moved = 0;
                var montes = new List<Transform>();
                foreach (Transform t in cen)
                    if (t.name.StartsWith("BG_Monte_") && !t.name.StartsWith("BG_Monte_Costa")) montes.Add(t);
                foreach (var t in montes)
                {
                    var rr = t.GetComponent<Renderer>();
                    var c = rr != null ? rr.bounds.center : t.position;
                    if (c.x >= -35f) continue;
                    // espelho em x: o centro do monte passa de c.x para −c.x
                    t.position += new Vector3(-2f * c.x, 0f, 0f);
                    moved++;
                }
                foreach (Transform t in new List<Transform>(EnumerateChildren(cen)))
                    if (t.name.StartsWith("BG_Monte_Costa")) Object.DestroyImmediate(t.gameObject);
                var src = montes.Count > 0 ? montes : null;
                var range = new (float x, float z, float s)[] { (96, -62, 1.7f), (104, -18, 2.1f), (100, 26, 1.9f), (92, 66, 1.6f), (118, 5, 2.4f), (122, 48, 2.0f), (115, -45, 2.2f) };
                int added = 0;
                if (src != null)
                    for (int k = 0; k < range.Length; k++)
                    {
                        var proto = src[k % src.Count];
                        var copy = Object.Instantiate(proto.gameObject, cen);
                        copy.name = "BG_Monte_Costa_" + k;
                        var rr = proto.GetComponent<Renderer>();
                        var off = rr != null ? rr.bounds.center - proto.position : Vector3.zero;
                        copy.transform.localScale = proto.localScale * range[k].s;
                        copy.transform.position = new Vector3(range[k].x, proto.position.y, range[k].z) - new Vector3(off.x, 0f, off.z) * range[k].s;
                        copy.transform.rotation = Quaternion.Euler(0f, k * 47f, 0f) * proto.rotation;
                        added++;
                    }
                log.AppendLine($"Montanhas: {moved} montes passaram do lado do mar para o lado oposto, mais {added} na cordilheira.");

                foreach (var t in costa.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ------------------------------------------------------------------ malhas

        static Mesh BeachBase(System.Random rng)
        {
            int nx = 14, nz = 90;
            var v = new List<Vector3>();
            var col = new List<Color>();
            var tri = new List<int>();
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = i / (float)nx;
                    float x = Mathf.Lerp(BeachTopX + 0.4f, BeachEndX - 4f, u);
                    float z = Mathf.Lerp(-BeachZ, BeachZ, j / (float)nz);
                    float y = BeachY(x, z) - 0.12f;
                    v.Add(new Vector3(x, y, z));
                    float wet = Mathf.InverseLerp(WaterY + 0.35f, WaterY - 0.2f, y);
                    col.Add(Color.Lerp(new Color(0.3f, 0.28f, 0.27f), new Color(0.16f, 0.15f, 0.15f), wet));
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tri.AddRange(new[] { a, b, c, b, d, c });   // x cresce para −X: ordem invertida para a face ficar virada para cima
                }
            var m = new Mesh { name = "CV_Praia_Base" };
            m.SetVertices(v);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Altura da praia: desce do muro até debaixo de água, com uma ligeira ondulação ao longo da costa.</summary>
        static float BeachY(float x, float z)
        {
            float u = Mathf.InverseLerp(BeachTopX, BeachEndX, x);
            float y = Mathf.Lerp(TopY, BottomY, Mathf.Pow(u, 0.85f));
            return y + 0.12f * Mathf.Sin(z * 0.11f) * (1f - u);
        }

        static Mesh Pebbles(string name, System.Random rng, float z0, float z1, out int count)
        {
            var v = new List<Vector3>();
            var col = new List<Color>();
            var tri = new List<int>();
            var unit = Rock();
            count = 0;
            float area = (z1 - z0) * (BeachTopX - BeachEndX);
            int target = Mathf.RoundToInt(area * 1.8f);
            for (int k = 0; k < target; k++)
            {
                float u = (float)rng.NextDouble();
                u = u * u * 0.35f + u * 0.65f;                      // mais densidade perto da água
                float x = Mathf.Lerp(BeachTopX - 0.6f, BeachEndX, u);
                float z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                float s = 0.22f + (float)(rng.NextDouble() * rng.NextDouble()) * 0.6f;
                var scale = new Vector3(s * (0.9f + 0.5f * (float)rng.NextDouble()), s * (0.45f + 0.3f * (float)rng.NextDouble()), s * (0.9f + 0.5f * (float)rng.NextDouble()));
                float y = BeachY(x, z) + scale.y * 0.25f;
                if (y < WaterY - 0.55f) continue;                     // invisível debaixo de água
                var rot = Quaternion.Euler((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 20f - 10f);
                float g = 0.07f + 0.14f * (float)rng.NextDouble();
                var c = new Color(g * (1f + 0.15f * (float)rng.NextDouble()), g * 0.97f, g * 0.95f);
                if (rng.NextDouble() < 0.1) c = new Color(0.27f, 0.25f, 0.23f);   // alguns mais claros (basalto gasto)
                if (y < WaterY + 0.15f) c *= 0.75f;                                  // molhados
                c.a = 1f;
                int b0 = v.Count;
                foreach (var p in unit.v) v.Add(new Vector3(x, y, z) + rot * Vector3.Scale(p, scale));
                for (int i = 0; i < unit.v.Length; i++) col.Add(c);
                foreach (var t in unit.t) tri.Add(b0 + t);
                count++;
            }
            var m = new Mesh { name = "CV_" + name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v);
            m.SetColors(col);
            m.SetTriangles(tri, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Seixo base: octaedro subdividido uma vez e deformado (32 triângulos, aspeto rolado).</summary>
        static (Vector3[] v, int[] t) Rock()
        {
            var verts = new List<Vector3> { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            var faces = new List<int> { 0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0, 4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5 };
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                mid[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var nf = new List<int>();
            for (int f = 0; f < faces.Count; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
            }
            for (int i = 0; i < verts.Count; i++) verts[i] *= 0.5f * (0.88f + 0.24f * Mathf.PerlinNoise(verts[i].x * 2f + 3f, verts[i].z * 2f + verts[i].y));
            return (verts.ToArray(), nf.ToArray());
        }

        static Mesh SeaWall(System.Random rng)
        {
            var m = new Mesh { name = "CV_Muro_Marginal" };
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (float z = -BeachZ + 1f; z < BeachZ - 1f; z += 2.4f)
            {
                float len = 2.3f, h = 0.65f + (float)rng.NextDouble() * 0.12f, w = 0.7f;
                float x = BeachTopX - 0.1f + (float)rng.NextDouble() * 0.08f;
                AddBox(v, tri, new Vector3(x - w * 0.5f, TopY - 0.6f, z), new Vector3(x + w * 0.5f, TopY + h, z + len));
            }
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            // UV simples (projeção lateral) para a textura do muro
            var uv = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++) uv[i] = new Vector2(v[i].z * 0.5f + v[i].x * 0.5f, v[i].y * 0.5f);
            m.uv = uv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh BoxMesh(string name, Vector3 min, Vector3 max, float uvScale)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            AddBox(v, tri, min, max);
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            var uv = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++) uv[i] = new Vector2(v[i].x * uvScale, v[i].z * uvScale);
            m.uv = uv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void AddBox(List<Vector3> v, List<int> tri, Vector3 a, Vector3 b)
        {
            // 6 faces com vértices próprios (normais planas)
            Vector3[] c =
            {
                new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(a.x, b.y, a.z),
                new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z),
            };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 5, 6, 7, 4 }, new[] { 4, 7, 3, 0 }, new[] { 1, 2, 6, 5 }, new[] { 3, 7, 6, 2 }, new[] { 4, 0, 1, 5 } };
            foreach (var q in f)
            {
                int s = v.Count;
                foreach (var i in q) v.Add(c[i]);
                tri.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = CostaFolder + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                // copia pela API de Mesh (o CopySerialized não atualiza a malha já carregada na GPU)
                existing.Clear();
                existing.indexFormat = m.indexFormat;
                existing.vertices = m.vertices;
                if (m.colors.Length > 0) existing.colors = m.colors;
                if (m.uv.Length > 0) existing.uv = m.uv;
                existing.subMeshCount = m.subMeshCount;
                for (int i = 0; i < m.subMeshCount; i++) existing.SetTriangles(m.GetTriangles(i), i);
                existing.normals = m.normals;
                existing.RecalculateBounds();
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material SeixosMaterial()
        {
            string path = CostaFolder + "/M_CV_Seixos.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("FPSLAN/Toon"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = Color.white;
            if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", 1f);
            if (m.HasProperty("_TopLight")) m.SetFloat("_TopLight", 0.15f);
            if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(0.55f, 0.55f, 0.65f));
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.06f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Add(Transform parent, string name, Mesh mesh, Material mat, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows;
        }

        static IEnumerable<Transform> EnumerateChildren(Transform t)
        {
            foreach (Transform c in t) yield return c;
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
