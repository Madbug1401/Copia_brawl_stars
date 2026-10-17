using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Gera os dados de Game Experience em Assets/_Game/Resources/GameExperience: configuração de progressão,
    /// catálogo de pets (um asset por pet), catálogo de skins (um asset por skin) e catálogo de missões/conquistas.
    /// Voltar a gerar repõe os valores iniciais mas mantém os GUID (as referências não se perdem).
    /// Novos pets/skins: criar um asset (menu Create → FPS-LAN → Game Experience) e juntá-lo ao catálogo.
    /// </summary>
    [InitializeOnLoad]
    public static class GameExperienceAssetBuilder
    {
        public const string Root = "Assets/_Game/Resources/GameExperience";
        public const string PetsFolder = Root + "/Pets";
        public const string SkinsFolder = Root + "/Skins";
        public const string ConfigPath = Root + "/ProgressionConfig.asset";
        public const string PetCatalogPath = Root + "/PetCatalog.asset";
        public const string SkinCatalogPath = Root + "/SkinCatalog.asset";
        public const string QuestCatalogPath = Root + "/QuestCatalog.asset";

        static GameExperienceAssetBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(ConfigPath) || !File.Exists(PetCatalogPath) || !File.Exists(SkinCatalogPath) || !File.Exists(QuestCatalogPath)) Build();
            };
        }

        [MenuItem("FPS-LAN/Gerar dados de Game Experience (substitui valores)", priority = 6)]
        static void BuildMenu()
        {
            if (File.Exists(ConfigPath) && !EditorUtility.DisplayDialog("FPS-LAN",
                    "Voltar a gerar a configuração de progressão, pets, skins e missões com os valores iniciais? Ajustes feitos no Inspector serão substituídos.",
                    "Gerar", "Cancelar"))
                return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder(Root);
            EnsureFolder(PetsFolder);
            EnsureFolder(SkinsFolder);

            // Configuração de progressão
            var cfg = LoadOrCreate<ProgressionConfig>(ConfigPath);
            var cfgDefault = ProgressionConfig.CreateDefault();
            EditorUtility.CopySerialized(cfgDefault, cfg);
            cfg.name = "ProgressionConfig";
            Object.DestroyImmediate(cfgDefault);
            EditorUtility.SetDirty(cfg);

            // Pets
            var petCatalog = LoadOrCreate<PetCatalog>(PetCatalogPath);
            petCatalog.pets = new List<PetDefinition>();
            foreach (var def in GameExperienceDefaults.CreatePets())
            {
                var asset = LoadOrCreate<PetDefinition>(PetsFolder + "/" + def.petId + ".asset");
                EditorUtility.CopySerialized(def, asset);
                asset.name = def.petId;
                Object.DestroyImmediate(def);
                EditorUtility.SetDirty(asset);
                petCatalog.pets.Add(asset);
            }
            EditorUtility.SetDirty(petCatalog);

            // Skins
            var skinCatalog = LoadOrCreate<SkinCatalog>(SkinCatalogPath);
            skinCatalog.skins = new List<SkinDefinition>();
            foreach (var def in GameExperienceDefaults.CreateSkins())
            {
                var asset = LoadOrCreate<SkinDefinition>(SkinsFolder + "/" + def.skinId + ".asset");
                EditorUtility.CopySerialized(def, asset);
                asset.name = def.skinId;
                Object.DestroyImmediate(def);
                EditorUtility.SetDirty(asset);
                skinCatalog.skins.Add(asset);
            }
            EditorUtility.SetDirty(skinCatalog);

            // Missões, conquistas, desafios e desbloqueio de personagens
            var quests = LoadOrCreate<QuestCatalog>(QuestCatalogPath);
            GameExperienceDefaults.FillQuests(quests);
            EditorUtility.SetDirty(quests);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FPS-LAN] Dados de Game Experience gerados em " + Root);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (existing is T typed) return typed;
            if (existing != null) AssetDatabase.DeleteAsset(path);
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
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

    /// <summary>Pontos de entrada para -executeMethod (batchmode): gerar dados sem abrir o Editor.</summary>
    public static class BatchTasks
    {
        /// <summary>Unity.exe -batchmode -quit -projectPath ... -executeMethod FPSLAN.EditorTools.BatchTasks.BuildGameData</summary>
        public static void BuildGameData()
        {
            BrawlerAssetBuilder.Build();
            GameExperienceAssetBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log("[FPS-LAN] BatchTasks.BuildGameData concluído.");
        }
    }
}
