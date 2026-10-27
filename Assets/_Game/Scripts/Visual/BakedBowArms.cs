using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Braços da 1.ª pessoa do N.Cage (trapper) animados no Blender, com o arco composto e as 3 flechas
    /// (Resources/Characters/trapper_fpview.fbx). Substituem as mãos estilizadas e o arco do jogo.
    ///
    /// O FBX traz um só esqueleto com os dois braços, o osso "Arco", o filho "Corda" (meio da corda) e as três flechas
    /// (escala ~0 quando estão escondidas). Animações (30 fps no Blender): Parado, Carregar (tirar as flechas da aljava,
    /// encaixar e puxar), Mirar (corda puxada, em ciclo) e Atirar (larga a corda; as flechas que voam são as do jogo).
    /// Marcadores: CamRef / CamRef_Frente / CamRef_Cima (câmara do Blender, alinhada com a do jogo) e Ponta (apoio das
    /// flechas no arco: de onde saem os projéteis).
    /// Ciclo no jogo: Carregar → Mirar (em ciclo) → ataque: Atirar → Carregar (acelerado, para acompanhar o cooldown).
    /// </summary>
    public class BakedBowArms : MonoBehaviour
    {
        public const string BrawlerId = "trapper";
        /// <summary>Cor das unhas (toon). Pública para afinar.</summary>
        public static Color NailColor = new Color(0.66f, 0.45f, 0.40f);
        /// <summary>Velocidade do Carregar depois de um tiro (o cooldown do N.Cage é curto) e o frame onde começa.</summary>
        public static float ReloadSpeed = 2.6f, ReloadStartSeconds = 6f / 30f;
        /// <summary>Parte do Atirar que se vê antes de ir buscar novas flechas (a corda já parou de vibrar).</summary>
        public static float ReleaseSeconds = 8f / 30f;
        const float Blend = 0.1f;

        enum Mode { Carregar, Mirar, Atirar }

        Transform m_Frame, m_Root, m_Muzzle;
        Camera m_Camera;
        Animation m_Anim;
        AnimationState m_Carregar, m_Mirar, m_Atirar, m_Cur, m_Prev;
        float m_T, m_PrevTime, m_BlendK = 1f, m_Rate = 1f, m_Speed = 1f, m_Drop;
        Mode m_Mode;

        /// <summary>Renderers do arco e das flechas (para o brilho do Super).</summary>
        public Renderer[] WeaponRenderers { get; private set; }
        public bool IsAiming => m_Mode == Mode.Mirar;

        public static BakedBowArms TryCreate(BrawlerDefinition b, Transform frame, Camera camera, int layer)
        {
            if (b == null || frame == null || b.brawlerId != BrawlerId) return null;
            var prefab = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId + BakedViewArms.ResourceSuffix);
            if (prefab == null) return null;
            var go = new GameObject("BakedBowArms");
            go.transform.SetParent(frame, false);
            var arms = go.AddComponent<BakedBowArms>();
            if (!arms.Build(b, prefab, camera, layer))
            {
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                return null;
            }
            return arms;
        }

        bool Build(BrawlerDefinition b, GameObject prefab, Camera camera, int layer)
        {
            m_Frame = transform;
            m_Camera = camera;
            var go = Instantiate(prefab, m_Frame, false);
            go.name = "NCage_Arco_Bracos";
            m_Root = go.transform;
            m_Anim = go.GetComponentInChildren<Animation>();
            if (m_Anim == null) { Debug.LogWarning("[BakedBowArms] O FBX tem de ser importado como Legacy."); return false; }

            Transform cam = null, camF = null, camU = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "CamRef": cam = t; break;
                    case "CamRef_Frente": camF = t; break;
                    case "CamRef_Cima": camU = t; break;
                    case "Ponta": m_Muzzle = t; break;
                }
            }
            if (cam == null || camF == null || camU == null || m_Muzzle == null) { Debug.LogWarning("[BakedBowArms] Faltam marcadores no FBX."); return false; }

            // a câmara do Blender (CamRef) passa a coincidir com a origem do referencial (câmara do jogo, +Z para a frente)
            var p0 = m_Frame.InverseTransformPoint(cam.position);
            var pf = m_Frame.InverseTransformPoint(camF.position);
            var pu = m_Frame.InverseTransformPoint(camU.position);
            var camInFrame = Matrix4x4.TRS(p0, Quaternion.LookRotation(pf - p0, pu - p0), Vector3.one);
            var local = camInFrame.inverse * Matrix4x4.TRS(m_Root.localPosition, m_Root.localRotation, m_Root.localScale);
            m_Root.localPosition = local.GetColumn(3);
            m_Root.localRotation = local.rotation;

            foreach (AnimationState st in m_Anim)
            {
                if (st.name.EndsWith("Carregar")) m_Carregar = st;
                else if (st.name.EndsWith("Mirar")) m_Mirar = st;
                else if (st.name.EndsWith("Atirar")) m_Atirar = st;
            }
            if (m_Carregar == null || m_Mirar == null || m_Atirar == null)
            {
                Debug.LogWarning("[BakedBowArms] Faltam as animações Carregar/Mirar/Atirar no FBX.");
                return false;
            }
            m_Anim.playAutomatically = false;
            m_Anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState st in m_Anim)
            {
                st.enabled = false; st.weight = 0f; st.speed = 0f;
                st.wrapMode = WrapMode.ClampForever;
                st.blendMode = AnimationBlendMode.Blend;
            }
            m_Rate = m_Mirar.length / (20f / 30f);   // o Mirar tem 21 frames a 30 fps (o FBX pode vir a outro ritmo)

            // materiais: pele = toon do próprio personagem (mesma textura/UV), arco e flechas = toons das armas do jogo
            Material skin = null, bow = null, arrow = null, nails = null;
            var character = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId);
            if (character != null)
            {
                var smr = character.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) skin = smr.sharedMaterial;
            }
            bow = FirstMaterial(WeaponModels.PrefabFolder + "Bow");
            arrow = FirstMaterial(WeaponModels.PrefabFolder + "Arrow");
            var weapon = new System.Collections.Generic.List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool isWeapon = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    if (n.Contains("Unha"))
                    {
                        if (nails == null)
                        {
                            nails = new Material(skin != null ? skin : Art.Toon) { name = "NCage_FP_Unhas" };
                            nails.mainTexture = null;
                            nails.color = NailColor;
                        }
                        mats[i] = nails;
                    }
                    else if (n.Contains("Arco")) { if (bow != null) mats[i] = bow; isWeapon = true; }
                    else if (n.Contains("Flecha")) { if (arrow != null) mats[i] = arrow; isWeapon = true; }
                    else if (skin != null) mats[i] = skin;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                if (isWeapon) weapon.Add(r);
            }
            WeaponRenderers = weapon.ToArray();
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            // ao aparecer: vai buscar as flechas e puxa a corda
            m_Cur = m_Carregar; m_Cur.enabled = true; m_Cur.weight = 1f;
            m_Mode = Mode.Carregar; m_T = 0f; m_Speed = 1.6f;
            UpdateFov();
            return true;
        }

        static Material FirstMaterial(string resource)
        {
            var p = Resources.Load<GameObject>(resource);
            if (p == null) return null;
            var r = p.GetComponentInChildren<Renderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        // ------------------------------------------------------------------ eventos (ViewModelRig)

        /// <summary>Ataque: larga a corda (as 3 flechas do jogo saem da Ponta).</summary>
        public void OnAttack()
        {
            Switch(m_Atirar, Mode.Atirar);
            m_T = 0f;
        }

        /// <summary>Ponto de onde saem as flechas (apoio no arco).</summary>
        public Vector3 Muzzle => m_Muzzle != null ? m_Muzzle.position : transform.position + transform.forward * 0.5f;

        /// <summary>Golpe de faca: os braços com o arco baixam (0..1).</summary>
        public void SetDrop(float k) => m_Drop = Mathf.Clamp01(k);

        /// <summary>Avança a animação fora do Play (pré-visualização / testes no editor).</summary>
        public void EditorStep(float dt)
        {
            Step(dt);
            UpdateFov();
            m_Anim.Sample();
        }

        void Switch(AnimationState next, Mode mode)
        {
            if (m_Prev != null && m_Prev != next && m_Prev != m_Cur) { m_Prev.enabled = false; m_Prev.weight = 0f; }
            m_Prev = m_Cur; m_PrevTime = m_Cur != null ? m_Cur.time : 0f;
            m_Cur = next; m_Mode = mode; m_BlendK = 0f;
            next.enabled = true;
        }

        void Update()
        {
            Step(Time.deltaTime);
            UpdateFov();
        }

        void Step(float dt)
        {
            float r = m_Rate;
            switch (m_Mode)
            {
                case Mode.Carregar:
                    m_T += dt * m_Speed * r;
                    if (m_T >= m_Carregar.length) { Switch(m_Mirar, Mode.Mirar); m_T = 0f; }
                    break;
                case Mode.Mirar:
                    m_T = Mathf.Repeat(m_T + dt * r, m_Mirar.length);
                    break;
                case Mode.Atirar:
                    m_T += dt * r;
                    if (m_T >= ReleaseSeconds * r)
                    {
                        Switch(m_Carregar, Mode.Carregar);
                        m_T = ReloadStartSeconds * r; m_Speed = ReloadSpeed;
                    }
                    break;
            }
            m_Cur.time = Mathf.Clamp(m_T, 0f, m_Cur.length);
            m_BlendK = Mathf.MoveTowards(m_BlendK, 1f, dt / Blend);
            m_Cur.enabled = true;
            m_Cur.weight = m_BlendK;
            if (m_Prev != null && m_Prev != m_Cur)
            {
                m_Prev.time = m_PrevTime;
                m_Prev.weight = 1f - m_BlendK;
                m_Prev.enabled = m_BlendK < 1f;
            }
            transform.localPosition = new Vector3(0f, -0.35f, -0.08f) * Mathf.SmoothStep(0f, 1f, m_Drop);
        }

        /// <summary>Mesmo truque do BakedViewArms: achata o referencial em profundidade para o campo de visão do jogo.</summary>
        void UpdateFov()
        {
            if (m_Camera == null) return;
            float k = Mathf.Tan(BakedViewArms.AuthoredVerticalFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(10f, m_Camera.fieldOfView) * 0.5f * Mathf.Deg2Rad);
            m_Frame.localScale = new Vector3(1f, 1f, Mathf.Clamp(k, 0.3f, 1.5f));
        }
    }
}
