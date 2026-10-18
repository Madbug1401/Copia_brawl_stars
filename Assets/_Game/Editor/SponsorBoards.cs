using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Placas dos patrocinadores do evento (SITA e UPRANIMAL) na Cidade Velha, nos sítios assinalados:
    /// frente das bancas de mercado, parede da casa térrea entre a janela e a porta (por baixo do candeeiro)
    /// e muro de pedra do spawn virado para a praça. Repetidas nos quatro quadrantes (mapa simétrico: as duas
    /// equipas veem o mesmo). Só decoração: sem colisores, não mexe em rotas nem coberturas.
    /// As posições saem dos colisores e dos módulos do FBX, por isso acompanham alterações no script do Blender.
    /// </summary>
    public static class SponsorBoards
    {
        const string Root = ProjectSetup.Root + "/Art/Sponsors";
        static readonly string[] Quadrants = { "EN", "ES", "WN", "WS" };

        const float StallBoardW = 1.85f, StallBoardH = 0.69f;     // saia de pano da banca: 2.3 × 0.86 m
        const float WallBoardW = 2.2f, WallBoardH = 0.82f;        // muro de pedra (2 m por módulo)
        const float HouseDisc = 1.25f;                             // UPRANIMAL é circular
        const float Gap = 0.015f;                                  // afastamento da parede (evita z-fighting)

        public static void AddToCidadeVelha(GameObject map, StringBuilder log)
        {
            var sita = LogoMaterial("SITA");
            var upr = LogoMaterial("Upranimal");
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var disc = DiscMesh();
            var parent = new GameObject("Patrocinadores").transform;
            parent.SetParent(map.transform, false);
            Physics.SyncTransforms();
            int count = 0;

            foreach (var tag in Quadrants)
            {
                // 1 · bancas de mercado: SITA de um lado, UPRANIMAL do outro (as duas faces compridas da saia)
                var stall = Find<BoxCollider>(map, "COL_Box_CVR_Banca_" + tag);
                if (stall != null)
                {
                    var b = stall.bounds;
                    bool alongX = b.size.x >= b.size.z;
                    float half = (alongX ? b.extents.z : b.extents.x) - 0.05f;       // saia 5 cm para dentro do tampo
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var n = alongX ? new Vector3(0, 0, s) : new Vector3(s, 0, 0);
                        var p = new Vector3(b.center.x, b.min.y + 0.47f, b.center.z) + n * (half + Gap);
                        if (s < 0) Board(parent, "SITA_Banca_" + tag, quad, sita, p, n, StallBoardW, StallBoardH);
                        else Board(parent, "UPRANIMAL_Banca_" + tag, disc, upr, p, n, 0.72f, 0.72f);
                        count++;
                    }
                }

                // 2 · casa térrea da fila: entre a janela e a porta, por baixo do candeeiro
                string face = tag[1] == 'N' ? "N" : "S";
                var body = Find<BoxCollider>(map, "COL_Box_BLD_Fila_" + tag);
                var door = FindT(map, "BLD_Fila_" + tag + "_Porta_" + face + "1");
                var window = FindT(map, "BLD_Fila_" + tag + "_Janela_" + face + "0");
                if (body != null && door != null && window != null)
                {
                    var b = body.bounds;
                    var toDoor = door.position - b.center;
                    bool alongZ = Mathf.Abs(toDoor.z) > Mathf.Abs(toDoor.x);
                    var n = alongZ ? new Vector3(0, 0, Mathf.Sign(toDoor.z)) : new Vector3(Mathf.Sign(toDoor.x), 0, 0);
                    var mid = (door.position + window.position) * 0.5f;
                    var p = alongZ
                        ? new Vector3(mid.x, 1.6f, n.z > 0 ? b.max.z + Gap : b.min.z - Gap)
                        : new Vector3(n.x > 0 ? b.max.x + Gap : b.min.x - Gap, 1.6f, mid.z);
                    Board(parent, "UPRANIMAL_Casa_" + tag, disc, upr, p, n, HouseDisc, HouseDisc);
                    count++;
                }

                // 3 · muro de pedra do spawn, virado para a praça
                var wall = Find<BoxCollider>(map, "COL_Box_MuroSpawn_" + tag);
                // entre o 2.º e o 3.º módulo do muro: fica à vista da bolsa lateral, ao lado do arbusto da junção
                var segA = FindT(map, "CVR_MuroSpawn_" + tag + "_01");
                var segB = FindT(map, "CVR_MuroSpawn_" + tag + "_02");
                if (wall != null && segA != null && segB != null)
                {
                    var segPos = (segA.position + segB.position) * 0.5f;
                    var b = wall.bounds;
                    bool alongX = b.size.x >= b.size.z;
                    var n = alongX ? new Vector3(0, 0, -Mathf.Sign(b.center.z)) : new Vector3(-Mathf.Sign(b.center.x), 0, 0);
                    float half = alongX ? b.extents.z : b.extents.x;
                    var c = new Vector3(alongX ? segPos.x : b.center.x, b.min.y + 1.35f, alongX ? b.center.z : segPos.z);
                    Board(parent, "SITA_Muro_" + tag, quad, sita, c + n * (half + Gap), n, WallBoardW, WallBoardH);
                    count++;
                }
            }
            log.AppendLine($"Patrocinadores: {count} placas (SITA e UPRANIMAL) nas bancas, casas da fila e muros dos spawns.");
        }

        static void Board(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 normal, float w, float h)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);   // a face visível do quad aponta para -Z
            go.transform.localScale = new Vector3(w, h, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static T Find<T>(GameObject root, string name) where T : Component
        {
            var t = FindT(root, name);
            return t != null ? t.GetComponent<T>() : null;
        }

        static Transform FindT(GameObject root, string name) => GemMapBuilder.FindDeep(root.transform, name);

        static Material LogoMaterial(string sponsor)
        {
            var texPath = Root + "/T_Logo_" + sponsor + ".png";
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti)
            {
                bool dirty = ti.wrapMode != TextureWrapMode.Clamp || ti.textureShape != TextureImporterShape.Texture2D ||
                             ti.textureType != TextureImporterType.Default || ti.alphaSource != TextureImporterAlphaSource.None;
                ti.textureType = TextureImporterType.Default;
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 8;
                ti.maxTextureSize = 1024;
                if (dirty) ti.SaveAndReimport();
            }
            var folder = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Root, "Materials");
            var path = folder + "/M_Logo_" + sponsor + ".mat";
            var shader = Shader.Find("FPSLAN/Toon") ?? Shader.Find("Standard");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.color = Color.white;
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (m.HasProperty("_Emission")) m.SetColor("_Emission", new Color(0.12f, 0.12f, 0.12f));   // legível à sombra
            if (m.HasProperty("_TopLight")) m.SetFloat("_TopLight", 0f);
            if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(0.8f, 0.8f, 0.86f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Disco (32 lados) no plano XY, diâmetro 1, face visível para -Z, UV do quadrado inscrito.</summary>
        static Mesh DiscMesh()
        {
            var path = Root + "/MSH_Disco_Logo.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            const int n = 32;
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, 0f));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                tris.Add(0); tris.Add(b); tris.Add(a);                        // sentido horário visto de -Z
            }
            mesh = new Mesh { name = "MSH_Disco_Logo" };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[v.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
