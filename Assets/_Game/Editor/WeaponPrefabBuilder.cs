using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Transforma o FBX de uma arma num prefab em Resources/Weapons/&lt;estilo&gt;, que o jogo usa no lugar da arma de
    /// primitivas desse estilo (3.ª e 1.ª pessoa). O FBX deve vir com a origem no punho, o cano para +Z e os
    /// marcadores "Muzzle" (boca do cano) e "Support" (mão de apoio).
    /// </summary>
    public static class WeaponPrefabBuilder
    {
        [MenuItem("FPS-LAN/Armas: montar sniper do Lince (modelo 3D)", priority = 35)]
        static void BuildLinceRifle()
        {
            var report = Build("Assets/_Game/Art/Weapons/Lince/LinceRifle.fbx", "Assets/_Game/Art/Weapons/Lince/LinceRifle_diffuse.png", RigStyle.Sniper);
            Debug.Log("[Armas] " + report);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("FPS-LAN", report, "OK");
        }

        [MenuItem("FPS-LAN/Armas: voltar à sniper de primitivas", priority = 36)]
        static void RemoveLinceRifle()
        {
            var path = "Assets/_Game/Resources/Weapons/" + RigStyle.Sniper + ".prefab";
            Debug.Log(AssetDatabase.DeleteAsset(path) ? "[Armas] Removido " + path : "[Armas] Não havia " + path);
        }

        public static string Build(string fbxPath, string texturePath, RigStyle style)
        {
            var mi = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (mi == null) return "FBX não encontrado: " + fbxPath;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

            // material cartoon com a textura da arma
            var matPath = System.IO.Path.ChangeExtension(fbxPath, null) + "_Toon.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            var shader = Shader.Find("FPSLAN/ToonOutline") ?? Shader.Find("Standard");
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = shader;
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_VertexColor")) mat.SetFloat("_VertexColor", 0f);
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0.0012f);
            if (mat.HasProperty("_OutlineColor")) mat.SetColor("_OutlineColor", new Color(0.06f, 0.05f, 0.1f));
            if (mat.HasProperty("_RimColor")) mat.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.25f));
            EditorUtility.SetDirty(mat);

            // prefab: raiz vazia (origem = punho) com o modelo dentro
            var root = new GameObject("Weapon_" + style + "_Model");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
            Transform muzzle = null, support = null;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Muzzle") muzzle = t;
                else if (t.name == "Support") support = t;
                else if (t.name == "Up") Object.DestroyImmediate(t.gameObject);
            }

            const string res = "Assets/_Game/Resources/Weapons";
            if (!AssetDatabase.IsValidFolder(res)) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Weapons");
            var prefabPath = res + "/" + style + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            string info = "Pronto: " + prefabPath +
                          "\nBoca do cano: " + (muzzle != null ? root.transform.InverseTransformPoint(muzzle.position).ToString("F2") : "em falta") +
                          "\nApoio da mão: " + (support != null ? root.transform.InverseTransformPoint(support.position).ToString("F2") : "em falta");
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return info;
        }
    }
}
