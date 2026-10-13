using UnityEngine;

namespace FPSLAN
{
    public enum BotDifficulty
    {
        Facil,
        Normal,
        Dificil
    }

    /// <summary>
    /// IA simples de bot (corre só no host). Produz PlayerCommands como um jogador humano:
    /// escolhe alvo visível, aproxima-se até ao alcance preferido do personagem, faz strafe,
    /// aponta com erro e tempo de reação conforme a dificuldade, e usa o Super quando faz sentido.
    /// Navegação por steering com desvio de obstáculos (sem NavMesh), suficiente para arenas pequenas.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class BotBrain : MonoBehaviour
    {
        public PlayerCommands Commands { get; private set; }

        PlayerNetwork m_Self;
        BotDifficulty m_Difficulty;

        PlayerNetwork m_Target;
        bool m_TargetVisible;
        float m_NextThink;
        float m_TargetSeenAt;

        float m_Yaw, m_Pitch;
        Vector3 m_WanderPoint;
        float m_StrafeDir = 1f;
        float m_NextStrafeSwitch;
        Vector3 m_LastPos;
        float m_StuckTimer;
        float m_UnstickUntil;
        Vector2 m_UnstickMove;
        float m_NoiseSeed;
        float m_EscapeUntil;
        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        // Afinação por dificuldade
        float m_AimError;      // graus
        float m_TurnSpeed;     // graus/s
        float m_Reaction;      // s até começar a disparar depois de ver o alvo
        float m_FireAngle;     // erro máximo de pontaria para disparar

        public void Init(PlayerNetwork self, BotDifficulty difficulty)
        {
            m_Self = self;
            SetDifficulty(difficulty);
            m_NoiseSeed = Random.value * 100f;
            m_Yaw = self.transform.eulerAngles.y;
            PickWanderPoint();
        }

        public void SetDifficulty(BotDifficulty d)
        {
            m_Difficulty = d;
            switch (d)
            {
                case BotDifficulty.Facil: m_AimError = 7f; m_TurnSpeed = 160f; m_Reaction = 0.7f; m_FireAngle = 12f; break;
                case BotDifficulty.Dificil: m_AimError = 1.2f; m_TurnSpeed = 600f; m_Reaction = 0.18f; m_FireAngle = 5f; break;
                default: m_AimError = 3.5f; m_TurnSpeed = 320f; m_Reaction = 0.38f; m_FireAngle = 8f; break;
            }
        }

        public void OnTeleported(float yaw)
        {
            m_Yaw = yaw;
            m_Pitch = 0f;
            m_Target = null;
            PickWanderPoint();
        }

        public void OnDamaged()
        {
            // Reage mais depressa a quem o ataca: força nova procura de alvo
            m_NextThink = 0f;
        }

        void Update()
        {
            if (m_Self == null || !m_Self.IsSpawned) return;

            if (m_Self.Health.IsDead || MatchManager.IsMatchOver)
            {
                Commands = default;
                return;
            }

            float now = Time.time;
            float dt = Time.deltaTime;
            var brawler = m_Self.Brawler;
            if (brawler == null) return;
            var profile = brawler.bot;
            var pos = m_Self.transform.position;

            if (now >= m_NextThink)
            {
                m_NextThink = now + 0.2f;
                Think();
            }

            var cmd = new PlayerCommands { absoluteAim = true };

            // ---------------- Pontaria ----------------
            Vector3 lookTarget;
            float distToTarget = 999f;
            if (m_Target != null)
            {
                var tp = m_Target.transform.position + Vector3.up * (profile.aimAtFeet ? 0.2f : 1.15f);
                // Ligeira antecipação do movimento do alvo nos níveis mais altos
                if (m_Difficulty != BotDifficulty.Facil) tp += EstimateVelocity(m_Target) * 0.12f;
                lookTarget = tp;
                distToTarget = Vector3.Distance(pos, m_Target.transform.position);
            }
            else
            {
                lookTarget = m_WanderPoint + Vector3.up * 1.4f;
            }

            var eye = m_Self.Motor.EyePosition;
            var toLook = lookTarget - eye;
            float desiredYaw = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg;
            float desiredPitch = -Mathf.Atan2(toLook.y, new Vector2(toLook.x, toLook.z).magnitude) * Mathf.Rad2Deg;

            bool escaping = now < m_EscapeUntil && m_Target != null;
            if (escaping)
            {
                // Fuga com o Super (salto): vira-se de costas para o alvo e aponta longe
                desiredYaw += 180f;
                desiredPitch = -12f;
            }
            else if (m_Target != null)
            {
                // Erro de pontaria suave (Perlin) — os bots não acertam tudo
                float t = now * 0.9f;
                desiredYaw += (Mathf.PerlinNoise(m_NoiseSeed, t) - 0.5f) * 2f * m_AimError;
                desiredPitch += (Mathf.PerlinNoise(t, m_NoiseSeed) - 0.5f) * 2f * m_AimError * 0.6f;
            }
            else
            {
                desiredPitch = 0f;
            }

            m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, desiredYaw, (escaping ? 900f : m_TurnSpeed) * dt);
            m_Pitch = Mathf.MoveTowards(m_Pitch, Mathf.Clamp(desiredPitch, -60f, 60f), m_TurnSpeed * 0.6f * dt);
            cmd.yaw = m_Yaw;
            cmd.pitch = m_Pitch;

            // ---------------- Movimento ----------------
            Vector3 moveWorld;
            if (m_Target != null)
            {
                var flat = m_Target.transform.position - pos;
                flat.y = 0f;
                var toward = flat.sqrMagnitude > 0.01f ? flat.normalized : m_Self.transform.forward;
                float pref = profile.preferredRange;

                Vector3 radial = Vector3.zero;
                if (!m_TargetVisible || distToTarget > pref + 2f) radial = toward;
                else if (distToTarget < pref - 3f) radial = -toward;

                if (now >= m_NextStrafeSwitch)
                {
                    m_NextStrafeSwitch = now + Random.Range(0.8f, 2.2f);
                    m_StrafeDir = Random.value < 0.5f ? -1f : 1f;
                }
                var strafe = Vector3.Cross(Vector3.up, toward) * m_StrafeDir * (m_TargetVisible ? 0.8f : 0.2f);
                moveWorld = radial + strafe;
            }
            else
            {
                var flat = m_WanderPoint - pos;
                flat.y = 0f;
                if (flat.magnitude < 2f) PickWanderPoint();
                moveWorld = flat.normalized;
            }

            if (moveWorld.sqrMagnitude > 1f) moveWorld.Normalize();
            moveWorld = AvoidObstacles(pos, moveWorld, out bool wantJump);

            // Preso? Tenta outra direção e salta
            m_StuckTimer += dt;
            if (m_StuckTimer > 1.2f)
            {
                if (Vector3.Distance(pos, m_LastPos) < 0.6f && moveWorld.sqrMagnitude > 0.1f)
                {
                    m_UnstickUntil = now + 0.8f;
                    var r = Random.insideUnitCircle.normalized;
                    m_UnstickMove = r;
                    wantJump = true;
                }
                m_StuckTimer = 0f;
                m_LastPos = pos;
            }

            // Converter direção do mundo para input local (relativo ao yaw atual)
            var yawRot = Quaternion.Euler(0f, m_Yaw, 0f);
            var local = Quaternion.Inverse(yawRot) * moveWorld;
            cmd.move = now < m_UnstickUntil ? m_UnstickMove : new Vector2(local.x, local.z);
            cmd.sprint = m_Target == null || !m_TargetVisible;
            cmd.jumpPressed = wantJump || (m_TargetVisible && Random.value < 0.004f);

            // ---------------- Disparo ----------------
            if (m_Target != null && m_TargetVisible && now - m_TargetSeenAt >= m_Reaction)
            {
                float aimErr = Mathf.Abs(Mathf.DeltaAngle(m_Yaw, Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg));
                bool inRange = brawler.primary.attack != null && distToTarget <= brawler.primary.attack.Range * 0.92f;
                cmd.fire = inRange && aimErr <= m_FireAngle;

                if (m_Self.Combat.SuperReady)
                {
                    float superRange = profile.superUseRange;
                    if (profile.superIsEscape)
                    {
                        if (distToTarget <= superRange && !escaping) m_EscapeUntil = now + 0.7f;
                        cmd.fire = cmd.fire && !escaping;
                    }
                    else cmd.superPressed = distToTarget <= superRange && aimErr <= m_FireAngle;
                }
            }

            if (escaping && m_Self.Combat.SuperReady)
            {
                float away = Mathf.Atan2(toLook.x, toLook.z) * Mathf.Rad2Deg + 180f;
                if (Mathf.Abs(Mathf.DeltaAngle(m_Yaw, away)) < 20f)
                {
                    cmd.superPressed = true;
                    m_EscapeUntil = 0f;
                }
            }

            Commands = cmd;
        }

        void Think()
        {
            PlayerNetwork best = null;
            float bestScore = float.MaxValue;
            bool bestVisible = false;

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == m_Self || !p.IsSpawned || p.Health.IsDead) continue;
                if (MatchManager.AreTeammates(m_Self, p)) continue;
                float d = Vector3.Distance(m_Self.transform.position, p.transform.position);
                if (d > 45f) continue;
                // Escondido num arbusto (e não revelado): o bot não o vê
                if (Bush.Conceals(p, m_Self.transform.position)) continue;
                bool visible = CombatMath.HasLineOfSight(m_Self, p);
                // Preferir alvos visíveis e próximos; manter o alvo atual (histerese)
                float score = d + (visible ? 0f : 25f) + (p == m_Target ? -5f : 0f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = p;
                    bestVisible = visible;
                }
            }

            if (best != m_Target || (bestVisible && !m_TargetVisible)) m_TargetSeenAt = Time.time;
            m_Target = best;
            m_TargetVisible = bestVisible;
        }

        Vector3 AvoidObstacles(Vector3 pos, Vector3 dir, out bool wantJump)
        {
            wantJump = false;
            if (dir.sqrMagnitude < 0.01f) return dir;

            const float look = 1.6f;
            var knee = pos + Vector3.up * 0.5f;
            if (!Blocked(knee, dir, look)) return dir;

            // Obstáculo baixo? saltar
            var chest = pos + Vector3.up * 1.6f;
            if (!Blocked(chest, dir, look))
            {
                wantJump = true;
                return dir;
            }

            // Desviar: tenta ângulos alternados
            for (int i = 1; i <= 4; i++)
            {
                float a = 35f * i;
                var left = Quaternion.Euler(0f, -a, 0f) * dir;
                var right = Quaternion.Euler(0f, a, 0f) * dir;
                bool preferRight = m_StrafeDir > 0f;
                var first = preferRight ? right : left;
                var second = preferRight ? left : right;
                if (!Blocked(knee, first, look)) return first;
                if (!Blocked(knee, second, look)) return second;
            }
            return -dir;
        }

        bool Blocked(Vector3 from, Vector3 dir, float dist)
        {
            // Todas as camadas (inclui barreiras invisíveis como a água), exceto jogadores
            int n = Physics.RaycastNonAlloc(from, dir.normalized, s_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_Hits[i].collider;
                if (c == null || c.GetComponentInParent<PlayerHealth>() != null) continue;
                return true;
            }
            return false;
        }

        void PickWanderPoint()
        {
            // Pontos de patrulha do mapa (ou aleatórios), com tendência para o centro (onde há luta)
            if (Random.value < 0.3f)
            {
                var r = Random.insideUnitCircle * 5f;
                m_WanderPoint = new Vector3(r.x, 0f, r.y);
                if (MapInfo.Instance != null && MapInfo.Instance.waypoints != null && MapInfo.Instance.waypoints.Length > 0)
                    m_WanderPoint = MapInfo.RandomWaypoint();
            }
            else m_WanderPoint = MapInfo.RandomWaypoint();
        }

        static Vector3 EstimateVelocity(PlayerNetwork p)
        {
            if (p.IsOwner) return p.Motor.HorizontalVelocity;
            // Remotos: sem velocidade exata; usa a direção do estado e assume velocidade de corrida
            return p.Motor.IsMoving ? p.transform.forward * p.Brawler.movement.walkSpeed * 0.5f : Vector3.zero;
        }
    }
}
