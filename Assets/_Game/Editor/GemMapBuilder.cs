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
    /// <summary>Tipo de shader de um material de mapa.</summary>
    public enum GemMapShader
    {
        Toon,        // FPSLAN/Toon (opaco, sem contorno)
        Outline,     // FPSLAN/ToonOutline
        Alpha,       // FPSLAN/FxAlpha (transparente, sem luz: água, espuma)
    }

    /// <summary>Material do Blender → material do jogo. Cores iguais às do script do Blender.</summary>
    public struct GemMapMat
    {
        public string name, texture;
        public Color color, emission;
        public GemMapShader shader;
        public float topLight;

        public GemMapMat(string n, string tex, Color c, bool outline = true, Color emi = default, float top = 0.15f)
        {
            name = n; texture = tex; color = c; shader = outline ? GemMapShader.Outline : GemMapShader.Toon; emission = emi; topLight = top;
        }

        public static GemMapMat Alpha(string n, string tex, Color c) =>
            new GemMapMat(n, tex, c, false) { shader = GemMapShader.Alpha, topLight = 0f };
    }

    /// <summary>FBX auxiliar do mapa transformado num prefab próprio (ex.: água da Buracona).</summary>
    public class GemMapAuxModel
    {
        public string fbx;              // nome do ficheiro na pasta Models
        public string prefabPath;
        public System.Action<GameObject, Dictionary<string, Material>> configure;
    }

    /// <summary>Tudo o que distingue um mapa Pique-Gemas dos outros. As regras e a rede são as mesmas.</summary>
    public class GemMapConfig
    {
        public string displayName;                  // "Cidade Velha"
        public string artRoot;                      // Assets/_Game/Art/Environment/<Ilha>/<Mapa>
        public string filePrefix;                   // prefixo dos FBX: <prefixo>_Static.fbx, _GemGenerator.fbx
        public string materialPrefix;               // M_CV_ → M_CV_Telha.mat
        public string scenePath;
        public string mapPrefabPath;
        public string sceneRootName;                // nome da instância do mapa na cena
        public string mapName;                      // MapInfo.mapName
        public GemMapMat[] materials;
        public GemMapAuxModel[] auxModels = new GemMapAuxModel[0];

        // Gema: um mapa constrói o prefab (Cidade Velha); os outros reutilizam-no.
        public string gemPrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_Gema.prefab";
        public string gemFbx;                       // se definido, (re)constrói o prefab da gema a partir deste FBX

        // Emissor (nomes dos objetos no FBX)
        public string gemSpawnName = "GEM_Spawn";
        public string emitterHaloName, emitterBaseName;
        public string[] emitterRendererNames = new string[0];
        public GemEmitterStyle emitterStyle = GemEmitterStyle.Pelourinho;
        public string waterSurfaceName;
        public float arcHeight = 2.2f;
        public Color emitterIdleEmission = new Color(0.25f, 0.12f, 0.45f);
        public Color emitterWarnEmission = new Color(1.1f, 0.7f, 1.6f);

        // Ambiente
        public Color sunColor = new Color(1f, 0.93f, 0.8f);
        public float sunIntensity = 1.25f;
        public Vector3 sunEuler = new Vector3(48f, -35f, 0f);
        public Color ambientSky = new Color(0.7f, 0.78f, 0.95f);
        public Color ambientEquator = new Color(0.66f, 0.62f, 0.58f);
        public Color ambientGround = new Color(0.42f, 0.36f, 0.3f);
        public Color fogColor = new Color(0.76f, 0.86f, 0.96f);
        public float fogStart = 50f, fogEnd = 220f;
        public float menuRadius = 42f, menuHeight = 28f;
        public float fallRecoveryY = -20f;
        public string expectedSize = "";            // texto para o relatório (ex.: "≈ 41 × 56")

        public string ModelsPath => artRoot + "/Models";
        public string TexturesPath => artRoot + "/Textures";
        public string MaterialsPath => artRoot + "/Materials";
        public string Model(string suffix) => ModelsPath + "/" + filePrefix + "_" + suffix + ".fbx";
        public string StaticFbx => Model("Static");
        public string GeneratorFbx => Model("GemGenerator");
    }

    /// <summary>
    /// Construção partilhada dos mapas Pique-Gemas a partir dos FBX do Blender: materiais cartoon, colisores
    /// (convenções de nomes abaixo), arbustos do jogo, spawns por equipa, prefabs e uma cena PRÓPRIA por mapa.
    /// Não toca em Arena.unity, Player.prefab (só o referencia) nem nos materiais/dados do ProjectSetup.
    ///
    /// Convenções de nomes no FBX:
    ///   COL_Box_*  → BoxCollider (limites da malha)   ·   COL_Mesh_* → MeshCollider convexo
    ///   COL_Tri_*  → MeshCollider não convexo (chão com buracos, interior de crateras)
    ///   *_Pass     → layer Ignore Raycast: trava o movimento mas os tiros passam (ex.: borda da Buracona)
    ///   BUSH_<nome>[_S<seed>][_D<densidade %>] → arbusto do jogo (Bush + LeafBush), sem colisão
    ///   BSHV_*     → folhagem só para pré-visualização no Blender (removida)
    ///   SPAWN_Blue_* / SPAWN_Red_* · WP_* (bots) · GEM_Spawn · GEM_Land_* · GEN_* (apresentação do emissor)
    /// </summary>
    public static class GemMapBuilder
    {
        public static bool HasModels(GemMapConfig cfg, out string missing)
        {
            missing = null;
            foreach (var p in new[] { cfg.StaticFbx, cfg.GeneratorFbx })
                if (!File.Exists(p)) { missing = p; return false; }
            foreach (var a in cfg.auxModels)
                if (!File.Exists(cfg.ModelsPath + "/" + a.fbx)) { missing = cfg.ModelsPath + "/" + a.fbx; return false; }
            if (cfg.gemFbx != null && !File.Exists(cfg.gemFbx)) { missing = cfg.gemFbx; return false; }
            if (cfg.gemFbx == null && !File.Exists(cfg.gemPrefabPath)) { missing = cfg.gemPrefabPath + " (constrói primeiro a Cidade Velha)"; return false; }
            return true;
        }

        public static void BuildFromMenu(GemMapConfig cfg, string blenderGuide)
        {
            if (!HasModels(cfg, out var missing))
            {
                EditorUtility.DisplayDialog(cfg.displayName, "Falta: " + missing + "\n\nCorre primeiro o script do Blender (" + blenderGuide +
                    ") e copia as pastas FBX e Textures para " + cfg.artRoot + ".", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorUtility.DisplayDialog(cfg.displayName, Build(cfg), "OK");
        }

        public static void OpenScene(GemMapConfig cfg)
        {
            if (File.Exists(cfg.scenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(cfg.scenePath);
        }

        // ================================================================== construção

        public static string Build(GemMapConfig cfg)
        {
            var log = new StringBuilder();
            EnsureFolder(cfg.MaterialsPath);
            EnsureFolder(Path.GetDirectoryName(cfg.mapPrefabPath).Replace('\\', '/'));

            ConfigureTextures(cfg);
            var mats = CreateMaterials(cfg);
            ConfigureModel(cfg.StaticFbx, false, mats, true);
            ConfigureModel(cfg.GeneratorFbx, false, mats, false);
            var anim = cfg.Model("Emissor_Anim");
            if (File.Exists(anim)) ConfigureModel(anim, true, mats, false);
            foreach (var a in cfg.auxModels) ConfigureModel(cfg.ModelsPath + "/" + a.fbx, false, mats, false);

            GameObject gemPrefab;
            if (cfg.gemFbx != null)
            {
                ConfigureModel(cfg.gemFbx, true, mats, false);
                gemPrefab = BuildGemPrefab(cfg, mats);
            }
            else gemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.gemPrefabPath);

            var mapPrefab = BuildMapPrefab(cfg, log);
            var aux = new List<GameObject>();
            foreach (var a in cfg.auxModels) aux.Add(BuildAuxPrefab(cfg, a, mats, log));
            BuildScene(cfg, mapPrefab, aux, gemPrefab, log);

            var summary = "Cena criada: " + cfg.scenePath + "\nPrefabs: " + cfg.mapPrefabPath + ", " + cfg.gemPrefabPath + "\n\n" + log;
            Debug.Log("[" + cfg.displayName + "] " + summary);
            return summary;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ConfigureTextures(GemMapConfig cfg)
        {
            if (!AssetDatabase.IsValidFolder(cfg.TexturesPath)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { cfg.TexturesPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
                bool dirty = ti.wrapMode != TextureWrapMode.Repeat || ti.maxTextureSize != 1024 || !ti.mipmapEnabled ||
                    ti.textureShape != TextureImporterShape.Texture2D || ti.textureType != TextureImporterType.Default;
                ti.textureType = TextureImporterType.Default;
                ti.textureShape = TextureImporterShape.Texture2D; // .meta mínimos importam como Cubemap
                ti.sRGBTexture = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = 1024;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                if (dirty) ti.SaveAndReimport();
            }
        }

        static Shader FindShader(GemMapShader kind)
        {
            Shader s = kind switch
            {
                GemMapShader.Outline => Shader.Find("FPSLAN/ToonOutline"),
                GemMapShader.Alpha => Shader.Find("FPSLAN/FxAlpha"),
                _ => Shader.Find("FPSLAN/Toon"),
            };
            if (s == null) s = Shader.Find(kind == GemMapShader.Alpha ? "Sprites/Default" : "Standard");
            return s;
        }

        static Dictionary<string, Material> CreateMaterials(GemMapConfig cfg)
        {
            var dict = new Dictionary<string, Material>();
            foreach (var spec in cfg.materials)
            {
                var path = cfg.MaterialsPath + "/" + spec.name.Replace("MAT_", cfg.materialPrefix) + ".mat";
                var shader = FindShader(spec.shader);
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
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(cfg.TexturesPath + "/" + spec.texture + ".png");
                    if (tex != null) m.mainTexture = tex;
                }
                if (spec.shader == GemMapShader.Alpha)
                {
                    Set(m, "_VertexColor", 0f);
                }
                else
                {
                    Set(m, "_Emission", spec.emission);
                    Set(m, "_TopLight", spec.topLight);
                    Set(m, "_ShadeColor", new Color(0.72f, 0.7f, 0.86f));
                    Set(m, "_RimColor", new Color(1f, 0.96f, 0.9f, 0.18f));
                    if (spec.shader == GemMapShader.Outline)
                    {
                        Set(m, "_OutlineWidth", 0.0018f);
                        Set(m, "_OutlineColor", Color.Lerp(spec.color, Color.black, 0.75f));
                    }
                }
                EditorUtility.SetDirty(m);
                dict[spec.name] = m;
            }
            AssetDatabase.SaveAssets();
            return dict;
        }

        static void Set(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        static void Set(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }

        /// <summary>Import em metros, sem câmaras/luzes, materiais do Blender remapeados para os do jogo.</summary>
        static void ConfigureModel(string path, bool animations, Dictionary<string, Material> mats, bool readable)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.isReadable = readable; // MeshColliders (degraus, chão com buraco)
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

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var r = FindDeep(c, name);
                if (r != null) return r;
            }
            return null;
        }

        public static void CollectDeep(Transform root, string prefix, List<Transform> into)
        {
            if (root.name.StartsWith(prefix)) into.Add(root);
            foreach (Transform c in root) CollectDeep(c, prefix, into);
        }

        // ------------------------------------------------------------------ gema

        static GameObject BuildGemPrefab(GemMapConfig cfg, Dictionary<string, Material> mats)
        {
            var root = new GameObject("PF_Gema");
            var model = Unpacked(cfg.gemFbx, "Modelo");
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
                if (mats.TryGetValue("MAT_Gema", out var gm)) r.sharedMaterial = gm;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, cfg.gemPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ cenário

        static void ConvertColliders(Transform root, StringBuilder log)
        {
            int boxes = 0, convex = 0, tri = 0, pass = 0;
            var cols = new List<Transform>();
            CollectDeep(root, "COL_", cols);
            foreach (var t in cols)
            {
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mesh = mf.sharedMesh;
                if (t.name.StartsWith("COL_Mesh_") || t.name.StartsWith("COL_Tri_"))
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = t.name.StartsWith("COL_Mesh_");
                    if (mc.convex) convex++; else tri++;
                }
                else
                {
                    var b = t.gameObject.AddComponent<BoxCollider>();
                    b.center = mesh.bounds.center;
                    b.size = mesh.bounds.size;
                    boxes++;
                }
                if (t.name.Contains("_Pass")) { t.gameObject.layer = 2; pass++; } // Ignore Raycast: tiros passam
                Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(mf);
            }
            log.AppendLine($"Colisores: {boxes} caixas, {convex} convexos, {tri} malhas (chão/buraco); {pass} deixam passar tiros.");
        }

        static readonly Regex s_Seed = new Regex(@"_S(\d+)");
        static readonly Regex s_Density = new Regex(@"_D(\d+)");

        static void ConvertBushes(Transform root, StringBuilder log)
        {
            // Zona BUSH_* → componente Bush do projeto (atravessa-se, oculta, revela por proximidade/ataque/dano)
            // + folhas LeafBush com a mesma seed/densidade da pré-visualização do Blender.
            var bushZones = new List<Transform>();
            CollectDeep(root, "BUSH_", bushZones);
            var bushParent = new GameObject("Arbustos").transform;
            bushParent.SetParent(root, false);
            foreach (var z in bushZones)
            {
                var zr = z.GetComponent<Renderer>();
                if (zr == null) continue;
                var wb = zr.bounds; // em metros e no espaço do mundo, independente da rotação do import
                var go = new GameObject();
                go.transform.SetParent(bushParent, false);
                go.transform.position = new Vector3(wb.center.x, wb.min.y, wb.center.z);
                var bush = go.AddComponent<Bush>();
                bush.halfExtents = new Vector2(wb.extents.x, wb.extents.z);
                bush.height = wb.size.y + 0.2f;
                int seed = BushTools.SeedFor(go.transform.position);
                var ms = s_Seed.Match(z.name);
                if (ms.Success) seed = int.Parse(ms.Groups[1].Value);
                float density = 1f;
                var md = s_Density.Match(z.name);
                if (md.Success) density = Mathf.Clamp(int.Parse(md.Groups[1].Value) / 100f, 0.1f, 2f);
                go.name = s_Density.Replace(s_Seed.Replace(z.name, ""), "");
                var leaves = BushTools.AddLeaves(bush, seed);
                if (!Mathf.Approximately(density, 1f))
                {
                    leaves.density = density;
                    leaves.Rebuild();
                }
            }
            foreach (var z in bushZones) Object.DestroyImmediate(z.gameObject);
            var previewLeaves = new List<Transform>();
            CollectDeep(root, "BSHV_", previewLeaves);
            foreach (var t in previewLeaves) Object.DestroyImmediate(t.gameObject);
            log.AppendLine($"Arbustos: {bushParent.childCount} (sistema Bush + LeafBush, sem colisores).");
        }

        static void ConvertSpawns(Transform root, StringBuilder log)
        {
            var spawns = new List<Transform>();
            CollectDeep(root, "SPAWN_", spawns);
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
        }

        static void FinishStatic(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("GEN_") || r.name.StartsWith("WTR_")) r.shadowCastingMode = ShadowCastingMode.Off;
                if (r.name.StartsWith("GRD_") || r.name.StartsWith("BG_Mar")) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!t.name.StartsWith("GEN_") && !t.name.StartsWith("GEM_") && !t.name.StartsWith("WTR_") && t.GetComponentInParent<Bush>() == null)
                    t.gameObject.isStatic = true; // arbustos, água e emissor fora do static batching (animados por código)
        }

        static GameObject BuildMapPrefab(GemMapConfig cfg, StringBuilder log)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(cfg.mapPrefabPath));
            var stat = Unpacked(cfg.StaticFbx, "Cenario");
            stat.transform.SetParent(root.transform, false);
            var gen = Unpacked(cfg.GeneratorFbx, "GeradorGemas");
            gen.transform.SetParent(root.transform, false);

            ConvertColliders(root.transform, log);
            ConvertBushes(root.transform, log);
            ConvertSpawns(root.transform, log);
            FinishStatic(root.transform);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, cfg.mapPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject BuildAuxPrefab(GemMapConfig cfg, GemMapAuxModel aux, Dictionary<string, Material> mats, StringBuilder log)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(aux.prefabPath));
            var model = Unpacked(cfg.ModelsPath + "/" + aux.fbx, "Modelo");
            model.transform.SetParent(root.transform, false);
            ConvertColliders(root.transform, log);
            FinishStatic(root.transform);
            aux.configure?.Invoke(root, mats);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, aux.prefabPath);
            Object.DestroyImmediate(root);
            log.AppendLine("Prefab: " + aux.prefabPath);
            return prefab;
        }

        // ------------------------------------------------------------------ cena

        static Transform FindInScene(List<GameObject> roots, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var r in roots)
            {
                var t = FindDeep(r.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        static void BuildScene(GemMapConfig cfg, GameObject mapPrefab, List<GameObject> auxPrefabs, GameObject gemPrefab, StringBuilder log)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = cfg.sunIntensity;
            sun.color = cfg.sunColor;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(cfg.sunEuler);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = cfg.ambientSky;
            RenderSettings.ambientEquatorColor = cfg.ambientEquator;
            RenderSettings.ambientGroundColor = cfg.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = cfg.fogColor;
            RenderSettings.fogStartDistance = cfg.fogStart;
            RenderSettings.fogEndDistance = cfg.fogEnd;
            var sky = AssetDatabase.LoadAssetAtPath<Material>(ProjectSetup.Root + "/Materials/M_Sky.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            var map = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab);
            map.name = cfg.sceneRootName;
            var roots = new List<GameObject> { map };
            foreach (var a in auxPrefabs)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(a);
                inst.transform.SetParent(map.transform, true);
                roots.Add(inst);
            }

            // Waypoints dos bots e limites a partir dos marcadores
            var wps = new List<Transform>();
            CollectDeep(map.transform, "WP_", wps);
            var waypoints = new List<Vector3>();
            foreach (var w in wps) waypoints.Add(w.position);
            float half = 25.5f;
            Collider floor = null;
            foreach (var c in map.GetComponentsInChildren<Collider>(true))
                if (c.name.StartsWith("COL_Box_Chao") || c.name.StartsWith("COL_Tri_Chao")) { floor = c; break; }
            if (floor != null)
            {
                Physics.SyncTransforms();
                var b = floor.bounds;
                half = Mathf.Max(b.extents.x, b.extents.z) - 1.5f;
                log.AppendLine($"Chão: {b.size.x:0.0} × {b.size.z:0.0} m, topo y={b.max.y:0.00} (esperado {cfg.expectedSize}, y=0).");
                if (b.size.x > 400f || b.size.x < 10f) log.AppendLine("ATENÇÃO: escala do FBX inesperada — confirma 'Convert Units'.");
            }
            else log.AppendLine("ATENÇÃO: colisor de chão (COL_Box_Chao / COL_Tri_Chao) não encontrado.");
            var info = new GameObject("MapInfo").AddComponent<MapInfo>();
            info.mapName = cfg.mapName;
            info.halfSize = half;
            info.fallRecoveryY = cfg.fallRecoveryY;
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
            menuCam.radius = cfg.menuRadius;
            menuCam.height = cfg.menuHeight;
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, cfg.menuHeight, -cfg.menuRadius);
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
            match.overrideMenuMode = true;
            match.sceneMode = GameMode.GemGrab;

            // Pique-Gemas (NetworkObject da cena: estado replicado e sincronizado para quem entra a meio)
            var gemGo = new GameObject("PiqueGemas");
            gemGo.AddComponent<NetworkObject>();
            var gm = gemGo.AddComponent<GemGrabManager>();
            var vis = gemGo.AddComponent<GemGrabVisuals>();
            gemGo.AddComponent<GemGrabHud>();
            gm.gemPrefab = gemPrefab;
            gm.arcHeight = cfg.arcHeight;
            gm.emitterIdleEmission = cfg.emitterIdleEmission;
            gm.emitterWarnEmission = cfg.emitterWarnEmission;
            gm.spawnPoint = FindInScene(roots, cfg.gemSpawnName);
            var lands = new List<Transform>();
            CollectDeep(map.transform, "GEM_Land_", lands);
            lands.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            gm.landingPoints = lands.ToArray();
            gm.emitterHalo = FindInScene(roots, cfg.emitterHaloName);
            gm.emitterBase = FindInScene(roots, cfg.emitterBaseName);
            var emitterRenderers = new List<Renderer>();
            if (gm.emitterHalo != null) emitterRenderers.AddRange(gm.emitterHalo.GetComponentsInChildren<Renderer>());
            if (gm.emitterBase != null) emitterRenderers.AddRange(gm.emitterBase.GetComponentsInChildren<Renderer>());
            foreach (var n in cfg.emitterRendererNames)
            {
                var t = FindInScene(roots, n);
                if (t != null) foreach (var r in t.GetComponentsInChildren<Renderer>()) if (!emitterRenderers.Contains(r)) emitterRenderers.Add(r);
            }
            gm.emitterRenderers = emitterRenderers.ToArray();
            vis.style = cfg.emitterStyle;
            vis.waterSurface = FindInScene(roots, cfg.waterSurfaceName);
            if (gm.spawnPoint == null) log.AppendLine("ATENÇÃO: " + cfg.gemSpawnName + " não encontrado no FBX do gerador.");
            log.AppendLine($"Gerador ({cfg.emitterStyle}): {lands.Count} pontos de aterragem, {cfg.gemSpawnName} a y={(gm.spawnPoint != null ? gm.spawnPoint.position.y : 0f):0.00}.");

            Validate(map.transform, gm, log);

            EnsureFolder(Path.GetDirectoryName(cfg.scenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(scene, cfg.scenePath);

            // Na lista de build (necessário para os NetworkObjects da cena). A ordem existente não muda: vai para o fim.
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == cfg.scenePath))
            {
                list.Add(new EditorBuildSettingsScene(cfg.scenePath, true));
                EditorBuildSettings.scenes = list.ToArray();
                log.AppendLine("Cena adicionada ao fim das Build Settings (ProjectSettings/EditorBuildSettings.asset — combinar com o Nico antes do commit).");
            }

            // Reabrir para gerar GlobalObjectIdHash dos NetworkObjects da cena (como no ProjectSetup)
            var reopened = EditorSceneManager.OpenScene(cfg.scenePath, OpenSceneMode.Single);
            foreach (var go in reopened.GetRootGameObjects())
                foreach (var no in go.GetComponentsInChildren<NetworkObject>(true))
                {
                    var m = no.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    try { m?.Invoke(no, null); } catch (System.Exception e) { Debug.LogWarning("[" + cfg.displayName + "] OnValidate: " + e.Message); }
                }
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        /// <summary>Verificações rápidas no editor: gemas fora de colisores e com chão, materiais em falta, eixos.</summary>
        static void Validate(Transform map, GemGrabManager gm, StringBuilder log)
        {
            Physics.SyncTransforms();
            int mask = Physics.DefaultRaycastLayers | (1 << 2);
            int bad = 0;
            foreach (var l in gm.landingPoints)
            {
                var hits = Physics.OverlapSphere(l.position, 0.3f, mask, QueryTriggerInteraction.Ignore);
                bool ground = Physics.Raycast(l.position + Vector3.up * 0.5f, Vector3.down, out var hit, 3f) && hit.point.y < l.position.y &&
                    hit.point.y > l.position.y - gm.gemFloatHeight - 0.3f;
                if (hits.Length > 0 || !ground)
                {
                    bad++;
                    log.AppendLine($"ATENÇÃO: {l.name} {(hits.Length > 0 ? "dentro de " + hits[0].name : "sem chão firme por baixo")}.");
                }
            }
            log.AppendLine(bad == 0 ? "Aterragens: todas livres e com chão." : $"Aterragens com problemas: {bad}.");

            int missing = 0, slots = 0;
            foreach (var r in map.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    slots++;
                    if (m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader") missing++;
                }
            log.AppendLine(missing == 0 ? $"Materiais: {slots} slots, sem referências em falta." : $"ATENÇÃO: {missing} slots de material em falta.");

            // Eixo das equipas (os mapas são simétricos; só para confirmar a orientação no import)
            Vector3 blue = Vector3.zero, red = Vector3.zero;
            int nb = 0, nr = 0;
            foreach (var sp in map.GetComponentsInChildren<SpawnPoint>())
            {
                if (sp.team == 0) { blue += sp.transform.position; nb++; } else { red += sp.transform.position; nr++; }
            }
            if (nb > 0 && nr > 0)
                log.AppendLine($"Equipa azul em {(blue / nb):F1}, vermelha em {(red / nr):F1} (lados opostos do mesmo eixo, y≈0).");
        }
    }
}
