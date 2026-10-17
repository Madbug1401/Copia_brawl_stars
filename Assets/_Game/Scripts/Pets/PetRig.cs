using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Estados de animação do pet.</summary>
    public enum PetAnim : byte { Idle, Walk, Run, Celebrate, React, Alert }

    /// <summary>
    /// Modelo cartoon de um pet montado a partir das peças do PetDefinition (primitivas, sem colisores),
    /// com animação procedural por papel de cada peça (cabeça, cauda, orelhas, patas, asas).
    /// Usado em jogo (PetFollower) e na pré-visualização do menu.
    /// </summary>
    public sealed class PetRig
    {
        public Transform Root { get; private set; }
        public int Stage { get; private set; }

        Transform m_Body, m_Head;
        readonly List<Transform> m_Tails = new List<Transform>();
        readonly List<Transform> m_Ears = new List<Transform>();
        readonly List<Transform> m_Wings = new List<Transform>();
        readonly Transform[] m_Legs = new Transform[4];
        readonly Dictionary<Transform, Quaternion> m_Rest = new Dictionary<Transform, Quaternion>();
        readonly List<Renderer> m_Renderers = new List<Renderer>();
        PetGait m_Gait;
        float m_HopHeight;
        float m_Phase;
        float m_AnimT;
        PetAnim m_Anim;
        bool m_Visible = true;

        public static PetRig Build(PetDefinition def, int stage, SkinDefinition skin, Transform parent, ShadowCastingMode shadows, int layer = 0)
        {
            var rig = new PetRig();
            rig.Create(def, stage, skin, parent, shadows, layer);
            return rig;
        }

        void Create(PetDefinition def, int stage, SkinDefinition skin, Transform parent, ShadowCastingMode shadows, int layer)
        {
            Stage = Mathf.Clamp(stage, 1, 3);
            Root = new GameObject("Pet_" + (def != null ? def.PetId : "none")).transform;
            Root.SetParent(parent, false);
            Root.gameObject.layer = layer;
            if (def == null) return;

            m_Gait = def.gait;
            m_HopHeight = def.hopHeight;
            bool useSkin = skin != null && skin.target == SkinTarget.Pet && skin.targetId == def.PetId;
            var main = useSkin ? skin.petMain : def.mainColor;
            var secondary = useSkin ? skin.petSecondary : def.secondaryColor;
            var accent = useSkin ? skin.petAccent : def.accentColor;

            m_Body = Node("Body", Root, Vector3.zero);
            m_Body.localScale = Vector3.one * def.ScaleForStage(Stage);

            // Pivôs por papel: as peças de cada papel rodam juntas à volta de um ponto
            var head = Node("Head", m_Body, Pivot(def, PetPartRole.Head));
            m_Head = head;
            var groups = new Dictionary<PetPartRole, Transform> { { PetPartRole.Head, head } };

            foreach (var part in def.parts)
            {
                if (part == null || Stage < part.minStage || Stage > part.maxStage) continue;
                var color = def.ColorFor(part.color, main, secondary, accent);
                Transform group;
                switch (part.role)
                {
                    case PetPartRole.Body:
                    case PetPartRole.Detail:
                        group = m_Body;
                        break;
                    default:
                        if (!groups.TryGetValue(part.role, out group))
                        {
                            group = Node(part.role.ToString(), part.role == PetPartRole.EarLeft || part.role == PetPartRole.EarRight ? head : m_Body, PivotFor(part, head));
                            groups[part.role] = group;
                            Register(part.role, group);
                        }
                        break;
                }
                var local = group.InverseTransformPoint(m_Body.TransformPoint(part.position));
                var t = Art.Part(ToPrimitive(part.shape), group, local, part.scale, color, part.shape != PetShape.Cube || part.scale.magnitude > 0.08f, part.euler, shadows);
                t.gameObject.layer = layer;
            }

            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Root.GetComponentsInChildren(true, m_Renderers);
            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) m_Rest[t] = t.localRotation;

            // Brilho leve nas fases desenvolvida/evoluída e nas skins lendárias
            if (Stage >= 3 || (useSkin && skin.sparkle))
                foreach (var r in m_Renderers) Art.SetEmission(r, Art.Darken(accent, 0.08f));
        }

        static Vector3 Pivot(PetDefinition def, PetPartRole role)
        {
            foreach (var p in def.parts)
                if (p != null && p.role == role && (p.name == "head" || p.name == "body" || p.name == "shell")) return p.position + new Vector3(0f, -0.08f, -0.06f);
            return new Vector3(0f, 0.4f, 0.2f);
        }

        static Vector3 PivotFor(PetPart part, Transform head)
        {
            switch (part.role)
            {
                case PetPartRole.LegFrontLeft:
                case PetPartRole.LegFrontRight:
                case PetPartRole.LegBackLeft:
                case PetPartRole.LegBackRight:
                    // Anca: no topo da pata
                    return new Vector3(part.position.x, part.position.y + part.scale.y, part.position.z);
                case PetPartRole.Tail:
                    return new Vector3(0f, part.position.y - 0.05f, part.position.z + 0.08f);
                case PetPartRole.WingLeft:
                case PetPartRole.WingRight:
                    return new Vector3(part.position.x, part.position.y + part.scale.y * 0.5f, part.position.z);
                case PetPartRole.EarLeft:
                case PetPartRole.EarRight:
                    return head.InverseTransformPoint(head.parent.TransformPoint(new Vector3(part.position.x, part.position.y - part.scale.y * 0.5f, part.position.z)));
                default:
                    return part.position;
            }
        }

        void Register(PetPartRole role, Transform group)
        {
            switch (role)
            {
                case PetPartRole.Tail: m_Tails.Add(group); break;
                case PetPartRole.EarLeft:
                case PetPartRole.EarRight: m_Ears.Add(group); break;
                case PetPartRole.WingLeft:
                case PetPartRole.WingRight: m_Wings.Add(group); break;
                case PetPartRole.LegFrontLeft: m_Legs[0] = group; break;
                case PetPartRole.LegFrontRight: m_Legs[1] = group; break;
                case PetPartRole.LegBackLeft: m_Legs[2] = group; break;
                case PetPartRole.LegBackRight: m_Legs[3] = group; break;
            }
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static PrimitiveType ToPrimitive(PetShape s)
        {
            switch (s)
            {
                case PetShape.Capsule: return PrimitiveType.Capsule;
                case PetShape.Cube: return PrimitiveType.Cube;
                case PetShape.Cylinder: return PrimitiveType.Cylinder;
                default: return PrimitiveType.Sphere;
            }
        }

        public void SetAnim(PetAnim anim)
        {
            if (anim == m_Anim) return;
            if (m_Anim == PetAnim.Celebrate && Root != null) Root.localRotation = Quaternion.identity;
            m_Anim = anim;
            m_AnimT = 0f;
        }

        public PetAnim Anim => m_Anim;

        public void SetVisible(bool visible)
        {
            if (Root == null || m_Visible == visible) return;
            m_Visible = visible;
            Root.gameObject.SetActive(visible);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }

        Quaternion Rest(Transform t) => t != null && m_Rest.TryGetValue(t, out var q) ? q : Quaternion.identity;

        /// <summary>Anima o pet. speed01: 0 parado, ~0.5 a andar, 1 a correr.</summary>
        public void Tick(float dt, float speed01)
        {
            if (Root == null || m_Body == null) return;
            m_AnimT += dt;
            float freq = Mathf.Lerp(6f, 13f, speed01);
            m_Phase += dt * freq * (speed01 > 0.05f ? 1f : 0.25f);
            float time = Time.time;
            float moving = Mathf.Clamp01(speed01 * 2f);

            float bodyY = 0f, bodyPitch = 0f, bodyRoll = 0f, headPitch = 0f, headYaw = 0f;
            float tailWag = Mathf.Sin(time * (m_Anim == PetAnim.Celebrate ? 22f : 9f)) * (m_Anim == PetAnim.Celebrate ? 40f : 15f);
            float earFlop = Mathf.Sin(m_Phase) * 10f * moving;
            float wingFlap = 0f;

            switch (m_Gait)
            {
                case PetGait.Hop:
                    bodyY = Mathf.Abs(Mathf.Sin(m_Phase * 0.5f)) * m_HopHeight * moving;
                    bodyPitch = -Mathf.Cos(m_Phase * 0.5f) * 8f * moving;
                    break;
                case PetGait.Waddle:
                    bodyRoll = Mathf.Sin(m_Phase) * 9f * moving;
                    bodyY = Mathf.Abs(Mathf.Sin(m_Phase)) * 0.025f * moving;
                    break;
                case PetGait.Crawl:
                    bodyY = Mathf.Abs(Mathf.Sin(m_Phase)) * 0.01f * moving;
                    headYaw = Mathf.Sin(m_Phase * 0.5f) * 6f * moving;
                    break;
                default:
                    bodyY = Mathf.Abs(Mathf.Sin(m_Phase)) * 0.035f * moving;
                    bodyPitch = Mathf.Sin(m_Phase * 2f) * 2f * moving;
                    break;
            }

            switch (m_Anim)
            {
                case PetAnim.Idle:
                    bodyY += Mathf.Sin(time * 2f) * 0.008f;
                    headYaw += Mathf.Sin(time * 0.7f) * 18f;
                    headPitch += Mathf.Sin(time * 1.3f) * 4f;
                    break;
                case PetAnim.Celebrate:
                {
                    // Saltos de alegria + giro
                    float k = Mathf.Abs(Mathf.Sin(m_AnimT * 9f));
                    bodyY += k * 0.25f;
                    Root.localRotation *= Quaternion.Euler(0f, dt * 360f, 0f);
                    headPitch -= 15f;
                    wingFlap = Mathf.Sin(time * 30f) * 50f;
                    break;
                }
                case PetAnim.React:
                {
                    // Susto: salto curto e abana a cabeça
                    float k = Mathf.Clamp01(1f - m_AnimT / 0.5f);
                    bodyY += Mathf.Sin(Mathf.Clamp01(m_AnimT / 0.35f) * Mathf.PI) * 0.15f;
                    headYaw += Mathf.Sin(m_AnimT * 40f) * 25f * k;
                    bodyPitch -= 10f * k;
                    break;
                }
                case PetAnim.Alert:
                    headPitch -= 20f;
                    wingFlap = Mathf.Sin(time * 26f) * 45f;
                    tailWag *= 0.2f;
                    break;
            }
            if (m_Wings.Count > 0 && wingFlap == 0f) wingFlap = Mathf.Sin(m_Phase) * 12f * moving;

            m_Body.localPosition = new Vector3(0f, bodyY, 0f);
            m_Body.localRotation = Quaternion.Euler(bodyPitch, 0f, bodyRoll);
            if (m_Head != null) m_Head.localRotation = Rest(m_Head) * Quaternion.Euler(headPitch, headYaw, 0f);
            foreach (var t in m_Tails) t.localRotation = Rest(t) * Quaternion.Euler(0f, tailWag, 0f);
            for (int i = 0; i < m_Ears.Count; i++)
                m_Ears[i].localRotation = Rest(m_Ears[i]) * Quaternion.Euler(earFlop, 0f, (i % 2 == 0 ? 1f : -1f) * earFlop * 0.5f);
            for (int i = 0; i < m_Wings.Count; i++)
                m_Wings[i].localRotation = Rest(m_Wings[i]) * Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * wingFlap);

            // Patas: diagonais em oposição (trote); no salto as patas de trás empurram juntas
            for (int i = 0; i < 4; i++)
            {
                var leg = m_Legs[i];
                if (leg == null) continue;
                float swing;
                if (m_Gait == PetGait.Hop) swing = Mathf.Sin(m_Phase * 0.5f) * 35f * moving;
                else swing = Mathf.Sin(m_Phase + (i == 0 || i == 3 ? 0f : Mathf.PI)) * 32f * moving;
                leg.localRotation = Rest(leg) * Quaternion.Euler(swing, 0f, 0f);
            }
        }
    }
}
