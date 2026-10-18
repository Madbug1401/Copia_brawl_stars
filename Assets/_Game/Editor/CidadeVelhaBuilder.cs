using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Mapa Pique-Gemas "Cidade Velha — Pelourinho": importa os FBX gerados no Blender
    /// (Tools/Blender/CidadeVelha), cria materiais cartoon, prefabs do cenário e da gema
    /// e uma cena PRÓPRIA (Scenes/CidadeVelha.unity).
    ///
    /// Não toca em Arena.unity, Player.prefab (só o referencia) nem nos materiais/dados do ProjectSetup.
    /// Pode ser corrido várias vezes: recria apenas os assets desta pasta, os dois prefabs e a cena do mapa.
    /// </summary>
    public static class CidadeVelhaBuilder
    {
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Santiago/CidadeVelha";
        const string ModelsPath = ArtRoot + "/Models";
        const string TexturesPath = ArtRoot + "/Textures";
        const string MaterialsPath = ArtRoot + "/Materials";
        const string PrefabsPath = ProjectSetup.Root + "/Prefabs/Environment";
        public const string ScenePath = ProjectSetup.Root + "/Scenes/CidadeVelha.unity";
        const string MapPrefabPath = PrefabsPath + "/PF_CidadeVelha.prefab";
        const string GemPrefabPath = PrefabsPath + "/PF_Gema.prefab";
        const string StaticFbx = ModelsPath + "/CidadeVelha_Static.fbx";
        const string GeneratorFbx = ModelsPath + "/CidadeVelha_GemGenerator.fbx";
        const string GemFbx = ModelsPath + "/CidadeVelha_Gem.fbx";
        const string EmitterAnimFbx = ModelsPath + "/CidadeVelha_Emissor_Anim.fbx";

        /// <summary>Material do Blender → aparência cartoon no Unity. Cores iguais às do script do Blender.</summary>
        struct MatSpec
        {
            public string name, texture;
            public Color color, emission;
            public bool outline;
            public float topLight;

            public MatSpec(string n, string tex, Color c, bool outl = true, Color emi = default, float top = 0.15f)
            {
                name = n; texture = tex; color = c; outline = outl; emission = emi; topLight = top;
            }
        }

        static readonly MatSpec[] Specs =
        {
            new MatSpec("MAT_Calcada_Basalto", "T_Calcada_Basalto", Color.white, false, default, 0f),
            new MatSpec("MAT_Lajes_Claras", "T_Lajes_Claras", Color.white, false, default, 0f),
            new MatSpec("MAT_Terra", "T_Terra", Color.white, false, default, 0f),
            new MatSpec("MAT_Pedra_Muro", "T_Alvenaria", new Color(1f, 0.97f, 0.92f)),
            new MatSpec("MAT_Pedra_Escura", "T_Alvenaria", new Color(0.62f, 0.6f, 0.6f)),
            new MatSpec("MAT_Pedra_Clara", "T_Pedra_Clara", Color.white, true, default, 0.25f),
            new MatSpec("MAT_Degraus", "T_Pedra_Clara", new Color(0.86f, 0.84f, 0.8f), true, default, 0.3f),
            new MatSpec("MAT_Reboco_Branco", "T_Reboco", new Color(0.97f, 0.96f, 0.92f)),
            new MatSpec("MAT_Reboco_Ocre", "T_Reboco", new Color(0.96f, 0.78f, 0.45f)),
            new MatSpec("MAT_Reboco_Terracota", "T_Reboco", new Color(0.86f, 0.48f, 0.34f)),
            new MatSpec("MAT_Reboco_Azul", "T_Reboco", new Color(0.62f, 0.78f, 0.92f)),
            new MatSpec("MAT_Reboco_Rosa", "T_Reboco", new Color(0.95f, 0.7f, 0.66f)),
            new MatSpec("MAT_Telha", "T_Telha", Color.white),
            new MatSpec("MAT_Madeira", "T_Madeira", new Color(0.66f, 0.46f, 0.3f)),
            new MatSpec("MAT_Madeira_Verde", "T_Madeira", new Color(0.24f, 0.58f, 0.42f)),
            new MatSpec("MAT_Madeira_Azul", "T_Madeira", new Color(0.2f, 0.44f, 0.72f)),
            new MatSpec("MAT_Ferro", null, new Color(0.16f, 0.16f, 0.19f)),
            new MatSpec("MAT_Pano_Vermelho", null, new Color(0.86f, 0.2f, 0.18f)),
            new MatSpec("MAT_Pano_Amarelo", null, new Color(0.98f, 0.78f, 0.18f)),
            new MatSpec("MAT_Pano_Azul", null, new Color(0.15f, 0.42f, 0.82f)),
            new MatSpec("MAT_Pano_Verde", null, new Color(0.15f, 0.62f, 0.42f)),
            new MatSpec("MAT_Folhas", null, new Color(0.22f, 0.5f, 0.2f)),
            new MatSpec("MAT_Folhas_Claras", null, new Color(0.38f, 0.64f, 0.26f)),
            new MatSpec("MAT_Tronco", null, new Color(0.4f, 0.29f, 0.2f)),
            new MatSpec("MAT_Mar", null, new Color(0.12f, 0.5f, 0.78f), false, new Color(0.02f, 0.08f, 0.14f), 0f),
            new MatSpec("MAT_Monte", null, new Color(0.56f, 0.44f, 0.32f), false),
            new MatSpec("MAT_Monte_Verde", null, new Color(0.42f, 0.52f, 0.28f), false),
            new MatSpec("MAT_Spawn_Azul", null, new Color(0.2f, 0.5f, 0.95f), false, new Color(0.08f, 0.2f, 0.4f), 0f),
            new MatSpec("MAT_Spawn_Vermelho", null, new Color(0.95f, 0.28f, 0.22f), false, new Color(0.4f, 0.1f, 0.08f), 0f),
            new MatSpec("MAT_Gema", null, new Color(0.75f, 0.45f, 1f), true, new Color(0.45f, 0.2f, 0.7f), 0f),
            new MatSpec("MAT_Emissor", null, new Color(0.85f, 0.7f, 1f), false, new Color(0.25f, 0.12f, 0.45f), 0f),
            new MatSpec("MAT_Marca_Aterragem", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new MatSpec("MAT_Marca_Aterragem_Apagada", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new MatSpec("MAT_Colisor", null, new Color(1f, 0.2f, 0.6f), false),
            new MatSpec("MAT_Barro", null, new Color(0.72f, 0.38f, 0.24f)),
            new MatSpec("MAT_Flor_Vermelha", null, new Color(0.92f, 0.16f, 0.2f), false),
            new MatSpec("MAT_Flor_Rosa", null, new Color(0.98f, 0.45f, 0.7f), false),
            new MatSpec("MAT_Flor_Amarela", null, new Color(1f, 0.82f, 0.18f), false),
            new MatSpec("MAT_Vidro_Lanterna", null, new Color(1f, 0.86f, 0.55f), false, new Color(0.55f, 0.42f, 0.22f), 0f),
            new MatSpec("MAT_Palha", null, new Color(0.78f, 0.64f, 0.38f)),
            new MatSpec("MAT_Cesto", null, new Color(0.8f, 0.62f, 0.36f)),
            new MatSpec("MAT_Bote_Azul", null, new Color(0.12f, 0.4f, 0.8f)),
            new MatSpec("MAT_Bote_Amarelo", null, new Color(0.98f, 0.76f, 0.16f)),
            new MatSpec("MAT_Bote_Vermelho", null, new Color(0.85f, 0.2f, 0.18f)),
            new MatSpec("MAT_Basalto_Escuro", "T_Calcada_Basalto", new Color(0.22f, 0.22f, 0.25f), false, default, 0f),
        };

        // ================================================================== menus

        [MenuItem("FPS-LAN/Mapas/Construir Cidade Velha (Pique-Gemas)", priority = 40)]
        static void BuildMenu()
        {
            if (!File.Exists(StaticFbx) || !File.Exists(GeneratorFbx) || !File.Exists(GemFbx))
            {
                EditorUtility.DisplayDialog("Cidade Velha",
                    "Faltam os FBX em " + ModelsPath + ".\n\nCorre primeiro o script do Blender (Tools/Blender/CidadeVelha/LEIA-ME.md) " +
                    "e copia a pasta FBX e Textures para essa pasta.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var report = Build();
            EditorUtility.DisplayDialog("Cidade Velha", report, "OK");
        }

        [MenuItem("FPS-LAN/Mapas/Abrir cena Cidade Velha", priority = 41)]
        static void OpenMenu()
        {
            if (File.Exists(ScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        // ================================================================== construção

        public static string Build()
        {
            var log = new StringBuilder();
            EnsureFolder(MaterialsPath);
            EnsureFolder(PrefabsPath);

            ConfigureTextures();
            var mats = CreateMaterials();
            ConfigureModel(StaticFbx, false, mats);
            ConfigureModel(GeneratorFbx, false, mats);
            ConfigureModel(GemFbx, true, mats);
            if (File.Exists(EmitterAnimFbx)) ConfigureModel(EmitterAnimFbx, true, mats);

            var gemPrefab = BuildGemPrefab(mats);
            var mapPrefab = BuildMapPrefab(log);
            BuildScene(mapPrefab, gemPrefab, log);

            var summary = "Cena criada: " + ScenePath + "\nPrefabs: " + MapPrefabPath + ", " + GemPrefabPath + "\n\n" + log;
            Debug.Log("[Cidade Velha] " + summary);
            return summary;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ConfigureTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool dirty = ti.wrapMode != TextureWrapMode.Repeat || ti.maxTextureSize != 1024 || !ti.mipmapEnabled ||
                    ti.textureShape != TextureImporterShape.Texture2D || ti.textureType != TextureImporterType.Default;
                ti.textureType = TextureImporterType.Default;
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.sRGBTexture = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = 1024;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                if (dirty) ti.SaveAndReimport();
            }
        }

        static Dictionary<string, Material> CreateMaterials()
        {
            var toon = Shader.Find("FPSLAN/Toon");
            var outl = Shader.Find("FPSLAN/ToonOutline");
            if (toon == null) toon = Shader.Find("Standard");
            if (outl == null) outl = toon;
            var dict = new Dictionary<string, Material>();
            foreach (var spec in Specs)
            {
                var path = MaterialsPath + "/" + spec.name.Replace("MAT_", "M_CV_") + ".mat";
                var shader = spec.outline ? outl : toon;
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                m.shader = shader;
                m.color = spec.color;
                if (spec.texture != null)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturesPath + "/" + spec.texture + ".png");
                    if (tex != null) m.mainTexture = tex;
                }
                Set(m, "_Emission", spec.emission);
                Set(m, "_TopLight", spec.topLight);
                Set(m, "_ShadeColor", new Color(0.72f, 0.7f, 0.86f));
                Set(m, "_RimColor", new Color(1f, 0.96f, 0.9f, 0.18f));
                if (spec.outline)
                {
                    Set(m, "_OutlineWidth", 0.0018f);
                    Set(m, "_OutlineColor", Color.Lerp(spec.color, Color.black, 0.75f));
                }
                EditorUtility.SetDirty(m);
                dict[spec.name] = m;
            }
            AssetDatabase.SaveAssets();
            return dict;
        }

        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void Set(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        /// <summary>Import em metros, sem câmaras/luzes, materiais do Blender remapeados para os cartoon.</summary>
        static void ConfigureModel(string path, bool animations, Dictionary<string, Material> mats)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.isReadable = path == StaticFbx; // MeshColliders convexos dos degraus
            mi.addCollider = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.preserveHierarchy = true;
            mi.sortHierarchyByName = false;
            mi.importAnimation = animations;
            mi.animationType = animations ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            mi.SaveAndReimport();
        }

        static GameObject Unpacked(string fbx, string name)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a); // sem Animator: o código anima
            return go;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        static void CollectDeep(Transform root, string prefix, List<Transform> into)
        {
            if (root.name.StartsWith(prefix)) into.Add(root);
            foreach (Transform c in root) CollectDeep(c, prefix, into);
        }

        // ------------------------------------------------------------------ gema

        static GameObject BuildGemPrefab(Dictionary<string, Material> mats)
        {
            var root = new GameObject("PF_Gema");
            var model = Unpacked(GemFbx, "Modelo");
            model.transform.SetParent(root.transform, false);
            var mesh = FindDeep(model.transform, "Gema");
            if (mesh != null)
            {
                // pose de repouso limpa (a origem da gema é o centro)
                mesh.localPosition = Vector3.zero;
                mesh.localScale = Vector3.one;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = mats["MAT_Gema"];
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GemPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ cenário

        static GameObject BuildMapPrefab(StringBuilder log)
        {
            var root = new GameObject("PF_CidadeVelha");
            var stat = Unpacked(StaticFbx, "Cenario");
            stat.transform.SetParent(root.transform, false);
            var gen = Unpacked(GeneratorFbx, "GeradorGemas");
            gen.transform.SetParent(root.transform, false);

            int boxes = 0, meshes = 0;
            var cols = new List<Transform>();
            CollectDeep(root.transform, "COL_", cols);
            foreach (var t in cols)
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (t.name.StartsWith("COL_Mesh_"))
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = true;
                    meshes++;
                }
                else
                {
                    var b = t.gameObject.AddComponent<BoxCollider>();
                    b.center = mesh.bounds.center;
                    b.size = mesh.bounds.size;
                    boxes++;
                }
                Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(mf);
            }
            log.AppendLine($"Colisores: {boxes} caixas + {meshes} convexos (degraus do Pelourinho).");

            // Arbustos de esconder: zona BUSH_* → componente Bush do projeto (atravessa-se, oculta, revela por
            // proximidade/ataque/dano) + folhas LeafBush; a folhagem de pré-visualização BSHV_* do Blender sai.
            var bushZones = new List<Transform>();
            CollectDeep(root.transform, "BUSH_", bushZones);
            var bushParent = new GameObject("Arbustos").transform;
            bushParent.SetParent(root.transform, false);
            foreach (var z in bushZones)
            {
                var zr = z.GetComponent<Renderer>();
                if (zr == null) continue;
                var wb = zr.bounds; // em metros e no espaço do mundo, independente da rotação do import
                var go = new GameObject(z.name);
                go.transform.SetParent(bushParent, false);
                go.transform.position = new Vector3(wb.center.x, wb.min.y, wb.center.z);
                var bush = go.AddComponent<Bush>();
                bush.halfExtents = new Vector2(wb.extents.x, wb.extents.z);
                bush.height = wb.size.y + 0.2f;
                // Mesma seed que o Blender usou na pré-visualização (sufixo _S<seed>) → o mesmo arbusto LeafBush
                int seed = BushTools.SeedFor(go.transform.position);
                int sIdx = z.name.LastIndexOf("_S");
                if (sIdx >= 0 && int.TryParse(z.name.Substring(sIdx + 2), out var parsed)) seed = parsed;
                go.name = sIdx >= 0 ? z.name.Substring(0, sIdx) : z.name;
                BushTools.AddLeaves(bush, seed);
            }
            foreach (var z in bushZones) Object.DestroyImmediate(z.gameObject);
            var previewLeaves = new List<Transform>();
            CollectDeep(root.transform, "BSHV_", previewLeaves);
            foreach (var t in previewLeaves) Object.DestroyImmediate(t.gameObject);
            log.AppendLine($"Arbustos: {bushParent.childCount} (sistema Bush + LeafBush, sem colisores).");

            // Spawns por equipa, a olhar para o centro do mapa
            var spawns = new List<Transform>();
            CollectDeep(root.transform, "SPAWN_", spawns);
            int nb = 0, nr = 0;
            foreach (var t in spawns)
            {
                var sp = t.gameObject.AddComponent<SpawnPoint>();
                sp.team = t.name.StartsWith("SPAWN_Blue") ? 0 : 1;
                if (sp.team == 0) nb++; else nr++;
                var toCenter = -t.position;
                toCenter.y = 0f;
                if (toCenter.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
            }
            log.AppendLine($"Spawns: {nb} azuis, {nr} vermelhos.");

            // Sombra e flags estáticas (as marcas de aterragem e o emissor não fazem sombra)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("GEN_")) r.shadowCastingMode = ShadowCastingMode.Off;
                if (r.name.StartsWith("GRD_") || r.name.StartsWith("BG_Mar")) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!t.name.StartsWith("GEN_Emissor") && !t.name.StartsWith("GEM_") && t.GetComponentInParent<Bush>() == null)
                    t.gameObject.isStatic = true; // arbustos fora do static batching (malha de folhas gerada em runtime + fade)

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, MapPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ cena

        static void BuildScene(GameObject mapPrefab, GameObject gemPrefab, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Luz quente de fim de tarde e ambiente claro (igual ao estilo da Arena, mais quente)
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.7f, 0.78f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.62f, 0.58f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.36f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.76f, 0.86f, 0.96f);
            RenderSettings.fogStartDistance = 50f;
            RenderSettings.fogEndDistance = 220f;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Materials/M_Sky.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
            map.name = "Map_CidadeVelha";

            // Waypoints dos bots e limites a partir dos marcadores
            var wps = new List<Transform>();
            CollectDeep(map.transform, "WP_", wps);
            var waypoints = new List<Vector3>();
            foreach (var w in wps) waypoints.Add(w.position);
            var floor = FindDeep(map.transform, "COL_Box_Chao");
            float half = 25.5f;
            if (floor != null && floor.GetComponent<BoxCollider>() is BoxCollider fb)
            {
                var b = fb.bounds;
                half = Mathf.Max(b.extents.x, b.extents.z) - 1.5f;
                log.AppendLine($"Chão: {b.size.x:0.0} × {b.size.z:0.0} m, topo y={b.max.y:0.00} (esperado ≈ 41 × 56, y=0).");
                if (b.size.x > 400f || b.size.x < 10f) log.AppendLine("ATENÇÃO: escala do FBX inesperada — confirma 'Convert Units'.");
            }
            var info = new GameObject("MapInfo").AddComponent<MapInfo>();
            info.mapName = "Cidade Velha";
            info.halfSize = half;
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
            menuCam.radius = 42f;
            menuCam.height = 28f;
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 28f, -42f);
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
            if (nm.NetworkConfig.PlayerPrefab == null) log.AppendLine("ATENÇÃO: Player.prefab não encontrado.");

            var matchGo = new GameObject("MatchManager");
            matchGo.AddComponent<NetworkObject>();
            var match = matchGo.AddComponent<MatchManager>();
            match.overrideMenuMode = false;
            match.sceneMode = GameMode.GemGrab;

            // Pique-Gemas (NetworkObject da cena: estado replicado e sincronizado para quem entra a meio)
            var gemGo = new GameObject("PiqueGemas");
            gemGo.AddComponent<NetworkObject>();
            var gm = gemGo.AddComponent<GemGrabManager>();
            gemGo.AddComponent<GemGrabVisuals>();
            gemGo.AddComponent<GemGrabHud>();
            gm.gemPrefab = gemPrefab;
            gm.spawnPoint = FindDeep(map.transform, "GEM_Spawn");
            var lands = new List<Transform>();
            CollectDeep(map.transform, "GEM_Land_", lands);
            lands.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            gm.landingPoints = lands.ToArray();
            gm.emitterHalo = FindDeep(map.transform, "GEN_Emissor_Halo");
            gm.emitterBase = FindDeep(map.transform, "GEN_Emissor_Base");
            var emitterRenderers = new List<Renderer>();
            if (gm.emitterHalo != null) emitterRenderers.AddRange(gm.emitterHalo.GetComponentsInChildren<Renderer>());
            if (gm.emitterBase != null) emitterRenderers.AddRange(gm.emitterBase.GetComponentsInChildren<Renderer>());
            gm.emitterRenderers = emitterRenderers.ToArray();
            if (gm.spawnPoint == null) log.AppendLine("ATENÇÃO: GEM_Spawn não encontrado no FBX do gerador.");
            log.AppendLine($"Gerador: {lands.Count} pontos de aterragem, GEM_Spawn a y={(gm.spawnPoint != null ? gm.spawnPoint.position.y : 0f):0.00}.");

            Validate(map.transform, gm, log);

            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Na lista de build (necessário para os NetworkObjects da cena). A Arena continua em primeiro.
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath))
            {
                list.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
                log.AppendLine("Cena adicionada ao fim das Build Settings (ProjectSettings/EditorBuildSettings.asset — combinar com o Nico antes do commit).");
            }

            // Reabrir para gerar GlobalObjectIdHash dos NetworkObjects da cena (como no ProjectSetup)
            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in reopened.GetRootGameObjects())
                foreach (var no in go.GetComponentsInChildren<NetworkObject>(true))
                {
                    var m = no.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    try { m?.Invoke(no, null); } catch (System.Exception e) { Debug.LogWarning("[Cidade Velha] OnValidate: " + e.Message); }
                }
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        /// <summary>Verificações rápidas no editor: gemas fora de colisores e com chão, materiais em falta, eixos.</summary>
        static void Validate(Transform map, GemGrabManager gm, StringBuilder log)
        {
            Physics.SyncTransforms();
            int bad = 0;
            foreach (var l in gm.landingPoints)
            {
                var hits = Physics.OverlapSphere(l.position, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool ground = Physics.Raycast(l.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f) && hit.point.y < l.position.y;
                if (hits.Length > 0 || !ground)
                {
                    bad++;
                    log.AppendLine($"ATENÇÃO: {l.name} {(hits.Length > 0 ? "dentro de " + hits[0].name : "sem chão por baixo")}.");
                }
            }
            log.AppendLine(bad == 0 ? "Aterragens: todas livres e com chão." : $"Aterragens com problemas: {bad}.");

            int missing = 0;
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader") missing++;
            log.AppendLine(missing == 0 ? "Materiais: sem referências em falta." : $"ATENÇÃO: {missing} slots de material em falta.");

            // Eixo das equipas (o mapa é simétrico; só para confirmar a orientação no import)
            Vector3 blue = Vector3.zero, red = Vector3.zero;
            int nb = 0, nr = 0;
            foreach (var sp in map.GetComponentsInChildren<SpawnPoint>())
            {
                if (sp.team == 0) { blue += sp.transform.position; nb++; } else { red += sp.transform.position; nr++; }
            }
            if (nb > 0 && nr > 0)
                log.AppendLine($"Equipa azul em {(blue / nb):F1}, vermelha em {(red / nr):F1} (deviam estar a ±22 m no mesmo eixo, y≈0).");
        }
    }
}
