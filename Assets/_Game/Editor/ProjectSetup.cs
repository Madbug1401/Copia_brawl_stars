using System.Collections.Generic;
using System.IO;
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
    /// Gera o conteúdo do MVP com APIs do Editor (cena Arena, prefab Player, materiais, NetworkManager).
    /// Corre sozinho na primeira vez que o projeto compila; depois pode ser repetido pelo menu FPS-LAN.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string Root = "Assets/_Game";
        public const string ScenePath = Root + "/Scenes/Arena.unity";
        public const string PlayerPrefabPath = Root + "/Prefabs/Player.prefab";
        const string MaterialsPath = Root + "/Materials";

        static ProjectSetup()
        {
            EditorApplication.delayCall += AutoRun;
        }

        static void AutoRun()
        {
            if (Application.isBatchMode) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (File.Exists(ScenePath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += AutoRun;
                return;
            }

            Debug.Log("[FPS-LAN] Primeira abertura: a gerar cena Arena, prefab Player e configurações...");
            BuildAll();
        }

        [MenuItem("FPS-LAN/Regenerar Arena + Player", priority = 1)]
        static void RebuildMenu()
        {
            if (!EditorUtility.DisplayDialog("FPS-LAN",
                    "Isto recria a cena Arena, o prefab Player e os materiais do MVP (alterações manuais nesses ficheiros perdem-se). Continuar?",
                    "Regenerar", "Cancelar"))
                return;
            BuildAll();
        }

        [MenuItem("FPS-LAN/Adicionar cobertura destrutível de teste", priority = 3)]
        static void AddTestCoverMenu()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/M_Cover.mat");
            var go = CreateDestructibleCover(mat, new Vector3(3.5f, 0.75f, -7f), new Vector3(2.6f, 1.5f, 0.8f));
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
            EnsureFolder(MaterialsPath);

            ConfigurePlayerSettings();
            var mats = CreateMaterials();
            var player = CreatePlayerPrefab(mats);
            CreateScene(player, mats);

            AssetDatabase.SaveAssets();
            Debug.Log("[FPS-LAN] Pronto. Cena: " + ScenePath + " — carrega Play e escolhe Host.");
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

        // ---------------------------------------------------------------- Materials

        class Mats
        {
            public Material floor, wall, cover, platform, ramp, crate, trim, player, gun;
        }

        static Shader LitShader()
        {
            Shader s = null;
            if (GraphicsSettings.currentRenderPipeline != null)
                s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            if (s == null) s = Shader.Find("Diffuse");
            return s;
        }

        static Mats CreateMaterials()
        {
            var shader = LitShader();
            return new Mats
            {
                floor = Mat(shader, "M_Floor", new Color(0.24f, 0.26f, 0.31f), 0.15f),
                wall = Mat(shader, "M_Wall", new Color(0.36f, 0.4f, 0.48f), 0.1f),
                cover = Mat(shader, "M_Cover", new Color(0.93f, 0.52f, 0.2f), 0.2f),
                platform = Mat(shader, "M_Platform", new Color(0.16f, 0.58f, 0.64f), 0.25f),
                ramp = Mat(shader, "M_Ramp", new Color(0.95f, 0.8f, 0.28f), 0.2f),
                crate = Mat(shader, "M_Crate", new Color(0.56f, 0.4f, 0.28f), 0.05f),
                trim = Mat(shader, "M_Trim", new Color(0.12f, 0.13f, 0.16f), 0.3f),
                player = Mat(shader, "M_Player", Color.white, 0.3f),
                gun = Mat(shader, "M_Gun", new Color(0.15f, 0.16f, 0.19f), 0.5f),
            };
        }

        static Material Mat(Shader shader, string name, Color color, float smoothness)
        {
            var path = MaterialsPath + "/" + name + ".mat";
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
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------------------------------------------------------- Player prefab

        static GameObject CreatePlayerPrefab(Mats mats)
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

            var body = Part(PrimitiveType.Capsule, root.transform, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.8f, 0.85f, 0.8f), mats.player);
            var head = Part(PrimitiveType.Sphere, root.transform, "Head", new Vector3(0f, 1.62f, 0f), Vector3.one * 0.46f, mats.player);
            var visor = Part(PrimitiveType.Cube, head.transform, "Visor", new Vector3(0f, 0.08f, 0.4f), new Vector3(0.8f, 0.25f, 0.3f), mats.trim);

            var cameraRoot = new GameObject("CameraRoot").transform;
            cameraRoot.SetParent(root.transform, false);
            cameraRoot.localPosition = new Vector3(0f, 1.6f, 0f);

            var aimPivot = new GameObject("AimPivot").transform;
            aimPivot.SetParent(root.transform, false);
            aimPivot.localPosition = new Vector3(0f, 1.32f, 0f);

            var gun = Part(PrimitiveType.Cube, aimPivot, "Gun", new Vector3(0.32f, 0f, 0.35f), new Vector3(0.12f, 0.16f, 0.6f), mats.gun);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(aimPivot, false);
            muzzle.localPosition = new Vector3(0.32f, 0.02f, 0.7f);

            motor.cameraRoot = cameraRoot;
            motor.aimPivot = aimPivot;

            visuals.bodyRenderers = new[]
            {
                body.GetComponent<Renderer>(), head.GetComponent<Renderer>(), visor.GetComponent<Renderer>(), gun.GetComponent<Renderer>()
            };
            visuals.tintRenderers = new[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() };
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

        static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
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

            // Luz e ambiente
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -38f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.46f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.19f, 0.18f);
            var sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;

            // Mapa
            var map = new GameObject("Map").transform;
            BuildArena(map, mats);

            // Spawns
            var spawns = new GameObject("SpawnPoints").transform;
            var spawnPositions = new[]
            {
                new Vector3(-18f, 0.05f, -18f), new Vector3(18f, 0.05f, -18f), new Vector3(-18f, 0.05f, 18f), new Vector3(18f, 0.05f, 18f),
                new Vector3(0f, 0.05f, -19f), new Vector3(0f, 0.05f, 19f), new Vector3(-19f, 0.05f, 0f), new Vector3(19f, 0.05f, 0f),
            };
            for (int i = 0; i < spawnPositions.Length; i++)
            {
                var sp = new GameObject("Spawn_" + i);
                sp.transform.SetParent(spawns, false);
                sp.transform.position = spawnPositions[i];
                var look = -spawnPositions[i];
                look.y = 0f;
                sp.transform.rotation = Quaternion.LookRotation(look.normalized);
                sp.AddComponent<SpawnPoint>();
            }

            // Sistemas locais
            var systems = new GameObject("Systems");
            systems.AddComponent<CombatSystem>();
            systems.AddComponent<ConnectionManager>();
            systems.AddComponent<GameUI>();

            // Câmara do menu
            var camGo = new GameObject("MenuCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<MenuCamera>();
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 24f, -34f);
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

            // Cobertura destrutível de teste (só os objetos com estado são NetworkObjects)
            CreateDestructibleCover(mats.cover, new Vector3(3.5f, 0.75f, -7f), new Vector3(2.6f, 1.5f, 0.8f));

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

        // ---------------------------------------------------------------- Arena
        // ~44x44 m, 3 corredores (oeste, centro, este), plataforma central elevada com 2 rampas,
        // cobertura média, caixas para saltar e spawns nos cantos/meios dos lados.

        static void BuildArena(Transform map, Mats m)
        {
            const float half = 22f;
            const float wallH = 4.5f;

            Block(map, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f + 2f, 1f, half * 2f + 2f), m.floor);

            // Paredes exteriores
            Block(map, "Wall_N", new Vector3(0f, wallH / 2f, half + 0.5f), new Vector3(half * 2f + 2f, wallH, 1f), m.wall);
            Block(map, "Wall_S", new Vector3(0f, wallH / 2f, -half - 0.5f), new Vector3(half * 2f + 2f, wallH, 1f), m.wall);
            Block(map, "Wall_E", new Vector3(half + 0.5f, wallH / 2f, 0f), new Vector3(1f, wallH, half * 2f), m.wall);
            Block(map, "Wall_W", new Vector3(-half - 0.5f, wallH / 2f, 0f), new Vector3(1f, wallH, half * 2f), m.wall);

            // Rodapé decorativo
            Block(map, "Trim_N", new Vector3(0f, 0.15f, half - 0.1f), new Vector3(half * 2f, 0.3f, 0.2f), m.trim);
            Block(map, "Trim_S", new Vector3(0f, 0.15f, -half + 0.1f), new Vector3(half * 2f, 0.3f, 0.2f), m.trim);

            // Plataforma central (topo a 2 m) + parapeitos
            Block(map, "Platform", new Vector3(0f, 1f, 0f), new Vector3(8f, 2f, 8f), m.platform);
            Block(map, "Platform_Edge", new Vector3(0f, 2.02f, 0f), new Vector3(8.2f, 0.06f, 8.2f), m.trim);
            Block(map, "Parapet_E", new Vector3(3.7f, 2.5f, 0f), new Vector3(0.5f, 1f, 4f), m.cover);
            Block(map, "Parapet_W", new Vector3(-3.7f, 2.5f, 0f), new Vector3(0.5f, 1f, 4f), m.cover);

            // Rampas norte/sul (≈17°)
            Ramp(map, "Ramp_N", new Vector3(0f, 0f, 4f), 1f, 2f, 6.6f, 3.2f, m.ramp);
            Ramp(map, "Ramp_S", new Vector3(0f, 0f, -4f), -1f, 2f, 6.6f, 3.2f, m.ramp);

            // Paredes que separam corredores (com passagem ao meio e nas pontas)
            foreach (float x in new[] { -10f, 10f })
            {
                Block(map, "LaneWall_" + x + "_N", new Vector3(x, 1.75f, 5.5f), new Vector3(1f, 3.5f, 7f), m.wall);
                Block(map, "LaneWall_" + x + "_S", new Vector3(x, 1.75f, -5.5f), new Vector3(1f, 3.5f, 7f), m.wall);
                Block(map, "LaneWindow_" + x, new Vector3(x, 0.5f, 0f), new Vector3(1f, 1f, 4f), m.trim); // parapeito baixo na passagem
            }

            // Corredor este: plataforma lateral com degraus para saltar
            Block(map, "East_Step1", new Vector3(16f, 0.35f, -10f), new Vector3(3f, 0.7f, 3f), m.crate);
            Block(map, "East_Step2", new Vector3(17.5f, 0.7f, -13f), new Vector3(3f, 1.4f, 3f), m.crate);
            Block(map, "East_Ledge", new Vector3(18.5f, 1f, -17.5f), new Vector3(5f, 2f, 6f), m.platform);
            Block(map, "East_Pillar", new Vector3(15f, 2f, 6f), new Vector3(1.6f, 4f, 1.6f), m.wall);
            Block(map, "East_Cover", new Vector3(16f, 0.75f, 12f), new Vector3(4f, 1.5f, 1f), m.cover);

            // Corredor oeste: espelhado com variação
            Block(map, "West_Step1", new Vector3(-16f, 0.35f, 10f), new Vector3(3f, 0.7f, 3f), m.crate);
            Block(map, "West_Step2", new Vector3(-17.5f, 0.7f, 13f), new Vector3(3f, 1.4f, 3f), m.crate);
            Block(map, "West_Ledge", new Vector3(-18.5f, 1f, 17.5f), new Vector3(5f, 2f, 6f), m.platform);
            Block(map, "West_Pillar", new Vector3(-15f, 2f, -6f), new Vector3(1.6f, 4f, 1.6f), m.wall);
            Block(map, "West_Cover", new Vector3(-16f, 0.75f, -12f), new Vector3(4f, 1.5f, 1f), m.cover);

            // Corredor central: cobertura dos dois lados da plataforma
            Block(map, "Mid_Cover_NE", new Vector3(5.5f, 0.75f, 11f), new Vector3(3f, 1.5f, 1f), m.cover);
            Block(map, "Mid_Cover_NW", new Vector3(-5.5f, 0.75f, 11f), new Vector3(3f, 1.5f, 1f), m.cover);
            Block(map, "Mid_Cover_SE", new Vector3(5.5f, 0.75f, -11f), new Vector3(3f, 1.5f, 1f), m.cover);
            Block(map, "Mid_Cover_SW", new Vector3(-5.5f, 0.75f, -11f), new Vector3(3f, 1.5f, 1f), m.cover);
            Block(map, "Mid_Crate_E", new Vector3(6.5f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), m.crate);
            Block(map, "Mid_Crate_W", new Vector3(-6.5f, 0.6f, 0f), new Vector3(1.2f, 1.2f, 1.2f), m.crate);

            // Proteção dos spawns (cortam a linha de visão direta para o centro)
            Block(map, "SpawnShield_N", new Vector3(0f, 1.1f, 15.5f), new Vector3(5f, 2.2f, 0.6f), m.wall);
            Block(map, "SpawnShield_S", new Vector3(0f, 1.1f, -15.5f), new Vector3(5f, 2.2f, 0.6f), m.wall);
            Block(map, "SpawnShield_E", new Vector3(15.5f, 1.1f, 0f), new Vector3(0.6f, 2.2f, 5f), m.wall);
            Block(map, "SpawnShield_W", new Vector3(-15.5f, 1.1f, 0f), new Vector3(0.6f, 2.2f, 5f), m.wall);
            Block(map, "Corner_NE", new Vector3(14f, 1.1f, 17f), new Vector3(0.6f, 2.2f, 4f), m.wall);
            Block(map, "Corner_SW", new Vector3(-14f, 1.1f, -17f), new Vector3(0.6f, 2.2f, 4f), m.wall);
            Block(map, "Corner_NW", new Vector3(-12f, 0.6f, 18.5f), new Vector3(1.2f, 1.2f, 1.2f), m.crate);
            Block(map, "Corner_SE", new Vector3(12f, 0.6f, -18.5f), new Vector3(1.2f, 1.2f, 1.2f), m.crate);
        }

        static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        /// <summary>Rampa que sobe de y=0 até 'height' a terminar na borda 'edge'. dir=+1 desce para +Z, -1 para -Z.</summary>
        static void Ramp(Transform parent, string name, Vector3 edge, float dir, float height, float run, float width, Material mat)
        {
            const float thickness = 0.4f;
            float angle = Mathf.Atan2(height, run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(height * height + run * run) + 0.3f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(width, thickness, length);
            go.transform.rotation = Quaternion.Euler(dir * angle, 0f, 0f);

            // Centro da superfície a meio caminho; descer meia espessura na normal
            var surfaceMid = new Vector3(edge.x, height / 2f, edge.z + dir * run / 2f);
            go.transform.position = surfaceMid - go.transform.up * (thickness / 2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
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
