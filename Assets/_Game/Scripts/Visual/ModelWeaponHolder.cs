using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Segura a arma (de primitivas) com as mãos de um modelo humanoide, por IK.
    /// A arma é posicionada à frente do peito, apontada para onde o jogador olha (pitch), com coice ao disparar;
    /// as mãos vão ter à arma (mão direita ao punho, esquerda ao cano nas armas de duas mãos).
    /// Assim as animações (paradas, a correr, a saltar) não precisam de saber que arma o personagem tem.
    /// Adicionado em runtime pelo <see cref="CharacterRig"/> ao objeto do Animator (o controller precisa de "IK Pass").
    /// </summary>
    public class ModelWeaponHolder : MonoBehaviour
    {
        enum Grip { TwoHanded, OneHandedRight, Dual, OneHandedLeft }

        Animator m_Anim;
        Transform m_Root, m_WeaponR, m_WeaponL;
        Grip m_Grip;
        float m_SupportZ;            // ponto do cano onde pousa a mão esquerda (espaço local da arma)
        Vector3 m_SupportLocal;      // ponto "Support" do modelo 3D da arma, se existir
        float m_KickR, m_KickL;
        float m_Weight;
        bool m_FromHands;            // animações já seguram a arma (ex.: Mixamo "Rifle"): a arma segue as mãos

        /// <summary>Parâmetro do Animator que indica animações com a arma nas mãos (criado pelo CharacterModelBuilder).</summary>
        public const string HoldsWeaponParam = "HoldsWeapon";
        /// <summary>Do pulso (alvo de IK da mão direita) ao punho da arma, no espaço da arma. Público para afinar em Play.</summary>
        public static Vector3 GripFromWrist = new Vector3(0f, -0.05f, 0.04f);

        /// <summary>Inclinação da mira em graus (positivo = a olhar para baixo).</summary>
        [System.NonSerialized] public float pitch;
        /// <summary>false enquanto morto: larga a arma (IK desliga e a arma esconde-se).</summary>
        [System.NonSerialized] public bool holding = true;

        // Pontos de referência, em metros, no espaço do personagem (x = direita, y = cima, z = frente),
        // medidos a partir do centro do corpo do Animator (bodyPosition, à altura da anca).
        static readonly Vector3 ShoulderR = new Vector3(0.13f, 0.40f, 0.02f);
        static readonly Vector3 ShoulderL = new Vector3(-0.13f, 0.40f, 0.02f);

        public void Setup(Animator anim, Transform root, Transform weaponR, Transform weaponL, RigStyle style)
        {
            m_Anim = anim;
            m_Root = root;
            m_WeaponR = weaponR;
            m_WeaponL = weaponL;
            switch (style)
            {
                case RigStyle.DualSMG:
                case RigStyle.DualSprayCans:
                case RigStyle.DualRevolvers:
                case RigStyle.DualExplosives: m_Grip = Grip.Dual; break;
                case RigStyle.Bow: m_Grip = Grip.OneHandedLeft; break;
                case RigStyle.SprayCan: m_Grip = Grip.OneHandedRight; break;
                default: m_Grip = Grip.TwoHanded; break;
            }
            m_SupportZ = style == RigStyle.Sniper ? 0.2f : style == RigStyle.Shotgun ? 0.16f : 0.1f;
            m_SupportLocal = new Vector3(0f, -0.02f, m_SupportZ);
            if (weaponR != null)
                foreach (var t in weaponR.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Support") { m_SupportLocal = weaponR.InverseTransformPoint(t.position); break; }
            m_Weight = 1f;
            foreach (var p in anim.parameters) if (p.name == HoldsWeaponParam) m_FromHands = m_Grip == Grip.TwoHanded;
        }

        /// <summary>
        /// Arma presa à mão direita da animação, mas sempre a apontar para a mira (aimFwd); a mão esquerda vai ao
        /// ponto de apoio da arma. Roda o conjunto à volta do peito para olhar para cima/baixo e aplica o coice.
        /// Devolve os alvos de IK das duas mãos. Partilhado com a vista em 1.ª pessoa.
        /// </summary>
        public static void HoldFromHands(Animator anim, Transform weapon, Vector3 supportLocal, Vector3 pivot, Quaternion pitchRot,
            Vector3 aimFwd, Vector3 upRef, float kick, out Vector3 rPos, out Quaternion rRot, out Vector3 lPos, out Quaternion lRot, Vector3 handOffset = default)
        {
            rPos = anim.GetIKPosition(AvatarIKGoal.RightHand);
            rPos = pivot + pitchRot * (rPos - pivot) + handOffset;

            var fwd = (pitchRot * aimFwd).normalized;
            var up = Vector3.ProjectOnPlane(upRef, fwd);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            var rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(-10f * kick, 0f, 0f);
            rRot = rot * Quaternion.Euler(30f, -45f, -90f);
            lRot = rot * Quaternion.Euler(20f, -60f, 180f);
            rPos += -fwd * 0.06f * kick + up.normalized * 0.01f * kick;   // coice
            // O apoio foi medido no espaço local da arma, que pode estar escalada.
            var supportOffset = weapon != null ? Vector3.Scale(supportLocal, weapon.lossyScale) : supportLocal;
            var gripOffset = rot * GripFromWrist;
            var leftOffset = gripOffset + rot * supportOffset;
            // As poses de corrida podem afastar o pulso direito do alcance do braço esquerdo.
            // Ajusta o conjunto às duas cadeias, mantendo o cano alinhado com a mira.
            for (int i = 0; i < 4; i++)
            {
                rPos += ReachCorrection(anim, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand, rPos + leftOffset);
                rPos += ReachCorrection(anim, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand, rPos);
            }
            var pos = rPos + gripOffset;
            if (weapon != null) weapon.SetPositionAndRotation(pos, rot);
            lPos = pos + rot * supportOffset;
        }

        static Vector3 ReachCorrection(Animator anim, HumanBodyBones upper, HumanBodyBones lower,
            HumanBodyBones hand, Vector3 target)
        {
            var shoulder = anim.GetBoneTransform(upper);
            var elbow = anim.GetBoneTransform(lower);
            var wrist = anim.GetBoneTransform(hand);
            if (shoulder == null || elbow == null || wrist == null) return Vector3.zero;
            float reach = (Vector3.Distance(shoulder.position, elbow.position) +
                Vector3.Distance(elbow.position, wrist.position)) * 0.97f;
            var delta = target - shoulder.position;
            return shoulder.position + Vector3.ClampMagnitude(delta, reach) - target;
        }

        /// <summary>Coice de um disparo. hand: 0 direita, 1 esquerda, -1 ambas.</summary>
        public void Kick(float amount, int hand)
        {
            if (hand != 1) m_KickR = Mathf.Max(m_KickR, amount);
            if (hand != 0) m_KickL = Mathf.Max(m_KickL, amount);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || m_Anim == null || m_Root == null) return;
            float dt = Time.deltaTime;
            m_Weight = Mathf.MoveTowards(m_Weight, holding ? 1f : 0f, dt * 6f);
            m_KickR = Mathf.MoveTowards(m_KickR, 0f, dt * 7f);
            m_KickL = Mathf.MoveTowards(m_KickL, 0f, dt * 7f);
            SetActive(m_WeaponR, holding);
            SetActive(m_WeaponL, holding);

            var body = m_Anim.bodyPosition;
            var yaw = m_Root.rotation;
            var aim = yaw * Quaternion.Euler(Mathf.Clamp(pitch, -55f, 55f) * 0.8f, 0f, 0f);

            if (m_FromHands)
            {
                var chest = m_Anim.GetBoneTransform(HumanBodyBones.Chest);
                var pivot = chest != null ? chest.position : body + Vector3.up * 0.4f;
                var pr = Quaternion.AngleAxis(Mathf.Clamp(pitch, -55f, 55f) * 0.85f, yaw * Vector3.right);
                HoldFromHands(m_Anim, m_WeaponR, m_SupportLocal, pivot, pr, yaw * Vector3.forward, yaw * Vector3.up, m_KickR, out var rp, out var rr, out var lp, out var lr);
                foreach (var g in new[] { AvatarIKGoal.RightHand, AvatarIKGoal.LeftHand })
                {
                    m_Anim.SetIKPositionWeight(g, m_Weight);
                    m_Anim.SetIKRotationWeight(g, m_Weight);
                }
                m_Anim.SetIKPosition(AvatarIKGoal.RightHand, rp); m_Anim.SetIKRotation(AvatarIKGoal.RightHand, rr);
                m_Anim.SetIKPosition(AvatarIKGoal.LeftHand, lp); m_Anim.SetIKRotation(AvatarIKGoal.LeftHand, lr);
                // a cabeça acompanha a mira
                var head = m_Anim.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    m_Anim.SetLookAtWeight(m_Weight, 0f, 0.7f, 0f, 0.5f);
                    m_Anim.SetLookAtPosition(head.position + aim * Vector3.forward * 10f);
                }
                return;
            }

            switch (m_Grip)
            {
                case Grip.TwoHanded:
                    // espingarda ao ombro direito, a apontar para a mira
                    PlaceWeapon(m_WeaponR, body + yaw * ShoulderR, aim, new Vector3(-0.05f, -0.12f, 0.25f), m_KickR);
                    HandTo(AvatarIKGoal.RightHand, m_WeaponR, Vector3.zero);
                    HandTo(AvatarIKGoal.LeftHand, m_WeaponR, m_SupportLocal);
                    Elbow(AvatarIKHint.RightElbow, body + yaw * new Vector3(0.38f, 0.12f, -0.12f));
                    Elbow(AvatarIKHint.LeftElbow, body + yaw * new Vector3(-0.28f, 0.1f, 0.1f));
                    break;
                case Grip.OneHandedRight:
                    PlaceWeapon(m_WeaponR, body + yaw * ShoulderR, aim, new Vector3(0.1f, -0.16f, 0.36f), m_KickR);
                    HandTo(AvatarIKGoal.RightHand, m_WeaponR, Vector3.zero);
                    m_Anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                    Elbow(AvatarIKHint.RightElbow, body + yaw * new Vector3(0.4f, 0.1f, -0.05f));
                    break;
                case Grip.OneHandedLeft:
                    PlaceWeapon(m_WeaponL, body + yaw * ShoulderL, aim, new Vector3(-0.08f, -0.12f, 0.4f), m_KickL);
                    HandTo(AvatarIKGoal.LeftHand, m_WeaponL, Vector3.zero);
                    m_Anim.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                    Elbow(AvatarIKHint.LeftElbow, body + yaw * new Vector3(-0.4f, 0.1f, -0.05f));
                    break;
                case Grip.Dual:
                    PlaceWeapon(m_WeaponR, body + yaw * ShoulderR, aim, new Vector3(0.12f, -0.18f, 0.36f), m_KickR);
                    PlaceWeapon(m_WeaponL, body + yaw * ShoulderL, aim, new Vector3(-0.12f, -0.18f, 0.36f), m_KickL);
                    HandTo(AvatarIKGoal.RightHand, m_WeaponR, Vector3.zero);
                    HandTo(AvatarIKGoal.LeftHand, m_WeaponL, Vector3.zero);
                    Elbow(AvatarIKHint.RightElbow, body + yaw * new Vector3(0.4f, 0.1f, -0.05f));
                    Elbow(AvatarIKHint.LeftElbow, body + yaw * new Vector3(-0.4f, 0.1f, -0.05f));
                    break;
            }
        }

        static void SetActive(Transform t, bool on)
        {
            if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
        }

        /// <summary>Arma presa a um "ombro" e rodada com a mira; o coice empurra-a para trás e levanta o cano.</summary>
        void PlaceWeapon(Transform weapon, Vector3 shoulder, Quaternion aim, Vector3 offset, float kick)
        {
            if (weapon == null) return;
            var pos = shoulder + aim * (offset + new Vector3(0f, 0.01f, -0.07f) * kick);
            var rot = aim * Quaternion.Euler(-10f * kick, 0f, 0f);
            weapon.SetPositionAndRotation(pos, rot);
        }

        void HandTo(AvatarIKGoal goal, Transform weapon, Vector3 localPoint)
        {
            if (weapon == null) { m_Anim.SetIKPositionWeight(goal, 0f); return; }
            m_Anim.SetIKPositionWeight(goal, m_Weight);
            m_Anim.SetIKPosition(goal, weapon.TransformPoint(localPoint));
            m_Anim.SetIKRotationWeight(goal, 0f);
        }

        void Elbow(AvatarIKHint hint, Vector3 position)
        {
            m_Anim.SetIKHintPositionWeight(hint, m_Weight * 0.8f);
            m_Anim.SetIKHintPosition(hint, position);
        }
    }
}
