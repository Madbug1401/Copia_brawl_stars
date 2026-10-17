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
        public bool meleePressed;
        public bool superPressed;

        public static PlayerCommands FromHuman(bool allowed)
        {
            if (!allowed) return default;
            var local = PlayerNetwork.Local;
            bool aiming = InputReader.AimHeld && local != null && local.Brawler != null &&
                local.Brawler.firstPerson.rig == RigStyle.Sniper;
            float sensitivity = aiming ? GameSession.AimSensitivity : 1f;
            return new PlayerCommands
            {
                move = InputReader.Move,
                lookDelta = InputReader.Look * GameSession.Sensitivity * sensitivity,
                sprint = InputReader.Sprint,
                jumpPressed = InputReader.JumpPressed,
                fire = InputReader.Fire,
                meleePressed = InputReader.MeleePressed,
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

        /// <summary>Velocidade inicial de um arco com gravidade que cai exatamente em 'to'.</summary>
        public static Vector3 ArcVelocity(Vector3 from, Vector3 to, float gravity, float horizontalSpeed, float minTime, float maxTime, out float flightTime)
        {
            var delta = to - from;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            flightTime = Mathf.Clamp(horizontal / Mathf.Max(1f, horizontalSpeed), minTime, maxTime);
            var g = Vector3.down * gravity;
            return (delta - 0.5f * g * flightTime * flightTime) / flightTime;
        }

        // ------------------------------------------------------------------ cabeça / distância ao corpo

        /// <summary>Altura (a partir dos pés) do centro da esfera da cabeça e o seu raio.</summary>
        public const float HeadCenterHeight = 1.6f;
        public const float HeadRadius = 0.3f;

        /// <summary>
        /// Deteção de headshot (corre no host): o ponto de impacto está na zona da cabeça, ou a linha do disparo passa
        /// pela esfera da cabeça antes de seguir para lá do ponto atingido. Não há tolerância extra nem mira automática.
        /// </summary>
        public static bool IsHeadshot(Transform victim, Vector3 rayOrigin, Vector3 rayDir, Vector3 hitPoint)
        {
            if (victim == null) return false;
            var feet = victim.position;
            if (hitPoint.y - feet.y >= CombatSystem.HeadshotHeight) return true;
            if (rayDir.sqrMagnitude < 0.0001f) return false;
            var dir = rayDir.normalized;
            var head = feet + Vector3.up * HeadCenterHeight;
            float along = Vector3.Dot(head - rayOrigin, dir);
            if (along < 0f) return false;
            // Só conta se a cabeça estiver (aproximadamente) no ponto onde o disparo tocou o jogador
            float hitAlong = Vector3.Dot(hitPoint - rayOrigin, dir);
            if (along > hitAlong + HeadRadius * 2f) return false;
            var closest = rayOrigin + dir * along;
            return (closest - head).sqrMagnitude <= HeadRadius * HeadRadius;
        }

        /// <summary>Distância de um ponto à cápsula do jogador (0 se estiver dentro).</summary>
        public static float DistanceToBody(Component player, Vector3 point)
        {
            if (player == null) return float.MaxValue;
            var t = player.transform;
            float height = 1.8f, radius = 0.4f;
            var center = new Vector3(0f, 0.9f, 0f);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                height = cc.height;
                radius = cc.radius;
                center = cc.center;
            }
            var a = t.position + center + Vector3.up * Mathf.Max(0f, height * 0.5f - radius);
            var b = t.position + center - Vector3.up * Mathf.Max(0f, height * 0.5f - radius);
            var ab = a - b;
            float k = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - b, ab) / ab.sqrMagnitude) : 0f;
            var onAxis = b + ab * k;
            return Mathf.Max(0f, Vector3.Distance(point, onAxis) - radius);
        }

        static readonly Collider[] s_Overlap = new Collider[16];
        const int IgnoreRaycastLayer = 2;

        /// <summary>
        /// Posição válida para uma armadilha: há chão firme por baixo, não está dentro de paredes/obstáculos,
        /// não está em barreiras invisíveis (água/limites) e fica dentro da arena. Determinístico (só cenário).
        /// </summary>
        public static bool TryGetTrapPosition(Vector3 wanted, out Vector3 ground)
        {
            ground = wanted;
            if (!Physics.Raycast(wanted + Vector3.up * 1.2f, Vector3.down, out var hit, 4f, ShotMask, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.GetComponentInParent<PlayerHealth>() != null) return false;
            if (hit.normal.y < 0.6f) return false; // parede/rampa demasiado inclinada
            ground = hit.point;

            float limit = MapInfo.HalfSize - 0.4f;
            if (Mathf.Abs(ground.x) > limit || Mathf.Abs(ground.z) > limit) return false;

            // Dentro de geometria (ex.: aponta para a base de uma parede)
            int n = Physics.OverlapSphereNonAlloc(ground + Vector3.up * 0.45f, 0.3f, s_Overlap, ShotMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (s_Overlap[i] != null && s_Overlap[i].GetComponentInParent<PlayerHealth>() == null) return false;

            // Água e limites usam a camada Ignore Raycast
            if (Physics.CheckSphere(ground + Vector3.up * 0.5f, 0.35f, 1 << IgnoreRaycastLayer, QueryTriggerInteraction.Ignore))
                return false;
            return true;
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
