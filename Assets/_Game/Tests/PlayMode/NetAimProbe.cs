using System.Collections;
using System.Reflection;
using UnityEngine;

namespace FPSLAN.Tests
{
    /// <summary>
    /// Sonda para testes de rede manuais (só Editor): o jogador local (cliente, sniper hitscan) aponta exatamente ao
    /// peito de um inimigo em movimento TAL COMO O VÊ e dispara. Conta quantos disparos o host confirma.
    /// Com compensação de latência, quase todos deviam acertar; sem ela, falham contra alvos em movimento.
    /// </summary>
    public class NetAimProbe : MonoBehaviour
    {
        public int shots = 20;
        public static string Result = "";
        public static bool Done;

        const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

        IEnumerator Start()
        {
            Done = false;
            int fired = 0, startHits = GameUI.HitConfirmCount;
            float started = Time.time;
            Result = "running";
            while (fired < shots && Time.time - started < 180f)
            {
                yield return new WaitForSeconds(1.3f);
                var me = PlayerNetwork.Local;
                if (me == null || me.Health.IsDead) continue;
                var target = FindTarget(me, out var aim);
                if (target == null)
                {
                    MoveNearEnemy(me);
                    continue;
                }
                var eye = me.Motor.EyePosition;
                var dir = (aim - eye).normalized;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;
                typeof(PlayerMotor).GetField("m_Yaw", Flags).SetValue(me.Motor, yaw);
                typeof(PlayerMotor).GetField("m_Pitch", Flags).SetValue(me.Motor, pitch);
                typeof(PlayerCombat).GetField("m_Ammo", Flags).SetValue(me.Combat, 3f);
                typeof(PlayerCombat).GetField("m_Cooldown", Flags).SetValue(me.Combat, 0f);
                typeof(PlayerCombat).GetMethod("FireLocal", Flags).Invoke(me.Combat, new object[] { false });
                fired++;
            }
            yield return new WaitForSeconds(1.5f); // últimas confirmações
            int hits = GameUI.HitConfirmCount - startHits;
            var local = PlayerNetwork.Local;
            Result = "shots=" + fired + " hits=" + hits + " ping=" + (local != null ? local.LocalPingMs : -1) + "ms";
            Done = true;
        }

        static PlayerNetwork FindTarget(PlayerNetwork me, out Vector3 aim)
        {
            aim = Vector3.zero;
            PlayerNetwork best = null;
            float bestD = float.MaxValue;
            var eye = me.Motor.EyePosition;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == me || !p.IsSpawned || p.Health.IsDead || !p.Motor.IsMoving) continue;
                var chest = p.transform.position + Vector3.up * 1.0f;
                float d = (chest - eye).magnitude;
                if (d > 45f || d < 4f) continue;
                if (Physics.Linecast(eye, chest, out var hit, CombatMath.ShotMask, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<PlayerNetwork>() != p) continue;
                if (d < bestD) { bestD = d; best = p; aim = chest; }
            }
            return best;
        }

        static void MoveNearEnemy(PlayerNetwork me)
        {
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == me || p.Health.IsDead) continue;
                for (int a = 0; a < 16; a++)
                {
                    var dir = Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward;
                    var cand = p.transform.position + dir * 12f;
                    if (!Physics.Raycast(cand + Vector3.up * 3f, Vector3.down, out var g, 6f, CombatMath.ShotMask, QueryTriggerInteraction.Ignore)) continue;
                    var spot = g.point + Vector3.up * 0.05f;
                    var chest = p.transform.position + Vector3.up;
                    if (Physics.Linecast(spot + Vector3.up * 1.6f, chest, out var h, CombatMath.ShotMask, QueryTriggerInteraction.Ignore) &&
                        h.collider.GetComponentInParent<PlayerNetwork>() != p) continue;
                    typeof(PlayerMotor).GetMethod("TeleportLocal", Flags).Invoke(me.Motor,
                        new object[] { spot, Quaternion.LookRotation(-dir).eulerAngles.y });
                    return;
                }
            }
        }
    }
}
