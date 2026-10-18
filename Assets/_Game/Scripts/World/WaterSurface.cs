using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Água estilizada barata (sem reflexos/refrações): desloca a textura em duas direções, oscila uns milímetros
    /// e respira a cor. Só visual, igual em todas as máquinas. Usada na Buracona (superfície e espuma).
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterSurface : MonoBehaviour
    {
        static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public Vector2 scrollSpeed = new Vector2(0.025f, 0.012f);
        public float tiling = 1f;
        [Tooltip("Amplitude da oscilação vertical (m).")]
        public float bob = 0.015f;
        public float bobSpeed = 0.9f;
        [Tooltip("Variação de opacidade/brilho da cor (0 = nenhuma).")]
        public float pulse = 0.06f;

        Renderer m_Renderer;
        MaterialPropertyBlock m_Mpb;
        Vector3 m_BasePos;
        Color m_BaseColor = Color.white;

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Mpb = new MaterialPropertyBlock();
            m_BasePos = transform.localPosition;
            if (m_Renderer != null && m_Renderer.sharedMaterial != null && m_Renderer.sharedMaterial.HasProperty(ColorId))
                m_BaseColor = m_Renderer.sharedMaterial.GetColor(ColorId);
        }

        void Update()
        {
            float t = Time.time;
            transform.localPosition = m_BasePos + Vector3.up * (Mathf.Sin(t * bobSpeed) * bob);
            if (m_Renderer == null) return;
            if (m_Mpb == null) m_Mpb = new MaterialPropertyBlock();
            m_Renderer.GetPropertyBlock(m_Mpb);
            var off = scrollSpeed * t;
            m_Mpb.SetVector(MainTexSt, new Vector4(tiling, tiling, off.x % 1f, off.y % 1f));
            float k = 1f + Mathf.Sin(t * 1.3f) * pulse;
            m_Mpb.SetColor(ColorId, new Color(m_BaseColor.r * k, m_BaseColor.g * k, m_BaseColor.b * k, m_BaseColor.a));
            m_Renderer.SetPropertyBlock(m_Mpb);
        }
    }
}
