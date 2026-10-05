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
        public Transform Root { get; private set; }
        public RigStyle Style { get; private set; }

        Transform m_Body, m_Hips, m_Spine, m_Head, m_LegL, m_LegR, m_ArmL, m_ArmR, m_SocketL, m_SocketR;
        WeaponModel m_WeaponL, m_WeaponR;
        readonly List<Renderer> m_Renderers = new List<Renderer>();
        CharacterLook m_Look;
        float m_Scale = 1f;

        // Estado de animação
        float m_Phase, m_Speed, m_Air;
        float m_AttackT = 10f, m_AttackDur = 0.4f;
        bool m_AttackSuper;
        float m_KickL, m_KickR, m_BodyKick;
        float m_Hurt, m_Flash;
        float m_Draw = 1f;
        float m_HideHeldUntil;
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
        public Vector3 ChestPosition => m_Spine != null ? m_Spine.position + Vector3.up * 0.3f * m_Scale : Root.position + Vector3.up * 1.1f;

        // ================================================================== construção

        public static CharacterRig Build(BrawlerDefinition b, Transform parent, ShadowCastingMode shadows, int layer)
        {
            var rig = new CharacterRig();
            rig.Create(b, parent, shadows, layer);
            return rig;
        }

        void Create(BrawlerDefinition b, Transform parent, ShadowCastingMode shadows, int layer)
        {
            var tp = b != null ? b.thirdPerson : new ThirdPersonPresentation();
            m_Look = tp.look ?? new CharacterLook();
            Style = tp.rig;
            var L = m_Look;
            var accentColor = b != null ? b.firstPerson.accentColor : Color.white;
            float hs = Mathf.Clamp(L.headScale, 0.6f, 1.6f);
            float bulk = Mathf.Clamp(L.bulk, 0.6f, 1.8f);
            m_Scale = Mathf.Clamp(L.bodyScale, 0.7f, 1.4f);

            Root = new GameObject("Rig_" + (b != null ? b.brawlerId : "generic")).transform;
            Root.SetParent(parent, false);
            Root.gameObject.layer = layer;

            m_Body = Node("Body", Root, Vector3.zero);
            m_Body.localScale = Vector3.one * m_Scale;

            // ---------------- ancas e pernas
            m_Hips = Node("Hips", m_Body, new Vector3(0f, 0.62f, 0f));
            Part(PrimitiveType.Cube, m_Hips, new Vector3(0f, 0f, 0f), new Vector3(0.42f * bulk, 0.2f, 0.28f), L.pants, shadows);
            m_LegL = Node("LegL", m_Hips, new Vector3(-0.13f * bulk, -0.02f, 0f));
            m_LegR = Node("LegR", m_Hips, new Vector3(0.13f * bulk, -0.02f, 0f));
            foreach (var leg in new[] { m_LegL, m_LegR })
            {
                Part(PrimitiveType.Capsule, leg, new Vector3(0f, -0.25f, 0f), new Vector3(0.19f, 0.25f, 0.19f), L.pants, shadows);
                Part(PrimitiveType.Cube, leg, new Vector3(0f, -0.55f, 0.05f), new Vector3(0.21f, 0.12f, 0.32f), Art.Darken(L.gear, 0.7f), shadows);
            }

            // ---------------- tronco
            m_Spine = Node("Spine", m_Hips, new Vector3(0f, 0.04f, 0f));
            Part(PrimitiveType.Sphere, m_Spine, new Vector3(0f, 0.3f, 0f), new Vector3(0.58f * bulk, 0.62f, 0.44f), L.outfit, shadows);
            Part(PrimitiveType.Cylinder, m_Spine, new Vector3(0f, 0.08f, 0f), new Vector3(0.5f * bulk, 0.035f, 0.4f), Art.Darken(L.gear, 0.6f), shadows);
            Part(PrimitiveType.Cube, m_Spine, new Vector3(0f, 0.08f, 0.2f), new Vector3(0.1f, 0.08f, 0.04f), new Color(1f, 0.82f, 0.3f), ShadowCastingMode.Off);
            // Lenço ao pescoço (cor de destaque)
            Part(PrimitiveType.Sphere, m_Spine, new Vector3(0f, 0.53f, 0.05f), new Vector3(0.36f * bulk, 0.12f, 0.32f), L.accent, shadows);
            BuildBackAccessory(m_Spine, L, accentColor, shadows, bulk);

            // ---------------- cabeça
            m_Head = Node("Head", m_Spine, new Vector3(0f, 0.56f, 0f));
            Part(PrimitiveType.Sphere, m_Head, new Vector3(0f, 0.28f * hs, 0f), Vector3.one * 0.6f * hs, L.skin, shadows);
            BuildFace(m_Head, L, hs, shadows);
            BuildHeadGear(m_Head, L, hs, shadows);

            // ---------------- braços
            m_ArmL = Node("ArmL", m_Spine, new Vector3(-0.31f * bulk, 0.42f, 0f));
            m_ArmR = Node("ArmR", m_Spine, new Vector3(0.31f * bulk, 0.42f, 0f));
            m_SocketL = BuildArm(m_ArmL, L, shadows);
            m_SocketR = BuildArm(m_ArmR, L, shadows);

            // ---------------- armas
            bool dual = Style == RigStyle.DualRevolvers || Style == RigStyle.DualExplosives;
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
            Part(PrimitiveType.Sphere, shoulder, new Vector3(0f, -0.02f, 0f), Vector3.one * 0.21f, L.outfit, s);
            Part(PrimitiveType.Capsule, shoulder, new Vector3(0f, -0.21f, 0f), new Vector3(0.14f, 0.2f, 0.14f), L.skin, s);
            Part(PrimitiveType.Sphere, shoulder, new Vector3(0f, -0.43f, 0f), Vector3.one * 0.16f, Art.Darken(L.gear, 0.85f), s);
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
                        Part(PrimitiveType.Capsule, head, new Vector3(0f, top, -0.04f * hs) + dir * 0.26f * hs, new Vector3(0.13f, 0.17f, 0.13f) * hs, L.hair, s, Quaternion.FromToRotation(Vector3.up, dir).eulerAngles);
                    }
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.08f * hs, -0.03f * hs), new Vector3(0.6f, 0.4f, 0.58f) * hs, L.hair, s);
                    // Óculos na testa
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top + 0.1f * hs, 0f), new Vector3(0.62f, 0.035f, 0.62f) * hs, Art.Darken(L.gear, 0.6f), ShadowCastingMode.Off, new Vector3(-12f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.11f, top + 0.13f * hs, 0.27f * hs), new Vector3(0.16f, 0.04f, 0.16f) * hs, L.gear, s, new Vector3(70f, 0f, 0f));
                        var lens = Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.11f, top + 0.135f * hs, 0.31f * hs), new Vector3(0.12f, 0.01f, 0.12f) * hs, new Color(0.55f, 0.85f, 1f), ShadowCastingMode.Off, new Vector3(70f, 0f, 0f), false);
                        Art.SetEmission(lens.GetComponent<Renderer>(), new Color(0.15f, 0.25f, 0.3f));
                    }
                    break;
                }
                case HeadGear.FoxHood:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.04f * hs, -0.06f * hs), new Vector3(0.68f, 0.66f, 0.64f) * hs, L.gear, s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.18f, top + 0.33f * hs, -0.04f * hs), new Vector3(0.17f, 0.17f, 0.06f) * hs, L.gear, s, new Vector3(0f, 0f, 45f + side * 12f));
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.18f, top + 0.32f * hs, -0.01f * hs), new Vector3(0.09f, 0.09f, 0.03f) * hs, L.accent, ShadowCastingMode.Off, new Vector3(0f, 0f, 45f + side * 12f), false);
                    }
                    // Máscara de pano na parte de baixo da cara
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.17f * hs, 0.12f * hs), new Vector3(0.56f, 0.24f, 0.42f) * hs, L.accent, s);
                    break;
                }
                case HeadGear.GasMask:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.17f * hs, 0.24f * hs), new Vector3(0.3f, 0.24f, 0.2f) * hs, new Color(0.25f, 0.27f, 0.3f), s);
                    for (int side = -1; side <= 1; side += 2)
                        Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.16f, 0.1f * hs, 0.26f * hs), new Vector3(0.12f, 0.05f, 0.12f) * hs, L.accent, s, new Vector3(70f, side * 30f, 0f));
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.1f * hs, -0.02f * hs), new Vector3(0.64f, 0.46f, 0.64f) * hs, L.gear, s);
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top - 0.02f * hs, 0f), new Vector3(0.65f, 0.05f, 0.65f) * hs, Art.Darken(L.gear, 0.75f), s);
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.36f * hs, -0.02f * hs), Vector3.one * 0.15f * hs, L.accent, s);
                    // Madeixas a sair do gorro
                    Part(PrimitiveType.Capsule, head, new Vector3(0.24f, 0.18f * hs, -0.12f * hs), new Vector3(0.12f, 0.16f, 0.12f) * hs, L.hair, s, new Vector3(0f, 0f, 15f));
                    Part(PrimitiveType.Capsule, head, new Vector3(-0.24f, 0.18f * hs, -0.12f * hs), new Vector3(0.12f, 0.16f, 0.12f) * hs, L.hair, s, new Vector3(0f, 0f, -15f));
                    break;
                }
                case HeadGear.Bandana:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.06f * hs, -0.04f * hs), new Vector3(0.62f, 0.5f, 0.62f) * hs, L.hair, s);
                    Part(PrimitiveType.Cylinder, head, new Vector3(0f, top + 0.12f * hs, 0f), new Vector3(0.64f, 0.06f, 0.64f) * hs, L.accent, s, new Vector3(-10f, 0f, 0f));
                    Part(PrimitiveType.Cube, head, new Vector3(0.05f, top + 0.08f * hs, -0.33f * hs), new Vector3(0.08f, 0.18f, 0.04f) * hs, L.accent, s, new Vector3(-20f, 0f, 25f));
                    Part(PrimitiveType.Capsule, head, new Vector3(0f, top - 0.05f * hs, -0.36f * hs), new Vector3(0.16f, 0.22f, 0.16f) * hs, L.hair, s, new Vector3(-30f, 0f, 0f));
                    break;
                }
                case HeadGear.EarCap:
                {
                    Part(PrimitiveType.Sphere, head, new Vector3(0f, top + 0.1f * hs, -0.02f * hs), new Vector3(0.64f, 0.42f, 0.64f) * hs, L.gear, s);
                    Part(PrimitiveType.Cube, head, new Vector3(0f, top + 0.06f * hs, 0.32f * hs), new Vector3(0.42f, 0.03f, 0.26f) * hs, Art.Darken(L.gear, 0.8f), s, new Vector3(-8f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Cube, head, new Vector3(side * 0.2f, top + 0.3f * hs, -0.02f * hs), new Vector3(0.1f, 0.2f, 0.05f) * hs, L.gear, s, new Vector3(0f, 0f, side * -20f));
                        Part(PrimitiveType.Capsule, head, new Vector3(side * 0.24f, top + 0.44f * hs, -0.02f * hs), new Vector3(0.04f, 0.08f, 0.04f) * hs, L.hair, ShadowCastingMode.Off, new Vector3(0f, 0f, side * -25f), false);
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
            bool dual = m_WeaponL != null && m_WeaponR != null;
            WeaponModel w;
            if (dual) w = (Mathf.Abs(shotIndex) % 2 == 0) ? m_WeaponR : m_WeaponL;
            else w = m_WeaponR ?? m_WeaponL;
            if (w == null) return Root.position + Vector3.up * 1.3f;
            return w.Muzzle(0);
        }

        /// <summary>Início de um ataque (principal ou Super).</summary>
        public void OnAttack(bool super, float duration)
        {
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
            if (m_WeaponL != null && m_WeaponR != null && Mathf.Abs(shotIndex) % 2 == 1) m_KickL = 1f;
            else m_KickR = 1f;
            if (Style == RigStyle.Shotgun || Style == RigStyle.Sniper) m_BodyKick = 1f;
        }

        public void OnHurt()
        {
            m_Hurt = 1f;
            m_Flash = 1f;
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
            }
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
            Pose(dt, f);
            ApplyMaterialState(dt);
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
            m_Hips.localPosition = new Vector3(0f, 0.62f + bob * 0.045f * move01 + breathe + m_Air * 0.04f, 0f);
            m_Hips.localRotation = Quaternion.Euler(0f, swing * 8f * move01, 0f);

            // ---------------- tronco e cabeça (seguem a pontaria)
            float pitch = Mathf.Clamp(f.pitch, -70f, 70f);
            m_Hurt = Mathf.MoveTowards(m_Hurt, 0f, dt * 5f);
            m_BodyKick = Mathf.MoveTowards(m_BodyKick, 0f, dt * 6f);
            m_SuperPose = Mathf.MoveTowards(m_SuperPose, 0f, dt * 2.2f);
            float lean = 9f * move01 * Mathf.Max(0f, fwd) - m_Hurt * 14f - m_BodyKick * 7f;
            float spinePitch = lean + pitch * 0.25f;
            m_Spine.localRotation = Quaternion.Euler(spinePitch, -swing * 6f * move01, -side * 5f * move01 + m_Hurt * 6f);
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

            switch (Style)
            {
                case RigStyle.DualRevolvers:
                    m_ArmR.localRotation = Quaternion.Euler(aim - 18f * m_KickR + idleArm + superRaise, -6f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(aim - 18f * m_KickL - idleArm + superRaise, 6f, 0f);
                    break;

                case RigStyle.DualExplosives:
                {
                    float throwR = attacking ? ThrowCurve(atk) : 0f;
                    float throwL = attacking ? ThrowCurve(Mathf.Clamp01((m_AttackT - 0.08f) / m_AttackDur)) : 0f;
                    m_ArmR.localRotation = Quaternion.Euler(aim + 30f + throwR + idleArm, -12f, 0f);
                    m_ArmL.localRotation = Quaternion.Euler(aim + 30f + throwL - idleArm, 12f, 0f);
                    bool hide = time < m_HideHeldUntil && time > m_HideHeldUntil - 0.55f;
                    SetHeldVisible(m_WeaponR, !hide);
                    SetHeldVisible(m_WeaponL, !hide);
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
