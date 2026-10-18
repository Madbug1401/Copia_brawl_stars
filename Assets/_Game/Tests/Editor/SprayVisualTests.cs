using NUnit.Framework;
using UnityEngine;

namespace FPSLAN.Tests
{
    public class SprayVisualTests
    {
        [Test]
        public void MsDamageAndDeliveryRemainUnchanged()
        {
            var attack = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackDefinition>("Assets/_Game/Data/Brawlers/sprayer/sprayer_Primary.asset");
            var delivery = attack.delivery as ConeVolumeDelivery;
            Assert.NotNull(delivery);
            Assert.AreEqual(7.5f, delivery.range);
            Assert.AreEqual(36, delivery.angle);
            Assert.AreEqual(.6f, delivery.duration);
            Assert.AreEqual(14, attack.effect.damage);
            Assert.AreEqual(4, delivery.ScheduledTicks);
        }

        [Test]
        public void GeometryUsesRadialRangeAndTheActualConeAngle()
        {
            var d = ScriptableObject.CreateInstance<ConeVolumeDelivery>();
            try
            {
                d.range = 7.5f; d.expandTime = .12f;
                Assert.AreEqual(0, SprayConeVisual.Reach(d, 0));
                Assert.AreEqual(3.75f, SprayConeVisual.Reach(d, .06f), .001f);
                Assert.AreEqual(7.5f, SprayConeVisual.Reach(d, 1));
                foreach (var forward in new[] { Vector3.forward, Vector3.up, new Vector3(1, -.3f, 2).normalized })
                    for (int i = 0; i < 32; i++)
                    {
                        var dir = SprayConeVisual.ConeDirection(forward, 18, i / 31f, i * .8f);
                        Assert.AreEqual(1, dir.magnitude, .0001f);
                        Assert.LessOrEqual(Vector3.Angle(forward, dir), 18.001f);
                    }
            }
            finally { Object.DestroyImmediate(d); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PaintStaysInRangeAndStopsAtWalls(bool wall)
        {
            var root = new GameObject("Spray visual test");
            var blocker = new GameObject("Spray blocking wall");
            var d = ScriptableObject.CreateInstance<ConeVolumeDelivery>();
            var attack = ScriptableObject.CreateInstance<AttackDefinition>();
            var origin = new Vector3(1000, 1000, 1000);
            try
            {
                d.range = 7.5f; d.angle = 36; d.expandTime = .12f; d.duration = .6f; d.dualStream = true;
                attack.delivery = d;
                if (wall)
                {
                    blocker.transform.position = origin + Vector3.forward * 3;
                    blocker.AddComponent<BoxCollider>().size = new Vector3(20, 20, .2f);
                }
                Physics.SyncTransforms();
                var visual = new SprayConeVisual(root.transform);
                visual.Begin(new AttackContext { attack = attack, origin = origin, direction = Vector3.forward, seed = 43 },
                    d, origin + Vector3.right * .2f, origin - Vector3.right * .2f);
                visual.Pose(.25f);
                int count = 0;
                foreach (var ps in visual.Root.GetComponentsInChildren<ParticleSystem>())
                {
                    var particles = new ParticleSystem.Particle[160];
                    int n = ps.GetParticles(particles); count += n;
                    for (int i = 0; i < n; i++)
                    {
                        var delta = particles[i].position - origin;
                        Assert.LessOrEqual(delta.magnitude + particles[i].startSize * .5f, 7.501f);
                        if (delta.magnitude > 1.5f) Assert.LessOrEqual(Vector3.Angle(Vector3.forward, delta), 18.01f);
                        if (wall) Assert.LessOrEqual(delta.z, 2.901f);
                    }
                }
                Assert.Greater(count, 80);
                Assert.AreEqual(0, root.GetComponentsInChildren<Collider>().Length);
                visual.Release();
                Assert.IsFalse(visual.Root.gameObject.activeSelf);
                visual.Begin(new AttackContext { attack = attack, origin = origin, direction = Vector3.forward }, d, origin, origin);
                Assert.IsTrue(visual.Root.gameObject.activeSelf);
                visual.Release();
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(blocker); Object.DestroyImmediate(d); Object.DestroyImmediate(attack); }
        }
    }
}
