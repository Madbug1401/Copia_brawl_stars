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
        int m_BuiltSkin = int.MinValue;
        int m_ViewLayer = -1;
        SkinDefinition m_Skin;
        PetFollower m_Pet;
        PlayerEmotesFx m_Emotes;

        // Estados visíveis para os outros
        Transform m_Cage;
        Transform m_ShieldBubble;
        Renderer m_ShieldBubbleR;
        Transform m_ModeRing;
        Renderer m_ModeRingR;
        float m_NextDotPuff;

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
        Transform m_Crown;

        // Líder da partida (calculado uma vez por frame para todos)
        static PlayerNetwork s_Leader;
        static int s_LeaderFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Leader = null;
            s_LeaderFrame = -1;
        }

        static PlayerNetwork Leader
        {
            get
            {
                if (s_LeaderFrame == Time.frameCount) return s_Leader;
                s_LeaderFrame = Time.frameCount;
                PlayerNetwork best = null;
                bool tie = false;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || !p.IsSpawned) continue;
                    if (best == null || p.Kills.Value > best.Kills.Value) { best = p; tie = false; }
                    else if (p.Kills.Value == best.Kills.Value) tie = true;
                }
                s_Leader = best != null && !tie && best.Kills.Value > 0 ? best : null;
                return s_Leader;
            }
        }

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
            if (GetComponent<TeamPresentation>() == null) gameObject.AddComponent<TeamPresentation>();
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
                CreateCrown();
                CreateStatusVisuals();
            }

            RebuildRigs();
            RefreshPet();
            m_Emotes = new PlayerEmotesFx(m_Player, this, m_ViewLayer);
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnHeroChanged;
            if (m_Player.IsLocalHuman) MenuCamera.SetActive(true);
            m_Rig?.Destroy();
            m_View?.Destroy();
            m_Rig = null;
            m_View = null;
            if (m_Pet != null) m_Pet.Despawn();
            m_Pet = null;
            m_Emotes?.Dispose();
            m_Emotes = null;
        }

        void OnHeroChanged(int previous, int current) => RebuildRigs();

        /// <summary>Skin equipada (sincronizada pelo loadout do dono) para o Brawler atual; null = aspeto original.</summary>
        SkinDefinition ResolveSkin(BrawlerDefinition b)
        {
            if (b == null) return null;
            return SkinManager.Resolve(GameExperience.Current.SkinCatalog, m_Player.Loadout.Value.skinHash, SkinTarget.Brawler, b.brawlerId);
        }

        void RebuildRigs()
        {
            var b = m_Player.Brawler;
            int hero = m_Player.HeroId.Value;
            int skinHash = m_Player.Loadout.Value.skinHash;
            if (hero == m_BuiltHero && skinHash == m_BuiltSkin && m_Rig != null) return;
            bool swap = m_BuiltHero != int.MinValue;
            m_BuiltHero = hero;
            m_BuiltSkin = skinHash;
            m_Skin = ResolveSkin(b);

            m_Rig?.Destroy();
            m_Rig = CharacterRig.Build(b, transform, ShadowCastingMode.On, 0, m_Skin);
            if (m_Player.IsLocalHuman) m_Rig.SetShadowsOnly(true);
            m_Rig.SetDead(m_Player.Health.IsDead, true);
            if (m_Player.Health.IsDead) m_Rig.SetVisible(false);

            if (m_Player.IsLocalHuman && m_Camera != null)
            {
                m_View?.Destroy();
                m_View = ViewModelRig.Build(b, m_Camera.transform, m_ViewLayer >= 0 ? m_ViewLayer : 0, m_Skin);
                m_View.SetVisible(!m_Player.Health.IsDead);
            }

            if (m_Player.Combat != null && m_Player.Combat.InSuperMode) OnSuperModeChanged(true);

            // Troca de personagem/skin em jogo: pequeno efeito no sítio
            if (swap && !m_Player.Health.IsDead && IsSpawned) Fx.SpawnBeam(transform.position, b != null ? b.Tint : Color.white);
        }

        /// <summary>O loadout mudou (skin, pet, nível do pet): reconstrói o que for preciso.</summary>
        public void OnLoadoutChanged()
        {
            if (!IsSpawned) return;
            RebuildRigs();
            RefreshPet();
        }

        /// <summary>Cria/atualiza o pet deste jogador a partir do loadout sincronizado (espécie, nível, skin).</summary>
        void RefreshPet()
        {
            var gx = GameExperience.Current;
            var l = m_Player.Loadout.Value;
            var def = gx.PetCatalog.FindByHash(l.petHash);
            if (def == null)
            {
                if (m_Pet != null) m_Pet.Despawn();
                m_Pet = null;
                return;
            }
            var petSkin = SkinManager.Resolve(gx.SkinCatalog, l.petSkinHash, SkinTarget.Pet, def.petId);
            int level = Mathf.Clamp(l.petLevel, 1, gx.Config.petLevels.maxLevel);
            if (m_Pet != null && m_Pet.Matches(def, level, petSkin != null ? petSkin.StableHash : 0)) return;
            if (m_Pet != null) m_Pet.Despawn();
            m_Pet = PetFollower.Create(m_Player, def, level, petSkin);
        }

        /// <summary>Cores dos efeitos dos ataques definidas pela skin (só visual).</summary>
        public void ApplySkinColors(AttackContext ctx)
        {
            if (ctx == null || m_Skin == null || !m_Skin.overrideVfx) return;
            ctx.hasColorOverride = true;
            ctx.colorOverride = ctx.isSuper ? m_Skin.superVfx : m_Skin.primaryVfx;
        }

        /// <summary>Especial em modo ligado/desligado (Minigun, tiro fatal armado).</summary>
        public void OnSuperModeChanged(bool on)
        {
            m_Rig?.SetSuperMode(on);
            m_View?.SetSuperMode(on);
            var b = m_Player.Brawler;
            var c = b != null ? b.vfx.superColor : Color.yellow;
            if (on)
            {
                Fx.Ring(transform.position, c, 3f, 0.4f);
                Fx.Sparks(transform.position + Vector3.up * 1.2f, c, 10, 4f);
                if (!m_Player.IsLocalHuman) Sfx.PlayAt(b != null && b.super.attack != null && b.super.attack.visuals.sound == Sound.Minigun ? Sound.MinigunSpin : Sound.SuperReady,
                    transform.position + Vector3.up, 1f);
            }
            else Fx.Smoke(transform.position + Vector3.up * 1.2f, Vector3.up * 0.5f, 0.4f, 0.5f, 0.4f);
        }

        /// <summary>O dono eliminou alguém: o pet celebra.</summary>
        public void OnOwnerScored() => m_Pet?.Celebrate();

        public void OnPetFed() { } // a celebração acontece no fim da animação de comer (PlayerEmotesFx)

        /// <summary>Pet que segue este jogador (pode ser null).</summary>
        public PetFollower Pet => m_Pet != null ? m_Pet : null;

        /// <summary>Spray SITA confirmado pelo host: lata na mão e jato de tinta.</summary>
        public void OnSpray(Vector3 point, int colorIndex) => m_Emotes?.OnSpray(point, colorIndex);

        /// <summary>Uma habilidade de pet foi ativada (ex.: escudo de lama do Porco).</summary>
        public void OnPetAbility(PetAbilityType ability)
        {
            if (ability == PetAbilityType.Protection)
            {
                Fx.Ring(transform.position, new Color(0.65f, 0.45f, 0.3f), 2.4f, 0.4f);
                Fx.Debris(transform.position + Vector3.up * 0.8f, new Color(0.55f, 0.38f, 0.25f), 8, 3f);
            }
            m_Pet?.Celebrate();
        }

        void CreateStatusVisuals()
        {
            // Jaula de imobilização (N.Cage): barras à volta do jogador preso
            m_Cage = new GameObject("RootCage").transform;
            m_Cage.SetParent(transform, false);
            var bar = new Color(0.45f, 0.75f, 0.3f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Art.Part(PrimitiveType.Cylinder, m_Cage, new Vector3(Mathf.Sin(a) * 0.62f, 0.75f, Mathf.Cos(a) * 0.62f), new Vector3(0.05f, 0.75f, 0.05f), bar, true, default, ShadowCastingMode.Off);
            }
            Art.Part(PrimitiveType.Cylinder, m_Cage, new Vector3(0f, 0.04f, 0f), new Vector3(1.35f, 0.04f, 1.35f), Art.Darken(bar, 0.7f), true, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, m_Cage, new Vector3(0f, 1.5f, 0f), new Vector3(1.35f, 0.04f, 1.35f), Art.Darken(bar, 0.7f), true, default, ShadowCastingMode.Off);
            m_Cage.gameObject.SetActive(false);

            // Escudo de lama (pet Porco)
            m_ShieldBubble = Art.FxPart(PrimitiveType.Sphere, transform, new Vector3(0f, 0.95f, 0f), new Vector3(1.5f, 2f, 1.5f), new Color(0.7f, 0.5f, 0.35f, 0.2f), false);
            m_ShieldBubble.name = "PetShield";
            m_ShieldBubbleR = m_ShieldBubble.GetComponent<Renderer>();
            m_ShieldBubble.gameObject.SetActive(false);

            // Anel no chão enquanto um especial em modo está ativo (Minigun)
            m_ModeRing = Art.FxQuad(transform, Art.RingTexture, Color.white, true);
            m_ModeRing.localPosition = Vector3.up * 0.05f;
            m_ModeRingR = m_ModeRing.GetComponent<Renderer>();
            m_ModeRing.gameObject.SetActive(false);
        }

        void UpdateStatusVisuals(bool dead)
        {
            if (m_Cage == null) return;
            bool hide = dead || m_Concealed || m_PendingRespawn;

            bool rooted = !hide && m_Player.Status != null && m_Player.Status.IsRooted;
            if (m_Cage.gameObject.activeSelf != rooted) m_Cage.gameObject.SetActive(rooted);
            if (rooted)
            {
                float k = Mathf.Clamp01(m_Player.Status.RootRemaining / 0.4f);
                m_Cage.localScale = new Vector3(1f, Mathf.Lerp(0.2f, 1f, k), 1f);
                m_Cage.localRotation = Quaternion.Euler(0f, Time.time * 20f, 0f);
            }

            bool shield = !hide && m_Player.Health.Shield.Value > 0;
            if (m_ShieldBubble.gameObject.activeSelf != shield) m_ShieldBubble.gameObject.SetActive(shield);
            if (shield) Art.SetColor(m_ShieldBubbleR, new Color(0.7f, 0.5f, 0.35f, 0.18f + 0.06f * Mathf.Sin(Time.time * 7f)));

            bool mode = !hide && m_Player.Combat.InSuperMode;
            if (m_ModeRing.gameObject.activeSelf != mode) m_ModeRing.gameObject.SetActive(mode);
            if (mode)
            {
                var b = m_Player.Brawler;
                var c = b != null ? b.vfx.superColor : Color.yellow;
                float pulse = 2.2f + Mathf.Sin(Time.time * 8f) * 0.15f;
                m_ModeRing.localScale = new Vector3(pulse, 1f, pulse);
                m_ModeRing.localRotation = Quaternion.Euler(0f, Time.time * 140f, 0f);
                Art.SetColor(m_ModeRingR, Art.WithAlpha(c, 0.8f));
            }

            // Envenenado: bolhas verdes por cima da cabeça
            if (!hide && m_Player.Status != null && m_Player.Status.HasDot.Value && Time.time >= m_NextDotPuff)
            {
                m_NextDotPuff = Time.time + 0.2f;
                Fx.Glow(HeadPosition + Vector3.up * 0.3f + Random.insideUnitSphere * 0.3f, Vector3.up * 0.8f, new Color(0.5f, 1f, 0.35f), 0.1f, 0.6f);
            }
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

        /// <summary>Coroa dourada que flutua sobre o líder da partida.</summary>
        void CreateCrown()
        {
            m_Crown = new GameObject("LeaderCrown").transform;
            m_Crown.SetParent(transform, false);
            var gold = new Color(1f, 0.8f, 0.2f);
            Art.Part(PrimitiveType.Cylinder, m_Crown, Vector3.zero, new Vector3(0.42f, 0.07f, 0.42f), gold, true, default, ShadowCastingMode.Off);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(a) * 0.17f, 0.12f, Mathf.Cos(a) * 0.17f);
                Art.Part(PrimitiveType.Cube, m_Crown, pos, new Vector3(0.08f, 0.16f, 0.08f), gold, true, new Vector3(0f, i * 72f, 45f), ShadowCastingMode.Off);
                Art.Part(PrimitiveType.Sphere, m_Crown, pos + Vector3.up * 0.11f, Vector3.one * 0.06f, i % 2 == 0 ? new Color(1f, 0.3f, 0.35f) : new Color(0.35f, 0.75f, 1f), false, default, ShadowCastingMode.Off);
            }
            foreach (var r in m_Crown.GetComponentsInChildren<Renderer>()) Art.SetEmission(r, new Color(0.25f, 0.18f, 0.02f));
            m_Crown.gameObject.SetActive(false);
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
                bool modeShot = super && b != null && b.super.IsMode;
                // Disparos do modo (Minigun) têm recuo próprio por bala; o resto recua no início do ataque
                if (!modeShot) m_Player.Motor.AddRecoil((super ? 3.5f : 1.8f) * kick);
                if (super && !modeShot) CameraShake.Add(0.3f);
                // Caçadeira (Riot): coice forte e visível
                if (b != null && b.firstPerson.rig == RigStyle.Shotgun) CameraShake.Add(super ? 0.35f : 0.2f);
            }
            if (super)
            {
                var c = b != null ? b.vfx.superColor : Color.yellow;
                Fx.Ring(transform.position, c, 4f, 0.45f);
                if (!m_Player.IsLocalHuman) Fx.Light(transform.position + Vector3.up * 1.2f, c, 3f, 6f, 0.3f);
            }
        }

        public void OnMeleeStarted()
        {
            var b = m_Player.Brawler;
            float duration = b != null && b.melee != null ? b.melee.animationDuration : 0.3f;
            m_Rig?.OnAttack(false, duration);
            m_View?.OnMelee(duration);
            var color = b != null ? b.vfx.primaryColor : Color.white;
            Fx.MeleeSlash(m_Player.Motor.EyePosition, m_Player.Motor.AimDirection, color);
            m_RevealedUntil = Time.time + 1.3f;
            if (m_Player.IsLocalHuman) m_Player.Motor.AddRecoil(0.45f);
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
            if (local)
            {
                CameraShake.Add(0.04f);
                // Armas automáticas: recuo por bala (metralhadoras e Minigun)
                if (m_Player.Combat.InSuperMode && b != null && b.firstPerson.rig == RigStyle.DualSMG) m_Player.Motor.AddRecoil(0.3f);
                else if (b != null && b.firstPerson.rig == RigStyle.DualSMG) m_Player.Motor.AddRecoil(0.22f * b.animations.attackKick);
            }
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
            m_Pet?.ReactToOwnerHurt();
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

            if (m_Player.HeroId.Value != m_BuiltHero || m_Player.Loadout.Value.skinHash != m_BuiltSkin) RebuildRigs();

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
            // Colegas de equipa continuam visíveis uns para os outros (só os adversários ficam ocultos)
            bool concealed = !local && !dead && me != null && me != m_Player && !MatchManager.AreTeammates(me, m_Player) &&
                Bush.Conceals(m_Player, me.transform.position);
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

            // ---------------- coroa do líder
            if (m_Crown != null)
            {
                bool show = !dead && !m_Concealed && !m_PendingRespawn && Leader == m_Player;
                if (m_Crown.gameObject.activeSelf != show) m_Crown.gameObject.SetActive(show);
                if (show)
                {
                    m_Crown.position = HeadPosition + Vector3.up * (1.3f + Mathf.Sin(Time.time * 3f) * 0.05f);
                    m_Crown.rotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
                }
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

            // ---------------- estados (preso, escudo do pet, especial ativo, veneno)
            UpdateStatusVisuals(dead);

            // ---------------- emotes de marca (UPRA / SITA)
            m_Emotes?.Tick(dt, m_Concealed || m_PendingRespawn, dead);

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
                // A alimentar o pet: a câmara baixa o olhar para a tigela
                float feedW = m_Emotes != null ? m_Emotes.FeedCameraWeight : 0f;
                var feedTilt = feedW > 0f ? Quaternion.Euler(Mathf.Clamp(38f - pitch, 0f, 70f) * feedW, 0f, 0f) : Quaternion.identity;
                m_Camera.transform.localRotation = feedTilt * m_CamRot * Quaternion.Euler(shake);

                // Abrandado: campo de visão mais fechado; sprint: ligeiramente mais aberto
                bool scoped = !dead && GameUI.GameplayInputAllowed && m_Player.Brawler != null &&
                    m_Player.Brawler.firstPerson.rig == RigStyle.Sniper && InputReader.AimHeld;
                // a mirar pela luneta: esconde a arma e as mãos (só se vê a mira no ecrã)
                bool emote = (m_Emotes != null && m_Emotes.HidesWeapon) || m_Player.SprayWheelOpen;
                if (m_View != null) m_View.SetVisible(!dead && !scoped && !emote);
                float fov = scoped ? GameSession.ScopeFieldOfView :
                    (m_Player.Health.IsSlowed ? 78f : (motor.IsSprinting && motor.IsMoving ? 90f : 85f));
                if (motor.IsLaunched) fov = 92f;
                if (motor.IsSliding && !scoped) fov = 98f; // slide: abre o campo de visão (sensação de velocidade)
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
