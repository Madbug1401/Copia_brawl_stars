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
        public float crouchBlendSpeed = 12f;
        public float slideBoost = 1.3f;          // × velocidade à entrada
        public float slideMaxSpeedMultiplier = 1.7f; // limite em relação à velocidade de sprint
        public float slideFriction = 7f;         // m/s² de travagem em plano
        public float slideSlopeAccel = 14f;      // m/s² extra em descidas (× inclinação)
        public float slideSteer = 1.6f;          // rad/s de controlo da direção
        public float slideMaxDuration = 1.1f;
        public float slideCooldown = 0.45f;
        public float airMomentumDrag = 2.5f;     // m/s² de perda de embalo no ar acima da velocidade normal
        public float airMomentumSteer = 2.2f;    // rad/s

        float m_StandHeight = 1.8f;
        Vector3 m_StandCenter = new Vector3(0f, 0.9f, 0f);
        float m_StandEye = 1.6f, m_StandAim = 1.32f;
        bool m_CrouchCached;

        bool m_Crouched;          // dono: estado lógico (inclui slide)
        bool m_Sliding;
        float m_SlideTime, m_SlideCooldownTimer;
        Vector3 m_SlideDir;
        float m_SlideSpeed;
        bool m_WasGrounded;
        float m_CrouchBlend;      // 0 de pé → 1 agachado (suavizado, usado pela câmara e pelo boneco)
        float m_SlideRoll;        // inclinação lateral da câmara no slide
        bool m_RemoteSliding;

        public bool IsCrouching => IsOwner ? m_Crouched : m_CrouchBlend > 0.5f;
        public bool IsSliding => IsOwner ? m_Sliding : m_RemoteSliding;
        /// <summary>0 de pé → 1 totalmente agachado (suave).</summary>
        public float CrouchBlend => m_CrouchBlend;
        /// <summary>Escala vertical do corpo agora (1 de pé, ≈0,67 agachado).</summary>
        public float CrouchScale => Mathf.Lerp(1f, crouchHeight / Mathf.Max(0.1f, m_StandHeight), m_CrouchBlend);
        /// <summary>Quanto a cabeça desceu (m) em relação a estar de pé. Usado nos headshots.</summary>
        public float CrouchDrop => CombatMath.HeadCenterHeight * (1f - CrouchScale);

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

            // --- iniciar slide ---
            bool fastEnough = flatSpeed > walkSpeed + 0.5f;
            bool trigger = cmd.crouchPressed || (justLanded && cmd.crouch);
            if (!m_Sliding && trigger && grounded && alive && !rooted && !m_Launched && fastEnough &&
                (cmd.sprint || flatSpeed > walkSpeed * 1.25f) && m_SlideCooldownTimer <= 0f)
            {
                m_Sliding = true;
                m_SlideTime = 0f;
                m_SlideDir = flatSpeed > 0.1f ? m_HorizontalVelocity / flatSpeed : transform.forward;
                m_SlideSpeed = Mathf.Min(Mathf.Max(flatSpeed, sprintSpeed) * slideBoost, sprintSpeed * slideMaxSpeedMultiplier);
                m_SlideRoll = 0f;
                if (m_Player.IsLocalHuman) Sfx.Play2D(Sound.Twirl, 0.55f);
            }

            // --- terminar slide ---
            if (m_Sliding)
            {
                m_SlideTime += dt;
                bool end = !alive || rooted || m_Launched || !cmd.crouch ||
                           m_SlideSpeed < walkSpeed * 0.75f ||
                           (m_SlideTime > slideMaxDuration && !OnDownSlope());
                if (!grounded)
                {
                    // Saiu de uma rampa/beira: mantém o embalo no ar e acaba o slide
                    m_HorizontalVelocity = m_SlideDir * m_SlideSpeed;
                    end = true;
                }
                if (end) StopSlide();
            }

            // --- agachar / levantar ---
            if (m_Sliding || wantCrouch) SetCrouched(true);
            else if (m_Crouched && HasHeadroom()) SetCrouched(false);
            if (!alive) { m_Sliding = false; SetCrouched(false, true); }

            if (m_Crouched && !m_Sliding)
            {
                speed = Mathf.Min(speed, walkSpeed * crouchSpeedMultiplier);
                m_Sprinting = false;
            }
            else if (m_Sliding) m_Sprinting = true; // FOV de velocidade

            // --- câmara ---
            m_CrouchBlend = Mathf.MoveTowards(m_CrouchBlend, m_Crouched ? 1f : 0f, crouchBlendSpeed * dt);
            float roll = 0f;
            if (m_Sliding) roll = -Mathf.Clamp(Vector3.Dot(m_SlideDir, transform.right) * 6f, -6f, 6f) - 2f;
            m_SlideRoll = Mathf.Lerp(m_SlideRoll, roll, 1f - Mathf.Exp(-10f * dt));
            ApplyCrouchPose();
        }

        void SlideVelocity(Vector3 wish, float dt)
        {
            // Pouco controlo: roda devagar para onde se carrega
            if (wish.sqrMagnitude > 0.01f)
                m_SlideDir = Vector3.RotateTowards(m_SlideDir, wish.normalized, slideSteer * dt, 0f);

            float accel = -slideFriction;
            if (TryGroundNormal(out var n))
            {
                var downhill = Vector3.ProjectOnPlane(Vector3.down, n);
                downhill.y = 0f;
                // Componente da descida na direção do slide (subidas travam mais)
                accel += Vector3.Dot(downhill, m_SlideDir) * slideSlopeAccel;
            }
            float cap = m_Player.Brawler.movement.sprintSpeed * m_Player.SprintMultiplier * m_Player.SpeedMultiplier * slideMaxSpeedMultiplier;
            m_SlideSpeed = Mathf.Clamp(m_SlideSpeed + accel * dt, 0f, cap);
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

        bool OnDownSlope()
        {
            if (!TryGroundNormal(out var n)) return false;
            var downhill = Vector3.ProjectOnPlane(Vector3.down, n);
            downhill.y = 0f;
            return Vector3.Dot(downhill, m_SlideDir) > 0.08f;
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
            ApplyCrouchPose();
        }

        /// <summary>Olhos e pivô da arma descem com o agachar (dono: câmara; host: origem dos tiros validados).</summary>
        void ApplyCrouchPose()
        {
            float k = CrouchScale;
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
