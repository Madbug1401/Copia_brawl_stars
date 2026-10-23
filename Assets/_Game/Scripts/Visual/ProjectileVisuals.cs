using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Objeto visual de um projétil em voo (pooled).</summary>
    public class ProjVisual
    {
        public ProjectileStyle style;
        public Transform root;
        public Transform spin;
        public Transform fuseTip;
        public TrailRenderer trail;
        public readonly List<Renderer> tint = new List<Renderer>();
        public readonly List<Renderer> all = new List<Renderer>();
        public Color color;
        public float age;
        public bool spinning;
        public float nextSpark;
        public float baseScale = 1f;
    }

    /// <summary>
    /// Modelos dos projéteis: balas brilhantes com rasto, bagos de caçadeira, flechas, dinamites a girar com rastilho aceso,
    /// barril, bombas e orbes. Também as minas. Tudo reutilizado (pool) por estilo.
    /// </summary>
    public static class ProjectileVisuals
    {
        static readonly Dictionary<ProjectileStyle, Stack<ProjVisual>> s_Pools = new Dictionary<ProjectileStyle, Stack<ProjVisual>>();
        static readonly Stack<ProjVisual> s_Mines = new Stack<ProjVisual>();
        static readonly Stack<ProjVisual> s_Snares = new Stack<ProjVisual>();
        static Transform s_Parent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Pools.Clear();
            s_Mines.Clear();
            s_Snares.Clear();
            s_Parent = null;
        }

        static Transform Parent
        {
            get
            {
                if (s_Parent == null) s_Parent = new GameObject("[Projectiles]").transform;
                return s_Parent;
            }
        }

        public static ProjectileStyle Resolve(AttackDefinition attack)
        {
            var style = attack.visuals.style;
            if (style != ProjectileStyle.Auto) return style;
            if (attack.delivery is ArcProjectileDelivery) return attack.visuals.scale > 2.5f ? ProjectileStyle.Barrel : ProjectileStyle.Dynamite;
            return ProjectileStyle.Bullet;
        }

        // ================================================================== projéteis

        public static ProjVisual Take(ProjectileStyle style, Color color, float scale, Vector3 pos, Vector3 velocity)
        {
            if (!s_Pools.TryGetValue(style, out var pool)) s_Pools[style] = pool = new Stack<ProjVisual>();
            ProjVisual v = null;
            while (pool.Count > 0 && (v == null || v.root == null)) v = pool.Pop();
            if (v == null || v.root == null) v = Create(style);

            v.root.gameObject.SetActive(true);
            v.root.position = pos;
            v.root.rotation = velocity.sqrMagnitude > 0.001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
            v.baseScale = Mathf.Clamp(scale, 0.3f, 4f);
            v.root.localScale = Vector3.one * v.baseScale;
            v.age = 0f;
            v.nextSpark = 0f;
            v.color = color;
            v.spinning = true;
            if (v.spin != null) v.spin.localRotation = Quaternion.identity;
            foreach (var r in v.tint) Art.SetColor(r, color);
            if (v.trail != null)
            {
                v.trail.Clear();
                v.trail.emitting = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.Lerp(color, Color.white, 0.5f), 0f), new GradientColorKey(color, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                v.trail.colorGradient = g;
                float w = TrailWidth(style) * Mathf.Clamp(scale, 0.5f, 3f);
                v.trail.widthMultiplier = w;
            }
            return v;
        }

        public static void Step(ProjVisual v, Vector3 pos, Vector3 velocity, float dt)
        {
            if (v == null || v.root == null) return;
            v.age += dt;
            v.root.position = pos;
            if (velocity.sqrMagnitude > 0.001f) v.root.rotation = Quaternion.LookRotation(velocity);
            if (v.spin != null && v.spinning)
            {
                float rate = v.style == ProjectileStyle.Arrow ? 0f : (v.style == ProjectileStyle.Barrel ? 380f : 720f);
                if (v.style == ProjectileStyle.Bullet || v.style == ProjectileStyle.Pellet) rate = 0f;
                v.spin.localRotation *= Quaternion.Euler(rate * dt, 0f, 0f);
            }
            if (v.fuseTip != null && v.age >= v.nextSpark)
            {
                v.nextSpark = v.age + 0.03f;
                // A granada de veneno larga gás verde/roxo em vez de faíscas
                var spark = v.style == ProjectileStyle.Grenade
                    ? (Random.value < 0.5f ? new Color(0.45f, 1f, 0.35f) : new Color(0.75f, 0.4f, 1f))
                    : new Color(1f, 0.75f, 0.3f);
                FPSLAN.Fx.Glow(v.fuseTip.position, Random.insideUnitSphere * 0.6f, spark, 0.12f, 0.22f);
            }
        }

        /// <summary>O projétil aterrou e fica no chão (rastilho): deixa de girar e deita-se.</summary>
        public static void Land(ProjVisual v, Vector3 pos)
        {
            if (v == null || v.root == null) return;
            v.spinning = false;
            if (v.trail != null) v.trail.emitting = false;
            v.root.position = pos;
            var yaw = v.root.eulerAngles.y;
            if (v.style == ProjectileStyle.Barrel || v.style == ProjectileStyle.Bomb)
                v.root.rotation = Quaternion.Euler(0f, yaw, 0f);
            else
                v.root.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, 0f);
            if (v.spin != null)
                v.spin.localRotation = v.style == ProjectileStyle.Dynamite ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        }

        /// <summary>Pisca enquanto o rastilho arde (k = 0..1 do tempo até explodir).</summary>
        public static void Blink(ProjVisual v, float k)
        {
            if (v == null || v.root == null) return;
            float rate = Mathf.Lerp(8f, 30f, k);
            bool on = Mathf.Sin(Time.time * rate) > 0f;
            var e = on ? new Color(1f, 0.35f, 0.15f) * (0.4f + k * 0.6f) : Color.black;
            foreach (var r in v.all) Art.SetEmission(r, e);
            float pulse = 1f + (on ? 0.08f + k * 0.12f : 0f);
            v.root.localScale = Vector3.one * pulse * v.baseScale;
            if (v.fuseTip != null && Random.value < 0.6f)
                FPSLAN.Fx.Glow(v.fuseTip.position, Random.insideUnitSphere * 0.8f + Vector3.up * 0.6f, new Color(1f, 0.75f, 0.3f), 0.12f, 0.25f);
        }

        public static void Release(ProjVisual v)
        {
            if (v == null || v.root == null) return;
            foreach (var r in v.all) Art.SetEmission(r, Color.black);
            if (v.trail != null)
            {
                v.trail.emitting = false;
                v.trail.Clear();
            }
            v.root.gameObject.SetActive(false);
            if (v.style == (ProjectileStyle)(-1)) { s_Mines.Push(v); return; }
            if (v.style == (ProjectileStyle)(-2)) { s_Snares.Push(v); return; }
            if (!s_Pools.TryGetValue(v.style, out var pool)) s_Pools[v.style] = pool = new Stack<ProjVisual>();
            pool.Push(v);
        }

        static float TrailWidth(ProjectileStyle style)
        {
            switch (style)
            {
                case ProjectileStyle.Bullet: return 0.11f;
                case ProjectileStyle.Pellet: return 0.08f;
                case ProjectileStyle.Arrow: return 0.05f;
                case ProjectileStyle.Orb: return 0.25f;
                default: return 0.06f;
            }
        }

        static ProjVisual Create(ProjectileStyle style)
        {
            var v = new ProjVisual { style = style };
            v.root = new GameObject("Proj_" + style).transform;
            v.root.SetParent(Parent, false);
            v.spin = new GameObject("Spin").transform;
            v.spin.SetParent(v.root, false);
            var s = ShadowCastingMode.Off;
            float trailTime = 0.12f;

            switch (style)
            {
                case ProjectileStyle.Pellet:
                    v.tint.Add(R(Art.FxPart(PrimitiveType.Sphere, v.spin, Vector3.zero, new Vector3(0.11f, 0.11f, 0.2f), Color.white, true)));
                    Art.FxPart(PrimitiveType.Sphere, v.spin, Vector3.zero, Vector3.one * 0.06f, Color.white, true);
                    trailTime = 0.08f;
                    break;

                case ProjectileStyle.Arrow:
                {
                    var a = new GameObject("Arrow").transform;
                    a.SetParent(v.spin, false);
                    a.localPosition = new Vector3(0f, 0f, -0.55f);
                    WeaponModels.BuildArrow(a, Color.white, ShadowCastingMode.On, 0.62f);
                    // a ponta usa a cor do ataque (só na flecha de primitivas; o modelo 3D é um único filho com as
                    // suas cores — o antigo GetChild(1) rebentava aqui e a flecha nunca saía)
                    if (a.childCount > 1 && a.GetChild(1).TryGetComponent<Renderer>(out var head)) v.tint.Add(head);
                    v.tint.Add(R(Art.FxPart(PrimitiveType.Sphere, v.spin, new Vector3(0f, 0f, 0.07f), Vector3.one * 0.14f, Color.white, true)));
                    trailTime = 0.1f;
                    break;
                }

                case ProjectileStyle.Dynamite:
                {
                    var stick = new GameObject("Stick").transform;
                    stick.SetParent(v.spin, false);
                    stick.localScale = Vector3.one * 1.4f;
                    WeaponModels.BuildDynamiteStick(stick, new Color(1f, 0.8f, 0.3f), ShadowCastingMode.On, false);
                    var tip = new GameObject("FuseTip").transform;
                    tip.SetParent(stick, false);
                    tip.localPosition = new Vector3(0f, 0.17f, 0f);
                    v.fuseTip = tip;
                    trailTime = 0.2f;
                    break;
                }

                case ProjectileStyle.Barrel:
                {
                    var b = new GameObject("Barrel").transform;
                    b.SetParent(v.spin, false);
                    b.localScale = Vector3.one * 0.55f;
                    WeaponModels.BuildBarrel(b, new Color(1f, 0.85f, 0.2f), ShadowCastingMode.On);
                    var tip = new GameObject("FuseTip").transform;
                    tip.SetParent(b, false);
                    tip.localPosition = new Vector3(0f, 0.38f, 0f);
                    v.fuseTip = tip;
                    trailTime = 0.25f;
                    break;
                }

                case ProjectileStyle.Bomb:
                {
                    Art.Part(PrimitiveType.Sphere, v.spin, Vector3.zero, Vector3.one * 0.2f, new Color(0.15f, 0.15f, 0.2f), true, default, ShadowCastingMode.On);
                    Art.Part(PrimitiveType.Cylinder, v.spin, new Vector3(0f, 0.1f, 0f), new Vector3(0.08f, 0.03f, 0.08f), new Color(0.5f, 0.5f, 0.55f), false, default, s);
                    var band = Art.Part(PrimitiveType.Cylinder, v.spin, Vector3.zero, new Vector3(0.205f, 0.02f, 0.205f), Color.white, false, default, s);
                    v.tint.Add(band.GetComponent<Renderer>());
                    var tip = new GameObject("FuseTip").transform;
                    tip.SetParent(v.spin, false);
                    tip.localPosition = new Vector3(0f, 0.15f, 0f);
                    v.fuseTip = tip;
                    trailTime = 0.15f;
                    break;
                }

                case ProjectileStyle.Orb:
                    v.tint.Add(R(Art.FxPart(PrimitiveType.Sphere, v.spin, Vector3.zero, Vector3.one * 0.3f, Color.white, true)));
                    Art.FxPart(PrimitiveType.Sphere, v.spin, Vector3.zero, Vector3.one * 0.15f, Color.white, true);
                    trailTime = 0.2f;
                    break;

                case ProjectileStyle.Tnt:
                {
                    // Molho de três paus de TNT atados com fita, rastilho aceso
                    var bundle = new GameObject("Tnt").transform;
                    bundle.SetParent(v.spin, false);
                    // o mesmo molho de TNT (modelo 3D) que o Lupus tem na mão, se existir
                    if (Resources.Load<GameObject>(WeaponModels.PrefabFolder + RigStyle.DualExplosives) != null)
                    {
                        WeaponModels.BuildDynamiteStick(bundle, Color.white, ShadowCastingMode.On, false);
                        foreach (var t in bundle.GetComponentsInChildren<Transform>(true))
                            if (t.name == "FuseTip") { v.fuseTip = t; break; }
                        if (v.fuseTip == null)
                        {
                            v.fuseTip = new GameObject("FuseTip").transform;
                            v.fuseTip.SetParent(bundle, false);
                            v.fuseTip.localPosition = new Vector3(0f, 0.12f, 0f);
                        }
                        trailTime = 0.22f;
                        break;
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 120f * Mathf.Deg2Rad;
                        Art.Part(PrimitiveType.Cylinder, bundle, new Vector3(Mathf.Cos(a) * 0.075f, 0f, Mathf.Sin(a) * 0.075f), new Vector3(0.12f, 0.17f, 0.12f),
                            new Color(0.88f, 0.16f, 0.14f), true, default, ShadowCastingMode.On);
                    }
                    var tape = Art.Part(PrimitiveType.Cylinder, bundle, Vector3.zero, new Vector3(0.27f, 0.035f, 0.27f), Color.white, false, default, s);
                    v.tint.Add(tape.GetComponent<Renderer>());
                    Art.Part(PrimitiveType.Cube, bundle, new Vector3(0f, 0f, 0.14f), new Vector3(0.1f, 0.08f, 0.02f), new Color(1f, 0.92f, 0.6f), false, default, s);
                    Art.Part(PrimitiveType.Cylinder, bundle, new Vector3(0f, 0.22f, 0f), new Vector3(0.018f, 0.06f, 0.018f), new Color(0.15f, 0.12f, 0.1f), false, new Vector3(0f, 0f, 18f), s);
                    var tip = new GameObject("FuseTip").transform;
                    tip.SetParent(bundle, false);
                    tip.localPosition = new Vector3(0.02f, 0.28f, 0f);
                    v.fuseTip = tip;
                    trailTime = 0.22f;
                    break;
                }

                case ProjectileStyle.Grenade:
                {
                    // Granada de veneno: lata com faixa colorida e válvula
                    Art.Part(PrimitiveType.Cylinder, v.spin, Vector3.zero, new Vector3(0.16f, 0.13f, 0.16f), new Color(0.25f, 0.27f, 0.3f), true, default, ShadowCastingMode.On);
                    var band = Art.Part(PrimitiveType.Cylinder, v.spin, Vector3.zero, new Vector3(0.165f, 0.05f, 0.165f), Color.white, false, default, s);
                    v.tint.Add(band.GetComponent<Renderer>());
                    Art.Part(PrimitiveType.Sphere, v.spin, new Vector3(0f, 0.13f, 0f), new Vector3(0.12f, 0.07f, 0.12f), new Color(0.55f, 0.58f, 0.62f), false, default, s);
                    Art.Part(PrimitiveType.Cube, v.spin, new Vector3(0.05f, 0.16f, 0f), new Vector3(0.08f, 0.02f, 0.03f), new Color(0.75f, 0.4f, 1f), false, new Vector3(0f, 0f, -20f), s);
                    var tip = new GameObject("FuseTip").transform;
                    tip.SetParent(v.spin, false);
                    tip.localPosition = new Vector3(0f, 0.18f, 0f);
                    v.fuseTip = tip;
                    trailTime = 0.18f;
                    break;
                }

                default: // Bullet
                    v.tint.Add(R(Art.FxPart(PrimitiveType.Capsule, v.spin, Vector3.zero, new Vector3(0.1f, 0.22f, 0.1f), Color.white, true, new Vector3(90f, 0f, 0f))));
                    Art.FxPart(PrimitiveType.Capsule, v.spin, Vector3.zero, new Vector3(0.05f, 0.16f, 0.05f), Color.white, true, new Vector3(90f, 0f, 0f));
                    break;
            }

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(v.root, false);
            v.trail = trailGo.AddComponent<TrailRenderer>();
            v.trail.sharedMaterial = Art.FxBeam;
            v.trail.time = trailTime;
            v.trail.minVertexDistance = 0.15f;
            v.trail.shadowCastingMode = ShadowCastingMode.Off;
            v.trail.receiveShadows = false;
            v.trail.numCapVertices = 2;
            v.trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            v.trail.autodestruct = false;

            v.root.GetComponentsInChildren(true, v.all);
            v.all.Remove(v.trail);
            return v;
        }

        static Renderer R(Transform t) => t.GetComponent<Renderer>();

        // ================================================================== minas

        /// <summary>Mina: disco com cúpula e luz que pisca.</summary>
        public static ProjVisual TakeMine(Color color, Vector3 pos, bool discreet)
        {
            ProjVisual v = null;
            while (s_Mines.Count > 0 && (v == null || v.root == null)) v = s_Mines.Pop();
            if (v == null || v.root == null)
            {
                v = new ProjVisual { style = (ProjectileStyle)(-1) };
                v.root = new GameObject("Mine").transform;
                v.root.SetParent(Parent, false);
                v.spin = v.root;
                Art.Part(PrimitiveType.Cylinder, v.root, new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.04f, 0.6f), new Color(0.25f, 0.27f, 0.32f), true, default, ShadowCastingMode.On);
                var dome = Art.Part(PrimitiveType.Sphere, v.root, new Vector3(0f, 0.08f, 0f), new Vector3(0.36f, 0.16f, 0.36f), Color.white, true, default, ShadowCastingMode.Off);
                v.tint.Add(dome.GetComponent<Renderer>());
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f;
                    Art.Part(PrimitiveType.Cube, v.root, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.05f, 0.3f), new Vector3(0.08f, 0.06f, 0.08f), new Color(0.9f, 0.75f, 0.2f), false, new Vector3(0f, a, 0f), ShadowCastingMode.Off);
                }
                v.root.GetComponentsInChildren(true, v.all);
            }
            v.root.gameObject.SetActive(true);
            v.root.position = pos;
            v.root.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            v.baseScale = discreet ? 0.75f : 1f;
            v.root.localScale = Vector3.one * v.baseScale;
            v.color = color;
            foreach (var r in v.tint) Art.SetColor(r, discreet ? Art.Darken(color, 0.4f) : color);
            foreach (var r in v.all) Art.SetFloat(r, Art.FadeId, discreet ? 0.45f : 1f);
            return v;
        }

        public static void PoseMine(ProjVisual v, bool armed, bool warning, bool ownerView, float age)
        {
            if (v == null || v.root == null) return;
            if (warning)
            {
                bool on = Mathf.Sin(age * 50f) > 0f;
                foreach (var r in v.tint) Art.SetEmission(r, on ? new Color(1f, 0.2f, 0.1f) : Color.black);
                foreach (var r in v.all) Art.SetFloat(r, Art.FadeId, 1f);
                v.root.localScale = Vector3.one * (1f + Mathf.Abs(Mathf.Sin(age * 25f)) * 0.25f);
            }
            if (!warning && armed && ownerView)
            {
                float pulse = 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(age * 4f));
                foreach (var r in v.tint) Art.SetEmission(r, v.color * pulse);
            }
        }

        public static void ReleaseMine(ProjVisual v)
        {
            if (v == null || v.root == null) return;
            foreach (var r in v.all)
            {
                Art.SetEmission(r, Color.black);
                Art.SetFloat(r, Art.FadeId, 1f);
            }
            v.root.gameObject.SetActive(false);
            s_Mines.Push(v);
        }

        // ================================================================== armadilhas de imobilização

        /// <summary>Armadilha de mandíbulas: aro com dentes abertos e placa central que brilha quando armada.</summary>
        public static ProjVisual TakeSnare(Color color, Vector3 pos, bool discreet)
        {
            ProjVisual v = null;
            while (s_Snares.Count > 0 && (v == null || v.root == null)) v = s_Snares.Pop();
            if (v == null || v.root == null)
            {
                v = new ProjVisual { style = (ProjectileStyle)(-2) };
                v.root = new GameObject("Snare").transform;
                v.root.SetParent(Parent, false);
                var metal = new Color(0.4f, 0.42f, 0.48f);
                Art.Part(PrimitiveType.Cylinder, v.root, new Vector3(0f, 0.025f, 0f), new Vector3(0.75f, 0.025f, 0.75f), Art.Darken(metal, 0.7f), true, default, ShadowCastingMode.On);
                var plate = Art.Part(PrimitiveType.Cylinder, v.root, new Vector3(0f, 0.05f, 0f), new Vector3(0.28f, 0.02f, 0.28f), Color.white, false, default, ShadowCastingMode.Off);
                v.tint.Add(plate.GetComponent<Renderer>());
                // Duas mandíbulas (metades do aro) com dentes, que fecham ao disparar
                for (int side = -1; side <= 1; side += 2)
                {
                    var jaw = new GameObject(side < 0 ? "JawL" : "JawR").transform;
                    jaw.SetParent(v.root, false);
                    jaw.localPosition = new Vector3(0f, 0.04f, 0f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = (i - 2) * 28f * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Sin(a) * 0.33f, 0.06f, side * Mathf.Cos(a) * 0.33f);
                        Art.Part(PrimitiveType.Cube, jaw, p, new Vector3(0.05f, 0.12f, 0.04f), metal, true, new Vector3(side * -20f, (i - 2) * 28f, 0f), ShadowCastingMode.Off);
                    }
                    if (side < 0) v.spin = jaw;
                    else v.fuseTip = jaw;
                }
                v.root.GetComponentsInChildren(true, v.all);
            }
            v.root.gameObject.SetActive(true);
            v.root.position = pos;
            v.root.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            v.baseScale = discreet ? 0.8f : 1f;
            v.root.localScale = Vector3.one * v.baseScale;
            v.color = color;
            if (v.spin != null) v.spin.localRotation = Quaternion.identity;
            if (v.fuseTip != null) v.fuseTip.localRotation = Quaternion.identity;
            foreach (var r in v.tint) Art.SetColor(r, discreet ? Art.Darken(color, 0.4f) : color);
            foreach (var r in v.all) Art.SetFloat(r, Art.FadeId, discreet ? 0.45f : 1f);
            return v;
        }

        /// <summary>Armada: os dentes abrem e a placa pulsa (para o dono e colegas).</summary>
        public static void PoseSnare(ProjVisual v, bool armed, bool ownerView, float age)
        {
            if (v == null || v.root == null) return;
            float open = armed ? 1f : Mathf.Clamp01(age / 0.4f);
            if (v.spin != null) v.spin.localRotation = Quaternion.Euler(-25f * open, 0f, 0f);
            if (v.fuseTip != null) v.fuseTip.localRotation = Quaternion.Euler(25f * open, 0f, 0f);
            if (armed && ownerView)
            {
                float pulse = 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(age * 4f));
                foreach (var r in v.tint) Art.SetEmission(r, v.color * pulse);
            }
        }

        public static void ReleaseSnare(ProjVisual v)
        {
            if (v == null || v.root == null) return;
            foreach (var r in v.all)
            {
                Art.SetEmission(r, Color.black);
                Art.SetFloat(r, Art.FadeId, 1f);
            }
            v.root.gameObject.SetActive(false);
            s_Snares.Push(v);
        }
    }
}
