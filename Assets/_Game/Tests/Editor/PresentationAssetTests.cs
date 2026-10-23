using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.Tests
{
    public class PresentationAssetTests
    {
        [TestCase("Fonts/GoblinOne-Regular")]
        [TestCase("Fonts/LeagueGothic-Regular")]
        public void TypographyIsEmbedded(string path) => Assert.IsNotNull(Resources.Load<Font>(path));

        [TestCase("ekko")]
        [TestCase("lince")]
        [TestCase("riot")]
        [TestCase("mrs")]
        [TestCase("ncage")]
        [TestCase("lupus")]
        public void TreatedVoiceIsAvailableWithoutDuplicateResourceNames(string key)
        {
            var clips = Resources.LoadAll<AudioClip>("Audio/Voices");
            int count = 0;
            foreach (var clip in clips) if (clip.name == key) { count++; Assert.Greater(clip.length, 0.1f); }
            Assert.AreEqual(1, count, key);
        }

        [Test]
        public void SetupMusicExists() => Assert.IsNotNull(Resources.Load<AudioClip>("Audio/Music/server"));

        [Test]
        public void SponsorDecorationPreservesCollisionAndSpawnLayout()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Environment/PF_CidadeVelha.prefab");
            var signs = prefab.transform.Find("Patrocinadores");
            Assert.IsNotNull(signs);
            Assert.AreEqual(16, signs.childCount);
            Assert.AreEqual(0, signs.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(67, prefab.GetComponentsInChildren<Collider>(true).Length);
            Assert.AreEqual(10, prefab.GetComponentsInChildren<SpawnPoint>(true).Length);
        }

        [TestCase("sniper")]
        [TestCase("shotgunner")]
        [TestCase("sprayer")]
        [TestCase("artillery")]
        [TestCase("trapper")]
        [TestCase("gunslinger")]
        public void EachCharacterHasConciseStoryAndRole(string id)
        {
            Assert.IsNotEmpty(CharacterStories.Role(id));
            Assert.That(CharacterStories.Summary(id).Length, Is.InRange(40, 160));
        }

        [Test]
        public void BuildStartsFullscreen() => Assert.AreEqual(FullScreenMode.FullScreenWindow, PlayerSettings.fullScreenMode);

        [Test]
        public void ExistingProfileHistoryIsTranslatedWithoutChangingSavedText()
        {
            const string saved = "Missão: Vencer dez partidas";
            Assert.AreEqual("Quest: Win ten matches", LegacyProfileText.English(saved));
            Assert.AreEqual("Missão: Vencer dez partidas", saved);
            Assert.AreEqual("+150 coins", LegacyProfileText.English("+150 moedas"));
            Assert.AreEqual("Strategist", LegacyProfileText.English("titulo_estratega"));
        }
    }
}
