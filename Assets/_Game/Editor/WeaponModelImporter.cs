using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Converte os modelos 3D das armas (gerados a partir de imagens, 1.9 m e orientação arbitrária) em prefabs prontos
    /// para o jogo, em Resources/Weapons/&lt;estilo&gt;: malha já escalada para metros, origem no punho (onde a mão
    /// fecha), +Z = cano, +Y = cima, e marcadores (Muzzle, Support, FuseTip, Label, BowTop/BowBottom/Nock).
    /// Malhas muito densas são reduzidas (LOD gerado pelo Unity) para não pesar na 1.ª pessoa.
    /// Os pontos são dados em coordenadas normalizadas da caixa do modelo (0..1 em x, y, z), já rodado.
    /// Menu: FPS-LAN → Armas: importar modelos 3D de todas as armas.
    /// </summary>
    public static class WeaponModelImporter
    {
        const string ArtRoot = "Assets/_Game/Art/Weapons/";
        const string ResFolder = "Assets/_Game/Resources/Weapons";

        public class Config
        {
            public string name, model, prefab;
            public Vector3 euler;          // rotação extra (depois da do próprio ficheiro)
            public int axis;                // eixo medido para a escala (0 x, 1 y, 2 z)
            public float size;              // tamanho final, em metros, nesse eixo
            public Vector3 grip;            // origem (normalizado)
            public int maxTris = 24000;
            public Bounds? cut;             // triângulos (normalizados) a remover, ex.: cordas do arco
            public float spinFromZ = -1f;   // > 0: triângulos à frente deste z (normalizado) vão para o filho "Barrels",
            public Vector2 spinAxis;        //       que roda à volta do eixo (x, y normalizados) — canos da Minigun
            public readonly List<KeyValuePair<string, Vector3>> markers = new List<KeyValuePair<string, Vector3>>();
            public Config Mark(string n, float x, float y, float z) { markers.Add(new KeyValuePair<string, Vector3>(n, new Vector3(x, y, z))); return this; }
        }

        public static List<Config> All()
        {
            var l = new List<Config>();
            // Ekko — metralhadora compacta (duas, uma em cada mão)
            l.Add(new Config { name = "Ekko", model = "Ekko/Ekko.fbx", prefab = RigStyle.DualSMG.ToString(), axis = 2, size = 0.40f, grip = new Vector3(0.5f, 0.30f, 0.15f) }
                .Mark("Muzzle", 0.5f, 0.70f, 1.0f));
            // Riot — caçadeira de bomba (mão esquerda no pump laranja)
            l.Add(new Config { name = "Riot", model = "Riot/Riot.obj", prefab = RigStyle.Shotgun.ToString(), axis = 2, size = 0.82f, grip = new Vector3(0.5f, 0.50f, 0.265f) }
                .Mark("Muzzle", 0.5f, 0.85f, 1.0f).Mark("Support", 0.5f, 0.60f, 0.68f));
            // Mr.S — lata de spray (uma em cada mão); o rótulo SITA vai à volta do corpo
            l.Add(new Config { name = "MrS", model = "MrS/MrS.fbx", prefab = RigStyle.DualSprayCans.ToString(), axis = 1, size = 0.19f, grip = new Vector3(0.5f, 0.40f, 0.5f) }
                .Mark("Muzzle", 0.5f, 1.0f, 0.62f).Mark("Label", 0.5f, 0.45f, 0.5f));
            // Lupus Marinus — molho de dinamite (o rastilho faz de boca)
            l.Add(new Config { name = "LupusTNT", model = "LupusTNT/LupusTNT.obj", prefab = RigStyle.DualExplosives.ToString(), axis = 1, size = 0.20f, grip = new Vector3(0.5f, 0.40f, 0.5f) }
                .Mark("FuseTip", 0.5f, 1.0f, 0.5f));
            // N.Cage — arco composto; as cordas da malha saem (a corda animada é desenhada pelo jogo)
            l.Add(new Config { name = "NCageBow", model = "NCageBow/NCageBow.fbx", prefab = RigStyle.Bow.ToString(), axis = 1, size = 0.85f, grip = new Vector3(0.5f, 0.42f, 0.55f),
                    cut = new Bounds(new Vector3(0.5f, 0.5f, 0.17f), new Vector3(2f, 0.74f, 0.34f)) }
                .Mark("BowTop", 0.5f, 0.92f, 0.13f).Mark("BowBottom", 0.5f, 0.08f, 0.13f).Mark("Nock", 0.5f, 0.5f, 0.12f));
            // N.Cage — flecha (ponta para +Z, origem na cauda)
            // (pequena no ecrã e há uma por disparo: poucos triângulos chegam)
            // Ekko — Minigun do Super: pega traseira vertical (mão direita), mão esquerda por baixo dos canos (que rodam)
            l.Add(new Config { name = "EkkoMinigun", model = "EkkoMinigun/EkkoMinigun.fbx", prefab = "Minigun", axis = 2, size = 0.9f, grip = new Vector3(0.575f, 0.62f, 0.07f),
                    spinFromZ = 0.6f, spinAxis = new Vector2(0.575f, 0.49f) }
                .Mark("Muzzle", 0.575f, 0.49f, 1.0f).Mark("Support", 0.575f, 0.24f, 0.57f));
            l.Add(new Config { name = "NCageArrow", model = "NCageArrow/NCageArrow.fbx", prefab = "Arrow", euler = new Vector3(90f, 0f, 0f), axis = 2, size = 0.62f, grip = new Vector3(0.5f, 0.5f, 0f), maxTris = 6000 }
                .Mark("Muzzle", 0.5f, 0.5f, 1f));
            return l;
        }

        [MenuItem("FPS-LAN/Armas: importar modelos 3D de todas as armas", priority = 34)]
        static void BuildAllMenu()
        {
            var report = BuildAll();
            Debug.Log("[Armas] " + report);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("FPS-LAN", report, "OK");
        }

        public static string BuildAll()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in All()) sb.AppendLine(Build(c));
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public static string BuildOne(string name)
        {
            foreach (var c in All()) if (c.name == name) { var r = Build(c); AssetDatabase.SaveAssets(); return r; }
            return "sem config: " + name;
        }

        public static string Build(Config c)
        {
            string modelPath = ArtRoot + c.model;
            string dir = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/');
            if (AssetImporter.GetAtPath(modelPath) is ModelImporter mi)
            {
                bool dirty = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || !mi.isReadable;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.isReadable = true;
                if (dirty) mi.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) return c.name + ": modelo não encontrado (" + modelPath + ")";

            // 1) malha no espaço do modelo tal como aparece na cena (mantém a rotação/escala do próprio ficheiro),
            //    mais a rotação extra
            var inst = (GameObject)Object.Instantiate(model);
            inst.transform.position = Vector3.zero;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var extra = Quaternion.Euler(c.euler);
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; var mtx = mf.transform.localToWorldMatrix;
                int baseV = verts.Count;
                foreach (var v in m.vertices) verts.Add(extra * mtx.MultiplyPoint3x4(v));
                var n = m.normals; foreach (var v in n) normals.Add((extra * mtx.MultiplyVector(v)).normalized);
                var uv = m.uv; if (uv.Length == m.vertexCount) uvs.AddRange(uv); else for (int i = 0; i < m.vertexCount; i++) uvs.Add(Vector2.zero);
                foreach (var t in m.triangles) tris.Add(baseV + t);
            }
            Object.DestroyImmediate(inst);

            var b = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b.Encapsulate(v);
            float s = c.size / Mathf.Max(1e-5f, b.size[c.axis]);
            Vector3 N(Vector3 n) => b.min + Vector3.Scale(n, b.size);
            var origin = N(c.grip);
            Vector3 P(Vector3 n) => (N(n) - origin) * s;

            // 2) cortar triângulos (ex.: cordas do arco)
            if (c.cut.HasValue)
            {
                var cut = c.cut.Value; var keep = new List<int>(tris.Count);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    bool inside = true;
                    for (int k = 0; k < 3 && inside; k++)
                    {
                        var v = verts[tris[i + k]]; var n = new Vector3((v.x - b.min.x) / b.size.x, (v.y - b.min.y) / b.size.y, (v.z - b.min.z) / b.size.z);
                        inside = cut.Contains(n);
                    }
                    if (!inside) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
                }
                tris = keep;
            }

            // 2b) peça que roda (canos da Minigun): triângulos à frente de spinFromZ, com pivô no eixo
            List<int> spinTris = null;
            Vector3 spinPivot = Vector3.zero;
            if (c.spinFromZ > 0f)
            {
                var keep = new List<int>(tris.Count); spinTris = new List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    float z = 0f;
                    for (int k = 0; k < 3; k++) z += (verts[tris[i + k]].z - b.min.z) / b.size.z;
                    var dst = z / 3f > c.spinFromZ ? spinTris : keep;
                    dst.Add(tris[i]); dst.Add(tris[i + 1]); dst.Add(tris[i + 2]);
                }
                tris = keep;
                spinPivot = P(new Vector3(c.spinAxis.x, c.spinAxis.y, c.spinFromZ));
            }

            // 3) escala/origem
            for (int i = 0; i < verts.Count; i++) verts[i] = (verts[i] - origin) * s;

            int before = tris.Count / 3 + (spinTris != null ? spinTris.Count / 3 : 0);
            var mesh = SaveMesh(MakeMesh(c.name + "_FP", verts, normals, uvs, tris, Vector3.zero, c.maxTris), dir + "/" + c.name + "_FP.asset");
            Mesh spinMesh = spinTris != null && spinTris.Count > 0
                ? SaveMesh(MakeMesh(c.name + "_FP_Spin", verts, normals, uvs, spinTris, spinPivot, c.maxTris), dir + "/" + c.name + "_FP_Spin.asset")
                : null;

            // 4) material cartoon com a textura do modelo
            var matPath = dir + "/" + c.name + "_Toon.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("FPSLAN/ToonOutline") ?? Shader.Find("Standard");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + c.name + "_diffuse.png");
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_VertexColor")) mat.SetFloat("_VertexColor", 0f);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.0012f);
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", new Color(0.06f, 0.05f, 0.1f));
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.25f));
            EditorUtility.SetDirty(mat);

            // 5) prefab: raiz (origem = punho) + malha + marcadores
            var root = new GameObject("Weapon_" + c.prefab + "_Model");
            var body = new GameObject("Mesh");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (spinMesh != null)
            {
                var spin = new GameObject("Barrels");
                spin.transform.SetParent(root.transform, false);
                spin.transform.localPosition = spinPivot;
                spin.AddComponent<MeshFilter>().sharedMesh = spinMesh;
                spin.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            var info = new System.Text.StringBuilder();
            foreach (var kv in c.markers)
            {
                var t = new GameObject(kv.Key).transform;
                t.SetParent(root.transform, false);
                t.localPosition = P(kv.Value);
                if (kv.Key == "Label") t.localScale = Vector3.one * LabelScale(verts, t.localPosition.y);
                info.Append(" " + kv.Key + t.localPosition.ToString("F3"));
            }
            if (!AssetDatabase.IsValidFolder(ResFolder)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Weapons");
            var prefabPath = ResFolder + "/" + c.prefab + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return c.name + " → " + prefabPath + " tris " + before + "→" + mesh.triangles.Length / 3 + " caixa " + mesh.bounds.min.ToString("F3") + ".." + mesh.bounds.max.ToString("F3") + info;
        }

        /// <summary>Malha só com estes triângulos (vértices deslocados de 'offset'), reduzida se for densa e compactada.</summary>
        static Mesh MakeMesh(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris, Vector3 offset, int maxTris)
        {
            var v = offset == Vector3.zero ? verts : verts.ConvertAll(p => p - offset);
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            if (tris.Count / 3 > maxTris) mesh = Reduce(mesh, maxTris);
            mesh = Compact(mesh);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Grava a malha como asset (substitui a anterior no mesmo caminho, mantendo as referências).</summary>
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Escala do rótulo SITA (raio 0.058) para ficar à superfície do corpo da lata.</summary>
        static float LabelScale(List<Vector3> verts, float y)
        {
            float r = 0f;
            foreach (var v in verts) if (Mathf.Abs(v.y - y) < 0.006f) r = Mathf.Max(r, new Vector2(v.x, v.z).magnitude);
            return r > 0f ? r * 0.95f / 0.058f : 1f;
        }

        /// <summary>Reduz a malha com o gerador de LODs do Unity e fica com o primeiro nível abaixo do limite.</summary>
        static Mesh Reduce(Mesh mesh, int maxTris)
        {
            MeshLodUtility.GenerateMeshLods(mesh, 12);
            int lods = mesh.lodCount, pick = lods - 1;
            for (int i = 0; i < lods; i++)
                if (mesh.GetIndexCount(0, i) / 3 <= maxTris) { pick = i; break; }
            var tri = mesh.GetTriangles(0, pick, true);
            var m = new Mesh { name = mesh.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = mesh.vertices; m.normals = mesh.normals; m.uv = mesh.uv;
            m.SetTriangles(tri, 0);
            return m;
        }

        /// <summary>Remove vértices não usados (e passa a índices de 16 bits quando possível).</summary>
        static Mesh Compact(Mesh mesh)
        {
            var tri = mesh.triangles; var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv;
            var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>();
            for (int i = 0; i < tri.Length; i++)
            {
                if (!map.TryGetValue(tri[i], out int j))
                {
                    j = nv.Count; map[tri[i]] = j;
                    nv.Add(v[tri[i]]); nn.Add(n.Length > 0 ? n[tri[i]] : Vector3.up); nuv.Add(uv.Length > 0 ? uv[tri[i]] : Vector2.zero);
                }
                tri[i] = j;
            }
            var m = new Mesh { name = mesh.name };
            m.indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nuv); m.SetTriangles(tri, 0);
            return m;
        }
    }
}
