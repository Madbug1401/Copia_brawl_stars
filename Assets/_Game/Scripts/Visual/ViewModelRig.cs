using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Estado lido pelo viewmodel em cada frame.</summary>
    public struct ViewFrame
    {
        public float speed;
        public bool grounded;
        public bool sprinting;
        public Vector2 look;
        public bool superReady;
        public float cooldown01;
    }

    /// <summary>
    /// Primeira pessoa: mãos + arma do estilo do Brawler, presas à câmara. Animação procedural de bob, sway,
    /// recuo por cano, recarga (revólver a girar, nova dinamite, puxar a corda, bombear a caçadeira), sprint e Super.
    /// As mãos e antebraços são estilizados (StylizedHands), com as cores dos braços de cada personagem.
    /// </summary>
    public class ViewModelRig
    {
        public Transform Root { get; private set; }
        public RigStyle Style { get; private set; }

        /// <summary>
        /// true = usa os braços do modelo 3D do personagem (IK). Desligado: os modelos gerados a partir de imagens têm
        /// dedos fundidos e não seguram a arma de forma convincente; as mãos estilizadas encaixam sempre.
        /// </summary>
        public static bool UseModelArms = false;

        Transform m_Sway, m_HandR, m_HandL, m_ArmR, m_ArmL, m_GunR, m_GunL;
        Transform m_Knife, m_KnifeHand, m_KnifeArm, m_UpperArmR;
        /// <summary>Arco: comprimento do antebraço que puxa a corda e ombro direito (espaço do balanço).</summary>
        const float BowForearmLength = 0.29f;
        /// <summary>Arco: mão que puxa a corda (raio da "pega" = corda, escala, rotação em relação ao arco e quanto o
        /// pulso se vira para o cotovelo). Públicas para afinar.</summary>
        public static float BowHandRadius = 0.012f, BowHandScale = 1f, BowHandAim = 0f;
        public static Vector3 BowHandEuler = new Vector3(0f, 160f, 0f);   // dedos enganchados na corda, à vista
        public static HandPose BowHandPose = HandPose.Fist;
        static readonly Vector3 BowShoulder = new Vector3(-0.36f, -0.26f, -0.3f);
        Vector3 m_KnifeRest;
        /// <summary>Cotovelo do braço da faca, no espaço do balanço.</summary>
        static readonly Vector3 KnifeElbow = new Vector3(0.42f, -0.72f, 0.05f);
        FirstPersonArms m_ModelArms;
        /// <summary>Braços animados no Blender (Resources/Characters/&lt;id&gt;_fpview), se existirem. Hoje: Mr.S.</summary>
        BakedViewArms m_Baked;
        BakedBowArms m_BakedBow;
        /// <summary>Usa os braços animados no Blender quando o personagem os tiver (desligar volta às mãos estilizadas).</summary>
        public static bool UseBakedArms = true;
        HandPalette m_Palette;
        // mão de apoio (caçadeira/sniper): ponto da arma e rotação da mão em relação à arma
        Transform m_SupportT;
        Quaternion m_SupportRot = Quaternion.identity;
        Quaternion m_HandRRestRot = Quaternion.identity, m_HandLRestRot = Quaternion.identity;
        Vector3 m_HandRRestPos, m_HandLRestPos;
        /// <summary>Quanto cada mão roda à volta do objeto para o pulso apontar ao cotovelo (0..1).</summary>
        float m_AimR = 0.5f, m_AimL = 0.5f;

        /// <summary>Sufixo do prefab dos braços em 1.ª pessoa (Resources/Characters/&lt;brawlerId&gt;_fparms).</summary>
        public const string FirstPersonArmsSuffix = "_fparms";
        /// <summary>Onde ficam os ombros do modelo, no espaço da câmara (abaixo e ligeiramente atrás).</summary>
        static readonly Vector3 ModelShoulders = new Vector3(0f, -0.36f, 0.3f);
        /// <summary>Braços da 1.ª pessoa um pouco maiores, para a mão esquerda chegar à frente da arma.</summary>
        const float ModelArmsScale = 1.12f;
        /// <summary>Animações com a arma nas mãos: onde fica a cabeça do modelo em relação à câmara. Público para afinar.</summary>
        public static Vector3 ModelHeadOffset = new Vector3(0.06f, -0.08f, -0.06f);
        WeaponModel m_WeaponR, m_WeaponL;
        Vector3 m_GunRRest, m_GunLRest;
        Quaternion m_GunRRot = Quaternion.identity, m_GunLRot = Quaternion.identity;
        readonly List<Renderer> m_WeaponRenderers = new List<Renderer>();
        Color m_Accent;

        float m_Bob, m_KickR, m_KickL, m_Throw = 10f, m_Twirl = -1f, m_TwirlSide, m_Pump = 1f, m_Draw = 1f, m_Spray;
        float m_Land, m_SuperPulse, m_Raise, m_Melee = -1f, m_MeleeDuration = 0.3f, m_MeleeSwing;
        /// <summary>Dinamite: mão que lança a seguir (0 = direita) e a que está a lançar agora — alternam.</summary>
        int m_NextThrowSide, m_ThrowSide;
        Vector2 m_SwayV;
        bool m_WasGrounded = true;
        bool m_SuperGlowApplied;
        /// <summary>Cotovelos (fora do ecrã), no espaço do balanço. Públicos para afinar.</summary>
        public static Vector3 ElbowR = new Vector3(0.34f, -0.62f, -0.05f), ElbowL = new Vector3(-0.34f, -0.62f, -0.05f);
        Vector3 m_ElbowR = ElbowR, m_ElbowL = ElbowL;
        int m_Layer;

        // Especial em modo (Minigun do Ekko / tiro fatal armado da Lince)
        bool m_SuperMode;
        float m_Transform;          // 0 = armas normais, 1 = Minigun em posição
        WeaponModel m_Minigun;
        Transform m_MinigunHolder;
        Vector3 m_MinigunRest = new Vector3(0.17f, -0.23f, 0.36f);
        /// <summary>Onde as mãos agarram a Minigun (espaço do suporte): pega traseira e por baixo dos canos.</summary>
        Vector3 m_MinigunGripR = new Vector3(0f, -0.13f, -0.06f), m_MinigunGripL = new Vector3(-0.02f, -0.06f, 0.3f);
        float m_MinigunSpin;

        // Recarga da caçadeira: mete um cartucho e bombeia
        float m_ShellReload = -1f;
        Transform m_Shell;

        static bool s_Prewarmed;

        /// <summary>
        /// Pré-aquece (no menu, um personagem por frame) o que custa na 1.ª vez: carregar os modelos 3D das armas e
        /// gerar as malhas das mãos/braços (ficam em cache), e os visuais dos projéteis com modelo. Assim o spawn
        /// numa partida não engasga (a 1.ª vez com o Ekko chegava a ~1 s).
        /// </summary>
        public static System.Collections.IEnumerator PrewarmRoutine()
        {
            if (s_Prewarmed) yield break;
            s_Prewarmed = true;
            for (int i = 0; i < 10; i++) yield return null;   // deixa o menu/intro arrancar primeiro
            var host = new GameObject("[ViewModelPrewarm]");
            host.transform.position = new Vector3(0f, -500f, 0f);
            foreach (var b in BrawlerRegistry.All)
            {
                if (b == null) continue;
                ViewModelRig rig = null;
                try { rig = Build(b, host.transform, 0); }
                catch (System.Exception e) { Debug.LogException(e); }
                if (rig != null) { rig.Root.gameObject.SetActive(false); rig.Destroy(); }
                yield return null;
            }
            foreach (var style in new[] { ProjectileStyle.Arrow, ProjectileStyle.Tnt, ProjectileStyle.Dynamite })
            {
                try { ProjectileVisuals.Release(ProjectileVisuals.Take(style, Color.white, 1f, host.transform.position, Vector3.forward)); }
                catch (System.Exception e) { Debug.LogException(e); }
                yield return null;
            }
            Object.Destroy(host);
        }

        public static ViewModelRig Build(BrawlerDefinition b, Transform camera, int layer, SkinDefinition skin = null)
        {
            var vm = new ViewModelRig();
            vm.Create(b, camera, layer, skin);
            return vm;
        }

        void Create(BrawlerDefinition b, Transform camera, int layer, SkinDefinition skin)
        {
            Style = b != null ? b.firstPerson.rig : RigStyle.Generic;
            m_Layer = layer;
            m_Accent = skin != null && skin.overrideWeaponColor ? skin.weaponAccent : (b != null ? b.firstPerson.accentColor : Color.white);
            var baseLook = b != null && b.thirdPerson.look != null ? b.thirdPerson.look : new CharacterLook();
            var look = skin != null ? skin.ApplyTo(baseLook) : baseLook;
            m_Palette = StylizedHands.For(b, look);

            Root = new GameObject("ViewModel_" + Style).transform;
            Root.SetParent(camera, false);
            m_Sway = new GameObject("Sway").transform;
            m_Sway.SetParent(Root, false);

            var s = ShadowCastingMode.Off;

            switch (Style)
            {
                case RigStyle.DualRevolvers:
                    m_GunRRest = new Vector3(0.2f, -0.2f, 0.4f);
                    m_GunLRest = new Vector3(-0.2f, -0.2f, 0.4f);
                    m_GunR = Holder("GunR", m_GunRRest);
                    m_GunL = Holder("GunL", m_GunLRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandR = Hand(m_GunR, Grip(m_WeaponR, new Vector3(0f, -0.065f, -0.05f)), HandPose.Pistol, false, 0.022f, new Vector3(18f, 0f, 0f));
                    m_HandL = Hand(m_GunL, Grip(m_WeaponL, new Vector3(0f, -0.065f, -0.05f)), HandPose.Pistol, true, 0.022f, new Vector3(18f, 0f, 0f));
                    break;

                case RigStyle.DualSMG:
                    m_GunRRest = new Vector3(0.19f, -0.19f, 0.38f);
                    m_GunLRest = new Vector3(-0.19f, -0.19f, 0.38f);
                    m_GunR = Holder("SmgR", m_GunRRest);
                    m_GunL = Holder("SmgL", m_GunLRest);
                    m_GunR.localScale = m_GunL.localScale = Vector3.one * 0.85f;
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    // modelo 3D: mão alta no punho (a membrana do polegar encosta à culatra, o indicador chega ao
                    // gatilho) e centrada no punho, que fica ~1.5 cm atrás da origem (medido na malha)
                    var smgGrip = new Vector3(0f, 0.014f, -0.016f);
                    m_HandR = Hand(m_GunR, Grip(m_WeaponR, new Vector3(0f, -0.075f, -0.035f)) + (m_WeaponR.fromModel ? smgGrip : new Vector3(0f, -0.012f, 0f)), HandPose.Pistol, false, 0.028f, new Vector3(22f, 0f, 0f));
                    m_HandL = Hand(m_GunL, Grip(m_WeaponL, new Vector3(0f, -0.075f, -0.035f)) + (m_WeaponL.fromModel ? smgGrip : new Vector3(0f, -0.012f, 0f)), HandPose.Pistol, true, 0.028f, new Vector3(22f, 0f, 0f));
                    break;

                case RigStyle.DualSprayCans:
                    m_GunRRest = new Vector3(0.21f, -0.185f, 0.4f);
                    m_GunLRest = new Vector3(-0.21f, -0.185f, 0.4f);
                    m_GunR = Holder("CanR", m_GunRRest);
                    m_GunL = Holder("CanL", m_GunLRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandR = CanHand(m_GunR, m_WeaponR, false);
                    m_HandL = CanHand(m_GunL, m_WeaponL, true);
                    break;

                case RigStyle.DualExplosives:
                    m_GunRRest = new Vector3(0.22f, -0.2f, 0.4f);
                    m_GunLRest = new Vector3(-0.22f, -0.2f, 0.4f);
                    m_GunR = Holder("StickR", m_GunRRest);
                    m_GunL = Holder("StickL", m_GunLRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandR = ExplosiveHand(m_GunR, m_WeaponR, false);
                    m_HandL = ExplosiveHand(m_GunL, m_WeaponL, true);
                    break;

                case RigStyle.Bow:
                    m_GunLRest = new Vector3(-0.2f, -0.12f, 0.6f);
                    m_GunL = Holder("Bow", m_GunLRest);
                    m_GunL.localScale = Vector3.one * 0.85f;
                    m_GunLRot = Quaternion.Euler(0f, 6f, 16f);
                    m_GunL.localRotation = m_GunLRot;
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    // mão esquerda no punho do arco; a direita fecha-se na corda (segue o nock)
                    m_HandL = Hand(m_GunL, Vector3.zero, HandPose.Fist, true, m_WeaponL.fromModel ? 0.02f : 0.024f, Vector3.zero);
                    m_HandR = Hand(m_Sway, Vector3.zero, BowHandPose, false, BowHandRadius, Vector3.zero);
                    m_HandR.localScale = Vector3.one * BowHandScale;
                    // braço que puxa a corda: cotovelo para fora, à altura da mão (o antebraço sai pela borda do
                    // ecrã; com o cotovelo para trás, o antebraço passava junto à câmara e tapava meio ecrã)
                    m_ElbowR = new Vector3(-0.6f, -0.12f, 0f);
                    break;

                case RigStyle.SprayCan:
                    m_GunRRest = new Vector3(0.21f, -0.2f, 0.4f);
                    m_GunR = Holder("Can", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = CanHand(m_GunR, m_WeaponR, false);
                    break;

                case RigStyle.Shotgun:
                    m_GunRRest = new Vector3(0.2f, -0.215f, 0.32f);
                    m_GunRRot = Quaternion.Euler(3f, -12f, -8f);
                    m_GunR = Holder("Shotgun", m_GunRRest);
                    m_GunR.localScale = Vector3.one * 1.12f;
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, Grip(m_WeaponR, new Vector3(0f, -0.06f, -0.08f)), HandPose.Pistol, false, 0.027f, new Vector3(20f, 0f, 0f));
                    SupportHand(0.03f);
                    break;

                case RigStyle.Sniper:
                    m_GunRRest = new Vector3(0.2f, -0.21f, 0.28f);
                    m_GunRRot = Quaternion.Euler(4f, -14f, -10f);
                    m_GunR = Holder("Sniper", m_GunRRest);
                    m_GunR.localScale = Vector3.one * 0.85f;
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, m_WeaponR != null && m_WeaponR.fromModel ? new Vector3(0f, -0.03f, 0f) : new Vector3(0f, -0.07f, -0.1f), HandPose.Pistol, false, m_WeaponR != null && m_WeaponR.fromModel ? 0.05f : 0.022f, new Vector3(20f, 0f, 0f));
                    SupportHand(0.026f);
                    break;

                default:
                    m_GunRRest = new Vector3(0.18f, -0.2f, 0.36f);
                    m_GunR = Holder("Gun", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, Grip(m_WeaponR, new Vector3(0f, -0.07f, -0.03f)), HandPose.Pistol, false, 0.022f, new Vector3(12f, 0f, 0f));
                    break;
            }
            switch (Style)
            {
                case RigStyle.Shotgun: case RigStyle.Sniper: m_AimR = 0.5f; m_AimL = 1f; break;
                case RigStyle.SprayCan: case RigStyle.DualSprayCans: m_AimR = m_AimL = 0f; break;   // pose fixa: o indicador fica a tapar o furo do bocal
                case RigStyle.DualExplosives: m_AimR = m_AimL = 0.8f; break;
                case RigStyle.Bow: m_AimR = BowHandAim; m_AimL = 0.8f; break;
            }
            if (m_HandR != null) { m_HandRRestRot = m_HandR.localRotation; m_HandRRestPos = m_HandR.localPosition; }
            if (m_HandL != null) { m_HandLRestRot = m_HandL.localRotation; m_HandLRestPos = m_HandL.localPosition; }

            // Braços animados no Blender (Mr.S): substituem as mãos estilizadas; as latas seguem as mãos.
            if (UseBakedArms && Style == RigStyle.DualSprayCans)
            {
                m_Baked = BakedViewArms.TryCreate(b, m_Sway, camera.GetComponent<Camera>(), layer, m_GunR, m_GunL);
                if (m_Baked != null) DestroyStylizedHands();
            }
            // N.Cage: braços animados no Blender com o próprio arco composto e as 3 flechas (substituem o arco e as mãos)
            if (UseBakedArms && Style == RigStyle.Bow)
            {
                m_BakedBow = BakedBowArms.TryCreate(b, m_Sway, camera.GetComponent<Camera>(), layer);
                if (m_BakedBow != null)
                {
                    DestroyStylizedHands();
                    if (m_GunL != null) m_GunL.gameObject.SetActive(false);
                }
            }

            // Braços do próprio modelo (opcional) ou mãos estilizadas com antebraços com as cores do personagem.
            if (m_Baked != null || m_BakedBow != null) { }
            else if (!(UseModelArms && TryModelArms(b)))
            {
                if (m_HandR != null && Style == RigStyle.Bow)
                {
                    // braço que puxa a corda: antebraço com o comprimento real, cotovelo e braço até ao ombro
                    m_ArmR = StylizedHands.BuildForearm(m_Sway, m_Palette, BowForearmLength);
                    m_UpperArmR = StylizedHands.BuildUpperArm(m_Sway, m_Palette);
                }
                else if (m_HandR != null) m_ArmR = StylizedHands.BuildForearm(m_Sway, m_Palette);
                if (m_HandL != null) m_ArmL = StylizedHands.BuildForearm(m_Sway, m_Palette);
            }

            m_Knife = new GameObject("MeleeKnife").transform;
            m_Knife.SetParent(m_Sway, false);
            m_KnifeRest = new Vector3(0.42f, -0.46f, 0.38f);
            var glove = Art.Darken(look.gear, 0.85f);
            WeaponModels.BuildKnife(m_Knife, Art.Darken(glove, 0.45f), m_Accent);
            m_Knife.localPosition = m_KnifeRest;
            if (m_ModelArms != null) m_ModelArms.knife = m_Knife;
            else
            {
                // mão fechada no cabo da faca (eixo do cabo = +Z da faca); o pulso vira-se para o cotovelo no Tick
                m_KnifeHand = Hand(m_Knife, new Vector3(0f, 0f, -0.062f), HandPose.Fist, false, 0.019f, new Vector3(90f, 0f, 0f));
                m_KnifeArm = StylizedHands.BuildForearm(m_Sway, m_Palette);
            }
            m_Knife.gameObject.SetActive(false);

            if (m_BakedBow != null) m_WeaponRenderers.AddRange(m_BakedBow.WeaponRenderers);
            else
                foreach (var w in new[] { m_WeaponR, m_WeaponL })
                    if (w != null) m_WeaponRenderers.AddRange(w.root.GetComponentsInChildren<Renderer>(true));

            foreach (var t in Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in Root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                Art.SetFloat(r, Art.OutlineWidthId, 0.0028f);
            }
            Tick(0f, default);
        }

        /// <summary>Ponto do punho: a origem do modelo 3D (já é o punho) ou o ponto dado para a arma de primitivas.</summary>
        static Vector3 Grip(WeaponModel w, Vector3 primitiveGrip) => w != null && w.fromModel ? Vector3.zero : primitiveGrip;

        Transform Hand(Transform parent, Vector3 local, HandPose pose, bool left, float radius, Vector3 euler, float canTop = StylizedHands.DefaultCanTop)
        {
            var h = StylizedHands.BuildHand(parent, pose, left, radius, m_Palette, canTop);
            h.localPosition = local;
            h.localRotation = Quaternion.Euler(euler);
            return h;
        }

        /// <summary>Lata (modelo 3D): rotação da mão à volta da lata e altura a que a agarra. Públicas para afinar.</summary>
        public static float CanHandYaw = 60f, CanHandLift = 0.04f;
        /// <summary>Lata: quanto o polegar desce (espaço da mão) para ficar no corpo da lata e não no ombro.</summary>
        public static float CanThumbDrop = 0.02f;

        /// <summary>Mão à volta da lata, com o indicador na válvula.</summary>
        Transform CanHand(Transform gun, WeaponModel w, bool left)
        {
            if (w != null && w.fromModel)
            {
                // raio do corpo da lata = escala do rótulo SITA (raio 0.058)
                float radius = 0.056f;
                foreach (var t in w.root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Label") { radius = 0.058f * t.localScale.x * 0.97f; break; }
                float top = 0.09f, valveTop = 0.114f;
                foreach (var t in w.root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Muzzle") { valveTop = w.root.InverseTransformPoint(t.position).y; top = valveTop - 0.025f; break; }
                // O furo do bocal do modelo está virado para trás (para o jogador): a mão agarra a lata por trás e
                // o indicador passa por cima do bocal e tapa o furo. Ponta do dedo no espaço da lata → espaço da mão.
                float yaw = left ? -CanHandYaw : CanHandYaw;
                var pos = new Vector3(0f, CanHandLift, 0f);
                var tip = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f)) * (new Vector3(0f, valveTop - 0.008f, -0.024f) - pos);
                if (left) tip.x = -tip.x;   // a mão esquerda é espelhada
                var h = StylizedHands.BuildHand(gun, HandPose.Can, left, radius, m_Palette, top - CanHandLift, tip, valveTop - CanHandLift, CanThumbDrop);
                h.localPosition = pos;
                h.localRotation = Quaternion.Euler(0f, yaw, 0f);
                return h;
            }
            return Hand(gun, new Vector3(0f, 0.03f, 0.05f), HandPose.Can, left, 0.056f, new Vector3(0f, left ? 40f : -40f, 0f), 0.13f);
        }

        /// <summary>Mão fechada à volta da dinamite (molho de TNT ou pau de primitivas).</summary>
        Transform ExplosiveHand(Transform gun, WeaponModel w, bool left)
        {
            if (w != null && w.fromModel)
            {
                var mf = w.root.GetComponentInChildren<MeshFilter>();
                float radius = mf != null && mf.sharedMesh != null ? Mathf.Min(mf.sharedMesh.bounds.extents.x, mf.sharedMesh.bounds.extents.z) * 0.92f : 0.04f;
                return Hand(gun, Vector3.zero, HandPose.Fist, left, radius, new Vector3(0f, left ? 20f : -20f, 0f));
            }
            // pau de primitivas: inclinado (-20°) e com centro em (0, 0.05, 0.02)
            return Hand(gun, new Vector3(0f, 0.03f, 0.02f), HandPose.Fist, left, 0.04f, new Vector3(-20f, 0f, 0f));
        }

        /// <summary>Mão esquerda por baixo do cano (pump da caçadeira / guarda-mão da sniper).</summary>
        void SupportHand(float radius)
        {
            if (m_WeaponR == null) return;
            m_SupportT = SupportPoint(m_WeaponR);
            if (Style == RigStyle.Sniper && m_WeaponR.fromModel)
            {
                // sniper 3D: mão esquerda à frente do corpo, no cano (fica visível no ecrã)
                m_SupportT = new GameObject("FrontGrip").transform;
                m_SupportT.SetParent(m_WeaponR.root, false);
                m_SupportT.localPosition = new Vector3(0f, 0.092f, 0.43f);
                radius = 0.021f;
            }
            // eixo do cilindro = cano (+Z da arma); dedos sobem pelo lado direito; antebraço vem de baixo/esquerda
            m_SupportRot = Quaternion.LookRotation(new Vector3(-0.75f, 0.66f, 0f), Vector3.forward);
            m_HandL = Hand(m_GunR, m_GunR.InverseTransformPoint(m_SupportT.position), HandPose.Support, true, radius, Vector3.zero);
            m_HandL.localRotation = m_SupportRot;
        }

        /// <summary>
        /// Usa os braços do modelo do personagem (prefab gerado pelo CharacterModelBuilder): as mãos seguram a arma
        /// por IK (direita no punho, esquerda no apoio / 2.ª arma / arco). Devolve false se o personagem não tiver modelo.
        /// Só com <see cref="UseModelArms"/> ligado.
        /// </summary>
        bool TryModelArms(BrawlerDefinition b)
        {
            if (b == null) return false;
            var prefab = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId + FirstPersonArmsSuffix);
            if (prefab == null) return false;
            var go = Object.Instantiate(prefab, Root, false);
            go.name = "ModelArms";
            var anim = go.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) { Object.Destroy(go); return false; }
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // Animações que já seguram a arma (Mixamo "Rifle"): a pose vem da animação e a arma segue as mãos.
            bool animatedHold = m_WeaponR != null && m_GunR != null &&
                (Style == RigStyle.Sniper || Style == RigStyle.Shotgun || Style == RigStyle.Generic);
            if (animatedHold)
            {
                animatedHold = false;
                foreach (var p in anim.parameters) if (p.name == ModelWeaponHolder.HoldsWeaponParam) animatedHold = true;
            }
            if (animatedHold)
            {
                go.transform.SetParent(m_Sway, false);   // balanço e sway da câmara também nos braços
                anim.Rebind();
                anim.Update(0f);
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) go.transform.localPosition += ModelHeadOffset - m_Sway.InverseTransformPoint(head.position);
                DestroyStylizedHands();
                m_ModelArms = anim.gameObject.AddComponent<FirstPersonArms>();
                m_ModelArms.Setup(anim, Root);
                m_GunR.localScale = Vector3.one * 0.8f;
                m_ModelArms.drivenWeapon = m_GunR;
                m_ModelArms.swayRef = m_Sway;
                foreach (var t in m_GunR.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Support") { m_ModelArms.supportLocal = m_GunR.InverseTransformPoint(t.position); break; }
                return true;
            }

            // ombros do modelo no sítio certo em relação à câmara
            go.transform.localScale *= ModelArmsScale;
            anim.Rebind();
            anim.Update(0f);
            var ua = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var ub = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (ua != null && ub != null)
            {
                var mid = Root.InverseTransformPoint((ua.position + ub.position) * 0.5f);
                go.transform.localPosition += ModelShoulders - mid;
            }

            // alvos das mãos
            Transform right = null, left = null;
            switch (Style)
            {
                case RigStyle.DualRevolvers:
                case RigStyle.DualExplosives:
                case RigStyle.DualSMG:
                case RigStyle.DualSprayCans:
                    right = m_WeaponR?.root; left = m_WeaponL?.root; break;
                case RigStyle.Bow:
                    left = m_WeaponL?.root; right = m_WeaponL?.nock; break;
                case RigStyle.SprayCan:
                    right = m_WeaponR?.root; break;
                default:
                    right = m_WeaponR?.root;
                    left = m_WeaponR != null ? SupportPoint(m_WeaponR) : null;
                    break;
            }

            DestroyStylizedHands();
            m_ModelArms = anim.gameObject.AddComponent<FirstPersonArms>();
            m_ModelArms.Setup(anim, Root);
            m_ModelArms.rightTarget = right;
            m_ModelArms.leftTarget = left;
            return true;
        }

        void DestroyStylizedHands()
        {
            if (m_HandR != null) { Object.Destroy(m_HandR.gameObject); m_HandR = null; }
            if (m_HandL != null) { Object.Destroy(m_HandL.gameObject); m_HandL = null; }
        }

        /// <summary>Ponto de apoio da mão esquerda: "Support" do modelo 3D da arma, o pump, ou um ponto no cano.</summary>
        static Transform SupportPoint(WeaponModel w)
        {
            foreach (var t in w.root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Support") return t;
            if (w.pump != null) return w.pump;
            var s = new GameObject("Support").transform;
            s.SetParent(w.root, false);
            s.localPosition = new Vector3(0f, -0.03f, 0.3f);
            return s;
        }

        Transform Holder(string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(m_Sway, false);
            t.localPosition = pos;
            return t;
        }

        // ================================================================== eventos

        public Vector3 GetMuzzle(int shotIndex)
        {
            if (m_SuperMode && m_Minigun != null) return m_Minigun.Muzzle(0);
            if (m_BakedBow != null) return m_BakedBow.Muzzle;
            bool dual = m_WeaponL != null && m_WeaponR != null;
            WeaponModel w;
            // dinamite: o 1.º disparo sai antes de OnAttack, por isso usa a mão "seguinte"
            if (dual && Style == RigStyle.DualExplosives) shotIndex += m_NextThrowSide;
            if (dual) w = (Mathf.Abs(shotIndex) % 2 == 0) ? m_WeaponR : m_WeaponL;
            else w = m_WeaponR ?? m_WeaponL;
            return w != null ? w.Muzzle(0) : Root.position + Root.forward * 0.5f;
        }

        public void OnAttack(bool super)
        {
            switch (Style)
            {
                case RigStyle.DualExplosives: m_Throw = 0f; m_ThrowSide = m_NextThrowSide; m_NextThrowSide ^= 1; break;
                case RigStyle.Bow: m_Draw = 0f; m_KickL = 1f; if (m_BakedBow != null) m_BakedBow.OnAttack(); break;
                case RigStyle.SprayCan:
                case RigStyle.DualSprayCans: m_Spray = super ? 1.2f : 0.65f; if (m_Baked != null) m_Baked.OnSpray(m_Spray); break;
                case RigStyle.Shotgun: m_Pump = 0f; break;
            }
            if (super && !m_SuperMode) m_Raise = 1f;
        }

        /// <summary>Especial em modo: transformação das armas (Ekko) ou mira armada (Lince).</summary>
        public void SetSuperMode(bool on)
        {
            if (on == m_SuperMode || Root == null) return;
            m_SuperMode = on;
            if (Style == RigStyle.DualSMG && on && m_Minigun == null)
            {
                m_MinigunHolder = Holder("Minigun", m_MinigunRest + new Vector3(0f, -0.45f, 0f));
                m_Minigun = WeaponModels.BuildMinigun(m_MinigunHolder, m_Accent, ShadowCastingMode.Off);
                m_MinigunHolder.localScale = Vector3.one * 0.85f;
                if (m_Minigun.fromModel)
                {
                    // modelo 3D: a origem já é a pega traseira; a mão esquerda vai ao marcador "Support"
                    m_MinigunGripR = new Vector3(0f, -0.01f, 0f);
                    foreach (var t in m_Minigun.root.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Support") { m_MinigunGripL = m_MinigunHolder.InverseTransformPoint(t.position); break; }
                }
                foreach (var t in m_MinigunHolder.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = m_Layer;
                foreach (var r in m_MinigunHolder.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    Art.SetFloat(r, Art.OutlineWidthId, 0.0028f);
                    m_WeaponRenderers.Add(r);
                }
            }
        }

        /// <summary>Um disparo da Minigun: os canos aceleram e a arma treme.</summary>
        void OnMinigunShot()
        {
            m_MinigunSpin = 1f;
            m_KickR = 0.5f;
        }

        public void OnMelee(float duration)
        {
            if (m_Knife == null) return;
            m_Knife.gameObject.SetActive(true);
            m_MeleeDuration = Mathf.Max(0.1f, duration);
            m_Melee = 0f;
        }

        public void OnShot(int shotIndex)
        {
            if (m_SuperMode && m_Minigun != null)
            {
                OnMinigunShot();
                return;
            }
            bool dual = m_WeaponL != null && m_WeaponR != null;
            if (dual && Mathf.Abs(shotIndex) % 2 == 1) m_KickL = 1f;
            else m_KickR = 1f;
        }

        /// <summary>Uma carga recarregou: pequena animação (revólver a girar, cartucho na caçadeira, etc.).</summary>
        public void OnChargeReloaded()
        {
            if (m_Baked != null) m_Baked.OnReload();
            if (Style == RigStyle.DualRevolvers && m_Twirl < 0f)
            {
                m_Twirl = 0f;
                m_TwirlSide = m_TwirlSide > 0.5f ? 0f : 1f;
            }
            if (Style == RigStyle.Shotgun && m_ShellReload < 0f && m_WeaponR != null)
            {
                m_ShellReload = 0f;
                if (m_Shell == null)
                {
                    m_Shell = new GameObject("Shell").transform;
                    m_Shell.SetParent(m_GunR, false);
                    Art.Part(PrimitiveType.Capsule, m_Shell, Vector3.zero, new Vector3(0.035f, 0.045f, 0.035f), new Color(0.95f, 0.25f, 0.2f), true,
                        new Vector3(90f, 0f, 0f), ShadowCastingMode.Off);
                    Art.Part(PrimitiveType.Cylinder, m_Shell, new Vector3(0f, 0f, -0.04f), new Vector3(0.038f, 0.012f, 0.038f), new Color(1f, 0.8f, 0.3f), false,
                        new Vector3(90f, 0f, 0f), ShadowCastingMode.Off);
                    foreach (var t in m_Shell.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = m_Layer;
                }
                m_Shell.gameObject.SetActive(true);
            }
        }

        public void SetVisible(bool visible)
        {
            if (Root != null && Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }

        void DestroyMinigun()
        {
            if (m_MinigunHolder == null) return;
            foreach (var r in m_MinigunHolder.GetComponentsInChildren<Renderer>(true)) m_WeaponRenderers.Remove(r);
            Object.Destroy(m_MinigunHolder.gameObject);
            m_MinigunHolder = null;
            m_Minigun = null;
        }

        // ================================================================== animação

        public void Tick(float dt, ViewFrame f)
        {
            if (Root == null) return;
            float time = Time.time;
            // mãos presas à arma voltam à rotação de descanso (a orientação do pulso é recalculada no fim)
            if (m_HandR != null && m_HandR.parent != m_Sway) m_HandR.localRotation = m_HandRRestRot;
            if (m_HandL != null && m_HandL.parent != m_Sway) m_HandL.localRotation = m_HandLRestRot;

            // ---------------- bob, sway e aterragem
            float move01 = Mathf.Clamp01(f.speed / 7f);
            if (f.grounded) m_Bob += dt * (5f + f.speed * 1.1f);
            if (f.grounded && !m_WasGrounded) m_Land = 1f;
            m_WasGrounded = f.grounded;
            m_Land = Mathf.MoveTowards(m_Land, 0f, dt * 4f);

            var targetSway = new Vector2(Mathf.Clamp(-f.look.x * 0.004f, -0.05f, 0.05f), Mathf.Clamp(-f.look.y * 0.004f, -0.04f, 0.04f));
            m_SwayV = Vector2.Lerp(m_SwayV, targetSway, 1f - Mathf.Exp(-10f * dt));

            float bobX = Mathf.Sin(m_Bob) * 0.014f * move01;
            float bobY = -Mathf.Abs(Mathf.Cos(m_Bob)) * 0.012f * move01;
            float idle = Mathf.Sin(time * 1.6f) * 0.004f;
            float sprint = f.sprinting ? 1f : 0f;
            m_Raise = Mathf.MoveTowards(m_Raise, 0f, dt * 2.5f);
            if (m_Melee >= 0f)
            {
                m_Melee += dt;
                float t = Mathf.Clamp01(m_Melee / m_MeleeDuration);
                float swing = Mathf.Sin(t * Mathf.PI);
                m_MeleeSwing = swing;
                m_Knife.localPosition = m_KnifeRest + new Vector3(-0.35f * swing, 0.2f * swing, 0.2f * swing);
                m_Knife.localRotation = Quaternion.Euler(-35f * swing, 0f, -75f * swing);
                if (t >= 1f)
                {
                    m_Melee = -1f;
                    m_MeleeSwing = 0f;
                    m_Knife.localPosition = m_KnifeRest;
                    m_Knife.localRotation = Quaternion.identity;
                    m_Knife.gameObject.SetActive(false);
                }
            }

            m_Sway.localPosition = new Vector3(bobX + m_SwayV.x, bobY + idle + m_SwayV.y - m_Land * 0.03f - sprint * 0.04f + m_Raise * 0.05f, -sprint * 0.03f);
            m_Sway.localRotation = Quaternion.Euler(sprint * 12f - m_Raise * 12f, -sprint * 18f, sprint * 10f + m_SwayV.x * 60f);

            // ---------------- recuo
            m_KickR = Mathf.MoveTowards(m_KickR, 0f, dt * 7f);
            m_KickL = Mathf.MoveTowards(m_KickL, 0f, dt * 7f);

            switch (Style)
            {
                case RigStyle.DualRevolvers:
                {
                    PoseGun(m_GunR, m_GunRRest, m_KickR, 0.06f, 16f, TwirlAngle(0f));
                    PoseGun(m_GunL, m_GunLRest, m_KickL, 0.06f, 16f, TwirlAngle(1f));
                    if (m_Twirl >= 0f)
                    {
                        m_Twirl += dt / 0.4f;
                        if (m_Twirl >= 1f) m_Twirl = -1f;
                    }
                    if (m_WeaponR != null && m_WeaponR.drum != null) m_WeaponR.drum.localRotation *= Quaternion.Euler(0f, 0f, 900f * m_KickR * dt);
                    if (m_WeaponL != null && m_WeaponL.drum != null) m_WeaponL.drum.localRotation *= Quaternion.Euler(0f, 0f, -900f * m_KickL * dt);
                    break;
                }

                case RigStyle.DualSMG:
                {
                    // Transformação: as metralhadoras descem e a Minigun sobe (e o contrário no fim)
                    float target = m_SuperMode ? 1f : 0f;
                    m_Transform = Mathf.MoveTowards(m_Transform, target, dt / 0.35f);
                    float down = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_Transform * 2f));
                    float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(m_Transform * 2f - 1f));
                    var drop = new Vector3(0f, -0.4f * down, -0.08f * down);
                    PoseGun(m_GunR, m_GunRRest + drop, m_KickR * (1f - down), 0.045f, 9f, 0f);
                    PoseGun(m_GunL, m_GunLRest + drop, m_KickL * (1f - down), 0.045f, 9f, 0f);
                    if (m_GunR != null) m_GunR.localRotation *= Quaternion.Euler(down * 40f, 0f, -down * 25f);
                    if (m_GunL != null) m_GunL.localRotation *= Quaternion.Euler(down * 40f, 0f, down * 25f);

                    if (m_MinigunHolder != null)
                    {
                        m_MinigunSpin = Mathf.MoveTowards(m_MinigunSpin, 0f, dt * 1.8f);
                        var shake = new Vector3(Mathf.Sin(time * 77f), Mathf.Cos(time * 69f), 0f) * 0.008f * m_MinigunSpin;
                        m_MinigunHolder.localPosition = m_MinigunRest + new Vector3(0f, -0.45f * (1f - up), -0.03f * m_KickR) + shake;
                        m_MinigunHolder.localRotation = Quaternion.Euler(-25f * (1f - up) - 6f * up - 3f * m_KickR, -16f, -4f);   // ligeiramente de lado: os canos ficam à vista
                        if (m_Minigun.drum != null) m_Minigun.drum.localRotation *= Quaternion.Euler(0f, 0f, (240f + 2200f * m_MinigunSpin) * dt * up);
                        // As mãos passam para a Minigun (pega de trás e por baixo dos canos)
                        if (up > 0.5f && m_HandR != null && m_HandL != null)
                        {
                            m_HandR.SetPositionAndRotation(m_MinigunHolder.TransformPoint(m_MinigunGripR), m_MinigunHolder.rotation * Quaternion.Euler(10f, 0f, 0f));
                            m_HandL.SetPositionAndRotation(m_MinigunHolder.TransformPoint(m_MinigunGripL), m_MinigunHolder.rotation * m_SupportRotMinigun);
                        }
                        else
                        {
                            RestHand(m_HandR, m_HandRRestPos, m_HandRRestRot);
                            RestHand(m_HandL, m_HandLRestPos, m_HandLRestRot);
                        }
                        // Fim do modo: quando as metralhadoras voltaram, a Minigun desaparece
                        if (!m_SuperMode && m_Transform <= 0f) DestroyMinigun();
                    }
                    break;
                }

                case RigStyle.DualSprayCans:
                {
                    m_Spray = Mathf.MoveTowards(m_Spray, 0f, dt);
                    if (m_Baked != null) break;   // as latas seguem as mãos animadas (BakedViewArms)
                    float shake = m_Spray > 0f ? 1f : 0f;
                    var jitterR = new Vector3(Mathf.Sin(time * 70f), Mathf.Cos(time * 63f), 0f) * 0.006f * shake;
                    var jitterL = new Vector3(Mathf.Cos(time * 66f), Mathf.Sin(time * 71f), 0f) * 0.006f * shake;
                    m_GunR.localPosition = m_GunRRest + jitterR + new Vector3(-0.03f, 0.03f, 0.06f) * shake;
                    m_GunL.localPosition = m_GunLRest + jitterL + new Vector3(0.03f, 0.03f, 0.06f) * shake;
                    m_GunR.localRotation = Quaternion.Euler(-6f * shake, -8f * shake, Mathf.Sin(time * 50f) * 6f * shake);
                    m_GunL.localRotation = Quaternion.Euler(-6f * shake, 8f * shake, -Mathf.Sin(time * 47f) * 6f * shake);
                    break;
                }

                case RigStyle.DualExplosives:
                {
                    m_Throw += dt;
                    float t = m_Throw;
                    Vector3 off = Vector3.zero;
                    float rot = 0f;
                    bool hidden = false;
                    if (t < 0.12f) { off = new Vector3(0f, 0.06f, -0.05f) * (t / 0.12f); rot = -30f * (t / 0.12f); }
                    else if (t < 0.25f) { float k = (t - 0.12f) / 0.13f; off = Vector3.Lerp(new Vector3(0f, 0.06f, -0.05f), new Vector3(0f, 0.05f, 0.18f), k); rot = Mathf.Lerp(-30f, 50f, k); }
                    else if (t < 0.55f) { hidden = true; off = new Vector3(0f, -0.25f, 0f); }
                    else if (t < 0.8f) { float k = (t - 0.55f) / 0.25f; off = new Vector3(0f, -0.25f * (1f - k * k), 0f); }
                    // lança uma mão de cada vez (alternam a cada ataque); a outra só acompanha um pouco o balanço
                    bool rightThrows = m_ThrowSide == 0;
                    var gThrow = rightThrows ? m_GunR : m_GunL;
                    var gOther = rightThrows ? m_GunL : m_GunR;
                    gThrow.localPosition = (rightThrows ? m_GunRRest : m_GunLRest) + off;
                    gThrow.localRotation = Quaternion.Euler(rot, 0f, 0f);
                    gOther.localPosition = (rightThrows ? m_GunLRest : m_GunRRest) + (hidden ? new Vector3(0f, 0.012f, 0.01f) : Vector3.zero);
                    gOther.localRotation = Quaternion.identity;
                    SetHeld(rightThrows ? m_WeaponR : m_WeaponL, !hidden);
                    SetHeld(rightThrows ? m_WeaponL : m_WeaponR, true);
                    // Faíscas do rastilho
                    if (dt > 0f && Random.value < dt * 30f)
                    {
                        var w = Random.value < 0.5f ? m_WeaponR : m_WeaponL;
                        if (w != null && w.fuseTip != null && w.arrows != null && w.arrows.gameObject.activeInHierarchy)
                            Fx.Glow(w.fuseTip.position, Random.insideUnitSphere * 0.15f + Vector3.up * 0.2f, new Color(1f, 0.7f, 0.25f), 0.025f, 0.18f);
                    }
                    break;
                }

                case RigStyle.Bow:
                {
                    if (m_BakedBow != null) break;   // braços e arco animados no Blender (BakedBowArms)
                    // Solta → volta a puxar a corda ao longo do cooldown
                    m_Draw = Mathf.MoveTowards(m_Draw, 1f, dt * 2.2f);
                    float pull = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((m_Draw - 0.15f) / 0.85f));
                    WeaponModels.PoseBow(m_WeaponL, pull, m_KickL);
                    m_GunL.localPosition = m_GunLRest + new Vector3(0f, 0f, -0.04f * m_KickL);
                    m_GunL.localRotation = m_GunLRot * Quaternion.Euler(-8f * m_KickL, 0f, 0f);
                    if (m_WeaponL != null && m_WeaponL.arrows != null) m_WeaponL.arrows.gameObject.SetActive(m_Draw > 0.35f);
                    // Mão direita fechada na corda, no nock
                    if (m_WeaponL != null && m_WeaponL.nock != null && m_HandR != null)
                        m_HandR.SetPositionAndRotation(m_WeaponL.nock.position, m_GunL.rotation * Quaternion.Euler(BowHandEuler));
                    break;
                }

                case RigStyle.SprayCan:
                {
                    m_Spray = Mathf.MoveTowards(m_Spray, 0f, dt);
                    float shake = m_Spray > 0f ? 1f : 0f;
                    var jitter = new Vector3(Mathf.Sin(time * 70f), Mathf.Cos(time * 63f), 0f) * 0.006f * shake;
                    m_GunR.localPosition = m_GunRRest + jitter + new Vector3(-0.03f, 0.03f, 0.06f) * shake;
                    m_GunR.localRotation = Quaternion.Euler(-6f * shake, -8f * shake, Mathf.Sin(time * 50f) * 6f * shake);
                    break;
                }

                case RigStyle.Shotgun:
                case RigStyle.Sniper:
                {
                    float kickBack = Style == RigStyle.Sniper ? 0.11f : 0.15f;
                    float kickRot = Style == RigStyle.Sniper ? 18f : 24f;
                    if (m_ModelArms != null && m_ModelArms.drivenWeapon != null) m_ModelArms.kick = m_KickR;   // a arma segue as mãos animadas
                    else
                    {
                        PoseGun(m_GunR, m_GunRRest, m_KickR, kickBack, kickRot, 0f);
                        m_GunR.localRotation = m_GunRRot * m_GunR.localRotation;
                    }
                    if (m_WeaponR != null && m_WeaponR.pump != null)
                    {
                        m_Pump = Mathf.MoveTowards(m_Pump, 1f, dt * 2.2f);
                        float pz = m_Pump > 0.2f && m_Pump < 0.8f ? Mathf.Sin((m_Pump - 0.2f) / 0.6f * Mathf.PI) * -0.09f : 0f;
                        m_WeaponR.pump.localPosition = new Vector3(0f, -0.015f, 0.27f + pz);
                    }
                    // Mão esquerda por baixo do cano (pump / guarda-mão); no modelo 3D, recua um pouco ao bombear
                    if (m_HandL != null && m_SupportT != null)
                    {
                        float pumpBack = m_WeaponR != null && m_WeaponR.pump == null && Style == RigStyle.Shotgun && m_Pump > 0.2f && m_Pump < 0.8f
                            ? Mathf.Sin((m_Pump - 0.2f) / 0.6f * Mathf.PI) * 0.06f : 0f;
                        m_HandL.SetPositionAndRotation(m_SupportT.position - m_GunR.forward * pumpBack * m_GunR.lossyScale.z, m_GunR.rotation * m_SupportRot);
                    }
                    // Recarga da caçadeira: a mão esquerda mete um cartucho por baixo e depois bombeia
                    if (Style == RigStyle.Shotgun && m_ShellReload >= 0f)
                    {
                        m_ShellReload += dt / 0.6f;
                        float t = m_ShellReload;
                        if (t < 0.5f)
                        {
                            float k = Mathf.SmoothStep(0f, 1f, t / 0.5f);
                            var from = m_GunR.TransformPoint(new Vector3(0.02f, -0.22f, 0.05f));
                            var to = m_GunR.TransformPoint(new Vector3(0f, -0.08f, 0.12f));
                            if (m_HandL != null) m_HandL.position = Vector3.Lerp(from, to, k);
                            if (m_Shell != null) m_Shell.position = (m_HandL != null ? m_HandL.position : to) + m_GunR.TransformDirection(new Vector3(0f, 0.05f, 0.02f));
                            m_GunR.localRotation *= Quaternion.Euler(0f, 0f, -18f * Mathf.Sin(k * Mathf.PI * 0.5f));
                        }
                        else
                        {
                            if (m_Shell != null && m_Shell.gameObject.activeSelf)
                            {
                                m_Shell.gameObject.SetActive(false);
                                m_Pump = 0f; // bombeia depois de meter o cartucho
                                // PlayerCombat já toca a recarga desta carga.
                                // A animação não deve repetir o clip completo.
                            }
                            m_GunR.localRotation *= Quaternion.Euler(0f, 0f, -18f * Mathf.Clamp01((1f - t) * 2f));
                        }
                        if (t >= 1f) m_ShellReload = -1f;
                    }
                    if (m_WeaponR != null && m_WeaponR.scope != null && Style == RigStyle.Sniper)
                    {
                        // Tiro fatal armado: a lente brilha a vermelho
                        var glow = m_SuperMode
                            ? new Color(1f, 0.12f, 0.08f) * (0.7f + 0.3f * Mathf.Sin(time * 9f))
                            : m_Accent * (0.4f + 0.3f * Mathf.Sin(time * 3f));
                        foreach (var g in m_WeaponR.glowRenderers) Art.SetEmission(g, glow);
                    }
                    break;
                }

                default:
                    PoseGun(m_GunR, m_GunRRest, m_KickR, 0.06f, 12f, 0f);
                    break;
            }

            // ---------------- faca: a arma da direita baixa enquanto a mão direita dá o golpe
            if (m_KnifeHand != null)
            {
                if (m_Baked != null) m_Baked.SetRightDrop(m_MeleeSwing * 1.6f);
                else if (m_BakedBow != null) m_BakedBow.SetDrop(m_MeleeSwing * 1.6f);
                else if (m_MeleeSwing > 0f && m_GunR != null && Style != RigStyle.Bow)
                    m_GunR.localPosition += new Vector3(0.04f, -0.32f, -0.06f) * Mathf.Clamp01(m_MeleeSwing * 1.6f);
                // arco: a mão direita (da corda) é a que pega na faca
                bool knifeOut = m_Knife.gameObject.activeSelf;
                if (Style == RigStyle.Bow && m_HandR != null && m_HandR.gameObject.activeSelf == knifeOut) m_HandR.gameObject.SetActive(!knifeOut);
                var ke = m_Sway.TransformPoint(KnifeElbow);
                AimWrist(m_KnifeHand, ke, 1f);
                StylizedHands.PoseForearm(m_KnifeArm, m_KnifeHand, ke);
            }

            // ---------------- pulsos virados para os cotovelos, depois os antebraços (fora do ecrã)
            AimWrist(m_HandR, m_Sway.TransformPoint(m_ElbowR), m_AimR);
            AimWrist(m_HandL, m_Sway.TransformPoint(m_ElbowL), m_AimL);
            if (m_UpperArmR != null && m_HandR != null)
            {
                // arco: o cotovelo fica a um antebraço do pulso, na direção do ponto de referência — recua quando
                // a corda é puxada; o braço vai do cotovelo ao ombro
                var wrist = m_HandR.Find("Wrist");
                var wp = wrist != null ? wrist.position : m_HandR.position;
                var dir = (m_Sway.TransformPoint(m_ElbowR) - wp).normalized;
                var elbow = wp + dir * (BowForearmLength * m_Sway.lossyScale.x);
                StylizedHands.PoseForearm(m_ArmR, m_HandR, elbow);
                StylizedHands.PoseLimb(m_UpperArmR, elbow, m_Sway.TransformPoint(BowShoulder), m_Sway.up);
                if (m_UpperArmR.gameObject.activeSelf != m_HandR.gameObject.activeInHierarchy) m_UpperArmR.gameObject.SetActive(m_HandR.gameObject.activeInHierarchy);
            }
            else StylizedHands.PoseForearm(m_ArmR, m_HandR, m_Sway.TransformPoint(m_ElbowR));
            StylizedHands.PoseForearm(m_ArmL, m_HandL, m_Sway.TransformPoint(m_ElbowL));

            // ---------------- Super pronto: arma brilha a pulsar
            if (f.superReady)
            {
                m_SuperPulse += dt;
                float p = 0.25f + 0.25f * Mathf.Sin(m_SuperPulse * 6f);
                var c = Color.Lerp(m_Accent, new Color(1f, 0.85f, 0.3f), 0.5f) * p;
                foreach (var r in m_WeaponRenderers) Art.SetEmission(r, c);
                m_SuperGlowApplied = true;
            }
            else if (m_SuperGlowApplied)
            {
                foreach (var r in m_WeaponRenderers) Art.SetEmission(r, Color.black);
                m_SuperGlowApplied = false;
            }
        }

        /// <summary>Mão esquerda na Minigun: por baixo dos canos, palma para cima.</summary>
        static readonly Quaternion m_SupportRotMinigun = Quaternion.LookRotation(new Vector3(0.75f, 0.66f, 0f), Vector3.forward);

        static readonly Vector3 HandBack = new Vector3(Mathf.Sin(18f * Mathf.Deg2Rad), 0f, -Mathf.Cos(18f * Mathf.Deg2Rad));

        /// <summary>
        /// Roda a mão à volta do eixo do objeto agarrado (mantém a pega) para o pulso apontar para o cotovelo,
        /// para o antebraço sair da mão sem a atravessar.
        /// </summary>
        static void AimWrist(Transform hand, Vector3 elbowWorld, float weight)
        {
            if (hand == null || weight <= 0f) return;
            bool left = hand.localScale.x < 0f;
            var axis = hand.up;
            var e = elbowWorld - hand.position;
            e -= axis * Vector3.Dot(e, axis);
            if (e.sqrMagnitude < 1e-6f) return;
            var back = new Vector3(left ? -HandBack.x : HandBack.x, 0f, HandBack.z);
            var target = Quaternion.LookRotation(e.normalized, axis) * Quaternion.Inverse(Quaternion.LookRotation(back, Vector3.up));
            hand.rotation = Quaternion.Slerp(hand.rotation, target, weight);
        }

        /// <summary>Volta a pôr a mão no punho da arma (depois de ter ido à Minigun).</summary>
        static void RestHand(Transform hand, Vector3 localPos, Quaternion localRot)
        {
            if (hand == null) return;
            hand.localPosition = localPos;
            hand.localRotation = localRot;
        }

        float TwirlAngle(float side)
        {
            if (m_Twirl < 0f || Mathf.Abs(m_TwirlSide - side) > 0.1f) return 0f;
            return Mathf.SmoothStep(0f, 1f, m_Twirl) * 360f;
        }

        static void PoseGun(Transform gun, Vector3 rest, float kick, float back, float rot, float twirl)
        {
            if (gun == null) return;
            gun.localPosition = rest + new Vector3(0f, 0.015f * kick, -back * kick);
            gun.localRotation = Quaternion.Euler(-rot * kick - twirl, 0f, 0f);
        }

        static void SetHeld(WeaponModel w, bool visible)
        {
            if (w == null || w.arrows == null) return;
            if (w.arrows.gameObject.activeSelf != visible) w.arrows.gameObject.SetActive(visible);
        }
    }
}
