using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Braços da 1.ª pessoa animados no Blender (FBX em Resources/Characters/&lt;brawlerId&gt;_fpview), usados em vez das
    /// mãos estilizadas quando existem. Hoje: Mr.S (sprayer) com as duas latas.
    ///
    /// O FBX traz só o braço ESQUERDO (com unhas e manga) e as animações dos dois braços, todas no lado esquerdo:
    /// L_Idle / L_Spray / L_Shake e R_Idle / R_Spray / R_Shake. O braço direito é o mesmo modelo dentro de um pai com
    /// escala X = -1 (espelho no plano vertical da câmara), a tocar as animações R_*.
    /// Marcadores no FBX: CamRef / CamRef_Frente / CamRef_Cima (câmara do Blender — alinhada com a câmara do jogo) e
    /// Lata / Lata_Cima / Lata_Frente (filhos do osso da mão: centro da lata, cima e direção do jato).
    /// As latas continuam a ser os modelos do jogo (WeaponModels, com o bico de onde sai o spray): seguem os marcadores.
    /// </summary>
    public class BakedViewArms : MonoBehaviour
    {
        public const string ResourceSuffix = "_fpview";
        /// <summary>Campo de visão vertical da câmara do Blender onde a animação foi feita (lente 18 mm, 16:9).</summary>
        public const float AuthoredVerticalFov = 58.72f;
        /// <summary>Distância (m) do centro da lata à origem do modelo da lata do jogo (a origem é o punho).</summary>
        public static float CanOriginBelowCenter = 0.019f;
        /// <summary>Cor das unhas (toon). Pública para afinar.</summary>
        public static Color NailColor = new Color(0.62f, 0.42f, 0.36f);

        // Tempos (s) dentro da animação "Spray" (30 fps): o polegar chega ao bico, mantém e larga.
        const float SprayPressEnd = 14f / 30f, SprayHoldStart = 16f / 30f, SprayHoldEnd = 51f / 30f, SprayReleaseStart = 59f / 30f;
        const float Blend = 0.12f;

        Transform m_Frame, m_Mirror;
        Arm m_L, m_R;
        Camera m_Camera;
        Transform m_CanL, m_CanR;
        float m_RightDrop, m_Aim;
        Vector3 m_LBase;

        enum Mode { Idle, Spray, Shake }

        class Arm
        {
            public Transform root;
            public Animation anim;
            public Transform can, canUp, canFwd;
            public AnimationState idle, spray, shake;
            public AnimationState cur, prev;
            public float curTime, prevTime, blend = 1f;
            public Mode mode = Mode.Idle;
            public float t, sprayLeft, shakeSpeed = 1.8f;
            public float idleOffset;
            /// <summary>Segundos do clip por segundo da animação feita no Blender (30 fps; o FBX pode vir a outro ritmo).</summary>
            public float rate = 1f;
        }

        /// <summary>
        /// Cria os braços para o personagem, se houver FBX. <paramref name="frame"/> = referencial da câmara com balanço
        /// (o "Sway" do ViewModelRig). Devolve null se este personagem não tiver braços animados.
        /// </summary>
        public static BakedViewArms TryCreate(BrawlerDefinition b, Transform frame, Camera camera, int layer, Transform canR, Transform canL)
        {
            if (b == null || frame == null) return null;
            var prefab = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId + ResourceSuffix);
            if (prefab == null) return null;
            var go = new GameObject("BakedArms");
            go.transform.SetParent(frame, false);
            var arms = go.AddComponent<BakedViewArms>();
            if (!arms.Build(b, prefab, camera, layer, canR, canL)) { Destroy(go); return null; }
            return arms;
        }

        bool Build(BrawlerDefinition b, GameObject prefab, Camera camera, int layer, Transform canR, Transform canL)
        {
            m_Frame = transform;
            m_Camera = camera;
            m_CanR = canR; m_CanL = canL;
            m_Mirror = new GameObject("Direito_Espelho").transform;
            m_Mirror.SetParent(m_Frame, false);
            m_Mirror.localScale = new Vector3(-1f, 1f, 1f);

            m_L = MakeArm(prefab, m_Frame, "L");
            m_R = MakeArm(prefab, m_Mirror, "R");
            if (m_L == null || m_R == null) return false;
            m_R.idleOffset = 0.35f;
            m_LBase = m_L.root.localPosition;   // as duas mãos não respiram exatamente juntas

            // material toon do próprio personagem (mesma textura e UV); unhas num toon liso
            Material skin = null;
            var character = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId);
            if (character != null)
            {
                var smr = character.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null) skin = smr.sharedMaterial;
            }
            Material nails = null;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    bool isNail = mats[i] != null && mats[i].name.Contains("Unha");
                    if (isNail)
                    {
                        if (nails == null)
                        {
                            nails = new Material(skin != null ? skin : Art.Toon) { name = "MrS_FP_Unhas" };
                            nails.mainTexture = null;
                            nails.color = NailColor;
                        }
                        mats[i] = nails;
                    }
                    else if (skin != null) mats[i] = skin;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
            }
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            UpdateFov();
            return true;
        }

        Arm MakeArm(GameObject prefab, Transform parent, string side)
        {
            var go = Instantiate(prefab, parent, false);
            go.name = "Braco_" + side;
            var a = new Arm { root = go.transform, anim = go.GetComponentInChildren<Animation>() };
            if (a.anim == null) { Debug.LogWarning("[BakedViewArms] O FBX dos braços tem de ser importado como Legacy."); return null; }
            Transform cam = null, camF = null, camU = null;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "CamRef": cam = t; break;
                    case "CamRef_Frente": camF = t; break;
                    case "CamRef_Cima": camU = t; break;
                    case "Lata": a.can = t; break;
                    case "Lata_Cima": a.canUp = t; break;
                    case "Lata_Frente": a.canFwd = t; break;
                }
            }
            if (cam == null || camF == null || camU == null || a.can == null) { Debug.LogWarning("[BakedViewArms] Faltam marcadores no FBX."); return null; }

            // a câmara do Blender (CamRef) passa a coincidir com a origem do referencial (câmara do jogo, +Z para a frente)
            var p0 = parent.InverseTransformPoint(cam.position);
            var pf = parent.InverseTransformPoint(camF.position);
            var pu = parent.InverseTransformPoint(camU.position);
            var camInParent = Matrix4x4.TRS(p0, Quaternion.LookRotation(pf - p0, pu - p0), Vector3.one);
            var local = camInParent.inverse * Matrix4x4.TRS(a.root.localPosition, a.root.localRotation, a.root.localScale);
            a.root.localPosition = local.GetColumn(3);
            a.root.localRotation = local.rotation;

            foreach (AnimationState st in a.anim)
            {
                if (st.name.EndsWith(side + "_Idle")) a.idle = st;
                else if (st.name.EndsWith(side + "_Spray")) a.spray = st;
                else if (st.name.EndsWith(side + "_Shake")) a.shake = st;
            }
            if (a.idle == null || a.spray == null || a.shake == null)
            {
                Debug.LogWarning("[BakedViewArms] Faltam animações " + side + "_Idle/_Spray/_Shake no FBX.");
                return null;
            }
            a.anim.playAutomatically = false;
            a.anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (AnimationState st in a.anim)
            {
                st.enabled = false; st.weight = 0f; st.speed = 0f;
                st.wrapMode = WrapMode.ClampForever;
                st.blendMode = AnimationBlendMode.Blend;
            }
            a.cur = a.idle; a.cur.enabled = true; a.cur.weight = 1f;
            a.rate = a.idle.length / (59f / 30f);   // o Idle tem 60 frames a 30 fps
            return a;
        }

        // ------------------------------------------------------------------ eventos (chamados pelo ViewModelRig)

        /// <summary>Ataque: os polegares carregam nos bicos durante <paramref name="seconds"/>.</summary>
        public void OnSpray(float seconds)
        {
            foreach (var a in new[] { m_L, m_R })
            {
                if (a == null) continue;
                if (a.mode != Mode.Spray) { Switch(a, a.spray, Mode.Spray); a.t = 0f; }
                else if (a.t > SprayHoldEnd) a.t = SprayHoldStart;   // continua a carregar
                a.sprayLeft = Mathf.Max(a.sprayLeft, seconds);
            }
        }

        /// <summary>Uma carga recarregou: abana as latas (só se não estiver a pintar nem já a abanar).</summary>
        public void OnReload()
        {
            foreach (var a in new[] { m_L, m_R })
            {
                if (a == null || a.mode != Mode.Idle) continue;
                Switch(a, a.shake, Mode.Shake);
                a.t = 0f;
            }
        }

        /// <summary>Avança a animação e coloca as latas fora do Play (pré-visualização / testes no editor).</summary>
        public void EditorStep(float dt)
        {
            Step(m_L, dt); Step(m_R, dt);
            PlaceArms();
            UpdateFov();
            if (m_L != null) m_L.anim.Sample();
            if (m_R != null) m_R.anim.Sample();
            LateUpdate();
        }

        /// <summary>Golpe de faca: o braço direito baixa (0..1).</summary>
        public void SetRightDrop(float k) => m_RightDrop = Mathf.Clamp01(k);

        /// <summary>A mirar (0..1): as duas latas aproximam-se do centro e sobem um pouco.</summary>
        public void SetAim(float k) => m_Aim = Mathf.Clamp01(k);

        void PlaceArms()
        {
            var aim = new Vector3(0.06f, 0.025f, 0f) * m_Aim;
            if (m_L != null) m_L.root.localPosition = m_LBase + aim;
            if (m_Mirror != null) m_Mirror.localPosition = new Vector3(0f, -0.35f, -0.08f) * Mathf.SmoothStep(0f, 1f, m_RightDrop) + new Vector3(-aim.x, aim.y, 0f);
        }

        static void Switch(Arm a, AnimationState next, Mode mode)
        {
            if (a.prev != null && a.prev != next && a.prev != a.cur) { a.prev.enabled = false; a.prev.weight = 0f; }
            a.prev = a.cur; a.prevTime = a.cur != null ? a.cur.time : 0f;
            a.cur = next; a.mode = mode; a.blend = 0f;
            next.enabled = true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Step(m_L, dt);
            Step(m_R, dt);
            PlaceArms();
            UpdateFov();
        }

        void Step(Arm a, float dt)
        {
            if (a == null) return;
            float r = a.rate;
            switch (a.mode)
            {
                case Mode.Idle:
                    a.t = Mathf.Repeat((Time.time + a.idleOffset * 2f) * r, a.idle.length);
                    break;
                case Mode.Spray:
                    a.sprayLeft -= dt;
                    if (a.t < SprayPressEnd * r) a.t = Mathf.Min(SprayPressEnd * r, a.t + dt * 3f * r);   // polegar ao bico, rápido
                    else if (a.sprayLeft > 0f)
                    {
                        a.t += dt * r;                                                                     // a pintar
                        if (a.t > SprayHoldEnd * r) a.t = SprayHoldStart * r + (a.t - SprayHoldEnd * r);
                    }
                    else
                    {
                        if (a.t < SprayReleaseStart * r) a.t = SprayReleaseStart * r;                    // larga o bico
                        a.t += dt * 1.4f * r;
                        if (a.t >= a.spray.length) Switch(a, a.idle, Mode.Idle);
                    }
                    break;
                case Mode.Shake:
                    a.t += dt * a.shakeSpeed * r;
                    if (a.t >= a.shake.length) Switch(a, a.idle, Mode.Idle);
                    break;
            }
            a.cur.time = Mathf.Clamp(a.t, 0f, a.cur.length);
            a.blend = Mathf.MoveTowards(a.blend, 1f, dt / Blend);
            a.cur.enabled = true;
            a.cur.weight = a.blend;
            if (a.prev != null && a.prev != a.cur)
            {
                a.prev.time = a.prevTime;
                a.prev.weight = 1f - a.blend;
                a.prev.enabled = a.blend < 1f;
            }
        }

        /// <summary>
        /// A animação foi feita com um campo de visão mais fechado do que o do jogo: o referencial é achatado em
        /// profundidade (o truque habitual dos braços em FPS) para os braços ocuparem no ecrã o mesmo que no Blender.
        /// </summary>
        void UpdateFov()
        {
            if (m_Camera == null) return;
            float k = Mathf.Tan(AuthoredVerticalFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(10f, m_Camera.fieldOfView) * 0.5f * Mathf.Deg2Rad);
            m_Frame.localScale = new Vector3(1f, 1f, Mathf.Clamp(k, 0.3f, 1.5f));
        }

        void LateUpdate()
        {
            // As latas do jogo seguem as mãos (depois da animação deste frame). A rotação é tirada no referencial da
            // câmara sem o achatamento, e o braço direito vem espelhado (a lata continua uma lata normal, não espelhada).
            Place(m_CanL, m_L, false);
            Place(m_CanR, m_R, true);
        }

        void Place(Transform can, Arm a, bool mirrored)
        {
            if (can == null || a == null || a.can == null) return;
            var parent = m_Frame.parent;
            if (parent == null) return;
            var c = parent.InverseTransformPoint(a.can.position);
            var up = parent.InverseTransformPoint(a.canUp.position) - c;
            var fw = parent.InverseTransformPoint(a.canFwd.position) - c;
            // desfaz o achatamento (só nas direções; a posição fica no espaço achatado, onde estão as mãos)
            float k = m_Frame.localScale.z;
            if (k > 1e-3f) { up.z /= k; fw.z /= k; }
            if (up.sqrMagnitude < 1e-8f || fw.sqrMagnitude < 1e-8f) return;
            var rot = Quaternion.LookRotation(fw.normalized, up.normalized);
            if (can.parent != parent) can.SetParent(parent, true);
            can.localRotation = rot;
            can.localPosition = c - rot * Vector3.up * (CanOriginBelowCenter * can.localScale.y);
        }
    }
}
