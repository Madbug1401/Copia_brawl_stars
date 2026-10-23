using System;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>One host-authored feeding window. Death cancels the effect, but not its cooldown or match quota.</summary>
    public struct PetFeedingState : INetworkSerializeByMemcpy, IEquatable<PetFeedingState>
    {
        public double endsAt, readyAt;
        public int petHash;
        public byte uses, ability;
        public float Remaining(double now) => (float)Math.Max(0, endsAt - now);
        public float Cooldown(double now) => (float)Math.Max(0, readyAt - now);
        public bool Equals(PetFeedingState other) => endsAt == other.endsAt && readyAt == other.readyAt &&
            petHash == other.petHash && uses == other.uses && ability == other.ability;
        public override bool Equals(object other) => other is PetFeedingState state && Equals(state);
        public override int GetHashCode() => petHash ^ uses ^ endsAt.GetHashCode();
    }

    public static class PetFoodRules
    {
        public const int StarterFood = 6, FoodPerMatch = 2, MaxStock = 999;
        public const int MaxUsesPerMatch = 3, PackSize = 3, PackCost = 15;
        public const float Duration = 20f, Cooldown = 90f;
        /// <summary>Animação de alimentar (o jogador fica parado, o pet come da tigela UPRA).</summary>
        public const float FeedAnimDuration = 2f;

        public static bool TryStart(ref PetFeedingState state, double now, int stock, int petHash,
            PetAbilityType ability, bool alive, bool playing, bool competitive)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || !alive || !playing || competitive || stock <= 0 ||
                petHash == 0 || ability <= PetAbilityType.None || ability > PetAbilityType.Recovery ||
                state.uses >= MaxUsesPerMatch || state.Cooldown(now) > 0 || state.Remaining(now) > 0) return false;
            state.endsAt = now + Duration;
            state.readyAt = now + Cooldown;
            state.petHash = petHash;
            state.ability = (byte)ability;
            state.uses++;
            return true;
        }

        public static void ApplyBoost(ref EffectiveModifiers mods, PetAbilityType ability)
        {
            if (mods.competitive || mods.PetAbility != ability) return;
            switch (ability)
            {
                case PetAbilityType.Detection:
                case PetAbilityType.Alert: mods.petAbilityValue = Mathf.Min(25f, mods.petAbilityValue + 1f); break;
                case PetAbilityType.Agility: mods.speedMultiplier *= 1.02f; break;
                case PetAbilityType.Mobility: mods.sprintMultiplier *= 1.03f; break;
                case PetAbilityType.Protection:
                case PetAbilityType.Recovery: mods.regenMultiplier *= 1.05f; break;
            }
        }

        public static string BoostText(PetAbilityType ability)
        {
            switch (ability)
            {
                case PetAbilityType.Detection: return "+1 m detection range";
                case PetAbilityType.Alert: return "+1 m warning range";
                case PetAbilityType.Agility: return "+2% movement speed";
                case PetAbilityType.Mobility: return "+3% sprint speed";
                case PetAbilityType.Protection:
                case PetAbilityType.Recovery: return "+5% natural regeneration";
                default: return "Equip a pet to use UPRA";
            }
        }
    }

    /// <summary>Local, saved food inventory. Stock follows the same local-profile trust model as coins.</summary>
    public sealed class PetFoodManager
    {
        readonly GameExperienceContext m_Ctx;
        public PetFoodManager(GameExperienceContext ctx) { m_Ctx = ctx; }
        public int Stock => Mathf.Clamp(m_Ctx.Profile.upraFood, 0, PetFoodRules.MaxStock);

        public void EnsureStarterFood()
        {
            if (m_Ctx.Profile.upraIntroduced) return;
            m_Ctx.Profile.upraIntroduced = true;
            Add(PetFoodRules.StarterFood);
        }

        public int Add(int amount)
        {
            int before = Stock;
            m_Ctx.Profile.upraFood = before + Mathf.Clamp(amount, 0, PetFoodRules.MaxStock - before);
            return Stock - before;
        }

        public bool Consume()
        {
            if (Stock <= 0) return false;
            m_Ctx.Profile.upraFood = Stock - 1;
            m_Ctx.Save();
            return true;
        }

        public bool BuyPack()
        {
            if (Stock > PetFoodRules.MaxStock - PetFoodRules.PackSize || !m_Ctx.Inventory.TrySpend(PetFoodRules.PackCost)) return false;
            Add(PetFoodRules.PackSize);
            m_Ctx.Log("UPRA", "Bought 3 UPRA for 15 coins");
            m_Ctx.Save();
            return true;
        }
    }
}
