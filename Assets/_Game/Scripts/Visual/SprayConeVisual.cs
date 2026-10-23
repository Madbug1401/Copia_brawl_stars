using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Pooled paint aerosol. Positions are reconstructed inside the damage cone instead of launching
    /// smoke that can drift beyond its range. Only the visual attack creates this object.
    /// </summary>
    public sealed class SprayConeVisual
    {
        const int Lanes = 32, RimSamples = 40;
        readonly Transform m_Root;
        readonly ParticleSystem m_Mist, m_Drops;
        readonly ParticleSystem.Particle[] m_Clouds = new ParticleSystem.Particle[Lanes * 2 + 20];
        readonly ParticleSystem.Particle[] m_Specks = new ParticleSystem.Particle[Lanes * 3 + RimSamples];
        readonly Vector3[] m_Directions = new Vector3[Lanes * 3];
        readonly float[] m_Distances = new float[Lanes * 3];
        readonly Vector3[] m_RimDirections = new Vector3[RimSamples + 1];
        readonly float[] m_RimDistances = new float[RimSamples + 1];
        readonly LineRenderer m_Edge;
        static Material s_DropMaterial, s_EdgeMaterial;
        AttackContext m_Context;
        ConeVolumeDelivery m_Delivery;
        Vector3 m_Nozzle0, m_Nozzle1;
        bool m_Local;
        float m_NextTrace, m_Phase;

        public Transform Root => m_Root;

        public SprayConeVisual(Transform parent)
        {
            m_Root = new GameObject("PaintSpray").transform;
            m_Root.SetParent(parent, false);
            m_Mist = MakeParticles("Aerosol", Art.FxAlpha, m_Clouds.Length);
            if (s_DropMaterial == null)
            {
                s_DropMaterial = new Material(Art.FxAlpha) { name = "Spray paint droplets", mainTexture = Art.CircleTexture };
                s_EdgeMaterial = new Material(Art.FxAlpha) { name = "Spray range edge", mainTexture = Texture2D.whiteTexture };
            }
            m_Drops = MakeParticles("Paint droplets", s_DropMaterial, m_Specks.Length);
            var dropMain = m_Drops.main;
            dropMain.startSize3D = true;
            var edge = new GameObject("Soft range rim");
            edge.transform.SetParent(m_Root, false);
            m_Edge = edge.AddComponent<LineRenderer>();
            m_Edge.sharedMaterial = s_EdgeMaterial;
            m_Edge.useWorldSpace = true;
            m_Edge.positionCount = RimSamples + 1;
            m_Edge.shadowCastingMode = ShadowCastingMode.Off;
            m_Edge.receiveShadows = false;
            m_Edge.numCornerVertices = 2;
            m_Root.gameObject.SetActive(false);
        }

        ParticleSystem MakeParticles(string name, Material material, int capacity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            main.startSpeed = 0;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>Radial distance, matching ConeTickAll's origin/direction and spherical range test.</summary>
        public static float Reach(ConeVolumeDelivery delivery, float age) =>
            Mathf.Max(0, delivery.range) * Mathf.Clamp01(age / Mathf.Max(0.01f, delivery.expandTime));

        public static Vector3 ConeDirection(Vector3 forward, float halfAngle, float radius01, float azimuth)
        {
            var rotation = Quaternion.LookRotation(forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward);
            float a = Mathf.Clamp(halfAngle, 0, 85) * Mathf.Deg2Rad * Mathf.Clamp01(radius01);
            return rotation * new Vector3(Mathf.Cos(azimuth) * Mathf.Sin(a), Mathf.Sin(azimuth) * Mathf.Sin(a), Mathf.Cos(a));
        }

        public void Begin(AttackContext context, ConeVolumeDelivery delivery, Vector3 nozzle0, Vector3 nozzle1)
        {
            m_Context = context; m_Delivery = delivery;
            m_Nozzle0 = nozzle0; m_Nozzle1 = nozzle1;
            m_Local = context.shooter != null && context.shooter.IsLocalHuman;
            m_NextTrace = 0;
            m_Phase = (context.seed & 1023) / 1024f;
            var rng = context.RngFor(1973);
            for (int i = 0; i < m_Directions.Length; i++)
                m_Directions[i] = ConeDirection(context.direction, delivery.angle * .5f,
                    Mathf.Sqrt((float)rng.NextDouble()), (float)rng.NextDouble() * Mathf.PI * 2f);
            for (int i = 0; i <= RimSamples; i++)
                m_RimDirections[i] = ConeDirection(context.direction, delivery.angle * .5f, 1, i * Mathf.PI * 2 / RimSamples);
            m_Root.gameObject.SetActive(true);
            m_Mist.Play(); m_Mist.Pause(); m_Drops.Play(); m_Drops.Pause();
            Pose(0);
        }

        public void Release()
        {
            m_Mist.Clear(); m_Drops.Clear();
            m_Root.gameObject.SetActive(false);
            m_Context = null; m_Delivery = null;
        }

        public void Pose(float age)
        {
            float reach = Reach(m_Delivery, age);
            float fade = Mathf.Clamp01(age / .035f) * Mathf.Clamp01((m_Delivery.duration - age) / .12f);
            if (age >= m_NextTrace)
            {
                m_NextTrace = age + .1f;
                for (int i = 0; i < m_Directions.Length; i++) m_Distances[i] = Trace(m_Directions[i]);
                for (int i = 0; i <= RimSamples; i++) m_RimDistances[i] = Trace(m_RimDirections[i]);
            }

            int clouds = 0, drops = 0;
            float travel = age / Mathf.Max(.08f, m_Delivery.expandTime) * .7f;
            var paint = m_Context.VisualColor;
            for (int i = 0; i < Lanes; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int lane = i*3+j;
                    float limit = Mathf.Min(reach, m_Distances[lane]);
                    float t = Mathf.Repeat(travel + i * .618034f + j * .5f + m_Phase, 1f);
                    float size = Mathf.Lerp(.14f, .75f, t);
                    float dist = Mathf.Lerp(.75f, Mathf.Max(.75f, limit - size * .6f), t);
                    if (dist + size * .6f > limit) continue;
                    var col = Color.Lerp(paint, new Color(.72f, .9f, 1f), (i % 5) * .055f);
                    col.a = fade * (m_Local ? .40f : .42f) * Mathf.Sin(t * Mathf.PI);
                    Put(m_Clouds, ref clouds, m_Context.origin + m_Directions[lane] * dist, size, col, i * 2.4f);
                }
                for (int j = 0; j < 3; j++)
                {
                    int lane = i*3+j;
                    float limit = Mathf.Min(reach, m_Distances[lane]);
                    float t = Mathf.Repeat(travel * 1.13f + i * .381966f + j / 3f + m_Phase, 1f);
                    float size = Mathf.Lerp(.028f, .075f, t);
                    float dist = Mathf.Lerp(.65f, Mathf.Max(.65f, limit - size), t);
                    if (dist + size > limit) continue;
                    var col = Color.Lerp(paint, i % 7 == 0 ? new Color(.2f, .92f, .8f) : new Color(1f, .65f, .95f), .35f);
                    col.a = fade * .7f * Mathf.Sin(t * Mathf.PI);
                    Put(m_Specks, ref drops, m_Context.origin + m_Directions[lane] * dist, size, col, i * 137.5f, m_Directions[lane] * 2f);
                }
            }

            // The pale, thin far rim and paint beads make the 36-degree envelope readable without filling the crosshair.
            for (int i = 0; i <= RimSamples; i++)
            {
                float limit = Mathf.Min(reach, m_RimDistances[i]);
                var point = m_Context.origin + m_RimDirections[i] * Mathf.Max(0, limit - .045f);
                m_Edge.SetPosition(i, point);
                if (i == RimSamples || limit < .8f) continue;
                var col = Color.Lerp(paint, Color.white, .3f);
                col.a = fade * .42f * (.7f + .3f * Mathf.Sin(age * 21 + i));
                Put(m_Specks, ref drops, point, .045f, col, 0);
            }
            var rimColor = Color.Lerp(paint, Color.white, .25f);
            rimColor.a = fade * (m_Local ? .38f : .42f);
            m_Edge.startColor = m_Edge.endColor = rimColor;
            m_Edge.startWidth = m_Edge.endWidth = m_Local ? .026f : .03f;
            m_Edge.enabled = reach > .8f && fade > 0;

            // A short, denser jet visibly connects each can to the common damage volume.
            int jets = m_Delivery.dualStream ? 2 : 1;
            for (int hand = 0; hand < jets; hand++)
            {
                var start = hand == 0 ? m_Nozzle0 : m_Nozzle1;
                var end = m_Context.origin + m_Context.direction * Mathf.Min(1.35f, reach);
                float distance = Vector3.Distance(start, end);
                var direction = distance > .001f ? (end - start) / distance : m_Context.direction;
                if (CombatMath.Raycast(start, direction, distance, m_Context.shooter, out var hit)) end = hit.point - direction * .05f;
                for (int i = 0; i < 10; i++)
                {
                    float t = Mathf.Repeat(travel * 1.6f + i * .1f, 1f);
                    var col = Color.Lerp(paint, Color.white, .35f);
                    col.a = fade * .32f * (1 - t);
                    Put(m_Clouds, ref clouds, Vector3.Lerp(start, end, t), .045f + t * .12f, col, i);
                }
            }
            m_Mist.SetParticles(m_Clouds, clouds);
            m_Drops.SetParticles(m_Specks, drops);
        }

        float Trace(Vector3 direction) => CombatMath.Raycast(m_Context.origin, direction, m_Delivery.range,
            m_Context.shooter, out var hit) ? Mathf.Max(0, hit.distance - .025f) : m_Delivery.range;

        static void Put(ParticleSystem.Particle[] particles, ref int count, Vector3 position, float size, Color color, float rotation, Vector3 velocity = default)
        {
            var particle = new ParticleSystem.Particle {
                position = position, startSize = size, startColor = color, rotation = rotation,
                startLifetime = 1, remainingLifetime = 1, velocity = velocity
            };
            if (velocity.sqrMagnitude > .001f) particle.startSize3D = new Vector3(size * .6f, size * 1.6f, size);
            particles[count++] = particle;
        }
    }
}
