using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Identidade e pontuação do jogador na rede (humano ou bot). Ponto central para aceder aos outros componentes.
    /// Os jogadores são identificados pelo NetworkObjectId (os bots pertencem ao host, por isso o OwnerClientId não é único).
    /// </summary>
    public class PlayerNetwork : NetworkBehaviour
    {
        public static readonly List<PlayerNetwork> All = new List<PlayerNetwork>();
        public static PlayerNetwork Local { get; private set; }

        // Suporte a "Enter Play Mode" sem domain reload
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            Local = null;
        }

        public readonly NetworkVariable<FixedString64Bytes> PlayerName =
            new NetworkVariable<FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<int> HeroId =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<bool> IsBotVar = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Deaths = new NetworkVariable<int>(0);

        /// <summary>0 = equipa azul, 1 = equipa vermelha, -1 = ainda não atribuída.</summary>
        public readonly NetworkVariable<int> TeamId = new NetworkVariable<int>(-1);

        /// <summary>Skin, pet e melhorias do jogador (enviados pelo dono, validados e escritos pelo host).</summary>
        public readonly NetworkVariable<PlayerLoadout> Loadout = new NetworkVariable<PlayerLoadout>(PlayerLoadout.Empty);

        public PlayerMotor Motor { get; private set; }
        public PlayerHealth Health { get; private set; }
        public PlayerCombat Combat { get; private set; }
        public PlayerVisuals Visuals { get; private set; }
        public BotBrain Bot { get; private set; }
        public StatusEffects Status { get; private set; }

        public ulong Id => NetworkObjectId;
        public bool IsBot => IsBotVar.Value;
        /// <summary>Este é o jogador controlado por teclado/rato nesta máquina.</summary>
        public bool IsLocalHuman => IsOwner && !IsBot;
        /// <summary>Esta máquina simula o movimento/combate deste jogador (o dono, humano ou bot no host).</summary>
        public bool IsSimulatedHere => IsOwner;

        /// <summary>Definição do Brawler atual (dados). HeroId é o índice na BrawlerRegistry.</summary>
        public BrawlerDefinition Brawler => BrawlerRegistry.Get(HeroId.Value);

        public string DisplayName
        {
            get
            {
                var n = PlayerName.Value.ToString();
                return string.IsNullOrEmpty(n) ? "Jogador " + Id : n;
            }
        }

        public Color PlayerColor => Color.HSVToRGB((Id * 0.29f + 0.05f) % 1f, 0.6f, 0.95f);

        // Configuração pendente de bot (definida no host antes do Spawn)
        bool m_PendingBot;
        string m_PendingName;
        int m_PendingHero;
        BotDifficulty m_PendingDifficulty;

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Health = GetComponent<PlayerHealth>();
            Combat = GetComponent<PlayerCombat>();
            Visuals = GetComponent<PlayerVisuals>();
            Status = GetComponent<StatusEffects>();
        }

        /// <summary>Host: marcar este Player como bot antes de chamar Spawn().</summary>
        public void ConfigureAsBot(string botName, int heroId, BotDifficulty difficulty)
        {
            m_PendingBot = true;
            m_PendingName = botName;
            m_PendingHero = heroId;
            m_PendingDifficulty = difficulty;
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);
            if (IsServer && MatchManager.Instance != null)
                MatchManager.Instance.ServerAssignMissingTeams();

            if (IsServer && m_PendingBot)
            {
                IsBotVar.Value = true;
                PlayerName.Value = new FixedString64Bytes(GameSession.Sanitize(m_PendingName));
                HeroId.Value = BrawlerRegistry.Sanitize(m_PendingHero);
                Loadout.Value = BotLoadout(HeroId.Value);
                Bot = gameObject.AddComponent<BotBrain>();
                Bot.Init(this, m_PendingDifficulty);
            }
            else if (IsOwner)
            {
                Local = this;
                PlayerName.Value = new FixedString64Bytes(GameSession.Sanitize(GameSession.LocalName));
                HeroId.Value = BrawlerRegistry.Sanitize(GameSession.LocalHeroId);
                SubmitLocalLoadout();
            }

            HeroId.OnValueChanged += OnHeroIdChanged;
            if (IsServer) MatchStatsTracker.ServerOnJoin(this);

            gameObject.name = (IsBot || m_PendingBot ? "Bot_" : "Player_") + Id;
        }

        public override void OnNetworkDespawn()
        {
            HeroId.OnValueChanged -= OnHeroIdChanged;
            if (IsServer) MatchStatsTracker.ServerOnLeave(this);
            All.Remove(this);
            if (Local == this) Local = null;
        }

        /// <summary>Comandos deste frame (só faz sentido onde o jogador é simulado).</summary>
        public PlayerCommands ReadCommands()
        {
            if (Bot != null) return Bot.Commands;
            return PlayerCommands.FromHuman(GameUI.GameplayInputAllowed);
        }

        /// <summary>Dono: trocar de personagem (aplica-se já, com vida e munição cheias).</summary>
        public void ChangeHero(int heroId)
        {
            if (!IsOwner) return;
            HeroId.Value = BrawlerRegistry.Sanitize(heroId);
        }

        void OnHeroIdChanged(int previous, int current)
        {
            // O loadout (skin/melhorias) é por Brawler: o dono envia o do novo Brawler
            if (IsLocalHuman) SubmitLocalLoadout();
            else if (IsServer && IsBot) Loadout.Value = BotLoadout(current);
        }

        // ------------------------------------------------------------------ loadout (skin, pet, melhorias)

        /// <summary>Dono humano: envia ao host o loadout gravado para o Brawler atual.</summary>
        public void SubmitLocalLoadout()
        {
            if (!IsLocalHuman || !IsSpawned) return;
            SubmitLoadoutRpc(GameExperience.BuildLoadout(HeroId.Value));
        }

        [Rpc(SendTo.Server)]
        void SubmitLoadoutRpc(PlayerLoadout requested)
        {
            // Nunca confiar no cliente: tudo é limitado aos dados e às regras do host
            Loadout.Value = LoadoutValidator.Validate(requested, GameExperienceDatabase.Catalog);
        }

        /// <summary>Bots: pet e skin aleatórios (só aspeto; sem melhorias, para não desequilibrar).</summary>
        static PlayerLoadout BotLoadout(int heroIndex)
        {
            var catalog = GameExperienceDatabase.Catalog;
            var l = PlayerLoadout.Empty;
            l.BrawlerIndex = heroIndex;
            if (catalog.pets.Count > 0 && Random.value < 0.6f)
            {
                l.PetIndex = (short)Random.Range(0, catalog.pets.Count);
                l.PetLevel = (byte)Random.Range(1, 11);
            }
            var skins = catalog.SkinsFor(SkinTarget.Brawler, catalog.BrawlerIdAt(heroIndex));
            if (skins.Count > 0 && Random.value < 0.5f)
                l.SkinIndex = (short)catalog.SkinIndex(skins[Random.Range(0, skins.Count)].skinId);
            return LoadoutValidator.Validate(l, catalog);
        }

        // ------------------------------------------------------------------ recompensas

        /// <summary>Host: envia a recompensa da partida só ao dono deste jogador.</summary>
        public void ServerSendMatchReward(MatchRewardMessage msg)
        {
            if (!IsServer || IsBot) return;
            MatchRewardRpc(msg);
        }

        [Rpc(SendTo.Owner)]
        void MatchRewardRpc(MatchRewardMessage msg)
        {
            if (!IsLocalHuman) return;
            GameExperience.OnMatchReward(msg);
        }

        public static PlayerNetwork Find(ulong id)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i] != null && All[i].NetworkObjectId == id)
                    return All[i];
            return null;
        }
    }
}
