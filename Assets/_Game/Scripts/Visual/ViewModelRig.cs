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
    /// </summary>
    public class ViewModelRig
    {
        public Transform Root { get; private set; }
        public RigStyle Style { get; private set; }

        Transform m_Sway, m_HandR, m_HandL, m_ArmR, m_ArmL, m_GunR, m_GunL;
        WeaponModel m_WeaponR, m_WeaponL;
        Vector3 m_GunRRest, m_GunLRest;
        Quaternion m_GunRRot = Quaternion.identity, m_GunLRot = Quaternion.identity;
        readonly List<Renderer> m_WeaponRenderers = new List<Renderer>();
        Color m_Accent;

        float m_Bob, m_KickR, m_KickL, m_Throw = 10f, m_Twirl = -1f, m_TwirlSide, m_Pump = 1f, m_Draw = 1f, m_Spray;
        float m_Land, m_SuperPulse, m_Raise;
        Vector2 m_SwayV;
        bool m_WasGrounded = true;
        bool m_SuperGlowApplied;
        Vector3 m_ArmAnchorR = new Vector3(0.36f, -0.55f, 0.05f), m_ArmAnchorL = new Vector3(-0.36f, -0.55f, 0.05f);

        public static ViewModelRig Build(BrawlerDefinition b, Transform camera, int layer)
        {
            var vm = new ViewModelRig();
            vm.Create(b, camera, layer);
            return vm;
        }

        void Create(BrawlerDefinition b, Transform camera, int layer)
        {
            Style = b != null ? b.firstPerson.rig : RigStyle.Generic;
            m_Accent = b != null ? b.firstPerson.accentColor : Color.white;
            var look = b != null && b.thirdPerson.look != null ? b.thirdPerson.look : new CharacterLook();

            Root = new GameObject("ViewModel_" + Style).transform;
            Root.SetParent(camera, false);
            m_Sway = new GameObject("Sway").transform;
            m_Sway.SetParent(Root, false);

            var s = ShadowCastingMode.Off;
            var glove = Art.Darken(look.gear, 0.85f);
            var sleeve = look.outfit;

            switch (Style)
            {
                case RigStyle.DualRevolvers:
                    m_GunRRest = new Vector3(0.2f, -0.2f, 0.4f);
                    m_GunLRest = new Vector3(-0.2f, -0.2f, 0.4f);
                    m_GunR = Holder("GunR", m_GunRRest);
                    m_GunL = Holder("GunL", m_GunLRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, -0.05f, -0.03f), glove);
                    m_HandL = Hand(m_GunL, new Vector3(0f, -0.05f, -0.03f), glove);
                    break;

                case RigStyle.DualExplosives:
                    m_GunRRest = new Vector3(0.23f, -0.23f, 0.38f);
                    m_GunLRest = new Vector3(-0.23f, -0.23f, 0.38f);
                    m_GunR = Holder("StickR", m_GunRRest);
                    m_GunL = Holder("StickL", m_GunLRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, 0f, 0f), glove);
                    m_HandL = Hand(m_GunL, new Vector3(0f, 0f, 0f), glove);
                    break;

                case RigStyle.Bow:
                    m_GunLRest = new Vector3(-0.2f, -0.12f, 0.6f);
                    m_GunL = Holder("Bow", m_GunLRest);
                    m_GunL.localScale = Vector3.one * 0.85f;
                    m_GunLRot = Quaternion.Euler(0f, 6f, 16f);
                    m_GunL.localRotation = m_GunLRot;
                    m_WeaponL = WeaponModels.Build(Style, m_GunL, m_Accent, 1, s);
                    m_HandL = Hand(m_GunL, Vector3.zero, glove);
                    m_HandR = Hand(m_Sway, new Vector3(0.02f, -0.15f, 0.3f), glove);
                    m_HandR.localScale = Vector3.one * 0.7f;
                    break;

                case RigStyle.SprayCan:
                    m_GunRRest = new Vector3(0.21f, -0.24f, 0.4f);
                    m_GunR = Holder("Can", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, 0.03f, 0f), glove);
                    break;

                case RigStyle.Shotgun:
                    m_GunRRest = new Vector3(0.21f, -0.25f, 0.3f);
                    m_GunR = Holder("Shotgun", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, -0.06f, -0.06f), glove);
                    m_HandL = Hand(m_Sway, Vector3.zero, glove);
                    break;

                case RigStyle.Sniper:
                    m_GunRRest = new Vector3(0.2f, -0.24f, 0.28f);
                    m_GunR = Holder("Sniper", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, -0.06f, -0.08f), glove);
                    m_HandL = Hand(m_Sway, Vector3.zero, glove);
                    break;

                default:
                    m_GunRRest = new Vector3(0.18f, -0.2f, 0.36f);
                    m_GunR = Holder("Gun", m_GunRRest);
                    m_WeaponR = WeaponModels.Build(Style, m_GunR, m_Accent, 0, s);
                    m_HandR = Hand(m_GunR, new Vector3(0f, -0.06f, -0.02f), glove);
                    break;
            }

            if (m_HandR != null) m_ArmR = Forearm(sleeve);
            if (m_HandL != null) m_ArmL = Forearm(sleeve);

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

        Transform Holder(string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(m_Sway, false);
            t.localPosition = pos;
            return t;
        }

        Transform Hand(Transform parent, Vector3 local, Color glove)
        {
            var h = new GameObject("Hand").transform;
            h.SetParent(parent, false);
            h.localPosition = local;
            Art.Part(PrimitiveType.Sphere, h, Vector3.zero, new Vector3(0.085f, 0.075f, 0.1f), glove, true, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Capsule, h, new Vector3(0.025f, 0.02f, 0.03f), new Vector3(0.03f, 0.035f, 0.03f), glove, true, new Vector3(60f, 0f, -30f), ShadowCastingMode.Off);
            return h;
        }

        Transform Forearm(Color sleeve)
        {
            var arm = new GameObject("Forearm").transform;
            arm.SetParent(m_Sway, false);
            Art.Part(PrimitiveType.Capsule, arm, Vector3.zero, new Vector3(0.075f, 0.5f, 0.075f), sleeve, true, default, ShadowCastingMode.Off);
            Art.Part(PrimitiveType.Cylinder, arm, new Vector3(0f, 0.4f, 0f), new Vector3(0.085f, 0.03f, 0.085f), Art.Darken(sleeve, 0.7f), true, default, ShadowCastingMode.Off);
            return arm;
        }

        // ================================================================== eventos

        public Vector3 GetMuzzle(int shotIndex)
        {
            bool dual = m_WeaponL != null && m_WeaponR != null;
            WeaponModel w;
            if (dual) w = (Mathf.Abs(shotIndex) % 2 == 0) ? m_WeaponR : m_WeaponL;
            else w = m_WeaponR ?? m_WeaponL;
            return w != null ? w.Muzzle(0) : Root.position + Root.forward * 0.5f;
        }

        public void OnAttack(bool super)
        {
            switch (Style)
            {
                case RigStyle.DualExplosives: m_Throw = 0f; break;
                case RigStyle.Bow: m_Draw = 0f; m_KickL = 1f; break;
                case RigStyle.SprayCan: m_Spray = super ? 1.2f : 0.65f; break;
                case RigStyle.Shotgun: m_Pump = 0f; break;
            }
            if (super) m_Raise = 1f;
        }

        public void OnShot(int shotIndex)
        {
            bool dual = m_WeaponL != null && m_WeaponR != null;
            if (dual && Mathf.Abs(shotIndex) % 2 == 1) m_KickL = 1f;
            else m_KickR = 1f;
        }

        /// <summary>Uma carga recarregou: pequena animação (revólver a girar, etc.).</summary>
        public void OnChargeReloaded()
        {
            if (Style == RigStyle.DualRevolvers && m_Twirl < 0f)
            {
                m_Twirl = 0f;
                m_TwirlSide = m_TwirlSide > 0.5f ? 0f : 1f;
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

        // ================================================================== animação

        public void Tick(float dt, ViewFrame f)
        {
            if (Root == null) return;
            float time = Time.time;

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
                    var offL = t > 0.08f && t < 0.88f ? off : Vector3.zero;
                    m_GunR.localPosition = m_GunRRest + off;
                    m_GunR.localRotation = Quaternion.Euler(rot, 0f, 0f);
                    m_GunL.localPosition = m_GunLRest + offL;
                    m_GunL.localRotation = Quaternion.Euler(t > 0.08f && t < 0.33f ? rot : 0f, 0f, 0f);
                    SetHeld(m_WeaponR, !hidden);
                    SetHeld(m_WeaponL, !(t > 0.33f && t < 0.6f));
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
                    // Solta → volta a puxar a corda ao longo do cooldown
                    m_Draw = Mathf.MoveTowards(m_Draw, 1f, dt * 2.2f);
                    float pull = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((m_Draw - 0.15f) / 0.85f));
                    WeaponModels.PoseBow(m_WeaponL, pull, m_KickL);
                    m_GunL.localPosition = m_GunLRest + new Vector3(0f, 0f, -0.04f * m_KickL);
                    m_GunL.localRotation = m_GunLRot * Quaternion.Euler(-8f * m_KickL, 0f, 0f);
                    if (m_WeaponL != null && m_WeaponL.arrows != null) m_WeaponL.arrows.gameObject.SetActive(m_Draw > 0.35f);
                    // Mão direita segue o nock
                    if (m_WeaponL != null && m_WeaponL.nock != null && m_HandR != null)
                        m_HandR.position = m_WeaponL.nock.position + Root.TransformDirection(new Vector3(0.025f, -0.02f, -0.02f));
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
                    float kickBack = Style == RigStyle.Sniper ? 0.11f : 0.09f;
                    float kickRot = Style == RigStyle.Sniper ? 18f : 14f;
                    PoseGun(m_GunR, m_GunRRest, m_KickR, kickBack, kickRot, 0f);
                    if (m_WeaponR != null && m_WeaponR.pump != null)
                    {
                        m_Pump = Mathf.MoveTowards(m_Pump, 1f, dt * 2.2f);
                        float pz = m_Pump > 0.2f && m_Pump < 0.8f ? Mathf.Sin((m_Pump - 0.2f) / 0.6f * Mathf.PI) * -0.09f : 0f;
                        m_WeaponR.pump.localPosition = new Vector3(0f, -0.015f, 0.27f + pz);
                    }
                    // Mão esquerda a segurar o pump/cano
                    if (m_HandL != null && m_WeaponR != null)
                    {
                        var target = m_WeaponR.pump != null
                            ? m_WeaponR.pump.position + m_GunR.TransformDirection(new Vector3(0f, -0.03f, 0f))
                            : m_GunR.TransformPoint(new Vector3(0f, -0.03f, 0.3f));
                        m_HandL.position = target;
                    }
                    if (m_WeaponR != null && m_WeaponR.scope != null && Style == RigStyle.Sniper)
                        foreach (var g in m_WeaponR.glowRenderers) Art.SetEmission(g, m_Accent * (0.4f + 0.3f * Mathf.Sin(time * 3f)));
                    break;
                }

                default:
                    PoseGun(m_GunR, m_GunRRest, m_KickR, 0.06f, 12f, 0f);
                    break;
            }

            // ---------------- antebraços (das mãos para fora do ecrã)
            PlaceForearm(m_ArmR, m_HandR, m_ArmAnchorR);
            PlaceForearm(m_ArmL, m_HandL, m_ArmAnchorL);

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

        void PlaceForearm(Transform arm, Transform hand, Vector3 anchor)
        {
            if (arm == null || hand == null) return;
            var handLocal = m_Sway.InverseTransformPoint(hand.position);
            var dir = handLocal - anchor;
            float len = dir.magnitude;
            if (len < 0.001f) return;
            arm.localPosition = anchor + dir * 0.5f - dir / len * 0.04f;
            arm.localRotation = Quaternion.FromToRotation(Vector3.up, dir / len);
            arm.localScale = new Vector3(1f, Mathf.Max(0.2f, len * 0.95f), 1f);
        }

        static void SetHeld(WeaponModel w, bool visible)
        {
            if (w == null || w.arrows == null) return;
            if (w.arrows.gameObject.activeSelf != visible) w.arrows.gameObject.SetActive(visible);
        }
    }
}
