using System.Text;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Braços da 1.ª pessoa com esqueleto: importa os FBX gerados no Blender (Tools/Blender/FPArms/build_fp_arms.py,
    /// Art/Characters/&lt;Nome&gt;/FP/&lt;Nome&gt;_FPArms.fbx) e grava Resources/Characters/&lt;brawlerId&gt;_fphands.prefab,
    /// que o ViewModelRig usa automaticamente (<see cref="FPSkinnedArms"/>).
    /// </summary>
    public static class FPArmsBuilder
    {
        public static readonly (string id, string folder)[] Roster =
        {
            ("gunslinger", "Ekko"),
            ("trapper", "NCage"),
            ("artillery", "LupusMarinus"),
            ("sprayer", "MrS"),
            ("shotgunner", "Riot"),
            ("sniper", "Lince"),
        };

        const string CharRoot = "Assets/_Game/Art/Characters";
        const string ResFolder = "Assets/_Game/Resources/Characters";

        [MenuItem("FPS-LAN/Personagens: braços da 1.ª pessoa com esqueleto (todos)", priority = 33)]
        static void Menu()
        {
            var r = BuildAll();
            Debug.Log("[Braços 1.ª pessoa]\n" + r);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("FPS-LAN", r, "OK");
        }

        public static string BuildAll()
        {
            var sb = new StringBuilder();
            foreach (var (id, folder) in Roster) sb.AppendLine(Build(id, folder));
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public static string Build(string id, string folder)
        {
            string fbx = $"{CharRoot}/{folder}/FP/{folder}_FPArms.fbx";
            var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) return $"{folder}: falta {fbx}";
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.importBlendShapes) { mi.importBlendShapes = false; dirty = true; }
            if (!mi.isReadable) { mi.isReadable = true; dirty = true; }
            if (dirty) mi.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) return $"{folder}: não consegui carregar {fbx}";
            Material mat = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { $"{CharRoot}/{folder}" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("_Toon.mat")) { mat = AssetDatabase.LoadAssetAtPath<Material>(p); break; }
            }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = folder + "_FPHands";
            foreach (var an in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
            int tris = 0, smrs = 0;
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (mat != null) smr.sharedMaterial = mat;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.updateWhenOffscreen = true;
                if (smr.sharedMesh != null) tris += smr.sharedMesh.triangles.Length / 3;
                smrs++;
            }
            inst.AddComponent<FPSkinnedArms>();
            if (!AssetDatabase.IsValidFolder(ResFolder)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Characters");
            string path = $"{ResFolder}/{id}_fphands.prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);
            return $"{folder} ({id}): {path} · {smrs} braços · {tris} triângulos · material {(mat != null ? mat.name : "—")}";
        }
    }
}
