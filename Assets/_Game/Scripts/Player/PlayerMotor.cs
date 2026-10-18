using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Estado de movimento enviado pelo dono do Player para os outros (LAN: autoridade do dono = zero input lag).</summary>
    public struct MotorState : INetworkSerializeByMemcpy
    {
        public Vector3 Position;
        public float Yaw;
        public float Pitch;
        public byte Flags; // bit0: no chão, bit1: a mover

        public bool Grounded => (Flags & 1) != 0;
        public bool Moving => (Flags & 2) != 0;
    }

    /// <summary>
    /// Movimento FPS (WASD, sprint, salto, gravidade) e mouse look. O dono simula (humano ou bot no host); os outros interpolam.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : NetworkBehaviour
    {
        [Header("Referências (preenchidas pelo setup)")]
        public Transform cameraRoot;
        public Transform aimPivot;

        [Header("Afinação")]
        public float gravity = 24f;
        public float groundAccel = 70f;
        public float airAccel = 14f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;

        public readonly NetworkVariable<MotorState> State =
            new NetworkVariable<MotorState>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        CharacterController m_Controller;
        PlayerNetwork m_Player;

        Vector3 m_HorizontalVelocity;
        float m_VerticalVelocity;
        float m_Yaw, m_Pitch;
        float m_CoyoteTimer, m_JumpBuffer;
        float m_RecoilPitch;
        Vector3 m_ExternalVelocity; // knockback (decai)
        Vector3 m_LaunchVelocity;   // salto/plataforma: mantém-se até aterrar
        bool m_Launched;
        float m_LaunchTime;
        float m_PadCooldown;
        bool m_Sprinting;

        public float Yaw => IsOwner ? m_Yaw : State.Value.Yaw;
        public float Pitch => IsOwner ? m_Pitch : State.Value.Pitch;
        public float EyeHeight => cameraRoot != null ? cameraRoot.localPosition.y : 1.6f;
        public bool IsGrounded => IsOwner ? (m_Controller.enabled && m_Controller.isGrounded) : State.Value.Grounded;
        public bool IsMoving => IsOwner ? m_HorizontalVelocity.sqrMagnitude > 0.5f : State.Value.Moving;
        public Vector3 Velocity => HorizontalVelocity + Vector3.up * m_VerticalVelocity;
        public Vector3 HorizontalVelocity => m_HorizontalVelocity + (m_Launched ? m_LaunchVelocity : Vector3.zero);
        public bool IsSprinting => IsOwner && m_Sprinting;
        public bool IsLaunched => m_Launched;

        /// <summary>Posição dos olhos vista por esta máquina (dono: exata; outros: último estado recebido).</summary>
        public Vector3 EyePosition => IsOwner ? cameraRoot.position : State.Value.Position + Vector3.up * EyeHeight;

        public Vector3 AimDirection => Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Player = GetComponent<PlayerNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                m_Yaw = transform.eulerAngles.y;
                PushState();
            }
            else
            {
                transform.position = State.Value.Position;
            }
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner) OwnerUpdate();
            else RemoteUpdate();
        }

        void OwnerUpdate()
        {
            var move = m_Player.Brawler.movement;
            bool alive = !m_Player.Health.IsDead;
            float dt = Time.deltaTime;
            var cmd = alive ? m_Player.ReadCommands() : default;

            // --- Look ---
            if (cmd.absoluteAim)
            {
                m_Yaw = cmd.yaw;
                m_Pitch = Mathf.Clamp(cmd.pitch, -88f, 88f);
            }
            else
            {
                m_Yaw += cmd.lookDelta.x;
                m_Pitch = Mathf.Clamp(m_Pitch - cmd.lookDelta.y, -88f, 88f);
            }

            m_RecoilPitch = Mathf.MoveTowards(m_RecoilPitch, 0f, 18f * dt);
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            cameraRoot.localRotation = Quaternion.Euler(m_Pitch - m_RecoilPitch, 0f, 0f);
            if (aimPivot != null) aimPivot.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);

            if (!m_Controller.enabled)
            {
                PushState();
                return;
            }

            // --- Move ---
            Vector3 wish = transform.right * cmd.move.x + transform.forward * cmd.move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float sprintAmount = cmd.sprint && cmd.move.y > 0.1f ? 1f : 0f;
            m_Sprinting = sprintAmount > 0.001f && alive;
            float speed = Mathf.Lerp(move.walkSpeed, move.sprintSpeed, sprintAmount) *
                m_Player.Health.SpeedMultiplier;

            bool grounded = m_Controller.isGrounded;
            m_CoyoteTimer = grounded ? coyoteTime : m_CoyoteTimer - dt;

            // Plataformas de salto
            m_PadCooldown -= dt;
            if (grounded && alive && m_PadCooldown <= 0f && !m_Launched)
            {
                var pad = JumpPad.Find(transform.position);
                if (pad != null)
                {
                    Launch(pad.target, pad.flightTime);
                    m_PadCooldown = 0.6f;
                }
            }

            // Lançado (salto/plataforma): segue o arco até aterrar, com pouco controlo no ar
            if (m_Launched && (Time.time - m_LaunchTime > 0.15f) && grounded)
            {
                m_HorizontalVelocity = m_LaunchVelocity;
                m_LaunchVelocity = Vector3.zero;
                m_Launched = false;
            }

            float accel = grounded ? groundAccel : airAccel;
            if (m_Launched)
                m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, wish * speed * 0.25f, airAccel * dt);
            else
                m_HorizontalVelocity = Vector3.MoveTowards(m_HorizontalVelocity, wish * speed, accel * dt);

            // --- Jump ---
            if (cmd.jumpPressed) m_JumpBuffer = jumpBufferTime;
            else m_JumpBuffer -= dt;

            if (m_JumpBuffer > 0f && m_CoyoteTimer > 0f && !m_Launched)
            {
                m_VerticalVelocity = Mathf.Sqrt(2f * gravity * move.jumpHeight);
                m_JumpBuffer = 0f;
                m_CoyoteTimer = 0f;
            }

            // --- Gravity ---
            if (grounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            m_VerticalVelocity -= gravity * dt;

            m_ExternalVelocity = Vector3.MoveTowards(m_ExternalVelocity, Vector3.zero, (grounded ? 30f : 8f) * dt);
            var launch = m_Launched ? m_LaunchVelocity : Vector3.zero;
            var flags = m_Controller.Move((m_HorizontalVelocity + launch + m_ExternalVelocity + Vector3.up * m_VerticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && m_VerticalVelocity > 0f) m_VerticalVelocity = 0f;
            // Bateu numa parede durante o salto: perde o impulso horizontal
            if (m_Launched && (flags & CollisionFlags.Sides) != 0) m_LaunchVelocity *= 0.5f;

            if (transform.position.y < MapInfo.FallRecoveryY) // -20 m por defeito; mapas com buracos definem mais alto
            {
                SpawnPoint.Pick(m_Player, out var safe, out var safeYaw);
                TeleportLocal(safe, safeYaw);
            }

            PushState();
        }

        void PushState()
        {
            byte f = 0;
            if (m_Controller.enabled && m_Controller.isGrounded) f |= 1;
            if (m_HorizontalVelocity.sqrMagnitude > 0.5f) f |= 2;

            State.Value = new MotorState
            {
                Position = transform.position,
                Yaw = m_Yaw,
                Pitch = m_Pitch,
                Flags = f
            };
        }

        void RemoteUpdate()
        {
            var s = State.Value;
            float t = 1f - Mathf.Exp(-20f * Time.deltaTime);

            if ((transform.position - s.Position).sqrMagnitude > 16f)
                transform.position = s.Position;
            else
                transform.position = Vector3.Lerp(transform.position, s.Position, t);

            float yaw = Mathf.LerpAngle(transform.eulerAngles.y, s.Yaw, t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (aimPivot != null)
            {
                float current = aimPivot.localEulerAngles.x;
                if (current > 180f) current -= 360f;
                aimPivot.localRotation = Quaternion.Euler(Mathf.Lerp(current, s.Pitch, t), 0f, 0f);
            }
        }

        /// <summary>Dono: impulso enviado pelo host (knockback), já limitado pelo StatusEffects.</summary>
        public void ApplyImpulse(Vector3 impulse)
        {
            var h = impulse;
            h.y = 0f;
            m_ExternalVelocity += h;
            if (impulse.y > 0f) m_VerticalVelocity = Mathf.Max(m_VerticalVelocity, impulse.y);
        }

        /// <summary>
        /// Dono: lança o jogador em arco até 'target' em 'flightTime' segundos (plataformas de salto, Super de salto).
        /// Usa a mesma gravidade do movimento, por isso cai exatamente no destino se nada o travar.
        /// </summary>
        public void Launch(Vector3 target, float flightTime)
        {
            if (!IsOwner) return;
            float T = Mathf.Max(0.2f, flightTime);
            var delta = target - transform.position;
            m_LaunchVelocity = new Vector3(delta.x / T, 0f, delta.z / T);
            m_VerticalVelocity = (delta.y + 0.5f * gravity * T * T) / T;
            m_HorizontalVelocity = Vector3.zero;
            m_ExternalVelocity = Vector3.zero;
            m_Launched = true;
            m_LaunchTime = Time.time;
            m_JumpBuffer = 0f;
            m_CoyoteTimer = 0f;
            if (m_Player.IsLocalHuman) Sfx.Play2D(Sound.Launch, 0.8f);
        }

        public void AddRecoil(float degrees)
        {
            m_RecoilPitch = Mathf.Min(m_RecoilPitch + degrees, 6f);
        }

        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 position, float yaw)
        {
            TeleportLocal(position, yaw);
        }

        void TeleportLocal(Vector3 position, float yaw)
        {
            bool wasEnabled = m_Controller.enabled;
            m_Controller.enabled = false;
            transform.position = position;
            m_Controller.enabled = wasEnabled;

            m_Yaw = yaw;
            m_Pitch = 0f;
            m_HorizontalVelocity = Vector3.zero;
            m_ExternalVelocity = Vector3.zero;
            m_LaunchVelocity = Vector3.zero;
            m_Launched = false;
            m_VerticalVelocity = 0f;
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            if (m_Player.Bot != null) m_Player.Bot.OnTeleported(yaw);
            PushState();
        }
    }
}
