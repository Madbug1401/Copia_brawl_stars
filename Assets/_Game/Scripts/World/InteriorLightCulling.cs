using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Desempenho dos interiores: as luzes pontuais das casas só ficam ligadas perto da câmara (as N mais próximas
    /// dentro de um raio). Cada luz ligada obriga a redesenhar as malhas que toca (uma passagem por luz), por isso
    /// as casas longe deixam de custar luz — de longe só se vê o interior pelas janelas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteriorLightCulling : MonoBehaviour
    {
        [Tooltip("Distância máxima (m) entre a câmara e a luz para ela ficar ligada.")]
        public float radius = 16f;
        [Tooltip("Número máximo de luzes ligadas ao mesmo tempo.")]
        public int maxActive = 6;
        [Tooltip("Intervalo entre atualizações (s).")]
        public float interval = 0.2f;

        Light[] lights;
        float[] dist;
        int[] order;
        float next;

        void Awake()
        {
            lights = GetComponentsInChildren<Light>(true);
            dist = new float[lights.Length];
            order = new int[lights.Length];
        }

        void OnDisable()
        {
            if (lights == null) return;
            foreach (var l in lights) if (l != null) l.enabled = true;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < next || lights.Length == 0) return;
            next = Time.unscaledTime + interval;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            float r2 = radius * radius;
            for (int i = 0; i < lights.Length; i++)
            {
                order[i] = i;
                dist[i] = lights[i] != null ? (lights[i].transform.position - p).sqrMagnitude : float.MaxValue;
            }
            // ordenação por inserção (poucas luzes, sem alocações)
            for (int i = 1; i < order.Length; i++)
            {
                int k = order[i];
                float d = dist[k];
                int j = i - 1;
                while (j >= 0 && dist[order[j]] > d) { order[j + 1] = order[j]; j--; }
                order[j + 1] = k;
            }
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                if (lights[i] == null) continue;
                bool on = n < maxActive && dist[i] <= r2;
                if (lights[i].enabled != on) lights[i].enabled = on;
            }
        }
    }
}
