using UnityEditor;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Importação dos sons em Resources/Audio: música em streaming comprimida (não ocupa memória) e falas
    /// comprimidas em memória (tocam sem atraso). Assim os WAV grandes não pesam na build.
    /// </summary>
    public class AudioImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            if (path.Contains("/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.6f;
                ai.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.75f;
                ai.loadInBackground = false;
            }
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.forceToMono = false;
        }
    }
}
