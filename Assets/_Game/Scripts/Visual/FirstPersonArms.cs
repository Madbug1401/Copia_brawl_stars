using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Braços do modelo do personagem na vista em 1.ª pessoa (malha só com braços e mãos, ver CharacterModelBuilder).
    /// As mãos vão ter à arma por IK: a direita ao punho, a esquerda ao apoio (ou à 2.ª arma / ao arco).
    /// Adicionado pelo <see cref="ViewModelRig"/> ao objeto do Animator (o controller precisa de "IK Pass").
    /// </summary>
    public class FirstPersonArms : MonoBehaviour
    {
        Animator m_Anim;
        Transform m_View;                 // raiz do viewmodel (presa à câmara)
        [System.NonSerialized] public Transform rightTarget, leftTarget, knife;
        /// <summary>Se definido, a pose vem da animação e esta arma é colocada entre as mãos (ver ModelWeaponHolder).</summary>
        [System.NonSerialized] public Transform drivenWeapon;
        [System.NonSerialized] public float kick;
        [System.NonSerialized] public Vector3 supportLocal = new Vector3(0f, 0.06f, 0.26f);
        /// <summary>Animações com a arma nas mãos: deslocamento extra da mão direita, no espaço da câmara. Público para afinar.</summary>
        public static Vector3 AnimatedHandOffset = new Vector3(0f, 0f, 0.3f);
        /// <summary>
        /// Pose fixa de FPS para a arma conduzida (em vez de a tirar da mão da animação, que é uma pose "de anca" para a
        /// 3.ª pessoa): posição do punho e rotação da arma no espaço da câmara (com balanço). Públicas para afinar.
        /// </summary>
        public static bool FixedGunPose = true;
        public static Vector3 GunPos = new Vector3(0.17f, -0.2f, 0.26f), GunEuler = new Vector3(2f, -4f, -6f);
        /// <summary>Pulso direito em relação ao punho da arma (espaço da arma). Público para afinar.</summary>
        public static Vector3 WristFromGrip = new Vector3(0f, 0.05f, -0.04f);
        public static Vector3 ElbowHintR = new Vector3(0.45f, -0.5f, 0f), ElbowHintL = new Vector3(-0.35f, -0.55f, 0.15f);
        /// <summary>Referência com balanço/sway (o pai do modelo). Se nulo, usa a raiz da vista.</summary>
        [System.NonSerialized] public Transform swayRef;

        // Afinações (espaço local do alvo / da câmara). Públicas para se poderem ajustar em Play.
        // Mão esquerda por baixo da arma, palma para cima (afinado na vista em 1.ª pessoa).
        // Mão direita: pulso atrás e à direita do punho, dedos para a frente/esquerda a envolvê-lo.
        [System.NonSerialized] public Vector3 rightOffset = new Vector3(0.09f, 0f, -0.1f), leftOffset = new Vector3(0.03f, -0.06f, -0.04f);
        [System.NonSerialized] public Vector3 rightEuler = new Vector3(30f, -45f, -90f), leftEuler = new Vector3(20f, -60f, 180f);
        [System.NonSerialized] public float rightRotWeight = 1f, leftRotWeight = 1f;
        [System.NonSerialized] public Vector3 elbowR = new Vector3(0.42f, -0.45f, 0.05f), elbowL = new Vector3(-0.42f, -0.45f, 0.1f);

        public void Setup(Animator anim, Transform view)
        {
            m_Anim = anim;
            m_View = view;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (layerIndex != 0 || m_Anim == null || m_View == null) return;
            if (drivenWeapon != null && FixedGunPose)
            {
                var basis = swayRef != null ? swayRef : m_View;
                var rot = basis.rotation * Quaternion.Euler(GunEuler) * Quaternion.Euler(-8f * kick, 0f, 0f);
                var pos = basis.TransformPoint(GunPos) - rot * Vector3.forward * (0.07f * kick);
                drivenWeapon.SetPositionAndRotation(pos, rot);
                var supportOffset = Vector3.Scale(supportLocal, drivenWeapon.lossyScale);
                var rp = pos + rot * Vector3.Scale(WristFromGrip, drivenWeapon.lossyScale);
                var lp = pos + rot * supportOffset;
                foreach (var g in new[] { AvatarIKGoal.RightHand, AvatarIKGoal.LeftHand })
                {
                    m_Anim.SetIKPositionWeight(g, 1f);
                    m_Anim.SetIKRotationWeight(g, 1f);
                }
                m_Anim.SetIKPosition(AvatarIKGoal.RightHand, rp); m_Anim.SetIKRotation(AvatarIKGoal.RightHand, rot * Quaternion.Euler(ModelWeaponHolder.RightHandEuler));
                m_Anim.SetIKPosition(AvatarIKGoal.LeftHand, lp); m_Anim.SetIKRotation(AvatarIKGoal.LeftHand, rot * Quaternion.Euler(ModelWeaponHolder.LeftHandEuler));
                m_Anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 1f);
                m_Anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 1f);
                m_Anim.SetIKHintPosition(AvatarIKHint.RightElbow, basis.TransformPoint(ElbowHintR));
                m_Anim.SetIKHintPosition(AvatarIKHint.LeftElbow, basis.TransformPoint(ElbowHintL));
                if (knife != null && knife.gameObject.activeInHierarchy)
                    Goal(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, knife, elbowR, rightOffset, rightEuler, rightRotWeight);
                return;
            }
            if (drivenWeapon != null)
            {
                ModelWeaponHolder.HoldFromHands(m_Anim, drivenWeapon, supportLocal, Vector3.zero, Quaternion.identity,
                    m_View.forward, m_View.up, kick, out var rp, out var rr, out var lp, out var lr, m_View.TransformDirection(AnimatedHandOffset));
                foreach (var g in new[] { AvatarIKGoal.RightHand, AvatarIKGoal.LeftHand })
                {
                    m_Anim.SetIKPositionWeight(g, 1f);
                    m_Anim.SetIKRotationWeight(g, 1f);
                }
                m_Anim.SetIKPosition(AvatarIKGoal.RightHand, rp); m_Anim.SetIKRotation(AvatarIKGoal.RightHand, rr);
                m_Anim.SetIKPosition(AvatarIKGoal.LeftHand, lp); m_Anim.SetIKRotation(AvatarIKGoal.LeftHand, lr);
                if (knife != null && knife.gameObject.activeInHierarchy)
                    Goal(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, knife, elbowR, rightOffset, rightEuler, rightRotWeight);
                return;
            }
            var right = knife != null && knife.gameObject.activeInHierarchy ? knife : rightTarget;
            Goal(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, right, elbowR, rightOffset, rightEuler, rightRotWeight);
            Goal(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, leftTarget, elbowL, leftOffset, leftEuler, leftRotWeight);
        }

        void Goal(AvatarIKGoal goal, AvatarIKHint hint, Transform target, Vector3 elbow, Vector3 offset, Vector3 euler, float rotWeight)
        {
            float w = target != null && target.gameObject.activeInHierarchy ? 1f : 0f;
            m_Anim.SetIKPositionWeight(goal, w);
            m_Anim.SetIKRotationWeight(goal, w * rotWeight);
            m_Anim.SetIKHintPositionWeight(hint, w);
            if (w <= 0f) return;
            // o deslocamento é no espaço da câmara, para não depender da escala da arma
            m_Anim.SetIKPosition(goal, target.position + m_View.TransformDirection(offset));
            // rotação relativa à arma (acompanha o coice); o alvo é o punho / apoio da arma
            m_Anim.SetIKRotation(goal, target.rotation * Quaternion.Euler(euler));
            m_Anim.SetIKHintPosition(hint, m_View.TransformPoint(elbow));
        }
    }
}
