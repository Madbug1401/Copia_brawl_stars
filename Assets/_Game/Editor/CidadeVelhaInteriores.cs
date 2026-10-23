using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Interiores da Cidade Velha (Pelourinho): a igreja (lado −Z) e o sobrado amarelo em frente (lado +Z) deixam de ser
    /// blocos maciços e passam a ter interior, com mobília detalhada, luzes e sombras:
    ///  - Igreja: nave com bancos, presbitério com altar, arco azul, crucifixo, nichos com imagens, estandartes,
    ///    ventoinhas de parede, teto com vigas; coro alto sobre a entrada com duas escadas. Do coro vê-se a praça pelo
    ///    óculo da fachada (à altura dos olhos) e por duas janelas laterais — posição de atirador furtivo.
    ///  - Sobrado: rés-do-chão rústico em madeira (fogão de ferro com panelas, mesa e cadeiras, baús, berço, cristaleira,
    ///    candeeiro suspenso) e escada para o 1.º andar (quarto); as 5 janelas de cima e a varanda abrem para a praça e
    ///    ficam viradas para o óculo da igreja.
    /// Os dois edifícios crescem para trás (fora da zona jogável) para caber o interior; a fachada mantém-se onde estava.
    /// Só mexe no prefab PF_CidadeVelha. Pode correr-se várias vezes (substitui o grupo "Interiores").
    /// </summary>
    public static partial class CidadeVelhaInteriores
    {
        const string PrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_CidadeVelha.prefab";
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Santiago/CidadeVelha";
        const string Folder = ArtRoot + "/Interiores";
        const string MeshFolder = Folder + "/Meshes";
        const string TexFolder = Folder + "/Textures";

        // Igreja
        const float ChurchFrontZ = -25f;     // face exterior da fachada (mundo)
        const float ChurchDepth = 12f;
        const float ChurchH = 6.8f;
        const float LoftY = 3.3f;            // coro alto: olhos a 4,9 m = centro do óculo
        const float LoftZ = 3.3f;            // fundo do coro (local)
        const float OculusY = 4.9f;
        // Sobrado
        const float HouseFrontZ = 25f;
        const float HouseDepth = 9f;
        const float HouseH = 7f;
        const float Floor2 = 3.55f;          // topo do 1.º andar (= laje da varanda)

        const float W = 18f, T = 0.3f;

        class Mats
        {
            public Material azulejo, mosaico, terracota, rosa, azulRb;
            public Material reboco, ocre, pedraMuro, pedraClara, telha, madeira, troncos, ladrilho, soalho, pintado,
                chama, vidroLuz, lumeVermelho, fogo, sombra, verde, azul, barro, folhas, florRosa, florVerm, ferro;
        }

        static readonly Color WoodDark = new Color(0.55f, 0.38f, 0.26f);
        static readonly Color WoodMid = new Color(0.78f, 0.58f, 0.40f);
        static readonly Color WoodLight = new Color(0.95f, 0.80f, 0.60f);
        static readonly Color Gold = new Color(0.92f, 0.70f, 0.26f);
        static readonly Color Iron = new Color(0.16f, 0.16f, 0.17f);
        static readonly Color Brass = new Color(0.80f, 0.60f, 0.30f);
        static readonly Color ClothWhite = new Color(0.96f, 0.95f, 0.92f);
        static readonly Color Skin = new Color(0.86f, 0.70f, 0.58f);
        static readonly Color Teal = new Color(0.20f, 0.56f, 0.56f);
        static readonly Color Blue = new Color(0.16f, 0.38f, 0.80f);
        static readonly Color Red = new Color(0.72f, 0.12f, 0.12f);

        [MenuItem("FPS-LAN/Mapas/Cidade Velha: interiores (igreja e sobrado)", priority = 43)]
        static void Menu()
        {
            var r = Apply();
            Debug.Log("[Cidade Velha] " + r);
            EditorUtility.DisplayDialog("Cidade Velha — interiores", r, "OK");
        }

        public static string Apply()
        {
            var log = new StringBuilder();
            GemMapBuilder.EnsureFolder(Folder);
            GemMapBuilder.EnsureFolder(MeshFolder);
            GemMapBuilder.EnsureFolder(TexFolder);
            var M = CreateMaterials();

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var cen = FindDeep(root.transform, "Cenario") ?? root.transform;
                // placas dos patrocinadores (SITA e UPRANIMAL): refeitas sempre, para nunca faltarem no mapa
                var oldSp = root.transform.Find("Patrocinadores");
                if (oldSp != null) Object.DestroyImmediate(oldSp.gameObject);
                SponsorBoards.AddToCidadeVelha(root, log);
                var old = cen.Find("Interiores");
                if (old != null) Object.DestroyImmediate(old.gameObject);
                var group = new GameObject("Interiores").transform;
                group.SetParent(cen, false);
                group.gameObject.AddComponent<InteriorLightCulling>();     // só as luzes perto da câmara ficam ligadas

                int colLayer = 0;
                var origCol = FindDeep(cen, "COL_Box_BLD_Capela_N");
                if (origCol != null) colLayer = origCol.gameObject.layer;

                // blocos originais (maciços) passam a inativos — continuam no prefab para se poder voltar atrás
                string[] hide =
                {
                    "BLD_Capela_N_Corpo", "BLD_Capela_N_Cobertura", "BLD_Capela_N_Chamine", "BLD_Capela_N_Oculo",
                    "COL_Box_BLD_Capela_N", "COL_Box_BLD_Capela_N_Portal",
                    "BLD_Sobrado_S_Corpo", "BLD_Sobrado_S_Cobertura", "COL_Box_BLD_Sobrado_S",
                    "BLD_Sobrado_S_Porta_N3", "BLD_Sobrado_S_PortaVaranda_N3", "BLD_Sobrado_S_Varanda_N3",
                };
                foreach (var n in hide)
                {
                    var t = FindDeep(cen, n);
                    if (t != null) t.gameObject.SetActive(false);
                    else log.AppendLine("Aviso: não encontrei " + n);
                }
                foreach (var t in cen.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("BLD_Sobrado_S_Janela")) t.gameObject.SetActive(false);
                var chimney = FindDeep(cen, "BLD_Sobrado_S_Chamine");
                if (chimney != null) chimney.position = new Vector3(7.6f, HouseH - 0.2f, HouseFrontZ + 8.25f);

                // fachada da igreja: tira a porta (verde) e o portal de pedra maciços, fica o frontão e a sineira
                var fach = FindDeep(cen, "BLD_Capela_N_Fachada");
                if (fach != null) OpenChurchFacade(fach, log);

                // ---------------------------------------------------------------- Igreja
                var igreja = new GameObject("Igreja").transform;
                igreja.SetParent(group, false);
                var k = new InteriorKit();
                SetupKit(k, M);
                float cz = ChurchFrontZ;
                k.Floor(0.03f, -9f, cz - ChurchDepth, 9f, cz + 2f);
                k.Floor(0.18f, -4.8f, cz - 11.7f, 4.8f, cz - 8.4f);
                k.Floor(0.33f, -4.5f, cz - 11.7f, 4.5f, cz - 8.7f);
                k.Floor(LoftY, -9f, cz - LoftZ, 9f, cz - 0.3f);
                k.Push(new Vector3(0f, 0f, ChurchFrontZ), 180f);
                Church(k, M);
                k.Pop();
                int tris = k.Build(igreja, "CVI_Igreja", SaveMesh, m => CasterMode(M, m));
                MakeColliders(igreja, k, colLayer);
                MakeLights(igreja, k);
                log.AppendLine($"Igreja: {tris} triângulos, {k.Cols.Count} colisores, {k.Lights.Count} luzes. Coro alto a {LoftY} m (olhos à altura do óculo), 2 escadas.");

                // ---------------------------------------------------------------- Sobrado
                var casa = new GameObject("Sobrado").transform;
                casa.SetParent(group, false);
                k = new InteriorKit();
                SetupKit(k, M);
                k.Floor(0.03f, -9f, HouseFrontZ - 2f, 9f, HouseFrontZ + HouseDepth);
                k.Floor(Floor2, -9.5f, HouseFrontZ - 1.2f, 9.5f, HouseFrontZ + HouseDepth);
                k.Push(new Vector3(0f, 0f, HouseFrontZ), 0f);
                House(k, M);
                k.Pop();
                tris = k.Build(casa, "CVI_Sobrado", SaveMesh, m => CasterMode(M, m));
                MakeColliders(casa, k, colLayer);
                MakeLights(casa, k);
                log.AppendLine($"Sobrado: {tris} triângulos, {k.Cols.Count} colisores, {k.Lights.Count} luzes. 1.º andar a {Floor2} m; 5 janelas e varanda abertas para a praça.");

                // ---------------------------------------------------------------- restantes casas (bares, casas, apartamentos…)
                Casas(group, cen, M, colLayer, log);

                // os limites invisíveis do mapa (N/S) passavam pelas fachadas: ficam só fora dos dois edifícios
                // (as paredes dos edifícios fecham o resto)
                foreach (var lim in new[] { "COL_Box_Limite_N", "COL_Box_Limite_S" })
                {
                    var t = FindDeep(cen, lim);
                    var bc = t != null ? t.GetComponent<BoxCollider>() : null;
                    if (bc == null) { log.AppendLine("Aviso: não encontrei " + lim); continue; }
                    t.gameObject.SetActive(false);
                    var c = t.TransformPoint(bc.center);
                    var sz = Vector3.Scale(bc.size, t.lossyScale);
                    float x0 = c.x - sz.x * 0.5f, x1 = c.x + sz.x * 0.5f;
                    foreach (var part in new[] { new Vector2(x0, -W * 0.5f), new Vector2(W * 0.5f, x1) })
                    {
                        if (part.y - part.x < 0.01f) continue;
                        var go = new GameObject(lim + (part.x < 0 ? "_A" : "_B"));
                        go.layer = t.gameObject.layer;
                        go.transform.SetParent(group, false);
                        go.transform.position = new Vector3((part.x + part.y) * 0.5f, c.y, c.z);
                        go.AddComponent<BoxCollider>().size = new Vector3(part.y - part.x, sz.y, sz.z);
                    }
                }
                log.AppendLine("Limites N/S do mapa abertos à frente da igreja e do sobrado.");

                foreach (var t in group.GetComponentsInChildren<Transform>(true))
                    if (t.GetComponent<Light>() == null) t.gameObject.isStatic = true;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            SetMapAreas(log);
            return log.ToString();
        }

        /// <summary>O MapInfo da cena passa a contar o interior dos dois edifícios como área jogável
        /// (saltos e gemas largadas lá dentro não são puxados para a praça).</summary>
        static void SetMapAreas(StringBuilder log)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string scenePath = ProjectSetup.Root + "/Scenes/CidadeVelha.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneByPath(scenePath);
            bool opened = false;
            if (!scene.isLoaded)
            {
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                opened = true;
            }
            MapInfo mi = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                mi = go.GetComponentInChildren<MapInfo>(true);
                if (mi != null) break;
            }
            if (mi != null)
            {
                Undo.RecordObject(mi, "Interiores");
                mi.extraAreas = new[]
                {
                    new Bounds(new Vector3(0f, 2f, ChurchFrontZ - ChurchDepth * 0.5f), new Vector3(W - 1.2f, 6f, ChurchDepth - 0.8f)),
                    new Bounds(new Vector3(0f, 2f, HouseFrontZ + HouseDepth * 0.5f), new Vector3(W - 1.2f, 6f, HouseDepth - 0.8f)),
                };
                EditorUtility.SetDirty(mi);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                log.AppendLine("MapInfo: interiores da igreja e do sobrado contam como área jogável.");
            }
            else log.AppendLine("Aviso: MapInfo não encontrado na cena CidadeVelha.");
            if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }

        static void SetupKit(InteriorKit k, Mats M)
        {
            k.Setup(M.reboco, 0.35f);
            k.Setup(M.ocre, 0.35f);
            k.Setup(M.pedraMuro, 0.5f);
            k.Setup(M.pedraClara, 0.6f);
            k.Setup(M.telha, 0.5f);
            k.Setup(M.madeira, 0.9f);
            k.Setup(M.troncos, 0.5f);
            k.Setup(M.ladrilho, 0.5f);
            k.Setup(M.soalho, 0.5f);
            k.Setup(M.pintado, 1f);
            k.Setup(M.sombra, 1f, false);
            k.Setup(M.chama, 1f, false);
            k.Setup(M.vidroLuz, 1f, false);
            k.Setup(M.lumeVermelho, 1f, false);
            k.Setup(M.fogo, 1f, false);
            k.Setup(M.verde, 0.8f);
            k.Setup(M.azul, 0.8f);
            k.Setup(M.ferro, 1f);
            if (M.azulejo != null) k.Setup(M.azulejo, 1.6f);       // 4 azulejos por repetição → ~15 cm cada
            if (M.mosaico != null) k.Setup(M.mosaico, 1.6f);       // 2 ladrilhos por repetição → ~30 cm
            if (M.terracota != null) k.Setup(M.terracota, 0.35f);
            if (M.rosa != null) k.Setup(M.rosa, 0.35f);
            if (M.azulRb != null) k.Setup(M.azulRb, 0.35f);
        }

        // =====================================================================================================
        // paredes com vãos

        /// <summary>Divide um retângulo de parede (a ao longo da parede, y na vertical) em caixas, tirando os vãos.
        /// Vãos: (a0, a1, y0, y1).</summary>
        static List<Vector4> Decompose(float a0, float a1, float y0, float y1, List<Vector4> holes)
        {
            var xs = new List<float> { a0, a1 };
            foreach (var h in holes)
            {
                if (h.x > a0 && h.x < a1) xs.Add(h.x);
                if (h.y > a0 && h.y < a1) xs.Add(h.y);
            }
            xs.Sort();
            var res = new List<Vector4>();
            for (int i = 0; i < xs.Count - 1; i++)
            {
                float l = xs[i], r = xs[i + 1];
                if (r - l < 1e-4f) continue;
                float mid = (l + r) * 0.5f;
                var ys = new List<Vector2> { new Vector2(y0, y1) };
                foreach (var h in holes)
                {
                    if (mid < h.x || mid > h.y) continue;
                    var next = new List<Vector2>();
                    foreach (var s in ys)
                    {
                        if (h.w <= s.x || h.z >= s.y) { next.Add(s); continue; }
                        if (h.z > s.x) next.Add(new Vector2(s.x, h.z));
                        if (h.w < s.y) next.Add(new Vector2(h.w, s.y));
                    }
                    ys = next;
                }
                foreach (var s in ys)
                {
                    if (s.y - s.x < 1e-4f) continue;
                    bool merged = false;
                    for (int j = 0; j < res.Count; j++)
                    {
                        var b = res[j];
                        if (Mathf.Abs(b.y - l) < 1e-4f && Mathf.Abs(b.z - s.x) < 1e-4f && Mathf.Abs(b.w - s.y) < 1e-4f)
                        {
                            b.y = r;
                            res[j] = b;
                            merged = true;
                            break;
                        }
                    }
                    if (!merged) res.Add(new Vector4(l, r, s.x, s.y));
                }
            }
            return res;
        }

        /// <summary>Parede ao longo de X (frente/fundo): z de f0 a f1.</summary>
        static void WallX(InteriorKit k, Material m, float f0, float f1, float a0, float a1, float y0, float y1, List<Vector4> holes, List<Vector4> colHoles, string col)
        {
            foreach (var b in Decompose(a0, a1, y0, y1, holes)) k.Box(m, new Vector3(b.x, b.z, f0), new Vector3(b.y, b.w, f1));
            if (col != null)
            {
                int i = 0;
                foreach (var b in Decompose(a0, a1, y0, y1, colHoles)) k.Col(col + "_" + i++, new Vector3(b.x, b.z, f0), new Vector3(b.y, b.w, f1));
            }
        }

        /// <summary>Parede ao longo de Z (lados): x de f0 a f1.</summary>
        static void WallZ(InteriorKit k, Material m, float f0, float f1, float a0, float a1, float y0, float y1, List<Vector4> holes, List<Vector4> colHoles, string col)
        {
            foreach (var b in Decompose(a0, a1, y0, y1, holes)) k.Box(m, new Vector3(f0, b.z, b.x), new Vector3(f1, b.w, b.y));
            if (col != null)
            {
                int i = 0;
                foreach (var b in Decompose(a0, a1, y0, y1, colHoles)) k.Col(col + "_" + i++, new Vector3(f0, b.z, b.x), new Vector3(f1, b.w, b.y));
            }
        }

        static Vector4 H(float c, float w, float y0, float y1) => new Vector4(c - w * 0.5f, c + w * 0.5f, y0, y1);

        /// <summary>Casca exterior: embasamento de pedra, cornija e cunhais (o reboco das paredes é feito à parte).</summary>
        static void Trim(InteriorKit k, Mats M, float D, float h, List<Vector4> frontDoors, bool friso)
        {
            float hw = W * 0.5f;
            // embasamento (0,55 m), interrompido nas portas da frente
            var baseHoles = new List<Vector4>();
            foreach (var d in frontDoors) baseHoles.Add(new Vector4(d.x, d.y, -1f, 1f));
            WallX(k, M.pedraMuro, -0.04f, 0f, -hw - 0.04f, hw + 0.04f, 0f, 0.55f, baseHoles, null, null);
            WallX(k, M.pedraMuro, D, D + 0.04f, -hw - 0.04f, hw + 0.04f, 0f, 0.55f, new List<Vector4>(), null, null);
            WallZ(k, M.pedraMuro, -hw - 0.04f, -hw, 0f, D, 0f, 0.55f, new List<Vector4>(), null, null);
            WallZ(k, M.pedraMuro, hw, hw + 0.04f, 0f, D, 0f, 0.55f, new List<Vector4>(), null, null);
            // cornija
            float c0 = h - 0.18f;
            k.Box(M.pedraClara, new Vector3(-hw - 0.06f, c0, -0.06f), new Vector3(hw + 0.06f, h, 0f));
            k.Box(M.pedraClara, new Vector3(-hw - 0.06f, c0, D), new Vector3(hw + 0.06f, h, D + 0.06f));
            k.Box(M.pedraClara, new Vector3(-hw - 0.06f, c0, 0f), new Vector3(-hw, h, D));
            k.Box(M.pedraClara, new Vector3(hw, c0, 0f), new Vector3(hw + 0.06f, h, D));
            if (friso)
            {
                float f0 = h * 0.5f - 0.07f, f1 = h * 0.5f + 0.07f;
                k.Box(M.pedraClara, new Vector3(-hw - 0.05f, f0, -0.05f), new Vector3(hw + 0.05f, f1, 0f));
                k.Box(M.pedraClara, new Vector3(-hw - 0.05f, f0, D), new Vector3(hw + 0.05f, f1, D + 0.05f));
                k.Box(M.pedraClara, new Vector3(-hw - 0.05f, f0, 0f), new Vector3(-hw, f1, D));
                k.Box(M.pedraClara, new Vector3(hw, f0, 0f), new Vector3(hw + 0.05f, f1, D));
            }
            // cunhais de pedra (blocos alternados)
            foreach (var qx in new[] { -1f, 1f })
                foreach (var qz in new[] { 0f, 1f })
                {
                    int i = 0;
                    for (float z = 0.55f; z < h - 0.4f; z += 0.36f, i++)
                    {
                        float w = i % 2 == 0 ? 0.42f : 0.30f;
                        float cx = qx * (hw + 0.05f) - qx * w * 0.5f;
                        float cz = qz < 0.5f ? -0.05f + w * 0.5f : D + 0.05f - w * 0.5f;
                        k.BoxB(M.pedraClara, new Vector3(cx, z, cz), new Vector3(w, 0.34f, w));
                    }
                }
        }

        static void GableRoof(InteriorKit k, Mats M, Material wall, float D, float h, float rise)
        {
            float hw = W * 0.5f, o = 0.4f, zc = D * 0.5f;
            float slope = rise / zc;
            float ye = h - o * slope;
            var A = new Vector3(-hw - o, ye, -o);
            var B = new Vector3(hw + o, ye, -o);
            var R0 = new Vector3(-hw - o, h + rise, zc);
            var R1 = new Vector3(hw + o, h + rise, zc);
            var C2 = new Vector3(-hw - o, ye, D + o);
            var D2 = new Vector3(hw + o, ye, D + o);
            var up = Vector3.up * 0.14f;
            k.Poly(M.telha, new[] { A + up, R0 + up, R1 + up, B + up }, new Vector3(0, 1, -1));
            k.Poly(M.telha, new[] { C2 + up, D2 + up, R1 + up, R0 + up }, new Vector3(0, 1, 1));
            k.Poly(M.madeira, new[] { A, R0, R1, B }, new Vector3(0, -1, 1));
            k.Poly(M.madeira, new[] { C2, D2, R1, R0 }, new Vector3(0, -1, -1));
            // espessura nas bordas
            k.Poly(M.madeira, new[] { A, A + up, B + up, B }, Vector3.back);
            k.Poly(M.madeira, new[] { C2, C2 + up, D2 + up, D2 }, Vector3.forward);
            foreach (var sx in new[] { -1f, 1f })
            {
                float x = sx * (hw + o);
                k.Poly(M.madeira, new[] { new Vector3(x, ye, -o), new Vector3(x, ye + 0.14f, -o), new Vector3(x, h + rise + 0.14f, zc), new Vector3(x, h + rise, zc) }, Vector3.right * sx);
                k.Poly(M.madeira, new[] { new Vector3(x, ye, D + o), new Vector3(x, ye + 0.14f, D + o), new Vector3(x, h + rise + 0.14f, zc), new Vector3(x, h + rise, zc) }, Vector3.right * sx);
                // empena (triângulo de reboco)
                float xw = sx * hw;
                k.Poly(wall, new[] { new Vector3(xw, h, 0f), new Vector3(xw, h, D), new Vector3(xw, h + rise, zc) }, Vector3.right * sx);
            }
            // cumeeira
            k.Push(new Vector3(-hw - o, h + rise + 0.12f, zc), 0f, 0f, -90f);
            k.Cyl(M.telha, Vector3.zero, 0.12f, W + 2 * o, 8);
            k.Pop();
            k.Col("Telhado", new Vector3(-hw, h, 0f), new Vector3(hw, h + rise * 0.7f, D));
        }

        static void HipRoof(InteriorKit k, Mats M, float D, float h, float rise)
        {
            float hw = W * 0.5f, o = 0.4f, zc = D * 0.5f;
            float slope = rise / zc;
            float ye = h - o * slope;
            var A = new Vector3(-hw - o, ye, -o);
            var B = new Vector3(hw + o, ye, -o);
            var C2 = new Vector3(hw + o, ye, D + o);
            var D2 = new Vector3(-hw - o, ye, D + o);
            var R0 = new Vector3(-hw + zc, h + rise, zc);
            var R1 = new Vector3(hw - zc, h + rise, zc);
            var up = Vector3.up * 0.14f;
            k.Poly(M.telha, new[] { A + up, R0 + up, R1 + up, B + up }, new Vector3(0, 1, -1));
            k.Poly(M.telha, new[] { C2 + up, R1 + up, R0 + up, D2 + up }, new Vector3(0, 1, 1));
            k.Poly(M.telha, new[] { D2 + up, R0 + up, A + up }, new Vector3(-1, 1, 0));
            k.Poly(M.telha, new[] { B + up, R1 + up, C2 + up }, new Vector3(1, 1, 0));
            k.Poly(M.madeira, new[] { A, R0, R1, B }, new Vector3(0, -1, 1));
            k.Poly(M.madeira, new[] { C2, R1, R0, D2 }, new Vector3(0, -1, -1));
            k.Poly(M.madeira, new[] { D2, R0, A }, new Vector3(1, -1, 0));
            k.Poly(M.madeira, new[] { B, R1, C2 }, new Vector3(-1, -1, 0));
            k.Poly(M.madeira, new[] { A, A + up, B + up, B }, Vector3.back);
            k.Poly(M.madeira, new[] { C2, C2 + up, D2 + up, D2 }, Vector3.forward);
            k.Poly(M.madeira, new[] { D2, D2 + up, A + up, A }, Vector3.left);
            k.Poly(M.madeira, new[] { B, B + up, C2 + up, C2 }, Vector3.right);
            k.Push(new Vector3(-hw + zc, h + rise + 0.12f, zc), 0f, 0f, -90f);
            k.Cyl(M.telha, Vector3.zero, 0.12f, W - 2 * zc, 8);
            k.Pop();
            foreach (var p in new[] { A, B, C2, D2 })
            {
                var r = p.x < 0 ? R0 : R1;
                k.Rod(M.telha, p + Vector3.up * 0.2f, r + Vector3.up * 0.2f, 0.1f, 6);
            }
            k.Col("Telhado", new Vector3(-hw, h, 0f), new Vector3(hw, h + rise * 0.7f, D));
        }

        static void Blob(InteriorKit k, Mats M, float x, float y, float z, float sx, float sz, float yaw = 0f)
        {
            k.FlatQuad(M.sombra, new Vector3(x, y + 0.012f, z), sx, sz, yaw);
        }

        // =====================================================================================================
        // IGREJA (local: fachada em z = 0, interior para +z, x da esquerda para a direita de quem entra… espelhado)

        static void Church(InteriorKit k, Mats M)
        {
            float D = ChurchDepth, h = ChurchH, hw = W * 0.5f;
            float iz0 = T, iz1 = D - T, ix = hw - T;

            // ---------------- paredes
            var front = new List<Vector4>
            {
                H(0f, 1.9f, 0f, 3.0f),                    // porta
                H(0f, 1.04f, OculusY - 0.52f, OculusY + 0.52f), // óculo
                H(-5.5f, 0.9f, 3.85f, 5.45f), H(5.5f, 0.9f, 3.85f, 5.45f),   // janelas do coro
            };
            WallX(k, M.reboco, 0f, T, -hw, hw, 0f, h, front, front, "Fachada");
            WallX(k, M.reboco, D - T, D, -hw, hw, 0f, h, new List<Vector4>(), new List<Vector4>(), "Fundo");
            var side = new List<Vector4> { H(5.2f, 0.9f, 3.4f, 5.6f), H(9.4f, 0.9f, 3.4f, 5.6f) };
            WallZ(k, M.reboco, -hw, -hw + T, T, D - T, 0f, h, side, new List<Vector4>(), "LadoA");
            WallZ(k, M.reboco, hw - T, hw, T, D - T, 0f, h, side, new List<Vector4>(), "LadoB");
            Trim(k, M, D, h, new List<Vector4> { H(0f, 1.9f, 0f, 3f) }, false);
            GableRoof(k, M, M.reboco, D, h, 2.6f);

            // ---------------- óculo (anel de pedra por fora e por dentro, vão octogonal, tubo)
            k.Push(new Vector3(0f, OculusY, 0f), 0f, 90f, 0f);   // eixo Y do toro → +Z
            // anéis largos à frente e atrás tapam os cantos do vão quadrado; o tubo tem as duas faces
            k.Torus(M.pedraClara, new Vector3(0f, -0.08f, 0f), 0.64f, 0.15f, 24, 6);
            k.Torus(M.pedraClara, new Vector3(0f, T + 0.06f, 0f), 0.63f, 0.14f, 24, 6);
            k.Lathe(M.pedraClara, new Vector3(0f, -0.08f, 0f), new[] { new Vector2(0.5f, T + 0.14f), new Vector2(0.5f, 0f) }, 20, true, false, false);
            k.Lathe(M.pedraClara, new Vector3(0f, -0.08f, 0f), new[] { new Vector2(0.53f, 0f), new Vector2(0.53f, T + 0.14f) }, 20, true, false, false);
            k.Pop();

            // ---------------- portal e porta aberta
            k.Box(M.pedraClara, new Vector3(-1.3f, 0f, -0.2f), new Vector3(-0.95f, 3.0f, T + 0.02f));
            k.Box(M.pedraClara, new Vector3(0.95f, 0f, -0.2f), new Vector3(1.3f, 3.0f, T + 0.02f));
            k.Box(M.pedraClara, new Vector3(-1.3f, 3.0f, -0.2f), new Vector3(1.3f, 3.6f, 0.02f));
            k.Box(M.pedraClara, new Vector3(-1.45f, 3.6f, -0.3f), new Vector3(1.45f, 3.74f, 0.02f));
            k.Box(M.pedraClara, new Vector3(-0.2f, 3.08f, -0.24f), new Vector3(0.2f, 3.55f, -0.2f));   // fecho
            k.Box(M.pedraClara, new Vector3(-1.4f, 0f, -0.75f), new Vector3(1.4f, 0.05f, 0.02f));      // soleira
            k.Box(M.pedraClara, new Vector3(-0.95f, 0f, 0f), new Vector3(0.95f, 0.03f, T));
            k.Col("Portal_A", new Vector3(-1.3f, 0f, -0.2f), new Vector3(-0.95f, 3.0f, 0f));
            k.Col("Portal_B", new Vector3(0.95f, 0f, -0.2f), new Vector3(1.3f, 3.0f, 0f));
            DoorLeaf(k, M, M.verde, new Vector3(-0.95f, 0.04f, T), 0.95f, 2.92f, -100f, true, "PortaA");
            DoorLeaf(k, M, M.verde, new Vector3(0.95f, 0.04f, T), 0.95f, 2.92f, 100f, false, "PortaB");

            // ---------------- janelas do coro (fachada) e janelas altas laterais
            foreach (var sx in new[] { -1f, 1f })
            {
                float cx = sx * 5.5f;
                StoneFrame(k, M, cx, 3.85f, 0.9f, 1.6f, true);
                OpenShuttersFlat(k, M, M.verde, cx, 3.85f, 0.9f, 1.6f);
                k.Box(M.madeira, new Vector3(cx - 0.5f, 3.8f, T - 0.02f), new Vector3(cx + 0.5f, 3.86f, T + 0.08f));   // peitoril interior
                foreach (var z in new[] { 5.2f, 9.4f })
                {
                    k.Push(new Vector3(sx * hw, 0f, z), sx > 0 ? -90f : 90f);   // local: +z para fora? usa x ao longo da parede
                    SideWindow(k, M);
                    k.Pop();
                }
            }

            // ---------------- chão, presbitério, teto
            k.Box(M.ladrilho, new Vector3(-ix, -0.06f, iz0), new Vector3(ix, 0.03f, iz1));
            k.Col("Chao", new Vector3(-ix, -0.4f, iz0 - 0.4f), new Vector3(ix, 0.03f, iz1));
            k.C = new Color(0.93f, 0.90f, 0.84f);
            k.Box(M.pedraClara, new Vector3(-4.8f, 0.03f, 8.4f), new Vector3(4.8f, 0.18f, iz1));
            k.Box(M.pedraClara, new Vector3(-4.5f, 0.18f, 8.7f), new Vector3(4.5f, 0.33f, iz1));
            k.C = Color.white;
            k.Col("Degrau1", new Vector3(-4.8f, 0.03f, 8.4f), new Vector3(4.8f, 0.18f, iz1));
            k.Col("Degrau2", new Vector3(-4.5f, 0.18f, 8.7f), new Vector3(4.5f, 0.33f, iz1));
            Runner(k, M, -0.75f, 0.75f, 0.03f, T + 0.05f, 8.3f, Red, Gold);
            Runner(k, M, -1.6f, 1.6f, 0.33f, 8.95f, 10.25f, Red, Gold);

            // teto branco com vigas verde-água
            k.Box(M.reboco, new Vector3(-ix, h, iz0), new Vector3(ix, h + 0.12f, iz1));
            k.Col("Teto", new Vector3(-hw, h, 0f), new Vector3(hw, h + 0.15f, D));
            k.C = Teal;
            for (float x = -7.5f; x <= 7.51f; x += 1.5f)
                k.Box(M.madeira, new Vector3(x - 0.1f, h - 0.26f, iz0), new Vector3(x + 0.1f, h, iz1));
            k.Box(M.madeira, new Vector3(-ix, h - 0.3f, iz0), new Vector3(ix, h, iz0 + 0.18f));
            k.Box(M.madeira, new Vector3(-ix, h - 0.3f, iz1 - 0.18f), new Vector3(ix, h, iz1));
            k.C = Color.white;
            // rodapé
            k.C = new Color(0.35f, 0.42f, 0.55f);
            k.Box(M.pintado, new Vector3(-ix, 0.03f, iz1 - 0.03f), new Vector3(ix, 0.33f, iz1));
            k.Box(M.pintado, new Vector3(-ix, 0.03f, iz0), new Vector3(-ix + 0.03f, 0.3f, iz1));
            k.Box(M.pintado, new Vector3(ix - 0.03f, 0.03f, iz0), new Vector3(ix, 0.3f, iz1));
            k.C = Color.white;

            // ---------------- coro alto
            k.Box(M.soalho, new Vector3(-ix, LoftY - 0.25f, iz0), new Vector3(ix, LoftY, LoftZ));
            k.Col("Coro", new Vector3(-ix, LoftY - 0.25f, iz0), new Vector3(ix, LoftY, LoftZ));
            k.C = Teal;
            k.Box(M.madeira, new Vector3(-ix, LoftY - 0.55f, LoftZ - 0.22f), new Vector3(ix, LoftY - 0.25f, LoftZ + 0.08f));
            for (float x = -7.5f; x <= 7.51f; x += 1.5f)
                k.Box(M.madeira, new Vector3(x - 0.08f, LoftY - 0.45f, iz0), new Vector3(x + 0.08f, LoftY - 0.25f, LoftZ - 0.22f));
            k.C = Color.white;
            foreach (var x in new[] { -5.4f, -2.4f, 2.4f, 5.4f })
            {
                Column(k, M, new Vector3(x, 0.03f, LoftZ - 0.07f), LoftY - 0.55f - 0.03f);
                k.Col("Coluna", new Vector3(x - 0.2f, 0f, LoftZ - 0.27f), new Vector3(x + 0.2f, LoftY - 0.55f, LoftZ + 0.13f));
                Blob(k, M, x, 0.03f, LoftZ - 0.07f, 0.9f, 0.9f);
            }
            Balustrade(k, M, new Vector3(-7.55f, LoftY, LoftZ - 0.05f), new Vector3(7.55f, LoftY, LoftZ - 0.05f), 1.0f, WoodDark);
            k.Col("Balaustrada", new Vector3(-7.6f, LoftY, LoftZ - 0.14f), new Vector3(7.6f, LoftY + 1.05f, LoftZ + 0.04f));

            // escadas (uma de cada lado) — pedra caiada com cobertor de madeira, 15 degraus de 0,22 m
            foreach (var sx in new[] { -1f, 1f })
            {
                k.Push(Vector3.zero, Quaternion.identity, new Vector3(sx, 1f, 1f));
                ChurchStair(k, M, sx);
                k.Pop();
            }

            // ---------------- mobília do coro
            Harmonium(k, M, new Vector3(2.7f, LoftY, iz0 + 0.02f), 0f);
            Stool(k, M, new Vector3(2.7f, LoftY, iz0 + 1.15f));
            Bench(k, M, new Vector3(-3.0f, LoftY, iz0 + 0.02f), 2.0f, 0f, WoodMid);
            MusicStand(k, M, new Vector3(3.6f, LoftY, 2.1f), 200f);
            foreach (var p in new[] { new Vector3(-7.15f, LoftY, 0.75f), new Vector3(7.15f, LoftY, 0.75f) })
                Chair(k, M, p, Random01(p) * 360f, WoodMid, false);
            BookStack(k, M, new Vector3(-3.6f, LoftY + 0.47f, iz0 + 0.2f), 4);
            BookStack(k, M, new Vector3(-2.4f, LoftY + 0.47f, iz0 + 0.2f), 3);
            // corda do sino
            k.C = new Color(0.72f, 0.62f, 0.45f);
            k.Rod(M.pintado, new Vector3(0.9f, h - 0.2f, 1.3f), new Vector3(0.9f, LoftY + 1.0f, 1.3f), 0.018f, 5);
            k.C = Red;
            k.Cone(M.pintado, new Vector3(0.9f, LoftY + 0.75f, 1.3f), 0.05f, 0.02f, 0.25f, 6);
            k.C = Color.white;

            // ---------------- debaixo do coro: pias de água benta, confessionário, velas votivas
            foreach (var sx in new[] { -1f, 1f }) HolyWaterFont(k, M, new Vector3(sx * 1.75f, 0.03f, 0.75f));
            k.Col("PiaA", new Vector3(-2.0f, 0f, 0.5f), new Vector3(-1.5f, 1.0f, 1.0f));
            k.Col("PiaB", new Vector3(1.5f, 0f, 0.5f), new Vector3(2.0f, 1.0f, 1.0f));
            Confessional(k, M, new Vector3(-ix, 0.03f, 1.7f));
            VotiveStand(k, M, new Vector3(7.2f, 0.03f, 1.6f), -90f);
            NoticeBoard(k, M, new Vector3(ix - 0.02f, 1.5f, 2.6f), -90f);

            // ---------------- nave: bancos
            for (int r = 0; r < 4; r++)
            {
                float z = 4.6f + r * 0.95f;
                foreach (var sx in new[] { -1f, 1f })
                {
                    float cx = sx * 3.75f;
                    Pew(k, M, new Vector3(cx, 0.03f, z), 5.3f, r * 2 + (sx > 0 ? 1 : 0));
                    k.Col("Banco_Assento", new Vector3(cx - 2.65f, 0f, z - 0.22f), new Vector3(cx + 2.65f, 0.48f, z + 0.22f));
                    k.Col("Banco_Costas", new Vector3(cx - 2.65f, 0f, z - 0.3f), new Vector3(cx + 2.65f, 0.95f, z - 0.2f));
                    Blob(k, M, cx, 0.03f, z - 0.05f, 5.6f, 0.9f);
                }
            }

            // ---------------- presbitério
            CommunionRail(k, M);
            Altar(k, M);
            BlueArch(k, M);
            Crucifix(k, M, new Vector3(0f, 2.75f, iz1 - 0.08f));
            foreach (var sx in new[] { -1f, 1f })
            {
                Niche(k, M, new Vector3(sx * 3.85f, 1.6f, iz1), sx < 0 ? 0 : 1);
                Niche(k, M, new Vector3(sx * 7.2f, 1.2f, iz1), sx < 0 ? 2 : 3);
                Banner(k, M, new Vector3(sx * 5.45f, 5.7f, iz1 - 0.05f), sx < 0 ? Blue : Red);
                Candlestick(k, M, new Vector3(sx * 1.95f, 0.33f, 10.35f), 1.15f, true);
                Vase(k, M, new Vector3(sx * 1.65f, 0.33f, 11.0f), sx < 0);
                WallFan(k, M, new Vector3(sx * ix, 3.35f, 10.8f), sx);
                WallFan(k, M, new Vector3(sx * 4.2f, 4.9f, iz1), 0f);
                StationPlaque(k, M, new Vector3(sx * ix, 1.75f, 9.0f), sx);
                StationPlaque(k, M, new Vector3(sx * ix, 1.75f, 11.15f), sx);
                Sconce(k, M, new Vector3(sx * ix, 2.6f, 9.95f), sx);
            }
            Lectern(k, M, new Vector3(-3.3f, 0.33f, 9.35f), 20f);
            k.Col("Ambao", new Vector3(-3.65f, 0.33f, 9.0f), new Vector3(-2.95f, 1.45f, 9.7f));
            PresiderChair(k, M, new Vector3(3.2f, 0.33f, 10.95f));
            k.Col("Cadeira", new Vector3(2.85f, 0.33f, 10.55f), new Vector3(3.55f, 1.6f, 11.3f));
            foreach (var x in new[] { 2.4f, 4.0f })
                Chair(k, M, new Vector3(x, 0.33f, 11.0f), 180f, WoodDark, true);
            Chandelier(k, M, new Vector3(0f, h - 0.26f, 6.2f), 1.9f);
            SanctuaryLamp(k, M, new Vector3(1.55f, h - 0.26f, 9.7f), 3.0f);

            k.Light("Luz_Altar", new Vector3(0f, 3.4f, 9.7f), new Color(1f, 0.82f, 0.6f), 8.5f, 1.35f, LightShadows.Soft);
            k.Light("Luz_Nave", new Vector3(0f, 4.3f, 6.2f), new Color(1f, 0.86f, 0.66f), 10f, 1.0f, LightShadows.None);
            k.Light("Luz_Coro", new Vector3(0f, 5.7f, 1.8f), new Color(1f, 0.9f, 0.75f), 7f, 0.75f, LightShadows.None);
        }

        static void ChurchStair(InteriorKit k, Mats M, float sx)
        {
            // escada encostada à parede lateral (x de 7,6 a 8,7 no lado +x local; espelhada pelo Push com escala)
            const int n = 15;
            float rise = LoftY / n, zTop = LoftZ, zBot = 8.0f, run = (zBot - zTop) / n;
            float x0 = 7.6f, x1 = W * 0.5f - T;
            for (int i = 0; i < n; i++)
            {
                float zb = zBot - (i + 1) * run, zf = zBot - i * run, y = (i + 1) * rise;
                k.Box(M.reboco, new Vector3(x0, 0.03f, zb), new Vector3(x1, y - 0.05f, zf));
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(x0 - 0.03f, y - 0.05f, zb), new Vector3(x1, y, zf + 0.03f));
                k.C = Color.white;
                MirCol(k, sx, "Degrau", new Vector3(x0, 0f, zb), new Vector3(x1, y, zf));
            }
            // rampa (colisor suave) pelos focinhos dos degraus
            var p0 = new Vector3(0f, 0f, zBot + run);
            var p1 = new Vector3(0f, LoftY, zTop + run);
            var d = (p1 - p0).normalized;
            var rot = Quaternion.LookRotation(d, Vector3.up);
            var mid = (p0 + p1) * 0.5f - (rot * Vector3.up) * 0.1f;
            mid.x = (x0 + x1) * 0.5f;
            MirColRot(k, sx, "Rampa", mid, new Vector3(x1 - x0, 0.2f, (p1 - p0).magnitude), rot);
            // corrimão do lado da nave: balaústres em cada degrau até à barra (a altura de cada um acompanha a inclinação),
            // prumo no fundo e no topo, e um troço horizontal que liga ao prumo da balaustrada do coro
            float hr = 0.95f, xr = x0 + 0.06f;
            var ra = new Vector3(xr, rise + hr, zBot);
            var rb = new Vector3(xr, LoftY + hr, zTop + run);
            System.Func<float, float> railY = z => Mathf.Lerp(ra.y, rb.y, Mathf.InverseLerp(ra.z, rb.z, z));
            for (int i = 0; i < n; i++)
            {
                float z = zBot - i * run - run * 0.5f;
                float y = (i + 1) * rise;
                k.C = WoodDark;
                k.Push(new Vector3(xr, y, z), Quaternion.identity, new Vector3(0.75f, 1f, 0.75f));
                Baluster(k, M, Vector3.zero, railY(z) - y - 0.01f);
                k.Pop();
                k.C = Color.white;
            }
            k.C = WoodDark;
            k.Rod(M.madeira, ra, rb, 0.05f, 8);
            k.Rod(M.madeira, rb, new Vector3(xr, LoftY + hr, LoftZ - 0.05f), 0.05f, 8);
            // prumo do fundo e prumo do topo (no coro), com remate
            k.BoxB(M.madeira, new Vector3(xr, 0.03f, zBot - run * 0.3f), new Vector3(0.14f, rise + hr + 0.1f, 0.14f));
            k.Sphere(M.madeira, new Vector3(xr, rise + hr + 0.17f, zBot - run * 0.3f), 0.08f, 8, 4);
            k.BoxB(M.madeira, new Vector3(xr, LoftY, rb.z), new Vector3(0.14f, hr + 0.1f, 0.14f));
            k.Sphere(M.madeira, new Vector3(xr, LoftY + hr + 0.17f, rb.z), 0.08f, 8, 4);
            k.C = Color.white;
            var r0 = new Vector3(x0 + 0.06f, rise, zBot);
            var r1 = new Vector3(x0 + 0.06f, LoftY, zTop + run);
            var dr = (r1 - r0).normalized;
            var rr = Quaternion.LookRotation(dr, Vector3.up);
            MirColRot(k, sx, "Corrimao", (r0 + r1) * 0.5f + rr * Vector3.up * 0.55f, new Vector3(0.1f, 1.1f, (r1 - r0).magnitude), rr);
            Blob(k, M, (x0 + x1) * 0.5f, 0.03f, zBot - 0.2f, 1.6f, 1.2f);
        }

        // colisores dentro de um Push espelhado (escala −1 em X): reconstrói a caixa a partir dos cantos
        static void MirCol(InteriorKit k, float sx, string name, Vector3 a, Vector3 b)
        {
            if (sx > 0) { k.Col(name, a, b); return; }
            k.Pop();
            k.Col(name, new Vector3(-a.x, a.y, a.z), new Vector3(-b.x, b.y, b.z));
            k.Push(Vector3.zero, Quaternion.identity, new Vector3(sx, 1f, 1f));
        }

        static void MirColRot(InteriorKit k, float sx, string name, Vector3 c, Vector3 size, Quaternion rot)
        {
            if (sx > 0) { k.ColRot(name, c, size, rot); return; }
            k.Pop();
            var e = rot.eulerAngles;
            k.ColRot(name, new Vector3(-c.x, c.y, c.z), size, Quaternion.Euler(e.x, -e.y, -e.z));
            k.Push(Vector3.zero, Quaternion.identity, new Vector3(sx, 1f, 1f));
        }

        static void SideWindow(InteriorKit k, Mats M)
        {
            // referencial: x ao longo da parede, z para dentro (0 = face exterior)
            float y0 = 3.4f, w = 0.9f, hh = 2.2f;
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pedraClara, new Vector3(-w * 0.5f - 0.14f, y0 - 0.12f, -0.06f), new Vector3(w * 0.5f + 0.14f, y0, 0.02f));
            k.Box(M.pedraClara, new Vector3(-w * 0.5f - 0.14f, y0, -0.06f), new Vector3(-w * 0.5f, y0 + hh, 0f));
            k.Box(M.pedraClara, new Vector3(w * 0.5f, y0, -0.06f), new Vector3(w * 0.5f + 0.14f, y0 + hh, 0f));
            k.Box(M.pedraClara, new Vector3(-w * 0.5f - 0.14f, y0 + hh, -0.06f), new Vector3(w * 0.5f + 0.14f, y0 + hh + 0.16f, 0f));
            k.C = Iron;
            for (float x = -w * 0.5f + 0.15f; x < w * 0.5f - 0.05f; x += 0.15f)
                k.Rod(M.pintado, new Vector3(x, y0, T * 0.5f), new Vector3(x, y0 + hh, T * 0.5f), 0.012f, 4, false);
            foreach (var y in new[] { y0 + 0.7f, y0 + 1.5f })
                k.Box(M.pintado, new Vector3(-w * 0.5f, y - 0.015f, T * 0.5f - 0.015f), new Vector3(w * 0.5f, y + 0.015f, T * 0.5f + 0.015f));
            k.C = Color.white;
        }

        static void StoneFrame(InteriorKit k, Mats M, float cx, float y0, float w, float hh, bool sill)
        {
            k.C = new Color(0.95f, 0.93f, 0.88f);
            if (sill) k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.16f, y0 - 0.1f, -0.12f), new Vector3(cx + w * 0.5f + 0.16f, y0, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.13f, y0, -0.08f), new Vector3(cx - w * 0.5f, y0 + hh, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx + w * 0.5f, y0, -0.08f), new Vector3(cx + w * 0.5f + 0.13f, y0 + hh, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.18f, y0 + hh, -0.1f), new Vector3(cx + w * 0.5f + 0.18f, y0 + hh + 0.16f, 0.02f));
            k.C = Color.white;
        }

        /// <summary>Portadas abertas encostadas à fachada, dos dois lados do vão.</summary>
        static void OpenShuttersFlat(InteriorKit k, Mats M, Material mat, float cx, float y0, float w, float hh)
        {
            float sw = w * 0.5f;
            foreach (var s in new[] { -1f, 1f })
            {
                float a = cx + s * (w * 0.5f + 0.13f), b = a + s * sw;
                k.Box(mat, new Vector3(Mathf.Min(a, b), y0 + 0.03f, -0.07f), new Vector3(Mathf.Max(a, b), y0 + hh - 0.03f, -0.02f));
                k.C = new Color(0.82f, 0.82f, 0.82f);
                for (int i = 0; i < 4; i++)
                {
                    float y = y0 + 0.2f + i * (hh - 0.35f) / 3f;
                    k.Box(mat, new Vector3(Mathf.Min(a, b) + 0.02f, y, -0.095f), new Vector3(Mathf.Max(a, b) - 0.02f, y + 0.04f, -0.07f));
                }
                k.C = Iron;
                k.Box(M.pintado, new Vector3(a - s * 0.02f - 0.02f, y0 + 0.25f, -0.1f), new Vector3(a - s * 0.02f + 0.02f, y0 + 0.31f, -0.02f));
                k.Box(M.pintado, new Vector3(a - s * 0.02f - 0.02f, y0 + hh - 0.31f, -0.1f), new Vector3(a - s * 0.02f + 0.02f, y0 + hh - 0.25f, -0.02f));
                k.C = Color.white;
            }
        }

        /// <summary>Folha de porta almofadada (montantes, travessas, almofadas em relevo nas duas faces, dobradiças de
        /// ferro, puxador e espelho de fechadura). Referencial: dobradiça em (0,0,0), folha ao longo de +x (leftHinge) ou −x,
        /// espessura 0..0,06 em z; yaw roda a folha à volta da dobradiça.</summary>
        static void DoorLeaf(InteriorKit k, Mats M, Material mat, Vector3 hinge, float w, float hh, float yaw, bool leftHinge, string col)
        {
            const float t = 0.06f, st = 0.1f;
            k.Push(hinge, yaw);
            float x0 = leftHinge ? 0f : -w, x1 = leftHinge ? w : 0f;
            float a = x0 + st, b = x1 - st;
            // estrutura: montantes e travessas (inferior, do meio/fechadura, superior)
            float rb = Mathf.Min(0.24f, hh * 0.1f), rm0 = hh * 0.42f, rm1 = rm0 + 0.14f, rt = hh - Mathf.Min(0.14f, hh * 0.06f);
            k.C = Color.white;
            k.Box(mat, new Vector3(x0, 0f, 0f), new Vector3(a, hh, t));
            k.Box(mat, new Vector3(b, 0f, 0f), new Vector3(x1, hh, t));
            k.Box(mat, new Vector3(a, 0f, 0f), new Vector3(b, rb, t));
            k.Box(mat, new Vector3(a, rm0, 0f), new Vector3(b, rm1, t));
            k.Box(mat, new Vector3(a, rt, 0f), new Vector3(b, hh, t));
            if (b - a > 0.5f)
            {
                // folhas largas (igreja): montante a meio
                float m = (a + b) * 0.5f;
                k.Box(mat, new Vector3(m - 0.05f, rb, 0f), new Vector3(m + 0.05f, rt, t));
                Panels(k, mat, a, m - 0.05f, rb, rm0, rm1, rt, t);
                Panels(k, mat, m + 0.05f, b, rb, rm0, rm1, rt, t);
            }
            else Panels(k, mat, a, b, rb, rm0, rm1, rt, t);
            // dobradiças de ferro (do lado da dobradiça, nas duas faces)
            k.C = Iron;
            float hx0 = leftHinge ? x0 : x1 - 0.32f, hx1 = leftHinge ? x0 + 0.32f : x1;
            foreach (var y in new[] { 0.28f, hh * 0.5f, hh - 0.34f })
                foreach (var z in new[] { -0.006f, t })
                    k.Box(M.pintado, new Vector3(hx0, y, z), new Vector3(hx1, y + 0.05f, z + 0.006f));
            float px = leftHinge ? x0 : x1;
            k.Cyl(M.pintado, new Vector3(px, 0.2f, t * 0.5f), 0.018f, hh - 0.4f, 6);
            // puxador, espelho e argola (do lado livre)
            float fx = leftHinge ? x1 - st * 0.5f : x0 + st * 0.5f;
            float ly = (rm0 + rm1) * 0.5f;
            k.C = Brass;
            foreach (var z in new[] { -0.012f, t })
            {
                k.Box(M.pintado, new Vector3(fx - 0.025f, ly - 0.12f, z), new Vector3(fx + 0.025f, ly + 0.08f, z + 0.012f));
                float zz = z < 0f ? -0.045f : t + 0.045f;
                k.Rod(M.pintado, new Vector3(fx, ly, z < 0f ? z : z + 0.012f), new Vector3(fx, ly, zz), 0.01f, 5);
                k.Rod(M.pintado, new Vector3(fx, ly, zz), new Vector3(fx + (leftHinge ? -0.1f : 0.1f), ly, zz), 0.011f, 6);
            }
            k.C = new Color(0.08f, 0.08f, 0.08f);
            k.Box(M.pintado, new Vector3(fx - 0.006f, ly - 0.09f, -0.014f), new Vector3(fx + 0.006f, ly - 0.06f, -0.012f));
            k.C = Iron;
            k.Push(new Vector3((x0 + x1) * 0.5f, hh * 0.62f, -0.03f), 0f, 90f, 0f);
            k.Torus(M.pintado, Vector3.zero, 0.065f, 0.011f, 12, 4);
            k.Pop();
            k.C = Color.white;
            k.Col(col, new Vector3(x0, 0f, -0.02f), new Vector3(x1, hh, t + 0.02f));
            k.Pop();
        }

        /// <summary>Almofadas da folha: duas (em baixo e em cima) em relevo nas duas faces, com rebaixo à volta.</summary>
        static void Panels(InteriorKit k, Material mat, float a, float b, float rb, float rm0, float rm1, float rt, float t)
        {
            foreach (var (y0, y1) in new[] { (rb, rm0), (rm1, rt) })
            {
                // fundo rebaixado
                k.C = new Color(0.78f, 0.78f, 0.78f);
                k.Box(mat, new Vector3(a, y0, 0.012f), new Vector3(b, y1, t - 0.012f));
                // almofada em relevo (moldura de 2 degraus)
                float i1 = 0.03f, i2 = 0.06f;
                foreach (var (z0, z1, z2) in new[] { (0.012f, 0.004f, -0.002f), (t - 0.012f, t - 0.004f, t + 0.002f) })
                {
                    k.C = new Color(0.9f, 0.9f, 0.9f);
                    k.Box(mat, new Vector3(a + i1, y0 + i1, Mathf.Min(z0, z1)), new Vector3(b - i1, y1 - i1, Mathf.Max(z0, z1)));
                    k.C = Color.white;
                    k.Box(mat, new Vector3(a + i2, y0 + i2, Mathf.Min(z1, z2)), new Vector3(b - i2, y1 - i2, Mathf.Max(z1, z2)));
                }
            }
        }

        static void Runner(InteriorKit k, Mats M, float x0, float x1, float y, float z0, float z1, Color c, Color border)
        {
            k.C = c;
            k.Box(M.pintado, new Vector3(x0, y, z0), new Vector3(x1, y + 0.012f, z1));
            k.C = border;
            k.Box(M.pintado, new Vector3(x0 + 0.06f, y + 0.012f, z0 + 0.06f), new Vector3(x0 + 0.1f, y + 0.016f, z1 - 0.06f));
            k.Box(M.pintado, new Vector3(x1 - 0.1f, y + 0.012f, z0 + 0.06f), new Vector3(x1 - 0.06f, y + 0.016f, z1 - 0.06f));
            k.C = Color.white;
        }

        static void Column(InteriorKit k, Mats M, Vector3 b, float hh)
        {
            k.C = new Color(0.93f, 0.91f, 0.86f);
            k.BoxB(M.pedraClara, b, new Vector3(0.42f, 0.18f, 0.42f));
            k.Lathe(M.pedraClara, b + Vector3.up * 0.18f, new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.2f, 0.06f), new Vector2(0.17f, 0.12f), new Vector2(0.16f, 0.2f),
                new Vector2(0.14f, hh - 0.5f), new Vector2(0.15f, hh - 0.42f), new Vector2(0.15f, hh - 0.38f), new Vector2(0.2f, hh - 0.3f),
            }, 12);
            k.BoxB(M.pedraClara, b + Vector3.up * (hh - 0.12f), new Vector3(0.44f, 0.12f, 0.44f));
            k.C = Color.white;
        }

        static void Baluster(InteriorKit k, Mats M, Vector3 b, float hh)
        {
            float s = hh / 0.85f;
            var prof = new[]
            {
                new Vector2(0.035f, 0f), new Vector2(0.035f, 0.08f * s), new Vector2(0.022f, 0.14f * s), new Vector2(0.045f, 0.36f * s),
                new Vector2(0.02f, 0.62f * s), new Vector2(0.032f, 0.72f * s), new Vector2(0.032f, 0.85f * s),
            };
            k.Lathe(M.madeira, b, prof, 6);
        }

        static void Balustrade(InteriorKit k, Mats M, Vector3 a, Vector3 b, float hh, Color c)
        {
            k.C = c;
            var d = b - a;
            float len = d.magnitude;
            var dir = d / len;
            int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.22f));
            for (int i = 0; i <= n; i++)
            {
                var p = a + dir * (len * i / n);
                if (i % 12 == 0 || i == n)
                {
                    k.BoxB(M.madeira, p, new Vector3(0.13f, hh + 0.06f, 0.13f));          // prumo
                    k.BoxB(M.madeira, p + Vector3.up * (hh + 0.06f), new Vector3(0.17f, 0.04f, 0.17f));
                }
                else Baluster(k, M, p + Vector3.up * 0.08f, hh - 0.16f);
            }
            // corrimão por cima dos balaústres e rodapé por baixo (eixo z local = direção da balaustrada)
            k.Push(a, Quaternion.LookRotation(dir, Vector3.up), Vector3.one);
            k.Box(M.madeira, new Vector3(-0.08f, hh - 0.08f, 0f), new Vector3(0.08f, hh - 0.02f, len));
            k.Box(M.madeira, new Vector3(-0.06f, hh - 0.02f, 0f), new Vector3(0.06f, hh + 0.02f, len));
            k.Box(M.madeira, new Vector3(-0.05f, 0f, 0f), new Vector3(0.05f, 0.08f, len));
            k.Pop();
            k.C = Color.white;
        }

        static void Pew(InteriorKit k, Mats M, Vector3 p, float len, int seed)
        {
            k.Push(p, 0f);
            float hl = len * 0.5f;
            k.C = WoodDark;
            // assento e costas
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.42f, -0.2f), new Vector3(hl - 0.06f, 0.47f, 0.22f));
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.47f, -0.25f), new Vector3(hl - 0.06f, 0.92f, -0.21f));
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.9f, -0.28f), new Vector3(hl - 0.06f, 0.95f, -0.19f));
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.3f, 0.16f), new Vector3(hl - 0.06f, 0.42f, 0.2f));        // travessa
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.06f, -0.06f), new Vector3(hl - 0.06f, 0.1f, 0.0f));        // travessa baixa
            // ripas de livros nas costas (para o banco de trás) e genuflexório
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.66f, -0.36f), new Vector3(hl - 0.06f, 0.69f, -0.25f));
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.69f, -0.37f), new Vector3(hl - 0.06f, 0.75f, -0.35f));
            k.Box(M.madeira, new Vector3(-hl + 0.06f, 0.1f, -0.62f), new Vector3(hl - 0.06f, 0.17f, -0.48f));
            // ilhargas com remate redondo
            foreach (var s in new[] { -1f, 1f })
            {
                float x = s * (hl - 0.03f);
                k.Box(M.madeira, new Vector3(x - 0.03f, 0f, -0.3f), new Vector3(x + 0.03f, 0.88f, 0.26f));
                k.Push(new Vector3(x - 0.035f, 0.88f, -0.02f), 0f, 0f, -90f);
                k.Cyl(M.madeira, Vector3.zero, 0.09f, 0.07f, 10);
                k.Pop();
                k.Box(M.madeira, new Vector3(x - 0.035f, 0.82f, -0.3f), new Vector3(x + 0.035f, 0.9f, -0.02f));
                k.Box(M.madeira, new Vector3(x - 0.04f, 0f, -0.32f), new Vector3(x + 0.04f, 0.06f, 0.28f));
                // almofada (relevo) na ilharga
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(x + s * 0.03f - 0.006f, 0.18f, -0.2f), new Vector3(x + s * 0.03f + 0.006f, 0.7f, 0.16f));
                k.C = WoodDark;
            }
            // pés intermédios
            for (float x = -hl + 1.4f; x < hl - 1f; x += 1.4f)
                k.Box(M.madeira, new Vector3(x - 0.03f, 0f, -0.18f), new Vector3(x + 0.03f, 0.42f, 0.18f));
            k.C = Color.white;
            // livros de cânticos e um terço esquecido
            var rng = new System.Random(seed * 31 + 7);
            int nb = 2 + rng.Next(3);
            for (int i = 0; i < nb; i++)
            {
                float x = -hl + 0.4f + (float)rng.NextDouble() * (len - 0.8f);
                k.C = rng.Next(2) == 0 ? new Color(0.45f, 0.08f, 0.1f) : new Color(0.1f, 0.18f, 0.4f);
                k.Push(new Vector3(x, 0.47f, 0.0f), (float)rng.NextDouble() * 40f - 20f);
                k.Box(M.pintado, new Vector3(-0.07f, 0f, -0.1f), new Vector3(0.07f, 0.035f, 0.1f));
                k.C = new Color(0.95f, 0.92f, 0.82f);
                k.Box(M.pintado, new Vector3(-0.065f, 0.004f, -0.095f), new Vector3(0.071f, 0.031f, 0.095f));
                k.Pop();
            }
            for (int i = 0; i < 3; i++)
            {
                float x = -hl + 0.3f + i * (len - 0.6f) / 2f;
                k.C = new Color(0.1f, 0.18f, 0.4f);
                k.Box(M.pintado, new Vector3(x - 0.07f, 0.69f, -0.355f), new Vector3(x + 0.07f, 0.9f, -0.32f));
            }
            k.C = Color.white;
            k.Pop();
        }

        static void CommunionRail(InteriorKit k, Mats M)
        {
            foreach (var s in new[] { -1f, 1f })
            {
                float a = s * 0.95f, b = s * 4.75f;
                k.C = new Color(0.94f, 0.92f, 0.87f);
                k.Box(M.pedraClara, new Vector3(Mathf.Min(a, b), 0.03f, 8.2f), new Vector3(Mathf.Max(a, b), 0.15f, 8.38f));
                k.Box(M.pedraClara, new Vector3(Mathf.Min(a, b), 0.78f, 8.17f), new Vector3(Mathf.Max(a, b), 0.88f, 8.41f));
                for (float x = Mathf.Min(a, b) + 0.11f; x < Mathf.Max(a, b) - 0.05f; x += 0.22f)
                    k.Lathe(M.pedraClara, new Vector3(x, 0.15f, 8.29f), new[]
                    {
                        new Vector2(0.045f, 0f), new Vector2(0.03f, 0.08f), new Vector2(0.06f, 0.3f), new Vector2(0.026f, 0.5f), new Vector2(0.04f, 0.6f), new Vector2(0.04f, 0.63f),
                    }, 6);
                k.BoxB(M.pedraClara, new Vector3(a + s * 0.07f, 0.03f, 8.29f), new Vector3(0.18f, 0.95f, 0.26f));
                k.C = Color.white;
                k.Col("Teia", new Vector3(Mathf.Min(a, b), 0.03f, 8.17f), new Vector3(Mathf.Max(a, b), 0.95f, 8.41f));
                Blob(k, M, (a + b) * 0.5f, 0.03f, 8.3f, 4.2f, 0.6f);
            }
        }

        static void Altar(InteriorKit k, Mats M)
        {
            float zc = 10.8f, y0 = 0.33f;
            k.C = new Color(0.92f, 0.9f, 0.85f);
            k.Box(M.pedraClara, new Vector3(-1.25f, y0, zc - 0.42f), new Vector3(1.25f, y0 + 0.95f, zc + 0.42f));
            k.Box(M.pedraClara, new Vector3(-1.38f, y0 + 0.93f, zc - 0.5f), new Vector3(1.38f, y0 + 1.01f, zc + 0.5f));
            k.Box(M.pedraClara, new Vector3(-1.3f, y0, zc - 0.48f), new Vector3(1.3f, y0 + 0.08f, zc + 0.48f));
            // banqueta (degrau dos castiçais) atrás
            k.Box(M.pedraClara, new Vector3(-1.25f, y0 + 1.01f, zc + 0.18f), new Vector3(1.25f, y0 + 1.17f, zc + 0.5f));
            k.C = ClothWhite;
            // toalha: tampo + caída à frente e dos lados + renda
            k.Box(M.pintado, new Vector3(-1.42f, y0 + 1.01f, zc - 0.53f), new Vector3(1.42f, y0 + 1.025f, zc + 0.18f));
            k.Box(M.pintado, new Vector3(-1.42f, y0 + 0.62f, zc - 0.535f), new Vector3(1.42f, y0 + 1.025f, zc - 0.52f));
            foreach (var s in new[] { -1f, 1f })
                k.Box(M.pintado, new Vector3(s * 1.405f - 0.008f, y0 + 0.5f, zc - 0.52f), new Vector3(s * 1.405f + 0.008f, y0 + 1.025f, zc + 0.18f));
            k.C = new Color(0.93f, 0.88f, 0.72f);
            for (float x = -1.36f; x < 1.38f; x += 0.08f)
                k.Box(M.pintado, new Vector3(x, y0 + 0.57f, zc - 0.54f), new Vector3(x + 0.05f, y0 + 0.63f, zc - 0.532f));
            // frontal vermelho com cruz dourada
            k.C = Red;
            k.Box(M.pintado, new Vector3(-0.32f, y0 + 0.08f, zc - 0.45f), new Vector3(0.32f, y0 + 0.62f, zc - 0.43f));
            k.C = Gold;
            k.Box(M.pintado, new Vector3(-0.03f, y0 + 0.2f, zc - 0.46f), new Vector3(0.03f, y0 + 0.55f, zc - 0.45f));
            k.Box(M.pintado, new Vector3(-0.12f, y0 + 0.42f, zc - 0.46f), new Vector3(0.12f, y0 + 0.47f, zc - 0.45f));
            // sacrário dourado
            k.Box(M.pintado, new Vector3(-0.22f, y0 + 1.17f, zc + 0.2f), new Vector3(0.22f, y0 + 1.6f, zc + 0.48f));
            k.Lathe(M.pintado, new Vector3(0f, y0 + 1.6f, zc + 0.34f), new[] { new Vector2(0.2f, 0f), new Vector2(0.18f, 0.06f), new Vector2(0.12f, 0.15f), new Vector2(0.03f, 0.22f), new Vector2(0f, 0.24f) }, 10);
            k.Box(M.pintado, new Vector3(-0.012f, y0 + 1.84f, zc + 0.33f), new Vector3(0.012f, y0 + 1.98f, zc + 0.35f));
            k.Box(M.pintado, new Vector3(-0.045f, y0 + 1.92f, zc + 0.33f), new Vector3(0.045f, y0 + 1.945f, zc + 0.35f));
            k.C = new Color(0.62f, 0.45f, 0.15f);
            k.Box(M.pintado, new Vector3(-0.14f, y0 + 1.22f, zc + 0.19f), new Vector3(0.14f, y0 + 1.53f, zc + 0.2f));
            // corporal, missal na estante e cálice
            k.C = ClothWhite;
            k.Box(M.pintado, new Vector3(-0.25f, y0 + 1.025f, zc - 0.3f), new Vector3(0.25f, y0 + 1.03f, zc + 0.05f));
            k.C = Gold;
            k.Lathe(M.pintado, new Vector3(0.05f, y0 + 1.03f, zc - 0.12f), new[] { new Vector2(0.05f, 0f), new Vector2(0.012f, 0.03f), new Vector2(0.01f, 0.1f), new Vector2(0.04f, 0.14f), new Vector2(0.045f, 0.2f) }, 8, true, true, false);
            k.Push(new Vector3(-0.6f, y0 + 1.03f, zc - 0.15f), 15f);
            k.C = Brass;
            k.Box(M.pintado, new Vector3(-0.17f, 0f, -0.12f), new Vector3(0.17f, 0.05f, 0.12f));
            k.Push(new Vector3(0f, 0.1f, 0f), 0f, -25f, 0f);
            k.C = Red;
            k.Box(M.pintado, new Vector3(-0.16f, -0.02f, -0.13f), new Vector3(0.16f, 0.015f, 0.13f));
            k.C = new Color(0.97f, 0.95f, 0.87f);
            k.Box(M.pintado, new Vector3(-0.15f, 0.015f, -0.12f), new Vector3(0.15f, 0.035f, 0.12f));
            k.Pop();
            k.Pop();
            k.C = Color.white;
            // castiçais da banqueta
            float[] xs = { -1.05f, -0.75f, -0.45f, 0.45f, 0.75f, 1.05f };
            for (int i = 0; i < xs.Length; i++)
            {
                float hh = 0.3f + Mathf.Abs(xs[i]) * 0.18f;
                Candlestick(k, M, new Vector3(xs[i], y0 + 1.17f, zc + 0.34f), hh, false);
            }
            k.Col("Altar", new Vector3(-1.38f, y0, zc - 0.53f), new Vector3(1.38f, y0 + 1.2f, zc + 0.5f));
            Blob(k, M, 0f, y0, zc, 3.2f, 1.4f);
        }

        static void Candlestick(InteriorKit k, Mats M, Vector3 b, float hh, bool floor)
        {
            k.C = floor ? Brass : Gold;
            float r = floor ? 0.18f : 0.07f;
            k.Lathe(M.pintado, b, new[]
            {
                new Vector2(r, 0f), new Vector2(r * 0.9f, hh * 0.04f), new Vector2(r * 0.35f, hh * 0.12f), new Vector2(r * 0.2f, hh * 0.2f),
                new Vector2(r * 0.32f, hh * 0.3f), new Vector2(r * 0.16f, hh * 0.38f), new Vector2(r * 0.14f, hh * 0.8f), new Vector2(r * 0.3f, hh * 0.86f),
                new Vector2(r * 0.55f, hh * 0.95f), new Vector2(r * 0.5f, hh),
            }, 8);
            k.C = new Color(0.98f, 0.96f, 0.9f);
            float cr = floor ? 0.04f : 0.022f, ch = floor ? 0.45f : 0.24f;
            k.Cyl(M.pintado, b + Vector3.up * hh, cr, ch, 8);
            k.C = Color.white;
            k.Ellipsoid(M.chama, b + Vector3.up * (hh + ch + 0.03f), new Vector3(0.014f, 0.035f, 0.014f) * (floor ? 1.4f : 1f), 6, 4);
            if (floor) Blob(k, M, b.x, b.y, b.z, 0.6f, 0.6f);
        }

        static void Vase(InteriorKit k, Mats M, Vector3 b, bool white)
        {
            k.C = new Color(0.85f, 0.85f, 0.88f);
            k.Lathe(M.pintado, b, new[] { new Vector2(0.12f, 0f), new Vector2(0.16f, 0.2f), new Vector2(0.1f, 0.42f), new Vector2(0.13f, 0.5f) }, 10, true, true, false);
            var rng = new System.Random(white ? 3 : 11);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 0.7f, rr = 0.06f + 0.1f * (float)rng.NextDouble();
                var p = b + new Vector3(Mathf.Cos(a) * rr, 0.58f + 0.25f * (float)rng.NextDouble(), Mathf.Sin(a) * rr);
                k.C = new Color(0.25f, 0.5f, 0.2f);
                k.Rod(M.pintado, b + Vector3.up * 0.45f, p, 0.008f, 3, false);
                k.Ellipsoid(M.pintado, p + new Vector3(0.04f, -0.06f, 0f), new Vector3(0.06f, 0.015f, 0.03f), 5, 3);
                k.C = white ? (i % 3 == 0 ? new Color(0.98f, 0.9f, 0.3f) : new Color(0.98f, 0.98f, 0.95f)) : (i % 2 == 0 ? new Color(0.9f, 0.2f, 0.25f) : new Color(0.98f, 0.6f, 0.7f));
                k.Sphere(M.pintado, p, 0.045f, 6, 4);
            }
            k.C = Color.white;
        }

        static void BlueArch(InteriorKit k, Mats M)
        {
            float z1 = ChurchDepth - T, y0 = 0.33f, spring = 3.9f, rIn = 2.15f, rOut = 2.5f;
            // fundo azul-claro dentro do arco
            k.C = new Color(0.62f, 0.78f, 0.94f);
            k.ArchPanel(M.pintado, 0f, y0, rIn, spring, z1 - 0.03f, z1, 16);
            // pilastras e arco azuis
            k.C = Blue;
            foreach (var s in new[] { -1f, 1f })
            {
                k.Box(M.pintado, new Vector3(s * rIn, y0, z1 - 0.16f), new Vector3(s * rOut, spring, z1));
                k.Box(M.pintado, new Vector3(s * (rIn - 0.04f) - 0.0f, spring - 0.12f, z1 - 0.2f), new Vector3(s * (rOut + 0.05f), spring, z1));
                k.Box(M.pintado, new Vector3(s * (rIn - 0.04f), y0, z1 - 0.2f), new Vector3(s * (rOut + 0.05f), y0 + 0.25f, z1));
            }
            k.ArchRing(M.pintado, new Vector2(0f, spring), rIn, rOut, 0f, 180f, 20, z1 - 0.16f, z1);
            k.C = Gold;
            k.ArchRing(M.pintado, new Vector2(0f, spring), rIn - 0.03f, rIn, 0f, 180f, 20, z1 - 0.17f, z1);
            k.BoxC(M.pintado, new Vector3(0f, spring + rOut - 0.1f, z1 - 0.12f), new Vector3(0.36f, 0.42f, 0.1f));    // fecho
            // estrelas douradas no fundo
            var rng = new System.Random(5);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI, rr = 0.6f + (float)rng.NextDouble() * 1.3f;
                var p = new Vector3(Mathf.Cos(a) * rr, spring + Mathf.Sin(a) * rr * 0.9f, z1 - 0.035f);
                if (Mathf.Abs(p.x) < 0.75f) continue;
                k.BoxC(M.pintado, p, new Vector3(0.06f, 0.06f, 0.01f));
            }
            k.C = Color.white;
        }

        static void Crucifix(InteriorKit k, Mats M, Vector3 b)
        {
            float z = b.z;
            k.C = new Color(0.35f, 0.22f, 0.14f);
            k.Box(M.madeira, new Vector3(-0.07f, b.y, z - 0.08f), new Vector3(0.07f, b.y + 2.5f, z));
            k.Box(M.madeira, new Vector3(-0.62f, b.y + 1.7f, z - 0.08f), new Vector3(0.62f, b.y + 1.84f, z));
            k.C = ClothWhite;
            k.Box(M.pintado, new Vector3(-0.12f, b.y + 2.08f, z - 0.1f), new Vector3(0.12f, b.y + 2.2f, z - 0.08f));
            k.C = Skin;
            float zc = z - 0.16f;
            k.Ellipsoid(M.pintado, new Vector3(0f, b.y + 1.42f, zc), new Vector3(0.12f, 0.22f, 0.075f), 8, 5);
            k.Sphere(M.pintado, new Vector3(0.015f, b.y + 1.74f, zc - 0.01f), 0.075f, 8, 5);
            k.Rod(M.pintado, new Vector3(-0.09f, b.y + 1.6f, zc), new Vector3(-0.56f, b.y + 1.78f, zc + 0.02f), 0.03f, 6);
            k.Rod(M.pintado, new Vector3(0.09f, b.y + 1.6f, zc), new Vector3(0.56f, b.y + 1.78f, zc + 0.02f), 0.03f, 6);
            k.Rod(M.pintado, new Vector3(-0.05f, b.y + 1.22f, zc), new Vector3(0.0f, b.y + 0.55f, zc + 0.02f), 0.038f, 6);
            k.Rod(M.pintado, new Vector3(0.05f, b.y + 1.22f, zc), new Vector3(0.01f, b.y + 0.55f, zc + 0.02f), 0.036f, 6);
            k.C = new Color(0.38f, 0.25f, 0.16f);
            k.Ellipsoid(M.pintado, new Vector3(0.01f, b.y + 1.77f, zc + 0.01f), new Vector3(0.08f, 0.07f, 0.075f), 8, 4);
            k.C = ClothWhite;
            k.Ellipsoid(M.pintado, new Vector3(0f, b.y + 1.22f, zc), new Vector3(0.13f, 0.08f, 0.09f), 8, 4);
            k.C = new Color(0.45f, 0.4f, 0.2f);
            k.Push(new Vector3(0.015f, b.y + 1.8f, zc), 0f, 0f, 0f);
            k.Torus(M.pintado, Vector3.zero, 0.075f, 0.012f, 10, 4);
            k.Pop();
            k.C = Color.white;
        }

        static readonly Color[,] StatueColors =
        {
            { new Color(0.97f, 0.96f, 0.93f), new Color(0.25f, 0.45f, 0.85f) },   // Nossa Senhora: túnica branca, manto azul
            { new Color(0.55f, 0.18f, 0.18f), new Color(0.5f, 0.38f, 0.28f) },    // Sagrado Coração
            { new Color(0.45f, 0.3f, 0.2f), new Color(0.38f, 0.25f, 0.16f) },     // S. Francisco (hábito castanho)
            { new Color(0.85f, 0.75f, 0.45f), new Color(0.7f, 0.15f, 0.15f) },    // Santo António / S. Tiago
        };

        static void Niche(InteriorKit k, Mats M, Vector3 b, int kind)
        {
            float z1 = b.z, half = 0.5f, y0 = b.y, hh = 1.35f;
            k.C = new Color(0.12f, 0.24f, 0.5f);
            k.ArchPanel(M.pintado, b.x, y0, half - 0.05f, y0 + hh - half, z1 - 0.02f, z1, 10);
            k.C = new Color(0.94f, 0.92f, 0.87f);
            k.Box(M.pedraClara, new Vector3(b.x - half - 0.1f, y0, z1 - 0.18f), new Vector3(b.x - half + 0.02f, y0 + hh - half, z1));
            k.Box(M.pedraClara, new Vector3(b.x + half - 0.02f, y0, z1 - 0.18f), new Vector3(b.x + half + 0.1f, y0 + hh - half, z1));
            k.ArchRing(M.pedraClara, new Vector2(b.x, y0 + hh - half), half - 0.02f, half + 0.1f, 0f, 180f, 10, z1 - 0.18f, z1);
            // prateleira com mísula
            k.Box(M.pedraClara, new Vector3(b.x - half - 0.12f, y0 - 0.1f, z1 - 0.34f), new Vector3(b.x + half + 0.12f, y0, z1));
            k.Lathe(M.pedraClara, new Vector3(b.x, y0 - 0.55f, z1 - 0.12f), new[] { new Vector2(0.02f, 0f), new Vector2(0.1f, 0.25f), new Vector2(0.22f, 0.45f), new Vector2(0.25f, 0.45f) }, 8, true, false, true);
            k.C = Color.white;
            Statue(k, M, new Vector3(b.x, y0, z1 - 0.19f), 0.95f, kind);
        }

        static void Statue(InteriorKit k, Mats M, Vector3 b, float hh, int kind)
        {
            var robe = StatueColors[kind, 0];
            var mantle = StatueColors[kind, 1];
            float s = hh / 1.0f;
            k.C = new Color(0.4f, 0.55f, 0.35f);
            k.Cyl(M.pintado, b, 0.14f * s, 0.06f * s, 10);      // peanha
            k.C = robe;
            k.Lathe(M.pintado, b + Vector3.up * 0.06f * s, new[]
            {
                new Vector2(0.13f * s, 0f), new Vector2(0.12f * s, 0.25f * s), new Vector2(0.1f * s, 0.5f * s), new Vector2(0.11f * s, 0.62f * s),
                new Vector2(0.09f * s, 0.72f * s), new Vector2(0.05f * s, 0.78f * s),
            }, 10);
            k.C = mantle;
            k.Lathe(M.pintado, b + new Vector3(0f, 0.12f * s, 0.015f * s), new[]
            {
                new Vector2(0.15f * s, 0f), new Vector2(0.135f * s, 0.3f * s), new Vector2(0.12f * s, 0.6f * s), new Vector2(0.09f * s, 0.7f * s), new Vector2(0.06f * s, 0.74f * s),
            }, 10, true, false, false);
            k.C = Skin;
            k.Sphere(M.pintado, b + new Vector3(0f, 0.9f * s, 0f), 0.075f * s, 8, 5);
            k.Rod(M.pintado, b + new Vector3(-0.06f * s, 0.6f * s, -0.08f * s), b + new Vector3(0.0f, 0.55f * s, -0.13f * s), 0.025f * s, 5);
            k.Rod(M.pintado, b + new Vector3(0.06f * s, 0.6f * s, -0.08f * s), b + new Vector3(0.0f, 0.57f * s, -0.13f * s), 0.025f * s, 5);
            if (kind == 0)
            {
                k.C = mantle;   // véu
                k.Ellipsoid(M.pintado, b + new Vector3(0f, 0.92f * s, 0.02f * s), new Vector3(0.09f, 0.1f, 0.09f) * s, 8, 4);
                k.C = Skin;
                k.Sphere(M.pintado, b + new Vector3(0f, 0.9f * s, -0.01f * s), 0.072f * s, 8, 5);
            }
            else if (kind == 1)
            {
                k.C = new Color(0.9f, 0.1f, 0.1f);
                k.Sphere(M.pintado, b + new Vector3(0f, 0.68f * s, -0.1f * s), 0.035f * s, 6, 4);
            }
            k.C = new Color(0.3f, 0.2f, 0.12f);
            if (kind != 0) k.Ellipsoid(M.pintado, b + new Vector3(0f, 0.93f * s, 0.01f * s), new Vector3(0.08f, 0.06f, 0.08f) * s, 8, 4);
            k.C = Gold;
            k.Push(b + new Vector3(0f, 0.95f * s, 0.07f * s), 0f, 90f, 0f);
            k.Torus(M.pintado, Vector3.zero, 0.11f * s, 0.01f * s, 14, 4);
            k.Pop();
            k.C = Color.white;
        }

        static void Banner(InteriorKit k, Mats M, Vector3 top, Color c)
        {
            float w = 0.75f, hh = 2.7f;
            k.C = Gold;
            k.Rod(M.pintado, top + new Vector3(-w * 0.5f - 0.08f, 0.05f, -0.06f), top + new Vector3(w * 0.5f + 0.08f, 0.05f, -0.06f), 0.02f, 6);
            k.Sphere(M.pintado, top + new Vector3(-w * 0.5f - 0.1f, 0.05f, -0.06f), 0.035f, 6, 4);
            k.Sphere(M.pintado, top + new Vector3(w * 0.5f + 0.1f, 0.05f, -0.06f), 0.035f, 6, 4);
            k.Rod(M.pintado, top + new Vector3(-0.2f, 0.05f, -0.06f), top + new Vector3(0f, 0.45f, -0.03f), 0.006f, 3, false);
            k.Rod(M.pintado, top + new Vector3(0.2f, 0.05f, -0.06f), top + new Vector3(0f, 0.45f, -0.03f), 0.006f, 3, false);
            k.C = c;
            var poly = new[]
            {
                new Vector2(top.x - w * 0.5f, top.y), new Vector2(top.x + w * 0.5f, top.y), new Vector2(top.x + w * 0.5f, top.y - hh + 0.3f),
                new Vector2(top.x, top.y - hh), new Vector2(top.x - w * 0.5f, top.y - hh + 0.3f),
            };
            k.Push(new Vector3(0f, 0f, top.z - 0.07f), 0f);
            k.Prism(M.pintado, poly, 0f, 0.02f);
            k.Pop();
            k.C = Gold;
            k.Box(M.pintado, new Vector3(top.x - w * 0.5f, top.y - 0.12f, top.z - 0.075f), new Vector3(top.x + w * 0.5f, top.y - 0.06f, top.z - 0.065f));
            k.Box(M.pintado, new Vector3(top.x - w * 0.5f + 0.06f, top.y - hh + 0.42f, top.z - 0.075f), new Vector3(top.x + w * 0.5f - 0.06f, top.y - hh + 0.46f, top.z - 0.065f));
            // emblema: cruz dentro de um círculo
            var e = new Vector3(top.x, top.y - 1.1f, top.z - 0.075f);
            k.Push(e, 0f, 90f, 0f);
            k.Torus(M.pintado, Vector3.zero, 0.2f, 0.018f, 18, 4);
            k.Pop();
            k.Box(M.pintado, e + new Vector3(-0.025f, -0.17f, -0.01f), e + new Vector3(0.025f, 0.17f, 0.005f));
            k.Box(M.pintado, e + new Vector3(-0.11f, 0.02f, -0.01f), e + new Vector3(0.11f, 0.07f, 0.005f));
            // franja
            for (float x = -w * 0.5f; x < w * 0.5f; x += 0.05f)
            {
                float yb = top.y - hh + 0.3f * (1f - Mathf.Abs(x + 0.025f) / (w * 0.5f));
                k.Box(M.pintado, new Vector3(top.x + x, yb - 0.08f, top.z - 0.07f), new Vector3(top.x + x + 0.02f, yb, top.z - 0.05f));
            }
            k.C = Color.white;
        }

        static void WallFan(InteriorKit k, Mats M, Vector3 p, float side)
        {
            // side: +1/−1 → parede lateral (normal para −x/+x local); 0 → parede do fundo (normal −z)
            float yaw = side > 0 ? -90f : side < 0 ? 90f : 180f;
            k.Push(p, yaw);
            // referencial: +z = para dentro da sala
            k.C = new Color(0.9f, 0.9f, 0.88f);
            k.Box(M.pintado, new Vector3(-0.08f, -0.12f, 0f), new Vector3(0.08f, 0.12f, 0.04f));
            k.Rod(M.pintado, new Vector3(0f, 0f, 0.02f), new Vector3(0f, 0f, 0.22f), 0.025f, 6);
            k.Push(new Vector3(0f, 0f, 0.22f), 0f, 22f, 0f);  // inclinada para baixo
            k.Push(Vector3.zero, 0f, -90f, 0f);              // eixo do motor ao longo de +z
            k.Lathe(M.pintado, new Vector3(0f, -0.18f, 0f), new[] { new Vector2(0.03f, 0f), new Vector2(0.08f, 0.04f), new Vector2(0.085f, 0.16f), new Vector2(0.05f, 0.22f) }, 10);
            k.C = new Color(0.7f, 0.7f, 0.72f);
            k.Torus(M.pintado, new Vector3(0f, 0.12f, 0f), 0.24f, 0.008f, 18, 3);
            k.Torus(M.pintado, new Vector3(0f, 0.2f, 0f), 0.26f, 0.008f, 18, 3);
            k.Torus(M.pintado, new Vector3(0f, 0.28f, 0f), 0.16f, 0.008f, 14, 3);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                k.Rod(M.pintado, d * 0.24f + Vector3.up * 0.12f, d * 0.26f + Vector3.up * 0.2f, 0.005f, 3, false);
                k.Rod(M.pintado, d * 0.26f + Vector3.up * 0.2f, d * 0.12f + Vector3.up * 0.29f, 0.005f, 3, false);
            }
            k.C = new Color(0.25f, 0.55f, 0.85f);
            for (int i = 0; i < 3; i++)
            {
                k.Push(new Vector3(0f, 0.2f, 0f), i * 120f, 0f, 14f);
                k.Ellipsoid(M.pintado, new Vector3(0.12f, 0f, 0f), new Vector3(0.11f, 0.01f, 0.06f), 8, 3);
                k.Pop();
            }
            k.C = new Color(0.9f, 0.9f, 0.88f);
            k.Sphere(M.pintado, new Vector3(0f, 0.21f, 0f), 0.03f, 6, 4);
            k.Pop();
            k.Pop();
            k.C = Color.white;
            k.Pop();
        }

        static void StationPlaque(InteriorKit k, Mats M, Vector3 p, float side)
        {
            k.Push(p, side > 0 ? -90f : 90f);
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.22f, -0.28f, 0f), new Vector3(0.22f, 0.28f, 0.04f));
            k.C = new Color(0.85f, 0.8f, 0.68f);
            k.Box(M.pintado, new Vector3(-0.17f, -0.22f, 0.04f), new Vector3(0.17f, 0.2f, 0.045f));
            k.C = new Color(0.45f, 0.35f, 0.3f);
            k.Box(M.pintado, new Vector3(-0.1f, -0.15f, 0.045f), new Vector3(0.08f, 0.1f, 0.05f));
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.025f, 0.28f, 0f), new Vector3(0.025f, 0.42f, 0.03f));
            k.Box(M.madeira, new Vector3(-0.07f, 0.33f, 0f), new Vector3(0.07f, 0.37f, 0.03f));
            k.C = Color.white;
            k.Pop();
        }

        static void Sconce(InteriorKit k, Mats M, Vector3 p, float side)
        {
            k.Push(p, side > 0 ? -90f : 90f);
            k.C = Brass;
            k.Box(M.pintado, new Vector3(-0.06f, -0.1f, 0f), new Vector3(0.06f, 0.1f, 0.02f));
            k.Rod(M.pintado, new Vector3(0f, 0f, 0.02f), new Vector3(0f, 0.08f, 0.2f), 0.012f, 5);
            k.Cyl(M.pintado, new Vector3(0f, 0.06f, 0.2f), 0.045f, 0.02f, 8);
            k.C = Color.white;
            k.Lathe(M.vidroLuz, new Vector3(0f, 0.08f, 0.2f), new[] { new Vector2(0.03f, 0f), new Vector2(0.06f, 0.08f), new Vector2(0.05f, 0.16f), new Vector2(0.035f, 0.18f) }, 8);
            k.Pop();
        }

        static void Lectern(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = WoodDark;
            k.BoxB(M.madeira, Vector3.zero, new Vector3(0.6f, 0.06f, 0.5f));
            k.BoxB(M.madeira, new Vector3(0f, 0.06f, 0f), new Vector3(0.18f, 0.9f, 0.18f));
            k.Push(new Vector3(0f, 1.0f, 0f), 0f, -25f, 0f);
            k.Box(M.madeira, new Vector3(-0.32f, -0.05f, -0.24f), new Vector3(0.32f, 0.02f, 0.24f));
            k.C = new Color(0.15f, 0.45f, 0.25f);
            k.Box(M.pintado, new Vector3(-0.2f, -0.3f, -0.26f), new Vector3(0.2f, 0.02f, -0.245f));
            k.C = Gold;
            k.Box(M.pintado, new Vector3(-0.025f, -0.24f, -0.265f), new Vector3(0.025f, -0.06f, -0.258f));
            k.Box(M.pintado, new Vector3(-0.07f, -0.13f, -0.265f), new Vector3(0.07f, -0.1f, -0.258f));
            k.C = new Color(0.97f, 0.95f, 0.87f);
            k.Box(M.pintado, new Vector3(-0.22f, 0.02f, -0.15f), new Vector3(0f, 0.045f, 0.15f));
            k.Box(M.pintado, new Vector3(0f, 0.02f, -0.15f), new Vector3(0.22f, 0.05f, 0.15f));
            k.C = Red;
            k.Box(M.pintado, new Vector3(-0.005f, -0.1f, -0.16f), new Vector3(0.005f, 0.05f, -0.14f));
            k.Pop();
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 0.9f, 0.8f);
        }

        static void PresiderChair(InteriorKit k, Mats M, Vector3 b)
        {
            k.Push(b, 0f);
            k.C = WoodDark;
            foreach (var x in new[] { -0.3f, 0.3f })
                foreach (var z in new[] { -0.25f, 0.25f })
                    k.BoxB(M.madeira, new Vector3(x, 0f, z), new Vector3(0.07f, z > 0 ? 1.45f : 0.68f, 0.07f));
            k.Box(M.madeira, new Vector3(-0.34f, 0.42f, -0.29f), new Vector3(0.34f, 0.48f, 0.29f));
            k.Box(M.madeira, new Vector3(-0.34f, 0.62f, -0.3f), new Vector3(-0.26f, 0.68f, 0.29f));
            k.Box(M.madeira, new Vector3(0.26f, 0.62f, -0.3f), new Vector3(0.34f, 0.68f, 0.29f));
            k.Box(M.madeira, new Vector3(-0.3f, 1.3f, 0.22f), new Vector3(0.3f, 1.48f, 0.29f));
            k.C = Red;
            k.Box(M.pintado, new Vector3(-0.29f, 0.48f, -0.27f), new Vector3(0.29f, 0.55f, 0.24f));
            k.Box(M.pintado, new Vector3(-0.26f, 0.58f, 0.2f), new Vector3(0.26f, 1.28f, 0.25f));
            k.C = Gold;
            k.Sphere(M.pintado, new Vector3(-0.3f, 1.5f, 0.25f), 0.04f, 6, 4);
            k.Sphere(M.pintado, new Vector3(0.3f, 1.5f, 0.25f), 0.04f, 6, 4);
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 0.9f, 0.9f);
        }

        static void Chandelier(InteriorKit k, Mats M, Vector3 hang, float drop)
        {
            k.C = Iron;
            var bot = hang - Vector3.up * drop;
            for (float y = 0f; y < drop - 0.15f; y += 0.09f)
            {
                k.Push(hang - Vector3.up * (y + 0.045f), (y * 100f) % 2 < 1 ? 0f : 90f, 0f, 90f);
                k.Torus(M.pintado, Vector3.zero, 0.035f, 0.007f, 8, 3);
                k.Pop();
            }
            k.Lathe(M.pintado, bot, new[] { new Vector2(0f, -0.25f), new Vector2(0.06f, -0.2f), new Vector2(0.04f, -0.05f), new Vector2(0.09f, 0f), new Vector2(0.05f, 0.12f), new Vector2(0.015f, 0.18f) }, 8);
            k.Torus(M.pintado, bot + Vector3.up * -0.05f, 0.6f, 0.02f, 24, 4);
            k.Torus(M.pintado, bot + Vector3.up * 0.15f, 0.3f, 0.015f, 18, 4);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                k.C = Iron;
                k.Rod(M.pintado, bot + Vector3.up * 0.1f, bot + d * 0.6f - Vector3.up * 0.05f, 0.012f, 4, false);
                k.Rod(M.pintado, bot + Vector3.up * 0.15f + d * 0.3f, bot + d * 0.6f, 0.01f, 4, false);
                k.Cyl(M.pintado, bot + d * 0.6f - Vector3.up * 0.03f, 0.04f, 0.03f, 8);
                k.C = new Color(0.98f, 0.96f, 0.9f);
                k.Cyl(M.pintado, bot + d * 0.6f, 0.018f, 0.16f, 6);
                k.C = Color.white;
                k.Ellipsoid(M.chama, bot + d * 0.6f + Vector3.up * 0.185f, new Vector3(0.012f, 0.03f, 0.012f), 5, 3);
            }
            k.C = Color.white;
        }

        static void SanctuaryLamp(InteriorKit k, Mats M, Vector3 hang, float drop)
        {
            var bot = hang - Vector3.up * drop;
            k.C = Brass;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                k.Rod(M.pintado, hang, bot + new Vector3(Mathf.Cos(a) * 0.12f, 0.12f, Mathf.Sin(a) * 0.12f), 0.004f, 3, false);
            }
            k.Lathe(M.pintado, bot, new[] { new Vector2(0f, -0.12f), new Vector2(0.04f, -0.08f), new Vector2(0.12f, 0.02f), new Vector2(0.13f, 0.12f) }, 10, true, false, false);
            k.C = Color.white;
            k.Lathe(M.lumeVermelho, bot + Vector3.up * 0.03f, new[] { new Vector2(0.05f, 0f), new Vector2(0.07f, 0.06f), new Vector2(0.065f, 0.14f) }, 8);
        }

        static void Harmonium(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            // referencial: costas na parede (z = 0), teclado para +z
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.65f, 0f, 0f), new Vector3(0.65f, 0.78f, 0.52f));
            k.Box(M.madeira, new Vector3(-0.68f, 0.76f, -0.02f), new Vector3(0.68f, 0.82f, 0.56f));
            k.Box(M.madeira, new Vector3(-0.65f, 0.82f, 0f), new Vector3(0.65f, 1.05f, 0.24f));
            k.Box(M.madeira, new Vector3(-0.65f, 1.05f, 0f), new Vector3(0.65f, 1.6f, 0.12f));
            k.Box(M.madeira, new Vector3(-0.7f, 1.6f, -0.02f), new Vector3(0.7f, 1.68f, 0.16f));
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-0.5f, 0.12f, 0.52f), new Vector3(0.5f, 0.6f, 0.53f));
            // tubos decorativos
            k.C = Gold;
            for (int i = 0; i < 11; i++)
            {
                float x = -0.5f + i * 0.1f;
                float hh = 0.3f + 0.25f * (1f - Mathf.Abs(i - 5) / 5f);
                k.Cyl(M.pintado, new Vector3(x, 1.12f, 0.15f), 0.025f, hh, 6);
            }
            // teclado
            k.C = new Color(0.97f, 0.96f, 0.9f);
            k.Box(M.pintado, new Vector3(-0.5f, 0.8f, 0.26f), new Vector3(0.5f, 0.84f, 0.42f));
            k.C = new Color(0.1f, 0.1f, 0.1f);
            for (float x = -0.47f; x < 0.48f; x += 0.045f)
                if (((int)((x + 0.5f) / 0.045f)) % 7 != 2 && ((int)((x + 0.5f) / 0.045f)) % 7 != 6)
                    k.Box(M.pintado, new Vector3(x, 0.84f, 0.3f), new Vector3(x + 0.018f, 0.86f, 0.36f));
            // pedais e partitura
            k.C = Iron;
            k.Box(M.pintado, new Vector3(-0.35f, 0.04f, 0.5f), new Vector3(-0.08f, 0.08f, 0.72f));
            k.Box(M.pintado, new Vector3(0.08f, 0.04f, 0.5f), new Vector3(0.35f, 0.08f, 0.72f));
            k.C = new Color(0.97f, 0.95f, 0.88f);
            k.Push(new Vector3(0f, 1.05f, 0.24f), 0f, -15f, 0f);
            k.Box(M.pintado, new Vector3(-0.22f, 0f, -0.01f), new Vector3(0.22f, 0.28f, 0f));
            k.Pop();
            k.C = Color.white;
            k.Col("Harmonio", new Vector3(-0.7f, 0f, 0f), new Vector3(0.7f, 1.7f, 0.56f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z + 0.3f, 1.6f, 0.9f, yaw);
        }

        static void Stool(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = WoodMid;
            k.Cyl(M.madeira, b + Vector3.up * 0.45f, 0.17f, 0.05f, 12);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                k.Rod(M.madeira, b + Vector3.up * 0.45f + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.1f, b + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.18f, 0.02f, 5);
            }
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.55f, 0.55f);
        }

        static void Bench(InteriorKit k, Mats M, Vector3 b, float len, float yaw, Color c)
        {
            k.Push(b, yaw);
            k.C = c;
            k.Box(M.madeira, new Vector3(-len * 0.5f, 0.42f, 0f), new Vector3(len * 0.5f, 0.47f, 0.36f));
            foreach (var s in new[] { -1f, 1f })
                k.Box(M.madeira, new Vector3(s * (len * 0.5f - 0.1f) - 0.03f, 0f, 0.03f), new Vector3(s * (len * 0.5f - 0.1f) + 0.03f, 0.42f, 0.33f));
            k.Box(M.madeira, new Vector3(-len * 0.5f + 0.1f, 0.15f, 0.16f), new Vector3(len * 0.5f - 0.1f, 0.2f, 0.2f));
            k.C = Color.white;
            k.Col("Banco", new Vector3(-len * 0.5f, 0f, 0f), new Vector3(len * 0.5f, 0.47f, 0.36f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z + 0.18f, len + 0.2f, 0.6f, yaw);
        }

        static void MusicStand(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = Iron;
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                k.Rod(M.pintado, new Vector3(0f, 0.25f, 0f), new Vector3(Mathf.Cos(a) * 0.25f, 0f, Mathf.Sin(a) * 0.25f), 0.01f, 4);
            }
            k.Rod(M.pintado, Vector3.up * 0.2f, Vector3.up * 1.15f, 0.012f, 5);
            k.Push(new Vector3(0f, 1.15f, 0f), 0f, -20f, 0f);
            k.Box(M.pintado, new Vector3(-0.25f, -0.02f, -0.01f), new Vector3(0.25f, 0.3f, 0.01f));
            k.C = new Color(0.97f, 0.95f, 0.88f);
            k.Box(M.pintado, new Vector3(-0.21f, 0.0f, -0.02f), new Vector3(0.21f, 0.28f, -0.01f));
            k.Pop();
            k.C = Color.white;
            k.Pop();
        }

        static void BookStack(InteriorKit k, Mats M, Vector3 b, int n)
        {
            var rng = new System.Random((int)(b.x * 100 + b.z * 37));
            float y = 0f;
            for (int i = 0; i < n; i++)
            {
                float th = 0.03f + 0.02f * (float)rng.NextDouble();
                k.Push(b + Vector3.up * y, (float)rng.NextDouble() * 30f - 15f);
                k.C = new Color(0.3f + 0.4f * (float)rng.NextDouble(), 0.1f + 0.2f * (float)rng.NextDouble(), 0.08f + 0.2f * (float)rng.NextDouble());
                k.Box(M.pintado, new Vector3(-0.1f, 0f, -0.14f), new Vector3(0.1f, th, 0.14f));
                k.C = new Color(0.95f, 0.92f, 0.82f);
                k.Box(M.pintado, new Vector3(-0.095f, 0.004f, -0.136f), new Vector3(0.104f, th - 0.004f, 0.136f));
                k.Pop();
                y += th;
            }
            k.C = Color.white;
        }

        static void HolyWaterFont(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = new Color(0.9f, 0.88f, 0.82f);
            k.Lathe(M.pedraClara, b, new[]
            {
                new Vector2(0.2f, 0f), new Vector2(0.2f, 0.08f), new Vector2(0.09f, 0.14f), new Vector2(0.07f, 0.6f),
                new Vector2(0.12f, 0.66f), new Vector2(0.26f, 0.8f), new Vector2(0.28f, 0.9f), new Vector2(0.24f, 0.9f), new Vector2(0.2f, 0.84f),
            }, 12, true, true, false);
            k.C = new Color(0.55f, 0.7f, 0.78f);
            k.Disc(M.pintado, b + Vector3.up * 0.84f, 0.21f, 12, true);
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.7f, 0.7f);
        }

        static void Confessional(InteriorKit k, Mats M, Vector3 wallP)
        {
            // encostado à parede −x local: largura ao longo de z, profundidade em x
            float x0 = wallP.x, x1 = x0 + 1.15f, z0 = wallP.z - 1.1f, z1 = wallP.z + 1.1f, y0 = wallP.y;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(x0, y0, z0), new Vector3(x1, y0 + 0.1f, z1));
            k.Box(M.madeira, new Vector3(x0, y0 + 2.2f, z0 - 0.05f), new Vector3(x1 + 0.08f, y0 + 2.32f, z1 + 0.05f));
            k.Box(M.madeira, new Vector3(x0, y0, z0), new Vector3(x1, y0 + 2.2f, z0 + 0.06f));
            k.Box(M.madeira, new Vector3(x0, y0, z1 - 0.06f), new Vector3(x1, y0 + 2.2f, z1));
            k.Box(M.madeira, new Vector3(x0, y0 + 1.95f, z0), new Vector3(x1, y0 + 2.2f, z1));
            // cabine central com porta baixa e cortinas laterais
            k.Box(M.madeira, new Vector3(x0, y0, -0.4f + wallP.z), new Vector3(x1, y0 + 2.2f, -0.34f + wallP.z));
            k.Box(M.madeira, new Vector3(x0, y0, 0.34f + wallP.z), new Vector3(x1, y0 + 2.2f, 0.4f + wallP.z));
            k.Box(M.madeira, new Vector3(x1 - 0.05f, y0 + 0.1f, -0.34f + wallP.z), new Vector3(x1, y0 + 1.15f, 0.34f + wallP.z));
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(x1, y0 + 0.25f, -0.25f + wallP.z), new Vector3(x1 + 0.015f, y0 + 1.0f, 0.25f + wallP.z));
            // grades das janelinhas
            k.C = new Color(0.25f, 0.16f, 0.1f);
            foreach (var s in new[] { -1f, 1f })
                for (float dz = -0.12f; dz <= 0.121f; dz += 0.06f)
                    k.Box(M.madeira, new Vector3(x0 + 0.3f, y0 + 1.2f, wallP.z + s * 0.37f + dz - 0.008f), new Vector3(x0 + 0.85f, y0 + 1.6f, wallP.z + s * 0.37f + dz + 0.008f));
            k.C = new Color(0.55f, 0.1f, 0.12f);
            foreach (var s in new[] { -1f, 1f })
            {
                float za = wallP.z + s * 0.42f, zb = wallP.z + s * 1.04f;
                for (int i = 0; i < 6; i++)
                {
                    float t0 = i / 6f, t1 = (i + 1) / 6f;
                    float dz = (i % 2 == 0) ? 0.02f : -0.01f;
                    k.Box(M.pintado, new Vector3(x1 - 0.06f + dz, y0 + 0.12f, Mathf.Lerp(za, zb, t0)), new Vector3(x1 - 0.03f + dz, y0 + 1.93f, Mathf.Lerp(za, zb, t1)));
                }
            }
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(x1 - 0.06f, y0 + 1.92f, z0), new Vector3(x1 + 0.02f, y0 + 1.97f, z1));
            k.Box(M.madeira, new Vector3(x0 + 0.5f, y0 + 2.32f, wallP.z - 0.025f), new Vector3(x0 + 0.56f, y0 + 2.62f, wallP.z + 0.025f));
            k.Box(M.madeira, new Vector3(x0 + 0.5f, y0 + 2.47f, wallP.z - 0.1f), new Vector3(x0 + 0.56f, y0 + 2.52f, wallP.z + 0.1f));
            k.C = Color.white;
            k.Col("Confessionario", new Vector3(x0, y0, z0), new Vector3(x1, y0 + 2.32f, z1));
            Blob(k, M, (x0 + x1) * 0.5f + 0.1f, y0, wallP.z, 1.6f, 2.6f);
        }

        static void VotiveStand(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = Iron;
            foreach (var x in new[] { -0.45f, 0.45f })
                foreach (var z in new[] { -0.2f, 0.2f })
                    k.Rod(M.pintado, new Vector3(x, 0f, z), new Vector3(x, 0.9f + (z < 0 ? 0f : 0.25f), z), 0.012f, 4);
            for (int row = 0; row < 3; row++)
            {
                float y = 0.75f + row * 0.13f, z = -0.15f + row * 0.15f;
                k.Box(M.pintado, new Vector3(-0.48f, y - 0.015f, z - 0.07f), new Vector3(0.48f, y, z + 0.07f));
                for (int i = 0; i < 7; i++)
                {
                    float x = -0.39f + i * 0.13f;
                    k.C = new Color(0.75f, 0.12f, 0.12f);
                    k.Cyl(M.pintado, new Vector3(x, y, z), 0.03f, 0.06f, 6);
                    k.C = Color.white;
                    if ((i + row) % 3 != 1) k.Ellipsoid(M.chama, new Vector3(x, y + 0.08f, z), new Vector3(0.01f, 0.022f, 0.01f), 5, 3);
                    k.C = Iron;
                }
            }
            k.C = Brass;
            k.Box(M.pintado, new Vector3(-0.1f, 0.5f, -0.24f), new Vector3(0.1f, 0.66f, -0.12f));
            k.C = Color.white;
            k.Col("Velas", new Vector3(-0.5f, 0f, -0.25f), new Vector3(0.5f, 1.15f, 0.25f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.2f, 0.7f, yaw);
        }

        static void NoticeBoard(InteriorKit k, Mats M, Vector3 p, float yaw)
        {
            k.Push(p, yaw);
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.45f, -0.35f, 0f), new Vector3(0.45f, 0.35f, 0.04f));
            k.C = new Color(0.72f, 0.58f, 0.4f);
            k.Box(M.pintado, new Vector3(-0.4f, -0.3f, 0.04f), new Vector3(0.4f, 0.3f, 0.045f));
            var rng = new System.Random(9);
            for (int i = 0; i < 5; i++)
            {
                k.C = i % 2 == 0 ? new Color(0.97f, 0.96f, 0.9f) : new Color(0.95f, 0.88f, 0.6f);
                float x = -0.32f + (float)rng.NextDouble() * 0.45f, y = -0.25f + (float)rng.NextDouble() * 0.3f;
                k.Box(M.pintado, new Vector3(x, y, 0.045f), new Vector3(x + 0.18f, y + 0.24f, 0.05f));
            }
            k.C = Color.white;
            k.Pop();
        }

        // =====================================================================================================
        // SOBRADO (local: fachada em z = 0 a olhar para −z, interior para +z)

        static readonly float[] Bays = { -7.5f, -4.5f, 1.5f, 4.5f, 7.5f };
        const float DoorX = -1.5f;

        static void House(InteriorKit k, Mats M)
        {
            float D = HouseDepth, h = HouseH, hw = W * 0.5f;
            float iz0 = T, iz1 = D - T, ix = hw - T;
            float f2b = Floor2 - 0.2f;

            // ---------------- paredes
            var frontVis = new List<Vector4> { H(DoorX, 1.3f, 0f, 2.3f), new Vector4(DoorX - 0.55f, DoorX + 0.55f, Floor2, Floor2 + 2.1f) };
            var frontCol = new List<Vector4>(frontVis);
            foreach (var x in Bays)
            {
                frontVis.Add(H(x, 1.0f, 1.0f, 2.3f));
                frontVis.Add(H(x, 1.0f, 4.05f, 5.35f));
                frontCol.Add(H(x, 1.0f, 4.05f, 5.35f));
            }
            WallX(k, M.ocre, 0f, T, -hw, hw, 0f, h, frontVis, frontCol, "Fachada");
            var backVis = new List<Vector4> { H(-0.8f, 1.0f, 1.0f, 2.3f), H(1.8f, 1.0f, 1.0f, 2.3f), H(-6.0f, 1.0f, 4.05f, 5.35f), H(1.2f, 1.0f, 4.05f, 5.35f) };
            WallX(k, M.ocre, D - T, D, -hw, hw, 0f, h, backVis, new List<Vector4>(), "Fundo");
            var sideVis = new List<Vector4> { H(4.5f, 1.0f, 4.05f, 5.35f) };
            WallZ(k, M.ocre, -hw, -hw + T, T, D - T, 0f, h, sideVis, new List<Vector4>(), "LadoA");
            WallZ(k, M.ocre, hw - T, hw, T, D - T, 0f, h, sideVis, new List<Vector4>(), "LadoB");
            Trim(k, M, D, h, new List<Vector4> { H(DoorX, 1.3f, 0f, 2.3f) }, true);
            HipRoof(k, M, D, h, 2.0f);

            // forro de troncos nas paredes interiores (os mesmos vãos)
            float lt = 0.05f;
            WallX(k, M.troncos, T, T + lt, -ix, ix, 0.03f, h, frontVis, null, null);
            WallX(k, M.troncos, D - T - lt, D - T, -ix, ix, 0.03f, h, backVis, null, null);
            WallZ(k, M.troncos, -ix, -ix + lt, T + lt, D - T - lt, 0.03f, h, sideVis, null, null);
            WallZ(k, M.troncos, ix - lt, ix, T + lt, D - T - lt, 0.03f, h, sideVis, null, null);
            // prumos de canto e a meio (troncos redondos)
            k.C = WoodMid;
            foreach (var p in new[] { new Vector2(-ix + 0.12f, iz0 + 0.12f), new Vector2(ix - 0.12f, iz0 + 0.12f), new Vector2(-ix + 0.12f, iz1 - 0.12f), new Vector2(ix - 0.12f, iz1 - 0.12f), new Vector2(-3.0f, iz0 + 0.1f), new Vector2(3.0f, iz0 + 0.1f), new Vector2(-2.6f, iz1 - 0.1f), new Vector2(3.0f, iz1 - 0.1f) })
                k.Cyl(M.madeira, new Vector3(p.x, 0.03f, p.y), 0.12f, h - 0.03f, 8, true, false);
            k.C = Color.white;

            // ---------------- portas, janelas, varanda
            DoorFrame(k, M, DoorX, 0f, 1.3f, 2.3f, true);
            k.Box(M.pedraClara, new Vector3(DoorX - 0.9f, 0f, -0.45f), new Vector3(DoorX + 0.9f, 0.13f, 0.02f));     // degrau
            k.Col("Degrau", new Vector3(DoorX - 0.9f, 0f, -0.45f), new Vector3(DoorX + 0.9f, 0.13f, 0.02f));
            k.Box(M.soalho, new Vector3(DoorX - 0.65f, 0f, 0f), new Vector3(DoorX + 0.65f, 0.08f, T));
            DoorLeaf(k, M, M.azul, new Vector3(DoorX - 0.65f, 0.05f, T), 1.3f, 2.24f, -100f, true, "Porta");
            DoorFrame(k, M, DoorX, Floor2, 1.1f, 2.1f, false);
            DoorLeaf(k, M, M.azul, new Vector3(DoorX - 0.55f, Floor2 + 0.01f, T), 0.55f, 2.06f, -95f, true, "PortaVarandaA");
            DoorLeaf(k, M, M.azul, new Vector3(DoorX + 0.55f, Floor2 + 0.01f, T), 0.55f, 2.06f, 95f, false, "PortaVarandaB");
            k.Box(M.soalho, new Vector3(DoorX - 0.55f, Floor2 - 0.2f, 0f), new Vector3(DoorX + 0.55f, Floor2, T));
            Balcony(k, M);
            foreach (var x in Bays)
            {
                HouseWindow(k, M, x, 1.0f, true);
                HouseWindow(k, M, x, 4.05f, false);
            }
            foreach (var x in new[] { -0.8f, 1.8f })
            {
                k.Push(new Vector3(0f, 0f, D), 180f);
                BarredWindow(k, M, -x, 1.0f);
                k.Pop();
            }
            foreach (var x in new[] { -6.0f, 1.2f })
            {
                k.Push(new Vector3(0f, 0f, D), 180f);
                BarredWindow(k, M, -x, 4.05f);
                k.Pop();
            }
            foreach (var s in new[] { -1f, 1f })
            {
                k.Push(new Vector3(s * hw, 0f, 4.5f), s > 0 ? -90f : 90f);
                BarredWindow(k, M, 0f, 4.05f);
                k.Pop();
            }

            // ---------------- pisos e tetos
            k.Box(M.soalho, new Vector3(-ix, -0.06f, iz0), new Vector3(ix, 0.03f, iz1));
            k.Col("Chao", new Vector3(-ix, -0.4f, iz0 - 0.3f), new Vector3(ix, 0.03f, iz1));
            // laje do 1.º andar com o vão da escada (x de −7,54 a −3,9, z de 7,6 ao fundo)
            float sh0 = -7.54f, sh1 = -3.9f, sz = 7.6f;   // vão largo: a cabeça não bate na laje ao subir
            k.Box(M.soalho, new Vector3(-ix, f2b, iz0), new Vector3(ix, Floor2, sz));
            k.Box(M.soalho, new Vector3(sh1, f2b, sz), new Vector3(ix, Floor2, iz1));
            k.Box(M.soalho, new Vector3(-ix, f2b, sz), new Vector3(sh0, Floor2, iz1));
            k.Col("Piso1_A", new Vector3(-ix, f2b, iz0), new Vector3(ix, Floor2, sz));
            k.Col("Piso1_B", new Vector3(sh1, f2b, sz), new Vector3(ix, Floor2, iz1));
            k.Col("Piso1_C", new Vector3(-ix, f2b, sz), new Vector3(sh0, Floor2, iz1));
            // vigas do rés-do-chão (troncos) e teto do 1.º andar
            k.C = WoodDark;
            for (float x = -7.8f; x <= 7.81f; x += 1.3f)
            {
                float zEnd = (x > sh0 - 0.2f && x < sh1 + 0.2f) ? sz : iz1;
                k.Push(new Vector3(x, f2b - 0.12f, iz0), 0f, 90f, 0f);
                k.Cyl(M.madeira, Vector3.zero, 0.12f, zEnd - iz0, 8);
                k.Pop();
            }
            k.Box(M.madeira, new Vector3(sh0 - 0.1f, f2b - 0.26f, sz - 0.2f), new Vector3(sh1 + 0.1f, f2b, sz));
            k.C = new Color(0.9f, 0.82f, 0.7f);
            k.Box(M.soalho, new Vector3(-ix, h - 0.08f, iz0), new Vector3(ix, h, iz1));
            k.C = WoodDark;
            for (float x = -7.8f; x <= 7.81f; x += 1.3f)
            {
                k.Push(new Vector3(x, h - 0.2f, iz0), 0f, 90f, 0f);
                k.Cyl(M.madeira, Vector3.zero, 0.12f, iz1 - iz0, 8);
                k.Pop();
            }
            k.Push(new Vector3(-ix, h - 0.32f, 4.5f), 0f, 0f, -90f);
            k.Cyl(M.madeira, Vector3.zero, 0.2f, 2 * ix, 10);       // trave mestra
            k.Pop();
            k.C = Color.white;
            k.Col("Teto", new Vector3(-hw, h - 0.08f, 0f), new Vector3(hw, h + 0.2f, D));

            // ---------------- escada de madeira (16 degraus) e guardas
            HouseStair(k, M, sz, iz1);
            Balustrade(k, M, new Vector3(sh0 + 0.05f, Floor2, sz + 0.05f), new Vector3(sh1, Floor2, sz + 0.05f), 0.95f, WoodDark);
            Balustrade(k, M, new Vector3(sh1 + 0.05f, Floor2, sz + 0.05f), new Vector3(sh1 + 0.05f, Floor2, iz1 - 0.05f), 0.95f, WoodDark);
            k.Col("GuardaVao_A", new Vector3(sh0, Floor2, sz - 0.04f), new Vector3(sh1 + 0.1f, Floor2 + 1.0f, sz + 0.14f));
            k.Col("GuardaVao_B", new Vector3(sh1, Floor2, sz), new Vector3(sh1 + 0.14f, Floor2 + 1.0f, iz1));

            // ---------------- rés-do-chão: cozinha rústica
            Stove(k, M, new Vector3(7.55f, 0.03f, iz1 - 0.42f));
            k.Col("Fogao", new Vector3(6.95f, 0f, iz1 - 0.8f), new Vector3(8.2f, 1.0f, iz1));
            Firewood(k, M, new Vector3(ix - 0.35f, 0.03f, 6.4f));
            k.Col("Lenha", new Vector3(ix - 0.6f, 0f, 5.75f), new Vector3(ix, 0.75f, 7.05f));
            Shelves(k, M, new Vector3(4.6f, 1.45f, iz1), 2.6f);
            HangingPans(k, M, new Vector3(6.0f, 2.35f, iz1 - 0.08f));
            Counter(k, M, new Vector3(ix, 0.03f, 3.6f), 2.6f);
            k.Col("Bancada", new Vector3(ix - 0.62f, 0f, 2.3f), new Vector3(ix, 0.95f, 4.9f));
            Table(k, M, new Vector3(1.6f, 0.03f, 4.3f), 1.9f, 0.95f, true);
            k.Col("Mesa", new Vector3(0.65f, 0f, 3.82f), new Vector3(2.55f, 0.8f, 4.78f));
            Chair(k, M, new Vector3(1.05f, 0.03f, 3.5f), 0f, WoodMid, false);
            Chair(k, M, new Vector3(2.15f, 0.03f, 3.45f), 8f, WoodMid, false);
            Chair(k, M, new Vector3(1.0f, 0.03f, 5.15f), 180f, WoodMid, false);
            Chair(k, M, new Vector3(2.75f, 0.03f, 4.3f), -90f + 12f, WoodMid, false);
            Bench(k, M, new Vector3(2.2f, 0.03f, 5.1f), 1.3f, 0f, WoodDark);
            Rug(k, M, new Vector3(1.6f, 0.03f, 4.3f), 3.4f, 2.4f, 0);
            HangingLamp(k, M, new Vector3(1.6f, f2b, 4.3f), 0.95f);
            k.Light("Luz_Candeeiro", new Vector3(1.6f, f2b - 1.05f, 4.3f), new Color(1f, 0.78f, 0.5f), 8.5f, 1.25f, LightShadows.Soft);
            k.Light("Luz_Fogao", new Vector3(7.55f, 0.55f, iz1 - 1.0f), new Color(1f, 0.48f, 0.18f), 4.5f, 1.3f, LightShadows.None);
            Cradle(k, M, new Vector3(5.2f, 0.03f, 6.6f), 15f);
            RockingChair(k, M, new Vector3(6.3f, 0.03f, 5.6f), -140f);
            Chest(k, M, new Vector3(-ix + 0.32f, 0.03f, 1.6f), -90f, 1.0f, new Color(0.45f, 0.28f, 0.18f));
            k.Col("Bau1", new Vector3(-ix, 0f, 1.05f), new Vector3(-ix + 0.62f, 0.62f, 2.15f));
            Chest(k, M, new Vector3(-4.5f, 0.03f, iz0 + 0.62f), 180f, 0.9f, new Color(0.2f, 0.35f, 0.55f));
            k.Col("Bau2", new Vector3(-4.95f, 0f, iz0 + 0.3f), new Vector3(-4.05f, 0.6f, iz0 + 0.95f));
            Cabinet(k, M, new Vector3(-ix + 0.25f, 0.03f, 4.6f), -90f);
            k.Col("Cristaleira", new Vector3(-ix, 0f, 3.95f), new Vector3(-ix + 0.5f, 2.05f, 5.25f));
            RockingChair(k, M, new Vector3(-6.6f, 0.03f, 3.2f), 60f);
            Rug(k, M, new Vector3(-6.4f, 0.03f, 3.4f), 2.2f, 1.6f, 1);
            Barrel(k, M, new Vector3(-6.7f, 0.03f, iz1 - 0.4f), 0.32f, 0.85f);
            Barrel(k, M, new Vector3(-6.0f, 0.03f, iz1 - 0.42f), 0.3f, 0.8f);
            Crate(k, M, new Vector3(-7.6f, 0.03f, iz1 - 0.4f), 0.6f, 15f);
            Crate(k, M, new Vector3(-7.6f, 0.63f, iz1 - 0.4f), 0.5f, -10f);
            k.Col("Barris", new Vector3(-8.0f, 0f, iz1 - 0.85f), new Vector3(-5.6f, 1.1f, iz1));
            WallClock(k, M, new Vector3(-ix + lt, 2.2f, 6.6f), 90f);
            CoatHooks(k, M, new Vector3(-3.5f, 1.75f, T + lt), 0f);
            Broom(k, M, new Vector3(-0.35f, 0.03f, T + lt + 0.06f));
            Picture(k, M, new Vector3(ix - lt, 1.85f, 1.6f), -90f, 0);
            foreach (var x in Bays)
            {
                Plant(k, M, new Vector3(x, 1.0f, T + lt + 0.14f), (int)(x * 10));
            }

            // ---------------- 1.º andar: quarto e posto de tiro
            float y2 = Floor2;
            Bed(k, M, new Vector3(4.4f, y2, iz1), 0f);
            k.Col("Cama", new Vector3(3.55f, y2, iz1 - 2.1f), new Vector3(5.25f, y2 + 0.6f, iz1));
            foreach (var x in new[] { 3.05f, 5.75f })
            {
                Nightstand(k, M, new Vector3(x, y2, iz1 - 0.25f));
                k.Col("Mesinha", new Vector3(x - 0.25f, y2, iz1 - 0.5f), new Vector3(x + 0.25f, y2 + 0.65f, iz1));
            }
            Chest(k, M, new Vector3(4.4f, y2, iz1 - 2.45f), 0f, 1.1f, new Color(0.5f, 0.3f, 0.18f));
            k.Col("Bau3", new Vector3(3.85f, y2, iz1 - 2.8f), new Vector3(4.95f, y2 + 0.62f, iz1 - 2.12f));
            Wardrobe(k, M, new Vector3(ix - 0.32f, y2, 6.6f), 90f);
            k.Col("Armario", new Vector3(ix - 0.65f, y2, 5.8f), new Vector3(ix, y2 + 2.25f, 7.4f));
            Washstand(k, M, new Vector3(7.3f, y2, iz1 - 0.3f));
            k.Col("Lavatorio", new Vector3(6.95f, y2, iz1 - 0.6f), new Vector3(7.65f, y2 + 0.85f, iz1));
            SpinningWheel(k, M, new Vector3(-4.8f, y2, 4.6f), 35f);
            Basket(k, M, new Vector3(-4.0f, y2, 5.2f));
            RockingChair(k, M, new Vector3(0.8f, y2, 6.9f), 200f);
            Desk(k, M, new Vector3(-1.8f, y2, iz1 - 0.35f));
            k.Col("Secretaria", new Vector3(-2.45f, y2, iz1 - 0.7f), new Vector3(-1.15f, y2 + 0.8f, iz1));
            Chair(k, M, new Vector3(-1.8f, y2, iz1 - 1.0f), 180f + 10f, WoodMid, false);
            Rug(k, M, new Vector3(4.4f, y2, 5.6f), 2.6f, 1.8f, 2);
            Rug(k, M, new Vector3(-1.5f, y2, 3.6f), 2.4f, 1.6f, 1);
            Picture(k, M, new Vector3(-1.8f, y2 + 1.75f, iz1 - lt), 180f, 1);
            Picture(k, M, new Vector3(-ix + lt, y2 + 1.7f, 6.2f), 90f, 2);
            HangingLamp(k, M, new Vector3(0f, h - 0.32f, 4.5f), 0.85f);
            k.Light("Luz_Quarto", new Vector3(0f, h - 1.2f, 4.5f), new Color(1f, 0.82f, 0.58f), 9.5f, 1.0f, LightShadows.None);
            Crate(k, M, new Vector3(-7.9f, y2, 1.0f), 0.55f, 20f);
            Crate(k, M, new Vector3(-7.85f, y2, 1.65f), 0.5f, -5f);
            k.Col("Caixotes", new Vector3(-ix, y2, 0.65f), new Vector3(-7.5f, y2 + 0.6f, 2.0f));
            foreach (var x in Bays) Plant(k, M, new Vector3(x + 0.38f, 4.05f, T + lt + 0.12f), (int)(x * 7) + 3);
        }

        static void HouseStair(InteriorKit k, Mats M, float z0, float z1)
        {
            const int n = 16;
            float rise = Floor2 / n, run = 0.29f, xBot = -2.9f;
            k.C = WoodMid;
            for (int i = 0; i < n; i++)
            {
                float xa = xBot - (i + 1) * run, xb = xBot - i * run, y = (i + 1) * rise;
                k.Box(M.madeira, new Vector3(xa, y - 0.05f, z0 + 0.04f), new Vector3(xb + 0.03f, y, z1 - 0.02f));
                k.C = WoodDark;
                k.Box(M.madeira, new Vector3(xb - 0.025f, y - rise, z0 + 0.06f), new Vector3(xb, y - 0.05f, z1 - 0.04f));
                k.C = WoodMid;
            }
            // pernas (vigas inclinadas)
            k.C = WoodDark;
            var a = new Vector3(xBot + run, 0f, 0f);
            var b = new Vector3(xBot - n * run, Floor2, 0f);
            foreach (var z in new[] { z0 + 0.04f, z1 - 0.05f })
            {
                var dir = (b - a).normalized;
                var rot = Quaternion.FromToRotation(Vector3.right, -dir);
                k.Push(new Vector3(b.x, b.y - 0.12f, z), rot, Vector3.one);
                k.Box(M.madeira, new Vector3(0f, -0.14f, -0.035f), new Vector3((b - a).magnitude, 0.1f, 0.035f));
                k.Pop();
            }
            // corrimão do lado aberto (z0): balaústres até à barra, prumos no fundo e no topo
            float zr = z0 + 0.06f;
            var ha = new Vector3(xBot, rise + 0.95f, zr);
            var hb = new Vector3(xBot - n * run, Floor2 + 0.95f, zr);
            System.Func<float, float> hrY = x => Mathf.Lerp(ha.y, hb.y, Mathf.InverseLerp(ha.x, hb.x, x));
            for (int i = 0; i < n; i++)
            {
                float x = xBot - i * run - run * 0.5f;
                float y = (i + 1) * rise;
                k.Push(new Vector3(x, y, zr), Quaternion.identity, new Vector3(0.75f, 1f, 0.75f));
                Baluster(k, M, Vector3.zero, hrY(x) - y - 0.01f);
                k.Pop();
            }
            k.Rod(M.madeira, ha, hb, 0.045f, 8);
            k.BoxB(M.madeira, new Vector3(xBot - 0.1f, 0.03f, zr), new Vector3(0.13f, rise + 1.05f, 0.13f));
            k.Sphere(M.madeira, new Vector3(xBot - 0.1f, rise + 1.12f, zr), 0.075f, 8, 4);
            k.C = Color.white;
            // colisores: rampa + corrimão
            var p0 = new Vector3(xBot + run, 0f, 0f);
            var p1 = new Vector3(xBot - (n - 1) * run, Floor2, 0f);
            var d = (p1 - p0).normalized;
            var r = Quaternion.LookRotation(d, Vector3.up);
            var mid = (p0 + p1) * 0.5f - (r * Vector3.up) * 0.1f;
            mid.z = (z0 + z1) * 0.5f;
            k.ColRot("Rampa", mid, new Vector3(z1 - z0, 0.2f, (p1 - p0).magnitude), r);
            var c0 = new Vector3(xBot, rise, z0 + 0.06f);
            var c1 = new Vector3(xBot - n * run, Floor2, z0 + 0.06f);
            var rr = Quaternion.LookRotation((c1 - c0).normalized, Vector3.up);
            k.ColRot("Corrimao", (c0 + c1) * 0.5f + rr * Vector3.up * 0.55f, new Vector3(0.1f, 1.1f, (c1 - c0).magnitude), rr);
        }

        static void DoorFrame(InteriorKit k, Mats M, float cx, float y0, float w, float hh, bool step)
        {
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.16f, y0, -0.1f), new Vector3(cx - w * 0.5f, y0 + hh, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx + w * 0.5f, y0, -0.1f), new Vector3(cx + w * 0.5f + 0.16f, y0 + hh, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.2f, y0 + hh, -0.12f), new Vector3(cx + w * 0.5f + 0.2f, y0 + hh + 0.22f, 0.02f));
            k.Box(M.pedraClara, new Vector3(cx - w * 0.5f - 0.26f, y0 + hh + 0.22f, -0.16f), new Vector3(cx + w * 0.5f + 0.26f, y0 + hh + 0.3f, 0.02f));
            k.C = Color.white;
            k.Col("Ombreira", new Vector3(cx - w * 0.5f - 0.16f, y0, -0.1f), new Vector3(cx - w * 0.5f, y0 + hh, 0f));
            k.Col("Ombreira", new Vector3(cx + w * 0.5f, y0, -0.1f), new Vector3(cx + w * 0.5f + 0.16f, y0 + hh, 0f));
        }

        static void Balcony(InteriorKit k, Mats M)
        {
            float x0 = DoorX - 1.25f, x1 = DoorX + 1.25f, z0 = -1.05f, y = Floor2;
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pedraClara, new Vector3(x0, y - 0.14f, z0), new Vector3(x1, y, 0.02f));
            k.Box(M.pedraClara, new Vector3(x0 - 0.04f, y - 0.2f, z0 - 0.04f), new Vector3(x1 + 0.04f, y - 0.14f, 0.02f));
            foreach (var x in new[] { x0 + 0.25f, DoorX, x1 - 0.25f })
            {
                k.Push(new Vector3(x, y - 0.2f, 0f), 90f);
                k.Prism(M.pedraClara, new[] { new Vector2(0f, 0f), new Vector2(0.75f, 0f), new Vector2(0.06f, -0.5f), new Vector2(0f, -0.5f) }, -0.09f, 0.09f);
                k.Pop();
            }
            k.C = Iron;
            float hr = 1.0f;
            void Rail(Vector3 a, Vector3 b)
            {
                k.Rod(M.pintado, a + Vector3.up * hr, b + Vector3.up * hr, 0.025f, 6);
                k.Rod(M.pintado, a + Vector3.up * 0.08f, b + Vector3.up * 0.08f, 0.015f, 4);
                k.Rod(M.pintado, a + Vector3.up * (hr - 0.18f), b + Vector3.up * (hr - 0.18f), 0.012f, 4);
                float len = (b - a).magnitude;
                int n = Mathf.RoundToInt(len / 0.12f);
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)n);
                    k.Rod(M.pintado, p, p + Vector3.up * hr, 0.01f, 4, false);
                    if (i % 2 == 1)
                    {
                        var dir = (b - a).normalized;
                        k.Push(p + Vector3.up * (hr - 0.32f), Quaternion.LookRotation(dir, Vector3.up), Vector3.one);
                        k.Push(Vector3.zero, 90f, 0f, 0f);
                        k.Push(Vector3.zero, 0f, 0f, 90f);
                        k.Torus(M.pintado, Vector3.zero, 0.06f, 0.008f, 10, 3);
                        k.Pop();
                        k.Pop();
                        k.Pop();
                    }
                }
            }
            var A = new Vector3(x0 + 0.03f, y, 0f);
            var B = new Vector3(x0 + 0.03f, y, z0 + 0.03f);
            var C = new Vector3(x1 - 0.03f, y, z0 + 0.03f);
            var D = new Vector3(x1 - 0.03f, y, 0f);
            Rail(A, B);
            Rail(B, C);
            Rail(C, D);
            foreach (var p in new[] { B, C })
                k.Sphere(M.pintado, p + Vector3.up * (hr + 0.05f), 0.04f, 6, 4);
            k.C = Color.white;
            // vaso com sardinheiras no canto
            k.C = new Color(0.72f, 0.38f, 0.24f);
            k.Lathe(M.pintado, new Vector3(x1 - 0.25f, y, z0 + 0.25f), new[] { new Vector2(0.11f, 0f), new Vector2(0.16f, 0.26f), new Vector2(0.17f, 0.28f) }, 10, true, true, true);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f;
                var p = new Vector3(x1 - 0.25f + Mathf.Cos(a) * 0.1f, y + 0.35f + (i % 3) * 0.05f, z0 + 0.25f + Mathf.Sin(a) * 0.1f);
                k.C = new Color(0.28f, 0.55f, 0.22f);
                k.Sphere(M.pintado, p, 0.09f, 6, 4);
                k.C = new Color(0.9f, 0.15f, 0.2f);
                k.Sphere(M.pintado, p + Vector3.up * 0.07f, 0.04f, 6, 3);
            }
            k.C = Color.white;
            k.Col("Varanda", new Vector3(x0, y - 0.2f, z0), new Vector3(x1, y, 0f));
            k.Col("Varanda_Frente", new Vector3(x0, y, z0 - 0.02f), new Vector3(x1, y + hr + 0.05f, z0 + 0.06f));
            k.Col("Varanda_LadoA", new Vector3(x0 - 0.02f, y, z0), new Vector3(x0 + 0.06f, y + hr + 0.05f, 0f));
            k.Col("Varanda_LadoB", new Vector3(x1 - 0.06f, y, z0), new Vector3(x1 + 0.02f, y + hr + 0.05f, 0f));
        }

        static void HouseWindow(InteriorKit k, Mats M, float cx, float y0, bool bars)
        {
            float w = 1.0f, hh = 1.3f;
            StoneFrame(k, M, cx, y0, w, hh, true);
            OpenShuttersFlat(k, M, M.azul, cx, y0, w, hh);
            // floreira por fora
            k.Box(M.barro, new Vector3(cx - 0.43f, y0 - 0.32f, -0.32f), new Vector3(cx + 0.43f, y0 - 0.12f, -0.12f));
            for (int i = 0; i < 5; i++)
            {
                float x = cx - 0.34f + i * 0.17f;
                k.Ellipsoid(M.folhas, new Vector3(x, y0 - 0.05f, -0.22f), new Vector3(0.1f, 0.08f, 0.09f), 6, 4);
                k.Sphere((i % 2 == 0) ? M.florRosa : M.florVerm, new Vector3(x + 0.03f, y0 + 0.02f, -0.27f), 0.045f, 6, 3);
            }
            // peitoril interior em madeira
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(cx - 0.6f, y0 - 0.05f, T - 0.02f), new Vector3(cx + 0.6f, y0, T + 0.2f));
            k.C = Color.white;
            if (bars)
            {
                k.C = Iron;
                for (float x = cx - w * 0.5f + 0.125f; x < cx + w * 0.5f - 0.05f; x += 0.125f)
                    k.Rod(M.pintado, new Vector3(x, y0, T * 0.5f), new Vector3(x, y0 + hh, T * 0.5f), 0.012f, 4, false);
                k.Box(M.pintado, new Vector3(cx - w * 0.5f, y0 + 0.6f, T * 0.5f - 0.012f), new Vector3(cx + w * 0.5f, y0 + 0.63f, T * 0.5f + 0.012f));
                k.C = Color.white;
            }
        }

        static void BarredWindow(InteriorKit k, Mats M, float cx, float y0)
        {
            float w = 1.0f, hh = 1.3f;
            StoneFrame(k, M, cx, y0, w, hh, true);
            OpenShuttersFlat(k, M, M.azul, cx, y0, w, hh);
            k.C = Iron;
            for (float x = cx - w * 0.5f + 0.125f; x < cx + w * 0.5f - 0.05f; x += 0.125f)
                k.Rod(M.pintado, new Vector3(x, y0, T * 0.5f), new Vector3(x, y0 + hh, T * 0.5f), 0.012f, 4, false);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(cx - 0.6f, y0 - 0.05f, T - 0.02f), new Vector3(cx + 0.6f, y0, T + 0.2f));
            k.C = Color.white;
        }

        // ------------------------------------------------------------------ mobília rústica

        static void Stove(InteriorKit k, Mats M, Vector3 b)
        {
            // fogão de lenha em ferro fundido, frente para −z
            k.Push(b, 0f);
            k.C = new Color(0.13f, 0.13f, 0.14f);
            foreach (var x in new[] { -0.55f, 0.55f })
                foreach (var z in new[] { -0.3f, 0.3f })
                    k.Lathe(M.pintado, new Vector3(x, 0f, z), new[] { new Vector2(0.05f, 0f), new Vector2(0.03f, 0.08f), new Vector2(0.045f, 0.16f) }, 6);
            k.Box(M.pintado, new Vector3(-0.6f, 0.16f, -0.34f), new Vector3(0.6f, 0.82f, 0.34f));
            k.Box(M.pintado, new Vector3(-0.63f, 0.82f, -0.37f), new Vector3(0.63f, 0.87f, 0.37f));
            k.Box(M.pintado, new Vector3(-0.63f, 0.87f, 0.28f), new Vector3(0.63f, 1.1f, 0.37f));     // costas
            // portas (forno e fornalha), puxadores e grelha com lume
            k.C = new Color(0.2f, 0.2f, 0.21f);
            k.Box(M.pintado, new Vector3(-0.55f, 0.24f, -0.37f), new Vector3(-0.08f, 0.7f, -0.34f));
            k.Box(M.pintado, new Vector3(0.05f, 0.45f, -0.37f), new Vector3(0.52f, 0.72f, -0.34f));
            k.C = Brass;
            k.Rod(M.pintado, new Vector3(-0.5f, 0.66f, -0.39f), new Vector3(-0.13f, 0.66f, -0.39f), 0.012f, 5);
            k.Rod(M.pintado, new Vector3(0.1f, 0.68f, -0.39f), new Vector3(0.47f, 0.68f, -0.39f), 0.012f, 5);
            k.Rod(M.pintado, new Vector3(-0.66f, 0.78f, -0.41f), new Vector3(0.66f, 0.78f, -0.41f), 0.01f, 5);
            k.C = new Color(0.1f, 0.1f, 0.1f);
            for (float x = 0.1f; x < 0.48f; x += 0.06f)
                k.Box(M.pintado, new Vector3(x, 0.2f, -0.375f), new Vector3(x + 0.025f, 0.4f, -0.36f));
            k.C = Color.white;
            k.Box(M.fogo, new Vector3(0.08f, 0.2f, -0.36f), new Vector3(0.5f, 0.4f, -0.34f));
            // tampas das bocas
            k.C = new Color(0.18f, 0.18f, 0.19f);
            foreach (var x in new[] { -0.32f, 0.1f, 0.42f })
                k.Cyl(M.pintado, new Vector3(x, 0.87f, -0.08f), 0.13f, 0.012f, 12);
            // panela grande, tacho e chaleira
            k.C = new Color(0.32f, 0.3f, 0.3f);
            k.Lathe(M.pintado, new Vector3(-0.32f, 0.88f, -0.08f), new[] { new Vector2(0.12f, 0f), new Vector2(0.16f, 0.04f), new Vector2(0.17f, 0.24f), new Vector2(0.18f, 0.26f) }, 12, true, true, true);
            k.C = new Color(0.2f, 0.2f, 0.2f);
            k.Lathe(M.pintado, new Vector3(-0.32f, 1.14f, -0.08f), new[] { new Vector2(0.17f, 0f), new Vector2(0.12f, 0.04f), new Vector2(0.03f, 0.06f), new Vector2(0.03f, 0.09f), new Vector2(0f, 0.09f) }, 12);
            k.Rod(M.pintado, new Vector3(-0.5f, 1.08f, -0.08f), new Vector3(-0.53f, 1.08f, -0.08f), 0.02f, 4);
            k.Rod(M.pintado, new Vector3(-0.14f, 1.08f, -0.08f), new Vector3(-0.11f, 1.08f, -0.08f), 0.02f, 4);
            k.C = new Color(0.62f, 0.38f, 0.22f);    // tacho de cobre
            k.Lathe(M.pintado, new Vector3(0.1f, 0.88f, -0.08f), new[] { new Vector2(0.1f, 0f), new Vector2(0.13f, 0.03f), new Vector2(0.13f, 0.1f), new Vector2(0.14f, 0.11f) }, 12, true, true, false);
            k.Rod(M.pintado, new Vector3(0.23f, 0.97f, -0.08f), new Vector3(0.42f, 0.98f, -0.25f), 0.012f, 4);
            k.C = new Color(0.25f, 0.3f, 0.35f);     // chaleira esmaltada
            k.Lathe(M.pintado, new Vector3(0.42f, 0.88f, 0.05f), new[] { new Vector2(0.09f, 0f), new Vector2(0.12f, 0.06f), new Vector2(0.11f, 0.14f), new Vector2(0.05f, 0.18f), new Vector2(0.03f, 0.2f), new Vector2(0f, 0.21f) }, 10);
            k.Rod(M.pintado, new Vector3(0.32f, 0.95f, 0.05f), new Vector3(0.24f, 1.06f, 0.05f), 0.015f, 4);
            k.Push(new Vector3(0.42f, 1.1f, 0.05f), 0f, 0f, 90f);
            k.Torus(M.pintado, Vector3.zero, 0.08f, 0.008f, 10, 3, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
            k.Pop();
            // chaminé (tubo) até ao teto
            k.C = new Color(0.15f, 0.15f, 0.16f);
            k.Cyl(M.pintado, new Vector3(-0.05f, 1.1f, 0.2f), 0.08f, 2.2f, 10, true, false);
            k.Torus(M.pintado, new Vector3(-0.05f, 1.6f, 0.2f), 0.085f, 0.01f, 10, 3);
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.6f, 1.1f);
            // tapete de pedra à frente (contra o lume)
            k.C = new Color(0.55f, 0.5f, 0.45f);
            k.Box(M.pedraClara, new Vector3(b.x - 0.8f, b.y, b.z - 0.9f), new Vector3(b.x + 0.8f, b.y + 0.02f, b.z - 0.37f));
            k.C = Color.white;
        }

        static void Firewood(InteriorKit k, Mats M, Vector3 b)
        {
            var rng = new System.Random(77);
            for (int row = 0; row < 5; row++)
                for (int i = 0; i < 6 - (row % 2); i++)
                {
                    float r = 0.06f + 0.015f * (float)rng.NextDouble();
                    float z = b.z - 0.6f + i * 0.22f + (row % 2) * 0.11f;
                    float y = b.y + r + row * 0.13f;
                    k.C = new Color(0.55f + 0.1f * (float)rng.NextDouble(), 0.4f, 0.28f);
                    k.Push(new Vector3(b.x - 0.28f + 0.03f * (float)rng.NextDouble(), y, z), 0f, 0f, -90f);
                    k.Cyl(M.madeira, Vector3.zero, r, 0.55f, 7);
                    k.Pop();
                }
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.9f, 1.5f);
        }

        static void Shelves(InteriorKit k, Mats M, Vector3 c, float len)
        {
            float z = c.z - 0.05f;
            var rng = new System.Random(12);
            for (int s = 0; s < 2; s++)
            {
                float y = c.y + s * 0.5f;
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(c.x - len * 0.5f, y - 0.04f, z - 0.28f), new Vector3(c.x + len * 0.5f, y, z));
                foreach (var x in new[] { c.x - len * 0.5f + 0.15f, c.x + len * 0.5f - 0.15f })
                    k.Box(M.madeira, new Vector3(x - 0.02f, y - 0.25f, z - 0.05f), new Vector3(x + 0.02f, y - 0.04f, z));
                for (float x = c.x - len * 0.5f + 0.12f; x < c.x + len * 0.5f - 0.1f;)
                {
                    int kind = rng.Next(4);
                    var p = new Vector3(x, y, z - 0.14f);
                    if (kind == 0)
                    {
                        // prato encostado
                        k.C = rng.Next(2) == 0 ? new Color(0.95f, 0.95f, 0.93f) : new Color(0.3f, 0.5f, 0.8f);
                        k.Push(p + new Vector3(0.1f, 0.1f, 0.08f), 0f, -78f, 0f);
                        k.Cyl(M.pintado, Vector3.zero, 0.1f, 0.015f, 12);
                        k.Pop();
                        x += 0.22f;
                    }
                    else if (kind == 1)
                    {
                        // frasco de vidro com conserva
                        k.C = new Color(0.75f + 0.2f * (float)rng.NextDouble(), 0.45f + 0.3f * (float)rng.NextDouble(), 0.2f);
                        k.Lathe(M.pintado, p + new Vector3(0.06f, 0f, 0f), new[] { new Vector2(0.05f, 0f), new Vector2(0.055f, 0.14f), new Vector2(0.035f, 0.17f), new Vector2(0.035f, 0.19f) }, 8);
                        k.C = new Color(0.9f, 0.85f, 0.75f);
                        k.Cyl(M.pintado, p + new Vector3(0.06f, 0.19f, 0f), 0.045f, 0.02f, 8);
                        x += 0.14f;
                    }
                    else if (kind == 2)
                    {
                        // tigela e caneca de barro
                        k.C = new Color(0.72f, 0.42f, 0.26f);
                        k.Lathe(M.pintado, p + new Vector3(0.09f, 0f, 0f), new[] { new Vector2(0.04f, 0f), new Vector2(0.09f, 0.04f), new Vector2(0.1f, 0.08f) }, 10, true, true, false);
                        k.C = new Color(0.85f, 0.8f, 0.7f);
                        k.Cyl(M.pintado, p + new Vector3(0.24f, 0f, 0.02f), 0.04f, 0.1f, 8);
                        x += 0.33f;
                    }
                    else
                    {
                        // cântaro
                        k.C = new Color(0.68f, 0.4f, 0.25f);
                        k.Lathe(M.pintado, p + new Vector3(0.09f, 0f, 0f), new[] { new Vector2(0.06f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0.08f, 0.22f), new Vector2(0.04f, 0.27f), new Vector2(0.05f, 0.3f) }, 10, true, true, false);
                        x += 0.24f;
                    }
                }
            }
            k.C = Color.white;
        }

        static void HangingPans(InteriorKit k, Mats M, Vector3 c)
        {
            k.C = Iron;
            k.Rod(M.pintado, c + new Vector3(-0.6f, 0f, -0.06f), c + new Vector3(0.6f, 0f, -0.06f), 0.015f, 5);
            for (int i = 0; i < 4; i++)
            {
                float x = -0.45f + i * 0.3f;
                k.C = i % 2 == 0 ? new Color(0.62f, 0.38f, 0.22f) : new Color(0.2f, 0.2f, 0.21f);
                k.Rod(M.pintado, c + new Vector3(x, -0.02f, -0.06f), c + new Vector3(x, -0.22f, -0.06f), 0.012f, 4);
                k.Push(c + new Vector3(x, -0.38f, -0.07f), 0f, 90f, 0f);
                k.Cyl(M.pintado, Vector3.zero, 0.12f + 0.02f * (i % 2), 0.04f, 12);
                k.Pop();
            }
            k.C = Color.white;
        }

        static void Counter(InteriorKit k, Mats M, Vector3 wallP, float len)
        {
            // bancada encostada à parede +x, ao longo de z
            float x1 = wallP.x, x0 = x1 - 0.6f, z0 = wallP.z - len * 0.5f, z1 = wallP.z + len * 0.5f, y = wallP.y;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(x0, y, z0), new Vector3(x1, y + 0.85f, z1));
            k.C = WoodLight;
            k.Box(M.madeira, new Vector3(x0 - 0.04f, y + 0.85f, z0 - 0.04f), new Vector3(x1, y + 0.9f, z1 + 0.04f));
            k.C = WoodMid;
            int nd = Mathf.RoundToInt(len / 0.65f);
            for (int i = 0; i < nd; i++)
            {
                float za = z0 + i * len / nd + 0.04f, zb = z0 + (i + 1) * len / nd - 0.04f;
                k.Box(M.madeira, new Vector3(x0 - 0.015f, y + 0.6f, za), new Vector3(x0, y + 0.8f, zb));
                k.Box(M.madeira, new Vector3(x0 - 0.015f, y + 0.08f, za), new Vector3(x0, y + 0.55f, zb));
                k.C = Brass;
                k.Sphere(M.pintado, new Vector3(x0 - 0.03f, y + 0.7f, (za + zb) * 0.5f), 0.02f, 6, 3);
                k.C = WoodMid;
            }
            // alguidar, tábua com pão, cântaro, balde
            k.C = new Color(0.72f, 0.42f, 0.26f);
            k.Lathe(M.pintado, new Vector3(x0 + 0.3f, y + 0.9f, z0 + 0.5f), new[] { new Vector2(0.12f, 0f), new Vector2(0.2f, 0.1f), new Vector2(0.22f, 0.12f) }, 12, true, true, false);
            k.C = WoodLight;
            k.Box(M.madeira, new Vector3(x0 + 0.12f, y + 0.9f, z0 + 1.1f), new Vector3(x0 + 0.48f, y + 0.93f, z0 + 1.55f));
            k.C = new Color(0.82f, 0.6f, 0.35f);
            k.Ellipsoid(M.pintado, new Vector3(x0 + 0.3f, y + 0.98f, z0 + 1.3f), new Vector3(0.11f, 0.06f, 0.15f), 8, 4);
            k.C = new Color(0.68f, 0.4f, 0.25f);
            k.Lathe(M.pintado, new Vector3(x0 + 0.3f, y + 0.9f, z1 - 0.45f), new[] { new Vector2(0.07f, 0f), new Vector2(0.12f, 0.12f), new Vector2(0.1f, 0.26f), new Vector2(0.05f, 0.32f), new Vector2(0.06f, 0.35f) }, 10, true, true, false);
            k.C = new Color(0.55f, 0.55f, 0.55f);
            k.Lathe(M.pintado, new Vector3(x0 - 0.35f, y, z1 - 0.2f), new[] { new Vector2(0.13f, 0f), new Vector2(0.16f, 0.3f), new Vector2(0.165f, 0.32f) }, 10, true, true, false);
            k.Push(new Vector3(x0 - 0.35f, y + 0.33f, z1 - 0.2f), 0f, 0f, 90f);
            k.Torus(M.pintado, Vector3.zero, 0.16f, 0.006f, 10, 3, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
            k.Pop();
            k.C = Color.white;
            Blob(k, M, x0 + 0.25f, y, wallP.z, 0.9f, len + 0.3f);
        }

        static void Table(InteriorKit k, Mats M, Vector3 b, float len, float dep, bool setTable)
        {
            k.Push(b, 0f);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-len * 0.5f, 0.72f, -dep * 0.5f), new Vector3(len * 0.5f, 0.78f, dep * 0.5f));
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-len * 0.5f + 0.08f, 0.62f, -dep * 0.5f + 0.08f), new Vector3(len * 0.5f - 0.08f, 0.72f, dep * 0.5f - 0.08f));
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    k.Lathe(M.madeira, new Vector3(sx * (len * 0.5f - 0.12f), 0f, sz * (dep * 0.5f - 0.12f)), new[]
                    {
                        new Vector2(0.035f, 0f), new Vector2(0.045f, 0.08f), new Vector2(0.03f, 0.2f), new Vector2(0.045f, 0.35f), new Vector2(0.03f, 0.5f), new Vector2(0.045f, 0.62f),
                    }, 8);
            k.Box(M.madeira, new Vector3(-len * 0.5f + 0.12f, 0.12f, -0.02f), new Vector3(len * 0.5f - 0.12f, 0.16f, 0.02f));
            if (setTable)
            {
                // toalha de linho a meio, pratos, canecas, fruteira, vela
                k.C = new Color(0.93f, 0.9f, 0.82f);
                k.Box(M.pintado, new Vector3(-len * 0.5f + 0.25f, 0.78f, -dep * 0.5f - 0.002f), new Vector3(len * 0.5f - 0.25f, 0.785f, dep * 0.5f + 0.002f));
                k.C = Red;
                k.Box(M.pintado, new Vector3(-len * 0.5f + 0.25f, 0.785f, -0.03f), new Vector3(len * 0.5f - 0.25f, 0.787f, 0.03f));
                foreach (var p in new[] { new Vector2(-0.55f, -0.27f), new Vector2(0.5f, -0.28f), new Vector2(-0.6f, 0.28f), new Vector2(0.75f, 0.0f) })
                {
                    k.C = new Color(0.95f, 0.95f, 0.92f);
                    k.Lathe(M.pintado, new Vector3(p.x, 0.785f, p.y), new[] { new Vector2(0.06f, 0f), new Vector2(0.11f, 0.015f), new Vector2(0.12f, 0.025f) }, 12, true, true, false);
                    k.C = new Color(0.5f, 0.3f, 0.2f);
                    k.Cyl(M.pintado, new Vector3(p.x + 0.17f, 0.785f, p.y + 0.06f), 0.035f, 0.09f, 8);
                }
                k.C = new Color(0.62f, 0.45f, 0.28f);
                k.Lathe(M.pintado, new Vector3(0f, 0.785f, 0.05f), new[] { new Vector2(0.06f, 0f), new Vector2(0.17f, 0.07f), new Vector2(0.18f, 0.09f) }, 12, true, true, false);
                var fruit = new[] { new Color(0.95f, 0.75f, 0.15f), new Color(0.85f, 0.2f, 0.15f), new Color(0.4f, 0.65f, 0.2f), new Color(0.95f, 0.55f, 0.1f) };
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 1.05f;
                    k.C = fruit[i % fruit.Length];
                    k.Sphere(M.pintado, new Vector3(Mathf.Cos(a) * 0.09f, 0.86f + (i == 5 ? 0.06f : 0f), 0.05f + Mathf.Sin(a) * 0.09f), 0.045f, 7, 4);
                }
                k.C = Brass;
                k.Lathe(M.pintado, new Vector3(-0.2f, 0.785f, -0.12f), new[] { new Vector2(0.06f, 0f), new Vector2(0.065f, 0.01f), new Vector2(0.015f, 0.03f), new Vector2(0.025f, 0.05f) }, 8, true, true, true);
                k.C = new Color(0.98f, 0.96f, 0.9f);
                k.Cyl(M.pintado, new Vector3(-0.2f, 0.835f, -0.12f), 0.016f, 0.14f, 6);
                k.C = Color.white;
                k.Ellipsoid(M.chama, new Vector3(-0.2f, 0.995f, -0.12f), new Vector3(0.01f, 0.025f, 0.01f), 5, 3);
            }
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, len + 0.3f, dep + 0.3f);
        }

        static void Chair(InteriorKit k, Mats M, Vector3 b, float yaw, Color c, bool cushion)
        {
            // cadeira de espaldar de travessas; o assento olha para +z local
            k.Push(b, yaw);
            k.C = c;
            float s = 0.42f;
            foreach (var x in new[] { -0.19f, 0.19f })
            {
                k.Lathe(M.madeira, new Vector3(x, 0f, 0.17f), new[] { new Vector2(0.022f, 0f), new Vector2(0.025f, 0.2f), new Vector2(0.02f, s) }, 6);
                k.Lathe(M.madeira, new Vector3(x, 0f, -0.18f), new[] { new Vector2(0.024f, 0f), new Vector2(0.025f, 0.5f), new Vector2(0.02f, 0.98f), new Vector2(0.03f, 1.0f), new Vector2(0f, 1.03f) }, 6);
            }
            k.Box(M.madeira, new Vector3(-0.22f, s, -0.2f), new Vector3(0.22f, s + 0.04f, 0.2f));
            for (int i = 0; i < 3; i++)
            {
                float y = 0.62f + i * 0.13f;
                k.Box(M.madeira, new Vector3(-0.18f, y, -0.195f), new Vector3(0.18f, y + 0.06f, -0.17f));
            }
            k.Box(M.madeira, new Vector3(-0.18f, 0.15f, -0.18f), new Vector3(0.18f, 0.18f, -0.16f));
            k.Box(M.madeira, new Vector3(-0.18f, 0.15f, 0.16f), new Vector3(0.18f, 0.18f, 0.18f));
            k.C = new Color(0.82f, 0.68f, 0.45f);
            // assento de palhinha
            for (float z = -0.18f; z < 0.18f; z += 0.04f)
                k.Box(M.pintado, new Vector3(-0.2f, s + 0.04f, z), new Vector3(0.2f, s + 0.05f, z + 0.03f));
            if (cushion)
            {
                k.C = Red;
                k.Box(M.pintado, new Vector3(-0.19f, s + 0.05f, -0.17f), new Vector3(0.19f, s + 0.1f, 0.19f));
            }
            k.C = Color.white;
            k.Col("Cadeira", new Vector3(-0.22f, 0f, -0.2f), new Vector3(0.22f, s + 0.05f, 0.2f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 0.6f, 0.6f, yaw);
        }

        static void Rug(InteriorKit k, Mats M, Vector3 c, float sx, float sz, int style)
        {
            Color[][] pal =
            {
                new[] { new Color(0.7f, 0.2f, 0.15f), new Color(0.9f, 0.75f, 0.35f), new Color(0.2f, 0.35f, 0.55f), new Color(0.9f, 0.88f, 0.8f) },
                new[] { new Color(0.25f, 0.45f, 0.4f), new Color(0.85f, 0.8f, 0.65f), new Color(0.6f, 0.25f, 0.2f), new Color(0.3f, 0.3f, 0.35f) },
                new[] { new Color(0.55f, 0.35f, 0.6f), new Color(0.95f, 0.85f, 0.6f), new Color(0.35f, 0.55f, 0.75f), new Color(0.85f, 0.4f, 0.3f) },
            };
            var p = pal[style % pal.Length];
            float y = c.y + 0.004f;
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                float z0 = c.z - sz * 0.5f + i * sz / n, z1 = z0 + sz / n;
                k.C = p[(i + (i / 3)) % p.Length];
                k.Box(M.pintado, new Vector3(c.x - sx * 0.5f, y, z0), new Vector3(c.x + sx * 0.5f, y + 0.008f, z1));
            }
            k.C = p[3];
            for (float x = c.x - sx * 0.5f; x < c.x + sx * 0.5f; x += 0.06f)
                foreach (var s in new[] { -1f, 1f })
                    k.Box(M.pintado, new Vector3(x, y, c.z + s * sz * 0.5f), new Vector3(x + 0.02f, y + 0.004f, c.z + s * (sz * 0.5f + 0.06f)));
            k.C = Color.white;
        }

        static void HangingLamp(InteriorKit k, Mats M, Vector3 hang, float drop)
        {
            var bot = hang - Vector3.up * drop;
            k.C = Iron;
            for (float y = 0f; y < drop - 0.1f; y += 0.08f)
            {
                k.Push(hang - Vector3.up * (y + 0.04f), ((int)(y / 0.08f)) % 2 == 0 ? 0f : 90f, 0f, 90f);
                k.Torus(M.pintado, Vector3.zero, 0.03f, 0.006f, 8, 3);
                k.Pop();
            }
            // candeeiro de petróleo: abajur de lata, chaminé de vidro, depósito
            k.C = new Color(0.25f, 0.35f, 0.3f);
            k.Lathe(M.pintado, bot, new[] { new Vector2(0.04f, 0f), new Vector2(0.18f, -0.08f), new Vector2(0.3f, -0.16f), new Vector2(0.31f, -0.18f) }, 14, true, false, false);
            k.Lathe(M.pintado, bot + Vector3.up * -0.005f, new[] { new Vector2(0.305f, -0.175f), new Vector2(0.3f, -0.16f), new Vector2(0.18f, -0.075f), new Vector2(0.035f, 0.005f) }, 14, true, false, false);
            k.C = Color.white;
            k.Lathe(M.vidroLuz, bot + Vector3.up * -0.32f, new[] { new Vector2(0.035f, 0f), new Vector2(0.06f, 0.06f), new Vector2(0.035f, 0.14f), new Vector2(0.03f, 0.25f) }, 10);
            k.C = Brass;
            k.Lathe(M.pintado, bot + Vector3.up * -0.44f, new[] { new Vector2(0f, 0f), new Vector2(0.07f, 0.03f), new Vector2(0.09f, 0.08f), new Vector2(0.05f, 0.12f) }, 10);
            k.Torus(M.pintado, bot + Vector3.up * -0.2f, 0.2f, 0.008f, 14, 3);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                k.Rod(M.pintado, bot + Vector3.up * -0.32f, bot + new Vector3(Mathf.Cos(a) * 0.2f, -0.2f, Mathf.Sin(a) * 0.2f), 0.006f, 3, false);
            }
            k.C = Color.white;
        }

        static void Cradle(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = WoodMid;
            // balancins
            foreach (var x in new[] { -0.42f, 0.42f })
            {
                k.Push(new Vector3(x, 0.45f, 0f), 0f, 0f, 90f);
                k.Torus(M.madeira, Vector3.zero, 0.45f, 0.025f, 12, 4, Mathf.PI * 0.62f, Mathf.PI * 1.38f);
                k.Pop();
            }
            k.Box(M.madeira, new Vector3(-0.45f, 0.14f, -0.32f), new Vector3(0.45f, 0.18f, 0.32f));
            k.Box(M.madeira, new Vector3(-0.45f, 0.18f, -0.32f), new Vector3(0.45f, 0.48f, -0.29f));
            k.Box(M.madeira, new Vector3(-0.45f, 0.18f, 0.29f), new Vector3(0.45f, 0.48f, 0.32f));
            k.Box(M.madeira, new Vector3(-0.48f, 0.18f, -0.32f), new Vector3(-0.45f, 0.62f, 0.32f));
            k.Box(M.madeira, new Vector3(0.45f, 0.18f, -0.32f), new Vector3(0.48f, 0.52f, 0.32f));
            k.Push(new Vector3(-0.465f, 0.62f, 0f), -90f);
            k.ArchRing(M.madeira, Vector2.zero, 0.28f, 0.32f, 0f, 180f, 8, -0.015f, 0.015f);
            k.Pop();
            // grade lateral
            for (float x = -0.38f; x < 0.4f; x += 0.1f)
                foreach (var z in new[] { -0.305f, 0.305f })
                    k.Rod(M.madeira, new Vector3(x, 0.48f, z), new Vector3(x, 0.58f, z), 0.012f, 4);
            foreach (var z in new[] { -0.305f, 0.305f })
                k.Box(M.madeira, new Vector3(-0.45f, 0.57f, z - 0.02f), new Vector3(0.45f, 0.6f, z + 0.02f));
            // colchão, mantinha e almofada
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pintado, new Vector3(-0.44f, 0.18f, -0.28f), new Vector3(0.44f, 0.3f, 0.28f));
            k.C = new Color(0.85f, 0.65f, 0.7f);
            k.Box(M.pintado, new Vector3(-0.1f, 0.3f, -0.28f), new Vector3(0.44f, 0.36f, 0.28f));
            k.C = new Color(0.97f, 0.96f, 0.93f);
            k.Ellipsoid(M.pintado, new Vector3(-0.3f, 0.34f, 0f), new Vector3(0.1f, 0.05f, 0.18f), 8, 4);
            k.C = Color.white;
            k.Col("Berco", new Vector3(-0.48f, 0f, -0.33f), new Vector3(0.48f, 0.62f, 0.33f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.2f, 0.9f, yaw);
        }

        static void RockingChair(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = WoodDark;
            foreach (var x in new[] { -0.24f, 0.24f })
            {
                k.Push(new Vector3(x, 0.9f, -0.05f), 0f, 0f, 90f);
                k.Torus(M.madeira, Vector3.zero, 0.9f, 0.02f, 14, 4, Mathf.PI * 0.8f, Mathf.PI * 1.2f);
                k.Pop();
                k.Rod(M.madeira, new Vector3(x, 0.03f, 0.2f), new Vector3(x, 0.45f, 0.22f), 0.022f, 6);
                k.Rod(M.madeira, new Vector3(x, 0.03f, -0.25f), new Vector3(x, 1.1f, -0.38f), 0.024f, 6);
                k.Rod(M.madeira, new Vector3(x, 0.66f, 0.22f), new Vector3(x, 0.68f, -0.3f), 0.02f, 6);
                k.Rod(M.madeira, new Vector3(x, 0.45f, 0.22f), new Vector3(x, 0.66f, 0.22f), 0.016f, 5);
            }
            k.Box(M.madeira, new Vector3(-0.25f, 0.43f, -0.26f), new Vector3(0.25f, 0.47f, 0.24f));
            for (int i = 0; i < 6; i++)
            {
                float x = -0.18f + i * 0.072f;
                k.Rod(M.madeira, new Vector3(x, 0.47f, -0.28f), new Vector3(x, 1.05f, -0.37f), 0.012f, 4);
            }
            k.Rod(M.madeira, new Vector3(-0.26f, 1.06f, -0.37f), new Vector3(0.26f, 1.06f, -0.37f), 0.03f, 6);
            // manta de croché
            k.C = new Color(0.85f, 0.45f, 0.3f);
            k.Push(new Vector3(0f, 0.9f, -0.36f), 0f, 12f, 0f);
            k.Box(M.pintado, new Vector3(-0.22f, -0.3f, 0.01f), new Vector3(0.22f, 0.1f, 0.03f));
            k.Pop();
            k.C = Color.white;
            k.Col("Cadeira", new Vector3(-0.28f, 0f, -0.35f), new Vector3(0.28f, 0.5f, 0.28f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 0.8f, 0.9f, yaw);
        }

        static void Chest(InteriorKit k, Mats M, Vector3 b, float yaw, float len, Color c)
        {
            k.Push(b, yaw);
            float d = 0.55f, hh = 0.45f;
            k.C = c;
            k.Box(M.madeira, new Vector3(-len * 0.5f, 0.04f, -d * 0.5f), new Vector3(len * 0.5f, hh, d * 0.5f));
            k.Push(new Vector3(-len * 0.5f, hh, 0f), 0f, 0f, -90f);
            k.Lathe(M.madeira, Vector3.zero, new[] { new Vector2(0f, 0f), new Vector2(d * 0.5f, 0f), new Vector2(d * 0.5f, len), new Vector2(0f, len) }, 12, true, false, false);
            k.Pop();
            // tira a metade de baixo do cilindro da tampa: tampa abaulada — o cilindro meio enterrado na caixa dá esse efeito
            k.C = Iron;
            foreach (var x in new[] { -len * 0.5f + 0.12f, len * 0.5f - 0.12f })
            {
                k.Box(M.pintado, new Vector3(x - 0.03f, 0.04f, -d * 0.5f - 0.008f), new Vector3(x + 0.03f, hh, -d * 0.5f));
                k.Push(new Vector3(x, hh, 0f), 0f, 0f, -90f);
                k.Torus(M.pintado, Vector3.zero, d * 0.5f + 0.005f, 0.012f, 12, 3, Mathf.PI * 0.5f, Mathf.PI * 1.5f);
                k.Pop();
            }
            k.Box(M.pintado, new Vector3(-0.06f, hh - 0.14f, -d * 0.5f - 0.012f), new Vector3(0.06f, hh + 0.02f, -d * 0.5f));
            foreach (var s in new[] { -1f, 1f })
            {
                k.Push(new Vector3(s * (len * 0.5f + 0.02f), hh - 0.08f, 0f), 0f, 0f, 90f);
                k.Torus(M.pintado, Vector3.zero, 0.06f, 0.01f, 8, 3, Mathf.PI * 0.5f, Mathf.PI * 1.5f);
                k.Pop();
            }
            k.Box(M.pintado, new Vector3(-len * 0.5f, 0f, -d * 0.5f), new Vector3(len * 0.5f, 0.04f, d * 0.5f));
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, len + 0.3f, 0.85f, yaw);
        }

        static void Cabinet(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            float w = 1.25f, d = 0.45f, hh = 2.0f;
            k.C = new Color(0.62f, 0.75f, 0.72f);   // cristaleira pintada (verde-água desbotado)
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0f, -d * 0.5f), new Vector3(w * 0.5f, 0.85f, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f + 0.04f, 0.85f, -d * 0.5f + 0.15f), new Vector3(w * 0.5f - 0.04f, hh, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f - 0.04f, hh, -d * 0.5f + 0.1f), new Vector3(w * 0.5f + 0.04f, hh + 0.08f, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f - 0.03f, 0.85f, -d * 0.5f - 0.03f), new Vector3(w * 0.5f + 0.03f, 0.89f, d * 0.5f));
            k.C = new Color(0.5f, 0.62f, 0.6f);
            foreach (var s in new[] { -1f, 1f })
                k.Box(M.madeira, new Vector3(s > 0 ? 0.02f : -w * 0.5f + 0.06f, 0.1f, -d * 0.5f - 0.015f), new Vector3(s > 0 ? w * 0.5f - 0.06f : -0.02f, 0.75f, -d * 0.5f));
            // prateleiras visíveis com louça (sem vidro)
            k.C = new Color(0.35f, 0.42f, 0.42f);
            k.Box(M.pintado, new Vector3(-w * 0.5f + 0.06f, 0.9f, -d * 0.5f + 0.16f + 0.27f), new Vector3(w * 0.5f - 0.06f, hh - 0.04f, d * 0.5f - 0.01f));
            for (int s = 0; s < 3; s++)
            {
                float y = 1.15f + s * 0.28f;
                k.C = new Color(0.58f, 0.7f, 0.67f);
                k.Box(M.madeira, new Vector3(-w * 0.5f + 0.06f, y - 0.02f, -d * 0.5f + 0.16f), new Vector3(w * 0.5f - 0.06f, y, d * 0.5f));
                for (int i = 0; i < 5; i++)
                {
                    float x = -0.45f + i * 0.22f;
                    k.C = (i + s) % 3 == 0 ? new Color(0.25f, 0.45f, 0.8f) : new Color(0.96f, 0.96f, 0.94f);
                    k.Push(new Vector3(x, y + 0.1f, d * 0.5f - 0.06f), 0f, -80f, 0f);
                    k.Cyl(M.pintado, Vector3.zero, 0.09f, 0.012f, 12);
                    k.Pop();
                }
            }
            // caixilhos das portas de cima
            k.C = new Color(0.62f, 0.75f, 0.72f);
            foreach (var x in new[] { -w * 0.5f + 0.06f, -0.015f, w * 0.5f - 0.09f })
                k.Box(M.madeira, new Vector3(x, 0.9f, -d * 0.5f + 0.13f), new Vector3(x + 0.03f, hh - 0.02f, -d * 0.5f + 0.16f));
            k.C = Brass;
            foreach (var x in new[] { -0.06f, 0.06f })
                k.Sphere(M.pintado, new Vector3(x, 0.55f, -d * 0.5f - 0.03f), 0.018f, 6, 3);
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.5f, 0.8f, yaw);
        }

        static void Barrel(InteriorKit k, Mats M, Vector3 b, float r, float hh)
        {
            k.C = WoodMid;
            k.Lathe(M.madeira, b, new[] { new Vector2(r * 0.85f, 0f), new Vector2(r, hh * 0.3f), new Vector2(r * 1.04f, hh * 0.5f), new Vector2(r, hh * 0.7f), new Vector2(r * 0.85f, hh) }, 14);
            k.C = Iron;
            foreach (var t in new[] { 0.08f, 0.3f, 0.7f, 0.92f })
            {
                float rr = Mathf.Lerp(r * 0.87f, r * 1.04f, 1f - Mathf.Abs(t - 0.5f) * 2f) + 0.008f;
                k.Cyl(M.pintado, b + Vector3.up * (hh * t - 0.02f), rr, 0.04f, 14, true, false);
            }
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, r * 2.8f, r * 2.8f);
        }

        static void Crate(InteriorKit k, Mats M, Vector3 b, float s, float yaw)
        {
            k.Push(b, yaw);
            k.C = WoodLight;
            k.Box(M.madeira, new Vector3(-s * 0.5f, 0f, -s * 0.5f), new Vector3(s * 0.5f, s, s * 0.5f));
            k.C = WoodMid;
            foreach (var sz in new[] { -1f, 1f })
            {
                k.Box(M.madeira, new Vector3(-s * 0.5f, 0f, sz * s * 0.5f - 0.01f), new Vector3(-s * 0.5f + 0.06f, s, sz * s * 0.5f + 0.01f));
                k.Box(M.madeira, new Vector3(s * 0.5f - 0.06f, 0f, sz * s * 0.5f - 0.01f), new Vector3(s * 0.5f, s, sz * s * 0.5f + 0.01f));
                k.Push(new Vector3(0f, s * 0.5f, sz * (s * 0.5f + 0.012f)), 0f, 0f, 45f);
                k.Box(M.madeira, new Vector3(-s * 0.68f, -0.03f, -0.006f), new Vector3(s * 0.68f, 0.03f, 0.006f));
                k.Pop();
            }
            k.C = Color.white;
            k.Pop();
            if (b.y < 0.1f || b.y > 3f) Blob(k, M, b.x, b.y, b.z, s + 0.25f, s + 0.25f, yaw);
        }

        static void WallClock(InteriorKit k, Mats M, Vector3 p, float yaw)
        {
            k.Push(p, yaw);
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.18f, -0.55f, 0f), new Vector3(0.18f, 0.25f, 0.12f));
            k.Push(new Vector3(0f, 0.25f, 0.06f), 0f, 0f, 0f);
            k.Prism(M.madeira, new[] { new Vector2(-0.2f, 0f), new Vector2(0f, 0.14f), new Vector2(0.2f, 0f) }, -0.06f, 0.07f);
            k.Pop();
            k.C = new Color(0.95f, 0.92f, 0.82f);
            k.Push(new Vector3(0f, 0.05f, 0.12f), 0f, 90f, 0f);
            k.Cyl(M.pintado, Vector3.zero, 0.13f, 0.01f, 14);
            k.Pop();
            k.C = Iron;
            k.Box(M.pintado, new Vector3(-0.005f, 0.05f, 0.13f), new Vector3(0.005f, 0.15f, 0.135f));
            k.Box(M.pintado, new Vector3(0f, 0.045f, 0.13f), new Vector3(0.07f, 0.055f, 0.135f));
            k.C = Brass;
            k.Rod(M.pintado, new Vector3(0f, -0.1f, 0.08f), new Vector3(0.03f, -0.42f, 0.08f), 0.005f, 3);
            k.Push(new Vector3(0.03f, -0.44f, 0.08f), 0f, 90f, 0f);
            k.Cyl(M.pintado, Vector3.zero, 0.045f, 0.01f, 10);
            k.Pop();
            k.C = Color.white;
            k.Pop();
        }

        static void CoatHooks(InteriorKit k, Mats M, Vector3 p, float yaw)
        {
            k.Push(p, yaw);
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.5f, -0.05f, 0f), new Vector3(0.5f, 0.05f, 0.03f));
            k.C = Iron;
            for (int i = 0; i < 4; i++)
                k.Rod(M.pintado, new Vector3(-0.36f + i * 0.24f, 0f, 0.03f), new Vector3(-0.36f + i * 0.24f, 0.05f, 0.12f), 0.01f, 4);
            // casaco e chapéu de palha
            k.C = new Color(0.35f, 0.3f, 0.25f);
            k.Push(new Vector3(-0.36f, -0.05f, 0.1f), 0f);
            k.Lathe(M.pintado, Vector3.zero, new[] { new Vector2(0.06f, 0f), new Vector2(0.17f, -0.15f), new Vector2(0.2f, -0.8f), new Vector2(0.18f, -0.82f) }, 8, true, false, true);
            k.Pop();
            k.C = new Color(0.88f, 0.78f, 0.5f);
            k.Lathe(M.pintado, new Vector3(0.12f, -0.12f, 0.12f), new[] { new Vector2(0.22f, 0f), new Vector2(0.22f, 0.015f), new Vector2(0.11f, 0.02f), new Vector2(0.1f, 0.12f), new Vector2(0f, 0.13f) }, 12);
            k.C = Color.white;
            k.Pop();
        }

        static void Broom(InteriorKit k, Mats M, Vector3 b)
        {
            k.Push(b, 0f, 0f, 8f);
            k.C = WoodLight;
            k.Cyl(M.madeira, new Vector3(0f, 0.3f, 0f), 0.015f, 1.1f, 6);
            k.C = new Color(0.8f, 0.68f, 0.35f);
            k.Cone(M.pintado, Vector3.zero, 0.13f, 0.04f, 0.32f, 8);
            k.C = Color.white;
            k.Pop();
        }

        static void Picture(InteriorKit k, Mats M, Vector3 p, float yaw, int kind)
        {
            k.Push(p, yaw);
            k.C = kind == 1 ? Gold : WoodDark;
            k.Box(M.madeira, new Vector3(-0.3f, -0.22f, 0f), new Vector3(0.3f, 0.22f, 0.04f));
            Color[] sky = { new Color(0.55f, 0.75f, 0.9f), new Color(0.9f, 0.85f, 0.7f), new Color(0.75f, 0.85f, 0.95f) };
            k.C = sky[kind % 3];
            k.Box(M.pintado, new Vector3(-0.25f, -0.17f, 0.04f), new Vector3(0.25f, 0.17f, 0.045f));
            if (kind == 1)
            {
                k.C = new Color(0.35f, 0.25f, 0.2f);   // retrato
                k.Ellipsoid(M.pintado, new Vector3(0f, -0.04f, 0.045f), new Vector3(0.1f, 0.12f, 0.005f), 8, 3);
                k.C = Skin;
                k.Ellipsoid(M.pintado, new Vector3(0f, 0.06f, 0.047f), new Vector3(0.05f, 0.065f, 0.005f), 8, 3);
            }
            else
            {
                k.C = new Color(0.3f, 0.45f, 0.3f);   // paisagem: montes e mar
                k.Prism(M.pintado, new[] { new Vector2(-0.25f, -0.17f), new Vector2(-0.1f, 0.04f), new Vector2(0.05f, -0.06f), new Vector2(0.25f, 0.02f), new Vector2(0.25f, -0.17f) }, 0.045f, 0.05f);
                k.C = new Color(0.2f, 0.4f, 0.65f);
                k.Box(M.pintado, new Vector3(-0.25f, -0.17f, 0.05f), new Vector3(0.25f, -0.11f, 0.052f));
            }
            k.C = Color.white;
            k.Pop();
        }

        static void Plant(InteriorKit k, Mats M, Vector3 b, int seed)
        {
            var rng = new System.Random(seed + 101);
            k.C = new Color(0.72f, 0.38f, 0.24f);
            k.Lathe(M.pintado, b, new[] { new Vector2(0.06f, 0f), new Vector2(0.085f, 0.13f), new Vector2(0.095f, 0.15f) }, 8, true, true, true);
            k.C = new Color(0.3f, 0.2f, 0.12f);
            k.Disc(M.pintado, b + Vector3.up * 0.13f, 0.08f, 8, true);
            int n = 5 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var tip = b + new Vector3(Mathf.Cos(a) * 0.12f, 0.25f + 0.15f * (float)rng.NextDouble(), Mathf.Sin(a) * 0.08f);
                k.C = new Color(0.22f + 0.1f * (float)rng.NextDouble(), 0.5f + 0.15f * (float)rng.NextDouble(), 0.2f);
                k.Rod(M.pintado, b + Vector3.up * 0.13f, tip, 0.006f, 3, false);
                k.Ellipsoid(M.pintado, tip, new Vector3(0.05f, 0.025f, 0.035f), 6, 3);
            }
            if (rng.Next(2) == 0)
            {
                k.C = new Color(0.95f, 0.3f, 0.35f);
                k.Sphere(M.pintado, b + new Vector3(0.02f, 0.38f, 0f), 0.035f, 6, 3);
            }
            k.C = Color.white;
        }

        static void Bed(InteriorKit k, Mats M, Vector3 headWall, float yaw, float w = 1.6f)
        {
            // cabeceira encostada à parede do fundo; cama para −z
            k.Push(headWall, yaw);
            float len = 2.0f;
            k.C = WoodDark;
            foreach (var x in new[] { -w * 0.5f, w * 0.5f })
            {
                k.Lathe(M.madeira, new Vector3(x, 0f, -0.05f), new[] { new Vector2(0.04f, 0f), new Vector2(0.045f, 0.1f), new Vector2(0.035f, 1.25f), new Vector2(0.06f, 1.3f), new Vector2(0f, 1.38f) }, 8);
                k.Lathe(M.madeira, new Vector3(x, 0f, -len), new[] { new Vector2(0.04f, 0f), new Vector2(0.045f, 0.1f), new Vector2(0.035f, 0.72f), new Vector2(0.06f, 0.76f), new Vector2(0f, 0.84f) }, 8);
            }
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0.55f, -0.08f), new Vector3(w * 0.5f, 1.15f, -0.04f));
            k.ArchRing(M.madeira, new Vector2(0f, 1.0f), 0.45f, 0.52f, 20f, 160f, 10, -0.08f, -0.04f);
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0.35f, -len - 0.02f), new Vector3(w * 0.5f, 0.7f, -len + 0.02f));
            foreach (var x in new[] { -w * 0.5f, w * 0.5f })
                k.Box(M.madeira, new Vector3(x - 0.03f, 0.22f, -len), new Vector3(x + 0.03f, 0.38f, -0.05f));
            // colchão, lençol, colcha de retalhos, almofadas
            k.C = new Color(0.95f, 0.94f, 0.9f);
            k.Box(M.pintado, new Vector3(-w * 0.5f + 0.04f, 0.3f, -len + 0.03f), new Vector3(w * 0.5f - 0.04f, 0.52f, -0.08f));
            Color[] patch = { new Color(0.8f, 0.25f, 0.2f), new Color(0.95f, 0.8f, 0.4f), new Color(0.25f, 0.45f, 0.7f), new Color(0.9f, 0.9f, 0.85f), new Color(0.35f, 0.6f, 0.35f), new Color(0.85f, 0.55f, 0.65f) };
            int nx = 5, nz = 6;
            float qz0 = -len - 0.01f, qz1 = -0.6f;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    k.C = patch[(i * 3 + j * 2 + (i * j) % 3) % patch.Length];
                    float x0 = -w * 0.5f - 0.02f + i * (w + 0.04f) / nx, x1 = x0 + (w + 0.04f) / nx;
                    float z0 = qz0 + j * (qz1 - qz0) / nz, z1 = z0 + (qz1 - qz0) / nz;
                    k.Box(M.pintado, new Vector3(x0, 0.52f, z0), new Vector3(x1, 0.56f, z1));
                }
            k.C = patch[0];
            foreach (var x in new[] { -w * 0.5f - 0.03f, w * 0.5f + 0.01f })
                k.Box(M.pintado, new Vector3(x, 0.28f, qz0), new Vector3(x + 0.02f, 0.56f, qz1));
            k.Box(M.pintado, new Vector3(-w * 0.5f - 0.03f, 0.28f, qz0 - 0.02f), new Vector3(w * 0.5f + 0.03f, 0.56f, qz0));
            k.C = new Color(0.97f, 0.96f, 0.93f);
            k.Box(M.pintado, new Vector3(-w * 0.5f + 0.04f, 0.52f, qz1), new Vector3(w * 0.5f - 0.04f, 0.565f, qz1 + 0.15f));
            foreach (var x in w > 1.2f ? new[] { -w * 0.24f, w * 0.24f } : new[] { 0f })
                k.Ellipsoid(M.pintado, new Vector3(x, 0.6f, -0.32f), new Vector3(Mathf.Min(0.3f, w * 0.32f), 0.08f, 0.17f), 10, 4);
            k.C = Color.white;
            k.Pop();
            Blob(k, M, headWall.x, headWall.y, headWall.z - 1.0f, 2.0f, 2.4f, yaw);
        }

        static void Nightstand(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = WoodMid;
            k.BoxB(M.madeira, b, new Vector3(0.45f, 0.6f, 0.4f));
            k.C = WoodDark;
            k.BoxB(M.madeira, b + Vector3.up * 0.6f, new Vector3(0.5f, 0.03f, 0.44f));
            k.Box(M.madeira, b + new Vector3(-0.19f, 0.38f, -0.215f), b + new Vector3(0.19f, 0.55f, -0.2f));
            k.C = Brass;
            k.Sphere(M.pintado, b + new Vector3(0f, 0.465f, -0.23f), 0.018f, 6, 3);
            // castiçal de mão com vela
            k.Cyl(M.pintado, b + Vector3.up * 0.63f, 0.07f, 0.012f, 10);
            k.Cyl(M.pintado, b + Vector3.up * 0.64f, 0.02f, 0.03f, 6);
            k.C = new Color(0.98f, 0.96f, 0.9f);
            k.Cyl(M.pintado, b + Vector3.up * 0.67f, 0.016f, 0.12f, 6);
            k.C = Color.white;
            k.Ellipsoid(M.chama, b + Vector3.up * 0.81f, new Vector3(0.01f, 0.024f, 0.01f), 5, 3);
            Blob(k, M, b.x, b.y, b.z, 0.65f, 0.6f);
        }

        static void Wardrobe(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            float w = 1.5f, d = 0.6f, hh = 2.1f;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0.08f, -d * 0.5f), new Vector3(w * 0.5f, hh, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f - 0.05f, hh, -d * 0.5f - 0.05f), new Vector3(w * 0.5f + 0.05f, hh + 0.12f, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f + 0.02f, 0f, -d * 0.5f + 0.02f), new Vector3(w * 0.5f - 0.02f, 0.08f, d * 0.5f));
            k.C = WoodMid;
            foreach (var s in new[] { -1f, 1f })
            {
                float x0 = s > 0 ? 0.015f : -w * 0.5f + 0.05f, x1 = s > 0 ? w * 0.5f - 0.05f : -0.015f;
                k.Box(M.madeira, new Vector3(x0, 0.2f, -d * 0.5f - 0.015f), new Vector3(x1, hh - 0.1f, -d * 0.5f));
                k.Box(M.madeira, new Vector3(x0 + 0.08f, 0.35f, -d * 0.5f - 0.03f), new Vector3(x1 - 0.08f, 0.95f, -d * 0.5f - 0.015f));
                k.Box(M.madeira, new Vector3(x0 + 0.08f, 1.1f, -d * 0.5f - 0.03f), new Vector3(x1 - 0.08f, hh - 0.25f, -d * 0.5f - 0.015f));
            }
            k.C = Brass;
            foreach (var x in new[] { -0.06f, 0.06f })
                k.Sphere(M.pintado, new Vector3(x, 1.05f, -d * 0.5f - 0.03f), 0.02f, 6, 3);
            // maleta em cima
            k.C = new Color(0.45f, 0.3f, 0.2f);
            k.Box(M.pintado, new Vector3(-0.4f, hh + 0.12f, -0.2f), new Vector3(0.2f, hh + 0.32f, 0.2f));
            k.C = Color.white;
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.8f, 0.95f, yaw);
        }

        static void Washstand(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = WoodMid;
            foreach (var x in new[] { -0.3f, 0.3f })
                foreach (var z in new[] { -0.2f, 0.2f })
                    k.BoxB(M.madeira, b + new Vector3(x, 0f, z), new Vector3(0.05f, 0.78f, 0.05f));
            k.BoxB(M.madeira, b + new Vector3(0f, 0.78f, 0f), new Vector3(0.7f, 0.04f, 0.5f));
            k.BoxB(M.madeira, b + new Vector3(0f, 0.15f, 0f), new Vector3(0.6f, 0.03f, 0.4f));
            k.BoxB(M.madeira, b + new Vector3(0f, 0.82f, 0.22f), new Vector3(0.7f, 0.12f, 0.03f));
            k.C = new Color(0.96f, 0.96f, 0.94f);
            k.Lathe(M.pintado, b + Vector3.up * 0.82f, new[] { new Vector2(0.08f, 0f), new Vector2(0.2f, 0.07f), new Vector2(0.22f, 0.09f) }, 12, true, true, false);
            k.Lathe(M.pintado, b + new Vector3(0.12f, 0.84f, 0.05f), new[] { new Vector2(0.06f, 0f), new Vector2(0.08f, 0.1f), new Vector2(0.05f, 0.22f), new Vector2(0.07f, 0.28f) }, 10, true, true, false);
            k.C = new Color(0.25f, 0.45f, 0.75f);
            k.Torus(M.pintado, b + Vector3.up * 0.9f, 0.215f, 0.006f, 14, 3);
            // espelho na parede
            k.C = WoodDark;
            k.Box(M.madeira, b + new Vector3(-0.25f, 1.15f, 0.22f), b + new Vector3(0.25f, 1.75f, 0.25f));
            k.C = new Color(0.7f, 0.78f, 0.82f);
            k.Box(M.pintado, b + new Vector3(-0.2f, 1.2f, 0.215f), b + new Vector3(0.2f, 1.7f, 0.22f));
            // toalha pendurada
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pintado, b + new Vector3(0.36f, 0.4f, -0.12f), b + new Vector3(0.37f, 0.76f, 0.12f));
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.9f, 0.7f);
        }

        static void SpinningWheel(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-0.5f, 0.3f, -0.08f), new Vector3(0.45f, 0.36f, 0.08f));
            foreach (var p in new[] { new Vector3(-0.42f, 0f, -0.15f), new Vector3(-0.42f, 0f, 0.15f), new Vector3(0.38f, 0f, 0f) })
                k.Rod(M.madeira, p, new Vector3(p.x * 0.9f, 0.33f, p.z * 0.4f), 0.02f, 5);
            foreach (var z in new[] { -0.05f, 0.05f })
                k.Rod(M.madeira, new Vector3(-0.12f, 0.36f, z), new Vector3(-0.12f, 0.95f, z), 0.018f, 5);
            k.Push(new Vector3(-0.12f, 0.9f, 0f), 0f, 90f, 0f);
            k.Torus(M.madeira, Vector3.zero, 0.36f, 0.025f, 20, 4);
            k.Cyl(M.madeira, new Vector3(0f, -0.04f, 0f), 0.05f, 0.08f, 8);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5f;
                k.Rod(M.madeira, Vector3.zero, new Vector3(Mathf.Cos(a) * 0.35f, 0f, Mathf.Sin(a) * 0.35f), 0.008f, 3, false);
            }
            k.Pop();
            k.Rod(M.madeira, new Vector3(0.3f, 0.36f, 0f), new Vector3(0.32f, 0.85f, 0f), 0.018f, 5);
            k.C = new Color(0.92f, 0.88f, 0.78f);
            k.Ellipsoid(M.pintado, new Vector3(0.32f, 0.8f, 0f), new Vector3(0.05f, 0.08f, 0.05f), 8, 4);
            k.C = new Color(0.85f, 0.8f, 0.7f);
            k.Rod(M.pintado, new Vector3(-0.12f, 1.25f, 0.0f), new Vector3(0.32f, 0.8f, 0.0f), 0.003f, 3, false);
            k.C = Color.white;
            k.Col("Roca", new Vector3(-0.55f, 0f, -0.2f), new Vector3(0.45f, 1.0f, 0.2f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.1f, 0.6f, yaw);
        }

        static void Basket(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = new Color(0.78f, 0.62f, 0.38f);
            k.Lathe(M.pintado, b, new[] { new Vector2(0.15f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.21f, 0.22f) }, 12, true, true, false);
            Color[] wool = { new Color(0.9f, 0.88f, 0.82f), new Color(0.7f, 0.25f, 0.25f), new Color(0.35f, 0.45f, 0.65f) };
            for (int i = 0; i < 3; i++)
            {
                k.C = wool[i];
                k.Sphere(M.pintado, b + new Vector3(Mathf.Cos(i * 2.1f) * 0.08f, 0.22f, Mathf.Sin(i * 2.1f) * 0.08f), 0.075f, 8, 4);
            }
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.55f, 0.55f);
        }

        static void Desk(InteriorKit k, Mats M, Vector3 b)
        {
            k.C = WoodDark;
            k.Box(M.madeira, b + new Vector3(-0.65f, 0.72f, -0.32f), b + new Vector3(0.65f, 0.76f, 0.32f));
            foreach (var x in new[] { -0.6f, 0.6f })
                foreach (var z in new[] { -0.27f, 0.27f })
                    k.BoxB(M.madeira, b + new Vector3(x, 0f, z), new Vector3(0.05f, 0.72f, 0.05f));
            k.Box(M.madeira, b + new Vector3(-0.6f, 0.58f, -0.3f), b + new Vector3(0.6f, 0.72f, 0.3f));
            k.C = WoodMid;
            k.Box(M.madeira, b + new Vector3(-0.25f, 0.6f, -0.31f), b + new Vector3(0.25f, 0.7f, -0.3f));
            // livro aberto, tinteiro com pena, castiçal
            k.C = new Color(0.97f, 0.95f, 0.87f);
            k.Push(b + new Vector3(0f, 0.76f, -0.05f), 12f);
            k.Box(M.pintado, new Vector3(-0.2f, 0f, -0.13f), new Vector3(0f, 0.025f, 0.13f));
            k.Box(M.pintado, new Vector3(0f, 0f, -0.13f), new Vector3(0.2f, 0.03f, 0.13f));
            k.Pop();
            k.C = new Color(0.1f, 0.1f, 0.15f);
            k.Cyl(M.pintado, b + new Vector3(0.38f, 0.76f, 0.1f), 0.035f, 0.05f, 8);
            k.C = new Color(0.95f, 0.95f, 0.92f);
            k.Rod(M.pintado, b + new Vector3(0.38f, 0.8f, 0.1f), b + new Vector3(0.45f, 1.0f, 0.15f), 0.006f, 3);
            k.C = Brass;
            k.Cyl(M.pintado, b + new Vector3(-0.45f, 0.76f, 0.1f), 0.06f, 0.012f, 8);
            k.C = new Color(0.98f, 0.96f, 0.9f);
            k.Cyl(M.pintado, b + new Vector3(-0.45f, 0.77f, 0.1f), 0.016f, 0.1f, 6);
            k.C = Color.white;
            k.Ellipsoid(M.chama, b + new Vector3(-0.45f, 0.89f, 0.1f), new Vector3(0.01f, 0.024f, 0.01f), 5, 3);
            Blob(k, M, b.x, b.y, b.z, 1.5f, 0.8f);
        }

        // =====================================================================================================
        // fachada da igreja: tira a porta e o portal maciços do modelo do Blender

        static void OpenChurchFacade(Transform fach, StringBuilder log)
        {
            var mf = fach.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var src = mf.sharedMesh;
            string path = MeshFolder + "/CVI_Igreja_Fachada.asset";
            if (AssetDatabase.GetAssetPath(src) == path) { log.AppendLine("Fachada da igreja já estava aberta."); return; }
            var v = src.vertices;
            var mesh = new Mesh { name = "CVI_Igreja_Fachada" };
            mesh.vertices = v;
            mesh.normals = src.normals;
            if (src.uv.Length == v.Length) mesh.uv = src.uv;
            if (src.colors.Length == v.Length) mesh.colors = src.colors;
            mesh.subMeshCount = src.subMeshCount;
            var mats = fach.GetComponent<MeshRenderer>().sharedMaterials;
            int removed = 0;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tri = src.GetTriangles(s);
                var keep = new List<int>();
                string mn = s < mats.Length && mats[s] != null ? mats[s].name : "";
                for (int i = 0; i < tri.Length; i += 3)
                {
                    var c = fach.TransformPoint((v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f);
                    bool door = mn.Contains("Madeira_Verde");
                    bool portal = mn.Contains("Pedra_Clara") && c.y < 3.7f && Mathf.Abs(c.x) < 1.5f;
                    if (door || portal) { removed++; continue; }
                    keep.Add(tri[i]);
                    keep.Add(tri[i + 1]);
                    keep.Add(tri[i + 2]);
                }
                mesh.SetTriangles(keep, s);
            }
            mesh.RecalculateBounds();
            mf.sharedMesh = SaveMesh(mesh);
            log.AppendLine($"Fachada da igreja: tirados {removed} triângulos (porta e portal maciços).");
        }

        // =====================================================================================================
        // colisores, luzes, materiais, texturas

        static void MakeColliders(Transform parent, InteriorKit k, int layer)
        {
            var g = new GameObject("Colisores").transform;
            g.SetParent(parent, false);
            g.gameObject.layer = layer;
            int i = 0;
            foreach (var c in k.Cols)
            {
                var go = new GameObject("COL_" + c.name + "_" + i++);
                go.layer = layer;
                go.transform.SetParent(g, false);
                go.transform.position = c.center;
                go.transform.rotation = c.rot;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(Mathf.Abs(c.size.x), Mathf.Abs(c.size.y), Mathf.Abs(c.size.z));
            }
        }

        /// <summary>Quem projeta sombra do sol (malhas fechadas: basta uma face).</summary>
        static ShadowCastingMode CasterMode(Mats M, Material m)
        {
            // só a estrutura (paredes, cantaria, telhado, troncos) projeta sombra do sol; a mobília fica dentro de casa,
            // onde o sol quase não chega, e cada malha a projetar é redesenhada em todas as cascatas de sombra
            if (m == M.reboco || m == M.ocre || m == M.terracota || m == M.rosa || m == M.azulRb || m == M.pedraMuro ||
                m == M.pedraClara || m == M.telha || m == M.troncos)
                return ShadowCastingMode.On;
            return ShadowCastingMode.Off;
        }

        static void MakeLights(Transform parent, InteriorKit k)
        {
            var g = new GameObject("Luzes").transform;
            g.SetParent(parent, false);
            foreach (var l in k.Lights)
            {
                var go = new GameObject(l.name);
                go.transform.SetParent(g, false);
                go.transform.position = l.pos;
                var lt = go.AddComponent<Light>();
                lt.type = LightType.Point;
                lt.color = l.color;
                lt.range = l.range;
                lt.intensity = l.intensity;
                // desempenho: luzes pontuais sem sombras (cada uma custava 6 mapas de sombra por frame) e em modo
                // automático — o Unity escolhe as mais importantes para cada objeto conforme a qualidade gráfica
                lt.shadows = LightShadows.None;
                lt.renderMode = LightRenderMode.Auto;
            }
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = MeshFolder + "/" + m.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null && existing != m)
            {
                existing.Clear();
                existing.indexFormat = m.indexFormat;
                existing.vertices = m.vertices;
                existing.normals = m.normals;
                if (m.uv.Length > 0) existing.uv = m.uv;
                if (m.colors.Length > 0) existing.colors = m.colors;
                existing.subMeshCount = m.subMeshCount;
                for (int i = 0; i < m.subMeshCount; i++) existing.SetTriangles(m.GetTriangles(i), i);
                existing.RecalculateBounds();
                Object.DestroyImmediate(m);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Mats CreateMaterials()
        {
            var M = new Mats();
            M.reboco = CopyMat("M_CVI_Reboco_Branco", "M_CV_Reboco_Branco", Color.white);
            M.ocre = CopyMat("M_CVI_Reboco_Ocre", "M_CV_Reboco_Ocre", Color.white);
            M.pedraMuro = CopyMat("M_CVI_Pedra_Muro", "M_CV_Pedra_Muro", Color.white);
            M.pedraClara = CopyMat("M_CVI_Pedra_Clara", "M_CV_Pedra_Clara", Color.white);
            M.telha = CopyMat("M_CVI_Telha", "M_CV_Telha", Color.white);
            M.madeira = CopyMat("M_CVI_Madeira", "M_CV_Madeira", Color.white);
            M.verde = CopyMat("M_CVI_Madeira_Verde", "M_CV_Madeira_Verde", Color.white);
            M.azul = CopyMat("M_CVI_Madeira_Azul", "M_CV_Madeira_Azul", Color.white);
            M.barro = CopyMat("M_CVI_Barro", "M_CV_Barro", Color.white);
            M.folhas = CopyMat("M_CVI_Folhas", "M_CV_Folhas_Claras", Color.white);
            M.florRosa = CopyMat("M_CVI_Flor_Rosa", "M_CV_Flor_Rosa", Color.white);
            M.florVerm = CopyMat("M_CVI_Flor_Vermelha", "M_CV_Flor_Vermelha", Color.white);
            M.ferro = CopyMat("M_CVI_Ferro", "M_CV_Ferro", Color.white);
            M.troncos = NewMat("M_CVI_Troncos", Tex("T_CVI_Troncos", LogsTex), Color.white, true, Color.black);
            M.ladrilho = NewMat("M_CVI_Ladrilho", Tex("T_CVI_Ladrilho", TilesTex), Color.white, true, Color.black);
            M.soalho = NewMat("M_CVI_Soalho", Tex("T_CVI_Soalho", PlanksTex), Color.white, true, Color.black);
            M.pintado = NewMat("M_CVI_Pintado", null, Color.white, true, Color.black);
            M.chama = NewMat("M_CVI_Chama", null, new Color(1f, 0.85f, 0.45f), false, new Color(1.3f, 0.85f, 0.35f));
            M.vidroLuz = NewMat("M_CVI_Vidro_Luz", null, new Color(1f, 0.9f, 0.7f), false, new Color(0.9f, 0.65f, 0.3f));
            M.lumeVermelho = NewMat("M_CVI_Lume_Vermelho", null, new Color(0.9f, 0.15f, 0.12f), false, new Color(0.9f, 0.12f, 0.08f));
            M.fogo = NewMat("M_CVI_Fogo", null, new Color(1f, 0.5f, 0.1f), false, new Color(1.4f, 0.45f, 0.08f));
            M.sombra = ShadowMat();
            M.azulejo = NewMat("M_CVI_Azulejo", Tex("T_CVI_Azulejo", AzulejoTex), Color.white, true, Color.black);
            M.mosaico = NewMat("M_CVI_Mosaico", Tex("T_CVI_Mosaico", MosaicoTex), Color.white, true, Color.black);
            M.terracota = CopyMat("M_CVI_Reboco_Terracota", "M_CV_Reboco_Terracota", Color.white);
            M.rosa = CopyMat("M_CVI_Reboco_Rosa", "M_CV_Reboco_Rosa", Color.white);
            M.azulRb = CopyMat("M_CVI_Reboco_Azul", "M_CV_Reboco_Azul", Color.white);
            // tom da sombra mais fundo nos materiais que só existem lá dentro: o interior fica na penumbra
            // e as luzes (candeeiros, altar, fogão) fazem manchas de luz com mais contraste
            foreach (var m in new[] { M.madeira, M.pintado, M.ladrilho, M.soalho, M.troncos })
                if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(0.5f, 0.47f, 0.6f));
            if (M.pintado.HasProperty("_RimColor")) M.pintado.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.12f));
            return M;
        }

        static Material CopyMat(string name, string src, Color col)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var s = AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + "/Materials/" + src + ".mat");
            if (m == null)
            {
                m = s != null ? new Material(s) : new Material(Shader.Find("FPSLAN/Toon"));
                AssetDatabase.CreateAsset(m, path);
            }
            else if (s != null) m.CopyPropertiesFromMaterial(s);
            m.name = name;
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", 1f);
            if (m.HasProperty("_Checker")) m.SetFloat("_Checker", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material NewMat(string name, Texture2D tex, Color col, bool vc, Color emission)
        {
            string path = Folder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("FPSLAN/Toon"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = tex;
            m.color = col;
            if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", vc ? 1f : 0f);
            if (m.HasProperty("_Emission")) m.SetColor("_Emission", emission);
            if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", new Color(0.62f, 0.6f, 0.72f));
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.08f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material ShadowMat()
        {
            string path = Folder + "/M_CVI_Sombra_Contacto.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("FPSLAN/FxAlpha"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.mainTexture = Tex("T_CVI_Sombra", BlobTex, true);
            m.color = new Color(0.05f, 0.04f, 0.06f, 0.7f);
            if (m.HasProperty("_VertexColor")) m.SetFloat("_VertexColor", 0f);
            m.renderQueue = 2450;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ texturas procedimentais

        static Texture2D Tex(string name, System.Func<int, Color[]> gen, bool clamp = false)
        {
            string path = TexFolder + "/" + name + ".png";
            const int size = 512;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.SetPixels(gen(size));
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null)
            {
                imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Trilinear;
                imp.anisoLevel = 4;
                imp.alphaIsTransparency = clamp;
                imp.maxTextureSize = 512;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        /// <summary>Ladrilho de barro claro 4×4 por textura (0,5 m), juntas cinzentas e losangos de canto.</summary>
        static Color[] TilesTex(int n)
        {
            var px = new Color[n * n];
            int cell = n / 4;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int cx = x / cell, cy = y / cell;
                    int lx = x % cell, ly = y % cell;
                    float v = Hash(cx, cy);
                    var c = Color.Lerp(new Color(0.86f, 0.8f, 0.68f), new Color(0.78f, 0.7f, 0.58f), v);
                    if ((cx + cy) % 2 == 0) c *= 0.96f;
                    float nz = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.08f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.04f;
                    c *= 0.95f + nz;
                    int ex = Mathf.Min(lx, cell - 1 - lx), ey = Mathf.Min(ly, cell - 1 - ly);
                    // losango escuro nos cantos
                    if (ex + ey < cell * 0.16f) c = Color.Lerp(new Color(0.55f, 0.28f, 0.2f), new Color(0.45f, 0.22f, 0.16f), Hash(x / 6, y / 6));
                    else if (ex + ey < cell * 0.16f + 2) c *= 0.8f;
                    if (ex < 2 || ey < 2) c = new Color(0.55f, 0.53f, 0.5f) * (0.9f + 0.1f * Hash(x, y));
                    else if (ex < 4 || ey < 4) c *= 0.9f;
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            return px;
        }

        /// <summary>Soalho: 8 tábuas por textura, ao longo de U, com veio, juntas e pregos.</summary>
        static Color[] PlanksTex(int n)
        {
            var px = new Color[n * n];
            int plank = n / 8;
            for (int y = 0; y < n; y++)
            {
                int p = y / plank;
                int ly = y % plank;
                float tone = Hash(p, 7);
                int joint = (int)(Hash(p, 3) * n);
                for (int x = 0; x < n; x++)
                {
                    int seg = ((x - joint) % n + n) % n < n / 2 ? 0 : 1;
                    float t2 = Hash(p, seg + 11);
                    var c = Color.Lerp(new Color(0.62f, 0.44f, 0.29f), new Color(0.48f, 0.33f, 0.21f), 0.5f * tone + 0.5f * t2);
                    float grain = Mathf.Sin((x * 0.05f + Mathf.PerlinNoise(x * 0.01f + p * 3f, ly * 0.15f) * 6f) * 3f + ly * 0.4f);
                    c *= 0.92f + 0.08f * grain + Mathf.PerlinNoise(x * 0.2f, y * 0.9f) * 0.06f;
                    if (ly < 2) c *= 0.45f;
                    else if (ly < 3) c *= 0.75f;
                    int dj = Mathf.Min(((x - joint) % (n / 2) + n / 2) % (n / 2), n / 2 - ((x - joint) % (n / 2) + n / 2) % (n / 2));
                    if (dj < 2) c *= 0.5f;
                    int nx = ((x - joint) % (n / 2) + n / 2) % (n / 2);
                    if ((nx == 10 || nx == n / 2 - 11) && (ly == plank / 4 || ly == plank * 3 / 4)) c = new Color(0.2f, 0.18f, 0.16f);
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            }
            return px;
        }

        /// <summary>Parede de troncos: 6 troncos por textura, sombreado cilíndrico, casca e junta escura.</summary>
        static Color[] LogsTex(int n)
        {
            var px = new Color[n * n];
            int log = n / 6;
            for (int y = 0; y < n; y++)
            {
                int l = y / log;
                float ly = (y % log + 0.5f) / log;
                float tone = Hash(l, 21);
                for (int x = 0; x < n; x++)
                {
                    float shade = Mathf.Sin(ly * Mathf.PI);
                    var c = Color.Lerp(new Color(0.56f, 0.38f, 0.24f), new Color(0.66f, 0.47f, 0.3f), tone);
                    c *= 0.55f + 0.5f * Mathf.Pow(shade, 0.6f);
                    float bark = Mathf.PerlinNoise(x * 0.03f + l * 5f, y * 0.35f);
                    c *= 0.9f + 0.18f * bark;
                    c *= 0.96f + 0.06f * Mathf.Sin(x * 0.08f + Mathf.PerlinNoise(x * 0.01f, l) * 8f);
                    if (ly < 0.06f || ly > 0.94f) c = new Color(0.2f, 0.15f, 0.11f);
                    // nós da madeira
                    float kx = Hash(l, 5) * n, dx = Mathf.Abs(x - kx), dy = Mathf.Abs(ly - 0.5f) * log;
                    if (dx * dx + dy * dy * 4f < 30f) c *= 0.6f;
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            }
            return px;
        }

        /// <summary>Azulejo azul e branco (padrão português): 4×4 azulejos por textura, rosácea e cantos.</summary>
        static Color[] AzulejoTex(int n)
        {
            var px = new Color[n * n];
            int cell = n / 4;
            var white = new Color(0.95f, 0.94f, 0.9f);
            var blue = new Color(0.12f, 0.27f, 0.62f);
            var blueL = new Color(0.45f, 0.6f, 0.85f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x % cell + 0.5f) / cell * 2f - 1f, v = (y % cell + 0.5f) / cell * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    var c = white;
                    float ang = Mathf.Atan2(v, u);
                    float petal = 0.42f + 0.18f * Mathf.Cos(ang * 4f);
                    if (r < petal && r > 0.12f) c = Color.Lerp(blueL, blue, Mathf.InverseLerp(0.12f, petal, r));
                    if (r < 0.1f) c = blue;
                    float cr = Mathf.Sqrt((1f - Mathf.Abs(u)) * (1f - Mathf.Abs(u)) + (1f - Mathf.Abs(v)) * (1f - Mathf.Abs(v)));
                    if (cr < 0.32f && cr > 0.2f) c = blue;
                    if (Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) < 0.03f && r > 0.62f && r < 1.2f) c = blueL;
                    if (Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) > 0.97f) c = new Color(0.7f, 0.7f, 0.68f);
                    c *= 0.96f + 0.06f * Hash(x / 3, y / 3);
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            return px;
        }

        /// <summary>Mosaico hidráulico (estrela de 8 pontas) em barro, creme e preto: 2×2 ladrilhos por textura.</summary>
        static Color[] MosaicoTex(int n)
        {
            var px = new Color[n * n];
            int cell = n / 2;
            var cream = new Color(0.88f, 0.82f, 0.7f);
            var terra = new Color(0.66f, 0.3f, 0.2f);
            var dark = new Color(0.16f, 0.15f, 0.15f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x % cell + 0.5f) / cell * 2f - 1f, v = (y % cell + 0.5f) / cell * 2f - 1f;
                    float a = Mathf.Abs(u), b = Mathf.Abs(v);
                    float star = Mathf.Min(Mathf.Max(a, b), (a + b) * 0.7071f);
                    var c = cream;
                    if (star < 0.55f) c = terra;
                    if (star < 0.5f && star > 0.42f) c = dark;
                    if (star < 0.2f) c = cream;
                    if (Mathf.Max(a, b) > 0.9f) c = dark;
                    else if (Mathf.Max(a, b) > 0.84f) c = terra;
                    if (Mathf.Max(a, b) > 0.985f) c = new Color(0.5f, 0.48f, 0.45f);
                    c *= 0.95f + 0.07f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f);
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            return px;
        }

        static Color[] BlobTex(int n)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 1f - Mathf.SmoothStep(0.25f, 1f, r);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a);
                }
            return px;
        }

        static float Random01(Vector3 p) => Hash((int)(p.x * 100f), (int)(p.z * 100f));

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
