using System;
using NUnit.Framework;
using UnityEngine;

namespace FPSLAN.Tests
{
    public class PetFoodTests
    {
        GameExperienceContext Context(MemoryProfileStorage storage) => new GameExperienceContext(
            ProgressionConfig.CreateDefault(), PetCatalog.CreateDefault(), SkinCatalog.CreateDefault(),
            QuestCatalog.CreateDefault(), storage, "upra-test", new FixedClock(new DateTime(2026, 10, 18)));

        [Test]
        public void StarterFoodIsGrantedOnceAndSpendingSurvivesReload()
        {
            var storage = new MemoryProfileStorage();
            var ctx = Context(storage);
            Assert.AreEqual(6, ctx.PetFood.Stock);
            Assert.IsTrue(ctx.PetFood.Consume());
            Assert.AreEqual(5, Context(storage).PetFood.Stock);
        }

        [Test]
        public void ExistingProfileReceivesFoodWithoutLosingProgress()
        {
            var storage = new MemoryProfileStorage();
            var profile = SaveManager.NewProfile();
            profile.coins = 74; profile.playerXp = 125;
            profile.upraFood = 0; profile.upraIntroduced = false;
            new SaveManager(storage, "upra-test").Save(profile);
            var ctx = Context(storage);
            Assert.AreEqual(6, ctx.PetFood.Stock);
            Assert.AreEqual(74, ctx.Inventory.Coins);
            Assert.AreEqual(125, ctx.Profile.playerXp);
        }

        [Test]
        public void ValidMatchGrantsFoodOnceAndInvalidMatchGrantsNone()
        {
            var ctx = Context(new MemoryProfileStorage());
            var r = new MatchResultData { matchId = "food-match", brawlerId = "sprayer", participated = true };
            Assert.AreEqual(2, ctx.Progression.ApplyMatchResult(r).upraFood);
            Assert.IsTrue(ctx.Progression.ApplyMatchResult(r).duplicate);
            Assert.AreEqual(8, ctx.PetFood.Stock);
            r.matchId = "invalid-match"; r.participated = false;
            Assert.AreEqual(0, ctx.Progression.ApplyMatchResult(r).upraFood);
            Assert.AreEqual(8, ctx.PetFood.Stock);
        }

        [Test]
        public void PacksRequireCoinsAndStockCannotOverflowOrBecomeNegative()
        {
            var ctx = Context(new MemoryProfileStorage());
            Assert.IsFalse(ctx.PetFood.BuyPack());
            ctx.Inventory.AddCoins(15);
            Assert.IsTrue(ctx.PetFood.BuyPack());
            Assert.AreEqual(9, ctx.PetFood.Stock);
            Assert.AreEqual(0, ctx.Inventory.Coins);
            ctx.PetFood.Add(int.MaxValue);
            Assert.AreEqual(999, ctx.PetFood.Stock);
            ctx.Inventory.AddCoins(15);
            Assert.IsFalse(ctx.PetFood.BuyPack());
            Assert.AreEqual(15, ctx.Inventory.Coins);
            ctx.Profile.upraFood = 0;
            Assert.IsFalse(ctx.PetFood.Consume());
            Assert.AreEqual(0, ctx.PetFood.Stock);
        }

        [Test]
        public void FeedingIsTwoSecondAnimationThenTwelveSecondBoostAndCooldownSurvivesCancellation()
        {
            var state = new PetFeedingState();
            Assert.IsTrue(PetFoodRules.TryStart(ref state, 10, 6, 123, PetSpecies.Rabbit, true, true));
            Assert.AreEqual(PetSpecies.Rabbit, state.Species);
            Assert.AreEqual(14, state.Remaining(10));
            Assert.IsFalse(PetFoodRules.BoostActive(state, 11));   // ainda a comer
            Assert.IsTrue(PetFoodRules.BoostActive(state, 12.5));  // bónus ativo
            Assert.AreEqual(0, state.Remaining(24));
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 30, 6, 123, PetSpecies.Rabbit, true, true));
            state.endsAt = 0; // morte ou trocar de pet
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 40, 6, 123, PetSpecies.Rabbit, true, true));
            Assert.IsTrue(PetFoodRules.TryStart(ref state, 70, 6, 123, PetSpecies.Rabbit, true, true));
        }

        [Test]
        public void ThreeMealsPerMatchAndExtraRequestsCannotExtendTheBoost()
        {
            var state = new PetFeedingState();
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(PetFoodRules.TryStart(ref state, i * 60, 99, 123, PetSpecies.Dog, true, true));
            var before = state;
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 180, int.MaxValue, 123, PetSpecies.Dog, true, true));
            Assert.AreEqual(before, state);
        }

        [TestCase(0, 1, true, true)]
        [TestCase(6, 0, true, true)]
        [TestCase(6, 1, false, true)]
        [TestCase(6, 1, true, false)]
        public void InvalidFeedingLeavesStateUntouched(int stock, int pet, bool alive, bool playing)
        {
            var state = new PetFeedingState();
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 0, stock, pet, PetSpecies.Cat, alive, playing));
            Assert.AreEqual(default(PetFeedingState), state);
        }

        [Test]
        public void EachPetGivesItsOwnBoostAndNoneChangesDamage()
        {
            var m = EffectiveModifiers.Neutral; PetFoodRules.ApplyBoost(ref m, PetSpecies.Rabbit);
            Assert.AreEqual(1.2f, m.speedMultiplier, 0.001f);
            m = EffectiveModifiers.Neutral; PetFoodRules.ApplyBoost(ref m, PetSpecies.Turtle);
            Assert.AreEqual(1.25f, m.healthMultiplier, 0.001f);
            m = EffectiveModifiers.Neutral; PetFoodRules.ApplyBoost(ref m, PetSpecies.Dog);
            Assert.AreEqual(3f, m.regenMultiplier, 0.001f);
            m = EffectiveModifiers.Neutral; PetFoodRules.ApplyBoost(ref m, PetSpecies.Cat);
            Assert.AreEqual(0.7f, m.reloadTimeMultiplier, 0.001f);
            m = EffectiveModifiers.Neutral; PetFoodRules.ApplyBoost(ref m, PetSpecies.Rooster);
            Assert.AreEqual(1.5f, m.superChargeMultiplier, 0.001f);
            foreach (PetSpecies sp in System.Enum.GetValues(typeof(PetSpecies)))
            {
                m = EffectiveModifiers.Neutral;
                PetFoodRules.ApplyBoost(ref m, sp);
                Assert.AreEqual(1f, m.damageMultiplier, sp.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(PetFoodRules.BoostText(sp)));
            }
        }

        [Test]
        public void SprayBrandingUsesTheRealLogoAndDoesNotCreateColliders()
        {
            var material = Resources.Load<Material>("Branding/SitaSprayLabel");
            Assert.NotNull(material);
            Assert.NotNull(material.mainTexture);
            var root = new GameObject("Sita test");
            try
            {
                SprayCanBranding.AddTo(root.transform);
                Assert.AreEqual(3, root.GetComponentsInChildren<MeshRenderer>().Length);
                Assert.AreEqual(0, root.GetComponentsInChildren<Collider>().Length);
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                    Assert.Greater(filter.sharedMesh.vertexCount, 20);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
