using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Junta as peças estáticas de uma rig (primitivas coloridas por property block) numa malha por osso e por
    /// material, com a cor de cada peça nos vértices. Uma personagem passa de ~50 renderers para ~15,
    /// o que poupa muitas draw calls (sombras e contornos incluídos) em máquinas modestas.
    /// </summary>
    public static class MeshMerge
    {
        static readonly List<MeshRenderer> s_Renderers = new List<MeshRenderer>();
        static readonly List<Vector3> s_Verts = new List<Vector3>();
        static readonly List<Vector3> s_Normals = new List<Vector3>();
        static readonly List<Color> s_Colors = new List<Color>();
        static readonly List<Vector2> s_Uvs = new List<Vector2>();
        static readonly List<int> s_Tris = new List<int>();
        static readonly Dictionary<Mesh, MeshData> s_Cache = new Dictionary<Mesh, MeshData>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Cache.Clear();

        class MeshData
        {
            public Vector3[] verts, normals;
            public Vector2[] uvs;
            public int[] tris;
        }

        static MeshData Data(Mesh m)
        {
            if (s_Cache.TryGetValue(m, out var d)) return d;
            d = new MeshData { verts = m.vertices, normals = m.normals, uvs = m.uv, tris = m.triangles };
            s_Cache[m] = d;
            return d;
        }

        /// <summary>
        /// Junta em 'node' todas as peças da sua subárvore que não passem por um nó de 'keep' (ossos/partes animadas).
        /// As peças juntas são destruídas; os nós vazios ficam (podem ser pontos de referência, ex.: canos).
        /// </summary>
        public static void MergeInto(Transform node, HashSet<Transform> keep, Material plain, Material outlined)
        {
            if (node == null) return;
            s_Renderers.Clear();
            Collect(node, node, keep, s_Renderers);
            if (s_Renderers.Count < 2) return;

            var groups = new Dictionary<Material, List<MeshRenderer>>();
            foreach (var r in s_Renderers)
            {
                var mat = r.sharedMaterial;
                if (mat == null) continue;
                if (!groups.TryGetValue(mat, out var list)) groups[mat] = list = new List<MeshRenderer>();
                list.Add(r);
            }

            var block = Art.Block;
            var toLocal = node.worldToLocalMatrix;
            foreach (var kv in groups)
            {
                if (kv.Value.Count < 2) continue;
                bool outline = kv.Key == Art.ToonOutline;
                bool isToon = outline || kv.Key == Art.Toon;
                if (!isToon) continue; // só peças cartoon (as de efeito ficam como estão)

                s_Verts.Clear(); s_Normals.Clear(); s_Colors.Clear(); s_Uvs.Clear(); s_Tris.Clear();
                var shadows = ShadowCastingMode.Off;
                foreach (var r in kv.Value)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var d = Data(mf.sharedMesh);
                    r.GetPropertyBlock(block);
                    var c = block.GetColor(Art.ColorId);
                    var e = block.GetColor(Art.EmissionId);
                    // A emissão (ex.: lentes) junta-se à cor; perde-se o brilho real mas mantém o tom claro
                    c = new Color(Mathf.Min(1f, c.r + e.r), Mathf.Min(1f, c.g + e.g), Mathf.Min(1f, c.b + e.b), 1f);

                    var m = toLocal * r.transform.localToWorldMatrix;
                    var n = m.inverse.transpose;
                    int baseIndex = s_Verts.Count;
                    for (int i = 0; i < d.verts.Length; i++)
                    {
                        s_Verts.Add(m.MultiplyPoint3x4(d.verts[i]));
                        s_Normals.Add(n.MultiplyVector(d.normals[i]).normalized);
                        s_Colors.Add(c);
                        s_Uvs.Add(d.uvs.Length > i ? d.uvs[i] : Vector2.zero);
                    }
                    for (int i = 0; i < d.tris.Length; i++) s_Tris.Add(baseIndex + d.tris[i]);
                    if (r.shadowCastingMode != ShadowCastingMode.Off) shadows = ShadowCastingMode.On;
                }
                if (s_Verts.Count == 0) continue;

                var mesh = new Mesh { name = "Merged_" + node.name };
                if (s_Verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(s_Verts);
                mesh.SetNormals(s_Normals);
                mesh.SetColors(s_Colors);
                mesh.SetUVs(0, s_Uvs);
                mesh.SetTriangles(s_Tris, 0);
                mesh.RecalculateBounds();

                var go = new GameObject("Merged");
                go.layer = node.gameObject.layer;
                go.transform.SetParent(node, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = outline ? outlined : plain;
                mr.shadowCastingMode = shadows;
                mr.receiveShadows = true;
                go.AddComponent<MeshOwner>().mesh = mesh;

                foreach (var r in kv.Value)
                {
                    r.gameObject.SetActive(false);
                    Object.Destroy(r.gameObject);
                }
            }
        }

        static void Collect(Transform root, Transform t, HashSet<Transform> keep, List<MeshRenderer> result)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (keep != null && keep.Contains(c)) continue;
                if (c.childCount == 0)
                {
                    var r = c.GetComponent<MeshRenderer>();
                    if (r != null && c.gameObject.activeSelf) result.Add(r);
                }
                else Collect(root, c, keep, result);
            }
        }
    }

    /// <summary>Destrói a malha gerada quando o objeto é destruído (evita fugas de memória ao trocar de Brawler).</summary>
    public class MeshOwner : MonoBehaviour
    {
        public Mesh mesh;

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }
    }
}
