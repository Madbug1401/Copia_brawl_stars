using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Modelo de um pet (peças animadas pelo PetFollower). Sem colisores: não bloqueia tiros nem movimento.</summary>
    public class PetModel
    {
        public Transform root, body, head, tail;
        public readonly List<Transform> legs = new List<Transform>();
        public readonly List<Transform> ears = new List<Transform>();
        public Transform wingL, wingR;
        public readonly List<Renderer> renderers = new List<Renderer>();
        public PetSpecies species;
        public int stage;
        public int level;
        public bool aura;
        public bool trickUnlocked;
        public float scale = 1f;
        public Color primary, accent;
        public SkinEffect effect;
    }

    /// <summary>
    /// Pets cartoon construídos com primitivas, no mesmo estilo das personagens. Estágios visuais:
    /// 0 Jovem (níveis 1–3: pequeno, cabeça grande) · 1 Desenvolvido (4–7: maior, coleira) ·
    /// 2 Evoluído (8–10: tamanho final + acessório da espécie). Nível 10: aura especial.
    /// Novas espécies: acrescentar um caso em BuildSpecies.
    /// </summary>
    public static class PetVisualBuilder
    {
        static readonly Color Ink = new Color(0.1f, 0.08f, 0.1f);

        public static PetModel Build(PetDefinition def, int level, SkinDefinition skin, Transform parent, ShadowCastingMode shadows, int layer = 0)
        {
            var m = new PetModel
            {
                species = def != null ? def.species : PetSpecies.Dog,
                level = Mathf.Clamp(level, 1, 10),
            };
            m.stage = PetDefinition.StageFor(m.level);
            m.aura = PetProgressionManager.HasMilestone(def, m.level, PetMilestoneType.Cosmetic);
            m.trickUnlocked = PetProgressionManager.HasMilestone(def, m.level, PetMilestoneType.Animation);
            m.effect = skin != null ? skin.effect : SkinEffect.None;

            var primary = skin != null ? skin.petPrimary : (def != null ? def.primaryColor : Color.white);
            var secondary = skin != null ? skin.petSecondary : (def != null ? def.secondaryColor : Color.gray);
            var accent = skin != null ? skin.petAccent : (def != null ? def.accentColor : Color.red);
            m.primary = primary;
            m.accent = accent;

            float stageScale = m.stage == 0 ? 0.68f : (m.stage == 1 ? 0.85f : 1f);
            m.scale = stageScale * (def != null ? Mathf.Clamp(def.scale, 0.5f, 2f) : 1f);
            float headBoost = m.stage == 0 ? 1.18f : 1f; // jovem: cabeça proporcionalmente maior

            m.root = new GameObject("Pet_" + (def != null ? def.petId : "pet")).transform;
            m.root.SetParent(parent, false);
            m.body = Node("Body", m.root, Vector3.zero);
            m.body.localScale = Vector3.one * m.scale;

            BuildSpecies(m, primary, secondary, accent, headBoost, shadows);

            // Desenvolvido: coleira/lenço com a cor de destaque
            if (m.stage >= 1 && m.head != null && m.species != PetSpecies.Turtle && m.species != PetSpecies.Rooster)
                Part(PrimitiveType.Cylinder, m.head, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.24f, 0.03f, 0.22f), accent, shadows);
            // Evoluído: acessório adicional da espécie
            if (m.stage >= 2) BuildEvolvedAccessory(m, accent, shadows);

            foreach (var t in m.root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            m.root.GetComponentsInChildren(true, m.renderers);
            return m;
        }

        static void BuildSpecies(PetModel m, Color primary, Color secondary, Color accent, float hb, ShadowCastingMode s)
        {
            var b = m.body;
            switch (m.species)
            {
                case PetSpecies.Dog:
                {
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.27f, 0.46f), primary, s);
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.27f, 0.08f), new Vector3(0.22f, 0.2f, 0.24f), secondary, s);
                    m.head = Node("Head", b, new Vector3(0f, 0.48f, 0.22f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.3f * hb, primary, s);
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.04f, 0.15f) * hb, new Vector3(0.15f, 0.12f, 0.17f) * hb, secondary, s);
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.01f, 0.24f) * hb, Vector3.one * 0.06f * hb, Ink, ShadowCastingMode.Off, default, false);
                    Eyes(m.head, 0.12f * hb, 0.05f * hb);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var ear = Node("Ear", m.head, new Vector3(side * 0.13f, 0.08f, -0.02f) * hb);
                        Part(PrimitiveType.Cube, ear, new Vector3(0f, -0.06f, 0f), new Vector3(0.08f, 0.16f, 0.05f) * hb, Art.Darken(primary, 0.75f), s);
                        ear.localRotation = Quaternion.Euler(0f, 0f, side * 18f);
                        m.ears.Add(ear);
                    }
                    Legs(m, primary, 0.1f, 0.14f, 0.13f, 0.08f, s);
                    m.tail = Node("Tail", b, new Vector3(0f, 0.36f, -0.22f));
                    Part(PrimitiveType.Capsule, m.tail, new Vector3(0f, 0.08f, -0.03f), new Vector3(0.06f, 0.1f, 0.06f), primary, s, new Vector3(-30f, 0f, 0f));
                    break;
                }
                case PetSpecies.Cat:
                {
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.28f, 0f), new Vector3(0.24f, 0.24f, 0.42f), primary, s);
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.25f, 0.1f), new Vector3(0.18f, 0.17f, 0.2f), secondary, s);
                    m.head = Node("Head", b, new Vector3(0f, 0.46f, 0.2f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.27f * hb, primary, s);
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.05f, 0.11f) * hb, new Vector3(0.13f, 0.09f, 0.08f) * hb, secondary, s);
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.02f, 0.14f) * hb, Vector3.one * 0.035f * hb, new Color(1f, 0.55f, 0.6f), ShadowCastingMode.Off, default, false);
                    Eyes(m.head, 0.11f * hb, 0.04f * hb);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var ear = Node("Ear", m.head, new Vector3(side * 0.08f, 0.12f, 0f) * hb);
                        Part(PrimitiveType.Cube, ear, Vector3.zero, new Vector3(0.08f, 0.08f, 0.03f) * hb, primary, s, new Vector3(0f, 0f, 45f));
                        m.ears.Add(ear);
                        // bigodes
                        Part(PrimitiveType.Cube, m.head, new Vector3(side * 0.1f, -0.05f, 0.12f) * hb, new Vector3(0.1f, 0.005f, 0.005f) * hb, Color.white, ShadowCastingMode.Off,
                            new Vector3(0f, side * -12f, 0f), false);
                    }
                    Legs(m, primary, 0.08f, 0.14f, 0.12f, 0.07f, s);
                    m.tail = Node("Tail", b, new Vector3(0f, 0.34f, -0.2f));
                    Part(PrimitiveType.Capsule, m.tail, new Vector3(0f, 0.12f, -0.04f), new Vector3(0.05f, 0.15f, 0.05f), primary, s, new Vector3(-20f, 0f, 0f));
                    Part(PrimitiveType.Sphere, m.tail, new Vector3(0f, 0.26f, -0.06f), Vector3.one * 0.06f, secondary, s);
                    break;
                }
                case PetSpecies.Pig:
                {
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.3f, 0f), new Vector3(0.38f, 0.32f, 0.46f), primary, s);
                    m.head = Node("Head", b, new Vector3(0f, 0.42f, 0.24f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.28f * hb, primary, s);
                    Part(PrimitiveType.Cylinder, m.head, new Vector3(0f, -0.03f, 0.14f) * hb, new Vector3(0.12f, 0.03f, 0.1f) * hb, secondary, s, new Vector3(90f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Part(PrimitiveType.Sphere, m.head, new Vector3(side * 0.025f, -0.03f, 0.17f) * hb, Vector3.one * 0.022f * hb, Art.Darken(secondary, 0.5f), ShadowCastingMode.Off, default, false);
                        var ear = Node("Ear", m.head, new Vector3(side * 0.1f, 0.11f, 0f) * hb);
                        Part(PrimitiveType.Cube, ear, Vector3.zero, new Vector3(0.08f, 0.07f, 0.03f) * hb, secondary, s, new Vector3(-20f, 0f, 45f));
                        m.ears.Add(ear);
                    }
                    Eyes(m.head, 0.1f * hb, 0.05f * hb);
                    Legs(m, Art.Darken(primary, 0.9f), 0.12f, 0.14f, 0.12f, 0.08f, s);
                    m.tail = Node("Tail", b, new Vector3(0f, 0.36f, -0.24f));
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Sphere, m.tail, new Vector3(Mathf.Sin(i * 2f) * 0.03f, i * 0.025f, -i * 0.015f), Vector3.one * 0.035f, secondary, s);
                    break;
                }
                case PetSpecies.Rooster:
                {
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.36f, 0f), new Vector3(0.27f, 0.3f, 0.32f), primary, s);
                    m.wingL = Node("WingL", b, new Vector3(-0.13f, 0.38f, 0f));
                    m.wingR = Node("WingR", b, new Vector3(0.13f, 0.38f, 0f));
                    Part(PrimitiveType.Sphere, m.wingL, Vector3.zero, new Vector3(0.06f, 0.18f, 0.24f), Art.Darken(primary, 0.9f), s);
                    Part(PrimitiveType.Sphere, m.wingR, Vector3.zero, new Vector3(0.06f, 0.18f, 0.24f), Art.Darken(primary, 0.9f), s);
                    m.head = Node("Head", b, new Vector3(0f, 0.6f, 0.1f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.17f * hb, primary, s);
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Sphere, m.head, new Vector3(0f, 0.09f + (i == 1 ? 0.02f : 0f), -0.03f + i * 0.035f) * hb, Vector3.one * 0.06f * hb, secondary, s);
                    Part(PrimitiveType.Cylinder, m.head, new Vector3(0f, -0.01f, 0.1f) * hb, new Vector3(0.045f, 0.04f, 0.045f) * hb, accent, s, new Vector3(90f, 0f, 0f));
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.06f, 0.07f) * hb, new Vector3(0.04f, 0.06f, 0.03f) * hb, secondary, ShadowCastingMode.Off);
                    Eyes(m.head, 0.07f * hb, 0.02f * hb);
                    m.tail = Node("Tail", b, new Vector3(0f, 0.42f, -0.14f));
                    for (int i = -1; i <= 1; i++)
                        Part(PrimitiveType.Capsule, m.tail, new Vector3(i * 0.05f, 0.1f, -0.04f), new Vector3(0.05f, 0.12f, 0.03f), i == 0 ? Art.Darken(secondary, 0.8f) : new Color(0.15f, 0.35f, 0.25f),
                            s, new Vector3(-35f, i * 18f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var leg = Node("Leg", b, new Vector3(side * 0.07f, 0.2f, 0f));
                        Part(PrimitiveType.Cylinder, leg, new Vector3(0f, -0.09f, 0f), new Vector3(0.025f, 0.09f, 0.025f), accent, s);
                        Part(PrimitiveType.Cube, leg, new Vector3(0f, -0.18f, 0.03f), new Vector3(0.06f, 0.015f, 0.08f), accent, ShadowCastingMode.Off);
                        m.legs.Add(leg);
                    }
                    break;
                }
                case PetSpecies.Rabbit:
                {
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.26f, 0f), new Vector3(0.26f, 0.26f, 0.32f), primary, s);
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.3f, -0.17f), Vector3.one * 0.1f, Color.white, s);
                    m.head = Node("Head", b, new Vector3(0f, 0.46f, 0.12f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.24f * hb, primary, s);
                    Part(PrimitiveType.Sphere, m.head, new Vector3(0f, -0.04f, 0.11f) * hb, Vector3.one * 0.04f * hb, secondary, ShadowCastingMode.Off, default, false);
                    Eyes(m.head, 0.09f * hb, 0.03f * hb);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var ear = Node("Ear", m.head, new Vector3(side * 0.06f, 0.1f, -0.02f) * hb);
                        Part(PrimitiveType.Capsule, ear, new Vector3(0f, 0.12f, 0f), new Vector3(0.07f, 0.13f, 0.04f) * hb, primary, s);
                        Part(PrimitiveType.Capsule, ear, new Vector3(0f, 0.12f, 0.012f), new Vector3(0.04f, 0.1f, 0.02f) * hb, secondary, ShadowCastingMode.Off);
                        ear.localRotation = Quaternion.Euler(0f, 0f, side * -10f);
                        m.ears.Add(ear);
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var foot = Node("Leg", b, new Vector3(side * 0.09f, 0.08f, 0.02f));
                        Part(PrimitiveType.Sphere, foot, new Vector3(0f, -0.04f, 0.04f), new Vector3(0.08f, 0.06f, 0.16f), primary, s);
                        m.legs.Add(foot);
                    }
                    break;
                }
                default: // Turtle
                {
                    Part(PrimitiveType.Cylinder, b, new Vector3(0f, 0.12f, 0f), new Vector3(0.42f, 0.04f, 0.48f), Art.Darken(primary, 0.85f), s);
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.2f, 0f), new Vector3(0.48f, 0.26f, 0.54f), secondary, s);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        Part(PrimitiveType.Sphere, b, new Vector3(Mathf.Sin(a) * 0.12f, 0.29f, Mathf.Cos(a) * 0.14f), new Vector3(0.1f, 0.04f, 0.1f), accent, ShadowCastingMode.Off);
                    }
                    Part(PrimitiveType.Sphere, b, new Vector3(0f, 0.33f, 0f), new Vector3(0.12f, 0.05f, 0.12f), accent, ShadowCastingMode.Off);
                    m.head = Node("Head", b, new Vector3(0f, 0.2f, 0.3f));
                    Part(PrimitiveType.Sphere, m.head, Vector3.zero, Vector3.one * 0.16f * hb, primary, s);
                    Eyes(m.head, 0.07f * hb, 0.03f * hb);
                    Legs(m, primary, 0.15f, 0.16f, 0.07f, 0.08f, s);
                    m.tail = Node("Tail", b, new Vector3(0f, 0.12f, -0.27f));
                    Part(PrimitiveType.Sphere, m.tail, Vector3.zero, new Vector3(0.05f, 0.04f, 0.08f), primary, s);
                    break;
                }
            }
        }

        static void BuildEvolvedAccessory(PetModel m, Color accent, ShadowCastingMode s)
        {
            if (m.head == null) return;
            switch (m.species)
            {
                case PetSpecies.Dog:
                    // Óculos de aviador na testa
                    Part(PrimitiveType.Cylinder, m.head, new Vector3(0f, 0.09f, 0.04f), new Vector3(0.3f, 0.02f, 0.3f), new Color(0.35f, 0.25f, 0.2f), s, new Vector3(-15f, 0f, 0f));
                    for (int side = -1; side <= 1; side += 2)
                        Part(PrimitiveType.Cylinder, m.head, new Vector3(side * 0.06f, 0.1f, 0.13f), new Vector3(0.07f, 0.015f, 0.07f), new Color(0.6f, 0.9f, 1f), ShadowCastingMode.Off,
                            new Vector3(70f, 0f, 0f), false);
                    break;
                case PetSpecies.Cat:
                    // Capa
                    Part(PrimitiveType.Cube, m.body, new Vector3(0f, 0.36f, -0.06f), new Vector3(0.3f, 0.02f, 0.34f), accent, s, new Vector3(10f, 0f, 0f));
                    break;
                case PetSpecies.Pig:
                    // Coroa pequena
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f * Mathf.Deg2Rad;
                        Part(PrimitiveType.Cube, m.head, new Vector3(Mathf.Sin(a) * 0.05f, 0.17f, Mathf.Cos(a) * 0.05f), new Vector3(0.035f, 0.06f, 0.035f), new Color(1f, 0.8f, 0.25f),
                            ShadowCastingMode.Off, new Vector3(0f, i * 90f, 45f));
                    }
                    break;
                case PetSpecies.Rooster:
                    // Penas da cauda douradas e maiores
                    if (m.tail != null)
                        Part(PrimitiveType.Capsule, m.tail, new Vector3(0f, 0.17f, -0.1f), new Vector3(0.05f, 0.16f, 0.03f), new Color(1f, 0.78f, 0.25f), s, new Vector3(-50f, 0f, 0f));
                    break;
                case PetSpecies.Rabbit:
                    // Lenço ao pescoço com ponta
                    Part(PrimitiveType.Cube, m.head, new Vector3(0.03f, -0.13f, -0.08f), new Vector3(0.08f, 0.1f, 0.02f), accent, s, new Vector3(-20f, 0f, 20f));
                    break;
                default:
                    // Picos dourados na carapaça
                    for (int i = 0; i < 3; i++)
                        Part(PrimitiveType.Capsule, m.body, new Vector3(0f, 0.36f, -0.12f + i * 0.12f), new Vector3(0.04f, 0.05f, 0.04f), new Color(1f, 0.8f, 0.3f), s);
                    break;
            }
        }

        static void Legs(PetModel m, Color c, float x, float zFront, float height, float thickness, ShadowCastingMode s)
        {
            for (int i = 0; i < 4; i++)
            {
                float sx = i % 2 == 0 ? -x : x;
                float sz = i < 2 ? zFront : -zFront;
                var leg = Node("Leg", m.body, new Vector3(sx, height + 0.02f, sz));
                Part(PrimitiveType.Capsule, leg, new Vector3(0f, -height * 0.5f, 0f), new Vector3(thickness, height * 0.6f, thickness), c, s);
                m.legs.Add(leg);
            }
        }

        static void Eyes(Transform head, float spacing, float up)
        {
            float r = head.GetChild(0).localScale.x * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                var p = new Vector3(side * spacing * 0.5f, up, r * 0.86f);
                Part(PrimitiveType.Sphere, head, p, new Vector3(0.07f, 0.08f, 0.04f) * (r / 0.15f) * 0.6f, Color.white, ShadowCastingMode.Off, default, false);
                Part(PrimitiveType.Sphere, head, p + new Vector3(0f, -0.004f, 0.018f), new Vector3(0.035f, 0.045f, 0.02f) * (r / 0.15f) * 0.6f, Ink, ShadowCastingMode.Off, default, false);
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
    }
}
