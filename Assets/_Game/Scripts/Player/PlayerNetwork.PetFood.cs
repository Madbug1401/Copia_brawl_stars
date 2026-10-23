using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    public partial class PlayerNetwork
    {
        public readonly NetworkVariable<PetFeedingState> PetFeeding = new NetworkVariable<PetFeedingState>();
        string m_FoodMatchId;
        bool m_FeedPending;
        float m_FeedRequestedAt;
        int m_FeedRequestId, m_LastFeedReply;
        public double PetFoodTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool UpraActive => IsSpawned && PetFeeding.Value.Remaining(PetFoodTime) > 0 && Health != null && !Health.IsDead;
        public float UpraRemaining => UpraActive ? PetFeeding.Value.Remaining(PetFoodTime) : 0;
        public float UpraCooldown => PetFeeding.Value.Cooldown(PetFoodTime);

        /// <summary>Segundos desde que começou a animação de alimentar (todas as máquinas), ou -1 fora dela.</summary>
        public float FeedAnimTime
        {
            get
            {
                if (!IsSpawned || Health == null || Health.IsDead) return -1f;
                var st = PetFeeding.Value;
                if (st.endsAt <= 0) return -1f;
                float t = (float)(PetFoodTime - (st.endsAt - PetFoodRules.Duration));
                return t >= -0.05f && t < PetFoodRules.FeedAnimDuration ? Mathf.Max(0f, t) : -1f;
            }
        }
        public bool IsFeedingAnim => FeedAnimTime >= 0f;
        /// <summary>À espera da resposta do host ao pedido de alimentar (o jogador já fica parado).</summary>
        public bool FeedPending => m_FeedPending;

        public string UpraStatus
        {
            get
            {
                if (IsFeedingAnim || m_FeedPending) return "FEEDING WITH UPRA...";
                if (Effective.Value.competitive) return "UPRA · CASUAL ONLY";
                if (UpraActive) return "UPRA · " + Mathf.CeilToInt(UpraRemaining) + "s · " +
                    PetFoodRules.BoostText((PetAbilityType)PetFeeding.Value.ability);
                if (PetFeeding.Value.uses >= PetFoodRules.MaxUsesPerMatch) return "UPRA · MATCH LIMIT";
                if (UpraCooldown > 0) return "UPRA · READY IN " + Mathf.CeilToInt(UpraCooldown) + "s";
                if (GameExperience.Current.PetFood.Stock <= 0) return "UPRA 0 · EARN FOOD IN MATCHES";
                return "[" + GameInputSettings.BindingLabel(GameAction.FeedPet) + "] UPRA " + GameExperience.Current.PetFood.Stock +
                    " · FEED PET · " + (PetFoodRules.MaxUsesPerMatch - PetFeeding.Value.uses) + " LEFT";
            }
        }

        void OnPetFeedingChanged(PetFeedingState previous, PetFeedingState current)
        {
            if (current.uses > previous.uses && current.Remaining(PetFoodTime) > 0) Visuals?.OnPetFed();
        }

        void TickPetFood()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                var match = MatchManager.Instance;
                string id = match != null && match.IsSpawned ? match.MatchId.Value.ToString() : "";
                if (id != m_FoodMatchId)
                {
                    m_FoodMatchId = id;
                    PetFeeding.Value = default;
                    ServerRefreshEffectiveStats();
                }
                var state = PetFeeding.Value;
                if (state.endsAt > 0 && (state.Remaining(PetFoodTime) <= 0 || Health == null || Health.IsDead ||
                    MatchManager.IsMatchOver || state.petHash != Loadout.Value.petHash || Effective.Value.competitive))
                {
                    state.endsAt = 0;
                    PetFeeding.Value = state;
                    ServerRefreshEffectiveStats();
                }
            }
            if (!IsLocalHuman) return;
            if (m_FeedPending && Time.unscaledTime - m_FeedRequestedAt > 5f) m_FeedPending = false;
            if (GameUI.GameplayInputAllowed && GameInputSettings.WasPressedThisFrame(GameAction.FeedPet)) RequestPetFeeding();
        }

        public bool RequestPetFeeding()
        {
            if (!IsSpawned || !IsLocalHuman || m_FeedPending || Health == null || Health.IsDead ||
                Effective.Value.competitive || Effective.Value.PetAbility == PetAbilityType.None ||
                !MatchManager.IsCombatActive || MatchManager.IsMatchOver ||
                UpraCooldown > 0 || PetFeeding.Value.uses >= PetFoodRules.MaxUsesPerMatch || GameExperience.Current.PetFood.Stock <= 0) return false;
            m_FeedPending = true;
            m_FeedRequestedAt = Time.unscaledTime;
            FeedPetRpc(++m_FeedRequestId, GameExperience.Current.PetFood.Stock);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void FeedPetRpc(int requestId, int declaredStock)
        {
            HandlePetFeedingRequest(requestId, declaredStock);
        }

        void HandlePetFeedingRequest(int requestId, int declaredStock)
        {
            var match = MatchManager.Instance;
            var pet = GameExperience.Current.PetCatalog.FindByHash(Loadout.Value.petHash);
            var state = PetFeeding.Value;
            bool accepted = pet != null && pet.ability == Effective.Value.PetAbility &&
                PetFoodRules.TryStart(ref state, PetFoodTime, Mathf.Clamp(declaredStock, 0, PetFoodRules.MaxUsesPerMatch),
                    pet.StableHash, pet.ability, !IsBot && Health != null && !Health.IsDead,
                    match != null && match.IsSpawned && MatchManager.IsCombatActive && !MatchManager.IsMatchOver,
                    match != null && match.Competitive.Value);
            if (accepted)
            {
                PetFeeding.Value = state;
                ServerRefreshEffectiveStats();
            }
            FeedPetResultRpc(requestId, accepted);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void FeedPetResultRpc(int requestId, bool accepted)
        {
            if (!IsLocalHuman || requestId <= m_LastFeedReply) return;
            m_LastFeedReply = requestId;
            m_FeedPending = false;
            if (!accepted) return;
            GameExperience.Current.PetFood.Consume();
            Sfx.Play2D(Sound.PetHappy, 0.55f);
        }
    }
}
