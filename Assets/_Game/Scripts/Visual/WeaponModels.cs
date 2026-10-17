using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Uma arma/objeto construído por código. +Z = direção do disparo; origem = mão.</summary>
    public class WeaponModel
    {
        public Transform root;
        public readonly List<Transform> muzzles = new List<Transform>();
        public readonly List<Renderer> glowRenderers = new List<Renderer>();
        /// <summary>Peças animadas opcionais (dependem do estilo).</summary>
        public Transform drum, pump, stringTop, stringBottom, nock, arrows, fuseTip, scope;
        public float stringLength, nockRestZ;

        public Vector3 Muzzle(int i) => muzzles.Count == 0 ? root.position : muzzles[Mathf.Abs(i) % muzzles.Count].position;
    }

    /// <summary>
    /// Modelos procedurais das armas de cada estilo (revólver, dinamite, arco, lata de spray, caçadeira, sniper).
    /// Formas grandes e arredondadas, cores fortes e contorno — legíveis à distância, ao estilo de jogos arcade.
    /// </summary>
    public static class WeaponModels
    {
        static readonly Color Metal = new Color(0.32f, 0.35f, 0.42f);
        static readonly Color DarkMetal = new Color(0.18f, 0.19f, 0.24f);
        static readonly Color Wood = new Color(0.55f, 0.33f, 0.18f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.25f);

        /// <summary>Constrói a arma de um estilo. 'hand' indica a mão (0 = direita, 1 = esquerda) para estilos duplos.</summary>
        public static WeaponModel Build(RigStyle style, Transform parent, Color accent, int hand, ShadowCastingMode shadows)
        {
            var w = new WeaponModel();
            var root = new GameObject("Weapon_" + style).transform;
            root.SetParent(parent, false);
            w.root = root;

            switch (style)
            {
                case RigStyle.DualRevolvers: Revolver(w, accent, shadows); break;
                case RigStyle.DualExplosives: Dynamite(w, accent, shadows); break;
                case RigStyle.Bow: Bow(w, accent, shadows); break;
                case RigStyle.SprayCan: SprayCan(w, accent, shadows); break;
                case RigStyle.Shotgun: Shotgun(w, accent, shadows); break;
                case RigStyle.Sniper: Sniper(w, accent, shadows); break;
                case RigStyle.DualSMG: Smg(w, accent, shadows); break;
                case RigStyle.DualSprayCans: SprayCan(w, accent, shadows); break;
                default: Blaster(w, accent, shadows); break;
            }
            return w;
        }

        /// <summary>Estilos com uma arma em cada mão.</summary>
        public static bool IsDual(RigStyle style) =>
            style == RigStyle.DualRevolvers || style == RigStyle.DualExplosives || style == RigStyle.DualSMG || style == RigStyle.DualSprayCans;

        // ------------------------------------------------------------------ metralhadora compacta (Ekko)

        static void Smg(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            // Punho + carregador vertical comprido + corpo quadrado + cano curto com supressor de chamas
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.07f, -0.03f), new Vector3(0.05f, 0.12f, 0.06f), DarkMetal, s, new Vector3(-14f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.1f, 0.07f), new Vector3(0.045f, 0.16f, 0.05f), accent, s, new Vector3(8f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.075f, 0.085f, 0.22f), Metal, s);
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.07f, 0.03f), new Vector3(0.05f, 0.025f, 0.14f), DarkMetal, ShadowCastingMode.Off);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0.2f), new Vector3(0.035f, 0.06f, 0.035f), DarkMetal, s, new Vector3(90f, 0f, 0f));
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0.27f), new Vector3(0.05f, 0.025f, 0.05f), accent, s, new Vector3(90f, 0f, 0f));
            // Coronha curta
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.01f, -0.1f), new Vector3(0.03f, 0.05f, 0.08f), DarkMetal, ShadowCastingMode.Off);
            P(w, PrimitiveType.Cube, new Vector3(0.04f, 0.035f, 0.05f), new Vector3(0.012f, 0.02f, 0.06f), Gold, ShadowCastingMode.Off);
            Muzzle(w, new Vector3(0f, 0.025f, 0.3f));
        }

        // ------------------------------------------------------------------ Minigun (especial do Ekko)

        /// <summary>Minigun segura com as duas mãos: 'drum' é o conjunto de canos que roda ao disparar.</summary>
        public static WeaponModel BuildMinigun(Transform parent, Color accent, ShadowCastingMode s)
        {
            var w = new WeaponModel();
            var root = new GameObject("Weapon_Minigun").transform;
            root.SetParent(parent, false);
            w.root = root;
            // Corpo e caixa de munições
            P(w, PrimitiveType.Cube, new Vector3(0f, 0f, 0.02f), new Vector3(0.16f, 0.15f, 0.26f), DarkMetal, s);
            P(w, PrimitiveType.Cube, new Vector3(-0.14f, -0.06f, 0f), new Vector3(0.12f, 0.14f, 0.18f), accent, s);
            P(w, PrimitiveType.Cube, new Vector3(-0.14f, 0.02f, 0f), new Vector3(0.125f, 0.02f, 0.185f), Gold, ShadowCastingMode.Off);
            // Pegas (traseira e de cima)
            P(w, PrimitiveType.Cylinder, new Vector3(0f, -0.12f, -0.06f), new Vector3(0.035f, 0.06f, 0.035f), Wood, s);
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.02f), new Vector3(0.03f, 0.05f, 0.14f), Metal, s);
            // Canos rotativos
            var barrels = new GameObject("Barrels").transform;
            barrels.SetParent(root, false);
            barrels.localPosition = new Vector3(0f, 0f, 0.36f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                var off = new Vector3(Mathf.Cos(a) * 0.045f, Mathf.Sin(a) * 0.045f, 0f);
                P(w, PrimitiveType.Cylinder, off, new Vector3(0.028f, 0.2f, 0.028f), Metal, s, new Vector3(90f, 0f, 0f), barrels);
            }
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.12f), new Vector3(0.14f, 0.02f, 0.14f), accent, ShadowCastingMode.Off, new Vector3(90f, 0f, 0f), barrels);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.14f), new Vector3(0.15f, 0.025f, 0.15f), DarkMetal, ShadowCastingMode.Off, new Vector3(90f, 0f, 0f), barrels);
            w.drum = barrels;
            Muzzle(w, new Vector3(0f, 0f, 0.58f));
            return w;
        }

        // ------------------------------------------------------------------ acessórios de skin (só visuais)

        /// <summary>Acessório de uma skin na cabeça/costas (coroa, auréola, capa, lenço, cornos, antenas).</summary>
        public static void BuildAccessory(SkinAccessory accessory, Transform head, Transform spine, float headTop, Color accent, Color gear, ShadowCastingMode s)
        {
            switch (accessory)
            {
                case SkinAccessory.Crown:
                {
                    var c = new GameObject("Crown").transform;
                    c.SetParent(head, false);
                    c.localPosition = new Vector3(0f, headTop + 0.12f, 0f);
                    var gold = new Color(1f, 0.82f, 0.25f);
                    Art.Part(PrimitiveType.Cylinder, c, Vector3.zero, new Vector3(0.32f, 0.05f, 0.32f), gold, true, default, s);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        var pos = new Vector3(Mathf.Sin(a) * 0.13f, 0.08f, Mathf.Cos(a) * 0.13f);
                        Art.Part(PrimitiveType.Cube, c, pos, new Vector3(0.05f, 0.1f, 0.05f), gold, true, new Vector3(0f, i * 72f, 45f), ShadowCastingMode.Off);
                    }
                    break;
                }
                case SkinAccessory.Halo:
                {
                    var h = Art.Part(PrimitiveType.Cylinder, head, new Vector3(0f, headTop + 0.32f, 0f), new Vector3(0.42f, 0.015f, 0.42f), Color.Lerp(accent, Color.white, 0.5f), false, default, ShadowCastingMode.Off);
                    Art.SetEmission(h.GetComponent<Renderer>(), accent * 0.6f);
                    break;
                }
                case SkinAccessory.Horns:
                    for (int side = -1; side <= 1; side += 2)
                        Art.Part(PrimitiveType.Capsule, head, new Vector3(side * 0.18f, headTop + 0.1f, 0.04f), new Vector3(0.07f, 0.12f, 0.07f), Art.Darken(accent, 0.7f), true,
                            new Vector3(0f, 0f, side * -28f), s);
                    break;
                case SkinAccessory.Antenna:
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Art.Part(PrimitiveType.Cylinder, head, new Vector3(side * 0.1f, headTop + 0.18f, 0f), new Vector3(0.015f, 0.12f, 0.015f), Art.Darken(gear, 0.6f), false,
                            new Vector3(0f, 0f, side * -15f), ShadowCastingMode.Off);
                        var tip = Art.Part(PrimitiveType.Sphere, head, new Vector3(side * 0.14f, headTop + 0.31f, 0f), Vector3.one * 0.06f, accent, false, default, ShadowCastingMode.Off);
                        Art.SetEmission(tip.GetComponent<Renderer>(), accent * 0.5f);
                    }
                    break;
                case SkinAccessory.Scarf:
                    if (spine != null)
                    {
                        Art.Part(PrimitiveType.Sphere, spine, new Vector3(0f, 0.56f, 0.04f), new Vector3(0.46f, 0.15f, 0.42f), accent, true, default, s);
                        Art.Part(PrimitiveType.Cube, spine, new Vector3(0.12f, 0.42f, -0.2f), new Vector3(0.1f, 0.22f, 0.04f), accent, true, new Vector3(-20f, 0f, 10f), s);
                    }
                    break;
                case SkinAccessory.Cape:
                    if (spine != null)
                        Art.Part(PrimitiveType.Cube, spine, new Vector3(0f, 0.22f, -0.27f), new Vector3(0.6f, 0.62f, 0.03f), accent, true, new Vector3(12f, 0f, 0f), s);
                    break;
            }
        }

        static Transform P(WeaponModel w, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, ShadowCastingMode s, Vector3 euler = default, Transform parent = null)
        {
            return Art.Part(t, parent != null ? parent : w.root, pos, scale, c, true, euler, s);
        }

        static Transform Muzzle(WeaponModel w, Vector3 pos, Transform parent = null)
        {
            var m = new GameObject("Muzzle").transform;
            m.SetParent(parent != null ? parent : w.root, false);
            m.localPosition = pos;
            w.muzzles.Add(m);
            return m;
        }

        // ------------------------------------------------------------------ revólver (pistoleiro)

        static void Revolver(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            // Cabo inclinado
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.07f, -0.04f), new Vector3(0.055f, 0.13f, 0.07f), Wood, s, new Vector3(-18f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.12f, -0.055f), new Vector3(0.06f, 0.025f, 0.075f), Gold, s, new Vector3(-18f, 0f, 0f));
            // Corpo
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.015f, 0.04f), new Vector3(0.06f, 0.075f, 0.15f), Metal, s);
            // Tambor (gira na recarga)
            var drum = new GameObject("Drum").transform;
            drum.SetParent(w.root, false);
            drum.localPosition = new Vector3(0f, 0.018f, 0.045f);
            P(w, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.085f, 0.035f, 0.085f), DarkMetal, s, new Vector3(90f, 0f, 0f), drum);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                P(w, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.026f, Mathf.Sin(a) * 0.026f, 0.035f), Vector3.one * 0.018f, Gold, ShadowCastingMode.Off, default, drum);
            }
            w.drum = drum;
            // Cano
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0.2f), new Vector3(0.036f, 0.1f, 0.036f), Metal, s, new Vector3(90f, 0f, 0f));
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.035f, 0.3f), new Vector3(0.046f, 0.012f, 0.046f), accent, s, new Vector3(90f, 0f, 0f));
            // Mira e cão
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.28f), new Vector3(0.012f, 0.02f, 0.02f), Gold, ShadowCastingMode.Off);
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.06f, -0.03f), new Vector3(0.02f, 0.03f, 0.03f), DarkMetal, ShadowCastingMode.Off, new Vector3(-30f, 0f, 0f));
            Muzzle(w, new Vector3(0f, 0.035f, 0.32f));
        }

        // ------------------------------------------------------------------ dinamite (artilheiro)

        static void Dynamite(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            var stick = new GameObject("Stick").transform;
            stick.SetParent(w.root, false);
            stick.localPosition = new Vector3(0f, 0.05f, 0.02f);
            stick.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            BuildDynamiteStick(stick, accent, s, false);
            w.arrows = stick; // reutiliza "arrows" como o objeto que desaparece ao lançar
            var tip = new GameObject("FuseTip").transform;
            tip.SetParent(stick, false);
            tip.localPosition = new Vector3(0f, 0.17f, 0f);
            w.fuseTip = tip;
            w.muzzles.Add(tip);
        }

        /// <summary>Pau de dinamite vertical (centro na origem). Usado na mão e como projétil.</summary>
        public static void BuildDynamiteStick(Transform parent, Color accent, ShadowCastingMode s, bool big)
        {
            float k = big ? 1.6f : 1f;
            Art.Part(PrimitiveType.Cylinder, parent, Vector3.zero, new Vector3(0.075f, 0.11f, 0.075f) * k, new Color(0.9f, 0.18f, 0.15f), true, default, s);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.03f * k, 0f), new Vector3(0.08f, 0.018f, 0.08f) * k, new Color(0.98f, 0.9f, 0.7f), false, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, -0.05f * k, 0f), new Vector3(0.08f, 0.012f, 0.08f) * k, accent, false, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.14f * k, 0f), new Vector3(0.012f, 0.035f, 0.012f) * k, new Color(0.15f, 0.12f, 0.1f), false, new Vector3(0f, 0f, 15f), ShadowCastingMode.Off);
        }

        /// <summary>Barril explosivo (Super do artilheiro).</summary>
        public static void BuildBarrel(Transform parent, Color accent, ShadowCastingMode s)
        {
            Art.Part(PrimitiveType.Cylinder, parent, Vector3.zero, new Vector3(0.42f, 0.26f, 0.42f), new Color(0.6f, 0.36f, 0.2f), true, default, s);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.16f, 0f), new Vector3(0.45f, 0.03f, 0.45f), DarkMetal, false, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, -0.16f, 0f), new Vector3(0.45f, 0.03f, 0.45f), DarkMetal, false, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cube, parent, new Vector3(0f, 0f, 0.2f), new Vector3(0.18f, 0.18f, 0.04f), accent, false, new Vector3(0f, 0f, 45f), ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0.32f, 0f), new Vector3(0.025f, 0.07f, 0.025f), new Color(0.15f, 0.12f, 0.1f), false, new Vector3(0f, 0f, 20f), ShadowCastingMode.Off);
        }

        // ------------------------------------------------------------------ arco (armadilheiro)

        static void Bow(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            // Arco vertical à frente da mão, corda para trás
            const float R = 0.42f;
            const int segs = 7;
            float maxA = 62f;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(-maxA, maxA, i / (float)segs) * Mathf.Deg2Rad;
                var p = new Vector3(0f, Mathf.Sin(a) * R, (Mathf.Cos(a) - 1f) * R * 0.75f + 0.04f);
                if (i > 0)
                {
                    var mid = (p + prev) * 0.5f;
                    var d = p - prev;
                    float len = d.magnitude;
                    var rot = Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles;
                    bool grip = i == segs / 2 + 1 || i == segs / 2;
                    P(w, PrimitiveType.Capsule, mid, new Vector3(grip ? 0.05f : 0.035f, len * 0.62f, grip ? 0.05f : 0.035f), grip ? accent : Wood, s, rot);
                }
                prev = p;
            }
            float tipY = Mathf.Sin(maxA * Mathf.Deg2Rad) * R;
            float tipZ = (Mathf.Cos(maxA * Mathf.Deg2Rad) - 1f) * R * 0.75f + 0.04f;
            // Pontas decorativas
            P(w, PrimitiveType.Sphere, new Vector3(0f, tipY, tipZ), Vector3.one * 0.045f, accent, ShadowCastingMode.Off);
            P(w, PrimitiveType.Sphere, new Vector3(0f, -tipY, tipZ), Vector3.one * 0.045f, accent, ShadowCastingMode.Off);

            // Corda: duas metades que se juntam no "nock" (animado ao puxar)
            var nock = new GameObject("Nock").transform;
            nock.SetParent(w.root, false);
            nock.localPosition = new Vector3(0f, 0f, tipZ);
            w.nock = nock;
            w.nockRestZ = tipZ;
            w.stringTop = Art.Part(PrimitiveType.Cylinder, w.root, Vector3.zero, new Vector3(0.008f, 0.1f, 0.008f), new Color(0.95f, 0.95f, 0.9f), false, default, ShadowCastingMode.Off);
            w.stringBottom = Art.Part(PrimitiveType.Cylinder, w.root, Vector3.zero, new Vector3(0.008f, 0.1f, 0.008f), new Color(0.95f, 0.95f, 0.9f), false, default, ShadowCastingMode.Off);
            w.stringLength = tipY;
            PoseBow(w, 0f, 0f);

            // Três flechas encaixadas no nock (leque ligeiro)
            var arrows = new GameObject("Arrows").transform;
            arrows.SetParent(nock, false);
            w.arrows = arrows;
            for (int i = -1; i <= 1; i++)
            {
                var a = new GameObject("Arrow").transform;
                a.SetParent(arrows, false);
                a.localRotation = Quaternion.Euler(0f, i * 6f, 0f);
                BuildArrow(a, accent, ShadowCastingMode.Off, 0.62f);
                if (i == 0) Muzzle(w, new Vector3(0f, 0f, 0.62f), a);
            }
        }

        /// <summary>Flecha ao longo de +Z (cauda na origem).</summary>
        public static void BuildArrow(Transform parent, Color accent, ShadowCastingMode s, float length)
        {
            Art.Part(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, length * 0.5f), new Vector3(0.016f, length * 0.5f, 0.016f), new Color(0.75f, 0.55f, 0.32f), false, new Vector3(90f, 0f, 0f), s);
            Art.Part(PrimitiveType.Sphere, parent, new Vector3(0f, 0f, length), new Vector3(0.05f, 0.05f, 0.11f), accent, true, default, s);
            Art.Part(PrimitiveType.Cube, parent, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.006f, 0.04f, 0.09f), Color.white, false, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cube, parent, new Vector3(0f, -0.02f, 0.05f), new Vector3(0.006f, 0.04f, 0.09f), Color.white, false, default, ShadowCastingMode.Off);
        }

        // ------------------------------------------------------------------ lata de spray

        static void SprayCan(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            var can = new GameObject("Can").transform;
            can.SetParent(w.root, false);
            can.localPosition = new Vector3(0f, 0.04f, 0.05f);
            P(w, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.11f, 0.13f, 0.11f), accent, s, default, can);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(0.115f, 0.04f, 0.115f), Color.white, ShadowCastingMode.Off, default, can);
            P(w, PrimitiveType.Sphere, new Vector3(0f, 0.13f, 0f), new Vector3(0.1f, 0.06f, 0.1f), new Color(0.85f, 0.88f, 0.92f), s, default, can);
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.175f, 0.015f), new Vector3(0.03f, 0.035f, 0.05f), DarkMetal, ShadowCastingMode.Off, default, can);
            // Pequena caveira/estrela pintada (rótulo)
            P(w, PrimitiveType.Sphere, new Vector3(0f, 0.02f, 0.055f), new Vector3(0.05f, 0.05f, 0.01f), Art.Lighten(accent, 0.6f), ShadowCastingMode.Off, default, can);
            w.scope = can;
            Muzzle(w, new Vector3(0f, 0.18f, 0.05f), can);
        }

        // ------------------------------------------------------------------ caçadeira

        static void Shotgun(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            // Coronha + corpo + cano largo + pump
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.03f, -0.16f), new Vector3(0.07f, 0.11f, 0.22f), Wood, s, new Vector3(8f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.04f), new Vector3(0.085f, 0.09f, 0.2f), DarkMetal, s);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0.3f), new Vector3(0.075f, 0.18f, 0.075f), Metal, s, new Vector3(90f, 0f, 0f));
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0.48f), new Vector3(0.095f, 0.02f, 0.095f), accent, s, new Vector3(90f, 0f, 0f));
            var pump = new GameObject("Pump").transform;
            pump.SetParent(w.root, false);
            pump.localPosition = new Vector3(0f, -0.015f, 0.27f);
            P(w, PrimitiveType.Cube, Vector3.zero, new Vector3(0.08f, 0.06f, 0.14f), Wood, s, default, pump);
            w.pump = pump;
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.09f, 0.08f), new Vector3(0.02f, 0.02f, 0.05f), Gold, ShadowCastingMode.Off);
            Muzzle(w, new Vector3(0f, 0.04f, 0.5f));
        }

        // ------------------------------------------------------------------ sniper

        static void Sniper(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.04f, -0.18f), new Vector3(0.06f, 0.1f, 0.24f), Wood, s, new Vector3(6f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0.06f), new Vector3(0.07f, 0.075f, 0.26f), new Color(0.3f, 0.36f, 0.24f), s);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0.42f), new Vector3(0.04f, 0.22f, 0.04f), DarkMetal, s, new Vector3(90f, 0f, 0f));
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.025f, 0.65f), new Vector3(0.058f, 0.04f, 0.058f), accent, s, new Vector3(90f, 0f, 0f));
            // Mira telescópica com lente brilhante
            var scope = new GameObject("Scope").transform;
            scope.SetParent(w.root, false);
            scope.localPosition = new Vector3(0f, 0.085f, 0.05f);
            P(w, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.05f, 0.1f, 0.05f), DarkMetal, s, new Vector3(90f, 0f, 0f), scope);
            var lens = P(w, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.1f), new Vector3(0.045f, 0.045f, 0.015f), accent, ShadowCastingMode.Off, default, scope);
            w.glowRenderers.Add(lens.GetComponent<Renderer>());
            w.scope = scope;
            // Ferrolho
            P(w, PrimitiveType.Sphere, new Vector3(0.045f, 0.03f, -0.02f), Vector3.one * 0.03f, Gold, ShadowCastingMode.Off);
            Muzzle(w, new Vector3(0f, 0.025f, 0.68f));
        }

        // ------------------------------------------------------------------ genérico

        static void Blaster(WeaponModel w, Color accent, ShadowCastingMode s)
        {
            P(w, PrimitiveType.Cube, new Vector3(0f, -0.06f, -0.02f), new Vector3(0.05f, 0.12f, 0.06f), DarkMetal, s, new Vector3(-12f, 0f, 0f));
            P(w, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.08f), new Vector3(0.08f, 0.09f, 0.24f), accent, s);
            P(w, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.25f), new Vector3(0.05f, 0.06f, 0.05f), Metal, s, new Vector3(90f, 0f, 0f));
            Muzzle(w, new Vector3(0f, 0.03f, 0.32f));
        }

        /// <summary>Atualiza a corda do arco: draw 0 = solta, 1 = puxada.</summary>
        public static void PoseBow(WeaponModel w, float draw, float vibrate)
        {
            if (w == null || w.nock == null || w.stringTop == null) return;
            var rest = new Vector3(0f, 0f, w.nockRestZ);
            var nockPos = rest + new Vector3(0f, 0f, -0.22f * Mathf.Clamp01(draw) + Mathf.Sin(Time.time * 70f) * 0.02f * vibrate);
            w.nock.localPosition = nockPos;
            var top = new Vector3(0f, w.stringLength, rest.z);
            var bottom = new Vector3(0f, -w.stringLength, rest.z);
            PlaceString(w.stringTop, top, nockPos);
            PlaceString(w.stringBottom, bottom, nockPos);
        }

        static void PlaceString(Transform s, Vector3 a, Vector3 b)
        {
            var d = b - a;
            s.localPosition = (a + b) * 0.5f;
            s.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            s.localScale = new Vector3(0.008f, d.magnitude * 0.5f, 0.008f);
        }
    }
}
