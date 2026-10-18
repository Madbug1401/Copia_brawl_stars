using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Mapa Pique-Gemas "Buracona" (Sal): configuração do <see cref="GemMapBuilder"/> a partir dos FBX gerados no
    /// Blender (Tools/Blender/Buracona/Buracona_Map.py). Cria Scenes/Buracona.unity, PF_Buracona (cenário + emissor)
    /// e PF_Buracona_Agua (água, espuma e brilho do emissor). Reutiliza o modo, a gema e os arbustos existentes.
    /// </summary>
    public static class BuraconaBuilder
    {
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Sal/Buracona";
        public const string ScenePath = ProjectSetup.Root + "/Scenes/Buracona.unity";

        static readonly GemMapMat[] Materials =
        {
            new GemMapMat("MAT_Chao_Vulcanico", "T_Cascalho", new Color(0.72f, 0.68f, 0.66f), false, default, 0f),
            new GemMapMat("MAT_Areia", "T_Areia", new Color(0.7f, 0.62f, 0.5f), false, default, 0f),
            new GemMapMat("MAT_Basalto", "T_Basalto", new Color(0.4f, 0.35f, 0.33f), true, default, 0.2f),
            new GemMapMat("MAT_Basalto_Escuro", "T_Basalto", new Color(0.24f, 0.22f, 0.22f), true, default, 0.2f),
            new GemMapMat("MAT_Rocha_Borda", "T_Basalto", new Color(0.5f, 0.44f, 0.4f), true, default, 0.3f),
            new GemMapMat("MAT_Rocha_Molhada", "T_Basalto", new Color(0.2f, 0.22f, 0.25f), false, default, 0f),
            new GemMapMat("MAT_Fundo_Agua", "T_Areia", new Color(0.35f, 0.85f, 0.85f), false, new Color(0.04f, 0.22f, 0.24f), 0f),
            GemMapMat.Alpha("MAT_Agua", "T_Agua", new Color(0.3f, 0.88f, 0.92f, 0.72f)),
            GemMapMat.Alpha("MAT_Espuma", "T_Espuma", new Color(1f, 1f, 1f, 0.8f)),
            new GemMapMat("MAT_Brilho_Agua", null, new Color(0.4f, 1f, 0.95f), false, new Color(0.08f, 0.35f, 0.36f), 0f),
            new GemMapMat("MAT_Sal", null, new Color(0.95f, 0.95f, 0.93f), false),
            new GemMapMat("MAT_Mar", null, new Color(0.12f, 0.55f, 0.75f), false, new Color(0.02f, 0.1f, 0.15f), 0f),
            new GemMapMat("MAT_Monte", null, new Color(0.66f, 0.52f, 0.38f), false),
            new GemMapMat("MAT_Monte_Escuro", null, new Color(0.32f, 0.28f, 0.26f), false),
            new GemMapMat("MAT_Madeira", null, new Color(0.55f, 0.4f, 0.28f)),
            new GemMapMat("MAT_Spawn_Azul", null, new Color(0.2f, 0.5f, 0.95f), false, new Color(0.08f, 0.2f, 0.4f), 0f),
            new GemMapMat("MAT_Spawn_Vermelho", null, new Color(0.95f, 0.28f, 0.22f), false, new Color(0.4f, 0.1f, 0.08f), 0f),
            new GemMapMat("MAT_Marca_Aterragem", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new GemMapMat("MAT_Marca_Aterragem_Apagada", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new GemMapMat("MAT_Colisor", null, new Color(1f, 0.2f, 0.6f), false),
        };

        static void ConfigureWater(GameObject root, Dictionary<string, Material> mats)
        {
            var water = GemMapBuilder.FindDeep(root.transform, "WTR_Agua");
            if (water != null)
            {
                var w = water.gameObject.AddComponent<WaterSurface>();
                w.scrollSpeed = new Vector2(0.03f, 0.014f);
                w.tiling = 1f;
                w.bob = 0.015f;
            }
            var foam = GemMapBuilder.FindDeep(root.transform, "WTR_Espuma");
            if (foam != null)
            {
                var f = foam.gameObject.AddComponent<WaterSurface>();
                f.scrollSpeed = new Vector2(-0.02f, 0.01f);
                f.bob = 0.015f;
                f.pulse = 0.12f;
            }
        }

        public static GemMapConfig Config => new GemMapConfig
        {
            displayName = "Buracona",
            artRoot = ArtRoot,
            filePrefix = "Buracona",
            materialPrefix = "M_BU_",
            scenePath = ScenePath,
            mapPrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_Buracona.prefab",
            sceneRootName = "Map_Buracona",
            mapName = "Buracona",
            materials = Materials,
            auxModels = new[]
            {
                new GemMapAuxModel
                {
                    fbx = "Buracona_Agua.fbx",
                    prefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_Buracona_Agua.prefab",
                    configure = ConfigureWater,
                },
            },
            gemFbx = null,                                       // reutiliza PF_Gema (construído pela Cidade Velha)
            gemSpawnName = "GEM_Spawn",
            emitterBaseName = "GEN_Agua_Brilho",
            emitterStyle = GemEmitterStyle.Agua,
            waterSurfaceName = "WTR_Agua",
            arcHeight = 3.0f,                                    // passa por cima da borda rochosa
            emitterIdleEmission = new Color(0.08f, 0.35f, 0.36f),
            emitterWarnEmission = new Color(0.6f, 1.6f, 1.5f),
            sunColor = new Color(1f, 0.96f, 0.88f),
            sunIntensity = 1.35f,
            sunEuler = new Vector3(55f, -30f, 0f),
            ambientSky = new Color(0.72f, 0.84f, 0.98f),
            ambientEquator = new Color(0.62f, 0.6f, 0.56f),
            ambientGround = new Color(0.3f, 0.27f, 0.25f),
            fogColor = new Color(0.8f, 0.9f, 0.97f),
            fogStart = 60f,
            fogEnd = 240f,
            menuRadius = 40f,
            menuHeight = 30f,
            fallRecoveryY = -1.6f,                               // dentro do buraco (água a -1.1 m) → volta ao spawn
            expectedSize = "≈ 41 × 50",
        };

        [MenuItem("FPS-LAN/Mapas/Construir Buracona (Pique-Gemas)", priority = 42)]
        static void BuildMenu() => GemMapBuilder.BuildFromMenu(Config, "Tools/Blender/Buracona/LEIA-ME.md");

        [MenuItem("FPS-LAN/Mapas/Abrir cena Buracona", priority = 43)]
        static void OpenMenu() => GemMapBuilder.OpenScene(Config);

        public static string Build() => GemMapBuilder.Build(Config);
    }
}
