using UnityEngine;

namespace FPSLAN
{
    /// <summary>Animação decorativa simples: roda e/ou desliza devagar (gemas, nuvens). Só visual.</summary>
    public class Spinner : MonoBehaviour
    {
        public Vector3 speed;
        [Tooltip("Deslocamento em m/s (nuvens). Dá a volta ao chegar a 'wrap' metros do centro.")]
        public Vector3 drift;
        public float wrap = 140f;
        [Tooltip("Centro da volta das nuvens (o centro do mapa a que pertencem).")]
        public Vector3 wrapCenter;
        public float bob = 0f;

        Vector3 m_Start;

        void Awake() => m_Start = transform.localPosition;

        void Update()
        {
            float dt = Time.deltaTime;
            if (speed != Vector3.zero) transform.Rotate(speed * dt, Space.World);
            if (drift != Vector3.zero)
            {
                var p = transform.position + drift * dt - wrapCenter;
                if (Mathf.Abs(p.x) > wrap) p.x = -Mathf.Sign(p.x) * wrap;
                if (Mathf.Abs(p.z) > wrap) p.z = -Mathf.Sign(p.z) * wrap;
                transform.position = p + wrapCenter;
            }
            if (bob > 0f)
                transform.localPosition = new Vector3(transform.localPosition.x, m_Start.y + Mathf.Sin(Time.time * 2f) * bob, transform.localPosition.z);
        }
    }
}
