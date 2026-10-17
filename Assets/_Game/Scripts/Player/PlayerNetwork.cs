using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Identidade e pontuação do jogador na rede (humano ou bot). Ponto central para aceder aos outros componentes.
    /// Os jogadores são identificados pelo NetworkObjectId (os bots pertencem ao host, por isso o OwnerClientId não é único).
    ///
    /// Progressão em jogo: o dono declara o seu loadout (skin, pet, níveis, melhorias); o host calcula os modificadores
    /// efetivos com limites e normalização (StatResolver) e sincroniza-os em 'Effective'. Os dados base não mudam.
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

        /// <summary>Skin, pet e níveis declarados pelo dono (o host limita tudo antes de afetar o jogo).</summary>
        public readonly NetworkVariable<PlayerLoadout> Loadout =
            new NetworkVariable<PlayerLoadout>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Modificadores efetivos calculados no host (vida, dano, regeneração, velocidade, recarga, especial, pet).</summary>
        public readonly NetworkVariable<EffectiveModifiers> Effective = new NetworkVariable<EffectiveModifiers>(EffectiveModifiers.Neutral);

        public readonly NetworkVariable<bool> IsBotVar = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<int> Kills = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Deaths = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> Assists = new NetworkVariable<int>(0);

        /// <summary>0 = equipa azul, 1 = equipa vermelha, -1 = ainda não atribuída.</summary>
        public readonly NetworkVariable<int> TeamId = new NetworkVariable<int>(-1);

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

        /// <summary>Vida máxima efetiva = base do Brawler × modificador de progressão (calculado no host).</summary>
        public int EffectiveMaxHealth => StatResolver.MaxHealth(Brawler, Effective.Value);

        public float DamageMultiplier => Safe(Effective.Value.damageMultiplier);
        public float SpeedMultiplier => Safe(Effective.Value.speedMultiplier);
        public float SprintMultiplier => Safe(Effective.Value.sprintMultiplier);
        public float ReloadTimeMultiplier => Safe(Effective.Value.reloadTimeMultiplier);
        public float SuperChargeMultiplier => Safe(Effective.Value.superChargeMultiplier);

        static float Safe(float v) => v > 0.01f && !float.IsNaN(v) ? v : 1f;

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
                Loadout.Value = BotLoadout();
                Bot = gameObject.AddComponent<BotBrain>();
                Bot.Init(this, m_PendingDifficulty);
            }
            else if (IsOwner)
            {
                Local = this;
                PlayerName.Value = new FixedString64Bytes(GameSession.Sanitize(GameSession.LocalName));
                HeroId.Value = BrawlerRegistry.Sanitize(GameSession.LocalHeroId);
                Loadout.Value = LocalLoadout();
                GameExperience.Current.ProfileChanged += OnLocalProfileChanged;
            }

            HeroId.OnValueChanged += OnHeroChangedLoadout;
            Loadout.OnValueChanged += OnLoadoutChanged;
            if (IsServer)
            {
                ServerRefreshEffectiveStats();
                MatchStatsTracker.ServerRegister(this, NetworkManager.ServerTime.Time);
            }

            gameObject.name = (IsBot || m_PendingBot ? "Bot_" : "Player_") + Id;
        }

        public override void OnNetworkDespawn()
        {
            HeroId.OnValueChanged -= OnHeroChangedLoadout;
            Loadout.OnValueChanged -= OnLoadoutChanged;
            if (IsOwner && !IsBot && GameExperience.Current != null) GameExperience.Current.ProfileChanged -= OnLocalProfileChanged;
            if (IsServer) MatchStatsTracker.ServerForget(this);
            All.Remove(this);
            if (Local == this) Local = null;
        }

        /// <summary>Comandos por script (testes automáticos e automação de rede): se definido, substitui teclado/bot.</summary>
        public System.Func<PlayerCommands> ScriptedCommands { get; set; }

        /// <summary>Comandos deste frame (só faz sentido onde o jogador é simulado).</summary>
        public PlayerCommands ReadCommands()
        {
            if (ScriptedCommands != null) return ScriptedCommands();
            if (Bot != null) return Bot.Commands;
            return PlayerCommands.FromHuman(GameUI.GameplayInputAllowed);
        }

        /// <summary>Dono: trocar de personagem (aplica-se já, com vida e munição cheias).</summary>
        public void ChangeHero(int heroId)
        {
            if (!IsOwner) return;
            HeroId.Value = BrawlerRegistry.Sanitize(heroId);
        }

        // ================================================================== loadout / atributos efetivos

        PlayerLoadout LocalLoadout()
        {
            var b = Brawler;
            return b != null ? GameExperience.BuildLoadout(b.brawlerId) : default;
        }

        /// <summary>Bots: pet aleatório de nível baixo (só pela variedade visual), sem skin nem melhorias.</summary>
        static PlayerLoadout BotLoadout()
        {
            var l = new PlayerLoadout { characterLevel = 1 };
            var pets = GameExperience.Current.PetCatalog.pets;
            if (pets.Count > 0 && Random.value < 0.75f)
            {
                var pet = pets[Random.Range(0, pets.Count)];
                if (pet != null)
                {
                    l.petHash = pet.StableHash;
                    l.petLevel = (byte)Random.Range(1, 4);
                }
            }
            return l;
        }

        void OnHeroChangedLoadout(int previous, int current)
        {
            // A skin e as melhorias são por personagem
            if (IsOwner && !IsBot) Loadout.Value = LocalLoadout();
        }

        void OnLocalProfileChanged()
        {
            if (!IsSpawned || !IsOwner || IsBot) return;
            var l = LocalLoadout();
            if (!l.Equals(Loadout.Value)) Loadout.Value = l;
        }

        void OnLoadoutChanged(PlayerLoadout previous, PlayerLoadout current)
        {
            Visuals?.OnLoadoutChanged();
            // Atributos só mudam fora de combate (acabou de nascer ou está morto); senão ficam para o próximo respawn
            if (IsServer && (Health == null || Health.IsDead || Health.IsProtected)) ServerRefreshEffectiveStats();
        }

        /// <summary>Host: recalcula os modificadores efetivos a partir do loadout declarado (limitado à configuração).</summary>
        public void ServerRefreshEffectiveStats()
        {
            if (!IsServer) return;
            bool competitive = MatchManager.Instance != null && MatchManager.Instance.IsSpawned && MatchManager.Instance.Competitive.Value;
            var gx = GameExperience.Current;
            int previousMax = EffectiveMaxHealth;
            var mods = StatResolver.Resolve(gx.Config, gx.PetCatalog, Loadout.Value, competitive);
            if (!mods.Equals(Effective.Value)) Effective.Value = mods;
            if (Health != null && Health.IsSpawned && EffectiveMaxHealth != previousMax) Health.ServerOnMaxHealthChanged(previousMax);
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
