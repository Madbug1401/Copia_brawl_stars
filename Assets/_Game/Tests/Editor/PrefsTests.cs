using NUnit.Framework;
using UnityEngine;

namespace FPSLAN.Tests
{
    /// <summary>A cache de preferências tem de se comportar exatamente como o PlayerPrefs.</summary>
    public class PrefsTests
    {
        const string Key = "fpslan.tests.prefs";

        [SetUp, TearDown]
        public void Clean()
        {
            PlayerPrefs.DeleteKey(Key);
            Prefs.Clear();
        }

        [Test]
        public void MissingKeyReturnsTheDefaultOfEachCall()
        {
            Assert.IsFalse(Prefs.HasKey(Key));
            Assert.AreEqual(7, Prefs.GetInt(Key, 7));
            Assert.AreEqual(9, Prefs.GetInt(Key, 9));
            Assert.AreEqual("a", Prefs.GetString(Key, "a"));
            Assert.AreEqual(1.5f, Prefs.GetFloat(Key, 1.5f));
        }

        [Test]
        public void WritesUpdateCacheAndPlayerPrefs()
        {
            Assert.AreEqual(3, Prefs.GetInt(Key, 3));   // fica em cache como "não existe"
            Prefs.SetInt(Key, 42);
            Assert.IsTrue(Prefs.HasKey(Key));
            Assert.AreEqual(42, Prefs.GetInt(Key, 3));
            Assert.AreEqual(42, PlayerPrefs.GetInt(Key, 3));
            Prefs.Clear();
            Assert.AreEqual(42, Prefs.GetInt(Key, 3), "relido do registo");
        }

        [Test]
        public void DeleteMakesTheKeyMissingAgain()
        {
            Prefs.SetFloat(Key, 2f);
            Prefs.DeleteKey(Key);
            Assert.IsFalse(Prefs.HasKey(Key));
            Assert.AreEqual(5f, Prefs.GetFloat(Key, 5f));
            Assert.IsFalse(PlayerPrefs.HasKey(Key));
        }

        [Test]
        public void StoredNaNStillExistsLikeInPlayerPrefs()
        {
            PlayerPrefs.SetFloat(Key, float.NaN);
            Prefs.Clear();
            Assert.IsTrue(float.IsNaN(PlayerPrefs.GetFloat(Key, 1f)));
            Assert.IsTrue(float.IsNaN(Prefs.GetFloat(Key, 1f)), "NaN não é o valor por omissão");
        }

        [Test]
        public void HasKeyStaysTrueAcrossTypeChanges()
        {
            Prefs.SetString(Key, "a");
            Prefs.SetInt(Key, 1);
            Assert.IsTrue(Prefs.HasKey(Key));
            Assert.AreEqual(PlayerPrefs.GetFloat(Key, 3f), Prefs.GetFloat(Key, 3f));
        }

        /// <summary>A cache só está certa se ninguém escrever no PlayerPrefs por fora: o código do jogo usa Prefs.</summary>
        [Test]
        public void GameCodeNeverUsesPlayerPrefsDirectly()
        {
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var file in System.IO.Directory.GetFiles("Assets/_Game/Scripts", "*.cs", System.IO.SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').EndsWith("/Core/Prefs.cs")) continue;
                foreach (var line in System.IO.File.ReadAllLines(file))
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\bPlayerPrefs\.[A-Z]") && !line.TrimStart().StartsWith("//"))
                        offenders.Add(file + ": " + line.Trim());
            }
            Assert.IsEmpty(offenders, "Usar Prefs em vez de PlayerPrefs:\n" + string.Join("\n", offenders));
        }

        [Test]
        public void KeyOfAnotherTypeBehavesLikePlayerPrefs()
        {
            PlayerPrefs.SetString(Key, "texto");
            Prefs.Clear();
            Assert.AreEqual(PlayerPrefs.GetInt(Key, 11), Prefs.GetInt(Key, 11));
            Assert.AreEqual(PlayerPrefs.GetFloat(Key, 2.5f), Prefs.GetFloat(Key, 2.5f));
            Assert.AreEqual("texto", Prefs.GetString(Key, "x"));
            Prefs.SetInt(Key, 4);   // muda de tipo
            Assert.AreEqual(PlayerPrefs.GetString(Key, "x"), Prefs.GetString(Key, "x"));
            Assert.AreEqual(4, Prefs.GetInt(Key, 0));
        }
    }
}
