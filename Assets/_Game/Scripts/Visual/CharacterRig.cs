using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Estado de movimento lido pela rig em cada frame.</summary>
    public struct RigFrame
    {
        public Vector3 worldVelocity;
        public bool grounded;
        public float pitch;
        public bool superReady;
    }

    /// <summary>
    /// Personagem em terceira pessoa construído com primitivas (corpo chibi: cabeça grande, olhos expressivos,
    /// acessório de cabeça e arma do estilo) e animado proceduralmente: idle, andar, correr, saltar, ataque por estilo,
    /// Super, reação a dano, morte e reaparecimento.
    /// Puramente visual: não tem colisores (o hitbox é o CharacterController do jogador).
    /// </summary>
    public class CharacterRig
    {
        const float HipHeight = 0.5f;

        public Transform Root { get; private set; }
        public RigStyle Style { get; private set; }

        Transform m_Body, m_Hips, m_Spine, m_Head, m_LegL, m_LegR, m_ArmL, m_ArmR, m_SocketL, m_SocketR;
        WeaponModel m_WeaponL, m_WeaponR;
        readonly List<Renderer> m_Renderers = new List<Renderer>();
        CharacterLook m_Look;
        float m_Scale = 1f;
        Color m_Accent = Color.white;
        ShadowCastingMode m_Shadows;

        // Skin (só visual) e especial em modo
        SkinEffect m_Effect;
        SkinAnimation m_AnimStyle;
        Color m_EffectColor = Color.white;
        float m_NextEffect;
        bool m_SuperMode;
        WeaponModel m_Minigun;
        readonly List<Renderer> m_MinigunRenderers = new List<Renderer>();
        float m_MinigunSpin;

        // Estado de animação
        float m_Phase, m_Speed, m_Air;
        float m_AttackT = 10f, m_AttackDur = 0.4f;
        bool m_AttackSuper;
        float m_KickL, m_KickR, m_BodyKick;
        float m_Hurt, m_Flash;
        float m_Draw = 1f;
        float m_HideHeldUntil;
        /// <summary>Dinamite: mão que lança a seguir (0 = direita) e a que está a lançar agora — alternam.</summary>
        int m_NextThrowSide, m_ThrowSide;
        float m_Pump;
        float m_Pop = 1f;
        float m_SuperPose;
        bool m_Dead;
        float m_DeadT;
        float m_Fade = 1f, m_AppliedFade = 1f;
        float m_AppliedFlash;
        bool m_Visible = true;
        Vector3 m_LastHeadPos;

        public bool IsDeadAnimDone => m_Dead && m_DeadT > 0.95f;
        public Vector3 HeadPosition => m_Head != null ? m_Head.position : Root.position + Vector3.up * 1.5f;
        public Vector3 ChestPosition => m_IsModel
            ? (m_Spine != null ? m_Spine.position : Root.position + Vector3.up * 1.1f)
            : (m_Spine != null ? m_Spine.position + Vector3.up * 0.3f * m_Scale : Root.position + Vector3.up * 1.1f);

        // ================================================================== construção

        public static CharacterRig Build(BrawlerDefinition b, Transform parent, ShadowCastingMode shadows, int layer, SkinDefinition skin = null)
        {
            var rig = new CharacterRig();
            // Modelo final (FBX com rig humanoide) se existir Resources/Characters/<brawlerId>; senão, personagem de primitivas.
            var model = b != null ? Resources.Load<GameObject>(ModelResourceFolder + b.brawlerId) : null;
            if (model != null && model.GetComponentInChildren<Animator>() != null) rig.CreateFromModel(b, model, parent, shadows, layer, skin);
            else rig.Create(b, parent, shadows, layer, skin);
            return rig;
        }

        // ================================================================== modelo animado (Animator)

        /// <summary>Pasta (em Resources) dos prefabs de personagens com modelo e Animator, um por brawlerId.</summary>
        public const string ModelResourceFolder = "Characters/";
        /// <summary>Altura de referência do personagem de primitivas (o modelo é escalado para esta altura).</summary>
        public const float ModelTargetHeight = 1.8f;
        /// <summary>Escala das armas de primitivas quando seguradas por um modelo (no boneco de primitivas é 1,45).</summary>
        const float ModelWeaponScale = 1.3f;

        Animator m_Anim;
        bool m_IsModel;
        ModelWeaponHolder m_Holder;
        readonly HashSet<int> m_AnimParams = new HashSet<int>();
        static readonly int AnimSpeed = Animator.StringToHash("Speed");
        static readonly int AnimGrounded = Animator.StringToHash("Grounded");
        static readonly int AnimDead = Animator.StringToHash("Dead");
        static readonly int AnimAttack = Animator.StringToHash("Attack");
        static readonly int AnimSuper = Animator.StringToHash("Super");
        static readonly int AnimHurt = Animator.StringToHash("Hurt");

        void CreateFromModel(BrawlerDefinition b, GameObject prefab, Transform parent, ShadowCastingMode shadows, int layer, SkinDefinition skin)
        {
            var tp = b.thirdPerson ?? new ThirdPersonPresentation();
            m_Look = skin != null ? skin.ApplyTo(tp.look) : (tp.look ?? new CharacterLook());
            Style = tp.rig;
            m_IsModel = true;
            var accentColor = skin != null && skin.overrideWeaponColor ? skin.weaponAccent : b.firstPerson.accentColor;
            m_Accent = accentColor;
            m_Shadows = shadows;
            m_Effect = skin != null ? skin.effect : SkinEffect.None;
            m_AnimStyle = skin != null ? skin.animation : SkinAnimation.Default;
            m_EffectColor = skin != null ? Color.Lerp(skin.primaryVfx, Color.white, 0.25f) : Color.white;

            Root = new GameObject("Rig_" + b.brawlerId + "_model").transform;
            Root.SetParent(parent, false);
            m_Body = Object.Instantiate(prefab, Root, false).transform;
            m_Body.name = "Model";
            m_Body.localPosition = Vector3.zero;
            m_Body.localRotation = Quaternion.identity;

            m_Anim = m_Body.GetComponentInChildren<Animator>();
            m_Anim.applyRootMotion = false;
            m_Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var p in m_Anim.parameters) m_AnimParams.Add(p.nameHash);

            if (m_Anim.isHuman)
            {
                m_Hips = m_Anim.GetBoneTransform(HumanBodyBones.Hips);
                m_Spine = m_Anim.GetBoneTransform(HumanBodyBones.Chest) ?? m_Anim.GetBoneTransform(HumanBodyBones.Spine);
                m_Head = m_Anim.GetBoneTransform(HumanBodyBones.Head);
            }
            m_Scale = 1f;

            if (skin != null && skin.overrideColors)
                foreach (var renderer in m_Body.GetComponentsInChildren<Renderer>(true))
                    Art.SetColor(renderer, skin.outfit);
            if (skin != null && skin.accessory != SkinAccessory.None && m_Head != null)
                WeaponModels.BuildAccessory(skin.accessory, m_Head, m_Spine, 0.12f, m_Look.accent, m_Look.gear, shadows);

            // ---------------- armas: as mesmas do boneco de primitivas, seguradas pelas mãos do modelo (IK)
            bool dual = WeaponModels.IsDual(Style);
            bool leftOnly = Style == RigStyle.Bow;
            if (!leftOnly)
            {
                m_SocketR = WeaponSocket("WeaponR");
                m_WeaponR = WeaponModels.Build(Style, m_SocketR, accentColor, 0, shadows);
            }
            if (dual || leftOnly)
            {
                m_SocketL = WeaponSocket("WeaponL");
                m_WeaponL = WeaponModels.Build(Style, m_SocketL, accentColor, 1, shadows);
            }
            if (m_Anim.isHuman)
            {
                m_Holder = m_Anim.gameObject.AddComponent<ModelWeaponHolder>();
                m_Holder.Setup(m_Anim, Root, m_SocketR, m_SocketL, Style);
            }

            foreach (var r in Root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = shadows;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            Root.GetComponentsInChildren(false, m_Renderers);
            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            m_LastHeadPos = HeadPosition;
        }

        Transform WeaponSocket(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(Root, false);
            t.localPosition = new Vector3(0f, 1.2f, 0.3f);
            t.localScale = Vector3.one * ModelWeaponScale;
            return t;
        }

        bool HasAnimParam(int hash) => m_AnimParams.Contains(hash);

        void ModelPose(float dt, RigFrame f)
        {
            if (m_Anim == null) return;
            var flat = new Vector3(f.worldVelocity.x, 0f, f.worldVelocity.z);
            if (HasAnimParam(AnimSpeed)) m_Anim.SetFloat(AnimSpeed, m_Dead ? 0f : flat.magnitude, 0.08f, dt);
            if (HasAnimParam(AnimGrounded)) m_Anim.SetBool(AnimGrounded, f.grounded || m_Dead);
            if (HasAnimParam(AnimDead)) m_Anim.SetBool(AnimDead, m_Dead);
            if (m_Holder != null)
            {
                m_Holder.pitch = f.pitch;
                m_Holder.holding = !m_Dead;
            }

            if (m_Dead)
            {
                m_DeadT = Mathf.Min(1.2f, m_DeadT + dt * 0.55f);   // ~1,8 s até o PlayerVisuals esconder o corpo
                float shrink = m_DeadT > 0.95f ? Mathf.Clamp01(1f - (m_DeadT - 0.95f) / 0.2f) : 1f;
                Root.localScale = Vector3.one * Mathf.Max(0.001f, shrink);
                return;
            }

            // reaparecer: pequeno "pop"
            if (m_Pop < 1f)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt * 3.2f);
                float p = m_Pop;
                float s = 1f + 0.18f * Mathf.Sin(p * Mathf.PI) * (1f - p) * 2f;
                Root.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, Mathf.Clamp01(p * 1.6f)) * s;
            }
            else Root.localScale = Vector3.one;

            if (m_SuperMode && m_Minigun != null)
            {
                m_MinigunSpin = Mathf.MoveTowards(m_MinigunSpin, 0f, dt * 1.5f);
                if (m_Minigun.drum != null) m_Minigun.drum.localRotation *= Quaternion.Euler(0f, 0f, (200f + 1600f * m_MinigunSpin) * dt);
            }

            // dano e coice: o corpo todo inclina-se para trás (a arma e as mãos acompanham)
            m_Hurt = Mathf.MoveTowards(m_Hurt, 0f, dt * 4f);
            m_BodyKick = Mathf.MoveTowards(m_BodyKick, 0f, dt * 6f);
            float hurtCurve = Mathf.Sin(Mathf.Clamp01(m_Hurt) * Mathf.PI * 0.5f);
            Root.localRotation = Quaternion.Euler(-11f * hurtCurve - 4f * m_BodyKick, 6f * hurtCurve, 0f);

            // explosivos e arco: a arma "na mão" desaparece por instantes depois de lançar
            // (dinamite: só a mão que lançou fica vazia)
            bool held = Time.time >= m_HideHeldUntil;
            bool tnt = Style == RigStyle.DualExplosives;
            SetHeldVisible(m_WeaponR, held || (tnt && m_ThrowSide != 0));
            SetHeldVisible(m_WeaponL, held || (tnt && m_ThrowSide == 0));
        }

        void Create(BrawlerDefinition b, Transform parent, ShadowCastingMode shadows, int layer, SkinDefinition skin)
        {
            var tp = b != null ? b.thirdPerson : new ThirdPersonPresentation();
            // A skin só muda o aspeto: cores, acessórios, arma, partículas e estilo de animação
            m_Look = skin != null ? skin.ApplyTo(tp.look) : (tp.look ?? new CharacterLook());
            Style = tp.rig;
            var L = m_Look;
            var accentColor = skin != null && skin.overrideWeaponColor ? skin.weaponAccent : (b != null ? b.firstPerson.accentColor : Color.white);
            m_Accent = accentColor;
            m_Shadows = shadows;
            m_Effect = skin != null ? skin.effect : SkinEffect.None;
            m_AnimStyle = skin != null ? skin.animation : SkinAnimation.Default;
            m_EffectColor = skin != null ? Color.Lerp(skin.primaryVfx, Color.white, 0.25f) : Color.white;
            // Proporções chibi: cabeça grande, pernas curtas, tronco largo
            float hs = Mathf.Clamp(L.headScale, 0.6f, 1.6f) * 1.15f;
            float bulk = Mathf.Clamp(L.bulk, 0.6f, 1.8f);
            m_Scale = Mathf.Clamp(L.bodyScale, 0.7f, 1.4f);

            Root = new GameObject("Rig_" + (b != null ? b.brawlerId : "generic")).transform;
            Root.SetParent(parent, false);
            Root.gameObject.layer = layer;

            m_Body = Node("Body", Root, Vector3.zero);
            m_Body.localScale = Vector3.one * m_Scale;

            // ---------------- ancas e pernas
            m_Hips = Node("Hips", m_Body, new Vector3(0f, HipHeight, 0f));
            Part(PrimitiveType.Cube, m_Hips, new Vector3(0f, 0f, 0f), new Vector3(0.48f * bulk, 0.22f, 0.32f), L.pants, shadows);
            m_LegL = Node("LegL", m_Hips, new Vector3(-0.14f * bulk, -0.02f, 0f));
            m_LegR = Node("LegR", m_Hips, new Vector3(0.14f * bulk, -0.02f, 0f));
            foreach (var leg in new[] { m_LegL, m_LegR })
            {
                Part(PrimitiveType.Capsule, leg, new Vector3(0f, -0.2f, 0f), new Vector3(0.24f, 0.2f, 0.24f), L.pants, shadows);
                Part(PrimitiveType.Cube, leg, new Vector3(0f, -0.43f, 0.05f), new Vector3(0.25f, 0.13f, 0.36f), Art.Darken(L.gear, 0.7f), shadows);
                Part(PrimitiveType.Cube, leg, new Vector3(0f, -0.37f, 0.02f), new Vector3(0.27f, 0.05f, 0.3f), Art.Darken(L.gear, 0.5f), ShadowCastingMode.Off);
            }

            // ---------------- tronco
            m_Spine = Node("Spine", m_Hips, new Vector3(0f, 0.04f, 0f));
            Part(PrimitiveType.Sphere, m_Spine, new Vector3(0f, 0.3f, 0f), new Vector3(0.68f * bulk, 0.66f, 0.5f), L.outfit, shadows);
            Part(PrimitiveType.Cylinder, m_Spine, new Vector3(0f, 0.08f, 0f), new Vector3(0.6f * bulk, 0.04f, 0.46f), Art.Darken(L.gear, 0.6f), shadows);
            Part(PrimitiveType.Cube, m_Spine, new Vector3(0f, 0.08f, 0.235f), new Vector3(0.12f, 0.09f, 0.04f), new Color(1f, 0.82f, 0.3f), ShadowCastingMode.Off);
            // Lenço ao pescoço (cor de destaque)
            Part(PrimitiveType.Sphere, m_Spine, new Vector3(0f, 0.55f, 0.05f), new Vector3(0.4f * bulk, 0.13f, 0.36f), L.accent, shadows);
            BuildBackAccessory(m_Spine, L, accentColor, shadows, bulk);

            // ---------------- cabeça
            m_Head = Node("Head", m_Spine, new Vector3(0f, 0.56f, 0f));
            Part(PrimitiveType.Sphere, m_Head, new Vector3(0f, 0.28f * hs, 0f), Vector3.one * 0.6f * hs, L.skin, shadows);
            BuildFace(m_Head, L, hs, shadows);
            BuildHeadGear(m_Head, L, hs, shadows);
            if (skin != null && skin.accessory != SkinAccessory.None)
                WeaponModels.BuildAccessory(skin.accessory, m_Head, m_Spine, 0.5f * hs, L.accent, L.gear, shadows);

            // ---------------- braços
            m_ArmL = Node("ArmL", m_Spine, new Vector3(-0.36f * bulk, 0.42f, 0f));
            m_ArmR = Node("ArmR", m_Spine, new Vector3(0.36f * bulk, 0.42f, 0f));
            m_SocketL = BuildArm(m_ArmL, L, shadows);
            m_SocketR = BuildArm(m_ArmR, L, shadows);

            // ---------------- armas
            bool dual = WeaponModels.IsDual(Style);
            if (Style == RigStyle.Bow)
            {
                m_WeaponL = WeaponModels.Build(Style, m_SocketL, accentColor, 1, shadows);
            }
            else
            {
                m_WeaponR = WeaponModels.Build(Style, m_SocketR, accentColor, 0, shadows);
                if (dual) m_WeaponL = WeaponModels.Build(Style, m_SocketL, accentColor, 1, shadows);
            }

            MergeStaticParts();
            Root.GetComponentsInChildren(false, m_Renderers);
            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            m_LastHeadPos = HeadPosition;
            Pose(0f, default);
        }

        /// <summary>Junta as peças de cada osso numa só malha (menos draw calls). As partes animadas ficam separadas.</summary>
        void MergeStaticParts()
        {
            if (!Art.SupportsVertexColor) return;
            var keep = new HashSet<Transform> { m_Body, m_Hips, m_LegL, m_LegR, m_Spine, m_Head, m_ArmL, m_ArmR, m_SocketL, m_SocketR };
            var weapons = new[] { m_WeaponL, m_WeaponR };
            foreach (var w in weapons)
            {
                if (w == null) continue;
                foreach (var t in new[] { w.drum, w.pump, w.nock, w.arrows, w.stringTop, w.stringBottom, w.scope })
                    if (t != null) keep.Add(t);
            }

            var plain = Art.ToonVC;
            var outlined = Art.ToonOutlineVC;
            foreach (var bone in new[] { m_Hips, m_LegL, m_LegR, m_Spine, m_Head, m_ArmL, m_ArmR })
                MeshMerge.MergeInto(bone, keep, plain, outlined);
            foreach (var w in weapons)
            {
                if (w == null) continue;
                MeshMerge.MergeInto(w.root, keep, plain, outlined);
                foreach (var t in new[] { w.drum, w.pump, w.arrows, w.scope })
                    if (t != null) MeshMerge.MergeInto(t, keep, plain, outlined);
            }
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, ShadowCastingMode s, Vector3 euler = default, bool outline = true)
        {
            return Art.Part(type, parent, pos, scale, c, outline, euler, s);
        }

        Transform BuildArm(Transform shoulder, CharacterLook L, ShadowCastingMode s)
        {
            Part(PrimitiveType.Sphere, shoulder, new Vector3(0f, -0.02f, 0f), Vector3.one * 0.24f, L.outfit, s);
            Part(PrimitiveType.Capsule, shoulder, new Vector3(0f, -0.21f, 0f), new Vector3(0.16f, 0.2f, 0.16f), L.skin, s);
            Part(PrimitiveType.Sphere, shoulder, new Vector3(0f, -0.43f, 0f), Vector3.one * 0.19f, Art.Darken(L.gear, 0.85f), s);
            var socket = Node("Socket", shoulder, new Vector3(0f, -0.45f, 0f));
            socket.localRotation = Quaternion.Euler(90f, 0f, 0f);
            socket.localScale = Vector3.one * 1.45f; // armas maiores = mais legíveis em terceira pessoa
            return socket;
        }

        void BuildFace(Transform head, CharacterLook L, float hs, ShadowCastingMode s)
        {
            var white = new Color(0.98f, 0.98f, 0.98f);
            var dark = new Color(0.1f, 0.08f, 0.1f);
            bool masked = L.headGear == HeadGear.GasMask;
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = new Vector3(side * 0.105f, 0.31f, 0.245f) * hs;
                Part(PrimitiveType.Sphere, head, eye, new Vector3(0.13f, 0.16f, 0.08f) * hs, masked ? new Color(0.65f, 1f, 0.75f) : white, ShadowCastingMode.Off, default, false);
                Part(PrimitiveType.Sphere, head, eye + new Vector3(side * -0.005f, -0.005f, 0.035f) * hs, new Vector3(0.06f, 0.085f, 0.04f) * hs, dark, ShadowCastingMode.Off, default, false);
                Part(PrimitiveType.Sphere, head, eye + new Vector3(side * -0.018f, 0.03f, 0.05f) * hs, Vector3.one * 0.022f * hs, white, ShadowCastingMode.Off, default, false);
                // Sobrancelhas grossas e inclinadas (ar determinado)
                Part(PrimitiveType.Cube, head, new Vector3(side * 0.11f, 0.42f, 0.255f) * hs, new Vector3(0.12f, 0.03f, 0.04f) * hs, Art.Darken(L.hair, 0.8f), ShadowCastingMode.Off, new Vector3(0f, 0f, side * -12f), false);
            }
            if (!masked)
            {
                Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.24f, 0.3f) * hs, new Vector3(0.07f, 0.06f, 0.06f) * hs, Art.Darken(L.skin, 0.92f), ShadowCastingMode.Off, default, false);
                Part(PrimitiveType.Cube, head, new Vector3(0.02f, 0.15f, 0.27f) * hs, new Vector3(0.12f, 0.025f, 0.03f) * hs, new Color(0.45f, 0.15f, 0.15f), ShadowCastingMode.Off, new Vector3(0f, 0f, -8f), false);
            }
        }

        void BuildHeadGear(Transform head, CharacterLook L, float hs, ShadowCastingMode s)
        {
            float top = 0.28f * hs;
            switch (L.headGear)
            {
                case HeadGear.WideHat:
                {
                    var hat = Node("Hat", head, new Vector3(0f, top + 0.2f * hs, -0.02f));
                    hat.localRotation = Quaternion.Euler(-8f, 0f, 4f);
                    Part(PrimitiveType.Cylinder, hat, Vector3.zero, new Vector3(1.0f, 0.022f, 0.92f) * hs, L.gear, s);
                    Part(PrimitiveType.Cylinder, hat, new Vector3(0f, 0.11f, 0f) * hs, new Vector3(0.48f, 0.11f, 0.46f) * hs, L.gear, s);
                    Part(PrimitiveType.Sphere, hat, new Vector3(0f, 0.22f, 0f) * hs, new Vector3(0.46f, 0.08f, 0.44f) * hs, Art.Darken(L.gear, 0.9f), s);
                    Part(PrimitiveType.Cylinder, hat, new Vector3(0f, 0.04f, 0f) * hs, new Vector3(0.5f, 0.03f, 0.48f) * hs, L.accent, ShadowCastingMode.Off);
                    // Abas levantadas dos lados
                    Part(PrimitiveType.Cube, hat, new Vector3(0.44f, 0.05f, 0f) * hs, new Vector3(0.12f, 0.02f, 0.6f) * hs, L.gear, s, new Vector3(0f, 0f, 35f));
                    Part(PrimitiveType.Cube, hat, new Vector3(-0.44f, 0.05f, 0f) * hs, new Vector3(0.12f, 0.02f, 0.6f) * hs, L.gear, s, new Vector3(0f, 0f, -35f));
                    break;
                }
                case HeadGear.Goggles:
                {
                    // Cabelo espetado
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (i - 2.5f) * 24f;
                        var dir = Quaternion.Euler(-25f + Mathf.Abs(i - 2.5f) * 6f, a, 0f) * Vector3.up;
                        Part(PrimitiveType.Capsule, head, new Vector3(0f, top, -0.04f * hs) + dir * 0.28f * hs, new Vector3(0.15f, 0.2f, 0.15f) * hs, L.hair, s, Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                    }
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.08f * hs, -0.03f * hs), new Vector3(0.6f, 0.4f, 0.58f) * hs, L.hair, s);
                    // Óculos na testa
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top + 0.1f * hs, 0f), new Vector3(0.62f, 0.035f, 0.62f) * hs, Art.Darken(L.gear, 0.6f), ShadowCastingMode.Off, new Vector3(-12f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.11f * hs, top + 0.13f * hs, 0.27f * hs), new Vector3(0.16f, 0.04f, 0.16f) * hs, L.gear, s, new Vector3(70f, 0f, 0f));
                        var lens = Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.11f * hs, top + 0.135f * hs, 0.31f * hs), new Vector3(0.12f, 0.01f, 0.12f) * hs, new Color(0.55f, 0.85f, 1f), ShadowCastingMode.Off, new Vector3(70f, 0f, 0f), false);
                        Art.SetEmission(lens.GetComponent<Renderer>(), new Color(0.15f, 0.25f, 0.3f));
                    }
                    break;
                }
                case HeadGear.FoxHood:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.04f * hs, -0.06f * hs), new Vector3(0.68f, 0.66f, 0.64f) * hs, L.gear, s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.18f * hs, top + 0.33f * hs, -0.04f * hs), new Vector3(0.17f, 0.17f, 0.06f) * hs, L.gear, s, new Vector3(0f, 0f, 45f + side * 12f));
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.18f * hs, top + 0.32f * hs, -0.01f * hs), new Vector3(0.09f, 0.09f, 0.03f) * hs, L.accent, ShadowCastingMode.Off, new Vector3(0f, 0f, 45f + side * 12f), false);
                    }
                    // Máscara de pano na parte de baixo da cara
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.17f * hs, 0.12f * hs), new Vector3(0.56f, 0.24f, 0.42f) * hs, L.accent, s);
                    break;
                }
                case HeadGear.GasMask:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.17f * hs, 0.24f * hs), new Vector3(0.3f, 0.24f, 0.2f) * hs, new Color(0.25f, 0.27f, 0.3f), s);
                    for (int side = -1; side <= 1; side += 2)
                        Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.16f * hs, 0.1f * hs, 0.26f * hs), new Vector3(0.12f, 0.05f, 0.12f) * hs, L.accent, s, new Vector3(70f, side * 30f, 0f));
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.1f * hs, -0.02f * hs), new Vector3(0.64f, 0.46f, 0.64f) * hs, L.gear, s);
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top - 0.02f * hs, 0f), new Vector3(0.65f, 0.05f, 0.65f) * hs, Art.Darken(L.gear, 0.75f), s);
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.36f * hs, -0.02f * hs), Vector3.one * 0.15f * hs, L.accent, s);
                    // Madeixas a sair do gorro
                    Part(PrimitiveType.Capsule, head, new Vector3(0.24f * hs, 0.18f * hs, -0.12f * hs), new Vector3(0.12f, 0.16f, 0.12f) * hs, L.hair, s, new Vector3(0f, 0f, 15f));
                    Part(PrimitiveType.Capsule, head, new Vector3(-0.24f * hs, 0.18f * hs, -0.12f * hs), new Vector3(0.12f, 0.16f, 0.12f) * hs, L.hair, s, new Vector3(0f, 0f, -15f));
                    break;
                }
                case HeadGear.Bandana:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.06f * hs, -0.04f * hs), new Vector3(0.62f, 0.5f, 0.62f) * hs, L.hair, s);
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top + 0.12f * hs, 0f), new Vector3(0.64f, 0.06f, 0.64f) * hs, L.accent, s, new Vector3(-10f, 0f, 0f));
                    Part(PrimitiveType.Cube, head, new Vector3(0.05f * hs, top + 0.08f * hs, -0.33f * hs), new Vector3(0.08f, 0.18f, 0.04f) * hs, L.accent, s, new Vector3(-20f, 0f, 25f));
                    Part(PrimitiveType.Capsule, head, new Vector3(0f, top - 0.05f * hs, -0.36f * hs), new Vector3(0.16f, 0.22f, 0.16f) * hs, L.hair, s, new Vector3(-30f, 0f, 0f));
                    break;
                }
                case HeadGear.EarCap:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.1f * hs, -0.02f * hs), new Vector3(0.64f, 0.42f, 0.64f) * hs, L.gear, s);
                    Part(PrimitiveType.Cube, head, new Vector3(0f, top + 0.06f * hs, 0.32f * hs), new Vector3(0.42f, 0.03f, 0.26f) * hs, Art.Darken(L.gear, 0.8f), s, new Vector3(-8f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.2f * hs, top + 0.3f * hs, -0.02f * hs), new Vector3(0.1f, 0.2f, 0.05f) * hs, L.gear, s, new Vector3(0f, 0f, side * -20f));
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.23f * hs, top + 0.41f * hs, -0.02f * hs), new Vector3(0.07f, 0.07f, 0.03f) * hs, L.hair, ShadowCastingMode.Off, new Vector3(0f, 0f, 45f + side * 15f), false);
                    }
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.2f * hs, 0.2f * hs), Vector3.one * 0.09f * hs, L.accent, ShadowCastingMode.Off);
                    break;
                }
                default:
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.06f * hs, -0.05f * hs), new Vector3(0.62f, 0.46f, 0.6f) * hs, L.hair, s);
                    break;
            }
        }

        void BuildBackAccessory(Transform spine, CharacterLook L, Color accent, ShadowCastingMode s, float bulk)
        {
            switch (Style)
            {
                case RigStyle.Bow:
                    // Aljava com flechas
                    var q = Node("Quiver", spine, new Vector3(0.12f, 0.38f, -0.24f));
                    q.localRotation = Quaternion.Euler(0f, 0f, -20f);
                    Part(PrimitiveType.Cylinder, q, Vector3.zero, new Vector3(0.14f, 0.2f, 0.14f), Art.Darken(L.gear, 0.8f), s);
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Sphere, q, new Vector3((i - 1) * 0.035f, 0.24f, 0f), new Vector3(0.04f, 0.07f, 0.04f), accent, ShadowCastingMode.Off);
                    break;
                case RigStyle.DualExplosives:
                    // Cinto com dinamites ao peito
                    var belt = Node("Bandolier", spine, new Vector3(0f, 0.32f, 0.18f));
                    belt.localRotation = Quaternion.Euler(0f, 0f, 35f);
                    Part(PrimitiveType.Cube, belt, Vector3.zero, new Vector3(0.09f, 0.62f, 0.05f), Art.Darken(L.gear, 0.6f), ShadowCastingMode.Off);
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Cylinder, belt, new Vector3(0f, (i - 1) * 0.15f, 0.04f), new Vector3(0.06f, 0.06f, 0.06f), new Color(0.9f, 0.2f, 0.15f), ShadowCastingMode.Off);
                    break;
                case RigStyle.SprayCan:
                case RigStyle.DualSprayCans:
                    // Depósito às costas
                    var tank = Node("Tank", spine, new Vector3(0f, 0.32f, -0.28f));
                    Part(PrimitiveType.Capsule, tank, Vector3.zero, new Vector3(0.24f, 0.22f, 0.24f), accent, s);
                    Part(PrimitiveType.Sphere, tank, new Vector3(0f, 0.24f, 0f), Vector3.one * 0.1f, new Color(0.85f, 0.88f, 0.92f), ShadowCastingMode.Off);
                    break;
                case RigStyle.Sniper:
                    // Mochila pequena
                    Part(PrimitiveType.Cube, spine, new Vector3(0f, 0.3f, -0.26f), new Vector3(0.36f * bulk, 0.34f, 0.16f), Art.Darken(L.gear, 0.9f), s);
                    break;
                case RigStyle.Shotgun:
                    // Cartucheira
                    var sh = Node("Shells", spine, new Vector3(0f, 0.3f, 0.19f));
                    sh.localRotation = Quaternion.Euler(0f, 0f, -35f);
                    Part(PrimitiveType.Cube, sh, Vector3.zero, new Vector3(0.08f, 0.6f, 0.05f), Art.Darken(L.gear, 0.6f), ShadowCastingMode.Off);
                    for (int i = 0; i < 4; i++)
                        Part(PrimitiveType.Capsule, sh, new Vector3(0f, (i - 1.5f) * 0.12f, 0.035f), new Vector3(0.04f, 0.04f, 0.04f), new Color(0.95f, 0.3f, 0.2f), ShadowCastingMode.Off);
                    break;
            }
        }

        // ================================================================== eventos

        public Vector3 GetMuzzle(int shotIndex)
        {
            if (m_SuperMode && m_Minigun != null) return m_Minigun.Muzzle(0);
            bool dual = m_WeaponL != null && m_WeaponR != null;
            WeaponModel w;
            // dinamite: o 1.º disparo sai antes de OnAttack, por isso usa a mão "seguinte"
            if (dual && Style == RigStyle.DualExplosives) shotIndex += m_NextThrowSide;
            if (dual) w = (Mathf.Abs(shotIndex) % 2 == 0) ? m_WeaponR : m_WeaponL;
            else w = m_WeaponR ?? m_WeaponL;
            if (w == null)
            {
                if (m_IsModel && m_SocketR != null) return m_SocketR.position + Root.forward * 0.25f;
                return Root.position + Vector3.up * 1.3f;
            }
            return w.Muzzle(0);
        }

        /// <summary>Início de um ataque (principal ou Super).</summary>
        public void OnAttack(bool super, float duration)
        {
            if (m_IsModel && m_Anim != null)
            {
                if (super && HasAnimParam(AnimSuper)) m_Anim.SetTrigger(AnimSuper);
                else if (HasAnimParam(AnimAttack)) m_Anim.SetTrigger(AnimAttack);
                if (m_Holder != null) m_Holder.Kick(super ? 1.6f : 1f, -1);
            }
            m_AttackT = 0f;
            m_AttackDur = Mathf.Max(0.15f, duration);
            m_AttackSuper = super;
            if (super) m_SuperPose = 1f;
            switch (Style)
            {
                case RigStyle.Bow:
                    m_Draw = 0f;
                    m_HideHeldUntil = Time.time + 0.35f;
                    break;
                case RigStyle.DualExplosives:
                    m_HideHeldUntil = Time.time + 0.7f;
                    m_ThrowSide = m_NextThrowSide;
                    m_NextThrowSide ^= 1;
                    break;
                case RigStyle.Shotgun:
                case RigStyle.Sniper:
                    m_BodyKick = 1f;
                    m_Pump = 0f;
                    break;
            }
        }

        /// <summary>Cada disparo de uma rajada (alterna as mãos nos estilos duplos).</summary>
        public void OnShot(int shotIndex)
        {
            if (m_SuperMode && m_Minigun != null)
            {
                m_KickR = 0.6f;
                m_MinigunSpin = 1f;
                return;
            }
            bool leftShot = m_WeaponL != null && m_WeaponR != null && Mathf.Abs(shotIndex) % 2 == 1;
            if (leftShot) m_KickL = 1f;
            else m_KickR = 1f;
            if (m_Holder != null) m_Holder.Kick(0.8f, leftShot ? 1 : 0);
            if (Style == RigStyle.Shotgun || Style == RigStyle.Sniper) m_BodyKick = 1f;
        }

        /// <summary>
        /// Especial em modo: o Ekko troca as duas metralhadoras pela Minigun (segura com as duas mãos); a Lince mostra a
        /// mira a brilhar enquanto o tiro fatal está armado.
        /// </summary>
        public void SetSuperMode(bool on)
        {
            if (on == m_SuperMode || Root == null) return;
            m_SuperMode = on;
            if (Style == RigStyle.DualSMG)
            {
                if (on)
                {
                    m_Minigun = WeaponModels.BuildMinigun(m_SocketR, m_Accent, m_Shadows);
                    m_Minigun.root.localPosition = new Vector3(0.06f, 0.02f, 0.05f);
                    m_Minigun.root.localScale = Vector3.one * 0.85f;
                    foreach (var t in m_Minigun.root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Root.gameObject.layer;
                    m_Minigun.root.GetComponentsInChildren(true, m_MinigunRenderers);
                    foreach (var r in m_MinigunRenderers)
                    {
                        r.shadowCastingMode = m_Renderers.Count > 0 && m_Renderers[0] != null ? m_Renderers[0].shadowCastingMode : m_Shadows;
                        r.enabled = m_Visible;
                    }
                    m_Renderers.AddRange(m_MinigunRenderers);
                    m_SuperPose = 1f;
                }
                else if (m_Minigun != null)
                {
                    foreach (var r in m_MinigunRenderers) m_Renderers.Remove(r);
                    m_MinigunRenderers.Clear();
                    Object.Destroy(m_Minigun.root.gameObject);
                    m_Minigun = null;
                }
                SetWeaponVisible(m_WeaponL, !on);
                SetWeaponVisible(m_WeaponR, !on);
            }
            else if (Style == RigStyle.Sniper && m_WeaponR != null)
            {
                foreach (var g in m_WeaponR.glowRenderers) Art.SetEmission(g, on ? new Color(1f, 0.15f, 0.1f) : Color.black);
            }
        }

        static void SetWeaponVisible(WeaponModel w, bool visible)
        {
            if (w != null && w.root != null && w.root.gameObject.activeSelf != visible) w.root.gameObject.SetActive(visible);
        }

        public void OnHurt()
        {
            m_Hurt = 1f;
            m_Flash = 1f;
            if (m_IsModel && m_Anim != null && HasAnimParam(AnimHurt)) m_Anim.SetTrigger(AnimHurt);
        }

        public void SetDead(bool dead, bool instant)
        {
            if (dead == m_Dead) return;
            m_Dead = dead;
            m_DeadT = instant ? 1f : 0f;
            if (!dead)
            {
                m_Pop = instant ? 1f : 0f;
                Root.localRotation = Quaternion.identity;
                Root.localPosition = Vector3.zero;
                Root.localScale = Vector3.one;
                if (m_IsModel && m_Anim != null)
                {
                    if (HasAnimParam(AnimDead)) m_Anim.SetBool(AnimDead, false);
                    m_Anim.Rebind();
                    m_Anim.Update(0f);
                }
            }
            if (m_Holder != null) m_Holder.holding = !dead;
        }

        public void SetVisible(bool visible)
        {
            if (visible == m_Visible) return;
            m_Visible = visible;
            foreach (var r in m_Renderers) if (r != null) r.enabled = visible;
        }

        /// <summary>Opacidade em dithering (escondido num arbusto).</summary>
        public void SetFade(float fade) => m_Fade = Mathf.Clamp01(fade);

        public void SetShadowsOnly(bool shadowsOnly)
        {
            foreach (var r in m_Renderers)
                if (r != null) r.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }

        // ================================================================== animação

        public void Tick(float dt, RigFrame f)
        {
            if (Root == null) return;
            if (m_IsModel) ModelPose(dt, f);
            else Pose(dt, f);
            ApplyMaterialState(dt);
            EmitSkinEffect(dt);
        }

        /// <summary>Partículas contínuas da skin (brilhos, brasas, neve, folhas, bolhas) — poucas e só quando visível.</summary>
        void EmitSkinEffect(float dt)
        {
            if (m_Effect == SkinEffect.None || dt <= 0f || !m_Visible || m_Fade < 0.5f || m_Dead) return;
            if (Time.time < m_NextEffect) return;
            m_NextEffect = Time.time + 0.16f;
            var p = ChestPosition + Random.insideUnitSphere * 0.45f * m_Scale;
            switch (m_Effect)
            {
                case SkinEffect.Sparkles:
                    Fx.Glow(p, Random.insideUnitSphere * 0.3f, Color.Lerp(m_EffectColor, Color.white, 0.4f), 0.07f, 0.5f);
                    break;
                case SkinEffect.Embers:
                    Fx.Glow(p - Vector3.up * 0.3f, Vector3.up * Random.Range(0.6f, 1.4f), new Color(1f, 0.5f, 0.15f), 0.08f, 0.6f);
                    break;
                case SkinEffect.Snowflakes:
                    Fx.Glow(p + Vector3.up * 0.6f, Vector3.down * 0.4f + Random.insideUnitSphere * 0.2f, new Color(0.9f, 0.95f, 1f), 0.06f, 0.9f);
                    break;
                case SkinEffect.Leaves:
                    Fx.Puff(p, Vector3.down * 0.3f + Random.insideUnitSphere * 0.3f, new Color(0.45f, 0.8f, 0.3f, 0.8f), 0.12f, 0.7f);
                    break;
                case SkinEffect.Bubbles:
                    Fx.Glow(p - Vector3.up * 0.2f, Vector3.up * Random.Range(0.4f, 0.9f), Color.Lerp(m_EffectColor, new Color(0.6f, 1f, 0.9f), 0.5f), 0.09f, 0.8f);
                    break;
            }
        }

        void Pose(float dt, RigFrame f)
        {
            float time = Time.time;

            // ---------------- morte
            if (m_Dead)
            {
                m_DeadT = Mathf.Min(1.2f, m_DeadT + dt * 1.3f);
                float k = Mathf.Clamp01(m_DeadT / 0.45f);
                float fall = 1f - (1f - k) * (1f - k);
                Root.localRotation = Quaternion.Euler(-80f * fall, 0f, 12f * fall);
                Root.localPosition = new Vector3(0f, 0.12f * Mathf.Sin(k * Mathf.PI), -0.3f * fall);
                m_ArmL.localRotation = Quaternion.Euler(-160f * fall, 0f, -20f);
                m_ArmR.localRotation = Quaternion.Euler(-160f * fall, 0f, 20f);
                m_LegL.localRotation = Quaternion.Euler(-30f * fall, 0f, 0f);
                m_LegR.localRotation = Quaternion.Euler(10f * fall, 0f, 0f);
                float shrink = m_DeadT > 0.8f ? Mathf.Clamp01(1f - (m_DeadT - 0.8f) / 0.2f) : 1f;
                m_Body.localScale = Vector3.one * m_Scale * Mathf.Max(0.001f, shrink);
                return;
            }

            // ---------------- reaparecer (pop com overshoot)
            if (m_Pop < 1f)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt * 3.2f);
                float p = m_Pop;
                float s = 1f + Mathf.Sin(p * Mathf.PI) * 0.25f * (1f - p);
                m_Body.localScale = Vector3.one * m_Scale * Mathf.Max(0.01f, Mathf.SmoothStep(0f, 1f, p) * s);
            }
            else
            {
                float superPop = 1f + m_SuperPose * 0.12f;
                m_Body.localScale = Vector3.one * m_Scale * superPop;
            }

            // ---------------- locomoção
            var localVel = Root.InverseTransformDirection(f.worldVelocity);
            localVel.y = 0f;
            float speed = localVel.magnitude;
            m_Speed = Mathf.Lerp(m_Speed, speed, 1f - Mathf.Exp(-10f * dt));
            m_Air = f.grounded ? Mathf.MoveTowards(m_Air, 0f, dt * 8f) : Mathf.MoveTowards(m_Air, 1f, dt * 5f);
            float move01 = Mathf.Clamp01(m_Speed / 6f);
            m_Phase += dt * (4f + m_Speed * 1.35f) * (f.grounded ? 1f : 0.2f);
            float swing = Mathf.Sin(m_Phase);
            float bob = Mathf.Abs(Mathf.Cos(m_Phase));

            var dirLocal = speed > 0.1f ? localVel / speed : Vector3.forward;
            float fwd = dirLocal.z, side = dirLocal.x;

            float legAmp = 38f * move01;
            float legL = swing * legAmp * Mathf.Sign(fwd >= -0.2f ? 1f : -1f);
            float legSideL = swing * 18f * move01 * side;
            m_LegL.localRotation = Quaternion.Euler(Mathf.Lerp(legL, -38f, m_Air), 0f, Mathf.Lerp(legSideL, -6f, m_Air));
            m_LegR.localRotation = Quaternion.Euler(Mathf.Lerp(-legL, 22f, m_Air), 0f, Mathf.Lerp(-legSideL, 6f, m_Air));

            float breathe = Mathf.Sin(time * 2.2f) * 0.012f * (1f - move01);
            m_Hips.localPosition = new Vector3(0f, HipHeight + bob * 0.045f * move01 + breathe + m_Air * 0.04f, 0f);
            m_Hips.localRotation = Quaternion.Euler(0f, swing * 8f * move01, 0f);
            // Estilo de animação da skin (só amplitude/ritmo)
            if (m_AnimStyle == SkinAnimation.Bouncy)
                m_Hips.localPosition += Vector3.up * (Mathf.Abs(Mathf.Sin(time * 5f)) * 0.035f * (1f - move01) + bob * 0.03f * move01);
            else if (m_AnimStyle == SkinAnimation.Floaty)
                m_Body.localPosition = Vector3.up * (0.06f + Mathf.Sin(time * 2f) * 0.03f) * (1f - m_Air);

            // ---------------- tronco e cabeça (seguem a pontaria)
            float pitch = Mathf.Clamp(f.pitch, -70f, 70f);
            m_Hurt = Mathf.MoveTowards(m_Hurt, 0f, dt * 5f);
            m_BodyKick = Mathf.MoveTowards(m_BodyKick, 0f, dt * 6f);
            m_SuperPose = Mathf.MoveTowards(m_SuperPose, 0f, dt * 2.2f);
            float lean = 9f * move01 * Mathf.Max(0f, fwd) - m_Hurt * 14f - m_BodyKick * 7f;
            float spinePitch = lean + pitch * 0.25f;
            m_Spine.localRotation = Quaternion.Euler(spinePitch, -swing * 6f * move01, -side * 5f * move01 + m_Hurt * 6f);
            if (m_AnimStyle == SkinAnimation.Swagger)
                m_Spine.localRotation *= Quaternion.Euler(0f, Mathf.Sin(time * 2.4f) * 7f * (1f - move01 * 0.5f), Mathf.Sin(time * 1.2f) * 3f);
            m_Head.localRotation = Quaternion.Euler(pitch * 0.45f - m_Hurt * 10f, 0f, Mathf.Sin(time * 1.3f) * 2f * (1f - move01));

            // ---------------- braços por estilo
            m_AttackT += dt;
            float atk = Mathf.Clamp01(m_AttackT / m_AttackDur);
            bool attacking = m_AttackT < m_AttackDur;
            m_KickL = Mathf.MoveTowards(m_KickL, 0f, dt * 9f);
            m_KickR = Mathf.MoveTowards(m_KickR, 0f, dt * 9f);
            // Os braços são filhos do tronco: compensa a inclinação para apontarem para onde o jogador olha
            float aim = -90f + pitch - spinePitch;
            float idleArm = Mathf.Sin(time * 2f) * 2f;
            float superRaise = -45f * m_SuperPose * (1f - atk);

            if (m_SuperMode && m_Minigun != null)
            {
                // Minigun às duas mãos: braço esquerdo a apoiar por baixo, tremer ao disparar
                m_MinigunSpin = Mathf.MoveTowards(m_MinigunSpin, 0f, dt * 1.5f);
                float shake = m_MinigunSpin > 0f ? Mathf.Sin(time * 70f) * 2.5f * m_MinigunSpin : 0f;
                m_ArmR.localRotation = Quaternion.Euler(aim + 12f - 8f * m_KickR + shake, -20f, 0f);
                m_ArmL.localRotation = Quaternion.Euler(aim + 22f + shake, 42f, 0f);
                if (m_Minigun.drum != null) m_Minigun.drum.localRotation *= Quaternion.Euler(0f, 0f, (200f + 1600f * m_MinigunSpin) * dt);
                return;
            }

            switch (Style)
            {
                case RigStyle.DualRevolvers:
                case RigStyle.DualSMG:
                    m_ArmR.localRotation = Quaternion.Euler(aim - 18f * m_KickR + idleArm + superRaise, -6f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(aim - 18f * m_KickL - idleArm + superRaise, 6f, 0f);
                    break;

                case RigStyle.DualSprayCans:
                {
                    // As duas latas a apontar e a tremer durante o spray
                    float shakeR = attacking ? Mathf.Sin(time * 60f) * 4f : 0f;
                    float shakeL = attacking ? Mathf.Sin(time * 57f + 1.3f) * 4f : 0f;
                    m_ArmR.localRotation = Quaternion.Euler(aim + shakeR + idleArm + superRaise, -10f, shakeR * 0.5f);
                    m_ArmL.localRotation = Quaternion.Euler(aim + shakeL - idleArm + superRaise, 10f, shakeL * 0.5f);
                    foreach (var w in new[] { m_WeaponR, m_WeaponL })
                        if (w != null && w.scope != null)
                            w.scope.localRotation = Quaternion.Euler(0f, 0f, attacking ? Mathf.Sin(time * 45f) * 8f : 0f);
                    break;
                }

                case RigStyle.DualExplosives:
                {
                    // lança com uma mão de cada vez (alternam); a outra acompanha um pouco
                    float throwMain = attacking ? ThrowCurve(atk) : 0f;
                    float throwOther = throwMain * 0.25f;
                    bool rightThrows = m_ThrowSide == 0;
                    m_ArmR.localRotation = Quaternion.Euler(aim + 30f + (rightThrows ? throwMain : throwOther) + idleArm, -12f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(aim + 30f + (rightThrows ? throwOther : throwMain) - idleArm, 12f, 0f);
                    bool hide = time < m_HideHeldUntil && time > m_HideHeldUntil - 0.55f;
                    SetHeldVisible(m_WeaponR, !(hide && rightThrows));
                    SetHeldVisible(m_WeaponL, !(hide && !rightThrows));
                    break;
                }

                case RigStyle.Bow:
                {
                    m_Draw = Mathf.MoveTowards(m_Draw, 1f, dt * 1.8f);
                    float release = attacking ? Mathf.Clamp01(1f - atk * 3f) : 0f;
                    m_ArmL.localRotation = Quaternion.Euler(aim + idleArm * 0.5f + superRaise, 14f, 0f);
                    float pull = Mathf.SmoothStep(0f, 1f, m_Draw);
                    m_ArmR.localRotation = Quaternion.Euler(aim + 10f - 15f * release, Mathf.Lerp(-20f, -62f, pull), 0f);
                    WeaponModels.PoseBow(m_WeaponL, pull * 0.8f, release);
                    if (m_WeaponL != null && m_WeaponL.arrows != null) m_WeaponL.arrows.gameObject.SetActive(time >= m_HideHeldUntil && m_Draw > 0.5f);
                    break;
                }

                case RigStyle.SprayCan:
                {
                    float shake = attacking ? Mathf.Sin(time * 60f) * 4f : 0f;
                    m_ArmR.localRotation = Quaternion.Euler(aim + shake + idleArm + superRaise, -6f, shake * 0.5f);
                    // Braço livre balança ao andar
                    m_ArmL.localRotation = Quaternion.Euler(swing * 30f * move01 - 8f + m_SuperPose * -120f, 0f, -12f - m_SuperPose * 30f);
                    if (m_WeaponR != null && m_WeaponR.scope != null)
                        m_WeaponR.scope.localRotation = Quaternion.Euler(0f, 0f, attacking ? Mathf.Sin(time * 45f) * 8f : 0f);
                    break;
                }

                case RigStyle.Shotgun:
                case RigStyle.Sniper:
                {
                    float kick = 22f * m_BodyKick;
                    m_ArmR.localRotation = Quaternion.Euler(aim - kick + idleArm + superRaise, -14f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(aim - kick * 0.8f - idleArm + superRaise, Style == RigStyle.Shotgun ? 38f : 32f, 0f);
                    if (m_WeaponR != null && m_WeaponR.pump != null)
                    {
                        m_Pump = Mathf.MoveTowards(m_Pump, 1f, dt * 2.5f);
                        float pz = m_Pump < 0.5f ? Mathf.Sin(m_Pump * 2f * Mathf.PI) * -0.08f : 0f;
                        m_WeaponR.pump.localPosition = new Vector3(0f, -0.015f, 0.27f + pz);
                    }
                    break;
                }

                default:
                    m_ArmR.localRotation = Quaternion.Euler(aim - 15f * m_KickR + idleArm, -8f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(swing * 30f * move01 - 5f, 0f, -10f);
                    break;
            }

            // Revólveres: o tambor gira enquanto dispara
            if (Style == RigStyle.DualRevolvers)
            {
                if (m_WeaponR != null && m_WeaponR.drum != null) m_WeaponR.drum.localRotation *= Quaternion.Euler(0f, 0f, 700f * m_KickR * dt);
                if (m_WeaponL != null && m_WeaponL.drum != null) m_WeaponL.drum.localRotation *= Quaternion.Euler(0f, 0f, -700f * m_KickL * dt);
            }

            // Rastilho aceso das dinamites
            if (Style == RigStyle.DualExplosives && dt > 0f && Random.value < dt * 18f)
            {
                var w = Random.value < 0.5f ? m_WeaponR : m_WeaponL;
                if (w != null && w.fuseTip != null && w.arrows != null && w.arrows.gameObject.activeInHierarchy && m_Visible && m_Fade > 0.5f)
                    Fx.Glow(w.fuseTip.position, Random.insideUnitSphere * 0.4f + Vector3.up * 0.5f, new Color(1f, 0.7f, 0.25f), 0.09f, 0.25f);
            }
        }

        static float ThrowCurve(float t)
        {
            // Atrás da cabeça e depois para a frente
            if (t < 0.3f) return Mathf.Lerp(0f, -120f, t / 0.3f);
            if (t < 0.55f) return Mathf.Lerp(-120f, 25f, (t - 0.3f) / 0.25f);
            return Mathf.Lerp(25f, 0f, (t - 0.55f) / 0.45f);
        }

        static void SetHeldVisible(WeaponModel w, bool visible)
        {
            if (w == null || w.arrows == null) return;
            if (w.arrows.gameObject.activeSelf != visible) w.arrows.gameObject.SetActive(visible);
        }

        void ApplyMaterialState(float dt)
        {
            m_Flash = Mathf.MoveTowards(m_Flash, 0f, dt * 7f);
            float flash = m_Flash > 0.01f ? Mathf.Clamp01(m_Flash) * 0.85f : 0f;
            if (Mathf.Abs(flash - m_AppliedFlash) > 0.01f || (flash == 0f && m_AppliedFlash != 0f))
            {
                foreach (var r in m_Renderers) Art.SetFloat(r, Art.FlashId, flash);
                m_AppliedFlash = flash;
            }
            if (Mathf.Abs(m_Fade - m_AppliedFade) > 0.01f)
            {
                foreach (var r in m_Renderers) Art.SetFloat(r, Art.FadeId, m_Fade);
                m_AppliedFade = m_Fade;
            }
        }
    }
}
