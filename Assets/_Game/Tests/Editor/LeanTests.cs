using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace FPSLAN.Tests
{
    public class LeanTests
    {
        static KeyCode Default(GameAction a)
        {
            var m = typeof(GameInputSettings).GetMethod("DefaultBinding", BindingFlags.NonPublic | BindingFlags.Static);
            return (KeyCode)m.Invoke(null, new object[] { a });
        }

        [Test]
        public void LeanOnQEAndOldActionsMovedToCX()
        {
            Assert.AreEqual(KeyCode.Q, Default(GameAction.LeanLeft));
            Assert.AreEqual(KeyCode.E, Default(GameAction.LeanRight));
            Assert.AreEqual(KeyCode.C, Default(GameAction.Melee));
            Assert.AreEqual(KeyCode.X, Default(GameAction.Super));
            Assert.AreEqual(KeyCode.LeftControl, Default(GameAction.Crouch));
        }

        [Test]
        public void DefaultBindingsAreUnique()
        {
            var seen = new Dictionary<KeyCode, GameAction>();
            foreach (GameAction a in System.Enum.GetValues(typeof(GameAction)))
            {
                var k = Default(a);
                Assert.AreNotEqual(KeyCode.None, k, a + " sem tecla por defeito");
                Assert.IsFalse(seen.ContainsKey(k), a + " usa a mesma tecla que " + (seen.ContainsKey(k) ? seen[k].ToString() : ""));
                seen[k] = a;
            }
        }

        [Test]
        public void LeanedHeadIsStillAHeadshot()
        {
            // Cabeça deslocada 0,36 m para a direita: um tiro nesse ponto conta; no centro antigo já não
            var feet = Vector3.zero;
            var leanFeet = feet + Vector3.right * 0.36f;
            var head = leanFeet + Vector3.up * CombatMath.HeadCenterHeight;
            Assert.IsTrue(CombatMath.IsHeadshot(leanFeet, head, head - Vector3.forward * 10f, Vector3.forward));
            var oldHead = feet + Vector3.up * CombatMath.HeadCenterHeight + Vector3.left * 0.05f;
            Assert.IsFalse(CombatMath.IsHeadshot(leanFeet, oldHead, oldHead - Vector3.forward * 10f, Vector3.forward));
        }
    }
}
