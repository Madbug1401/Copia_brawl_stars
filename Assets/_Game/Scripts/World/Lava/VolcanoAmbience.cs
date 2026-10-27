using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Ambiente do vulcão (só visual): fumo moderado nas fumarolas da crista, brilho alaranjado na cratera a pulsar
    /// devagar e brasas às entradas para se reconhecerem de longe. Custo fixo: 1 luz, poucas partículas por segundo.
    /// </summary>
    public class VolcanoAmbience : MonoBehaviour
    {
        public Transform[] fumaroles = new Transform[0];
        public Transform craterLight;
        public Transform[] entrances = new Transform[0];
        public float smokePerVent = 0.45f;

        Light m_Light;
        float[] m_Next = new float[0];
        float m_EntranceNext;
        static readonly Color Ember = new Color(1f, 0.52f, 0.16f);

        void Start()
        {
            // Ambiente sonoro do vulcão (2D, discreto, em ciclo)
            var bed = Resources.Load<AudioClip>("Audio/Ambience/VolcanoInterior_loop");
            if (bed != null)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = bed;
                src.loop = true;
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.volume = 0.22f;
                src.priority = 200;
                src.time = Random.Range(0f, bed.length * 0.9f);
                src.Play();
            }

            m_Next = new float[fumaroles.Length];
            for (int i = 0; i < m_Next.Length; i++) m_Next[i] = Time.time + Random.Range(0f, 2f);
            if (craterLight != null)
            {
                m_Light = craterLight.gameObject.AddComponent<Light>();
                m_Light.type = LightType.Point;
                m_Light.color = new Color(1f, 0.5f, 0.2f);
                m_Light.range = 22f;
                m_Light.intensity = 1.2f;
                m_Light.shadows = LightShadows.None;
            }
        }

        void Update()
        {
            float now = Time.time;
            for (int i = 0; i < fumaroles.Length; i++)
            {
                if (fumaroles[i] == null || now < m_Next[i]) continue;
                m_Next[i] = now + Random.Range(0.6f, 1.4f) / Mathf.Max(0.05f, smokePerVent);
                var p = fumaroles[i].position;
                Fx.Smoke(p + Random.insideUnitSphere * 0.5f, new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(1.0f, 1.6f), Random.Range(-0.2f, 0.2f)),
                         Random.Range(1.8f, 2.8f), Random.Range(4f, 6f), 0.3f);
            }
            if (m_Light != null) m_Light.intensity = 1.15f + 0.25f * Mathf.Sin(now * 0.7f) + 0.1f * Mathf.PerlinNoise(now * 1.3f, 0.5f);
            if (now >= m_EntranceNext && entrances.Length > 0)
            {
                m_EntranceNext = now + 0.35f;
                var e = entrances[Random.Range(0, entrances.Length)];
                if (e != null)
                    Fx.Glow(e.position + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(0.2f, 1.5f), Random.Range(-2.5f, 2.5f)),
                            Vector3.up * Random.Range(0.6f, 1.2f), Ember, 0.09f, 1.4f);
            }
        }
    }
}
