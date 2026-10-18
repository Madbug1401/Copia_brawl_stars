using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Emissor de gemas de um mapa (só dados e apresentação). Vive no pacote do mapa; o <see cref="GemGrabManager"/>
    /// liga-se a ele quando a partida é de Pique-Gemas. Fora desse modo os objetos de <see cref="gemModeOnly"/>
    /// (halo, marcas de aterragem) ficam escondidos e o mapa é um campo normal.
    /// Na arena de arranque, que não tem emissor modelado, <see cref="EnsureFallback"/> cria um no topo do totem.
    /// </summary>
    public class GemEmitterConfig : MonoBehaviour
    {
        public static GemEmitterConfig Current { get; private set; }

        public Transform spawnPoint;
        public Transform[] landingPoints = new Transform[0];
        public Transform emitterHalo;
        public Transform emitterBase;
        public Renderer[] emitterRenderers = new Renderer[0];
        public GemEmitterStyle style = GemEmitterStyle.Pelourinho;
        public Transform waterSurface;
        public float arcHeight = 2.2f;
        public Color emitterIdleEmission = new Color(0.25f, 0.12f, 0.45f);
        public Color emitterWarnEmission = new Color(1.1f, 0.7f, 1.6f);
        [Tooltip("Opcional: gema deste mapa (vazio = a do GemGrabManager).")]
        public GameObject gemPrefab;
        [Tooltip("Só aparecem no modo de gemas (halo do emissor, marcas de aterragem).")]
        public GameObject[] gemModeOnly = new GameObject[0];

        bool m_GemMode = true;

        void OnEnable()
        {
            Current = this;
            SetGemMode(MatchManager.Instance != null && MatchManager.Instance.IsSpawned
                ? MatchManager.Instance.Mode.Value == GameMode.GemGrab
                : m_GemMode);
        }

        void OnDisable()
        {
            if (Current == this) Current = null;
        }

        public void SetGemMode(bool on)
        {
            m_GemMode = on;
            if (gemModeOnly == null) return;
            foreach (var go in gemModeOnly)
                if (go != null && go.activeSelf != on) go.SetActive(on);
        }

        public void ApplyTo(GemGrabManager gm)
        {
            gm.spawnPoint = spawnPoint;
            gm.landingPoints = landingPoints ?? new Transform[0];
            gm.emitterHalo = emitterHalo;
            gm.emitterBase = emitterBase;
            gm.emitterRenderers = emitterRenderers ?? new Renderer[0];
            gm.arcHeight = arcHeight;
            gm.emitterIdleEmission = emitterIdleEmission;
            gm.emitterWarnEmission = emitterWarnEmission;
            if (gemPrefab != null) gm.gemPrefab = gemPrefab;
            var vis = gm.GetComponent<GemGrabVisuals>();
            if (vis != null)
            {
                vis.style = style;
                vis.waterSurface = waterSurface;
            }
        }

        /// <summary>
        /// Arena de arranque sem emissor modelado: gera um no ponto mais alto do centro (totem) e 8 aterragens
        /// simétricas em chão livre à volta. Calculado igual em todas as máquinas (raycasts à geometria estática).
        /// </summary>
        public static GemEmitterConfig EnsureFallback()
        {
            if (Current != null) return Current;
            if (MapLoader.CurrentKey != MapCatalog.BaseKey || MapInfo.Instance == null || !MapLoader.SceneSupportsSwap) return null;
            // Só depois de o mapa da partida ser conhecido (o cliente pode receber o gerador antes do MatchManager)
            var mm = MatchManager.Instance;
            if (mm == null || !mm.IsSpawned || mm.MapKey.Value.ToString() != MapCatalog.BaseKey) return null;

            var parentGo = GameObject.Find("Map");
            var root = new GameObject("GeradorGemas_Arena");
            if (parentGo != null) root.transform.SetParent(parentGo.transform, false);
            root.SetActive(false);
            var cfg = root.AddComponent<GemEmitterConfig>();
            int mask = Physics.DefaultRaycastLayers;

            float top = 0f;
            if (Physics.Raycast(new Vector3(0f, 30f, 0f), Vector3.down, out var hitTop, 60f, mask, QueryTriggerInteraction.Ignore))
                top = hitTop.point.y;
            var sp = new GameObject("GEM_Spawn").transform;
            sp.SetParent(root.transform, false);
            sp.position = new Vector3(0f, top + 0.5f, 0f);
            cfg.spawnPoint = sp;

            var lands = new List<Transform>();
            float[] radii = { 5.5f, 4.5f, 6.5f, 2.6f, 8f };
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + 45f * k) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                foreach (var r in radii)
                {
                    var probe = dir * r + Vector3.up * 30f;
                    if (!Physics.Raycast(probe, Vector3.down, out var hit, 60f, mask, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.75f || hit.point.y > 3f) continue;
                    var p = hit.point + Vector3.up * 0.6f;
                    if (Physics.CheckSphere(p, 0.35f, mask | (1 << 2), QueryTriggerInteraction.Ignore)) continue;
                    var t = new GameObject("GEM_Land_" + k.ToString("00")).transform;
                    t.SetParent(root.transform, false);
                    t.position = p;
                    lands.Add(t);
                    break;
                }
            }
            cfg.landingPoints = lands.ToArray();
            cfg.arcHeight = 2.5f;
            root.SetActive(true);
            return cfg;
        }
    }
}
