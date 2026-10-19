using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPSLAN.Tests
{
    public class AudioPlaybackTests
    {
        GameObject originalRoot;

        [SetUp] public void Before() => originalRoot = GameObject.Find("[Sfx]");
        [TearDown] public void After()
        {
            if (originalRoot == null)
            {
                var root = GameObject.Find("[Sfx]");
                if (root != null) Object.DestroyImmediate(root);
            }
        }

        static AudioSource[] Pool(string field) => (AudioSource[])typeof(Sfx).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        [UnityTest]
        public IEnumerator ImportedClipsPlayWithVariationsSpatialAudioAndFallbacks()
        {
            Sfx.Play2D(Sound.Step);
            var catalog = Resources.Load<SfxCatalog>("Audio/SfxCatalog");
            var step = System.Array.Find(catalog.cues, c => c.sound == Sound.Step);
            var select = typeof(Sfx).GetMethod("SelectClip", BindingFlags.Static | BindingFlags.NonPublic);
            AudioClip previous = null;
            var seen = new System.Collections.Generic.HashSet<AudioClip>();
            for (int i = 0; i < 64; i++)
            {
                object[] args = { Sound.Step, 0f, 0f };
                var clip = (AudioClip)select.Invoke(null, args);
                Assert.Contains(clip, step.clips);
                Assert.AreNotEqual(previous, clip, "Consecutive footsteps must vary");
                Assert.That((float)args[2], Is.InRange(.94f, 1.06f));
                seen.Add(clip);
                previous = clip;
            }
            Assert.Greater(seen.Count, 1);

            var position = new Vector3(31, 2, -7);
            Sfx.PlayAt(Sound.Shotgun, position, .5f);
            var spatial = System.Array.Find(Pool("s_3D"), s => s.clip != null && s.clip.name == "Shotgun");
            Assert.NotNull(spatial);
            Assert.AreEqual(position, spatial.transform.position);
            Assert.AreEqual(1f, spatial.spatialBlend);
            Assert.AreEqual(0f, spatial.dopplerLevel);
            Assert.AreEqual(.425f, spatial.volume, .0001f);

            Sfx.Play2D(Sound.Reward);
            var reward = System.Array.Find(Pool("s_2D"), s => s.clip != null && s.clip.name == "Reward");
            Assert.NotNull(reward);
            Assert.AreEqual(0f, reward.spatialBlend);
            Assert.AreEqual(1f, reward.pitch, "Musical feedback must keep its pitch");

            Sfx.Play2D(Sound.Snare);
            Assert.NotNull(System.Array.Find(Pool("s_2D"), s => s.clip != null && s.clip.name == "snare"), "Missing recordings keep a working fallback");
            Sfx.Play2D(Sound.UiClick);
            Assert.NotNull(System.Array.Find(Pool("s_2D"), s => s.clip != null && s.clip.name == "uiclick"));
            yield return null;
        }
    }
}
