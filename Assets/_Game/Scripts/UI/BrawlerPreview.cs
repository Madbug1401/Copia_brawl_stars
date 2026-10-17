using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Pré-visualização 3D do Brawler escolhido no menu (com a skin e o pet), ou só de um pet: a rig é montada longe
    /// do mapa e filmada por uma câmara própria para uma RenderTexture, desenhada pela UI. Roda devagar e mostra o
    /// ataque de vez em quando.
    /// </summary>
    public class BrawlerPreview
    {
        readonly Vector3 m_Origin;

        public BrawlerPreview() : this(new Vector3(0f, -400f, 0f)) { }

        /// <summary>Cada pré-visualização usa um ponto diferente do mundo (as câmaras não se veem umas às outras).</summary>
        public BrawlerPreview(Vector3 origin)
        {
            m_Origin = origin;
        }

        Transform m_Root;
        Camera m_Camera;
        RenderTexture m_Texture;
        CharacterRig m_Rig;
        PetModel m_Pet;
        Transform m_Podium;
        Light m_Light;
        int m_Index = -1;
        int m_SkinHash, m_PetHash, m_PetLevel = -1, m_PetSkinHash;
        bool m_PetOnly;
        float m_Spin = 200f;
        float m_NextAttack;
        int m_Shot;

        public Texture Texture => m_Texture;

        /// <summary>Brawler (com skin opcional) e, opcionalmente, o pet equipado ao lado.</summary>
        public void Show(int brawlerIndex, SkinDefinition skin = null, PetDefinition pet = null, int petLevel = 1, SkinDefinition petSkin = null)
        {
            if (m_Root == null) Create();
            if (!m_Root.gameObject.activeSelf) m_Root.gameObject.SetActive(true);
            int skinHash = skin != null ? skin.StableHash : 0;
            if (m_PetOnly || brawlerIndex != m_Index || skinHash != m_SkinHash)
            {
                m_PetOnly = false;
                m_Index = brawlerIndex;
                m_SkinHash = skinHash;
                m_Rig?.Destroy();
                var b = BrawlerRegistry.Get(brawlerIndex);
                m_Rig = CharacterRig.Build(b, m_Root, ShadowCastingMode.On, 0, skin);
                m_Rig.SetDead(true, true);
                m_Rig.SetDead(false, false); // aparece com "pop"
                m_NextAttack = Time.time + 1f;
                if (b != null && m_Podium != null)
                    foreach (var r in m_Podium.GetComponentsInChildren<Renderer>()) Art.SetEmission(r, Art.Darken(b.Tint, 0.15f));
                m_PetLevel = -1; // reposiciona o pet
            }
            SetPet(pet, petLevel, petSkin, false);
        }

        /// <summary>Só um pet (ecrãs de pets): maior e ao centro.</summary>
        public void ShowPet(PetDefinition pet, int level, SkinDefinition petSkin = null)
        {
            if (m_Root == null) Create();
            if (!m_Root.gameObject.activeSelf) m_Root.gameObject.SetActive(true);
            if (!m_PetOnly)
            {
                m_PetOnly = true;
                m_Rig?.Destroy();
                m_Rig = null;
                m_Index = -1;
                m_PetLevel = -1;
            }
            SetPet(pet, level, petSkin, true);
        }

        void SetPet(PetDefinition pet, int level, SkinDefinition petSkin, bool centered)
        {
            int hash = pet != null ? pet.StableHash : 0;
            int skinHash = petSkin != null ? petSkin.StableHash : 0;
            if (hash == m_PetHash && level == m_PetLevel && skinHash == m_PetSkinHash && (m_Pet != null || hash == 0)) return;
            m_PetHash = hash;
            m_PetLevel = level;
            m_PetSkinHash = skinHash;
            if (m_Pet != null) Object.Destroy(m_Pet.root.gameObject);
            m_Pet = null;
            if (pet == null) return;
            m_Pet = PetVisualBuilder.Build(pet, level, petSkin, m_Root, ShadowCastingMode.On);
            m_Pet.root.localPosition = centered ? new Vector3(0f, 0f, 0.3f) : new Vector3(0.85f, 0f, 0.55f);
            m_Pet.root.localScale = Vector3.one * (centered ? 2.3f : 1f);
            m_Pet.root.localRotation = Quaternion.Euler(0f, centered ? 200f : 210f, 0f);
        }

        public void Hide()
        {
            if (m_Root != null && m_Root.gameObject.activeSelf) m_Root.gameObject.SetActive(false);
        }

        public void Tick(float dt)
        {
            if (m_Root == null || !m_Root.gameObject.activeSelf) return;
            m_Spin += dt * 25f;

            if (m_Pet != null)
            {
                // Idle simples: respira, abana a cauda e (no modo só pet) roda devagar
                float t = Time.time;
                m_Pet.body.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(t * 2.4f)) * 0.03f;
                if (m_Pet.tail != null) m_Pet.tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 9f) * 25f, 0f);
                if (m_Pet.head != null) m_Pet.head.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.8f) * 20f, 0f);
                if (m_PetOnly) m_Pet.root.localRotation = Quaternion.Euler(0f, m_Spin, 0f);
                if (m_Pet.aura && Random.value < dt * 6f)
                    Fx.Glow(m_Pet.root.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.5f, Vector3.up * 0.5f, Color.Lerp(m_Pet.accent, Color.yellow, 0.5f), 0.07f, 0.6f);
            }

            if (m_Rig == null || m_Rig.Root == null) return;
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
            m_Root.position = m_Origin;

            m_Texture = new RenderTexture(512, 640, 16, RenderTextureFormat.ARGB32) { name = "BrawlerPreview", antiAliasing = 2 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(m_Root, false);
            camGo.transform.localPosition = new Vector3(0f, 1.25f, 3.6f);
            camGo.transform.LookAt(m_Origin + new Vector3(0f, 0.95f, 0f));
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
            m_Pet = null;
            m_Index = -1;
        }
    }
}
