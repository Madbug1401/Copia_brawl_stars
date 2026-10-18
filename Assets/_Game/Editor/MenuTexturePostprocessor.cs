using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Imagens de UI (Resources/UI/...): sem redimensionar para potência de 2 (mantém as proporções), sem mipmaps
    /// e com transparência correta. Aplica-se automaticamente a qualquer imagem nova que entre nessa pasta.
    /// </summary>
    public class MenuTexturePostprocessor : AssetPostprocessor
    {
        const string UiFolder = "Assets/_Game/Resources/UI/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UiFolder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;   // tamanhos não múltiplos de 4; são poucas e pequenas
        }
    }
}
