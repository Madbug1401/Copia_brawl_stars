using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FPSLAN.Tests
{
    /// <summary>Nomes, vida, danos e especiais das seis personagens (lidos dos assets gerados).</summary>
    public class BrawlerDataTests
    {
        const string RegistryPath = "Assets/_Game/Resources/BrawlerRegistry.asset";

        static BrawlerRegistry Registry
        {
            get
            {
                var r = AssetDatabase.LoadAssetAtPath<BrawlerRegistry>(RegistryPath);
                Assert.IsNotNull(r, "BrawlerRegistry em falta");
                return r;
            }
        }

        static BrawlerDefinition Get(string id)
        {
            foreach (var b in Registry.brawlers)
                if (b != null && b.brawlerId == id) return b;
            Assert.Fail("Brawler em falta: " + id);
            return null;
        }

        static int ShotTotal(AttackDefinition a, float distance)
        {
            int total = 0;
            for (int i = 0; i < a.delivery.ShotCount; i++) total += a.effect.DirectDamage(i, distance, a.Range, false);
            return total;
        }

        // ------------------------------------------------------------------ nomes e ids

        [TestCase("gunslinger", "Ekko")]
        [TestCase("sprayer", "Mr.S")]
        [TestCase("shotgunner", "Riot")]
        [TestCase("artillery", "Lupus Marinus")]
        [TestCase("trapper", "N.Cage")]
        [TestCase("sniper", "Lince")]
        public void NewDisplayNames(string id, string expected)
        {
            Assert.AreEqual(expected, Get(id).displayName);
        }

        [Test]
        public void LupusMarinusShortNameIsLM()
        {
            Assert.AreEqual("L.M", Get("artillery").ShortName);
        }

        [Test]
        public void OldNamesAreGoneFromPlayerFacingText()
        {
            foreach (var b in Registry.brawlers)
                foreach (var old in new[] { "Dex", "Névoa", "Brasa", "Cardo", "Vigia" })
                {
                    StringAssert.DoesNotContain(old, b.displayName);
                    StringAssert.DoesNotContain(old, b.ShortName);
                    StringAssert.DoesNotContain(old, b.role);
                }
        }

        [Test]
        public void TechnicalIdsAndNetworkOrderArePreserved()
        {
            // A rede envia o índice na registry: a ordem tem de ser a mesma de antes
            var expected = new[] { "gunslinger", "artillery", "trapper", "sprayer", "shotgunner", "sniper" };
            Assert.AreEqual(expected.Length, Registry.brawlers.Count);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], Registry.brawlers[i].brawlerId);
        }

        // ------------------------------------------------------------------ vida

        [TestCase("gunslinger", 120)]
        [TestCase("sprayer", 160)]
        [TestCase("shotgunner", 135)]
        [TestCase("artillery", 100)]
        [TestCase("trapper", 110)]
        [TestCase("sniper", 85)]
        public void MaxHealth(string id, int hp)
        {
            Assert.AreEqual(hp, Get(id).stats.maxHealth);
        }

        // ------------------------------------------------------------------ Ekko

        [Test]
        public void EkkoDualSmgDamageDependsOnDistance()
        {
            var b = Get("gunslinger");
            var a = b.primary.attack;
            Assert.AreEqual(RigStyle.DualSMG, b.firstPerson.rig);
            Assert.AreEqual(RigStyle.DualSMG, b.thirdPerson.rig);
            Assert.IsInstanceOf<ProjectileDelivery>(a.delivery);
            var d = (ProjectileDelivery)a.delivery;
            Assert.Greater(d.spreadDegrees, 0f, "dispersão");
            Assert.Greater(d.count, 1, "rajada automática");
            Assert.AreEqual(12, a.effect.DirectDamage(0, 0f, a.Range, false));
            Assert.AreEqual(7, a.effect.DirectDamage(0, a.Range, a.Range, false));
            Assert.AreEqual(60, ShotTotal(a, 2f), "rajada de perto");
            Assert.Less(ShotTotal(a, a.Range), ShotTotal(a, 2f));
            Assert.Greater(b.animations.attackKick, 0f, "recuo");
            Assert.AreEqual(2, b.firstPerson.muzzleOffsets.Length, "duas armas alternadas");
        }

        [Test]
        public void EkkoMinigunHas200PotentialDamage()
        {
            var b = Get("gunslinger");
            Assert.IsTrue(b.super.IsMode);
            var a = b.super.attack;
            var mode = b.super.mode;
            Assert.Greater(mode.duration, 0f);
            Assert.Greater(b.super.chargeRequired, 0f, "exige carregamento");
            Assert.IsFalse(a.effect.canHeadshot);
            Assert.LessOrEqual(a.effect.nearMultiplier, 1f);
            int potential = mode.maxShots * a.delivery.ShotCount * a.effect.damage;
            Assert.AreEqual(200, potential);
            Assert.Greater(a.delivery.ShotCount, 1, "não aplica 200 de uma vez");
            Assert.Less(mode.shotCooldown, b.primary.cooldown, "maior cadência");
        }

        // ------------------------------------------------------------------ Mr.S

        [Test]
        public void MrSSprayTotalBetween50And60()
        {
            var b = Get("sprayer");
            Assert.AreEqual(RigStyle.DualSprayCans, b.firstPerson.rig);
            var cone = b.primary.attack.delivery as ConeVolumeDelivery;
            Assert.IsNotNull(cone);
            Assert.IsTrue(cone.dualStream);
            Assert.LessOrEqual(cone.range, 8f, "alcance curto");
            int ticks = cone.ScheduledTicks;
            Assert.AreEqual(4, ticks);
            var e = b.primary.attack.effect;
            int near = Mathf.RoundToInt(e.damage * e.DistanceMultiplier(0.5f, cone.range)) * ticks;
            int far = Mathf.RoundToInt(e.damage * e.DistanceMultiplier(cone.range, cone.range)) * ticks;
            Assert.That(near, Is.InRange(50, 60));
            Assert.That(far, Is.InRange(50, 60));
            Assert.AreEqual(ticks, cone.maxTicksPerTarget, "limite por alvo");
        }

        [Test]
        public void MrSPoisonGrenadesLast12Seconds()
        {
            var b = Get("sprayer");
            Assert.IsInstanceOf<ArcProjectileDelivery>(b.super.attack.delivery);
            var z = b.super.attack.effect.zoneOnImpact;
            Assert.IsTrue(z.enabled);
            Assert.AreEqual(12f, z.duration);
            Assert.Greater(z.tickDamage, 0);
            Assert.IsTrue(z.nonStackingPerAttacker, "regra de acumulação");
            Assert.AreEqual(ProjectileStyle.Grenade, b.super.attack.visuals.style);
        }

        // ------------------------------------------------------------------ Riot

        [Test]
        public void RiotPumpShotgunLikeFortnite()
        {
            var b = Get("shotgunner");
            var a = b.primary.attack;
            var d = a.delivery as PumpShotgunDelivery;
            Assert.IsNotNull(d, "caçadeira pump (hitscan)");
            Assert.AreEqual(10, a.delivery.ShotCount, "10 bagos");
            Assert.AreEqual(3, d.minPelletsToDamage, "mínimo de 3 bagos");
            Assert.Greater(d.spreadDegrees, 0f, "espalhamento");
            Assert.AreEqual(18f, d.EffectiveRange, 0.01f);
            var e = a.effect;
            Assert.AreEqual(1f, e.DistanceMultiplier(0f, a.Range), 0.001f);
            Assert.AreEqual(1f, e.DistanceMultiplier(6f, a.Range), 0.001f, "dano total até 6 m");
            Assert.AreEqual(0.8f, e.DistanceMultiplier(10f, a.Range), 0.001f);
            Assert.AreEqual(0.6f, e.DistanceMultiplier(13f, a.Range), 0.001f);
            Assert.AreEqual(0.49f, e.DistanceMultiplier(15f, a.Range), 0.001f);
            Assert.AreEqual(0.35f, e.DistanceMultiplier(18f, a.Range), 0.001f);
            Assert.Less(e.DistanceMultiplier(a.Range, a.Range), 0.35f, "continua a descer até ao alcance máximo");
            Assert.AreEqual(60, ShotTotal(a, 3f), "10 × 6 de perto");
        }

        [Test]
        public void PumpPelletsAreDeterministicAndInsideCone()
        {
            var b = Get("shotgunner");
            var d = (PumpShotgunDelivery)b.primary.attack.delivery;
            var ctx = new AttackContext { attack = b.primary.attack, direction = Vector3.forward, seed = 1234 };
            for (int i = 0; i < d.ShotCount; i++)
            {
                var dir = d.PelletDirection(ctx, i);
                Assert.AreEqual(dir, d.PelletDirection(ctx, i), "igual em todas as máquinas");
                Assert.LessOrEqual(Vector3.Angle(Vector3.forward, dir), d.spreadDegrees + d.jitterDegrees * 1.5f + 0.01f);
            }
        }

        [Test]
        public void RiotKeepsBrasaSuper()
        {
            var b = Get("shotgunner");
            var e = b.super.attack.effect;
            Assert.Greater(e.knockback, 0f, "empurrão");
            Assert.GreaterOrEqual(e.coverDamage, 100, "destrói cobertura");
            Assert.AreEqual(SuperActivation.Instant, b.super.activation);
        }

        // ------------------------------------------------------------------ Lupus Marinus

        [Test]
        public void LupusTntDirectHitsTotal120()
        {
            var b = Get("artillery");
            var a = b.primary.attack;
            var d = a.delivery as ArcProjectileDelivery;
            Assert.IsNotNull(d);
            Assert.IsTrue(d.hitsPlayers, "acerto direto");
            Assert.AreEqual(0f, d.fuseDelay);
            Assert.AreEqual(ProjectileStyle.Tnt, a.visuals.style);
            Assert.AreEqual(40, a.effect.SplashDamageAt(0f));
            Assert.AreEqual(120, 3 * a.effect.SplashDamageAt(0f));
            int edge = a.effect.SplashDamageAt(a.effect.splashRadius);
            Assert.Greater(edge, 0);
            Assert.Less(edge, 40, "reduz com a distância");
        }

        [Test]
        public void LupusPlacesFiveProximityMines()
        {
            var b = Get("artillery");
            var d = b.super.attack.delivery as PlaceTrapsDelivery;
            Assert.IsNotNull(d);
            Assert.AreEqual(5, d.count);
            Assert.AreEqual(TrapKind.Explosive, d.trap.kind);
            Assert.Greater(d.trap.triggerRadius, 0f);
            Assert.Greater(d.trap.warningTime, 0f);
            Assert.Greater(d.trap.armTime, 0f);
            Assert.Greater(d.trap.lifetime, 0f);
            Assert.GreaterOrEqual(d.trap.maxActivePerOwner, 5);
            Assert.Greater(b.super.attack.effect.splashDamage, 0);
        }

        // ------------------------------------------------------------------ N.Cage

        [Test]
        public void NCageThreeArrowsTotal100()
        {
            var b = Get("trapper");
            var a = b.primary.attack;
            Assert.AreEqual(3, a.delivery.ShotCount);
            Assert.IsFalse(a.effect.canHeadshot);
            Assert.AreEqual(0, a.effect.splashDamage);
            Assert.AreEqual(100, ShotTotal(a, 5f));
            Assert.Greater(((ProjectileDelivery)a.delivery).gravity, 0f, "trajetória visível");
            Assert.AreEqual(ProjectileStyle.Arrow, a.visuals.style);
        }

        [Test]
        public void NCageSnaresRoot2sAnd16Poison()
        {
            var b = Get("trapper");
            var d = b.super.attack.delivery as PlaceTrapsDelivery;
            Assert.IsNotNull(d);
            Assert.AreEqual(3, d.count);
            Assert.AreEqual(TrapKind.Snare, d.trap.kind);
            var e = b.super.attack.effect;
            Assert.AreEqual(2f, e.rootDuration);
            Assert.AreEqual(16f, e.dotDamagePerSecond * e.dotDuration, 0.001f);
            Assert.Greater(e.rootImmunity, 0f, "evita cadeias de armadilhas");
        }

        // ------------------------------------------------------------------ Lince

        [Test]
        public void LinceBody55HeadGrowsWithDistanceAndSlowestCadence()
        {
            var b = Get("sniper");
            var a = b.primary.attack;
            Assert.AreEqual(55, a.effect.DirectDamage(0, 10f, a.Range, false));
            Assert.AreEqual(55, a.effect.DirectDamage(0, a.Range, a.Range, false));
            // headshot cresce com a distância: 80 a 1 m, 150 a 60 m (linear)
            Assert.AreEqual(60f, a.Range, 0.01f, "alcance 60 m");
            Assert.AreEqual(80, a.effect.DirectDamage(0, 0.5f, a.Range, true));
            Assert.AreEqual(80, a.effect.DirectDamage(0, 1f, a.Range, true));
            Assert.AreEqual(115, a.effect.DirectDamage(0, 30.5f, a.Range, true));
            Assert.AreEqual(150, a.effect.DirectDamage(0, 60f, a.Range, true));
            Assert.Less(a.effect.DirectDamage(0, 10f, a.Range, true), a.effect.DirectDamage(0, 40f, a.Range, true));
            Assert.AreEqual(RigStyle.Sniper, b.firstPerson.rig);
            foreach (var other in Registry.brawlers)
                if (other != b) Assert.Greater(b.primary.cooldown, other.primary.cooldown, "cadência menor que " + other.displayName);
        }

        [Test]
        public void LinceFatalShot()
        {
            var b = Get("sniper");
            Assert.IsTrue(b.super.IsMode);
            Assert.Greater(b.super.passiveChargePerSecond, 0f, "carrega com o tempo");
            Assert.IsTrue(b.super.mode.consumeOnFirstShot);
            Assert.AreEqual(1, b.super.mode.maxShots);
            var e = b.super.attack.effect;
            Assert.IsTrue(e.executeOnHeadshot);
            Assert.AreEqual(55, e.DirectDamage(0, 5f, b.super.attack.Range, false), "no corpo não elimina");
            Assert.IsInstanceOf<HitscanDelivery>(b.super.attack.delivery);
        }

        [Test]
        public void AllSixSpecialsAreConfigured()
        {
            foreach (var b in Registry.brawlers)
            {
                Assert.IsNotNull(b.super.attack, b.displayName);
                Assert.IsNotNull(b.super.attack.delivery, b.displayName);
                Assert.IsNotNull(b.primary.attack, b.displayName);
            }
        }
    }
}
