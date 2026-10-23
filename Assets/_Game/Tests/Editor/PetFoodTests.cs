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
        public void FeedingExpiresAfterTwentySecondsAndCooldownSurvivesCancellation()
        {
            var state = new PetFeedingState();
            Assert.IsTrue(PetFoodRules.TryStart(ref state, 10, 6, 123, PetAbilityType.Agility, true, true, false));
            Assert.AreEqual(20, state.Remaining(10));
            Assert.AreEqual(0, state.Remaining(30));
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 31, 6, 123, PetAbilityType.Agility, true, true, false));
            state.endsAt = 0; // death or unequipping
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 40, 6, 123, PetAbilityType.Agility, true, true, false));
            Assert.IsTrue(PetFoodRules.TryStart(ref state, 100, 6, 123, PetAbilityType.Agility, true, true, false));
        }

        [Test]
        public void ThreeUsesAreAllowedAndExtraRequestsCannotExtendTheBoost()
        {
            var state = new PetFeedingState();
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(PetFoodRules.TryStart(ref state, i*90, 99, 123, PetAbilityType.Mobility, true, true, false));
            var before = state;
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 270, int.MaxValue, 123, PetAbilityType.Mobility, true, true, false));
            Assert.AreEqual(before, state);
        }

        [TestCase(0, 1, PetAbilityType.Agility, true, true, false)]
        [TestCase(6, 0, PetAbilityType.Agility, true, true, false)]
        [TestCase(6, 1, PetAbilityType.None, true, true, false)]
        [TestCase(6, 1, PetAbilityType.Agility, false, true, false)]
        [TestCase(6, 1, PetAbilityType.Agility, true, false, false)]
        [TestCase(6, 1, PetAbilityType.Agility, true, true, true)]
        public void InvalidFeedingLeavesStateUntouched(int stock, int pet, PetAbilityType ability, bool alive, bool playing, bool competitive)
        {
            var state = new PetFeedingState();
            Assert.IsFalse(PetFoodRules.TryStart(ref state, 0, stock, pet, ability, alive, playing, competitive));
            Assert.AreEqual(default(PetFeedingState), state);
        }

        [TestCase(PetAbilityType.Detection)]
        [TestCase(PetAbilityType.Agility)]
        [TestCase(PetAbilityType.Protection)]
        [TestCase(PetAbilityType.Alert)]
        [TestCase(PetAbilityType.Mobility)]
        [TestCase(PetAbilityType.Recovery)]
        public void EveryBoostIsSmallAndNeverChangesDamageOrMaxHealth(PetAbilityType ability)
        {
            var mods = EffectiveModifiers.Neutral;
            mods.petAbility = (byte)ability; mods.petAbilityValue = 6;
            PetFoodRules.ApplyBoost(ref mods, ability);
            Assert.AreEqual(1, mods.damageMultiplier);
            Assert.AreEqual(1, mods.healthMultiplier);
            Assert.LessOrEqual(mods.speedMultiplier, 1.021f);
            Assert.LessOrEqual(mods.sprintMultiplier, 1.031f);
            Assert.LessOrEqual(mods.regenMultiplier, 1.051f);
            Assert.LessOrEqual(mods.petAbilityValue, 7);
            mods.competitive = true;
            var before = mods;
            PetFoodRules.ApplyBoost(ref mods, ability);
            Assert.AreEqual(before, mods);
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
