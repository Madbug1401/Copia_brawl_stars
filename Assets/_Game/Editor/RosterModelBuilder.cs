using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>Associa cada modelo entregue ao ID de rede e ao estilo de arma já existentes.</summary>
    public static class RosterModelBuilder
    {
        static readonly string[] ModelNames = { "Ekko", "LupusMarinus", "MrS", "NCage", "Riot" };
        static readonly string[] BrawlerIds = { "gunslinger", "artillery", "sprayer", "trapper", "shotgunner" };
        const string ArtFolder = "Assets/_Game/Art/Characters/";

        [MenuItem("FPS-LAN/Personagens: montar os cinco modelos do elenco", priority = 33)]
        public static void BuildAll() => BuildSelected(null);

        public static void BuildMrS() => BuildSelected("sprayer");

        static void BuildSelected(string selectedId)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Sair de Play antes de montar personagens.");
            // Estes clips são neutros. As mãos seguem o estilo do brawler por IK, sem impor a pose Rifle aos outros.
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var role in new[] { "Idle", "Walk", "Run", "Jump", "Death" })
            {
                var path = ArtFolder + "Lince/" + (role == "Death" ? "" : "Procedural/") + "Lince@" + role + ".fbx";
                clips.Add(role.ToLowerInvariant(), AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .First(c => !c.name.StartsWith("__preview__") && c.humanMotion));
            }
            var report = new StringBuilder();
            for (int i = 0; i < ModelNames.Length; i++)
            {
                string name = ModelNames[i], id = BrawlerIds[i];
                if (selectedId != null && id != selectedId) continue;
                string folder = ArtFolder + name;
                string modelPath = folder + "/" + name + "_Source.fbx";
                string texturePath = folder + "/" + name + "_diffuse.png";
                var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("Falta o modelo: " + modelPath);
                if (!File.Exists(texturePath))
                {
                    string extracted = folder + "/bound_output.fbm";
                    if (!File.Exists(extracted)) importer.ExtractTextures(folder);
                    if (!File.Exists(extracted)) throw new InvalidOperationException("Falta a textura incorporada: " + name);
                    // O FBX chama .fbm a uma imagem PNG; o Unity precisa da extensão real para a importar como textura.
                    var error = AssetDatabase.MoveAsset(extracted, texturePath);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                }
                var texImporter = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                texImporter.textureShape = TextureImporterShape.Texture2D;
                texImporter.maxTextureSize = 2048;
                texImporter.sRGBTexture = true;
                texImporter.SaveAndReimport();
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == null)
                    throw new InvalidOperationException("A textura 2D não foi importada: " + texturePath);
                report.AppendLine(name + " → " + id);
                report.AppendLine(CharacterModelBuilder.Build(id, folder, name, clips, texturePath));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/Characters/" + id + ".prefab");
                var anim = prefab != null ? prefab.GetComponentInChildren<Animator>() : null;
                if (anim == null || !anim.isHuman || !anim.avatar.isValid || anim.runtimeAnimatorController == null)
                    throw new InvalidOperationException("Não foi possível montar o humanoide " + name + ".\n" + report);
            }
            Directory.CreateDirectory("Temp/roster-validation");
            File.WriteAllText("Temp/roster-validation/import.txt", report.ToString());
            Debug.Log("[Elenco] " + report);
        }
    }
}
