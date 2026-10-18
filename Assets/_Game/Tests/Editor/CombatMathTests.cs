using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN.Tests
{
    /// <summary>Headshots, quedas de dano, veneno exato, colocação de armadilhas e serialização de rede.</summary>
    public class CombatMathTests
    {
        // ------------------------------------------------------------------ headshots

        static bool Shot(float height, float lateral = 0f)
        {
            var feet = Vector3.zero;
            var origin = new Vector3(lateral, height, -10f);
            var dir = Vector3.forward;
            // Ponto de impacto na frente da cápsula (raio 0,4)
            var hit = new Vector3(lateral, height, -0.4f);
            return CombatMath.IsHeadshot(feet, hit, origin, dir);
        }

        [Test]
        public void HeadshotOnlyInHeadZone()
        {
            Assert.IsTrue(Shot(1.5f), "centro da cabeça");
            Assert.IsTrue(Shot(1.7f), "topo da cabeça");
            Assert.IsTrue(Shot(1.25f), "parte de baixo da cabeça");
            Assert.IsFalse(Shot(1.1f), "ombros/peito");
            Assert.IsFalse(Shot(0.6f), "pernas");
            Assert.IsFalse(Shot(1.5f, 0.38f), "raspão de lado, fora da cabeça");
        }

        [Test]
        public void HeadshotFromAboveAndBelow()
        {
            var feet = new Vector3(0f, 0f, 10f);
            var head = feet + Vector3.up * CombatMath.HeadCenterHeight;
            var originHigh = new Vector3(0f, 6f, 0f);
            Assert.IsTrue(CombatMath.IsHeadshot(feet, head, originHigh, (head - originHigh).normalized));
            var chest = feet + Vector3.up * 0.9f;
            Assert.IsFalse(CombatMath.IsHeadshot(feet, chest, originHigh, (chest - originHigh).normalized));
        }

        [Test]
        public void HeadshotWithoutRayUsesHitHeight()
        {
            Assert.IsTrue(CombatMath.IsHeadshot(Vector3.zero, new Vector3(0f, 1.5f, 0f), Vector3.zero, Vector3.zero));
            Assert.IsFalse(CombatMath.IsHeadshot(Vector3.zero, new Vector3(0f, 1f, 0f), Vector3.zero, Vector3.zero));
        }

        // ------------------------------------------------------------------ dano

        [Test]
        public void DistanceFalloffStartsAfterConfiguredFraction()
        {
            var e = new EffectPayload { damage = 10, nearMultiplier = 1f, farMultiplier = 0.5f, falloffStartFraction = 0.25f };
            Assert.AreEqual(1f, e.DistanceMultiplier(2f, 10f), 1e-4f);
            Assert.AreEqual(1f, e.DistanceMultiplier(2.5f, 10f), 1e-4f);
            Assert.AreEqual(0.5f, e.DistanceMultiplier(10f, 10f), 1e-4f);
            Assert.Less(e.DistanceMultiplier(6f, 10f), 1f);
            Assert.Greater(e.DistanceMultiplier(6f, 10f), 0.5f);
        }

        [Test]
        public void SplashFalloffIsMonotonic()
        {
            var e = new EffectPayload { splashRadius = 2.4f, splashDamage = 40, splashFullRadiusFraction = 0.3f, splashEdgeMultiplier = 0.35f };
            int prev = int.MaxValue;
            for (float d = 0f; d <= 2.4f; d += 0.1f)
            {
                int v = e.SplashDamageAt(d);
                Assert.LessOrEqual(v, prev);
                prev = v;
            }
            Assert.AreEqual(40, e.SplashDamageAt(0.5f));
        }

        [Test]
        public void SplashDefaultsKeepOldFlatBehaviour()
        {
            var e = new EffectPayload { splashRadius = 3f, splashDamage = 50 };
            Assert.AreEqual(50, e.SplashDamageAt(0f));
            Assert.AreEqual(50, e.SplashDamageAt(2.9f));
        }

        [Test]
        public void PerShotDamageOverridesBaseDamage()
        {
            var e = new EffectPayload { damage = 10, perShotDamage = new[] { 33, 34, 33 } };
            Assert.AreEqual(33, e.DirectDamage(0, 0f, 10f, false));
            Assert.AreEqual(34, e.DirectDamage(1, 0f, 10f, false));
            Assert.AreEqual(10, e.DirectDamage(5, 0f, 10f, false), "índice fora da lista usa o dano base");
        }

        [Test]
        public void BodyHitDistanceForExplosions()
        {
            var feet = Vector3.zero;
            Assert.AreEqual(0f, EffectResolver.DistanceToBody(new Vector3(0.2f, 1f, 0f), feet), 1e-4f, "dentro da cápsula");
            Assert.AreEqual(1.6f, EffectResolver.DistanceToBody(new Vector3(2f, 1f, 0f), feet), 1e-4f);
            // Por baixo dos pés: mede até à ponta de baixo da cápsula (centro a 0,4 m, raio 0,4)
            Assert.AreEqual(1f, EffectResolver.DistanceToBody(new Vector3(0f, -1f, 0f), feet), 1e-4f);
        }

        // ------------------------------------------------------------------ veneno (DoT)

        [TestCase(0.016f)]
        [TestCase(0.033f)]
        [TestCase(0.1f)]
        [TestCase(0.37f)]
        public void PoisonDealsExactTotal(float dt)
        {
            float remaining = 8f * 5f, carry = 0f, t = 0f;
            int total = 0;
            while (true)
            {
                t += dt;
                bool expired = t >= 5f;
                total += DotMath.Step(ref remaining, ref carry, 8f, dt, expired);
                if (expired || DotMath.IsDone(remaining, carry)) break;
            }
            Assert.AreEqual(40, total);
        }

        [Test]
        public void ConeTicksScheduled()
        {
            var cone = ScriptableObject.CreateInstance<ConeVolumeDelivery>();
            cone.duration = 0.6f;
            cone.tickInterval = 0.15f;
            cone.expandTime = 0.12f;
            cone.maxTicksPerTarget = 0;
            Assert.AreEqual(4, cone.ScheduledTicks);
            cone.maxTicksPerTarget = 3;
            Assert.AreEqual(3, cone.ScheduledTicks);
            Object.DestroyImmediate(cone);
        }

        // ------------------------------------------------------------------ armadilhas

        [Test]
        public void TrapPlacementAvoidsWallsAndNeedsGround()
        {
            // Geometria de teste muito abaixo de qualquer cena (sem MapInfo: limites de ±22 m em X/Z)
            const float y0 = -500f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, y0 - 0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(5f, y0 + 1f, 0f);
            wall.transform.localScale = new Vector3(2f, 2f, 2f);
            Physics.SyncTransforms();
            try
            {
                var settings = new TrapSettings { placementClearance = 0.3f };
                var owner = new Vector3(-5f, y0, 0f);
                bool ok = CombatSystem.TryFindTrapPosition(new Vector3(5f, y0, 0f), owner, settings, out var pos);
                Assert.IsTrue(ok, "recua até um sítio livre");
                Assert.Less(pos.x, 3.9f, "fora da parede");
                Assert.AreEqual(y0, pos.y, 0.05f, "assente no chão");

                ok = CombatSystem.TryFindTrapPosition(new Vector3(-2f, y0, 0f), owner, settings, out pos);
                Assert.IsTrue(ok);
                Assert.AreEqual(-2f, pos.x, 0.01f, "sítio livre fica onde se apontou");

                ok = CombatSystem.TryFindTrapPosition(new Vector3(18f, y0, 18f), new Vector3(17f, y0, 17f), settings, out _);
                Assert.IsFalse(ok, "sem chão não coloca");
            }
            finally
            {
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(wall);
            }
        }

        // ------------------------------------------------------------------ rede

        [Test]
        public void MatchResultSerializesRoundTrip()
        {
            var data = new MatchResultData
            {
                matchId = new FixedString64Bytes("abc123"),
                brawlerId = new FixedString32Bytes("sniper"),
                petHash = StableId.Hash("pet_dog"),
                mode = 2,
                competitive = true,
                participated = true,
                won = true,
                kills = 7,
                killsOnBots = 2,
                assists = 3,
                deaths = 1,
                objectives = 2,
                headshots = 4,
                placement = 1,
                damageDealt = 987,
                secondsPlayed = 300,
                xpParticipation = 50,
                xpKills = 72,
                xpAssists = 18,
                xpVictory = 75,
                xpObjectives = 30,
                coins = 77,
                petXp = 99,
            };
            using (var writer = new FastBufferWriter(1024, Allocator.Temp))
            {
                writer.WriteNetworkSerializable(data);
                using (var reader = new FastBufferReader(writer, Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out MatchResultData back);
                    Assert.AreEqual("abc123", back.matchId.ToString());
                    Assert.AreEqual("sniper", back.brawlerId.ToString());
                    Assert.AreEqual(data.petHash, back.petHash);
                    Assert.AreEqual(data.kills, back.kills);
                    Assert.AreEqual(data.damageDealt, back.damageDealt);
                    Assert.AreEqual(data.XpTotal, back.XpTotal);
                    Assert.AreEqual(data.coins, back.coins);
                    Assert.IsTrue(back.won && back.competitive && back.participated);
                }
            }
        }

        [Test]
        public void StableIdsAreStableAndNonZero()
        {
            Assert.AreEqual(StableId.Hash("skin_ekko_neon"), StableId.Hash("skin_ekko_neon"));
            Assert.AreNotEqual(StableId.Hash("skin_ekko_neon"), StableId.Hash("skin_ekko_gold"));
            Assert.AreEqual(0, StableId.Hash(""));
            Assert.AreNotEqual(0, StableId.Hash("x"));
        }
    }
}
