using UnityEditor;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Importação dos braços da 1.ª pessoa animados no Blender (Resources/Characters/*_fpview.fbx, ver BakedViewArms):
    /// animação Legacy (o código escolhe e mistura as animações à mão), materiais só pelo nome (o código troca-os pelo toon do personagem)
    /// e sem câmaras/luzes.
    /// </summary>
    public class FpViewArmsImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.EndsWith(BakedViewArms.ResourceSuffix + ".fbx")) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.optimizeGameObjects = false;
        }
    }
}
