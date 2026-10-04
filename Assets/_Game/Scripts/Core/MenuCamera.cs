using UnityEngine;

namespace FPSLAN
{
    /// <summary>Câmara de vista geral usada no menu e quando não há jogador local.</summary>
    public class MenuCamera : MonoBehaviour
    {
        public static MenuCamera Instance { get; private set; }

        public Vector3 focus = Vector3.zero;
        public float radius = 34f;
        public float height = 24f;
        public float speed = 4f;

        float m_Angle = 210f;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static void SetActive(bool active)
        {
            if (Instance != null) Instance.gameObject.SetActive(active);
        }

        void Update()
        {
            m_Angle += speed * Time.deltaTime;
            var rad = m_Angle * Mathf.Deg2Rad;
            transform.position = focus + new Vector3(Mathf.Sin(rad) * radius, height, Mathf.Cos(rad) * radius);
            transform.LookAt(focus);
        }
    }
}
