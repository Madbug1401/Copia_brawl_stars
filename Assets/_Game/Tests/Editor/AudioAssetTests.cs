using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.Tests
{
    public class AudioAssetTests
    {
        [Test]
        public void ImportedCatalogHasUniqueCompleteCuesAndPreparedAudio()
        {
            var catalog = Resources.Load<SfxCatalog>("Audio/SfxCatalog");
            Assert.NotNull(catalog);
            Assert.AreEqual(40, catalog.cues.Length);   // 26 (18/10) + 14 do pacote P1 (26/10)
            var sounds = new HashSet<Sound>();
            int count = 0;
            foreach (var cue in catalog.cues)
            {
                Assert.True(sounds.Add(cue.sound), "Duplicate " + cue.sound);
                Assert.That(cue.volume, Is.InRange(.01f, 1f));
                Assert.That(cue.pitchVariation, Is.InRange(0f, .1f));
                Assert.NotNull(cue.clips);
                Assert.IsNotEmpty(cue.clips);
                foreach (var clip in cue.clips)
                {
                    Assert.NotNull(clip, cue.sound.ToString());
                    count++;
                    Assert.AreEqual(1, clip.channels, clip.name);
                    Assert.AreEqual(48000, clip.frequency, clip.name);
                    Assert.That(clip.length, Is.InRange(.1f, 3.5f), clip.name);
                    var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                    Assert.True(importer.forceToMono, clip.name);
                    Assert.False(importer.loadInBackground, clip.name);
                    Assert.True(clip.preloadAudioData, clip.name);
                    Assert.AreEqual(AudioClipLoadType.DecompressOnLoad, importer.defaultSampleSettings.loadType);
                    Assert.AreEqual(AudioCompressionFormat.PCM, importer.defaultSampleSettings.compressionFormat);
                    var samples = new float[clip.samples];
                    Assert.True(clip.GetData(samples, 0), clip.name);
                    float peak = 0;
                    bool finite = true;
                    foreach (float sample in samples)
                    {
                        if (float.IsNaN(sample) || float.IsInfinity(sample)) finite = false;
                        peak = Mathf.Max(peak, Mathf.Abs(sample));
                    }
                    Assert.True(finite, clip.name);
                    Assert.That(peak, Is.InRange(.03f, .72f), clip.name + " peak");
                    if (cue.sound == Sound.Step || cue.sound == Sound.Minigun)
                        Assert.LessOrEqual(clip.length, .32f, "Full recording must not play on every step/burst");
                }
            }
            Assert.AreEqual(51, count);
            Assert.False(sounds.Contains(Sound.Snare), "The poison file must not be used for a metal snare");
            Assert.False(sounds.Contains(Sound.UiClick), "Menu feedback is independent from weapon recordings");
        }

        [Test]
        public void SerializedSoundIdsAndWeaponRoutingRemainCompatible()
        {
            Assert.AreEqual(24, (int)Sound.Smg);
            Assert.AreEqual(25, (int)Sound.Minigun);
            Assert.AreEqual(37, (int)Sound.Evolve);
            Assert.AreEqual(39, (int)Sound.GemSpawn);
            Assert.AreEqual(51, (int)Sound.SuperNCage);
            foreach (var pair in new[] {
                ("gunslinger/gunslinger_Primary", Sound.Smg),
                ("gunslinger/gunslinger_Super", Sound.Minigun),
                ("shotgunner/shotgunner_Primary", Sound.Shotgun),
                ("sniper/sniper_Primary", Sound.Sniper),
                ("trapper/trapper_Primary", Sound.Bow),
                ("sprayer/sprayer_Primary", Sound.Spray),
                ("artillery/artillery_Primary", Sound.Lob) })
            {
                var attack = AssetDatabase.LoadAssetAtPath<AttackDefinition>("Assets/_Game/Data/Brawlers/" + pair.Item1 + ".asset");
                Assert.NotNull(attack);
                Assert.AreEqual(pair.Item2, attack.visuals.sound);
            }
        }
    }
}
