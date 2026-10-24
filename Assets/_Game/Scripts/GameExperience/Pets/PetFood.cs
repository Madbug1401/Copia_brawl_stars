using System;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Uma refeição UPRA decidida pelo host: 2 s de animação (o jogador fica parado) seguidos do bónus do pet.
    /// Morrer cancela o bónus, mas não a espera nem a quota da partida.
    /// </summary>
    public struct PetFeedingState : INetworkSerializeByMemcpy, IEquatable<PetFeedingState>
    {
        public double endsAt, readyAt;
        public int petHash;
        /// <summary>Espécie do pet + 1 (0 = nenhuma). Define o bónus.</summary>
        public byte uses, species;
        public float Remaining(double now) => (float)Math.Max(0, endsAt - now);
        public float Cooldown(double now) => (float)Math.Max(0, readyAt - now);
        public PetSpecies Species => (PetSpecies)Math.Max(0, species - 1);
        public bool HasSpecies => species > 0;
        public bool Equals(PetFeedingState other) => endsAt == other.endsAt && readyAt == other.readyAt &&
            petHash == other.petHash && uses == other.uses && species == other.species;
        public override bool Equals(object other) => other is PetFeedingState state && Equals(state);
        public override int GetHashCode() => petHash ^ uses ^ endsAt.GetHashCode();
    }

    /// <summary>
    /// Bónus UPRA por pet (espécie). Cada pet dá um tipo de vantagem diferente durante <see cref="BoostDuration"/> s,
    /// depois da animação de comer:
    /// Coelho = velocidade · Tartaruga = vida máxima · Cão = cura + regeneração · Porco = escudo ·
    /// Gato = recarga rápida · Galo = Super carrega mais depressa.
    /// Funciona em todos os modos; 60 s de espera e 3 refeições por partida mantêm o equilíbrio.
    /// </summary>
    public static class PetFoodRules
    {
        public const int StarterFood = 6, FoodPerMatch = 2, MaxStock = 999;
        public const int MaxUsesPerMatch = 3, PackSize = 3, PackCost = 15;
        /// <summary>Animação de alimentar (o jogador fica parado, o pet come da tigela UPRA).</summary>
        public const float FeedAnimDuration = 2f;
        public const float BoostDuration = 12f;
        /// <summary>Janela total = animação + bónus.</summary>
        public const float Duration = FeedAnimDuration + BoostDuration;
        public const float Cooldown = 60f;

        // Valores dos bónus (afinação num só sítio)
        public const float SpeedBoost = 0.20f, SprintBoost = 0.10f;
        public const float MaxHealthBoost = 0.25f;
        public const float HealFraction = 0.40f, RegenBoost = 3f;
        public const float ShieldFraction = 0.35f;
        public const float ReloadBoost = 0.30f;
        public const float SuperBoost = 0.50f;

        public static bool TryStart(ref PetFeedingState state, double now, int stock, int petHash,
            PetSpecies species, bool alive, bool playing)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || !alive || !playing || stock <= 0 ||
                petHash == 0 || !Enum.IsDefined(typeof(PetSpecies), species) ||
                state.uses >= MaxUsesPerMatch || state.Cooldown(now) > 0 || state.Remaining(now) > 0) return false;
            state.endsAt = now + Duration;
            state.readyAt = now + Cooldown;
            state.petHash = petHash;
            state.species = (byte)((int)species + 1);
            state.uses++;
            return true;
        }

        /// <summary>O bónus já está ativo (a animação acabou)?</summary>
        public static bool BoostActive(in PetFeedingState state, double now)
        {
            float r = state.Remaining(now);
            return r > 0 && r <= BoostDuration + 0.001f && state.HasSpecies;
        }

        /// <summary>Host: modificadores contínuos do bónus (vida máx., velocidade, recarga, Super, regeneração).</summary>
        public static void ApplyBoost(ref EffectiveModifiers mods, PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Rabbit:
                    mods.speedMultiplier *= 1f + SpeedBoost;
                    mods.sprintMultiplier *= 1f + SprintBoost;
                    break;
                case PetSpecies.Turtle:
                    mods.healthMultiplier *= 1f + MaxHealthBoost;
                    break;
                case PetSpecies.Dog:
                    mods.regenMultiplier *= RegenBoost;
                    mods.regenDelayReduction = Mathf.Max(mods.regenDelayReduction, 1.5f);
                    break;
                case PetSpecies.Cat:
                    mods.reloadTimeMultiplier *= 1f - ReloadBoost;
                    break;
                case PetSpecies.Rooster:
                    mods.superChargeMultiplier *= 1f + SuperBoost;
                    break;
                // Porco: escudo instantâneo (ver PlayerNetwork.ServerStartPetBoost)
            }
        }

        public static string BoostText(PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Rabbit: return "+20% movement speed";
                case PetSpecies.Turtle: return "+25% max health";
                case PetSpecies.Dog: return "Heals 40% + 3x regeneration";
                case PetSpecies.Pig: return "Shield: 35% of max health";
                case PetSpecies.Cat: return "Reload 30% faster";
                case PetSpecies.Rooster: return "Super charges 50% faster";
                default: return "Equip a pet to use UPRA";
            }
        }

        /// <summary>Etiqueta curta para o HUD.</summary>
        public static string BoostLabel(PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Rabbit: return "SPEED";
                case PetSpecies.Turtle: return "MAX HEALTH";
                case PetSpecies.Dog: return "HEALING";
                case PetSpecies.Pig: return "SHIELD";
                case PetSpecies.Cat: return "FAST RELOAD";
                case PetSpecies.Rooster: return "SUPER CHARGE";
                default: return "UPRA";
            }
        }

        public static Color BoostColor(PetSpecies species)
        {
            switch (species)
            {
                case PetSpecies.Rabbit: return new Color(0.35f, 0.85f, 1f);
                case PetSpecies.Turtle: return new Color(0.35f, 0.9f, 0.35f);
                case PetSpecies.Dog: return new Color(1f, 0.45f, 0.55f);
                case PetSpecies.Pig: return new Color(1f, 0.8f, 0.3f);
                case PetSpecies.Cat: return new Color(0.75f, 0.5f, 1f);
                case PetSpecies.Rooster: return new Color(1f, 0.55f, 0.15f);
                default: return Color.white;
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
