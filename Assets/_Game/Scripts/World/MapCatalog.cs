using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Mapas que o menu pode escolher. A chave é a mesma do carrossel do menu (ilha).
    /// "pradaria" é a arena da cena de arranque (Arena.unity); os outros são pacotes em Resources/Maps
    /// (gerados por FPS-LAN ▸ Mapas ▸ Construir …) que o <see cref="MapLoader"/> carrega por cima dela.
    /// Os modos são independentes do mapa: qualquer mapa joga-se em qualquer modo.
    /// </summary>
    public static class MapCatalog
    {
        public const string BaseKey = "pradaria";
        /// <summary>MapInfo.mapName da arena de arranque (identifica a cena onde se pode trocar de mapa).</summary>
        public const string BaseMapName = "Pradaria";
        const string KeyMenuMap = "fpslan.map";

        public struct Entry
        {
            public string key, displayName, resource;
            public Entry(string key, string displayName, string resource) { this.key = key; this.displayName = displayName; this.resource = resource; }
        }

        public static readonly Entry[] Maps =
        {
            new Entry(BaseKey, "Pradaria", null),
            new Entry("santiago", "Cidade Velha", "Maps/Map_CidadeVelha"),
            new Entry("sal", "Buracona", "Maps/Map_Buracona"),
        };

        public static bool IsPlayable(string key)
        {
            foreach (var e in Maps) if (e.key == key) return true;
            return false;
        }

        public static string ResourceFor(string key)
        {
            foreach (var e in Maps) if (e.key == key) return e.resource;
            return null;
        }

        public static string DisplayName(string key)
        {
            foreach (var e in Maps) if (e.key == key) return e.displayName;
            return key;
        }

        /// <summary>Mapa escolhido no menu deste PC (o host usa-o ao criar a partida; os clientes recebem o do host).</summary>
        public static string MenuSelectedKey
        {
            get
            {
                var k = PlayerPrefs.GetString(KeyMenuMap, BaseKey);
                return IsPlayable(k) ? k : BaseKey;
            }
        }
    }
}
