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
            // colisores do mapa fora dos interiores das casas (os interiores têm os seus, gerados à parte)
            var interiors = prefab.transform.Find("Cenario/Interiores");
            int outside = 0;
            foreach (var c in prefab.GetComponentsInChildren<Collider>(true))
                if (interiors == null || !c.transform.IsChildOf(interiors)) outside++;
            Assert.AreEqual(67, outside);
            int team = 0, dm = 0;
            foreach (var sp in prefab.GetComponentsInChildren<SpawnPoint>(true))
            {
                if (sp.deathmatchOnly) { dm++; Assert.AreEqual(-1, sp.team, sp.name); }
                else team++;
            }
            Assert.AreEqual(10, team, "spawns das equipas (Pique-Gemas)");
            Assert.GreaterOrEqual(dm, 10, "spawns do Deathmatch nas casas");
            Assert.IsNull(prefab.transform.Find("Cenario/GRD_Spawn_Blue"), "sem faixa azul");
            Assert.IsNull(prefab.transform.Find("Cenario/GRD_Spawn_Red"), "sem faixa vermelha");
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
