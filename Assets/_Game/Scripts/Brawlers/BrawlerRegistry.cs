using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Lista de Brawlers jogáveis. Fica em Resources/BrawlerRegistry.asset.
    /// A rede envia apenas o índice nesta lista (PlayerNetwork.HeroId).
    /// </summary>
    [CreateAssetMenu(menuName = "FPS-LAN/Brawler Registry")]
    public class BrawlerRegistry : ScriptableObject
    {
        public const string ResourcePath = "BrawlerRegistry";

        public List<BrawlerDefinition> brawlers = new List<BrawlerDefinition>();

        static BrawlerRegistry s_Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Instance = null;

        public static BrawlerRegistry Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = Resources.Load<BrawlerRegistry>(ResourcePath);
                    if (s_Instance == null)
                        Debug.LogError("[FPS-LAN] BrawlerRegistry não encontrado em Resources. Corre o menu FPS-LAN → Gerar dados dos Brawlers.");
                    else
                        s_Instance.Index();
                }
                return s_Instance;
            }
        }

        public static int Count => Instance != null ? Instance.brawlers.Count : 0;

        public static IReadOnlyList<BrawlerDefinition> All =>
            Instance != null ? (IReadOnlyList<BrawlerDefinition>)Instance.brawlers : System.Array.Empty<BrawlerDefinition>();

        void Index()
        {
            for (int i = 0; i < brawlers.Count; i++)
                if (brawlers[i] != null) brawlers[i].runtimeIndex = i;
        }

        /// <summary>Brawler por índice (inválido/indisponível → primeiro disponível).</summary>
        public static BrawlerDefinition Get(int index)
        {
            var reg = Instance;
            if (reg == null || reg.brawlers.Count == 0) return null;
            if (index >= 0 && index < reg.brawlers.Count && reg.brawlers[index] != null && reg.brawlers[index].available)
                return reg.brawlers[index];
            foreach (var b in reg.brawlers)
                if (b != null && b.available) return b;
            return reg.brawlers[0];
        }

        public static int Sanitize(int index) => Get(index) != null ? Get(index).runtimeIndex : 0;

        public static int RandomAvailableIndex()
        {
            var reg = Instance;
            if (reg == null) return 0;
            for (int tries = 0; tries < 20; tries++)
            {
                int i = Random.Range(0, reg.brawlers.Count);
                if (reg.brawlers[i] != null && reg.brawlers[i].available) return i;
            }
            return 0;
        }
    }
}
