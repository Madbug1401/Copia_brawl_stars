using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Luz e ambiente de um mapa carregado por cima da arena (sol, luz ambiente, nevoeiro).
    /// Aplicado pelo <see cref="MapLoader"/>; ao voltar à arena repõe-se o que estava na cena.
    /// </summary>
    public class MapTheme : MonoBehaviour
    {
        public Color sunColor = new Color(1f, 0.93f, 0.8f);
        public float sunIntensity = 1.25f;
        public Vector3 sunEuler = new Vector3(48f, -35f, 0f);
        public Color ambientSky = new Color(0.7f, 0.78f, 0.95f);
        public Color ambientEquator = new Color(0.66f, 0.62f, 0.58f);
        public Color ambientGround = new Color(0.42f, 0.36f, 0.3f);
        public Color fogColor = new Color(0.76f, 0.86f, 0.96f);
        public float fogStart = 50f, fogEnd = 220f;

        public struct Snapshot
        {
            public bool valid;
            public Light sun;
            public Color sunColor;
            public float sunIntensity;
            public Quaternion sunRotation;
            public AmbientMode ambientMode;
            public Color sky, equator, ground, ambientLight;
            public bool fog;
            public FogMode fogMode;
            public Color fogColor;
            public float fogStart, fogEnd, fogDensity;
        }

        static Light Sun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }

        public static Snapshot Capture()
        {
            var sun = Sun();
            return new Snapshot
            {
                valid = true,
                sun = sun,
                sunColor = sun != null ? sun.color : Color.white,
                sunIntensity = sun != null ? sun.intensity : 1f,
                sunRotation = sun != null ? sun.transform.rotation : Quaternion.identity,
                ambientMode = RenderSettings.ambientMode,
                sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor,
                ambientLight = RenderSettings.ambientLight,
                fog = RenderSettings.fog,
                fogMode = RenderSettings.fogMode,
                fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance,
                fogDensity = RenderSettings.fogDensity,
            };
        }

        public static void Restore(Snapshot s)
        {
            if (!s.valid) return;
            if (s.sun != null)
            {
                s.sun.color = s.sunColor;
                s.sun.intensity = s.sunIntensity;
                s.sun.transform.rotation = s.sunRotation;
            }
            RenderSettings.ambientMode = s.ambientMode;
            RenderSettings.ambientSkyColor = s.sky;
            RenderSettings.ambientEquatorColor = s.equator;
            RenderSettings.ambientGroundColor = s.ground;
            RenderSettings.ambientLight = s.ambientLight;
            RenderSettings.fog = s.fog;
            RenderSettings.fogMode = s.fogMode;
            RenderSettings.fogColor = s.fogColor;
            RenderSettings.fogStartDistance = s.fogStart;
            RenderSettings.fogEndDistance = s.fogEnd;
            RenderSettings.fogDensity = s.fogDensity;
        }

        public void Apply()
        {
            var sun = Sun();
            if (sun != null)
            {
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.transform.rotation = Quaternion.Euler(sunEuler);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }
    }
}
