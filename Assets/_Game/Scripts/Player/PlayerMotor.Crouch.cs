using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Agachar e slide.
    /// - Agachar (manter Ctrl/C): cápsula mais baixa, câmara mais baixa, anda mais devagar. Só volta a pôr-se de pé
    ///   se houver espaço por cima.
    /// - Slide: carregar em agachar a correr (ou aterrar com agachar premido a correr) dá um impulso e desliza com
    ///   pouca fricção; em descidas acelera. Saltar durante o slide mantém o embalo (slide-jump).
    /// - No ar, quem vai mais rápido do que a velocidade normal (slide-jump, plataformas) perde o embalo devagar,
    ///   em vez de travar de repente.
    /// O dono simula; os outros recebem "agachado/a deslizar" nas flags do snapshot e ajustam a cápsula (hitbox),
    /// a altura dos olhos (validação de tiros no host) e o boneco (<see cref="CrouchPose"/>).
    /// </summary>
    public partial class PlayerMotor
    {
        [Header("Agachar / slide")]
        public float crouchHeight = 1.2f;
        public float crouchSpeedMultiplier = 0.5f;
        public float crouchBlendSpeed = 10f;
        [Tooltip("Velocidade à entrada do slide, × velocidade de sprint (estilo CoD: impulso forte logo no início).")]
        public float slideBoost = 1.45f;
        public float slideMaxSpeedMultiplier = 1.75f; // limite em relação à velocidade de sprint
        [Tooltip("Duração do slide em plano (s). Mantém quase toda a velocidade no início e perde-a no fim.")]
        public float slideDuration = 0.85f;
        public float slideMaxDuration = 1.6f;     // em descidas pode durar mais, até este limite
        [Tooltip("Velocidade no fim do slide, × velocidade a andar.")]
        public float slideEndSpeedFactor = 0.55f;
        public float slideSlopeAccel = 12f;       // m/s² extra em descidas (× inclinação)
        public float slideSteer = 0.5f;           // rad/s: quase não se vira, mas pode-se olhar à vontade
        public float slideCooldown = 0.35f;
        [Tooltip("Altura dos olhos no slide em relação a estar de pé (mais baixo do que agachado).")]
        public float slideEyeScale = 0.5f;
        public float slideDropSpeed = 9f;         // quão depressa a câmara desce no início do slide
        public float airMomentumDrag = 2.5f;      // m/s² de perda de embalo no ar acima da velocidade normal
        public float airMomentumSteer = 2.2f;     // rad/s

        float m_StandHeight = 1.8f;
        Vector3 m_StandCenter = new Vector3(0f, 0.9f, 0f);
        float m_StandEye = 1.6f, m_StandAim = 1.32f;
        bool m_CrouchCached;

        bool m_Crouched;          // dono: estado lógico (inclui slide)
        bool m_Sliding;
        float m_SlideTime, m_SlideCooldownTimer;
        Vector3 m_SlideDir;
        float m_SlideSpeed, m_SlideStartSpeed, m_SlideEndSpeed, m_SlideSlopeBonus;
        bool m_WasGrounded;
        float m_CrouchBlend;      // 0 de pé → 1 agachado (suavizado, usado pela câmara e pelo boneco)
        float m_SlideBlend;       // 0 → 1 câmara rente ao chão do slide
        float m_SlideRoll;        // inclinação lateral da câmara no slide
        float m_SlideKick;        // "mergulho" da câmara ao entrar no slide (decai)
        bool m_RemoteSliding;
        float m_SlideAirTime;

        public bool IsCrouching => IsOwner ? m_Crouched : m_CrouchBlend > 0.5f;
        public bool IsSliding => IsOwner ? m_Sliding : m_RemoteSliding;
        /// <summary>0 de pé → 1 totalmente agachado (suave).</summary>
        public float CrouchBlend => m_CrouchBlend;
        /// <summary>Escala vertical do corpo agora (1 de pé, ≈0,67 agachado).</summary>
        public float CrouchScale => Mathf.Lerp(1f, crouchHeight / Mathf.Max(0.1f, m_StandHeight), m_CrouchBlend);
        /// <summary>Quanto a cabeça desceu (m) em relação a estar de pé. Usado nos headshots.</summary>
        public float CrouchDrop => CombatMath.HeadCenterHeight * (1f - CrouchScale);
        /// <summary>Graus a somar ao pitch da câmara (mergulho curto no início do slide).</summary>
        float SlideCameraPitch => 5f * m_SlideKick * m_SlideKick;

        void CrouchAwake()
        {
            if (m_CrouchCached) return;
            m_CrouchCached = true;
            m_StandHeight = m_Controller.height;
            m_StandCenter = m_Controller.center;
            if (cameraRoot != null) m_StandEye = cameraRoot.localPosition.y;
            if (aimPivot != null) m_StandAim = aimPivot.localPosition.y;
            if (GetComponent<CrouchPose>() == null) gameObject.AddComponent<CrouchPose>();
        }

        // ------------------------------------------------------------------ dono

        void UpdateCrouchSlide(in PlayerCommands cmd, Vector3 wish, ref float speed, bool grounded, bool alive, bool rooted, float dt)
        {
            m_SlideCooldownTimer -= dt;
            bool justLanded = grounded && !m_WasGrounded;
            m_WasGrounded = grounded;

            bool wantCrouch = cmd.crouch && alive && !m_Launched;
            float flatSpeed = new Vector3(m_HorizontalVelocity.x, 0f, m_HorizontalVelocity.z).magnitude;
            float sprintSpeed = Mathf.Max(0.1f, m_Player.Brawler.movement.sprintSpeed * m_Player.SprintMultiplier *
                m_Player.SpeedMultiplier * m_Player.Health.SpeedMultiplier);
            float walkSpeed = m_Player.Brawler.movement.walkSpeed * m_Player.SpeedMultiplier * m_Player.Health.SpeedMultiplier;

            // --- iniciar slide (a correr + carregar em agachar, ou aterrar com agachar premido a correr) ---
            bool fastEnough = flatSpeed > walkSpeed + 0.5f;
            bool trigger = cmd.crouchPressed || (justLanded && cmd.crouch);
            if (!m_Sliding && trigger && grounded && alive && !rooted && !m_Launched && fastEnough &&
                (cmd.sprint || flatSpeed > walkSpeed * 1.25f) && m_SlideCooldownTimer <= 0f)
            {
                m_Sliding = true;
                m_SlideTime = 0f;
                m_SlideDir = flatSpeed > 0.1f ? m_HorizontalVelocity / flatSpeed : transform.forward;
                m_SlideDir.y = 0f;
                m_SlideDir.Normalize();
                m_SlideStartSpeed = Mathf.Min(Mathf.Max(flatSpeed, sprintSpeed) * slideBoost, sprintSpeed * slideMaxSpeedMultiplier);
                m_SlideEndSpeed = walkSpeed * slideEndSpeedFactor;
                m_SlideSpeed = m_SlideStartSpeed;
                m_SlideSlopeBonus = 0f;
                m_SlideAirTime = 0f;
                m_SlideKick = 1f;
                if (m_Player.IsLocalHuman)
                {
                    Sfx.Play2D(Sound.Twirl, 0.6f);
                    CameraShake.Add(0.22f);
                }
            }

            // --- terminar slide ---
            if (m_Sliding)
            {
                bool cancel = m_SlideTime > 0.15f && cmd.crouchPressed; // carregar outra vez cancela (fica agachado)
                bool end = !alive || rooted || m_Launched || cancel || m_SlideTime >= SlideTimeLimit();
                // Bateu numa parede: a velocidade real caiu muito
                if (!end && m_SlideTime > 0.12f)
                {
                    var real = m_Controller.velocity; real.y = 0f;
                    if (real.magnitude < m_SlideSpeed * 0.35f) end = true;
                }
                // Pequenos ressaltos não acabam o slide; sair mesmo de uma beira/rampa sim (mantém o embalo no ar)
                m_SlideAirTime = grounded ? 0f : m_SlideAirTime + dt;
                if (m_SlideAirTime > 0.15f)
                {
                    m_HorizontalVelocity = m_SlideDir * m_SlideSpeed;
                    end = true;
                }
                if (end) StopSlide();
            }

            // --- agachar / levantar (depois do slide: fica agachado se ainda tiver a tecla premida) ---
            if (m_Sliding || wantCrouch) SetCrouched(true);
            else if (m_Crouched && HasHeadroom()) SetCrouched(false);
            if (!alive) { m_Sliding = false; SetCrouched(false, true); }

            if (m_Crouched && !m_Sliding)
            {
                speed = Mathf.Min(speed, walkSpeed * crouchSpeedMultiplier);
                m_Sprinting = false;
            }
            else if (m_Sliding) m_Sprinting = false; // a arma não fica em pose de sprint; o FOV vem de IsSliding

            // --- câmara ---
            m_CrouchBlend = Mathf.MoveTowards(m_CrouchBlend, m_Crouched ? 1f : 0f, crouchBlendSpeed * dt);
            m_SlideBlend = Mathf.MoveTowards(m_SlideBlend, m_Sliding ? 1f : 0f, (m_Sliding ? slideDropSpeed : 5f) * dt);
            m_SlideKick = Mathf.MoveTowards(m_SlideKick, 0f, 3.5f * dt);
            float roll = 0f;
            if (m_Sliding) roll = 4f - Mathf.Clamp(Vector3.Dot(m_SlideDir, transform.right) * 8f, -8f, 8f);
            m_SlideRoll = Mathf.Lerp(m_SlideRoll, roll, 1f - Mathf.Exp(-10f * dt));
            if (m_Sliding && m_Player.IsLocalHuman)
                CameraShake.Add(0.45f * dt * Mathf.Clamp01(m_SlideSpeed / Mathf.Max(0.1f, sprintSpeed)));
            ApplyCrouchPose();
        }

        /// <summary>Tempo máximo do slide: mais em descidas, menos em subidas.</summary>
        float SlideTimeLimit()
        {
            float slope = SlideSlope();
            if (slope > 0.08f) return slideMaxDuration;
            return slideDuration;
        }

        /// <summary>Componente da descida na direção do slide (&gt;0 a descer, &lt;0 a subir).</summary>
        float SlideSlope()
        {
            if (!TryGroundNormal(out var n)) return 0f;
            var downhill = Vector3.ProjectOnPlane(Vector3.down, n);
            downhill.y = 0f;
            return Vector3.Dot(downhill, m_SlideDir);
        }

        void SlideVelocity(Vector3 wish, float dt)
        {
            // Direção praticamente fixa (como no CoD); a câmara roda à vontade
            if (wish.sqrMagnitude > 0.01f)
                m_SlideDir = Vector3.RotateTowards(m_SlideDir, wish.normalized, slideSteer * dt, 0f);

            float slope = SlideSlope();
            // Em descidas o tempo "anda" mais devagar e ganha velocidade; em subidas trava depressa
            m_SlideTime += dt * (slope > 0.08f ? 0.45f : slope < -0.08f ? 1.8f : 1f);
            m_SlideSlopeBonus += slope * slideSlopeAccel * dt;
            if (slope <= 0.08f) m_SlideSlopeBonus = Mathf.MoveTowards(m_SlideSlopeBonus, 0f, 6f * dt);
            m_SlideSlopeBonus = Mathf.Max(m_SlideSlopeBonus, -m_SlideStartSpeed);

            // Curva do CoD: segura quase toda a velocidade no início e larga-a no fim (ease-in)
            float t = Mathf.Clamp01(m_SlideTime / Mathf.Max(0.05f, slideDuration));
            float baseSpeed = Mathf.Lerp(m_SlideStartSpeed, m_SlideEndSpeed, t * t * t);
            float cap = m_Player.Brawler.movement.sprintSpeed * m_Player.SprintMultiplier * m_Player.SpeedMultiplier * slideMaxSpeedMultiplier;
            m_SlideSpeed = Mathf.Clamp(baseSpeed + m_SlideSlopeBonus, 0f, cap);
            m_HorizontalVelocity = m_SlideDir * m_SlideSpeed;
        }

        /// <summary>
        /// No ar acima da velocidade normal: conserva o embalo (perde devagar e deixa virar um pouco).
        /// Falso = usar a aceleração normal (inclui travar a carregar para trás).
        /// </summary>
        bool AirMomentum(Vector3 wish, float speed, float dt)
        {
            float cur = m_HorizontalVelocity.magnitude;
            if (cur <= speed + 0.1f) return false;
            var dir = m_HorizontalVelocity / cur;
            if (wish.sqrMagnitude > 0.01f)
            {
                var w = wish.normalized;
                if (Vector3.Dot(w, dir) < -0.3f) return false;
                dir = Vector3.RotateTowards(dir, w, airMomentumSteer * dt, 0f);
            }
            m_HorizontalVelocity = dir * Mathf.MoveTowards(cur, speed, airMomentumDrag * dt);
            return true;
        }

        bool CanJumpFromCrouch() => !m_Crouched || m_Sliding || HasHeadroom();

        void OnJumpFromCrouch()
        {
            if (m_Sliding)
            {
                // Slide-jump: o embalo do slide passa para o salto
                m_HorizontalVelocity = m_SlideDir * m_SlideSpeed;
                StopSlide();
            }
            SetCrouched(false, true);
        }

        void StopSlide()
        {
            if (!m_Sliding) return;
            m_Sliding = false;
            m_SlideCooldownTimer = slideCooldown;
        }

        void SetCrouched(bool crouched, bool force = false)
        {
            if (m_Crouched == crouched && !force) return;
            m_Crouched = crouched;
            ApplyCapsule(crouched);
        }

        void ApplyCapsule(bool crouched)
        {
            float h = crouched ? crouchHeight : m_StandHeight;
            if (Mathf.Approximately(m_Controller.height, h)) return;
            m_Controller.height = h;
            m_Controller.center = new Vector3(m_StandCenter.x, m_StandCenter.y - (m_StandHeight - h) * 0.5f, m_StandCenter.z);
        }

        bool HasHeadroom()
        {
            float r = m_Controller.radius * 0.9f;
            var start = transform.position + Vector3.up * (crouchHeight - r);
            float dist = m_StandHeight - crouchHeight + 0.05f;
            var hits = Physics.SphereCastAll(start, r, Vector3.up, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (!h.collider.transform.IsChildOf(transform)) return false;
            return true;
        }

        bool TryGroundNormal(out Vector3 normal)
        {
            normal = Vector3.up;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform))
            {
                normal = hit.normal;
                return true;
            }
            return false;
        }

        byte CrouchFlags()
        {
            byte f = 0;
            if (m_Crouched) f |= 4;
            if (m_Sliding) f |= 8;
            return f;
        }

        void ResetCrouch()
        {
            m_Sliding = false;
            m_SlideCooldownTimer = 0f;
            m_SlideRoll = 0f;
            m_SlideKick = 0f;
            m_SlideBlend = 0f;
            m_CrouchBlend = 0f;
            m_RemoteSliding = false;
            if (m_Controller != null) SetCrouched(false, true);
            ApplyCrouchPose();
        }

        // ------------------------------------------------------------------ outros (e host)

        void ApplyRemoteCrouch(bool crouched, bool sliding, float dt)
        {
            m_RemoteSliding = sliding;
            if (crouched != m_Crouched) { m_Crouched = crouched; ApplyCapsule(crouched); }
            m_CrouchBlend = Mathf.MoveTowards(m_CrouchBlend, crouched ? 1f : 0f, crouchBlendSpeed * dt);
            m_SlideBlend = Mathf.MoveTowards(m_SlideBlend, sliding ? 1f : 0f, (sliding ? slideDropSpeed : 5f) * dt);
            ApplyCrouchPose();
        }

        /// <summary>Olhos e pivô da arma descem com o agachar (dono: câmara; host: origem dos tiros validados).</summary>
        void ApplyCrouchPose()
        {
            // Slide: olhos ainda mais baixos do que agachado
            float k = Mathf.Lerp(CrouchScale, Mathf.Min(CrouchScale, slideEyeScale), m_SlideBlend);
            if (cameraRoot != null)
            {
                var p = cameraRoot.localPosition;
                p.y = m_StandEye * k;
                cameraRoot.localPosition = p;
            }
            if (aimPivot != null)
            {
                var p = aimPivot.localPosition;
                p.y = m_StandAim * k;
                aimPivot.localPosition = p;
            }
        }
    }
}
