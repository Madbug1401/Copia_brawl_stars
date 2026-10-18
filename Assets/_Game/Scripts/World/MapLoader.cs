using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Troca o mapa jogado sem mudar de cena: a cena de arranque (Arena) mantém rede, UI e sistemas; quando o host
    /// escolhe outro mapa, a geometria da arena (Map, SpawnPoints, MapInfo, coberturas destrutíveis) é escondida e o
    /// pacote do mapa (Resources/Maps/Map_*) é instanciado no lugar dela, com o seu MapInfo, spawns, arbustos,
    /// luz e emissor de gemas. Corre em todas as máquinas a partir de <see cref="MatchManager.MapKey"/> (replicado),
    /// por isso quem entra a meio recebe o mapa certo. Numa cena dedicada (ex.: Buracona.unity) não faz nada.
    /// </summary>
    public static class MapLoader
    {
        public static string CurrentKey { get; private set; } = MapCatalog.BaseKey;
        public static GameObject Loaded => s_Loaded;

        static GameObject s_Loaded;
        static readonly List<GameObject> s_HiddenBase = new List<GameObject>();
        static readonly List<DestructibleCover> s_HiddenCovers = new List<DestructibleCover>();
        static MapTheme.Snapshot s_BaseTheme;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CurrentKey = MapCatalog.BaseKey;
            s_Loaded = null;
            s_HiddenBase.Clear();
            s_HiddenCovers.Clear();
            s_BaseTheme = default;
        }

        /// <summary>MapInfo da arena de arranque (null numa cena dedicada a um mapa).</summary>
        static MapInfo FindBaseInfo()
        {
            foreach (var mi in Object.FindObjectsByType<MapInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mi.mapName == MapCatalog.BaseMapName && (s_Loaded == null || !mi.transform.IsChildOf(s_Loaded.transform)))
                    return mi;
            return null;
        }

        /// <summary>Esta cena permite trocar de mapa (é a arena de arranque)?</summary>
        public static bool SceneSupportsSwap => FindBaseInfo() != null;

        public static void Apply(string key)
        {
            if (string.IsNullOrEmpty(key) || !MapCatalog.IsPlayable(key)) key = MapCatalog.BaseKey;
            var baseInfo = FindBaseInfo();
            if (baseInfo == null) return;                                   // cena dedicada: mapa fixo
            if (key == CurrentKey && (key == MapCatalog.BaseKey || s_Loaded != null)) return;

            Unload();
            if (key == MapCatalog.BaseKey) return;

            var path = MapCatalog.ResourceFor(key);
            var prefab = path != null ? Resources.Load<GameObject>(path) : null;
            if (prefab == null)
            {
                Debug.LogWarning("[Mapas] Pacote do mapa '" + key + "' não encontrado em Resources/" + path + " — fica a arena.");
                return;
            }

            HideBase(baseInfo);
            s_BaseTheme = MapTheme.Capture();
            s_Loaded = Object.Instantiate(prefab);
            s_Loaded.name = prefab.name;
            var theme = s_Loaded.GetComponent<MapTheme>();
            if (theme != null) theme.Apply();
            CurrentKey = key;
            Physics.SyncTransforms();
            Debug.Log("[Mapas] Mapa carregado: " + MapCatalog.DisplayName(key));
        }

        static void HideBase(MapInfo baseInfo)
        {
            s_HiddenBase.Clear();
            foreach (var go in baseInfo.gameObject.scene.GetRootGameObjects())
            {
                if (!go.activeSelf) continue;
                bool geometry = go.name == "Map" || go.name == "SpawnPoints" || go == baseInfo.gameObject;
                // Objetos de rede nunca se desativam (o NGO conta com eles); as coberturas só se escondem.
                if (geometry && go.GetComponentInChildren<NetworkObject>(true) == null)
                {
                    go.SetActive(false);
                    s_HiddenBase.Add(go);
                }
            }
            s_HiddenCovers.Clear();
            foreach (var c in Object.FindObjectsByType<DestructibleCover>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.gameObject.scene != baseInfo.gameObject.scene) continue;
                c.SetHiddenByMap(true);
                s_HiddenCovers.Add(c);
            }
        }

        static void Unload()
        {
            if (s_Loaded != null)
            {
                s_Loaded.SetActive(false);                                   // tira já spawns/arbustos/MapInfo das listas
                Object.Destroy(s_Loaded);
                s_Loaded = null;
                MapTheme.Restore(s_BaseTheme);
            }
            foreach (var go in s_HiddenBase) if (go != null) go.SetActive(true);
            s_HiddenBase.Clear();
            foreach (var c in s_HiddenCovers) if (c != null) c.SetHiddenByMap(false);
            s_HiddenCovers.Clear();
            CurrentKey = MapCatalog.BaseKey;
            Physics.SyncTransforms();
        }

        /// <summary>Mostra os elementos do emissor (halo, marcas de aterragem) só no modo de gemas.</summary>
        public static void RefreshGemPresentation(bool gemMode)
        {
            var cfg = GemEmitterConfig.Current;
            if (cfg != null) cfg.SetGemMode(gemMode);
        }
    }
}
