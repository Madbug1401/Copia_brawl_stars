using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Comandos de um frame para controlar um Player. Vêm do teclado/rato (humano) ou do BotBrain (bot),
    /// para que o movimento e o combate usem exatamente o mesmo código.
    /// </summary>
    public struct PlayerCommands
    {
        public Vector2 move;
        public Vector2 lookDelta;    // humano: delta do rato (graus)
        public bool absoluteAim;     // bot: usa yaw/pitch absolutos
        public float yaw, pitch;
        public bool sprint;
        public bool jumpPressed;
        public bool fire;
        public bool superPressed;

        public static PlayerCommands FromHuman(bool allowed)
        {
            if (!allowed) return default;
            return new PlayerCommands
            {
                move = InputReader.Move,
                lookDelta = InputReader.Look * GameSession.Sensitivity,
                sprint = InputReader.Sprint,
                jumpPressed = InputReader.JumpPressed,
                fire = InputReader.Fire,
                superPressed = InputReader.SuperPressed,
            };
        }
    }

    public static class CombatMath
    {
        static readonly RaycastHit[] s_Hits = new RaycastHit[24];

        /// <summary>
        /// Camadas que bloqueiam ataques e linha de visão. Exclui "Ignore Raycast" (usada pelas barreiras invisíveis
        /// do mapa: água e limites, que travam o movimento mas deixam passar tiros).
        /// </summary>
        public static int ShotMask => Physics.DefaultRaycastLayers;

        /// <summary>Primeiro ponto atingido ao longo de um raio, ignorando os colisores do próprio jogador.</summary>
        public static bool Raycast(Vector3 origin, Vector3 dir, float maxDist, PlayerNetwork ignore, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(origin, dir, s_Hits, maxDist, ShotMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_Hits[i];
                if (h.distance >= bestDist) continue;
                if (ignore != null && h.collider.transform.IsChildOf(ignore.transform)) continue;
                best = h;
                bestDist = h.distance;
                found = true;
            }
            return found;
        }

        /// <summary>Chão por baixo de um ponto (ignora o próprio jogador).</summary>
        public static Vector3 GroundBelow(Vector3 point, PlayerNetwork ignore)
        {
            if (Raycast(point + Vector3.up * 0.3f, Vector3.down, 40f, ignore, out var hit)) return hit.point;
            return point;
        }

        /// <summary>Ponto para onde o jogador está a apontar (ou o ponto máximo do alcance).</summary>
        public static Vector3 AimPoint(PlayerNetwork shooter, float range)
        {
            var origin = shooter.Motor.EyePosition;
            var dir = shooter.Motor.AimDirection;
            if (Raycast(origin, dir, range, shooter, out var hit)) return hit.point;
            // Sem nada à frente: cai no chão por baixo do ponto máximo
            var far = origin + dir * range;
            if (Raycast(far + Vector3.up * 0.5f, Vector3.down, 50f, shooter, out var down)) return down.point;
            return far;
        }

        /// <summary>[Legado — usado só pelo ProjectileSystem] Velocidade de um arco a partir de AttackSpec.</summary>
        public static Vector3 LobVelocity(Vector3 from, Vector3 target, AttackSpec spec, out float flightTime)
        {
            var delta = target - from;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            flightTime = Mathf.Clamp(horizontal / Mathf.Max(1f, spec.speed), spec.minFlightTime, spec.maxFlightTime);
            var g = Vector3.down * spec.gravity;
            return (delta - 0.5f * g * flightTime * flightTime) / flightTime;
        }

        /// <summary>Velocidade inicial de um arco com gravidade que cai exatamente em 'to'.</summary>
        public static Vector3 ArcVelocity(Vector3 from, Vector3 to, float gravity, float horizontalSpeed, float minTime, float maxTime, out float flightTime)
        {
            var delta = to - from;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            flightTime = Mathf.Clamp(horizontal / Mathf.Max(1f, horizontalSpeed), minTime, maxTime);
            var g = Vector3.down * gravity;
            return (delta - 0.5f * g * flightTime * flightTime) / flightTime;
        }

        public static bool HasLineOfSight(PlayerNetwork from, PlayerNetwork to)
        {
            var a = from.Motor.EyePosition;
            var b = to.transform.position + Vector3.up * 1.3f;
            var d = b - a;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            if (!Raycast(a, d / dist, dist, from, out var hit)) return true;
            return hit.collider.transform.IsChildOf(to.transform);
        }
    }
}
