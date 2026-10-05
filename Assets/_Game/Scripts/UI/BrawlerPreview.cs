using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Pré-visualização 3D do Brawler escolhido no menu: a rig é montada longe do mapa e filmada por uma
    /// câmara própria para uma RenderTexture, desenhada pela UI. Roda devagar e mostra o ataque de vez em quando.
    /// </summary>
    public class BrawlerPreview
    {
        static readonly Vector3 Origin = new Vector3(0f, -400f, 0f);

        Transform m_Root;
        Camera m_Camera;
        RenderTexture m_Texture;
        CharacterRig m_Rig;
        Transform m_Podium;
        Light m_Light;
        int m_Index = -1;
        float m_Spin = 200f;
        float m_NextAttack;
        int m_Shot;

        public Texture Texture => m_Texture;

        public void Show(int brawlerIndex)
        {
            if (m_Root == null) Create();
            if (!m_Root.gameObject.activeSelf) m_Root.gameObject.SetActive(true);
            if (brawlerIndex != m_Index)
            {
                m_Index = brawlerIndex;
                m_Rig?.Destroy();
                var b = BrawlerRegistry.Get(brawlerIndex);
                m_Rig = CharacterRig.Build(b, m_Root, ShadowCastingMode.On, 0);
                m_Rig.SetDead(true, true);
                m_Rig.SetDead(false, false); // aparece com "pop"
                m_NextAttack = Time.time + 1f;
                if (b != null && m_Podium != null)
                    foreach (var r in m_Podium.GetComponentsInChildren<Renderer>()) Art.SetEmission(r, Art.Darken(b.Tint, 0.15f));
            }
        }

        public void Hide()
        {
            if (m_Root != null && m_Root.gameObject.activeSelf) m_Root.gameObject.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (m_Root == null || !m_Root.gameObject.activeSelf || m_Rig == null || m_Rig.Root == null) return;
            m_Spin += dt * 25f;
            m_Rig.Root.localRotation = Quaternion.Euler(0f, m_Spin, 0f);
            m_Rig.Tick(dt, new RigFrame { worldVelocity = Vector3.zero, grounded = true, pitch = 5f, superReady = false });

            if (Time.time >= m_NextAttack)
            {
                var b = BrawlerRegistry.Get(m_Index);
                m_Rig.OnAttack(false, b != null ? b.animations.attackDuration : 0.4f);
                m_Rig.OnShot(m_Shot++);
                m_NextAttack = Time.time + 2.6f;
            }
        }

        void Create()
        {
            m_Root = new GameObject("[BrawlerPreview]").transform;
            m_Root.position = Origin;

            m_Texture = new RenderTexture(512, 640, 16, RenderTextureFormat.ARGB32) { name = "BrawlerPreview", antiAliasing = 2 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(m_Root, false);
            camGo.transform.localPosition = new Vector3(0f, 1.25f, 3.6f);
            camGo.transform.LookAt(Origin + new Vector3(0f, 0.95f, 0f));
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_Camera.fieldOfView = 36f;
            m_Camera.nearClipPlane = 0.1f;
            m_Camera.farClipPlane = 20f;
            m_Camera.targetTexture = m_Texture;
            m_Camera.depth = -10f;

            // Luz de recorte para dar volume
            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(m_Root, false);
            lightGo.transform.localPosition = new Vector3(-2f, 3f, 2.5f);
            m_Light = lightGo.AddComponent<Light>();
            m_Light.type = LightType.Point;
            m_Light.range = 8f;
            m_Light.intensity = 1.2f;
            m_Light.color = new Color(1f, 0.95f, 0.85f);
            m_Light.shadows = LightShadows.None;

            m_Podium = new GameObject("Podium").transform;
            m_Podium.SetParent(m_Root, false);
            Art.Part(PrimitiveType.Cylinder, m_Podium, new Vector3(0f, -0.08f, 0f), new Vector3(1.6f, 0.08f, 1.6f), new Color(0.95f, 0.85f, 0.55f), true, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, m_Podium, new Vector3(0f, -0.2f, 0f), new Vector3(1.75f, 0.06f, 1.75f), new Color(0.45f, 0.3f, 0.2f), true, default, ShadowCastingMode.Off);
        }

        public void Dispose()
        {
            if (m_Root != null) Object.Destroy(m_Root.gameObject);
            if (m_Texture != null) m_Texture.Release();
            m_Root = null;
            m_Rig = null;
            m_Index = -1;
        }
    }
}
