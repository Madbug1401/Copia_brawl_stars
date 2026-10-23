using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Estado de movimento enviado pelo dono do Player para os outros (autoridade do dono = zero input lag).</summary>
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
    /// Movimento FPS (WASD, sprint, salto, gravidade) e mouse look. O dono simula (humano ou bot no host).
    ///
    /// Rede (pensada para a Internet, não só LAN):
    /// - O dono envia "snapshots" 30×/s por mensagens NÃO fiáveis: um pacote perdido é simplesmente substituído
    ///   pelo seguinte, em vez de travar os outros à espera de reenvio (o que acontecia com a NetworkVariable fiável).
    /// - Cada snapshot leva a hora em que foi tirado (relógio do servidor). Quem recebe guarda-os num buffer e desenha
    ///   o jogador <see cref="InterpolationDelay"/> atrás, interpolando entre dois snapshots reais: movimento suave
    ///   mesmo com pacotes irregulares e um atraso conhecido (base para a compensação de latência no host).
    /// - O host guarda o histórico de todos (<see cref="TryGetPositionAt"/>) para "recuar" alvos ao validar tiros.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : NetworkBehaviour
    {
        /// <summary>Snapshots de movimento por segundo enviados pelo dono.</summary>
        public const float SendRate = 30f;
        /// <summary>Atraso inicial com que os outros jogadores são desenhados (≈3 snapshots: tolera 1–2 pacotes perdidos).</summary>
        public const double InterpolationDelay = 0.1;
        /// <summary>Limites do atraso adaptativo: cresce com a irregularidade da ligação (jitter), nunca acima disto.</summary>
        const double MinInterpolationDelay = 0.05, MaxInterpolationDelay = 0.2;
        /// <summary>Histórico guardado por jogador (chega para recuar tiros até ~1 s).</summary>
        const double HistoryLength = 1.0;
        /// <summary>Sem snapshots novos: continua o movimento no máximo isto, depois para.</summary>
        const double MaxExtrapolation = 0.1;

        [Header("Referências (preenchidas pelo setup)")]
        public Transform cameraRoot;
        public Transform aimPivot;

        [Header("Afinação")]
        public float gravity = 24f;
        public float groundAccel = 70f;
        public float airAccel = 14f;
        public float coyoteTime = 0.1f;
        public float jumpBufferTime = 0.12f;

        struct Snapshot
        {
            public double Time;      // hora do snapshot (relógio do servidor)
            public double Delay;     // hora local de chegada − Time (inclui a diferença de relógios)
            public MotorState State;
        }

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

        // Rede
        MotorState m_Latest;        // último estado (dono: o atual; outros: o mais recente recebido)
        MotorState m_Rendered;      // outros: o estado interpolado que está a ser desenhado
        bool m_HasLatest;
        double m_NextSendTime;
        readonly List<Snapshot> m_Snapshots = new List<Snapshot>(48);
        double m_ClockOffset;       // outros: menor atraso de chegada no último segundo (relógio local − hora do snapshot)
        double m_InterpDelay = InterpolationDelay;   // outros: atraso de desenho atual (adapta-se ao jitter)
        static readonly double[] s_DelayScratch = new double[64];

        /// <summary>Atraso (s) com que este jogador está a ser desenhado nesta máquina.</summary>
        public double CurrentInterpolationDelay => m_InterpDelay;

        public float Yaw => IsOwner ? m_Yaw : m_Rendered.Yaw;
        public float Pitch => IsOwner ? m_Pitch : m_Rendered.Pitch;
        public float EyeHeight => cameraRoot != null ? cameraRoot.localPosition.y : 1.6f;
        public bool IsGrounded => IsOwner ? (m_Controller.enabled && m_Controller.isGrounded) : m_Rendered.Grounded;
        public bool IsMoving => IsOwner ? m_HorizontalVelocity.sqrMagnitude > 0.5f : m_Rendered.Moving;
        public Vector3 Velocity => HorizontalVelocity + Vector3.up * m_VerticalVelocity;
        public Vector3 HorizontalVelocity => m_HorizontalVelocity + (m_Launched ? m_LaunchVelocity : Vector3.zero);
        public bool IsSprinting => IsOwner && m_Sprinting;
        public bool IsLaunched => m_Launched;

        /// <summary>
        /// Posição dos olhos vista por esta máquina. Dono: exata. Host (validação): último estado recebido, o mais
        /// próximo da posição real. Outros clientes: onde o jogador está a ser desenhado.
        /// </summary>
        public Vector3 EyePosition
        {
            get
            {
                if (IsOwner) return cameraRoot.position;
                var basePos = IsServer && m_HasLatest ? m_Latest.Position : transform.position;
                return basePos + Vector3.up * EyeHeight;
            }
        }

        public Vector3 AimDirection => Quaternion.Euler(Pitch, Yaw, 0f) * Vector3.forward;

        /// <summary>Hora (na escala dos snapshots ≈ relógio do servidor) a que este jogador está a ser desenhado aqui.</summary>
        public double RenderTime => Time.unscaledTimeAsDouble - m_ClockOffset - m_InterpDelay;

        void Awake()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Player = GetComponent<PlayerNetwork>();
        }

        public override void OnNetworkSpawn()
        {
            m_Snapshots.Clear();
            m_InterpDelay = InterpolationDelay;
            m_Rendered = new MotorState { Position = transform.position, Yaw = transform.eulerAngles.y };
            if (IsOwner)
            {
                m_Yaw = transform.eulerAngles.y;
                PushState(true);
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
            // Imobilizado (armadilha): pode olhar e atacar, mas não anda nem salta
            bool rooted = m_Player.Health.IsRooted;
            Vector3 wish = rooted ? Vector3.zero : transform.right * cmd.move.x + transform.forward * cmd.move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float sprintAmount = cmd.sprint && cmd.move.y > 0.1f && !rooted ? 1f : 0f;
            m_Sprinting = sprintAmount > 0.001f && alive;
            // Velocidade efetiva = base do Brawler × melhorias/pet (calculados no host) × estados
            float speed = Mathf.Lerp(move.walkSpeed, move.sprintSpeed * m_Player.SprintMultiplier, sprintAmount) *
                m_Player.SpeedMultiplier * m_Player.Health.SpeedMultiplier;

            bool grounded = m_Controller.isGrounded;
            m_CoyoteTimer = grounded ? coyoteTime : m_CoyoteTimer - dt;

            // Plataformas de salto
            m_PadCooldown -= dt;
            if (grounded && alive && !rooted && m_PadCooldown <= 0f && !m_Launched)
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

            if (rooted)
            {
                m_HorizontalVelocity = Vector3.zero;
                m_ExternalVelocity = Vector3.zero;
                m_LaunchVelocity = Vector3.zero;
            }
            if (m_JumpBuffer > 0f && m_CoyoteTimer > 0f && !m_Launched && !rooted)
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

        // ================================================================== rede: envio (dono)

        /// <summary>Dono: atualiza o estado atual e envia um snapshot se for altura (30×/s) ou se forçado (teleporte).</summary>
        void PushState(bool force = false)
        {
            byte f = 0;
            if (m_Controller.enabled && m_Controller.isGrounded) f |= 1;
            if (m_HorizontalVelocity.sqrMagnitude > 0.5f) f |= 2;

            m_Latest = new MotorState
            {
                Position = transform.position,
                Yaw = m_Yaw,
                Pitch = m_Pitch,
                Flags = f
            };
            m_HasLatest = true;

            var nm = NetworkManager;
            if (!IsSpawned || nm == null) return;
            double now = Time.unscaledTimeAsDouble;
            if (!force && now < m_NextSendTime) return;
            // Mantém a cadência mesmo com frames irregulares, sem rajadas de recuperação
            m_NextSendTime = now >= m_NextSendTime + 1.0 / SendRate ? now + 1.0 / SendRate : m_NextSendTime + 1.0 / SendRate;

            // Host: hora do servidor. Cliente: o seu relógio local (estável); o host converte-o para a sua escala.
            double stamp = IsServer ? nm.ServerTime.Time : now;
            if (IsServer)
            {
                ServerRecord(stamp, m_Latest);
                if (nm.ConnectedClientsIds.Count > 1) RelayStateRpc(m_Latest, stamp, RpcTarget.NotServer);
            }
            else SubmitStateRpc(m_Latest, stamp);
        }

        /// <summary>Cliente dono → host. Não fiável: um snapshot perdido é substituído pelo seguinte.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void SubmitStateRpc(MotorState state, double stamp, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (!IsFinite(state.Position) || float.IsNaN(state.Yaw) || float.IsNaN(state.Pitch)) return;
            if (double.IsNaN(stamp) || double.IsInfinity(stamp)) return;
            stamp = ServerConvertClientStamp(stamp);
            ReceiveSnapshot(stamp, state);
            ServerRecord(stamp, state);
            // Reenvia aos outros clientes (não ao dono nem ao host, que já o tem)
            if (NetworkManager.ConnectedClientsIds.Count > 2) RelayStateRpc(state, stamp, RpcTarget.Not(OwnerClientId, RpcTargetUse.Temp));
        }

        // Host: conversão do relógio do cliente para a hora do servidor. Usa o pacote mais rápido do último segundo,
        // por isso mantém o espaçamento real entre snapshots (sem o jitter da viagem) e não confia no relógio do cliente.
        readonly Queue<(double arrival, double delta)> m_ClientClock = new Queue<(double, double)>(40);

        double ServerConvertClientStamp(double clientStamp)
        {
            double serverNow = NetworkManager.ServerTime.Time;
            m_ClientClock.Enqueue((serverNow, serverNow - clientStamp));
            while (m_ClientClock.Count > 1 && m_ClientClock.Peek().arrival < serverNow - 1.0) m_ClientClock.Dequeue();
            double min = double.MaxValue;
            foreach (var c in m_ClientClock) if (c.delta < min) min = c.delta;
            // Nunca no futuro do host
            return System.Math.Min(clientStamp + min, serverNow);
        }

        /// <summary>Host → clientes. Não fiável.</summary>
        [Rpc(SendTo.SpecifiedInParams, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void RelayStateRpc(MotorState state, double stamp, RpcParams rpcParams)
        {
            if (IsOwner || IsServer) return; // o host já guardou ao receber; o dono simula
            ReceiveSnapshot(stamp, state);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        // ================================================================== rede: receção e interpolação (outros)

        void ReceiveSnapshot(double stamp, MotorState state)
        {
            // Guarda por ordem de hora (os pacotes não fiáveis podem chegar trocados) e ignora duplicados
            int i = m_Snapshots.Count;
            while (i > 0 && m_Snapshots[i - 1].Time > stamp) i--;
            if (i > 0 && System.Math.Abs(m_Snapshots[i - 1].Time - stamp) < 1e-6) return;
            m_Snapshots.Insert(i, new Snapshot { Time = stamp, Delay = Time.unscaledTimeAsDouble - stamp, State = state });

            if (i == m_Snapshots.Count - 1)
            {
                m_Latest = state;
                m_HasLatest = true;
            }

            // Descarta o que já não serve para desenhar nem para o histórico do host
            double oldest = m_Snapshots[m_Snapshots.Count - 1].Time - HistoryLength;
            int remove = 0;
            while (remove < m_Snapshots.Count - 2 && m_Snapshots[remove + 1].Time < oldest) remove++;
            if (remove > 0) m_Snapshots.RemoveRange(0, remove);

            // Relógio: o pacote mais rápido do último segundo dá a diferença entre o relógio local (estável, nunca
            // salta) e a hora dos snapshots. Quem chega mais tarde do que isso cabe nos 100 ms do buffer.
            // Funciona mesmo que os relógios dos PCs não coincidam e adapta-se em ~1 s se a latência mudar.
            int count = Mathf.Min(m_Snapshots.Count, s_DelayScratch.Length);
            for (int k = 0; k < count; k++) s_DelayScratch[k] = m_Snapshots[m_Snapshots.Count - count + k].Delay;
            System.Array.Sort(s_DelayScratch, 0, count);
            m_ClockOffset = s_DelayScratch[0];

            // Atraso adaptativo: o jitter (percentil 90 dos atrasos de chegada) + meio intervalo entre snapshots.
            // Ligação estável → ~50–70 ms; Wi-Fi/4G irregular → até 200 ms, para não ficar a "adivinhar" posições.
            if (count >= 8)
            {
                double jitter = s_DelayScratch[(int)((count - 1) * 0.9)] - s_DelayScratch[0];
                double target = System.Math.Max(MinInterpolationDelay, System.Math.Min(MaxInterpolationDelay, jitter + 0.5 / SendRate));
                // Sobe depressa (evita travões), desce devagar (evita oscilar)
                m_InterpDelay += (target - m_InterpDelay) * (target > m_InterpDelay ? 0.25 : 0.02);
            }
        }

        void RemoteUpdate()
        {
            if (m_Snapshots.Count == 0) return;
            var s = Sample(RenderTime);
            m_Rendered = s;

            transform.position = s.Position;
            transform.rotation = Quaternion.Euler(0f, s.Yaw, 0f);
            if (aimPivot != null) aimPivot.localRotation = Quaternion.Euler(s.Pitch, 0f, 0f);
        }

        /// <summary>Estado interpolado na hora 't' (na escala dos snapshots). Extrapola pouco e nunca atravessa teleportes.</summary>
        MotorState Sample(double t)
        {
            int n = m_Snapshots.Count;
            var first = m_Snapshots[0];
            if (t <= first.Time || n == 1) return first.State;

            for (int i = 1; i < n; i++)
            {
                var b = m_Snapshots[i];
                if (t > b.Time) continue;
                var a = m_Snapshots[i - 1];
                return Blend(a, b, (float)((t - a.Time) / System.Math.Max(1e-4, b.Time - a.Time)));
            }

            // Mais recente do que o último snapshot (pacotes atrasados/perdidos): continua um pouco e para
            var last = m_Snapshots[n - 1];
            var prev = m_Snapshots[n - 2];
            double ahead = System.Math.Min(t - last.Time, MaxExtrapolation);
            double span = System.Math.Max(1e-4, last.Time - prev.Time);
            if ((last.State.Position - prev.State.Position).sqrMagnitude > 16f) return last.State;
            var result = last.State;
            result.Position += (last.State.Position - prev.State.Position) * (float)(ahead / span);
            return result;
        }

        static MotorState Blend(Snapshot a, Snapshot b, float k)
        {
            // Mais de 4 m entre snapshots seguidos = teleporte/respawn: salta, não desliza pelo mapa
            if ((b.State.Position - a.State.Position).sqrMagnitude > 16f) return k < 0.5f ? a.State : b.State;
            return new MotorState
            {
                Position = Vector3.Lerp(a.State.Position, b.State.Position, k),
                Yaw = Mathf.LerpAngle(a.State.Yaw, b.State.Yaw, k),
                Pitch = Mathf.Lerp(a.State.Pitch, b.State.Pitch, k),
                Flags = k < 0.5f ? a.State.Flags : b.State.Flags,
            };
        }

        // ================================================================== histórico no host (compensação de latência)

        readonly List<Snapshot> m_ServerHistory = new List<Snapshot>(64);

        /// <summary>Host: regista onde este jogador estava, na escala de hora do servidor.</summary>
        void ServerRecord(double stamp, MotorState state)
        {
            if (!IsServer) return;
            int i = m_ServerHistory.Count;
            while (i > 0 && m_ServerHistory[i - 1].Time > stamp) i--;
            m_ServerHistory.Insert(i, new Snapshot { Time = stamp, State = state });
            double oldest = m_ServerHistory[m_ServerHistory.Count - 1].Time - HistoryLength;
            int remove = 0;
            while (remove < m_ServerHistory.Count - 2 && m_ServerHistory[remove + 1].Time < oldest) remove++;
            if (remove > 0) m_ServerHistory.RemoveRange(0, remove);
        }

        /// <summary>Host: posição (pés) deste jogador na hora 'time' do servidor. Falso sem histórico.</summary>
        public bool TryGetPositionAt(double time, out Vector3 position)
        {
            position = transform.position;
            int n = m_ServerHistory.Count;
            if (!IsServer || n == 0) return false;
            if (time <= m_ServerHistory[0].Time) { position = m_ServerHistory[0].State.Position; return true; }
            for (int i = 1; i < n; i++)
            {
                var b = m_ServerHistory[i];
                if (time > b.Time) continue;
                var a = m_ServerHistory[i - 1];
                float k = (float)((time - a.Time) / System.Math.Max(1e-4, b.Time - a.Time));
                position = Blend(a, b, k).Position;
                return true;
            }
            position = m_ServerHistory[n - 1].State.Position;
            return true;
        }

        // ================================================================== ações

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
            PushState(true);
        }
    }
}
