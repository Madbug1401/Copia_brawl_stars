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
            if (drivenWeapon != null)
            {
                ModelWeaponHolder.HoldFromHands(m_Anim, drivenWeapon, supportLocal, Vector3.zero, Quaternion.identity,
                    m_View.forward, m_View.up, kick, out var rp, out var rr, out var lp, out var lr, m_View.forward * 0.3f);
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
