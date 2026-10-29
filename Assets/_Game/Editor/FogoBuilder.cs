using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Mapa de sobrevivência "Fogo": constrói Scenes/Fogo.unity a partir dos FBX do Blender
    /// (Tools/Blender/Fogo/Fogo_Map.py): relevo e vulcão (colisor = o próprio relevo), coberturas, arbustos secos
    /// (sistema Bush do jogo com folhagem leve do Blender), lava com shader FPSLAN/Lava e dano pelo host
    /// (<see cref="LavaArea"/>), efeitos com pooling (<see cref="LavaFx"/>), jatos com aviso (<see cref="LavaJets"/>),
    /// ambiente do vulcão e a tempestade de cinza (<see cref="StormManager"/>) que fecha para dentro da cratera.
    /// Não toca nas outras cenas nem regenera a Cidade Velha, a Buracona ou a Arena.
    /// </summary>
    public static class FogoBuilder
    {
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Fogo";
        public const string ScenePath = ProjectSetup.Root + "/Scenes/Fogo.unity";
        const string MapPrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_Fogo.prefab";
        const string LavaPrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_Fogo_Lava.prefab";
        const string LayoutPath = ArtRoot + "/Data/fogo_layout.json";

        static readonly GemMapMat[] Materials =
        {
            new GemMapMat("MAT_Cinza", "T_Cinza", new Color(0.62f, 0.6f, 0.58f), false, default, 0f),
            new GemMapMat("MAT_Cinza_Escura", "T_Cinza", new Color(0.42f, 0.4f, 0.39f), false, default, 0f),
            new GemMapMat("MAT_Basalto", "T_Basalto", new Color(0.4f, 0.35f, 0.33f), true, default, 0.2f),
            new GemMapMat("MAT_Basalto_Escuro", "T_Basalto", new Color(0.26f, 0.24f, 0.24f), true, default, 0.2f),
            new GemMapMat("MAT_Vulcao", "T_Vulcao", new Color(0.66f, 0.52f, 0.46f), false, default, 0.15f),
            new GemMapMat("MAT_Vulcao_Escuro", "T_Vulcao", new Color(0.44f, 0.38f, 0.36f), false, default, 0.1f),
            new GemMapMat("MAT_Vulcao_Cinza", "T_Cinza", new Color(0.5f, 0.47f, 0.45f), false, default, 0f),
            new GemMapMat("MAT_Escoria", "T_Escoria", new Color(0.8f, 0.74f, 0.72f), false, new Color(0.05f, 0.012f, 0f), 0f),
            new GemMapMat("MAT_Ramo", null, new Color(0.22f, 0.16f, 0.12f), false),
            new GemMapMat("MAT_Spawn", null, new Color(0.85f, 0.55f, 0.25f), false, new Color(0.3f, 0.12f, 0.03f), 0f),
            new GemMapMat("MAT_Monte", null, new Color(0.3f, 0.26f, 0.25f), false),
            new GemMapMat("MAT_Monte_Cinza", null, new Color(0.45f, 0.42f, 0.4f), false),
            new GemMapMat("MAT_Colisor", null, new Color(1f, 0.2f, 0.6f), false),
            new GemMapMat("MAT_Brasa", null, new Color(1f, 0.5f, 0.15f), false, new Color(0.9f, 0.32f, 0.05f), 0f),
        };

        static GemMapConfig Cfg => new GemMapConfig
        {
            displayName = "Fogo",
            artRoot = ArtRoot,
            filePrefix = "Fogo",
            materialPrefix = "M_FG_",
            scenePath = ScenePath,
            mapPrefabPath = MapPrefabPath,
            sceneRootName = "Map_Fogo",
            mapName = "Fogo",
            materials = Materials,
        };

        // ------------------------------------------------------------------ layout (JSON do Blender)

        [System.Serializable] class LavaData { public int nx, ny; public float cell, x0, y0, surface_out, surface_in; public string[] rows; }
        [System.Serializable] class Entrance { public float angle; public float[] outer, mouth, middle, inner; }
        [System.Serializable] class Volcano { public float r_bowl, r_rim_in, r_rim_out, r_foot, rim_h, bowl_y, gorge_half_width; }
        [System.Serializable] class Layout { public LavaData lava; public Entrance[] entrances; public Volcano volcano; }

        // Blender (x, y) → Unity (−x, ?, −y): o exportador FBX troca o sinal destes dois eixos
        static Vector3 U(float[] b, float y = 0f) => new Vector3(-b[0], y, -b[1]);

        [MenuItem("FPS-LAN/Mapas/Construir Fogo (sobrevivência)", priority = 44)]
        static void BuildMenu()
        {
            if (!File.Exists(ArtRoot + "/Models/Fogo_Static.fbx") || !File.Exists(LayoutPath))
            {
                EditorUtility.DisplayDialog("Fogo", "Faltam os FBX/JSON em " + ArtRoot + ". Corre Tools/Blender/Fogo/Fogo_Map.py.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorUtility.DisplayDialog("Fogo", Build(), "OK");
        }

        [MenuItem("FPS-LAN/Mapas/Abrir cena Fogo", priority = 45)]
        static void OpenMenu()
        {
            if (File.Exists(ScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static string Build()
        {
            var log = new StringBuilder();
            var cfg = Cfg;
            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));
            GemMapBuilder.EnsureFolder(cfg.MaterialsPath);
            GemMapBuilder.EnsureFolder(ProjectSetup.Root + "/Prefabs/Environment");
            GemMapBuilder.ConfigureTextures(cfg);
            var mats = GemMapBuilder.CreateMaterials(cfg);
            mats["MAT_Arbusto_Vulcanico"] = BushMaterial(cfg);
            mats["MAT_Lava"] = LavaMaterial(cfg);
            GemMapBuilder.ConfigureModel(cfg.Model("Static"), false, mats, true);
            GemMapBuilder.ConfigureModel(cfg.Model("Lava"), false, mats, false);
            if (File.Exists(cfg.Model("Lava_FX_Ref"))) GemMapBuilder.ConfigureModel(cfg.Model("Lava_FX_Ref"), true, mats, false);

            var mapPrefab = BuildMapPrefab(cfg, mats, log);
            var lavaPrefab = BuildLavaPrefab(cfg, mats, layout, log);
            BuildScene(cfg, mapPrefab, lavaPrefab, mats, layout, log);
            var summary = "Cena criada: " + ScenePath + "\nPrefabs: " + MapPrefabPath + ", " + LavaPrefabPath + "\n\n" + log;
            Debug.Log("[Fogo] " + summary);
            return summary;
        }

        static void Set(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
        static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }

        static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            return m;
        }

        static Material BushMaterial(GemMapConfig cfg)
        {
            var m = LoadOrCreate(cfg.MaterialsPath + "/M_FG_Arbusto_Vulcanico.mat", Shader.Find("FPSLAN/Toon"));
            m.color = new Color(0.66f, 0.6f, 0.52f);      // escurece as cores de vértice: mato seco, oliva/castanho com cinza
            Set(m, "_VertexColor", 1f);
            Set(m, "_TopLight", 0.25f);
            Set(m, "_ShadeColor", new Color(0.62f, 0.6f, 0.7f));
            Set(m, "_RimColor", new Color(1f, 0.9f, 0.75f, 0.15f));
            Set(m, "_Fade", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LavaMaterial(GemMapConfig cfg)
        {
            var m = LoadOrCreate(cfg.MaterialsPath + "/M_FG_Lava.mat", Shader.Find("FPSLAN/Lava"));
            m.SetTexture("_CrustTex", AssetDatabase.LoadAssetAtPath<Texture2D>(cfg.TexturesPath + "/T_Lava_Crosta.png"));
            m.SetTexture("_HeatTex", AssetDatabase.LoadAssetAtPath<Texture2D>(cfg.TexturesPath + "/T_Lava_Calor.png"));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ cenário

        static readonly Regex s_Seed = new Regex(@"_S(\d+)$");

        static GameObject BuildMapPrefab(GemMapConfig cfg, Dictionary<string, Material> mats, StringBuilder log)
        {
            var root = new GameObject("PF_Fogo");
            var stat = GemMapBuilder.Unpacked(cfg.Model("Static"), "Cenario");
            stat.transform.SetParent(root.transform, false);
            GemMapBuilder.ConvertColliders(root.transform, log);

            // Arbustos: zona BUSH_<nome>_S<seed> → Bush do jogo; folhagem BSHM_<nome> fica debaixo dele (o Bush desvanece-a)
            var zones = new List<Transform>();
            GemMapBuilder.CollectDeep(root.transform, "BUSH_", zones);
            var bushParent = new GameObject("Arbustos").transform;
            bushParent.SetParent(root.transform, false);
            int bushTris = 0;
            foreach (var z in zones)
            {
                var zr = z.GetComponent<Renderer>();
                if (zr == null) continue;
                var wb = zr.bounds;
                string baseName = s_Seed.Replace(z.name.Substring("BUSH_".Length), "");
                var go = new GameObject("BUSH_" + baseName);
                go.transform.SetParent(bushParent, false);
                go.transform.position = new Vector3(wb.center.x, wb.min.y, wb.center.z);
                var bush = go.AddComponent<Bush>();
                bush.halfExtents = new Vector2(wb.extents.x, wb.extents.z);
                bush.height = wb.size.y + 0.2f;
                var foliage = GemMapBuilder.FindDeep(root.transform, "BSHM_" + baseName);
                if (foliage != null)
                {
                    foliage.SetParent(go.transform, true);
                    var r = foliage.GetComponent<MeshRenderer>();
                    if (r != null)
                    {
                        r.sharedMaterial = mats["MAT_Arbusto_Vulcanico"];
                        r.shadowCastingMode = ShadowCastingMode.On;
                    }
                    var mf = foliage.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) bushTris += mf.sharedMesh.triangles.Length / 3;
                }
            }
            foreach (var z in zones) Object.DestroyImmediate(z.gameObject);
            log.AppendLine($"Arbustos: {bushParent.childCount} (sistema Bush, sem colisores), {bushTris} triângulos de folhagem.");

            MergeCovers(root.transform, log);

            // Spawns de sobrevivência (sem equipa)
            var spawns = new List<Transform>();
            GemMapBuilder.CollectDeep(root.transform, "SPAWN_", spawns);
            foreach (var t in spawns)
            {
                var sp = t.gameObject.AddComponent<SpawnPoint>();
                // equipas para os modos por equipas (Pique-Gemas, Wipeout…): azul a −Z, vermelho a +Z.
                // Em Showdown/Deathmatch as equipas dos spawns são ignoradas (usa-se qualquer spawn).
                sp.team = t.position.z < 0f ? 0 : 1;
                var toCenter = -t.position;
                toCenter.y = 0f;
                if (toCenter.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
            }
            log.AppendLine($"Spawns: {spawns.Count} (sobrevivência, fora do vulcão).");

            // Ambiente do vulcão
            var amb = root.AddComponent<VolcanoAmbience>();
            var fum = new List<Transform>();
            GemMapBuilder.CollectDeep(root.transform, "FX_Fumo_", fum);
            amb.fumaroles = fum.ToArray();
            amb.craterLight = GemMapBuilder.FindDeep(root.transform, "FX_Cratera_Luz");
            var ent = new List<Transform>();
            GemMapBuilder.CollectDeep(root.transform, "FX_Entrada_", ent);
            amb.entrances = ent.ToArray();

            // Estático (batching) exceto arbustos e marcadores; o fundo não projeta sombras
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("BG_") || r.name.StartsWith("TER_")) r.shadowCastingMode = r.name.StartsWith("TER_") ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponentInParent<Bush>() == null && !t.name.StartsWith("FX_") && !t.name.StartsWith("JET_"))
                    t.gameObject.isStatic = true;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MapPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Desempenho: as ~300 peças de cobertura (muros, pedras, pilares, vedações) passam a 4 malhas (uma por
        /// quadrante, um submesh por material). Os colisores continuam a ser as caixas COL_Box_* separadas.
        /// </summary>
        static void MergeCovers(Transform root, StringBuilder log)
        {
            var groups = new Dictionary<int, Dictionary<Material, List<CombineInstance>>>();
            int pieces = 0;
            var toLocal = root.worldToLocalMatrix;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.name.StartsWith("CVR_")) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var c = mr.bounds.center;
                int q = (c.x >= 0f ? 1 : 0) + (c.z >= 0f ? 2 : 0);
                if (!groups.TryGetValue(q, out var byMat)) groups[q] = byMat = new Dictionary<Material, List<CombineInstance>>();
                var mats = mr.sharedMaterials;
                for (int sm = 0; sm < mf.sharedMesh.subMeshCount && sm < mats.Length; sm++)
                {
                    if (mats[sm] == null) continue;
                    if (!byMat.TryGetValue(mats[sm], out var list)) byMat[mats[sm]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = sm, transform = toLocal * mr.localToWorldMatrix });
                }
                Object.DestroyImmediate(mr);
                Object.DestroyImmediate(mf);
                pieces++;
            }
            string folder = ArtRoot + "/Meshes";
            GemMapBuilder.EnsureFolder(folder);
            int draws = 0;
            foreach (var kv in groups)
            {
                var parts = new List<CombineInstance>();
                var mats = new List<Material>();
                foreach (var m in kv.Value)
                {
                    var part = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    part.CombineMeshes(m.Value.ToArray(), true, true);
                    parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                    mats.Add(m.Key);
                }
                var mesh = new Mesh { name = "Fogo_Coberturas_Q" + kv.Key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), false, false);
                mesh.RecalculateBounds();
                foreach (var p in parts) Object.DestroyImmediate(p.mesh);
                string path = folder + "/" + mesh.name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null)
                {
                    // mantém o GUID (sem .meta novos a cada construção); cópia pela API para atualizar também a GPU
                    existing.Clear();
                    existing.indexFormat = mesh.indexFormat;
                    existing.vertices = mesh.vertices;
                    existing.normals = mesh.normals;
                    if (mesh.uv.Length > 0) existing.uv = mesh.uv;
                    if (mesh.colors.Length > 0) existing.colors = mesh.colors;
                    existing.subMeshCount = mesh.subMeshCount;
                    for (int sm = 0; sm < mesh.subMeshCount; sm++) existing.SetTriangles(mesh.GetTriangles(sm), sm);
                    existing.RecalculateBounds();
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                    EditorUtility.SetDirty(mesh);
                }
                else AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject("Coberturas_Q" + kv.Key);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mats.ToArray();
                r.shadowCastingMode = ShadowCastingMode.On;
                go.isStatic = true;
                draws += mats.Count;
            }
            log.AppendLine($"Coberturas: {pieces} peças juntas em {groups.Count} malhas ({draws} submeshes/draw calls em vez de ≥{pieces}).");
        }

        static GameObject BuildLavaPrefab(GemMapConfig cfg, Dictionary<string, Material> mats, Layout layout, StringBuilder log)
        {
            var root = new GameObject("PF_Fogo_Lava");
            var model = GemMapBuilder.Unpacked(cfg.Model("Lava"), "Superficies");
            model.transform.SetParent(root.transform, false);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (r.name.StartsWith("LAV_Rios") || r.name.StartsWith("LAV_Cratera")) r.receiveShadows = false;
            }
            var area = root.AddComponent<LavaArea>();
            var L = layout.lava;
            area.nx = L.nx;
            area.ny = L.ny;
            area.cell = L.cell;
            area.x0 = L.x0;
            area.y0 = L.y0;
            area.surfaceOut = L.surface_out;
            area.surfaceIn = L.surface_in;
            area.rows = L.rows;
            area.flipX = true;
            area.flipZ = true;
            var fx = root.AddComponent<LavaFx>();
            fx.bubbleMaterial = mats["MAT_Lava"];
            fx.dropMaterial = mats["MAT_Brasa"];
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("LAV_")) t.gameObject.isStatic = true;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, LavaPrefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine($"Lava: {L.nx}×{L.ny} células de {L.cell} m; superfícies a y={L.surface_out} (exterior) e {L.surface_in} (cratera).");
            return prefab;
        }

        // ------------------------------------------------------------------ cena

        static void BuildScene(GemMapConfig cfg, GameObject mapPrefab, GameObject lavaPrefab, Dictionary<string, Material> mats, Layout layout, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.86f, 0.72f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(42f, -38f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.55f, 0.55f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.44f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.19f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.56f, 0.5f, 0.47f);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 170f;
            RenderSettings.skybox = SkyMaterial(cfg);
            RenderSettings.sun = sun;

            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
            map.name = "Map_Fogo";
            var lava = (GameObject)PrefabUtility.InstantiatePrefab(lavaPrefab);
            lava.name = "Lava_Fogo";
            lava.transform.SetParent(map.transform, true);
            Physics.SyncTransforms();

            // luzes de calor da lava e jatos (marcadores do cenário)
            var heat = new List<Transform>();
            GemMapBuilder.CollectDeep(map.transform, "FX_Calor_", heat);
            lava.GetComponent<LavaFx>().heatLights = heat.ToArray();

            // MapInfo
            var wps = new List<Transform>();
            GemMapBuilder.CollectDeep(map.transform, "WP_", wps);
            var waypoints = new List<Vector3>();
            foreach (var w in wps) waypoints.Add(w.position);
            float half = 30f;
            Collider floor = null;
            foreach (var c in map.GetComponentsInChildren<Collider>(true))
                if (c.name.StartsWith("COL_Tri_Chao")) { floor = c; break; }
            if (floor != null)
            {
                var b = floor.bounds;
                half = Mathf.Max(b.extents.x, b.extents.z) - 1.2f;
                log.AppendLine($"Relevo: {b.size.x:0.0} × {b.size.z:0.0} m, de y={b.min.y:0.00} a y={b.max.y:0.00} (esperado ≈ 60,8 × 64).");
            }
            else log.AppendLine("ATENÇÃO: COL_Tri_Chao não encontrado.");
            var info = new GameObject("MapInfo").AddComponent<MapInfo>();
            info.mapName = cfg.mapName;
            info.halfSize = half;
            info.fallRecoveryY = -12f;
            info.waypoints = waypoints.ToArray();

            var systems = new GameObject("Systems");
            systems.AddComponent<CombatSystem>();
            systems.AddComponent<ConnectionManager>();
            systems.AddComponent<GameUI>();

            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.farClipPlane = 450f;
            camGo.AddComponent<AudioListener>();
            var menuCam = camGo.AddComponent<MenuCamera>();
            menuCam.radius = 46f;
            menuCam.height = 30f;
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 30f, -46f);
            camGo.transform.LookAt(Vector3.zero);

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.PlayerPrefabPath);
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.TickRate = 60;
            var defaultList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (defaultList != null && !nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(defaultList))
                nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(defaultList);
            utp.SetConnectionData("127.0.0.1", ConnectionManager.DefaultPort, "0.0.0.0");

            var matchGo = new GameObject("MatchManager");
            matchGo.AddComponent<NetworkObject>();
            var match = matchGo.AddComponent<MatchManager>();
            match.overrideMenuMode = false;          // segue o modo do menu (o Fogo foi pensado para SHOWDOWN)

            // Tempestade de cinza (estado replicado: hora de início + ativa)
            var stormGo = new GameObject("TempestadeCinza");
            stormGo.AddComponent<NetworkObject>();
            var storm = stormGo.AddComponent<StormManager>();
            stormGo.AddComponent<StormVisual>();
            float bowlY = layout.volcano != null ? layout.volcano.bowl_y : -1.5f;
            storm.center = new Vector3(0f, bowlY, 0f);
            storm.innerRadius = layout.volcano != null ? layout.volcano.r_bowl - 0.5f : 13f;
            storm.footRadius = layout.volcano != null ? layout.volcano.r_foot : 22.5f;
            var entrances = new List<StormEntrance>();
            if (layout.entrances != null)
                foreach (var e in layout.entrances)
                {
                    var mid = U(e.middle);
                    var outer = U(e.outer);
                    var dir = (U(e.inner) - outer).normalized;
                    var side = Vector3.Cross(Vector3.up, dir).normalized;
                    entrances.Add(new StormEntrance
                    {
                        outer = Ground(outer),
                        mouth = Ground(U(e.mouth)),
                        laneA = Ground(mid + side * 2.1f),
                        laneB = Ground(mid - side * 2.1f),
                        inner = Ground(U(e.inner)),
                    });
                }
            storm.entrances = entrances.ToArray();
            log.AppendLine($"Tempestade: centro (0, {bowlY:0.0}, 0), {storm.phases.Length} fases até {storm.phases[storm.phases.Length - 1].radius} m; {entrances.Count} entradas para os bots.");

            // Jatos de lava (aviso → coluna; dano pelo host)
            var jetsGo = new GameObject("JatosLava");
            jetsGo.AddComponent<NetworkObject>();
            var jets = jetsGo.AddComponent<LavaJets>();
            var vents = new List<Transform>();
            GemMapBuilder.CollectDeep(map.transform, "JET_", vents);
            vents.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            jets.vents = vents.ToArray();
            jets.columnMaterial = mats["MAT_Brasa"];
            log.AppendLine($"Jatos de lava: {vents.Count}.");

            BuildGemGrab(mats, bowlY, log);

            Validate(map.transform, layout, storm, log);

            GemMapBuilder.EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
                log.AppendLine("Cena acrescentada ao fim das Build Settings.");
            }
            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in reopened.GetRootGameObjects())
                foreach (var no in go.GetComponentsInChildren<NetworkObject>(true))
                {
                    var m = no.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    try { m?.Invoke(no, null); } catch (System.Exception ex) { Debug.LogWarning("[Fogo] OnValidate: " + ex.Message); }
                }
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }


        // ------------------------------------------------------------------ Pique-Gemas

        /// <summary>
        /// Pique-Gemas no Fogo: um pequeno respiradouro de lava no centro da arena interior (só visual, sem dano)
        /// que borbulha no aviso e rebenta num jato pequeno de onde saltam as gemas para 12 pontos de rocha seca
        /// à volta (simétricos entre as duas equipas).
        /// </summary>
        static void BuildGemGrab(Dictionary<string, Material> mats, float bowlY, StringBuilder log)
        {
            Physics.SyncTransforms();
            var root = new GameObject("PiqueGemas");
            root.AddComponent<NetworkObject>();
            var gm = root.AddComponent<GemGrabManager>();
            var vis = root.AddComponent<GemGrabVisuals>();
            root.AddComponent<GemGrabHud>();
            gm.gemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.Root + "/Prefabs/Environment/PF_Gema.prefab");
            gm.gemColor = new Color(0.72f, 0.42f, 1f);
            gm.arcHeight = 2.6f;
            vis.style = GemEmitterStyle.Lava;
            vis.waterRadius = 0.8f;

            // respiradouro: poça de lava redonda e um anel de escória à volta (sem colisores: anda-se por cima)
            var vent = new GameObject("Respiradouro_Gemas").transform;
            vent.SetParent(root.transform, false);
            vent.position = new Vector3(0f, bowlY, 0f);
            var pool = new GameObject("Lava_Respiradouro");
            pool.transform.SetParent(vent, false);
            pool.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            pool.transform.localScale = new Vector3(1.7f, 0.02f, 1.7f);
            pool.AddComponent<MeshFilter>().sharedMesh = Art.PrimitiveMesh(PrimitiveType.Cylinder);
            var pr = pool.AddComponent<MeshRenderer>();
            pr.sharedMaterial = mats["MAT_Lava"];
            pr.shadowCastingMode = ShadowCastingMode.Off;
            var rng = new System.Random(77);
            var rockMesh = Art.PrimitiveMesh(PrimitiveType.Sphere);
            for (int i = 0; i < 11; i++)
            {
                float a = i * Mathf.PI * 2f / 11f + (float)rng.NextDouble() * 0.2f;
                var rock = new GameObject("Escoria_" + i);
                rock.transform.SetParent(vent, false);
                rock.transform.localPosition = new Vector3(Mathf.Cos(a), 0.05f, Mathf.Sin(a)) * 1.0f;
                rock.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 30f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f);
                rock.transform.localScale = new Vector3(0.38f + (float)rng.NextDouble() * 0.2f, 0.18f + (float)rng.NextDouble() * 0.12f, 0.3f + (float)rng.NextDouble() * 0.2f);
                rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
                var rr = rock.AddComponent<MeshRenderer>();
                rr.sharedMaterial = i % 3 == 0 ? mats["MAT_Escoria"] : mats["MAT_Basalto_Escuro"];
            }
            var surface = new GameObject("Superficie").transform;
            surface.SetParent(vent, false);
            surface.localPosition = new Vector3(0f, 0.06f, 0f);
            var exit = new GameObject("GEM_Saida").transform;
            exit.SetParent(vent, false);
            exit.localPosition = new Vector3(0f, 0.45f, 0f);
            gm.spawnPoint = exit;
            vis.waterSurface = surface;
            gm.emitterRenderers = new Renderer[0];

            // aterragens: metade a −Z escolhida em rocha seca e livre, e o espelho em +Z (justo para as duas equipas)
            var lava = Object.FindAnyObjectByType<LavaArea>();
            LavaArea tmp = null;
            if (lava != null)
            {
                tmp = new GameObject("tmpLava").AddComponent<LavaArea>();
                tmp.nx = lava.nx; tmp.ny = lava.ny; tmp.cell = lava.cell; tmp.x0 = lava.x0; tmp.y0 = lava.y0;
                tmp.surfaceIn = lava.surfaceIn; tmp.surfaceOut = lava.surfaceOut; tmp.rows = lava.rows;
                typeof(LavaArea).GetMethod("Decode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tmp, null);
            }
            bool Good(Vector3 xz, out Vector3 ground)
            {
                ground = default;
                if (!Physics.Raycast(new Vector3(xz.x, bowlY + 4f, xz.z), Vector3.down, out var hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                if (!(hit.collider is MeshCollider) || hit.normal.y < 0.9f || Mathf.Abs(hit.point.y - bowlY) > 0.3f) return false;
                if (tmp != null && tmp.IsLava(hit.point, out _)) return false;
                foreach (var c in Physics.OverlapCapsule(hit.point + Vector3.up * 0.3f, hit.point + Vector3.up * 1.2f, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!(c is MeshCollider)) return false;
                ground = hit.point;
                return true;
            }
            var lands = new List<Transform>();
            var landRoot = new GameObject("Aterragens").transform;
            landRoot.SetParent(root.transform, false);
            float[] angles = { 200f, 228f, 256f, 284f, 312f, 340f };
            float[] radii = { 3.4f, 5.6f, 3.6f, 5.4f, 3.4f, 5.6f };
            int k = 0;
            for (int i = 0; i < angles.Length; i++)
            {
                bool placed = false;
                for (float dr = 0f; dr <= 2.2f && !placed; dr += 0.4f)
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        float r = radii[i] + sign * dr;
                        if (r < 2.4f || r > 7f) continue;
                        float a = angles[i] * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        var pm = new Vector3(p.x, 0f, -p.z);
                        if (!Good(p, out var g1) || !Good(pm, out var g2)) continue;
                        foreach (var g in new[] { g1, g2 })
                        {
                            var t = new GameObject("GEM_Land_" + (k++).ToString("00")).transform;
                            t.SetParent(landRoot, false);
                            t.position = g + Vector3.up * gm.gemFloatHeight;
                            lands.Add(t);
                        }
                        placed = true;
                        break;
                    }
            }
            if (tmp != null) Object.DestroyImmediate(tmp.gameObject);
            gm.landingPoints = lands.ToArray();
            log.AppendLine($"Pique-Gemas: respiradouro de lava no centro (y={bowlY:0.00}), {lands.Count} aterragens em rocha seca (simétricas), spawns azul −Z / vermelho +Z.");
            if (gm.gemPrefab == null) log.AppendLine("ATENÇÃO: PF_Gema.prefab não encontrado (constrói primeiro a Cidade Velha).");
        }

        static Vector3 Ground(Vector3 p)
        {
            // o ponto mais baixo: o chão por baixo dos arcos de rocha das entradas (e não o topo do arco)
            var hits = Physics.RaycastAll(new Vector3(p.x, 40f, p.z), Vector3.down, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) return p;
            var best = hits[0].point;
            foreach (var h in hits) if (h.point.y < best.y) best = h.point;
            return best;
        }

        static Material SkyMaterial(GemMapConfig cfg)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Materials/M_Sky.mat");
            var m = LoadOrCreate(cfg.MaterialsPath + "/M_FG_Ceu.mat", shader);
            Set(m, "_SkyTint", new Color(0.62f, 0.55f, 0.52f));
            Set(m, "_GroundColor", new Color(0.32f, 0.27f, 0.25f));
            Set(m, "_AtmosphereThickness", 1.6f);
            Set(m, "_Exposure", 1.05f);
            Set(m, "_SunSize", 0.03f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Verificações no editor: máscara de lava alinhada com as superfícies, spawns fora da lava,
        /// entradas com chão transitável, centro final em chão seco, materiais em falta.</summary>
        static void Validate(Transform map, Layout layout, StormManager storm, StringBuilder log)
        {
            Physics.SyncTransforms();
            var area = map.GetComponentInChildren<LavaArea>();
            // a LavaArea só descodifica no Awake (runtime): validar aqui com uma cópia
            var tmp = new GameObject("tmpLava").AddComponent<LavaArea>();
            tmp.nx = area.nx; tmp.ny = area.ny; tmp.cell = area.cell; tmp.x0 = area.x0; tmp.y0 = area.y0;
            tmp.surfaceIn = area.surfaceIn; tmp.surfaceOut = area.surfaceOut; tmp.rows = area.rows;
            typeof(LavaArea).GetMethod("Decode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tmp, null);
            int ok = 0, bad = 0;
            foreach (var mf in map.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.StartsWith("LAV_Rios") && !mf.name.StartsWith("LAV_Cratera")) continue;
                var mesh = mf.sharedMesh;
                var v = mesh.vertices;
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 6)
                {
                    var c = mf.transform.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f);
                    if (tmp.IsLava(c, out float s) && Mathf.Abs(s - c.y) < 0.05f) ok++; else bad++;
                }
            }
            log.AppendLine(bad == 0 ? $"Máscara de lava alinhada com as superfícies ({ok} amostras)." : $"ATENÇÃO: {bad} amostras de lava fora da máscara ({ok} certas).");
            int spawnInLava = 0;
            foreach (var sp in map.GetComponentsInChildren<SpawnPoint>()) if (tmp.IsLava(sp.transform.position, out _)) spawnInLava++;
            log.AppendLine(spawnInLava == 0 ? "Spawns: nenhum em lava." : $"ATENÇÃO: {spawnInLava} spawns em lava.");
            bool centerOk = Physics.Raycast(storm.center + Vector3.up * 3f, Vector3.down, out var hc, 6f) && !tmp.IsLava(storm.center, out _);
            log.AppendLine(centerOk ? $"Centro da zona final em rocha seca (y={hc.point.y:0.00})." : "ATENÇÃO: centro da zona final sem chão seco.");
            Object.DestroyImmediate(tmp.gameObject);

            int missing = 0, slots = 0;
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    slots++;
                    if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader") missing++;
                }
            log.AppendLine(missing == 0 ? $"Materiais: {slots} slots, sem referências em falta." : $"ATENÇÃO: {missing} slots de material em falta.");
        }
    }
}
