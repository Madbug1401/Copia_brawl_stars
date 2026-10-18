using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Mapa Pique-Gemas "Cidade Velha — Pelourinho" (Santiago): configuração do <see cref="GemMapBuilder"/>
    /// a partir dos FBX gerados no Blender (Tools/Blender/CidadeVelha). Cria a cena PRÓPRIA Scenes/CidadeVelha.unity,
    /// PF_CidadeVelha e PF_Gema (a gema é partilhada com os outros mapas).
    /// </summary>
    public static class CidadeVelhaBuilder
    {
        const string ArtRoot = ProjectSetup.Root + "/Art/Environment/Santiago/CidadeVelha";
        public const string ScenePath = ProjectSetup.Root + "/Scenes/CidadeVelha.unity";

        static readonly GemMapMat[] Materials =
        {
            new GemMapMat("MAT_Calcada_Basalto", "T_Calcada_Basalto", Color.white, false, default, 0f),
            new GemMapMat("MAT_Lajes_Claras", "T_Lajes_Claras", Color.white, false, default, 0f),
            new GemMapMat("MAT_Terra", "T_Terra", Color.white, false, default, 0f),
            new GemMapMat("MAT_Pedra_Muro", "T_Alvenaria", new Color(1f, 0.97f, 0.92f)),
            new GemMapMat("MAT_Pedra_Escura", "T_Alvenaria", new Color(0.62f, 0.6f, 0.6f)),
            new GemMapMat("MAT_Pedra_Clara", "T_Pedra_Clara", Color.white, true, default, 0.25f),
            new GemMapMat("MAT_Degraus", "T_Pedra_Clara", new Color(0.86f, 0.84f, 0.8f), true, default, 0.3f),
            new GemMapMat("MAT_Reboco_Branco", "T_Reboco", new Color(0.97f, 0.96f, 0.92f)),
            new GemMapMat("MAT_Reboco_Ocre", "T_Reboco", new Color(0.96f, 0.78f, 0.45f)),
            new GemMapMat("MAT_Reboco_Terracota", "T_Reboco", new Color(0.86f, 0.48f, 0.34f)),
            new GemMapMat("MAT_Reboco_Azul", "T_Reboco", new Color(0.62f, 0.78f, 0.92f)),
            new GemMapMat("MAT_Reboco_Rosa", "T_Reboco", new Color(0.95f, 0.7f, 0.66f)),
            new GemMapMat("MAT_Telha", "T_Telha", Color.white),
            new GemMapMat("MAT_Madeira", "T_Madeira", new Color(0.66f, 0.46f, 0.3f)),
            new GemMapMat("MAT_Madeira_Verde", "T_Madeira", new Color(0.24f, 0.58f, 0.42f)),
            new GemMapMat("MAT_Madeira_Azul", "T_Madeira", new Color(0.2f, 0.44f, 0.72f)),
            new GemMapMat("MAT_Ferro", null, new Color(0.16f, 0.16f, 0.19f)),
            new GemMapMat("MAT_Pano_Vermelho", null, new Color(0.86f, 0.2f, 0.18f)),
            new GemMapMat("MAT_Pano_Amarelo", null, new Color(0.98f, 0.78f, 0.18f)),
            new GemMapMat("MAT_Pano_Azul", null, new Color(0.15f, 0.42f, 0.82f)),
            new GemMapMat("MAT_Pano_Verde", null, new Color(0.15f, 0.62f, 0.42f)),
            new GemMapMat("MAT_Folhas", null, new Color(0.22f, 0.5f, 0.2f)),
            new GemMapMat("MAT_Folhas_Claras", null, new Color(0.38f, 0.64f, 0.26f)),
            new GemMapMat("MAT_Tronco", null, new Color(0.4f, 0.29f, 0.2f)),
            new GemMapMat("MAT_Mar", null, new Color(0.12f, 0.5f, 0.78f), false, new Color(0.02f, 0.08f, 0.14f), 0f),
            new GemMapMat("MAT_Monte", null, new Color(0.56f, 0.44f, 0.32f), false),
            new GemMapMat("MAT_Monte_Verde", null, new Color(0.42f, 0.52f, 0.28f), false),
            new GemMapMat("MAT_Spawn_Azul", null, new Color(0.2f, 0.5f, 0.95f), false, new Color(0.08f, 0.2f, 0.4f), 0f),
            new GemMapMat("MAT_Spawn_Vermelho", null, new Color(0.95f, 0.28f, 0.22f), false, new Color(0.4f, 0.1f, 0.08f), 0f),
            new GemMapMat("MAT_Gema", null, new Color(0.75f, 0.45f, 1f), true, new Color(0.45f, 0.2f, 0.7f), 0f),
            new GemMapMat("MAT_Emissor", null, new Color(0.85f, 0.7f, 1f), false, new Color(0.25f, 0.12f, 0.45f), 0f),
            new GemMapMat("MAT_Marca_Aterragem", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new GemMapMat("MAT_Marca_Aterragem_Apagada", null, new Color(0.75f, 0.55f, 1f), false, new Color(0.15f, 0.08f, 0.25f), 0f),
            new GemMapMat("MAT_Colisor", null, new Color(1f, 0.2f, 0.6f), false),
            new GemMapMat("MAT_Barro", null, new Color(0.72f, 0.38f, 0.24f)),
            new GemMapMat("MAT_Flor_Vermelha", null, new Color(0.92f, 0.16f, 0.2f), false),
            new GemMapMat("MAT_Flor_Rosa", null, new Color(0.98f, 0.45f, 0.7f), false),
            new GemMapMat("MAT_Flor_Amarela", null, new Color(1f, 0.82f, 0.18f), false),
            new GemMapMat("MAT_Vidro_Lanterna", null, new Color(1f, 0.86f, 0.55f), false, new Color(0.55f, 0.42f, 0.22f), 0f),
            new GemMapMat("MAT_Palha", null, new Color(0.78f, 0.64f, 0.38f)),
            new GemMapMat("MAT_Cesto", null, new Color(0.8f, 0.62f, 0.36f)),
            new GemMapMat("MAT_Bote_Azul", null, new Color(0.12f, 0.4f, 0.8f)),
            new GemMapMat("MAT_Bote_Amarelo", null, new Color(0.98f, 0.76f, 0.16f)),
            new GemMapMat("MAT_Bote_Vermelho", null, new Color(0.85f, 0.2f, 0.18f)),
            new GemMapMat("MAT_Basalto_Escuro", "T_Calcada_Basalto", new Color(0.22f, 0.22f, 0.25f), false, default, 0f),
        };

        public static GemMapConfig Config => new GemMapConfig
        {
            displayName = "Cidade Velha",
            artRoot = ArtRoot,
            filePrefix = "CidadeVelha",
            materialPrefix = "M_CV_",
            scenePath = ScenePath,
            mapPrefabPath = ProjectSetup.Root + "/Prefabs/Environment/PF_CidadeVelha.prefab",
            sceneRootName = "Map_CidadeVelha",
            mapName = "Cidade Velha",
            materials = Materials,
            gemFbx = ArtRoot + "/Models/CidadeVelha_Gem.fbx",      // a gema partilhada é construída aqui
            emitterHaloName = "GEN_Emissor_Halo",
            emitterBaseName = "GEN_Emissor_Base",
            emitterStyle = GemEmitterStyle.Pelourinho,
            arcHeight = 2.2f,
            expectedSize = "≈ 41 × 56",
        };

        [MenuItem("FPS-LAN/Mapas/Construir Cidade Velha (Pique-Gemas)", priority = 40)]
        static void BuildMenu() => GemMapBuilder.BuildFromMenu(Config, "Tools/Blender/CidadeVelha/LEIA-ME.md");

        [MenuItem("FPS-LAN/Mapas/Abrir cena Cidade Velha", priority = 41)]
        static void OpenMenu() => GemMapBuilder.OpenScene(Config);

        public static string Build() => GemMapBuilder.Build(Config);
    }
}
