using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Efeitos visuais do jogo (só visuais, nunca afetam o resultado): explosões, faíscas, fumo, estrelas de impacto,
    /// clarões de luz, tracers, anéis no chão e marcas de queimado.
    ///
    /// Usa um pequeno conjunto de ParticleSystems partilhados em espaço do mundo e chama Emit() com parâmetros por
    /// partícula — barato e sem instanciar objetos por efeito (máquinas modestas na LAN).
    /// </summary>
    public class Fx : MonoBehaviour
    {
        static Fx s_Instance;

        ParticleSystem m_Fire, m_Smoke, m_Spark, m_Debris, m_Ring, m_Flash, m_Star, m_Puff, m_Decal, m_Glow;

        class FlashLight
        {
            public Light light;
            public float age, life, intensity;
        }

        class Tracer
        {
            public LineRenderer line;
            public float age, life;
            public Color color;
        }

        readonly List<FlashLight> m_Lights = new List<FlashLight>();
        readonly List<Tracer> m_Tracers = new List<Tracer>();
        readonly Stack<LineRenderer> m_LinePool = new Stack<LineRenderer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Instance = null;

        static Fx I
        {
            get
            {
                if (s_Instance != null) return s_Instance;
                var go = new GameObject("[Fx]");
                s_Instance = go.AddComponent<Fx>();
                s_Instance.Build();
                return s_Instance;
            }
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        // ================================================================== API

        /// <summary>Explosão completa: clarão, bola de fogo, fumo, faíscas, destroços, anel no chão, marca e luz.</summary>
        public static void Explosion(Vector3 pos, float radius, Color color, float intensity = 1f)
        {
            var fx = I;
            radius = Mathf.Max(0.5f, radius);
            float k = Mathf.Clamp(radius / 2.2f, 0.5f, 2.5f) * intensity;

            var hot = Color.Lerp(color, new Color(1f, 0.95f, 0.6f), 0.55f);
            fx.EmitOne(fx.m_Flash, pos, Vector3.zero, radius * 2.6f, new Color(1f, 0.95f, 0.75f, 1f), 0.16f);

            int fire = Mathf.RoundToInt(10 + 8 * k);
            for (int i = 0; i < fire; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.15f;
                var c = Color.Lerp(hot, color, Random.value * 0.7f);
                fx.EmitOne(fx.m_Fire, pos + dir * radius * 0.25f, dir * Random.Range(1.5f, 4.5f) * k,
                    Random.Range(0.6f, 1.2f) * radius, c, Random.Range(0.28f, 0.5f));
            }

            int smoke = Mathf.RoundToInt(6 + 6 * k);
            for (int i = 0; i < smoke; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y);
                var g = Random.Range(0.35f, 0.6f);
                fx.EmitOne(fx.m_Smoke, pos + dir * radius * 0.4f + Vector3.up * 0.2f, dir * Random.Range(0.6f, 1.8f) * k + Vector3.up * 0.8f,
                    Random.Range(0.8f, 1.4f) * radius, new Color(g, g * 0.95f, g * 0.92f, 0.85f), Random.Range(0.9f, 1.6f));
            }

            Sparks(pos, color, Mathf.RoundToInt(12 * k), 9f * Mathf.Sqrt(k));
            Debris(pos, new Color(0.35f, 0.28f, 0.22f), Mathf.RoundToInt(6 * k), 6f * Mathf.Sqrt(k));

            var ground = GroundBelow(pos, 2.5f, out bool onGround);
            if (onGround)
            {
                fx.EmitOne(fx.m_Ring, ground + Vector3.up * 0.06f, Vector3.zero, radius * 2.4f, Color.Lerp(color, Color.white, 0.4f), 0.35f);
                fx.EmitOne(fx.m_Decal, ground + Vector3.up * 0.03f, Vector3.zero, radius * 1.5f, new Color(0.05f, 0.04f, 0.04f, 0.55f), 7f, Random.Range(0f, 360f));
            }

            Light(pos + Vector3.up * 0.5f, Color.Lerp(color, Color.white, 0.3f), 4f * Mathf.Min(1.6f, k), radius * 3.5f, 0.25f);
            CameraShake.AddAt(pos, 0.55f * Mathf.Min(1.8f, k), radius * 4f);
        }

        /// <summary>Faíscas esticadas que caem com gravidade.</summary>
        public static void Sparks(Vector3 pos, Color color, int count, float speed)
        {
            var fx = I;
            var c = Color.Lerp(color, Color.white, 0.5f);
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.2f;
                fx.EmitOne(fx.m_Spark, pos, dir.normalized * speed * Random.Range(0.4f, 1.1f), Random.Range(0.08f, 0.16f), c, Random.Range(0.25f, 0.55f));
            }
        }

        /// <summary>Pedaços (cubos) que saltam e caem.</summary>
        public static void Debris(Vector3 pos, Color color, int count, float speed)
        {
            var fx = I;
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.5f;
                var c = Color.Lerp(color, Color.black, Random.value * 0.35f);
                c.a = 1f;
                fx.EmitOne(fx.m_Debris, pos, dir.normalized * speed * Random.Range(0.4f, 1f), Random.Range(0.1f, 0.24f), c, Random.Range(0.7f, 1.2f), Random.Range(0f, 360f));
            }
        }

        /// <summary>Impacto pequeno (bala na parede): estrela, faíscas e pó.</summary>
        public static void Impact(Vector3 pos, Color color, float size = 1f)
        {
            var fx = I;
            fx.EmitOne(fx.m_Star, pos, Vector3.zero, 0.45f * size, Color.Lerp(color, Color.white, 0.3f), 0.14f, Random.Range(0f, 360f));
            Sparks(pos, color, Mathf.RoundToInt(5 * size), 5f);
            fx.EmitOne(fx.m_Smoke, pos, Random.onUnitSphere * 0.3f + Vector3.up * 0.4f, 0.35f * size, new Color(0.85f, 0.82f, 0.75f, 0.5f), 0.45f);
        }

        /// <summary>Acerto num jogador: estrela grande colorida (estilo cartoon) + faíscas.</summary>
        public static void HitStar(Vector3 pos, Color color, float size = 1f)
        {
            var fx = I;
            fx.EmitOne(fx.m_Star, pos, Vector3.zero, 0.75f * size, Color.white, 0.12f, Random.Range(0f, 360f));
            fx.EmitOne(fx.m_Star, pos, Vector3.zero, 1.05f * size, Color.Lerp(color, Color.white, 0.2f), 0.18f, Random.Range(0f, 360f));
            Sparks(pos, color, Mathf.RoundToInt(6 * size), 6f);
        }

        /// <summary>Clarão do cano.</summary>
        public static void Muzzle(Vector3 pos, Vector3 dir, Color color, float size, bool local)
        {
            var fx = I;
            var c = Color.Lerp(color, new Color(1f, 0.95f, 0.8f), 0.5f);
            fx.EmitOne(fx.m_Star, pos, Vector3.zero, (local ? 0.16f : 0.5f) * size, c, 0.06f, Random.Range(0f, 360f));
            fx.EmitOne(fx.m_Flash, pos + dir * (local ? 0.02f : 0.08f), Vector3.zero, (local ? 0.22f : 0.7f) * size, c, 0.07f);
            if (!local)
                for (int i = 0; i < 2; i++)
                    fx.EmitOne(fx.m_Smoke, pos + dir * 0.15f, dir * 1.2f + Random.insideUnitSphere * 0.4f, 0.25f * size, new Color(0.9f, 0.9f, 0.9f, 0.35f), 0.4f);
            Light(pos, color, local ? 1.6f : 2.2f, local ? 4f : 6f, 0.06f);
        }

        /// <summary>Fumo/nuvem colorida (spray, venenos, poeira).</summary>
        public static void Puff(Vector3 pos, Vector3 velocity, Color color, float size, float life)
        {
            var fx = I;
            fx.EmitOne(fx.m_Puff, pos, velocity, size, color, life, Random.Range(0f, 360f));
        }

        /// <summary>Fumo cinzento/branco simples.</summary>
        public static void Smoke(Vector3 pos, Vector3 velocity, float size, float life, float alpha = 0.6f)
        {
            var fx = I;
            fx.EmitOne(fx.m_Smoke, pos, velocity, size, new Color(0.9f, 0.9f, 0.9f, alpha), life, Random.Range(0f, 360f));
        }

        /// <summary>Brilho aditivo (faísca de rastilho, partículas mágicas).</summary>
        public static void Glow(Vector3 pos, Vector3 velocity, Color color, float size, float life)
        {
            var fx = I;
            fx.EmitOne(fx.m_Glow, pos, velocity, size, color, life);
        }

        /// <summary>Anel que se expande no chão.</summary>
        public static void Ring(Vector3 pos, Color color, float size, float life = 0.4f)
        {
            var fx = I;
            fx.EmitOne(fx.m_Ring, pos + Vector3.up * 0.05f, Vector3.zero, size, color, life);
        }

        /// <summary>"Puf" de fumo de desaparecer (morte) com estrelas.</summary>
        public static void Poof(Vector3 pos, Color color)
        {
            var fx = I;
            for (int i = 0; i < 14; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.6f;
                fx.EmitOne(fx.m_Smoke, pos + dir * 0.3f, dir * Random.Range(1f, 2.8f) + Vector3.up * 0.6f, Random.Range(0.7f, 1.2f),
                    new Color(0.95f, 0.95f, 0.98f, 0.9f), Random.Range(0.6f, 1.1f));
            }
            for (int i = 0; i < 5; i++)
                fx.EmitOne(fx.m_Star, pos + Random.insideUnitSphere * 0.6f, Random.insideUnitSphere * 2f + Vector3.up * 2f, Random.Range(0.25f, 0.4f),
                    Color.Lerp(color, Color.white, 0.5f), Random.Range(0.4f, 0.7f), Random.Range(0f, 360f));
            fx.EmitOne(fx.m_Ring, GroundBelow(pos, 3f, out _) + Vector3.up * 0.05f, Vector3.zero, 3.2f, Color.white, 0.4f);
        }

        /// <summary>Coluna de luz ao reaparecer.</summary>
        public static void SpawnBeam(Vector3 feet, Color color)
        {
            var fx = I;
            for (int i = 0; i < 12; i++)
            {
                var off = Random.insideUnitCircle * 0.6f;
                fx.EmitOne(fx.m_Glow, feet + new Vector3(off.x, Random.Range(0f, 0.6f), off.y), Vector3.up * Random.Range(2f, 5f),
                    Random.Range(0.12f, 0.25f), Color.Lerp(color, Color.white, 0.4f), Random.Range(0.4f, 0.8f));
            }
            fx.EmitOne(fx.m_Ring, feet + Vector3.up * 0.05f, Vector3.zero, 3f, Color.Lerp(color, Color.white, 0.5f), 0.5f);
            fx.EmitOne(fx.m_Flash, feet + Vector3.up * 1f, Vector3.zero, 2.5f, Color.Lerp(color, Color.white, 0.6f), 0.2f);
        }

        /// <summary>Linha de tiro instantâneo.</summary>
        public static void TracerLine(Vector3 from, Vector3 to, Color color, float width, float life = 0.09f)
        {
            var fx = I;
            if (fx.m_Tracers.Count > 40) return;
            var line = fx.m_LinePool.Count > 0 ? fx.m_LinePool.Pop() : fx.NewLine();
            line.gameObject.SetActive(true);
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = width * 0.6f;
            line.endWidth = width;
            fx.m_Tracers.Add(new Tracer { line = line, age = 0f, life = life, color = color });
            fx.ApplyTracer(fx.m_Tracers[fx.m_Tracers.Count - 1], 0f);
        }

        public static void MeleeSlash(Vector3 origin, Vector3 direction, Color color)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            direction.Normalize();
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            var center = origin + direction * 1.15f + Vector3.up * 0.05f;
            const int segments = 5;
            Vector3 previous = center - right * 0.42f + Vector3.up * 0.18f;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var next = center + Vector3.Lerp(-right * 0.42f + Vector3.up * 0.18f, right * 0.42f - Vector3.up * 0.18f, t);
                TracerLine(previous, next, i % 2 == 0 ? Color.white : color, 0.045f, 0.11f);
                previous = next;
            }
        }

        /// <summary>Luz pontual breve (clarão).</summary>
        public static void Light(Vector3 pos, Color color, float intensity, float range, float life)
        {
            var fx = I;
            FlashLight best = null;
            foreach (var l in fx.m_Lights)
            {
                if (!l.light.enabled) { best = l; break; }
                if (best == null || l.age / l.life > best.age / best.life) best = l;
            }
            if (best == null) return;
            best.light.transform.position = pos;
            best.light.color = color;
            best.light.range = range;
            best.light.intensity = intensity;
            best.light.enabled = true;
            best.intensity = intensity;
            best.age = 0f;
            best.life = Mathf.Max(0.02f, life);
        }

        // ================================================================== construção

        void Build()
        {
            m_Glow = MakeSystem("Glow", Art.FxAdditive, Art.SoftTexture, ParticleSystemRenderMode.Billboard, 400, 0f,
                Grad(1f, 1f, 0f), Curve(1f, 0.2f));
            m_Fire = MakeSystem("Fire", Art.FxAdditive, Art.SoftTexture, ParticleSystemRenderMode.Billboard, 500, -0.15f,
                GradColor(new Color(1f, 1f, 1f), new Color(1f, 0.55f, 0.3f), 1f, 0.9f, 0f), Curve(0.5f, 1.25f), drag: 3f);
            m_Smoke = MakeSystem("Smoke", Art.FxAlpha, Art.SmokeTexture, ParticleSystemRenderMode.Billboard, 500, -0.04f,
                GradAlpha(0f, 1f, 0f, 0.12f), Curve(0.5f, 1.6f), drag: 2f, rotate: true);
            m_Spark = MakeSystem("Spark", Art.FxAdditive, Art.SparkTexture, ParticleSystemRenderMode.Stretch, 400, 1.3f,
                Grad(1f, 1f, 0f), Curve(1f, 0.3f));
            m_Debris = MakeSystem("Debris", Art.FxAlpha, Texture2D.whiteTexture, ParticleSystemRenderMode.Mesh, 200, 2.2f,
                GradAlpha(1f, 1f, 0f, 0.85f), Curve(1f, 0.6f), rotate: true, mesh: Art.PrimitiveMesh(PrimitiveType.Cube), collide: true);
            m_Ring = MakeSystem("Ring", Art.FxAdditive, Art.RingTexture, ParticleSystemRenderMode.HorizontalBillboard, 80, 0f,
                Grad(1f, 0.8f, 0f), Curve(0.15f, 1f));
            m_Flash = MakeSystem("Flash", Art.FxAdditive, Art.SoftTexture, ParticleSystemRenderMode.Billboard, 120, 0f,
                Grad(1f, 0.6f, 0f), Curve(0.6f, 1f));
            m_Star = MakeSystem("Star", Art.FxAdditive, Art.StarTexture, ParticleSystemRenderMode.Billboard, 200, 0f,
                Grad(1f, 1f, 0f), Curve(0.7f, 1.1f), rotate: true);
            m_Puff = MakeSystem("Puff", Art.FxAlpha, Art.SmokeTexture, ParticleSystemRenderMode.Billboard, 700, -0.02f,
                GradAlpha(0f, 1f, 0f, 0.15f), Curve(0.35f, 1.5f), drag: 2.5f, rotate: true);
            m_Decal = MakeSystem("Decal", Art.FxAlpha, Art.SoftTexture, ParticleSystemRenderMode.HorizontalBillboard, 60, 0f,
                GradAlpha(1f, 1f, 0f, 0.05f), Curve(1f, 1f), rotate: false);

            for (int i = 0; i < 6; i++)
            {
                var lgo = new GameObject("FxLight" + i);
                lgo.transform.SetParent(transform, false);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.Auto; // respeita o limite de luzes por pixel da qualidade (PCs modestos)
                l.enabled = false;
                m_Lights.Add(new FlashLight { light = l });
            }
        }

        ParticleSystem MakeSystem(string name, Material baseMat, Texture tex, ParticleSystemRenderMode mode, int max, float gravity,
            Gradient gradient, AnimationCurve size, float drag = 0f, bool rotate = false, Mesh mesh = null, bool collide = false)
        {
            var go = new GameObject("Fx_" + name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.gravityModifier = gravity;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, size);

            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.drag = drag;
                limit.multiplyDragByParticleSize = false;
                limit.multiplyDragByParticleVelocity = false;
            }

            if (rotate)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            }

            if (collide)
            {
                var c = ps.collision;
                c.enabled = true;
                c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D;
                c.dampen = 0.4f;
                c.bounce = 0.35f;
                c.lifetimeLoss = 0f;
                c.quality = ParticleSystemCollisionQuality.Low;
                c.collidesWith = Physics.DefaultRaycastLayers;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(baseMat) { name = "Fx_" + name, mainTexture = tex };
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 4f;
            if (mode == ParticleSystemRenderMode.Stretch)
            {
                r.velocityScale = 0.04f;
                r.lengthScale = 1.5f;
            }
            if (mode == ParticleSystemRenderMode.Mesh && mesh != null)
            {
                r.mesh = mesh;
                r.alignment = ParticleSystemRenderSpace.World;
            }
            if (mode == ParticleSystemRenderMode.HorizontalBillboard)
                r.alignment = ParticleSystemRenderSpace.World;

            ps.Play();
            return ps;
        }

        void EmitOne(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, Color color, float life, float rotationDeg = 0f)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startColor = color,
                startLifetime = life,
                rotation = rotationDeg,
                applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        LineRenderer NewLine()
        {
            var go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Art.FxBeam;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            return lr;
        }

        void ApplyTracer(Tracer t, float k)
        {
            float a = 1f - k;
            var c = t.color;
            t.line.startColor = new Color(c.r, c.g, c.b, a * 0.5f);
            t.line.endColor = new Color(Mathf.Min(1f, c.r + 0.4f), Mathf.Min(1f, c.g + 0.4f), Mathf.Min(1f, c.b + 0.4f), a);
        }

        // ================================================================== update

        void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < m_Lights.Count; i++)
            {
                var l = m_Lights[i];
                if (!l.light.enabled) continue;
                l.age += dt;
                float k = l.age / l.life;
                if (k >= 1f) l.light.enabled = false;
                else l.light.intensity = l.intensity * (1f - k) * (1f - k);
            }

            for (int i = m_Tracers.Count - 1; i >= 0; i--)
            {
                var t = m_Tracers[i];
                t.age += dt;
                float k = t.age / t.life;
                if (k >= 1f || t.line == null)
                {
                    if (t.line != null)
                    {
                        t.line.gameObject.SetActive(false);
                        m_LinePool.Push(t.line);
                    }
                    m_Tracers.RemoveAt(i);
                    continue;
                }
                ApplyTracer(t, k);
            }
        }

        // ================================================================== utilitários

        static Vector3 GroundBelow(Vector3 pos, float maxDist, out bool found)
        {
            if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out var hit, maxDist + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerHealth>() == null)
            {
                found = true;
                return hit.point;
            }
            found = false;
            return pos;
        }

        static AnimationCurve Curve(float start, float end) => AnimationCurve.EaseInOut(0f, start, 1f, end);

        static Gradient Grad(float a0, float aMid, float a1)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(aMid, 0.5f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        static Gradient GradAlpha(float a0, float a1, float a2, float peakAt)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, Mathf.Clamp(peakAt, 0.01f, 0.95f)), new GradientAlphaKey(a2, 1f) });
            return g;
        }

        static Gradient GradColor(Color c0, Color c1, float a0, float aMid, float a1)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 0.6f), new GradientColorKey(new Color(0.35f, 0.25f, 0.2f), 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(aMid, 0.4f), new GradientAlphaKey(a1, 1f) });
            return g;
        }
    }

    /// <summary>Abanão da câmara do jogador local (explosões perto, Super, levar dano).</summary>
    public static class CameraShake
    {
        static float s_Trauma;
        static float s_Seed = 17.3f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Trauma = 0f;

        public static void Add(float amount) => s_Trauma = Mathf.Clamp01(s_Trauma + amount);

        /// <summary>Abanão com queda pela distância à câmara local.</summary>
        public static void AddAt(Vector3 pos, float amount, float radius)
        {
            var me = PlayerNetwork.Local;
            if (me == null) return;
            float d = Vector3.Distance(me.transform.position, pos);
            if (d > radius) return;
            Add(amount * (1f - d / Mathf.Max(0.1f, radius)));
        }

        /// <summary>Rotação a aplicar este frame (graus). Decai sozinho.</summary>
        public static Vector3 Sample(float dt)
        {
            s_Trauma = Mathf.Max(0f, s_Trauma - dt * 1.6f);
            float s = s_Trauma * s_Trauma;
            if (s <= 0.0001f) return Vector3.zero;
            float t = Time.time * 28f;
            return new Vector3(
                (Mathf.PerlinNoise(s_Seed, t) - 0.5f) * 2f * 4f * s,
                (Mathf.PerlinNoise(s_Seed + 10f, t) - 0.5f) * 2f * 4f * s,
                (Mathf.PerlinNoise(s_Seed + 20f, t) - 0.5f) * 2f * 3f * s);
        }
    }
}
