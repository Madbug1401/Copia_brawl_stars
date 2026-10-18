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
        float m_KickR, m_KickL;
        float m_Weight;

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
                case RigStyle.DualRevolvers:
                case RigStyle.DualExplosives: m_Grip = Grip.Dual; break;
                case RigStyle.Bow: m_Grip = Grip.OneHandedLeft; break;
                case RigStyle.SprayCan: m_Grip = Grip.OneHandedRight; break;
                default: m_Grip = Grip.TwoHanded; break;
            }
            m_SupportZ = style == RigStyle.Sniper ? 0.2f : style == RigStyle.Shotgun ? 0.16f : 0.1f;
            m_Weight = 1f;
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

            switch (m_Grip)
            {
                case Grip.TwoHanded:
                    // espingarda ao ombro direito, a apontar para a mira
                    PlaceWeapon(m_WeaponR, body + yaw * ShoulderR, aim, new Vector3(-0.06f, -0.07f, 0.22f), m_KickR);
                    HandTo(AvatarIKGoal.RightHand, m_WeaponR, Vector3.zero);
                    HandTo(AvatarIKGoal.LeftHand, m_WeaponR, new Vector3(0f, -0.02f, m_SupportZ));
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
