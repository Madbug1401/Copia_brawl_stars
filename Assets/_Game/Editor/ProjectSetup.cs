using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Gera o conteúdo do jogo com APIs do Editor: materiais cartoon, cena Arena (mapas "Pradaria" e "Bosque"), prefab Player,
    /// NetworkManager e iluminação. Corre sozinho na primeira abertura e sempre que a versão do conteúdo sobe
    /// (SetupVersion); depois pode ser repetido pelo menu FPS-LAN.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        /// <summary>Subir este número faz o projeto regenerar a cena/prefab/dados automaticamente ao abrir.</summary>
        public const int SetupVersion = 3;

        public const string Root = "Assets/_Game";
        public const string ScenePath = Root + "/Scenes/Arena.unity";
        public const string PlayerPrefabPath = Root + "/Prefabs/Player.prefab";
        const string MaterialsPath = Root + "/Materials";
        const string ArtResourcesPath = Root + "/Resources/Art";
        const string VersionFile = Root + "/Data/setup_version.txt";

        static ProjectSetup()
        {
            EditorApplication.delayCall += AutoRun;
        }

        static int InstalledVersion
        {
            get
            {
                try
                {
                    if (File.Exists(VersionFile) && int.TryParse(File.ReadAllText(VersionFile).Trim(), out var v)) return v;
                }
                catch { }
                return 0;
            }
        }

        static void AutoRun()
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool missing = !File.Exists(ScenePath);
            bool outdated = InstalledVersion < SetupVersion;
            if (!missing && !outdated) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoRun;
                return;
            }

            Debug.Log(missing
                ? "[FPS-LAN] Primeira abertura: a gerar cena Arena, prefab Player, materiais e dados..."
                : "[FPS-LAN] Conteúdo novo (versão " + SetupVersion + "): a regenerar Brawlers, materiais, prefab Player e os mapas...");
            BrawlerAssetBuilder.Build();
            BuildAll();
        }

        [MenuItem("FPS-LAN/Regenerar Arena + Player", priority = 1)]
        static void RebuildMenu()
        {
            if (!EditorUtility.DisplayDialog("FPS-LAN",
                    "Isto recria a cena Arena (mapas Pradaria e Bosque), o prefab Player e os materiais (alterações manuais nesses ficheiros perdem-se). Continuar?",
                    "Regenerar", "Cancelar"))
                return;
            BuildAll();
        }

        [MenuItem("FPS-LAN/Adicionar cobertura destrutível de teste", priority = 3)]
        static void AddTestCoverMenu()
        {
            var mats = CreateMaterials();
            var go = CreateDestructibleCover(mats.crateDark, new Vector3(3.5f, 0.75f, -7f), new Vector3(2.6f, 1.5f, 0.8f));
            Selection.activeGameObject = go;
            FinalizeSceneNetworkObjects();
        }

        /// <summary>Cria uma cobertura destrutível (NetworkObject na cena + DestructibleCover).</summary>
        public static GameObject CreateDestructibleCover(Material mat, Vector3 center, Vector3 size, int health = 150)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "DestructibleCover";
            go.transform.position = center;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<NetworkObject>();
            var cover = go.AddComponent<DestructibleCover>();
            cover.maxHealth = health;
            return go;
        }

        /// <summary>Guarda a cena ativa e gera os GlobalObjectIdHash dos NetworkObjects colocados na cena.</summary>
        public static void FinalizeSceneNetworkObjects()
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var no in root.GetComponentsInChildren<NetworkObject>(true))
                    InvokeOnValidate(no);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("FPS-LAN/Abrir cena Arena", priority = 2)]
        static void OpenArena()
        {
            if (File.Exists(ScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void BuildAll()
        {
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Data");
            EnsureFolder(MaterialsPath);
            EnsureFolder(ArtResourcesPath);

            ConfigurePlayerSettings();
            EnsureLayer(8, PlayerVisuals.ViewModelLayerName);
            CreateArtMaterials();
            var mats = CreateMaterials();
            var player = CreatePlayerPrefab();
            CreateScene(player, mats);

            AssetDatabase.SaveAssets();
            try { File.WriteAllText(VersionFile, SetupVersion.ToString()); } catch { }
            AssetDatabase.ImportAsset(VersionFile);
            Debug.Log("[FPS-LAN] Pronto. Cena: " + ScenePath + " — carrega Play e escolhe Host ou Treino.");
        }

        // ---------------------------------------------------------------- Settings

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "FPS-LAN";
            PlayerSettings.companyName = "FPS-LAN";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
        }

        /// <summary>Garante que a camada do viewmodel existe (câmara própria para as mãos/arma).</summary>
        static void EnsureLayer(int index, string name)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null || layers.arraySize <= index) return;
            var current = layers.GetArrayElementAtIndex(index).stringValue;
            if (current == name) return;
            if (!string.IsNullOrEmpty(current))
            {
                Debug.LogWarning("[FPS-LAN] A camada " + index + " já se chama '" + current + "'. O viewmodel usa a câmara principal.");
                return;
            }
            layers.GetArrayElementAtIndex(index).stringValue = name;
            so.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------- Materials

        class Mats
        {
            public Material grass, grassForest, path, stone, stoneTop, crateDark, crateLight, bush, bushLight, water, waterFoam, rock, rockLight,
                fence, barrel, metal, pad, padGlow, platform, platformTop, ramp, totem, gem, trunk, leaves, leavesLight, cliff,
                flowerPink, flowerYellow, flowerWhite, flowerCenter, grassTuft, mushroomCap, mushroomStem, spawn, cloud, lily, hill, mountain, snow;
        }

        static Shader ToonShader(bool outline)
        {
            var s = Shader.Find(outline ? "FPSLAN/ToonOutline" : "FPSLAN/Toon");
            if (s != null && !ShaderUtil.ShaderHasError(s)) return s;
            Debug.LogWarning("[FPS-LAN] Shader cartoon indisponível; a usar Standard.");
            return Shader.Find("Standard");
        }

        /// <summary>Materiais base usados pelos visuais gerados em runtime (Resources → entram no build com os shaders).</summary>
        static void CreateArtMaterials()
        {
            MakeMat(ArtResourcesPath + "/M_Toon.mat", ToonShader(false), Color.white, m => { });
            MakeMat(ArtResourcesPath + "/M_ToonOutline.mat", ToonShader(true), Color.white, m => { });
            foreach (var n in new[] { "FxAdditive", "FxAlpha" })
            {
                var sh = Shader.Find("FPSLAN/" + n);
                if (sh == null || ShaderUtil.ShaderHasError(sh)) sh = Shader.Find("Sprites/Default");
                MakeMat(ArtResourcesPath + "/M_" + n + ".mat", sh, Color.white, m => { });
            }
        }

        static Mats CreateMaterials()
        {
            var toon = ToonShader(false);
            var outl = ToonShader(true);
            return new Mats
            {
                grass = MakeMat(MaterialsPath + "/M_Grass.mat", toon, new Color(0.47f, 0.8f, 0.32f), m => { Set(m, "_Checker", 0.09f); Set(m, "_CheckerScale", 2f); Set(m, "_Noise", 0.1f); Set(m, "_ShadeColor", new Color(0.55f, 0.7f, 0.85f)); }),
                grassForest = MakeMat(MaterialsPath + "/M_GrassForest.mat", toon, new Color(0.36f, 0.66f, 0.3f), m => { Set(m, "_Checker", 0.08f); Set(m, "_CheckerScale", 2f); Set(m, "_Noise", 0.14f); Set(m, "_ShadeColor", new Color(0.6f, 0.55f, 0.75f)); }),
                path = MakeMat(MaterialsPath + "/M_Path.mat", toon, new Color(0.95f, 0.82f, 0.56f), m => { Set(m, "_Noise", 0.12f); Set(m, "_Checker", 0.04f); Set(m, "_CheckerScale", 1f); }),
                stone = MakeMat(MaterialsPath + "/M_Stone.mat", outl, new Color(0.6f, 0.65f, 0.76f), m => { Set(m, "_TopLight", 0.25f); Set(m, "_OutlineWidth", 0.0022f); Set(m, "_OutlineColor", new Color(0.2f, 0.22f, 0.32f)); }),
                stoneTop = MakeMat(MaterialsPath + "/M_StoneTop.mat", toon, new Color(0.8f, 0.84f, 0.92f), m => { Set(m, "_TopLight", 0.15f); }),
                crateDark = MakeMat(MaterialsPath + "/M_CrateDark.mat", outl, new Color(0.62f, 0.38f, 0.2f), m => { Set(m, "_OutlineWidth", 0.0022f); Set(m, "_OutlineColor", new Color(0.25f, 0.13f, 0.06f)); }),
                crateLight = MakeMat(MaterialsPath + "/M_CrateLight.mat", toon, new Color(0.93f, 0.68f, 0.4f), m => { Set(m, "_TopLight", 0.15f); }),
                bush = MakeMat(MaterialsPath + "/M_Bush.mat", outl, new Color(0.24f, 0.62f, 0.26f), m => { Set(m, "_OutlineWidth", 0.003f); Set(m, "_OutlineColor", new Color(0.08f, 0.25f, 0.1f)); Set(m, "_RimColor", new Color(0.8f, 1f, 0.6f, 0.35f)); }),
                bushLight = MakeMat(MaterialsPath + "/M_BushLight.mat", outl, new Color(0.38f, 0.76f, 0.3f), m => { Set(m, "_OutlineWidth", 0.003f); Set(m, "_OutlineColor", new Color(0.08f, 0.25f, 0.1f)); Set(m, "_RimColor", new Color(0.9f, 1f, 0.7f, 0.35f)); }),
                water = MakeMat(MaterialsPath + "/M_Water.mat", toon, new Color(0.25f, 0.62f, 0.95f), m => { Set(m, "_Checker", 0.06f); Set(m, "_CheckerScale", 1.2f); Set(m, "_RimColor", new Color(0.8f, 0.95f, 1f, 0.6f)); Set(m, "_Emission", new Color(0.03f, 0.1f, 0.18f)); }),
                waterFoam = MakeMat(MaterialsPath + "/M_WaterFoam.mat", toon, new Color(0.85f, 0.95f, 1f), m => { }),
                rock = MakeMat(MaterialsPath + "/M_Rock.mat", outl, new Color(0.55f, 0.56f, 0.66f), m => { Set(m, "_TopLight", 0.3f); Set(m, "_OutlineWidth", 0.0025f); Set(m, "_OutlineColor", new Color(0.18f, 0.18f, 0.26f)); }),
                rockLight = MakeMat(MaterialsPath + "/M_RockLight.mat", outl, new Color(0.7f, 0.71f, 0.8f), m => { Set(m, "_TopLight", 0.2f); Set(m, "_OutlineWidth", 0.0025f); Set(m, "_OutlineColor", new Color(0.18f, 0.18f, 0.26f)); }),
                fence = MakeMat(MaterialsPath + "/M_Fence.mat", outl, new Color(0.9f, 0.68f, 0.42f), m => { Set(m, "_OutlineWidth", 0.002f); Set(m, "_OutlineColor", new Color(0.3f, 0.18f, 0.08f)); }),
                barrel = MakeMat(MaterialsPath + "/M_Barrel.mat", outl, new Color(0.8f, 0.3f, 0.18f), m => { Set(m, "_OutlineWidth", 0.0025f); Set(m, "_OutlineColor", new Color(0.25f, 0.08f, 0.05f)); }),
                metal = MakeMat(MaterialsPath + "/M_Metal.mat", toon, new Color(0.3f, 0.32f, 0.38f), m => { }),
                pad = MakeMat(MaterialsPath + "/M_Pad.mat", outl, new Color(0.25f, 0.85f, 0.95f), m => { Set(m, "_Emission", new Color(0.05f, 0.3f, 0.38f)); Set(m, "_OutlineWidth", 0.0025f); }),
                padGlow = MakeMat(MaterialsPath + "/M_PadGlow.mat", toon, new Color(0.8f, 1f, 1f), m => { Set(m, "_Emission", new Color(0.4f, 0.9f, 1f)); }),
                platform = MakeMat(MaterialsPath + "/M_Platform.mat", outl, new Color(0.82f, 0.66f, 0.46f), m => { Set(m, "_TopLight", 0.2f); Set(m, "_OutlineWidth", 0.002f); Set(m, "_OutlineColor", new Color(0.3f, 0.2f, 0.1f)); }),
                platformTop = MakeMat(MaterialsPath + "/M_PlatformTop.mat", toon, new Color(0.96f, 0.86f, 0.6f), m => { Set(m, "_Checker", 0.08f); Set(m, "_CheckerScale", 1f); }),
                ramp = MakeMat(MaterialsPath + "/M_Ramp.mat", outl, new Color(0.95f, 0.78f, 0.35f), m => { Set(m, "_OutlineWidth", 0.002f); Set(m, "_Checker", 0.08f); Set(m, "_CheckerScale", 0.5f); }),
                totem = MakeMat(MaterialsPath + "/M_Totem.mat", outl, new Color(0.55f, 0.48f, 0.68f), m => { Set(m, "_TopLight", 0.25f); Set(m, "_OutlineWidth", 0.003f); }),
                gem = MakeMat(MaterialsPath + "/M_Gem.mat", outl, new Color(0.75f, 0.45f, 1f), m => { Set(m, "_Emission", new Color(0.45f, 0.2f, 0.7f)); Set(m, "_RimColor", new Color(1f, 0.9f, 1f, 0.8f)); Set(m, "_OutlineWidth", 0.003f); }),
                trunk = MakeMat(MaterialsPath + "/M_Trunk.mat", outl, new Color(0.55f, 0.36f, 0.22f), m => { Set(m, "_OutlineWidth", 0.003f); }),
                leaves = MakeMat(MaterialsPath + "/M_Leaves.mat", outl, new Color(0.2f, 0.55f, 0.28f), m => { Set(m, "_OutlineWidth", 0.003f); Set(m, "_OutlineColor", new Color(0.05f, 0.2f, 0.08f)); }),
                leavesLight = MakeMat(MaterialsPath + "/M_LeavesLight.mat", outl, new Color(0.35f, 0.7f, 0.3f), m => { Set(m, "_OutlineWidth", 0.003f); Set(m, "_OutlineColor", new Color(0.05f, 0.2f, 0.08f)); }),
                cliff = MakeMat(MaterialsPath + "/M_Cliff.mat", outl, new Color(0.62f, 0.55f, 0.5f), m => { Set(m, "_TopLight", 0.2f); Set(m, "_OutlineWidth", 0.002f); Set(m, "_OutlineColor", new Color(0.25f, 0.2f, 0.18f)); }),
                flowerPink = MakeMat(MaterialsPath + "/M_FlowerPink.mat", toon, new Color(1f, 0.5f, 0.75f), m => { }),
                flowerYellow = MakeMat(MaterialsPath + "/M_FlowerYellow.mat", toon, new Color(1f, 0.88f, 0.3f), m => { }),
                flowerWhite = MakeMat(MaterialsPath + "/M_FlowerWhite.mat", toon, new Color(0.98f, 0.98f, 1f), m => { }),
                flowerCenter = MakeMat(MaterialsPath + "/M_FlowerCenter.mat", toon, new Color(1f, 0.7f, 0.15f), m => { }),
                grassTuft = MakeMat(MaterialsPath + "/M_GrassTuft.mat", toon, new Color(0.35f, 0.7f, 0.25f), m => { }),
                mushroomCap = MakeMat(MaterialsPath + "/M_MushroomCap.mat", outl, new Color(0.95f, 0.25f, 0.2f), m => { Set(m, "_OutlineWidth", 0.002f); }),
                mushroomStem = MakeMat(MaterialsPath + "/M_MushroomStem.mat", toon, new Color(0.98f, 0.94f, 0.85f), m => { }),
                spawn = MakeMat(MaterialsPath + "/M_Spawn.mat", toon, new Color(1f, 0.92f, 0.55f), m => { Set(m, "_Emission", new Color(0.25f, 0.2f, 0.05f)); }),
                cloud = MakeMat(MaterialsPath + "/M_Cloud.mat", toon, new Color(1f, 1f, 1f), m => { Set(m, "_Emission", new Color(0.35f, 0.38f, 0.42f)); Set(m, "_ShadeColor", new Color(0.8f, 0.85f, 0.95f)); }),
                lily = MakeMat(MaterialsPath + "/M_Lily.mat", toon, new Color(0.35f, 0.75f, 0.3f), m => { }),
                hill = MakeMat(MaterialsPath + "/M_Hill.mat", toon, new Color(0.42f, 0.7f, 0.35f), m => { Set(m, "_Noise", 0.08f); }),
                mountain = MakeMat(MaterialsPath + "/M_Mountain.mat", toon, new Color(0.55f, 0.6f, 0.75f), m => { Set(m, "_ShadeColor", new Color(0.7f, 0.75f, 0.95f)); }),
                snow = MakeMat(MaterialsPath + "/M_Snow.mat", toon, new Color(0.97f, 0.98f, 1f), m => { }),
            };
        }

        static Material MakeMat(string path, Shader shader, Color color, System.Action<Material> setup)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            if (m.HasProperty("_Color")) m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            setup(m);
            m.enableInstancing = false;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        static void Set(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        // ---------------------------------------------------------------- Player prefab

        static GameObject CreatePlayerPrefab()
        {
            var root = new GameObject("Player");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 46f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            root.AddComponent<NetworkObject>();
            root.AddComponent<PlayerNetwork>();
            var motor = root.AddComponent<PlayerMotor>();
            root.AddComponent<PlayerHealth>();
            root.AddComponent<PlayerCombat>();
            var visuals = root.AddComponent<PlayerVisuals>();
            root.AddComponent<StatusEffects>();

            var cameraRoot = new GameObject("CameraRoot").transform;
            cameraRoot.SetParent(root.transform, false);
            cameraRoot.localPosition = new Vector3(0f, 1.6f, 0f);

            var aimPivot = new GameObject("AimPivot").transform;
            aimPivot.SetParent(root.transform, false);
            aimPivot.localPosition = new Vector3(0f, 1.32f, 0f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(aimPivot, false);
            muzzle.localPosition = new Vector3(0.32f, 0.02f, 0.7f);

            motor.cameraRoot = cameraRoot;
            motor.aimPivot = aimPivot;

            // O corpo é a rig cartoon do Brawler, criada em runtime (PlayerVisuals/CharacterRig)
            visuals.bodyRenderers = new Renderer[0];
            visuals.tintRenderers = new Renderer[0];
            visuals.thirdPersonMuzzle = muzzle;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            // Garante que o NetworkObject do prefab tem o GlobalObjectIdHash gerado
            var no = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            InvokeOnValidate(no);
            EditorUtility.SetDirty(no);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) ?? prefab;
        }

        static void InvokeOnValidate(Object target)
        {
            if (target == null) return;
            var m = target.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            try { m?.Invoke(target, null); }
            catch (System.Exception e) { Debug.LogWarning("[FPS-LAN] OnValidate falhou em " + target.name + ": " + e.Message); }
        }

        // ---------------------------------------------------------------- Scene

        static void CreateScene(GameObject playerPrefab, Mats mats)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Luz e ambiente: sol quente, sombras suaves, ambiente claro (estilo cartoon)
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.3f;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.66f, 0.75f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.64f, 0.66f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.4f, 0.3f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.86f, 0.98f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 170f;
            RenderSettings.skybox = CreateSkybox();
            RenderSettings.sun = sun;

            // Mapas: todos na mesma cena, afastados uns dos outros; o host escolhe qual se joga (MatchManager.Map)
            var spawns = new GameObject("SpawnPoints").transform;
            BuildArenaMap(0, "Pradaria", "Plataforma central e lagos", ArenaLayout.Build(), ArenaLayout.Half, Vector3.zero, spawns, mats);
            var bosque = BuildArenaMap(1, "Bosque", "Arbustos densos e torres", ArenaLayout.BuildBosque(), ArenaLayout.BosqueHalf, BosqueOffset, spawns, mats);
            // Fim de tarde: sol baixo e quente, nevoeiro alaranjado
            bosque.sunColor = new Color(1f, 0.82f, 0.62f);
            bosque.sunIntensity = 1.1f;
            bosque.sunEuler = new Vector3(32f, 60f, 0f);
            bosque.ambientSky = new Color(0.7f, 0.68f, 0.82f);
            bosque.ambientEquator = new Color(0.64f, 0.56f, 0.5f);
            bosque.ambientGround = new Color(0.3f, 0.34f, 0.24f);
            bosque.fogColor = new Color(0.95f, 0.8f, 0.66f);

            // Sistemas locais
            var systems = new GameObject("Systems");
            systems.AddComponent<CombatSystem>();
            systems.AddComponent<ConnectionManager>();
            systems.AddComponent<GameUI>();

            // Câmara do menu (órbita sobre o mapa)
            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var menuCam = camGo.AddComponent<MenuCamera>();
            menuCam.radius = 40f;
            menuCam.height = 26f;
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 26f, -40f);
            camGo.transform.LookAt(Vector3.zero);

            // NetworkManager + Unity Transport
            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>();
            var utp = nmGo.AddComponent<UnityTransport>();
            if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = utp;
            nm.NetworkConfig.PlayerPrefab = playerPrefab;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.TickRate = 60;
            utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0");

            // MatchManager (NetworkObject colocado na cena)
            var matchGo = new GameObject("MatchManager");
            matchGo.AddComponent<NetworkObject>();
            matchGo.AddComponent<MatchManager>();

            EditorSceneManager.SaveScene(scene, ScenePath);

            // A cena tem de estar nas Build Settings para os NetworkObjects da cena serem reconhecidos
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();

            // Reabrir para gerar GlobalObjectIdHash dos NetworkObjects da cena e guardar
            var reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in reopened.GetRootGameObjects())
                foreach (var no in go.GetComponentsInChildren<NetworkObject>(true))
                    InvokeOnValidate(no);
            EditorSceneManager.MarkSceneDirty(reopened);
            EditorSceneManager.SaveScene(reopened);
        }

        /// <summary>Distância entre mapas: maior do que o alcance da câmara (400 m) para nunca se verem uns aos outros.</summary>
        static readonly Vector3 BosqueOffset = new Vector3(600f, 0f, 0f);

        static MapInfo BuildArenaMap(int index, string name, string tagline, List<Piece> pieces, float half, Vector3 offset, Transform spawns, Mats mats)
        {
            ArenaLayout.Offset(pieces, offset);
            var map = new GameObject("Map_" + name).transform;
            var waypoints = new List<Vector3>();
            BuildMap(map, spawns, pieces, mats, waypoints);
            BuildScenery(map, mats, offset);

            var info = new GameObject("MapInfo_" + name).AddComponent<MapInfo>();
            info.mapIndex = index;
            info.mapName = name;
            info.tagline = tagline;
            info.center = offset;
            info.halfSize = half;
            info.waypoints = waypoints.ToArray();
            return info;
        }

        static Material CreateSkybox()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            var m = MakeMat(MaterialsPath + "/M_Sky.mat", shader, Color.white, mat =>
            {
                Set(mat, "_SunSize", 0.035f);
                Set(mat, "_SunSizeConvergence", 5f);
                Set(mat, "_AtmosphereThickness", 0.75f);
                Set(mat, "_SkyTint", new Color(0.42f, 0.62f, 0.95f));
                Set(mat, "_GroundColor", new Color(0.45f, 0.55f, 0.4f));
                Set(mat, "_Exposure", 1.25f);
            });
            return m;
        }

        // ---------------------------------------------------------------- Mapa a partir das peças

        static void BuildMap(Transform map, Transform spawns, List<Piece> pieces, Mats m, List<Vector3> waypoints)
        {
            var groups = new Dictionary<PieceKind, Transform>();
            Transform Group(PieceKind k)
            {
                if (groups.TryGetValue(k, out var g)) return g;
                g = new GameObject(k.ToString()).transform;
                g.SetParent(map, false);
                groups[k] = g;
                return g;
            }

            int idx = 0;
            foreach (var p in pieces)
            {
                idx++;
                var rot = Quaternion.Euler(0f, p.yaw, 0f);
                switch (p.kind)
                {
                    case PieceKind.Ground:
                        Block(Group(p.kind), "Ground", p.center, p.size, rot, p.variant == 1 ? m.grassForest : m.grass, true);
                        break;

                    case PieceKind.Path:
                        Visual(Group(p.kind), PrimitiveType.Cube, "Path", p.center, p.size, rot, m.path, false);
                        // Pedrinhas nas bordas
                        for (int i = 0; i < 6; i++)
                        {
                            float side = (i % 2 == 0 ? 1f : -1f) * (p.size.x * 0.5f + 0.15f);
                            float along = -p.size.z * 0.5f + (i + 0.5f) * p.size.z / 6f;
                            Visual(Group(p.kind), PrimitiveType.Sphere, "Pebble", p.center + rot * new Vector3(side, 0.02f, along), new Vector3(0.35f, 0.12f, 0.28f), rot, m.rockLight, false);
                        }
                        break;

                    case PieceKind.Wall:
                    {
                        var wall = Block(Group(p.kind), "Wall", p.center, p.size, rot, m.stone, true);
                        // Tampa mais clara (silhueta de bloco)
                        Visual(wall.transform, PrimitiveType.Cube, "Cap", new Vector3(0f, 0.5f, 0f), new Vector3(1.04f, 0.12f / p.size.y, 1.04f), Quaternion.identity, m.stoneTop, true, true);
                        // Linha de pedras na base
                        Visual(wall.transform, PrimitiveType.Cube, "Base", new Vector3(0f, -0.5f + 0.1f / p.size.y, 0f), new Vector3(1.03f, 0.2f / p.size.y, 1.03f), Quaternion.identity, m.rock, true, true);
                        break;
                    }

                    case PieceKind.Crate:
                        CrateStack(Group(p.kind), p, rot, m);
                        break;

                    case PieceKind.Fence:
                        Fence(Group(p.kind), p, rot, m);
                        break;

                    case PieceKind.Rock:
                        Rock(Group(p.kind), p, rot, m, idx);
                        break;

                    case PieceKind.Bush:
                        BushPatch(Group(p.kind), p, rot, m, idx);
                        break;

                    case PieceKind.Water:
                        Pond(Group(p.kind), p, rot, m, idx);
                        break;

                    case PieceKind.Destructible:
                        DestructibleCrate(Group(p.kind), p, rot, m);
                        break;

                    case PieceKind.Barrel:
                        DestructibleBarrel(Group(p.kind), p, rot, m);
                        break;

                    case PieceKind.JumpPad:
                        JumpPadPiece(Group(p.kind), p, rot, m);
                        break;

                    case PieceKind.Spawn:
                    {
                        var sp = new GameObject("Spawn_" + spawns.childCount);
                        sp.transform.SetParent(spawns, false);
                        sp.transform.position = p.center;
                        sp.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f);
                        sp.AddComponent<SpawnPoint>();
                        Visual(sp.transform, PrimitiveType.Cylinder, "Pad", new Vector3(0f, -0.04f, 0f), new Vector3(1.8f, 0.02f, 1.8f), Quaternion.identity, m.spawn, true, true);
                        Visual(sp.transform, PrimitiveType.Cylinder, "Ring", new Vector3(0f, -0.045f, 0f), new Vector3(2.2f, 0.015f, 2.2f), Quaternion.identity, m.platform, true, true);
                        break;
                    }

                    case PieceKind.Platform:
                    {
                        var plat = Block(Group(p.kind), "Platform", p.center, p.size, rot, m.platform, true);
                        Visual(plat.transform, PrimitiveType.Cube, "Top", new Vector3(0f, 0.5f, 0f), new Vector3(1.03f, 0.06f / p.size.y, 1.03f), Quaternion.identity, m.platformTop, true, true);
                        foreach (var c in new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1) })
                            Block(Group(p.kind), "Pillar", p.center + new Vector3(c.x * p.size.x * 0.5f, 0.125f, c.z * p.size.z * 0.5f), new Vector3(0.6f, p.size.y + 0.25f, 0.6f), Quaternion.identity, m.stone, true);
                        break;
                    }

                    case PieceKind.Ramp:
                        Ramp(Group(p.kind), p, m);
                        break;

                    case PieceKind.Totem:
                        Totem(Group(p.kind), p, m);
                        break;

                    case PieceKind.Boundary:
                        Boundary(Group(p.kind), p, rot, m, idx);
                        break;

                    case PieceKind.Tree:
                        Tree(Group(p.kind), p, m, idx);
                        break;

                    case PieceKind.Flower:
                    {
                        var g = Group(p.kind);
                        var petal = p.variant == 0 ? m.flowerPink : (p.variant == 1 ? m.flowerYellow : m.flowerWhite);
                        var f = new GameObject("Flower").transform;
                        f.SetParent(g, false);
                        f.position = p.center;
                        f.rotation = rot;
                        Visual(f, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.12f, 0f), new Vector3(0.03f, 0.12f, 0.03f), Quaternion.identity, m.grassTuft, false, true);
                        Visual(f, PrimitiveType.Sphere, "Petals", new Vector3(0f, 0.26f, 0f), new Vector3(0.22f, 0.07f, 0.22f), Quaternion.identity, petal, false, true);
                        Visual(f, PrimitiveType.Sphere, "Center", new Vector3(0f, 0.29f, 0f), new Vector3(0.08f, 0.05f, 0.08f), Quaternion.identity, m.flowerCenter, false, true);
                        break;
                    }

                    case PieceKind.Grass:
                    {
                        var g = Group(p.kind);
                        var t = new GameObject("Tuft").transform;
                        t.SetParent(g, false);
                        t.position = p.center;
                        t.rotation = rot;
                        for (int i = 0; i < 3; i++)
                            Visual(t, PrimitiveType.Cube, "Blade", new Vector3((i - 1) * 0.06f, 0.12f, 0f), new Vector3(0.04f, 0.26f, 0.02f), Quaternion.Euler(0f, i * 50f, (i - 1) * 18f), m.grassTuft, false, true);
                        break;
                    }

                    case PieceKind.Mushroom:
                    {
                        var g = Group(p.kind);
                        var t = new GameObject("Mushroom").transform;
                        t.SetParent(g, false);
                        t.position = p.center;
                        Visual(t, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.1f, 0f), new Vector3(0.09f, 0.1f, 0.09f), Quaternion.identity, m.mushroomStem, false, true);
                        Visual(t, PrimitiveType.Sphere, "Cap", new Vector3(0f, 0.2f, 0f), new Vector3(0.26f, 0.14f, 0.26f), Quaternion.identity, m.mushroomCap, false, true);
                        Visual(t, PrimitiveType.Sphere, "Dot", new Vector3(0.06f, 0.26f, 0.03f), new Vector3(0.05f, 0.03f, 0.05f), Quaternion.identity, m.mushroomStem, false, true);
                        break;
                    }

                    case PieceKind.Waypoint:
                        waypoints.Add(p.center);
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- peças

        static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot, Material mat, bool isStatic)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = rot;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = isStatic;
            return go;
        }

        /// <summary>Peça só visual (sem colisor). 'local' = posição/rotação relativas ao pai.</summary>
        static GameObject Visual(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, bool shadows, bool local = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            if (local)
            {
                go.transform.localPosition = pos;
                go.transform.localRotation = rot;
            }
            else
            {
                go.transform.position = pos;
                go.transform.rotation = rot;
            }
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        /// <summary>Colisor sem render (limites, barreiras). layer 2 = Ignore Raycast → trava o movimento, os tiros passam.</summary>
        static GameObject Blocker(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot, bool shotsPass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = rot;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            if (shotsPass) go.layer = 2;
            go.isStatic = true;
            return go;
        }

        /// <summary>Uma caixa de madeira com painéis claros e moldura escura (4 cubos).</summary>
        static void CrateVisual(Transform parent, Vector3 localPos, float s, Mats m, Quaternion localRot)
        {
            var c = new GameObject("Crate").transform;
            c.SetParent(parent, false);
            c.localPosition = localPos;
            c.localRotation = localRot;
            Visual(c, PrimitiveType.Cube, "Frame", Vector3.zero, Vector3.one * s, Quaternion.identity, m.crateDark, true, true);
            Visual(c, PrimitiveType.Cube, "PanelX", Vector3.zero, new Vector3(s * 1.02f, s * 0.78f, s * 0.78f), Quaternion.identity, m.crateLight, false, true);
            Visual(c, PrimitiveType.Cube, "PanelY", Vector3.zero, new Vector3(s * 0.78f, s * 1.02f, s * 0.78f), Quaternion.identity, m.crateLight, false, true);
            Visual(c, PrimitiveType.Cube, "PanelZ", Vector3.zero, new Vector3(s * 0.78f, s * 0.78f, s * 1.02f), Quaternion.identity, m.crateLight, false, true);
        }

        static void CrateStack(Transform parent, Piece p, Quaternion rot, Mats m)
        {
            var root = Blocker(parent, "CrateStack", p.center, p.size, rot, false);
            bool tall = p.size.y > 1.4f;
            float s1 = Mathf.Min(p.size.z, p.size.y * (tall ? 0.56f : 1f));
            int nx = Mathf.Max(1, Mathf.RoundToInt(p.size.x / s1));
            float cell = p.size.x / nx;
            s1 = Mathf.Min(s1, cell);
            float s2 = p.size.y - s1;
            for (int i = 0; i < nx; i++)
            {
                float x = -p.size.x * 0.5f + (i + 0.5f) * cell;
                CrateVisual(root.transform, new Vector3(x, -p.size.y * 0.5f + s1 * 0.5f, 0f), s1, m, Quaternion.Euler(0f, (i * 7) % 11 - 5, 0f));
                // Segunda fila (alternada) se a pilha for alta — dentro do colisor
                if (tall && i % 2 == 0 && s2 > 0.3f)
                    CrateVisual(root.transform, new Vector3(x + cell * 0.15f, -p.size.y * 0.5f + s1 + s2 * 0.5f, 0f), s2, m, Quaternion.Euler(0f, 8f, 0f));
            }
        }

        static void Fence(Transform parent, Piece p, Quaternion rot, Mats m)
        {
            var root = Blocker(parent, "Fence", p.center, p.size, rot, false);
            int posts = Mathf.Max(2, Mathf.RoundToInt(p.size.x / 1f) + 1);
            for (int i = 0; i < posts; i++)
            {
                float x = -p.size.x * 0.5f + i * p.size.x / (posts - 1);
                Visual(root.transform, PrimitiveType.Cube, "Post", new Vector3(x, 0f, 0f), new Vector3(0.16f, p.size.y, 0.16f), Quaternion.identity, m.fence, true, true);
                Visual(root.transform, PrimitiveType.Cube, "PostTop", new Vector3(x, p.size.y * 0.5f + 0.05f, 0f), new Vector3(0.2f, 0.1f, 0.2f), Quaternion.Euler(0f, 45f, 0f), m.fence, false, true);
            }
            Visual(root.transform, PrimitiveType.Cube, "RailTop", new Vector3(0f, p.size.y * 0.25f, 0f), new Vector3(p.size.x, 0.14f, 0.08f), Quaternion.identity, m.fence, true, true);
            Visual(root.transform, PrimitiveType.Cube, "RailBottom", new Vector3(0f, -p.size.y * 0.15f, 0f), new Vector3(p.size.x, 0.14f, 0.08f), Quaternion.identity, m.fence, true, true);
        }

        static void Rock(Transform parent, Piece p, Quaternion rot, Mats m, int seed)
        {
            var root = Blocker(parent, "Rock", p.center, p.size * 0.92f, rot, false);
            var rng = new System.Random(seed * 31);
            for (int i = 0; i < 4; i++)
            {
                var off = new Vector3((float)(rng.NextDouble() - 0.5) * p.size.x * 0.4f, (float)(rng.NextDouble() - 0.5) * p.size.y * 0.3f, (float)(rng.NextDouble() - 0.5) * p.size.z * 0.4f);
                float k = 0.65f + (float)rng.NextDouble() * 0.35f;
                var sc = new Vector3(p.size.x * k, p.size.y * k * 1.05f, p.size.z * k);
                Visual(root.transform, i % 2 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube, "Stone", off, sc, Quaternion.Euler(rng.Next(-15, 15), rng.Next(0, 90), rng.Next(-15, 15)), i == 0 ? m.rockLight : m.rock, true, true);
            }
        }

        static void BushPatch(Transform parent, Piece p, Quaternion rot, Mats m, int seed)
        {
            var root = new GameObject("Bush");
            root.transform.SetParent(parent, false);
            root.transform.position = p.center;
            root.transform.rotation = rot;
            var bush = root.AddComponent<Bush>();
            bush.halfExtents = new Vector2(p.size.x * 0.5f, p.size.z * 0.5f);
            bush.height = p.size.y + 0.2f;

            var rng = new System.Random(seed * 17 + 3);
            float step = 1.05f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(p.size.x / step));
            int nz = Mathf.Max(1, Mathf.RoundToInt(p.size.z / step));
            for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    float x = -p.size.x * 0.5f + (ix + 0.5f) * p.size.x / nx + (float)(rng.NextDouble() - 0.5) * 0.3f;
                    float z = -p.size.z * 0.5f + (iz + 0.5f) * p.size.z / nz + (float)(rng.NextDouble() - 0.5) * 0.3f;
                    float h = p.size.y * (0.85f + (float)rng.NextDouble() * 0.3f);
                    float w = 1.35f + (float)rng.NextDouble() * 0.35f;
                    var mat = rng.NextDouble() < 0.5 ? m.bush : m.bushLight;
                    Visual(root.transform, PrimitiveType.Sphere, "Leaves", new Vector3(x, h * 0.5f, z), new Vector3(w, h * 1.05f, w), Quaternion.identity, mat, true, true);
                    if (rng.NextDouble() < 0.4)
                        Visual(root.transform, PrimitiveType.Sphere, "Top", new Vector3(x + 0.15f, h * 0.95f, z - 0.1f), new Vector3(0.7f, 0.5f, 0.7f), Quaternion.identity, m.bushLight, false, true);
                }
        }

        static void Pond(Transform parent, Piece p, Quaternion rot, Mats m, int seed)
        {
            var root = new GameObject("Pond");
            root.transform.SetParent(parent, false);
            root.transform.position = p.center;
            root.transform.rotation = rot;

            Visual(root.transform, PrimitiveType.Cube, "Water", new Vector3(0f, 0.035f, 0f), new Vector3(p.size.x, 0.05f, p.size.z), Quaternion.identity, m.water, false, true);
            // Barreira invisível: trava o movimento, os tiros e projéteis passam por cima
            var block = new GameObject("WaterBlocker");
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = new Vector3(0f, 1f, 0f);
            block.layer = 2;
            block.AddComponent<BoxCollider>().size = new Vector3(p.size.x, 2f, p.size.z);
            block.isStatic = true;

            // Margem de pedras arredondadas
            var rng = new System.Random(seed * 13);
            float per = (p.size.x + p.size.z) * 2f;
            int n = Mathf.RoundToInt(per / 0.8f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n * per;
                Vector3 pos;
                float hx = p.size.x * 0.5f, hz = p.size.z * 0.5f;
                if (t < p.size.x) pos = new Vector3(-hx + t, 0f, -hz);
                else if (t < p.size.x + p.size.z) pos = new Vector3(hx, 0f, -hz + (t - p.size.x));
                else if (t < p.size.x * 2f + p.size.z) pos = new Vector3(hx - (t - p.size.x - p.size.z), 0f, hz);
                else pos = new Vector3(-hx, 0f, hz - (t - p.size.x * 2f - p.size.z));
                float sz = 0.55f + (float)rng.NextDouble() * 0.35f;
                Visual(root.transform, PrimitiveType.Sphere, "RimStone", pos + new Vector3(0f, 0.06f, 0f), new Vector3(sz, 0.32f, sz * 0.85f), Quaternion.Euler(0f, rng.Next(0, 180), 0f), rng.NextDouble() < 0.5 ? m.rock : m.rockLight, false, true);
            }
            // Nenúfares
            for (int i = 0; i < 3; i++)
            {
                var pos = new Vector3((float)(rng.NextDouble() - 0.5) * p.size.x * 0.6f, 0.07f, (float)(rng.NextDouble() - 0.5) * p.size.z * 0.6f);
                Visual(root.transform, PrimitiveType.Cylinder, "Lily", pos, new Vector3(0.6f, 0.01f, 0.6f), Quaternion.identity, m.lily, false, true);
                if (i == 0) Visual(root.transform, PrimitiveType.Sphere, "LilyFlower", pos + new Vector3(0.1f, 0.06f, 0f), new Vector3(0.18f, 0.1f, 0.18f), Quaternion.identity, m.flowerPink, false, true);
            }
        }

        static void DestructibleCrate(Transform parent, Piece p, Quaternion rot, Mats m)
        {
            // NetworkObject de cena: fica na raiz (sem pai) para a sincronização ser simples
            var go = CreateDestructibleCover(m.crateDark, p.center, p.size, Mathf.RoundToInt(p.value));
            go.name = "DestructibleCrate";
            go.transform.rotation = rot;
            // Painéis claros (filhos sem colisor; o DestructibleCover desliga-os todos ao partir)
            AddChildPanel(go.transform, new Vector3(1.02f, 0.8f, 0.8f), m.crateLight);
            AddChildPanel(go.transform, new Vector3(0.86f, 1.02f, 0.8f), m.crateLight);
            AddChildPanel(go.transform, new Vector3(0.88f, 0.8f, 1.02f), m.crateLight);
        }

        static void AddChildPanel(Transform parent, Vector3 localScale, Material mat)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "Panel";
            c.transform.SetParent(parent, false);
            c.transform.localScale = localScale;
            c.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static void DestructibleBarrel(Transform parent, Piece p, Quaternion rot, Mats m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "DestructibleBarrel";
            go.transform.position = p.center;
            go.transform.rotation = rot;
            go.transform.localScale = new Vector3(p.size.x, p.size.y * 0.5f, p.size.z);
            go.GetComponent<Renderer>().sharedMaterial = m.barrel;
            go.AddComponent<NetworkObject>();
            go.AddComponent<DestructibleCover>().maxHealth = Mathf.RoundToInt(p.value);
            foreach (var y in new[] { -0.6f, 0.6f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.name = "Band";
                band.transform.SetParent(go.transform, false);
                band.transform.localPosition = new Vector3(0f, y, 0f);
                band.transform.localScale = new Vector3(1.04f, 0.06f, 1.04f);
                band.GetComponent<Renderer>().sharedMaterial = m.metal;
            }
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(mark.GetComponent<Collider>());
            mark.name = "Mark";
            mark.transform.SetParent(go.transform, false);
            mark.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            mark.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            mark.transform.localScale = new Vector3(0.3f, 0.3f, 0.04f);
            mark.GetComponent<Renderer>().sharedMaterial = m.flowerYellow;
        }

        static void JumpPadPiece(Transform parent, Piece p, Quaternion rot, Mats m)
        {
            var root = new GameObject("JumpPad");
            root.transform.SetParent(parent, false);
            root.transform.position = p.center;
            var pad = root.AddComponent<JumpPad>();
            pad.target = p.target;
            pad.flightTime = p.value;
            pad.radius = 1.05f;

            // Base com colisor baixo (sobe-se sem saltar)
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.name = "Base";
            b.transform.SetParent(root.transform, false);
            b.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            b.transform.localScale = new Vector3(2.4f, 0.06f, 2.4f);
            b.GetComponent<Renderer>().sharedMaterial = m.stone;
            b.isStatic = true;
            Visual(root.transform, PrimitiveType.Cylinder, "Pad", new Vector3(0f, 0.06f, 0f), new Vector3(1.9f, 0.04f, 1.9f), Quaternion.identity, m.pad, false, true);
            Visual(root.transform, PrimitiveType.Cylinder, "Glow", new Vector3(0f, 0.09f, 0f), new Vector3(1.1f, 0.02f, 1.1f), Quaternion.identity, m.padGlow, false, true);

            // Seta a apontar para o destino (animada pelo JumpPad)
            var dir = p.target - p.center;
            dir.y = 0f;
            var arrow = new GameObject("Arrow").transform;
            arrow.SetParent(root.transform, false);
            arrow.localPosition = new Vector3(0f, 0.18f, 0f);
            arrow.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward);
            var a1 = Visual(arrow, PrimitiveType.Cube, "Head1", new Vector3(0.18f, 0f, 0.1f), new Vector3(0.12f, 0.06f, 0.6f), Quaternion.Euler(0f, -45f, 0f), m.padGlow, false, true);
            var a2 = Visual(arrow, PrimitiveType.Cube, "Head2", new Vector3(-0.18f, 0f, 0.1f), new Vector3(0.12f, 0.06f, 0.6f), Quaternion.Euler(0f, 45f, 0f), m.padGlow, false, true);
            a1.isStatic = false;
            a2.isStatic = false;
        }

        static void Ramp(Transform parent, Piece p, Mats m)
        {
            // Rampa que sobe de y=0 até 'height' a terminar na borda 'edge', descendo na direção do yaw (+z local)
            float width = p.size.x, height = p.size.y, run = p.size.z;
            const float thickness = 0.4f;
            float angle = Mathf.Atan2(height, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(height * height + run * run) + 0.3f;
            var yawRot = Quaternion.Euler(0f, p.yaw, 0f);
            var fwd = yawRot * Vector3.forward;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ramp";
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(width, thickness, length);
            go.transform.rotation = yawRot * Quaternion.Euler(angle, 0f, 0f);
            var surfaceMid = p.center + fwd * (run / 2f) + Vector3.up * (height / 2f);
            go.transform.position = surfaceMid - go.transform.up * (thickness / 2f);
            go.GetComponent<Renderer>().sharedMaterial = m.ramp;
            go.isStatic = true;

            // Corrimãos laterais baixos (só visuais)
            foreach (var side in new[] { -1f, 1f })
            {
                var rail = Visual(parent, PrimitiveType.Cube, "RampRail", surfaceMid + yawRot * new Vector3(side * (width * 0.5f + 0.1f), 0.1f, 0f), new Vector3(0.2f, 0.25f, length), go.transform.rotation, m.stone, true);
                rail.isStatic = true;
            }
        }

        static void Totem(Transform parent, Piece p, Mats m)
        {
            var root = new GameObject("Totem");
            root.transform.SetParent(parent, false);
            root.transform.position = p.center;
            // Colisor do totem inteiro
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, p.size.y * 0.5f, 0f);
            col.size = p.size;
            root.isStatic = true;
            Visual(root.transform, PrimitiveType.Cube, "Base", new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.6f, 1.4f), Quaternion.identity, m.stone, true, true);
            Visual(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 1.2f, 0f), new Vector3(1.1f, 1.3f, 1.1f), Quaternion.Euler(0f, 45f, 0f), m.totem, true, true);
            Visual(root.transform, PrimitiveType.Cube, "Head", new Vector3(0f, 2.15f, 0f), new Vector3(1.25f, 0.6f, 1.25f), Quaternion.identity, m.totem, true, true);
            // "Caras" do totem nos 4 lados
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0f, i * 90f, 0f);
                Visual(root.transform, PrimitiveType.Cube, "EyeL", q * new Vector3(-0.25f, 2.2f, 0.63f), new Vector3(0.18f, 0.18f, 0.05f), q, m.gem, false, true);
                Visual(root.transform, PrimitiveType.Cube, "EyeR", q * new Vector3(0.25f, 2.2f, 0.63f), new Vector3(0.18f, 0.18f, 0.05f), q, m.gem, false, true);
            }
            var gem = Visual(root.transform, PrimitiveType.Cube, "Gem", new Vector3(0f, 2.95f, 0f), new Vector3(0.5f, 0.5f, 0.5f), Quaternion.Euler(45f, 0f, 45f), m.gem, true, true);
            gem.isStatic = false;
            gem.AddComponent<Spinner>().speed = new Vector3(0f, 45f, 0f);
        }

        static void Boundary(Transform parent, Piece p, Quaternion rot, Mats m, int seed)
        {
            var root = Block(parent, "BoundaryWall", p.center, p.size, rot, m.cliff, true);
            root.GetComponent<Renderer>().enabled = false;
            // Barreira alta invisível: impede saltos para fora do mapa (os tiros passam)
            Blocker(parent, "BoundaryBlocker", p.center + Vector3.up * 10f, new Vector3(p.size.x, 20f, p.size.z), rot, true);

            // Rochedos irregulares por cima da parede
            var rng = new System.Random(seed * 7 + 11);
            int n = Mathf.RoundToInt(p.size.x / 2.2f);
            for (int i = 0; i < n; i++)
            {
                float x = -p.size.x * 0.5f + (i + 0.5f) * p.size.x / n;
                float h = p.size.y * (1f + (float)rng.NextDouble() * 0.6f);
                float w = 2.6f + (float)rng.NextDouble() * 0.8f;
                var pos = p.center + rot * new Vector3(x, (h - p.size.y) * 0.5f, (float)(rng.NextDouble() - 0.3) * 0.6f);
                Visual(parent, PrimitiveType.Cube, "Cliff", pos, new Vector3(w, h, p.size.z + 0.6f), rot * Quaternion.Euler(0f, rng.Next(-8, 8), 0f), rng.NextDouble() < 0.5 ? m.cliff : m.rock, true);
                Visual(parent, PrimitiveType.Cube, "CliffTop", pos + Vector3.up * (h * 0.5f), new Vector3(w * 1.02f, 0.25f, p.size.z + 0.7f), rot * Quaternion.Euler(0f, rng.Next(-8, 8), 0f), m.hill, false);
            }
        }

        static void Tree(Transform parent, Piece p, Mats m, int seed)
        {
            var rng = new System.Random(seed * 5 + 1);
            var root = new GameObject("Tree").transform;
            root.SetParent(parent, false);
            root.position = p.center;
            root.rotation = Quaternion.Euler(0f, rng.Next(0, 360), 0f);
            float k = 0.9f + (float)rng.NextDouble() * 0.6f;
            Visual(root, PrimitiveType.Cylinder, "Trunk", new Vector3(0f, 1.4f * k, 0f), new Vector3(0.5f, 1.4f * k, 0.5f), Quaternion.identity, m.trunk, true, true);
            if (p.variant == 2)
            {
                // Pinheiro: cones de "esferas achatadas"
                for (int i = 0; i < 3; i++)
                    Visual(root, PrimitiveType.Sphere, "Pine", new Vector3(0f, (2.4f + i * 1.1f) * k, 0f), new Vector3((2.8f - i * 0.7f) * k, 1.2f * k, (2.8f - i * 0.7f) * k), Quaternion.identity, i % 2 == 0 ? m.leaves : m.leavesLight, true, true);
            }
            else
            {
                Visual(root, PrimitiveType.Sphere, "Crown", new Vector3(0f, 3.4f * k, 0f), new Vector3(3.2f, 2.8f, 3.2f) * k, Quaternion.identity, m.leaves, true, true);
                Visual(root, PrimitiveType.Sphere, "Crown2", new Vector3(0.8f * k, 3.0f * k, 0.5f * k), new Vector3(2.2f, 2f, 2.2f) * k, Quaternion.identity, m.leavesLight, true, true);
                Visual(root, PrimitiveType.Sphere, "Crown3", new Vector3(-0.7f * k, 4.1f * k, -0.3f * k), new Vector3(1.8f, 1.6f, 1.8f) * k, Quaternion.identity, m.leavesLight, true, true);
            }
        }

        /// <summary>Paisagem distante: colinas, montanhas com neve e nuvens cartoon.</summary>
        static void BuildScenery(Transform map, Mats m, Vector3 offset)
        {
            var root = new GameObject("Scenery").transform;
            root.SetParent(map, false);
            var rng = new System.Random(99);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f + (float)rng.NextDouble() * 0.2f;
                float r = 62f + (float)rng.NextDouble() * 10f;
                var pos = offset + new Vector3(Mathf.Sin(a) * r, -2f, Mathf.Cos(a) * r);
                float w = 22f + (float)rng.NextDouble() * 14f;
                Visual(root, PrimitiveType.Sphere, "Hill", pos, new Vector3(w, 12f + (float)rng.NextDouble() * 8f, w * 0.8f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), m.hill, false);
            }
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f + 0.3f;
                float r = 120f + (float)rng.NextDouble() * 25f;
                float h = 35f + (float)rng.NextDouble() * 25f;
                var pos = offset + new Vector3(Mathf.Sin(a) * r, h * 0.2f, Mathf.Cos(a) * r);
                var rot = Quaternion.Euler(0f, a * Mathf.Rad2Deg + 45f, 0f) * Quaternion.Euler(45f, 0f, 45f);
                Visual(root, PrimitiveType.Cube, "Mountain", pos, new Vector3(h, h, h), rot, m.mountain, false);
                Visual(root, PrimitiveType.Cube, "Snow", pos + Vector3.up * h * 0.52f, new Vector3(h * 0.36f, h * 0.36f, h * 0.36f), rot, m.snow, false);
            }
            for (int i = 0; i < 10; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 50f + (float)rng.NextDouble() * 60f;
                var c = new GameObject("Cloud").transform;
                c.SetParent(root, false);
                c.position = offset + new Vector3(Mathf.Sin(a) * r, 34f + (float)rng.NextDouble() * 14f, Mathf.Cos(a) * r);
                c.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                int puffs = 3 + rng.Next(0, 3);
                for (int j = 0; j < puffs; j++)
                {
                    float s = 5f + (float)rng.NextDouble() * 4f;
                    var puff = Visual(c, PrimitiveType.Sphere, "Puff", new Vector3((j - puffs * 0.5f) * 3.2f, (float)rng.NextDouble() * 1.5f, (float)(rng.NextDouble() - 0.5) * 2f), new Vector3(s, s * 0.6f, s * 0.8f), Quaternion.identity, m.cloud, false, true);
                    puff.isStatic = false; // as nuvens deslizam (Spinner)
                }
                var drift = c.gameObject.AddComponent<Spinner>();
                drift.drift = new Vector3(0.6f, 0f, 0.2f);
                drift.wrapCenter = offset;
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    public static class BuildTools
    {
        public static void BuildWindowsPlayer()
        {
            var outputPath = System.Environment.GetEnvironmentVariable("FPSLAN_WINDOWS_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputPath))
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "build windows", "FPS-LAN.exe");

            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new UnityEditor.Build.BuildFailedException("Não há cenas ativadas em Build Settings.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new UnityEditor.Build.BuildFailedException("A build Windows terminou com estado " + report.summary.result + ".");

            Debug.Log("[FPS-LAN] Build Windows x64 pronta: " + outputPath);
        }

        [MenuItem("FPS-LAN/Build Windows (para a LAN)", priority = 20)]
        static void BuildWindows()
        {
            var path = EditorUtility.SaveFolderPanel("Pasta do build", "Builds", "Windows");
            if (string.IsNullOrEmpty(path)) return;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = Path.Combine(path, "FPS-LAN.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log("[FPS-LAN] Build pronto: " + options.locationPathName);
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError("[FPS-LAN] Build falhou: " + report.summary.result);
            }
        }
    }
}
