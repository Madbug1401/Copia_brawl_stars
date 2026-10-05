using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Apresentação do jogador (só visual):
    /// - terceira pessoa: personagem cartoon do Brawler (CharacterRig) para os outros, e só sombra para o próprio;
    /// - primeira pessoa: câmara + mãos e arma do estilo (ViewModelRig), numa câmara própria para não atravessar paredes;
    /// - eventos: ataque/Super, disparos por cano, recarga, dano, morte/reaparecer, escudo de spawn;
    /// - arbustos (esconder quem está longe), passos, abanão de câmara, trajetória dos ataques em arco.
    /// </summary>
    public class PlayerVisuals : NetworkBehaviour
    {
        public const string ViewModelLayerName = "ViewModel";

        [Header("Referências (preenchidas pelo setup)")]
        public Renderer[] bodyRenderers;
        public Renderer[] tintRenderers;
        public Transform thirdPersonMuzzle;

        PlayerNetwork m_Player;
        CharacterController m_Controller;

        Camera m_Camera, m_ViewCamera;
        CharacterRig m_Rig;
        ViewModelRig m_View;
        int m_BuiltHero = int.MinValue;
        int m_ViewLayer = -1;

        LineRenderer m_Trajectory;
        Transform m_TargetMarker;
        Renderer m_TargetMarkerR;
        Transform m_Bubble;
        Renderer m_BubbleR;

        float m_StepDistance;
        Vector3 m_LastPos;
        Vector3 m_SmoothedVelocity;
        float m_LastYaw, m_LastPitch;
        Quaternion m_CamRot = Quaternion.identity;

        float m_RevealedUntil;
        bool m_Dead, m_PendingRespawn, m_PoofDone;
        float m_DeadAt, m_AliveAt;
        Vector3 m_DeathPos;
        bool m_Concealed;
        float m_NextSuperSpark;

        public Camera PlayerCamera => m_Camera;
        /// <summary>Até quando este jogador fica visível mesmo dentro de um arbusto (atacou/levou dano).</summary>
        public float RevealedUntil => m_RevealedUntil;
        /// <summary>Escondido num arbusto para o jogador local.</summary>
        public bool IsConcealedForLocal => m_Concealed;
        public bool InBush { get; private set; }
        public Vector3 HeadPosition => m_Rig != null ? m_Rig.HeadPosition : transform.position + Vector3.up * 1.6f;

        public Vector3 MuzzlePosition => GetMuzzle(0);

        /// <summary>Ponto de saída do disparo n.º shotIndex (com dois canos alterna entre eles).</summary>
        public Vector3 GetMuzzle(int shotIndex)
        {
            if (m_Player.IsLocalHuman && m_View != null && m_View.Root != null && m_View.Root.gameObject.activeInHierarchy)
                return m_View.GetMuzzle(shotIndex);
            if (m_Rig != null && m_Rig.Root != null && !m_Player.IsLocalHuman)
                return m_Rig.GetMuzzle(shotIndex);

            var b = m_Player.Brawler;
            if (m_Player.IsLocalHuman && m_Camera != null && b != null && b.firstPerson.muzzleOffsets != null && b.firstPerson.muzzleOffsets.Length > 0)
                return m_Camera.transform.TransformPoint(b.firstPerson.muzzleOffsets[Mathf.Abs(shotIndex) % b.firstPerson.muzzleOffsets.Length]);
            var pivot = m_Player.Motor != null ? m_Player.Motor.aimPivot : null;
            var tp = b != null ? b.thirdPerson.muzzleOffsets : null;
            if (pivot != null && tp != null && tp.Length > 0)
                return pivot.TransformPoint(tp[Mathf.Abs(shotIndex) % tp.Length]);
            return thirdPersonMuzzle != null ? thirdPersonMuzzle.position : transform.position + Vector3.up * 1.4f;
        }

        void Awake()
        {
            m_Player = GetComponent<PlayerNetwork>();
            m_Controller = GetComponent<CharacterController>();
            m_ViewLayer = LayerMask.NameToLayer(ViewModelLayerName);
        }

        public override void OnNetworkSpawn()
        {
            m_LastPos = transform.position;
            m_Player.HeroId.OnValueChanged += OnHeroChanged;

            // As peças antigas do prefab (cápsula) dão lugar à rig do Brawler
            if (bodyRenderers != null)
                foreach (var r in bodyRenderers)
                    if (r != null) r.enabled = false;

            if (m_Player.IsLocalHuman)
            {
                CreateCameras();
                CreateAimHelpers();
                MenuCamera.SetActive(false);
            }
            else
            {
                CreateBubble();
            }

            RebuildRigs();
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnHeroChanged;
            if (m_Player.IsLocalHuman) MenuCamera.SetActive(true);
            m_Rig?.Destroy();
            m_View?.Destroy();
            m_Rig = null;
            m_View = null;
        }

        void OnHeroChanged(int previous, int current) => RebuildRigs();

        void RebuildRigs()
        {
            var b = m_Player.Brawler;
            int hero = m_Player.HeroId.Value;
            if (hero == m_BuiltHero && m_Rig != null) return;
            bool swap = m_BuiltHero != int.MinValue;
            m_BuiltHero = hero;

            m_Rig?.Destroy();
            m_Rig = CharacterRig.Build(b, transform, ShadowCastingMode.On, 0);
            if (m_Player.IsLocalHuman) m_Rig.SetShadowsOnly(true);
            m_Rig.SetDead(m_Player.Health.IsDead, true);
            if (m_Player.Health.IsDead) m_Rig.SetVisible(false);

            if (m_Player.IsLocalHuman && m_Camera != null)
            {
                m_View?.Destroy();
                m_View = ViewModelRig.Build(b, m_Camera.transform, m_ViewLayer >= 0 ? m_ViewLayer : 0);
                m_View.SetVisible(!m_Player.Health.IsDead);
            }

            // Troca de personagem em jogo: pequeno efeito no sítio
            if (swap && !m_Player.Health.IsDead && IsSpawned) Fx.SpawnBeam(transform.position, b != null ? b.Tint : Color.white);
        }

        // ================================================================== construção

        void CreateCameras()
        {
            var root = m_Player.Motor.cameraRoot;

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root, false);
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.fieldOfView = 85f;
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            if (m_ViewLayer >= 0)
            {
                // Câmara só para as mãos/arma: desenhada por cima, nunca atravessa paredes
                m_Camera.cullingMask &= ~(1 << m_ViewLayer);
                var vmGo = new GameObject("ViewModelCamera");
                vmGo.transform.SetParent(camGo.transform, false);
                m_ViewCamera = vmGo.AddComponent<Camera>();
                m_ViewCamera.clearFlags = CameraClearFlags.Depth;
                m_ViewCamera.cullingMask = 1 << m_ViewLayer;
                m_ViewCamera.depth = m_Camera.depth + 1f;
                m_ViewCamera.nearClipPlane = 0.01f;
                m_ViewCamera.farClipPlane = 5f;
                m_ViewCamera.fieldOfView = m_Camera.fieldOfView;
                m_ViewCamera.allowHDR = m_Camera.allowHDR;
                m_ViewCamera.allowMSAA = m_Camera.allowMSAA;
            }
        }

        void CreateAimHelpers()
        {
            // Linha de trajetória (ataques em arco / armadilhas / salto)
            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(transform, false);
            m_Trajectory = trajGo.AddComponent<LineRenderer>();
            m_Trajectory.positionCount = 0;
            m_Trajectory.startWidth = 0.03f;
            m_Trajectory.endWidth = 0.09f;
            m_Trajectory.shadowCastingMode = ShadowCastingMode.Off;
            m_Trajectory.receiveShadows = false;
            m_Trajectory.useWorldSpace = true;
            m_Trajectory.textureMode = LineTextureMode.Stretch;
            m_Trajectory.sharedMaterial = Art.FxBeam;

            m_TargetMarker = Art.FxQuad(null, Art.RingTexture, Color.white, true);
            m_TargetMarker.name = "AimMarker";
            m_TargetMarkerR = m_TargetMarker.GetComponent<Renderer>();
            m_TargetMarker.gameObject.SetActive(false);
        }

        void CreateBubble()
        {
            m_Bubble = Art.FxPart(PrimitiveType.Sphere, transform, new Vector3(0f, 0.95f, 0f), new Vector3(1.6f, 2.1f, 1.6f), new Color(0.45f, 0.8f, 1f, 0.16f), false);
            m_Bubble.name = "SpawnShield";
            m_BubbleR = m_Bubble.GetComponent<Renderer>();
            m_Bubble.gameObject.SetActive(false);
        }

        public override void OnDestroy()
        {
            if (m_TargetMarker != null) Destroy(m_TargetMarker.gameObject);
            base.OnDestroy();
        }

        // ================================================================== eventos

        public void OnAttackStarted(AttackDefinition attack, bool super)
        {
            var b = m_Player.Brawler;
            float dur = b != null ? (super ? b.animations.superDuration : b.animations.attackDuration) : 0.4f;
            m_Rig?.OnAttack(super, dur);
            m_View?.OnAttack(super);
            m_RevealedUntil = Time.time + 1.3f;

            if (m_Player.IsLocalHuman)
            {
                float kick = b != null ? b.animations.attackKick : 1f;
                m_Player.Motor.AddRecoil((super ? 3.5f : 1.8f) * kick);
                if (super) CameraShake.Add(0.3f);
            }
            if (super)
            {
                var c = b != null ? b.vfx.superColor : Color.yellow;
                Fx.Ring(transform.position, c, 4f, 0.45f);
                if (!m_Player.IsLocalHuman) Fx.Light(transform.position + Vector3.up * 1.2f, c, 3f, 6f, 0.3f);
            }
        }

        public void MuzzleFlash(Color color) => MuzzleFlash(color, 0);

        public void MuzzleFlash(Color color, int shotIndex)
        {
            bool local = m_Player.IsLocalHuman;
            var pos = GetMuzzle(shotIndex);
            var dir = m_Player.Motor.AimDirection;
            var b = m_Player.Brawler;
            float size = b != null ? b.vfx.muzzleFlashScale : 1f;
            if (!m_Concealed) Fx.Muzzle(pos, dir, color, size, local);
            m_Rig?.OnShot(shotIndex);
            m_View?.OnShot(shotIndex);
            if (local) CameraShake.Add(0.04f);
        }

        public void OnChargeReloaded()
        {
            m_View?.OnChargeReloaded();
            var b = m_Player.Brawler;
            if (m_Player.IsLocalHuman && b != null && b.firstPerson.rig == RigStyle.DualRevolvers) Sfx.Play2D(Sound.Twirl, 0.5f);
        }

        public void OnHurt(int amount)
        {
            m_Rig?.OnHurt();
            m_RevealedUntil = Mathf.Max(m_RevealedUntil, Time.time + 1f);
            if (m_Player.IsLocalHuman) CameraShake.Add(Mathf.Clamp(amount / 120f, 0.08f, 0.35f));
        }

        /// <summary>Morte/vida. A rig cai e desaparece num "puf"; ao reaparecer surge com um feixe de luz.</summary>
        public void SetDead(bool dead, bool instant)
        {
            if (m_Controller != null) m_Controller.enabled = !dead;
            if (m_View != null) m_View.SetVisible(!dead);

            if (dead)
            {
                m_Dead = true;
                m_PendingRespawn = false;
                m_DeadAt = Time.time;
                m_DeathPos = transform.position;
                m_PoofDone = instant;
                if (m_Rig != null)
                {
                    // O próprio jogador vê o seu boneco cair (câmara de morte em terceira pessoa)
                    if (m_Player.IsLocalHuman) m_Rig.SetShadowsOnly(false);
                    m_Rig.SetDead(true, instant);
                    if (instant) m_Rig.SetVisible(false);
                }
            }
            else
            {
                m_Dead = false;
                if (m_Player.IsLocalHuman) m_Rig?.SetShadowsOnly(true);
                if (instant)
                {
                    m_PendingRespawn = false;
                    m_Rig?.SetDead(false, true);
                    m_Rig?.SetVisible(true);
                }
                else
                {
                    // Espera pela posição de spawn antes de mostrar (o teletransporte chega por outra mensagem)
                    m_PendingRespawn = true;
                    m_AliveAt = Time.time;
                    m_Rig?.SetVisible(false);
                }
            }
        }

        // ================================================================== update

        void Update()
        {
            if (!IsSpawned) return;
            float dt = Time.deltaTime;
            bool local = m_Player.IsLocalHuman;
            bool dead = m_Player.Health.IsDead;
            var b = m_Player.Brawler;

            if (m_Player.HeroId.Value != m_BuiltHero) RebuildRigs();

            // ---------------- velocidade (remotos: pela posição)
            var pos = transform.position;
            Vector3 vel;
            if (IsOwner) vel = m_Player.Motor.Velocity;
            else vel = dt > 0f ? (pos - m_LastPos) / dt : Vector3.zero;
            if (vel.sqrMagnitude > 400f) vel = Vector3.zero; // teletransporte
            m_SmoothedVelocity = Vector3.Lerp(m_SmoothedVelocity, vel, 1f - Mathf.Exp(-12f * dt));

            // ---------------- morte / reaparecer
            if (m_Dead && !m_PoofDone && Time.time - m_DeadAt > 0.75f)
            {
                m_PoofDone = true;
                Fx.Poof(m_DeathPos + Vector3.up * 0.6f, b != null ? b.Tint : Color.white);
                Sfx.PlayAt(Sound.Poof, m_DeathPos + Vector3.up, 0.8f);
                m_Rig?.SetVisible(false);
            }
            if (m_PendingRespawn && ((pos - m_DeathPos).sqrMagnitude > 9f || Time.time - m_AliveAt > 0.35f))
            {
                m_PendingRespawn = false;
                m_PoofDone = false;
                if (m_Rig != null)
                {
                    m_Rig.SetDead(false, false);
                    m_Rig.SetVisible(true);
                }
                Fx.SpawnBeam(pos, b != null ? b.Tint : Color.white);
                Sfx.PlayAt(Sound.Respawn, pos + Vector3.up, local ? 0.6f : 0.9f);
                m_SmoothedVelocity = Vector3.zero;
            }

            // ---------------- arbustos
            InBush = !dead && Bush.Find(pos) != null;
            var me = PlayerNetwork.Local;
            bool concealed = !local && !dead && me != null && me != m_Player && Bush.Conceals(m_Player, me.transform.position);
            if (concealed != m_Concealed)
            {
                m_Concealed = concealed;
                if (concealed) Fx.Puff(pos + Vector3.up * 0.8f, Vector3.up, new Color(0.3f, 0.7f, 0.3f, 0.6f), 0.9f, 0.5f);
            }
            if (m_Rig != null && !m_Dead && !m_PendingRespawn)
            {
                m_Rig.SetVisible(!m_Concealed);
                // Dentro do arbusto mas visível (perto/revelado): meio transparente
                m_Rig.SetFade(InBush && !local ? 0.55f : 1f);
            }

            // ---------------- rig de terceira pessoa
            if (m_Rig != null)
            {
                m_Rig.Tick(dt, new RigFrame
                {
                    worldVelocity = m_SmoothedVelocity,
                    grounded = m_Player.Motor.IsGrounded,
                    pitch = m_Player.Motor.Pitch,
                    superReady = m_Player.Combat.SuperReady,
                });
            }

            // Brilho de Super pronto nos outros
            if (!local && !dead && !m_Concealed && m_Player.Combat.SuperReady && Time.time >= m_NextSuperSpark && m_Rig != null)
            {
                m_NextSuperSpark = Time.time + 0.12f;
                var c = b != null ? Color.Lerp(b.vfx.superColor, new Color(1f, 0.9f, 0.3f), 0.5f) : Color.yellow;
                Fx.Glow(m_Rig.ChestPosition + Random.insideUnitSphere * 0.5f, Vector3.up * 1.2f, c, 0.12f, 0.6f);
            }

            // ---------------- escudo de spawn
            if (m_Bubble != null)
            {
                bool prot = m_Player.Health.IsProtected && !m_Concealed && !m_PendingRespawn;
                if (m_Bubble.gameObject.activeSelf != prot) m_Bubble.gameObject.SetActive(prot);
                if (prot)
                {
                    float p = 0.13f + 0.06f * Mathf.Sin(Time.time * 8f);
                    Art.SetColor(m_BubbleR, new Color(0.45f, 0.8f, 1f, p));
                }
            }

            // ---------------- primeira pessoa
            if (local) UpdateFirstPerson(dt, dead);

            // ---------------- passos
            var delta = pos - m_LastPos;
            m_LastPos = pos;
            delta.y = 0f;
            if (!dead && m_Player.Motor.IsGrounded && delta.magnitude < 2f)
            {
                m_StepDistance += delta.magnitude;
                if (m_StepDistance > 2.3f)
                {
                    m_StepDistance = 0f;
                    if (local) Sfx.Play2D(Sound.Step, 0.35f);
                    else if (!m_Concealed) Sfx.PlayAt(Sound.Step, pos, 0.8f);
                    if (!local && !m_Concealed && m_SmoothedVelocity.sqrMagnitude > 40f)
                        Fx.Smoke(pos + Vector3.up * 0.1f, -m_SmoothedVelocity.normalized * 0.4f + Vector3.up * 0.3f, 0.35f, 0.45f, 0.35f);
                }
            }
        }

        void UpdateFirstPerson(float dt, bool dead)
        {
            var motor = m_Player.Motor;
            float yaw = motor.Yaw, pitch = motor.Pitch;
            var look = new Vector2(Mathf.DeltaAngle(m_LastYaw, yaw), Mathf.DeltaAngle(m_LastPitch, pitch));
            m_LastYaw = yaw;
            m_LastPitch = pitch;

            if (m_View != null)
            {
                float cooldown01 = 0f;
                m_View.Tick(dt, new ViewFrame
                {
                    speed = new Vector2(motor.Velocity.x, motor.Velocity.z).magnitude,
                    grounded = motor.IsGrounded,
                    sprinting = motor.IsSprinting && motor.IsMoving,
                    look = look,
                    superReady = m_Player.Combat.SuperReady,
                    cooldown01 = cooldown01,
                });
            }

            if (m_Camera != null)
            {
                var target = dead ? new Vector3(0f, 2.5f, -2.8f) : Vector3.zero;
                m_Camera.transform.localPosition = Vector3.Lerp(m_Camera.transform.localPosition, target, 1f - Mathf.Exp(-6f * dt));
                var baseRot = dead ? Quaternion.Euler(35f, 0f, 0f) : Quaternion.identity;
                var shake = CameraShake.Sample(dt);
                m_CamRot = Quaternion.Slerp(m_CamRot, baseRot, 1f - Mathf.Exp(-6f * dt));
                m_Camera.transform.localRotation = m_CamRot * Quaternion.Euler(shake);

                // Abrandado: campo de visão mais fechado; sprint: ligeiramente mais aberto
                float fov = m_Player.Health.IsSlowed ? 78f : (motor.IsSprinting && motor.IsMoving ? 90f : 85f);
                if (motor.IsLaunched) fov = 92f;
                m_Camera.fieldOfView = Mathf.Lerp(m_Camera.fieldOfView, fov, 1f - Mathf.Exp(-8f * dt));
                if (m_ViewCamera != null) m_ViewCamera.fieldOfView = m_Camera.fieldOfView;
            }

            UpdateTrajectory(dead);
        }

        /// <summary>Mostra a trajetória/alvo dos ataques que usam ponto-alvo (arcos, minas, salto).</summary>
        void UpdateTrajectory(bool dead)
        {
            if (m_Trajectory == null) return;

            var b = m_Player.Brawler;
            AttackDefinition attack = null;
            if (b != null)
            {
                if (m_Player.Combat.SuperReady && b.super.attack != null && b.super.attack.delivery != null && b.super.attack.delivery.UsesTargetPoint) attack = b.super.attack;
                else if (b.primary.attack != null && b.primary.attack.delivery != null && b.primary.attack.delivery.UsesTargetPoint) attack = b.primary.attack;
            }

            if (dead || attack == null || !GameUI.GameplayInputAllowed)
            {
                m_Trajectory.positionCount = 0;
                if (m_TargetMarker != null && m_TargetMarker.gameObject.activeSelf) m_TargetMarker.gameObject.SetActive(false);
                return;
            }

            var c = attack.visuals.color;
            var target = CombatMath.AimPoint(m_Player, attack.Range);

            if (attack.delivery is ArcProjectileDelivery arc)
            {
                var start = GetMuzzle(0);
                var v = arc.Velocity(start, target, out float t);
                const int n = 26;
                m_Trajectory.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float ti = t * i / (n - 1);
                    m_Trajectory.SetPosition(i, start + v * ti + 0.5f * Vector3.down * arc.gravity * ti * ti);
                }
                m_Trajectory.startColor = new Color(c.r, c.g, c.b, 0.15f);
                m_Trajectory.endColor = new Color(c.r, c.g, c.b, 0.9f);
                PlaceMarker(target, Mathf.Max(1f, attack.effect.splashRadius * 2f), c);
            }
            else if (attack.delivery is LeapDelivery leap)
            {
                var start = transform.position + Vector3.up * 0.2f;
                var land = MapInfo.ClampInside(target);
                var flat = land - transform.position;
                flat.y = 0f;
                if (flat.magnitude > leap.range) land = transform.position + flat.normalized * leap.range + Vector3.up * (land.y - transform.position.y);
                float T = Mathf.Max(0.2f, leap.flightTime);
                var delta = land - start;
                float g = m_Player.Motor.gravity;
                var v = new Vector3(delta.x / T, (delta.y + 0.5f * g * T * T) / T, delta.z / T);
                const int n = 22;
                m_Trajectory.positionCount = n;
                for (int i = 0; i < n; i++)
                {
                    float ti = T * i / (n - 1);
                    m_Trajectory.SetPosition(i, start + v * ti + 0.5f * Vector3.down * g * ti * ti);
                }
                m_Trajectory.startColor = new Color(c.r, c.g, c.b, 0.1f);
                m_Trajectory.endColor = new Color(c.r, c.g, c.b, 0.8f);
                PlaceMarker(land, 1.6f, c);
            }
            else
            {
                m_Trajectory.positionCount = 0;
                PlaceMarker(target, attack.delivery is PlaceTrapsDelivery traps ? traps.spread * 2f + 1.5f : 1.5f, c);
            }
        }

        void PlaceMarker(Vector3 point, float size, Color c)
        {
            if (m_TargetMarker == null) return;
            if (!m_TargetMarker.gameObject.activeSelf) m_TargetMarker.gameObject.SetActive(true);
            var ground = CombatMath.GroundBelow(point + Vector3.up * 0.5f, m_Player);
            m_TargetMarker.position = ground + Vector3.up * 0.06f;
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.05f;
            m_TargetMarker.localScale = new Vector3(size * pulse, 1f, size * pulse);
            m_TargetMarker.rotation = Quaternion.Euler(0f, Time.time * 40f, 0f);
            Art.SetColor(m_TargetMarkerR, new Color(c.r, c.g, c.b, 0.85f));
        }
    }
}
