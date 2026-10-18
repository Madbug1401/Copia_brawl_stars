using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    public enum CoopMissionStage : byte { Infiltration, Resistance, Puzzle, Discovery }

    /// <summary>The Pelourinho Heist: estado persistente no host; legendas e objetos locais em cada cliente.</summary>
    public partial class MatchManager
    {
        public const string CoopMissionTitle = "THE PELOURINHO HEIST";

        [Header("The Pelourinho Heist")]
        public Vector3 coopPlateA = new Vector3(-6.2f, 0.1f, 0f);
        public Vector3 coopPlateB = new Vector3(0f, 0.1f, -7f);
        public Vector3 coopGemPosition = new Vector3(0f, 0.7f, -4.3f);
        public float coopPlateRadius = 1.5f;
        public float coopPuzzleSeconds = 2.5f;
        public float coopGemPickupRadius = 1.8f;
        public float coopGemPartnerRadius = 4f;

        public readonly NetworkVariable<CoopMissionStage> CoopStage = new NetworkVariable<CoopMissionStage>();
        public readonly NetworkVariable<float> CoopPuzzleProgress = new NetworkVariable<float>();
        public readonly NetworkVariable<byte> CoopPlates = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> CoopStoryBeat = new NetworkVariable<int>();
        public readonly NetworkVariable<double> CoopStoryUntil = new NetworkVariable<double>();

        Transform m_CoopStoryVisuals, m_CoopGem;
        Renderer m_CoopPlateARenderer, m_CoopPlateBRenderer, m_CoopGemRenderer;
        double m_CoopNextDangerLine;

        public string CoopStageTitle => CoopStage.Value == CoopMissionStage.Infiltration ? "THE INFILTRATION" :
            CoopStage.Value == CoopMissionStage.Resistance ? "THE RESISTANCE" :
            CoopStage.Value == CoopMissionStage.Puzzle ? "THE PUZZLE" : "THE DISCOVERY";

        public string CoopObjective => CoopStage.Value <= CoopMissionStage.Resistance
            ? "Abram caminho até à Gem. Protejam-se e eliminem os guardas."
            : CoopStage.Value == CoopMissionStage.Puzzle
                ? "Uma pessoa em cada placa. Mantenham as duas ativas para libertar a Gem."
                : "Recolham a Gem dourada com o parceiro por perto.";

        public static bool IsCoopStoryHero(BrawlerDefinition b) => b != null &&
            (b.brawlerId == "sniper" || b.brawlerId == "shotgunner");

        public static int CoopHeroIndex(string id)
        {
            foreach (var b in BrawlerRegistry.All)
                if (b != null && b.available && b.brawlerId == id) return b.runtimeIndex;
            return BrawlerRegistry.Sanitize(0);
        }

        // O primeiro humano mantém a escolha; o segundo recebe a personagem complementar.
        void ServerCoopAssignStoryRoles()
        {
            PlayerNetwork first = null, second = null;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.IsBot) continue;
                if (first == null || p.Id < first.Id) { second = first; first = p; }
                else second = p;
            }
            if (first == null) return;
            int hero = IsCoopStoryHero(first.Brawler) ? first.HeroId.Value : CoopHeroIndex("sniper");
            if (first.HeroId.Value != hero) first.AssignCoopHeroRpc(hero);
            int partner = hero == CoopHeroIndex("sniper") ? CoopHeroIndex("shotgunner") : CoopHeroIndex("sniper");
            if (second != null && second.HeroId.Value != partner) second.AssignCoopHeroRpc(partner);
        }

        int CoopPartnerHero()
        {
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && !p.IsBot)
                    return p.HeroId.Value == CoopHeroIndex("shotgunner") ? CoopHeroIndex("sniper") : CoopHeroIndex("shotgunner");
            return CoopHeroIndex("shotgunner");
        }

        static bool CoopIsHelping(PlayerNetwork p) => p.IsBot ? p.Bot != null && p.Bot.Commands.interact : p.CoopInteractHeld.Value;

        void ServerCoopStoryReset()
        {
            CoopStage.Value = CoopMissionStage.Infiltration;
            CoopPuzzleProgress.Value = 0f;
            CoopPlates.Value = 0;
            m_CoopNextDangerLine = 0;
            ServerCoopSetStory(0);
        }

        void ServerCoopSetStory(int beat, float seconds = 11f)
        {
            CoopStoryBeat.Value = beat;
            CoopStoryUntil.Value = NetworkManager.ServerTime.Time + seconds;
        }

        public void ServerCoopDangerLine(PlayerNetwork p, bool revived)
        {
            if (!IsServer || !IsCoop || !IsCoopTeam(p) || Phase.Value != MatchPhase.Playing) return;
            double now = NetworkManager.ServerTime.Time;
            if (!revived && now < m_CoopNextDangerLine) return;
            bool riot = p.Brawler != null && p.Brawler.brawlerId == "shotgunner";
            ServerCoopSetStory(revived ? (riot ? 11 : 13) : (riot ? 10 : 12), 5f);
            m_CoopNextDangerLine = now + 2f;
        }

        void ServerCoopBeginPuzzle()
        {
            CoopEnemiesLeft.Value = 0;
            CoopStage.Value = CoopMissionStage.Puzzle;
            CoopPuzzleProgress.Value = 0f;
            CoopPlates.Value = 0;
            IsRoundIntermission.Value = false;
            CombatSystem.Instance?.ClearAll();
            ServerCoopSetStory(6);
        }

        bool CoopAt(PlayerNetwork p, Vector3 point, float radius) => IsCoopTeam(p) && !p.Health.IsDead &&
            Mathf.Abs(p.transform.position.y - point.y) < 2f &&
            new Vector2(p.transform.position.x - point.x, p.transform.position.z - point.z).sqrMagnitude <= radius * radius;

        void ServerCoopObjectiveUpdate()
        {
            if (CoopStage.Value == CoopMissionStage.Puzzle)
            {
                PlayerNetwork onA = null, onB = null;
                foreach (var p in PlayerNetwork.All)
                {
                    if (CoopAt(p, coopPlateA, coopPlateRadius)) onA = p;
                    if (CoopAt(p, coopPlateB, coopPlateRadius)) onB = p;
                }
                byte mask = (byte)((onA != null ? 1 : 0) | (onB != null ? 2 : 0));
                if (CoopPlates.Value != mask) CoopPlates.Value = mask;
                // A mesma pessoa nunca conta para as duas placas, mesmo com raios personalizados.
                float progress = onA != null && onB != null && onA != onB
                    ? Mathf.Min(1f, CoopPuzzleProgress.Value + Time.deltaTime / Mathf.Max(0.1f, coopPuzzleSeconds)) : 0f;
                if (!Mathf.Approximately(CoopPuzzleProgress.Value, progress)) CoopPuzzleProgress.Value = progress;
                if (progress >= 1f)
                {
                    CoopStage.Value = CoopMissionStage.Discovery;
                    ServerCoopSetStory(7);
                }
                return;
            }

            foreach (var p in PlayerNetwork.All)
            {
                if (!CoopAt(p, coopGemPosition, coopGemPickupRadius)) continue;
                foreach (var mate in PlayerNetwork.All)
                    if (mate != p && CoopAt(mate, coopGemPosition, coopGemPartnerRadius))
                    { ServerCoopEnd(true); return; }
            }
        }

        /// <summary>O bot vai à placa oposta à mais próxima do humano e aguarda. Não resolve o puzzle sozinho.</summary>
        public bool TryCoopObjectiveGoal(PlayerNetwork bot, out Vector3 goal)
        {
            goal = default;
            if (!IsCoop || !IsCoopTeam(bot) || Phase.Value != MatchPhase.Playing) return false;
            PlayerNetwork human = null;
            foreach (var p in PlayerNetwork.All)
                if (IsCoopTeam(p) && !p.IsBot && !p.Health.IsDead) { human = p; break; }
            if (human == null) return false;
            if (CoopStage.Value == CoopMissionStage.Puzzle)
            {
                float a = (human.transform.position - coopPlateA).sqrMagnitude;
                float b = (human.transform.position - coopPlateB).sqrMagnitude;
                goal = a <= b ? coopPlateB : coopPlateA;
                return true;
            }
            if (CoopStage.Value == CoopMissionStage.Discovery)
            { goal = coopGemPosition + Vector3.left * 2.4f; return true; }
            // Fora de combate, o parceiro acompanha o humano e mantém o Laço.
            goal = human.transform.position;
            return true;
        }

        public string CoopStoryText
        {
            get
            {
                switch (CoopStoryBeat.Value)
                {
                    case 0: return "RIOT: Uma Gem rara, guardas por todo o lado... o encontro perfeito.\nLINCE: Fica perto. Desta vez seguimos o plano.";
                    case 1: return "RIOT: Viste? Eu conseguia fazer isto sozinha.\nLINCE: Claro. Eu só estava a salvar o teu plano.";
                    case 2: return "LINCE: A segurança está a apertar. Usa as coberturas; eu cubro-te.\nRIOT: Tu tratas da pontaria. Eu abro caminho.";
                    case 3: return "RIOT: Alguém quer mesmo manter-nos longe daquela Gem.\nLINCE: Ou quer que cheguemos até ela. Ainda não percebi porquê.";
                    case 4: return "LINCE: Última linha de defesa. Sem aventuras a solo.\nRIOT: Juntos, então. Tenta acompanhar-me, handsome.";
                    case 6: return "LINCE: Duas placas. Uma pessoa em cada uma, ao mesmo tempo.\nRIOT: Está bem, estratega. Vamos fazer isto à tua maneira.";
                    case 7: return "RIOT: See? We're unstoppable.\nLINCE: A Gem está livre. Aproxima-te; levamo-la juntos.";
                    case 8: return "GEM RECUPERADA\nLINCE: Estas marcas... há mais Gems. Alguém queria que encontrássemos esta.\nRIOT: Então este foi só o primeiro encontro.";
                    case 9: return "RIOT: Da próxima vez, ninguém fica para trás.\nLINCE: Recomeçamos. Juntos.";
                    case 10: return "LINCE: Stay with me, babe. I've got you.";
                    case 11: return "RIOT: Took you long enough, handsome.";
                    case 12: return "RIOT: You're not leaving me here alone!";
                    case 13: return "LINCE: I knew you'd come back for me.";
                    default: return "";
                }
            }
        }

        // Objetos decorativos, sem NetworkObjects: todos os clientes reconstroem o mesmo estado sincronizado.
        void LateUpdate()
        {
            bool visible = IsCoop && Phase.Value == MatchPhase.Playing && CoopStage.Value >= CoopMissionStage.Puzzle;
            if (!visible)
            {
                if (m_CoopStoryVisuals != null) m_CoopStoryVisuals.gameObject.SetActive(false);
                return;
            }
            if (m_CoopStoryVisuals == null)
            {
                m_CoopStoryVisuals = new GameObject("PelourinhoHeist_Objetivos").transform;
                m_CoopStoryVisuals.SetParent(transform, false);
                m_CoopPlateARenderer = CoopPrimitive("Placa A", PrimitiveType.Cylinder, coopPlateA,
                    new Vector3(coopPlateRadius * 2, 0.05f, coopPlateRadius * 2));
                m_CoopPlateBRenderer = CoopPrimitive("Placa B", PrimitiveType.Cylinder, coopPlateB,
                    new Vector3(coopPlateRadius * 2, 0.05f, coopPlateRadius * 2));
                m_CoopGemRenderer = CoopPrimitive("Gem dourada", PrimitiveType.Cube, coopGemPosition,
                    new Vector3(0.6f, 0.9f, 0.6f));
                m_CoopGem = m_CoopGemRenderer.transform;
            }
            m_CoopStoryVisuals.gameObject.SetActive(true);
            bool open = CoopStage.Value == CoopMissionStage.Discovery;
            Art.SetColor(m_CoopPlateARenderer, open || (CoopPlates.Value & 1) != 0 ? Color.green : new Color(0.15f, 0.65f, 1f));
            Art.SetColor(m_CoopPlateBRenderer, open || (CoopPlates.Value & 2) != 0 ? Color.green : new Color(0.15f, 0.65f, 1f));
            Art.SetColor(m_CoopGemRenderer, open ? new Color(1f, 0.72f, 0.1f) : new Color(0.3f, 0.28f, 0.2f));
            m_CoopGem.position = coopGemPosition + Vector3.up * (0.45f + Mathf.Sin(Time.time * 2f) * 0.15f);
            m_CoopGem.rotation = Quaternion.Euler(20f, Time.time * 55f, 45f);
        }

        Renderer CoopPrimitive(string label, PrimitiveType shape, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = label;
            go.transform.SetParent(m_CoopStoryVisuals, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            col.enabled = false;
            Destroy(col);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Art.ToonOutline;
            return renderer;
        }
    }
}
