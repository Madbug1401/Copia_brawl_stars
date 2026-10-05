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
        static Transform s_Parent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Pools.Clear();
            s_Mines.Clear();
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
                FPSLAN.Fx.Glow(v.fuseTip.position, Random.insideUnitSphere * 0.6f, new Color(1f, 0.75f, 0.3f), 0.12f, 0.22f);
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
                    // a ponta usa a cor do ataque
                    var head = a.GetChild(1).GetComponent<Renderer>();
                    v.tint.Add(head);
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
    }
}
