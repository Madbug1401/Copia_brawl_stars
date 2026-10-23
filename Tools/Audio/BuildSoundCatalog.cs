using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using FPSLAN;
using UnityEditor;
using UnityEngine;

public static class BuildSoundCatalog
{
    public static string Main()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var manifest = JObject.Parse(File.ReadAllText("Tools/Audio/sound_pack_manifest.json"));
        var cues = new List<SfxCatalog.Cue>();
        foreach (var data in manifest["cues"])
        {
            var clips = new List<AudioClip>();
            foreach (var item in data["clips"])
            {
                string clipPath = (string)item["asset"];
                AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null) throw new Exception("Audio clip not imported: " + clipPath);
                clips.Add(clip);
            }
            cues.Add(new SfxCatalog.Cue {
                sound = (Sound)Enum.Parse(typeof(Sound), (string)data["sound"]),
                clips = clips.ToArray(), volume = (float)data["volume"], pitchVariation = (float)data["pitchVariation"]
            });
        }
        const string path = "Assets/_Game/Resources/Audio/SfxCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<SfxCatalog>(path);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<SfxCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }
        catalog.cues = cues.ToArray();
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        int count = 0;
        foreach (var cue in catalog.cues) count += cue.clips.Length;
        return "Imported " + cues.Count + " cues / " + count + " clips. Catalog: " + path;
    }
}
