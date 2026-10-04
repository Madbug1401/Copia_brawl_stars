using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Aspeto do jogador: corpo colorido para os outros, câmara + arma em primeira pessoa para o humano local,
    /// cor do personagem, muzzle flash, recuo, passos, trajetória de granadas e estado de morte.
    /// </summary>
    public class PlayerVisuals : NetworkBehaviour
    {
        [Header("Referências (preenchidas pelo setup)")]
        public Renderer[] bodyRenderers;
        public Renderer[] tintRenderers;
        public Transform thirdPersonMuzzle;

        PlayerNetwork m_Player;
        CharacterController m_Controller;

        Camera m_Camera;
        Transform m_ViewModel;
        Transform m_ViewMuzzle;
        Renderer m_ViewTint;
        Vector3 m_ViewModelRest;
        float m_ViewKick;
        float m_BobTime;

        GameObject m_Flash;
        Light m_FlashLight;
        float m_FlashTimer;

        LineRenderer m_Trajectory;
        Renderer m_HeroTintRenderer;

        float m_StepDistance;
        Vector3 m_LastPos;
        MaterialPropertyBlock m_Block;

        public Camera PlayerCamera => m_Camera;
        Color TintColor => m_Player.Brawler != null ? m_Player.Brawler.Tint : Color.white;
        int m_LastFlashIndex;

        public Vector3 MuzzlePosition => GetMuzzle(0);

        /// <summary>
        /// Ponto de saída do disparo n.º shotIndex. Com vários canos (ex.: dois revólveres) alterna entre eles.
        /// Os offsets vêm da apresentação FP/TP do BrawlerDefinition.
        /// </summary>
        public Vector3 GetMuzzle(int shotIndex)
        {
            var b = m_Player.Brawler;
            if (m_Player.IsLocalHuman && m_Camera != null)
            {
                var offsets = b != null ? b.firstPerson.muzzleOffsets : null;
                if (offsets != null && offsets.Length > 1)
                    return m_Camera.transform.TransformPoint(offsets[Mathf.Abs(shotIndex) % offsets.Length]);
                if (m_ViewMuzzle != null) return m_ViewMuzzle.position;
            }

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
            m_Block = new MaterialPropertyBlock();

            var visor = transform.Find("Head/Visor");
            if (visor != null) m_HeroTintRenderer = visor.GetComponent<Renderer>();
        }

        public override void OnNetworkSpawn()
        {
            ApplyTint();
            m_LastPos = transform.position;
            m_Player.HeroId.OnValueChanged += OnHeroChanged;

            if (m_Player.IsLocalHuman)
            {
                CreateFirstPersonRig();
                foreach (var r in bodyRenderers)
                    if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                MenuCamera.SetActive(false);
            }

            CreateFlash();
            ApplyHeroTint();
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnHeroChanged;
            if (m_Player.IsLocalHuman) MenuCamera.SetActive(true);
        }

        void OnHeroChanged(int previous, int current) => ApplyHeroTint();

        void ApplyTint()
        {
            var c = m_Player.PlayerColor;
            if (tintRenderers == null) return;
            foreach (var r in tintRenderers)
                if (r != null) SetColor(r, c);
        }

        void ApplyHeroTint()
        {
            var c = TintColor;
            if (m_HeroTintRenderer != null) SetColor(m_HeroTintRenderer, c);
            if (m_ViewTint != null) SetColor(m_ViewTint, c);
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(m_Block);
            m_Block.SetColor("_Color", c);
            m_Block.SetColor("_BaseColor", c);
            r.SetPropertyBlock(m_Block);
        }

        void CreateFirstPersonRig()
        {
            var root = m_Player.Motor.cameraRoot;

            var camGo = new GameObject("PlayerCamera");
            camGo.transform.SetParent(root, false);
            m_Camera = camGo.AddComponent<Camera>();
            m_Camera.fieldOfView = 85f;
            m_Camera.nearClipPlane = 0.03f;
            m_Camera.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            var vm = new GameObject("ViewModel").transform;
            vm.SetParent(camGo.transform, false);
            m_ViewModelRest = new Vector3(0.17f, -0.16f, 0.36f);
            vm.localPosition = m_ViewModelRest;
            vm.localScale = Vector3.one * 0.45f;
            m_ViewModel = vm;

            MakePart(PrimitiveType.Cube, vm, Vector3.zero, new Vector3(0.09f, 0.13f, 0.32f), new Color(0.2f, 0.22f, 0.26f));
            MakePart(PrimitiveType.Cube, vm, new Vector3(0f, 0.03f, 0.22f), new Vector3(0.05f, 0.05f, 0.22f), new Color(0.12f, 0.12f, 0.14f));
            m_ViewTint = MakePart(PrimitiveType.Cube, vm, new Vector3(0f, -0.1f, -0.06f), new Vector3(0.06f, 0.13f, 0.07f), TintColor).GetComponent<Renderer>();
            MakePart(PrimitiveType.Cube, vm, new Vector3(0f, 0.075f, 0.05f), new Vector3(0.1f, 0.025f, 0.2f), TintColor);

            m_ViewMuzzle = new GameObject("Muzzle").transform;
            m_ViewMuzzle.SetParent(vm, false);
            m_ViewMuzzle.localPosition = new Vector3(0f, 0.03f, 0.36f);

            // Linha de trajetória (granadas)
            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(transform, false);
            m_Trajectory = trajGo.AddComponent<LineRenderer>();
            m_Trajectory.positionCount = 0;
            m_Trajectory.startWidth = 0.008f;
            m_Trajectory.endWidth = 0.07f;
            m_Trajectory.shadowCastingMode = ShadowCastingMode.Off;
            m_Trajectory.receiveShadows = false;
            m_Trajectory.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) m_Trajectory.material = new Material(shader);
            m_Trajectory.startColor = new Color(1f, 1f, 1f, 0.8f);
            m_Trajectory.endColor = new Color(1f, 1f, 1f, 0.2f);
        }

        Transform MakePart(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            col.enabled = false;
            Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            SetColor(r, color);
            return go.transform;
        }

        void CreateFlash()
        {
            m_Flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Flash.name = "MuzzleFlash";
            var col = m_Flash.GetComponent<Collider>();
            col.enabled = false;
            Destroy(col);
            var r = m_Flash.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            m_Flash.transform.SetParent(transform, false);
            m_Flash.transform.localScale = Vector3.one * (m_Player.IsLocalHuman ? 0.07f : 0.25f);

            m_FlashLight = m_Flash.AddComponent<Light>();
            m_FlashLight.type = LightType.Point;
            m_FlashLight.range = 6f;
            m_FlashLight.intensity = 2.5f;
            m_FlashLight.shadows = LightShadows.None;
            m_Flash.SetActive(false);
        }

        public void OnAttackStarted(AttackDefinition attack, bool super)
        {
            if (m_Player.IsLocalHuman)
            {
                float kick = m_Player.Brawler != null ? m_Player.Brawler.animations.attackKick : 1f;
                m_ViewKick = 1f;
                m_Player.Motor.AddRecoil((super ? 3.5f : 1.8f) * kick);
            }
        }

        public void MuzzleFlash(Color color) => MuzzleFlash(color, 0);

        public void MuzzleFlash(Color color, int shotIndex)
        {
            if (m_Flash == null) return;
            m_LastFlashIndex = shotIndex;
            m_Flash.transform.position = GetMuzzle(shotIndex);
            var r = m_Flash.GetComponent<Renderer>();
            SetColor(r, color * 2f);
            m_FlashLight.color = color;
            m_Flash.SetActive(true);
            m_FlashTimer = 0.04f;
            if (m_Player.IsLocalHuman) m_ViewKick = Mathf.Min(1.2f, m_ViewKick + 0.35f);
        }

        /// <summary>Esconde/mostra o jogador e desliga o colisor quando morto.</summary>
        public void SetDead(bool dead, bool instant)
        {
            if (m_Controller != null) m_Controller.enabled = !dead;

            if (!m_Player.IsLocalHuman)
            {
                foreach (var r in bodyRenderers)
                    if (r != null) r.enabled = !dead;
            }

            if (m_ViewModel != null) m_ViewModel.gameObject.SetActive(!dead);
        }

        void Update()
        {
            if (!IsSpawned) return;
            float dt = Time.deltaTime;

            if (m_FlashTimer > 0f)
            {
                m_FlashTimer -= dt;
                m_Flash.transform.position = GetMuzzle(m_LastFlashIndex);
                if (m_FlashTimer <= 0f) m_Flash.SetActive(false);
            }

            bool dead = m_Player.Health.IsDead;
            bool local = m_Player.IsLocalHuman;

            if (local && m_ViewModel != null)
            {
                m_ViewKick = Mathf.MoveTowards(m_ViewKick, 0f, dt * 6f);
                float speed = new Vector2(m_Player.Motor.Velocity.x, m_Player.Motor.Velocity.z).magnitude;
                if (m_Player.Motor.IsGrounded && speed > 0.5f) m_BobTime += dt * speed * 1.4f;
                var bob = new Vector3(Mathf.Sin(m_BobTime) * 0.012f, Mathf.Abs(Mathf.Cos(m_BobTime)) * 0.012f, 0f);
                m_ViewModel.localPosition = m_ViewModelRest + bob + new Vector3(0f, 0.01f, -0.08f) * m_ViewKick;
                m_ViewModel.localRotation = Quaternion.Euler(-8f * m_ViewKick, 0f, 0f);
            }

            if (local && m_Camera != null)
            {
                var target = dead ? new Vector3(0f, 2.5f, -2.5f) : Vector3.zero;
                m_Camera.transform.localPosition = Vector3.Lerp(m_Camera.transform.localPosition, target, 1f - Mathf.Exp(-6f * dt));
                var rot = dead ? Quaternion.Euler(35f, 0f, 0f) : Quaternion.identity;
                m_Camera.transform.localRotation = Quaternion.Slerp(m_Camera.transform.localRotation, rot, 1f - Mathf.Exp(-6f * dt));

                // Abrandado: campo de visão ligeiramente mais fechado
                float fov = m_Player.Health.IsSlowed ? 78f : 85f;
                m_Camera.fieldOfView = Mathf.Lerp(m_Camera.fieldOfView, fov, 1f - Mathf.Exp(-8f * dt));
            }

            if (local) UpdateTrajectory(dead);

            // Passos
            var pos = transform.position;
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
                    else Sfx.PlayAt(Sound.Step, pos, 0.8f);
                }
            }
        }

        /// <summary>Mostra o arco do ataque em arco (principal, ou Super quando está pronto) para o jogador local.</summary>
        void UpdateTrajectory(bool dead)
        {
            if (m_Trajectory == null) return;

            var b = m_Player.Brawler;
            AttackDefinition attack = null;
            if (b != null)
            {
                if (m_Player.Combat.SuperReady && b.super.attack != null && b.super.attack.delivery is ArcProjectileDelivery) attack = b.super.attack;
                else if (b.primary.attack != null && b.primary.attack.delivery is ArcProjectileDelivery) attack = b.primary.attack;
            }

            if (dead || attack == null || !GameUI.GameplayInputAllowed)
            {
                m_Trajectory.positionCount = 0;
                return;
            }

            var arc = (ArcProjectileDelivery)attack.delivery;
            var start = GetMuzzle(0);
            var target = CombatMath.AimPoint(m_Player, arc.range);
            var v = arc.Velocity(start, target, out float t);
            const int n = 24;
            m_Trajectory.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float ti = t * i / (n - 1);
                m_Trajectory.SetPosition(i, start + v * ti + 0.5f * Vector3.down * arc.gravity * ti * ti);
            }
            var c = attack.visuals.color;
            m_Trajectory.startColor = new Color(c.r, c.g, c.b, 0.85f);
            m_Trajectory.endColor = new Color(c.r, c.g, c.b, 0.35f);
        }
    }
}
