using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Adiciona/remove bots (só no host). Os bots usam o mesmo prefab Player que os humanos.</summary>
    public static class BotManager
    {
        public const int MaxTotalPlayers = 8;

        public static BotDifficulty Difficulty = BotDifficulty.Normal;

        static int s_Counter;

        public static int BotCount
        {
            get
            {
                int n = 0;
                foreach (var p in PlayerNetwork.All)
                    if (p != null && p.IsSpawned && p.IsBot) n++;
                return n;
            }
        }

        public static bool CanAdd
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsServer && PlayerNetwork.All.Count < MaxTotalPlayers;
            }
        }

        /// <summary>Host: adiciona um bot. heroId &lt; 0 = personagem aleatório.</summary>
        public static bool AddBot(int heroId = -1)
        {
            var nm = NetworkManager.Singleton;
            if (!CanAdd) return false;

            var prefab = nm.NetworkConfig.PlayerPrefab;
            if (prefab == null) return false;

            int hero = heroId >= 0 ? BrawlerRegistry.Sanitize(heroId) : BrawlerRegistry.RandomAvailableIndex();
            s_Counter++;
            string botName = "Bot " + BrawlerRegistry.Get(hero).displayName + " " + s_Counter;

            var go = Object.Instantiate(prefab, new Vector3(0f, 4f, 0f), Quaternion.identity);
            go.GetComponent<PlayerNetwork>().ConfigureAsBot(botName, hero, Difficulty);
            go.GetComponent<NetworkObject>().Spawn(true);
            return true;
        }

        /// <summary>Host: remove o bot mais recente.</summary>
        public static bool RemoveBot()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return false;

            PlayerNetwork last = null;
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.IsSpawned && p.IsBot && (last == null || p.Id > last.Id))
                    last = p;
            if (last == null) return false;

            last.NetworkObject.Despawn(true);
            return true;
        }

        public static void RemoveAll()
        {
            for (int i = 0; i < MaxTotalPlayers && RemoveBot(); i++) { }
        }

        /// <summary>Aplica a dificuldade atual a todos os bots existentes.</summary>
        public static void ApplyDifficulty()
        {
            foreach (var p in PlayerNetwork.All)
                if (p != null && p.Bot != null) p.Bot.SetDifficulty(Difficulty);
        }

        public static string DifficultyLabel(BotDifficulty d)
        {
            switch (d)
            {
                case BotDifficulty.Facil: return "Fácil";
                case BotDifficulty.Dificil: return "Difícil";
                default: return "Normal";
            }
        }
    }
}
