using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Alimentar o pet com UPRA ([T]): pedido do dono → host valida e escreve <see cref="PetFeeding"/> → todas as máquinas
    /// tocam a animação (2 s, jogador parado) → o host aplica o bónus da espécie do pet durante 12 s.
    /// Quando não dá para alimentar, o jogador recebe sempre uma mensagem com o motivo.
    /// </summary>
    public partial class PlayerNetwork
    {
        public readonly NetworkVariable<PetFeedingState> PetFeeding = new NetworkVariable<PetFeedingState>();
        // Comparado por valor a cada frame: FixedString evita converter o MatchId para string (alocação) em cada tick.
        FixedString64Bytes m_FoodMatchId;
        bool m_FoodMatchKnown;
        bool m_FeedPending, m_ServerBoostStarted;
        float m_FeedRequestedAt;
        int m_FeedRequestId, m_LastFeedReply;
        public double PetFoodTime => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        /// <summary>Bónus UPRA ativo (depois da animação de comer).</summary>
        public bool UpraActive => IsSpawned && Health != null && !Health.IsDead && PetFoodRules.BoostActive(PetFeeding.Value, PetFoodTime);
        public float UpraRemaining => UpraActive ? PetFeeding.Value.Remaining(PetFoodTime) : 0;
        public float UpraCooldown => PetFeeding.Value.Cooldown(PetFoodTime);
        public PetSpecies UpraSpecies => PetFeeding.Value.Species;

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

        PetDefinition EquippedPetDef => GameExperience.Current != null ? GameExperience.Current.PetCatalog.FindByHash(Loadout.Value.petHash) : null;

        public string UpraStatus
        {
            get
            {
                var pet = EquippedPetDef;
                if (pet == null) return "NO PET · EQUIP ONE IN PETS";
                if (IsFeedingAnim || m_FeedPending) return "FEEDING WITH UPRA...";
                if (UpraActive) return "UPRA " + PetFoodRules.BoostLabel(UpraSpecies) + " · " + Mathf.CeilToInt(UpraRemaining) + "s";
                if (PetFeeding.Value.uses >= PetFoodRules.MaxUsesPerMatch) return "UPRA · MATCH LIMIT";
                if (UpraCooldown > 0) return "UPRA · READY IN " + Mathf.CeilToInt(UpraCooldown) + "s";
                if (GameExperience.Current.PetFood.Stock <= 0) return "UPRA 0 · BUY IN PETS";
                return "[" + GameInputSettings.BindingLabel(GameAction.FeedPet) + "] FEED · " + PetFoodRules.BoostLabel(pet.species) +
                    " · UPRA " + GameExperience.Current.PetFood.Stock;
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
                var id = match != null && match.IsSpawned ? match.MatchId.Value : default;
                if (!m_FoodMatchKnown || id != m_FoodMatchId)
                {
                    m_FoodMatchKnown = true;
                    m_FoodMatchId = id;
                    PetFeeding.Value = default;
                    m_ServerBoostStarted = false;
                    ServerRefreshEffectiveStats();
                }
                var state = PetFeeding.Value;
                if (state.endsAt > 0 && (state.Remaining(PetFoodTime) <= 0 || Health == null || Health.IsDead ||
                    MatchManager.IsMatchOver || state.petHash != Loadout.Value.petHash))
                {
                    state.endsAt = 0;
                    PetFeeding.Value = state;
                    m_ServerBoostStarted = false;
                    ServerRefreshEffectiveStats();
                }
                else if (!m_ServerBoostStarted && PetFoodRules.BoostActive(state, PetFoodTime))
                    ServerStartPetBoost(state.Species);
            }
            if (!IsLocalHuman) return;
            if (m_FeedPending && Time.unscaledTime - m_FeedRequestedAt > 5f) m_FeedPending = false;
            if (GameUI.GameplayInputAllowed && GameInputSettings.WasPressedThisFrame(GameAction.FeedPet)) RequestPetFeeding();
        }

        /// <summary>Host: fim da animação → começa o bónus (modificadores + efeitos instantâneos da espécie).</summary>
        void ServerStartPetBoost(PetSpecies species)
        {
            m_ServerBoostStarted = true;
            int previousMax = EffectiveMaxHealth;
            ServerRefreshEffectiveStats();
            if (Health == null || Health.IsDead) return;
            int max = EffectiveMaxHealth;
            switch (species)
            {
                case PetSpecies.Turtle: Health.ServerHeal(Mathf.Max(0, max - previousMax)); break;
                case PetSpecies.Dog: Health.ServerHeal(Mathf.RoundToInt(max * PetFoodRules.HealFraction)); break;
                case PetSpecies.Pig: Health.ServerGrantShield(Mathf.RoundToInt(max * PetFoodRules.ShieldFraction), PetFoodRules.BoostDuration); break;
            }
        }

        /// <summary>Dono: pede para alimentar o pet. Se não puder, mostra o motivo no ecrã.</summary>
        public bool RequestPetFeeding()
        {
            if (!IsSpawned || !IsLocalHuman || m_FeedPending || Health == null || Health.IsDead) return false;
            string why = null;
            if (EquippedPetDef == null) why = "NO PET EQUIPPED · CHOOSE ONE IN PETS";
            else if (MatchManager.IsMatchOver || !MatchManager.IsCombatActive) why = "UPRA · WAIT FOR THE MATCH TO START";
            else if (IsFeedingAnim || UpraActive) why = "YOUR PET IS ALREADY BOOSTED";
            else if (PetFeeding.Value.uses >= PetFoodRules.MaxUsesPerMatch) why = "UPRA · 3 MEALS PER MATCH USED";
            else if (UpraCooldown > 0) why = "UPRA READY IN " + Mathf.CeilToInt(UpraCooldown) + "s";
            else if (GameExperience.Current.PetFood.Stock <= 0) why = "NO UPRA LEFT · BUY SACKS IN PETS";
            if (why != null)
            {
                EmoteHintText(why);
                Sfx.Play2D(Sound.PetFoodDenied, 0.6f);
                return false;
            }
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
            var pet = EquippedPetDef;
            var state = PetFeeding.Value;
            bool accepted = pet != null &&
                PetFoodRules.TryStart(ref state, PetFoodTime, Mathf.Clamp(declaredStock, 0, PetFoodRules.MaxUsesPerMatch),
                    pet.StableHash, pet.species, !IsBot && Health != null && !Health.IsDead,
                    match != null && match.IsSpawned && MatchManager.IsCombatActive && !MatchManager.IsMatchOver);
            if (accepted)
            {
                m_ServerBoostStarted = false;
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
            if (!accepted)
            {
                EmoteHintText("UPRA NOT AVAILABLE RIGHT NOW");
                return;
            }
            GameExperience.Current.PetFood.Consume();
            Sfx.Play2D(Sound.PetHappy, 0.55f);
        }
    }
}
