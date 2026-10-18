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

        // Afinações (espaço local do alvo / da câmara). Públicas para se poderem ajustar em Play.
        // Mão esquerda por baixo da arma, palma para cima e dedos a envolvê-la (afinado na vista em 1.ª pessoa).
        // Mão direita: pulso atrás e à direita do punho, dedos para a frente/esquerda a envolvê-lo.
        [System.NonSerialized] public Vector3 rightOffset = new Vector3(0.09f, 0f, -0.1f), leftOffset = new Vector3(-0.01f, -0.06f, 0f);
        [System.NonSerialized] public Vector3 rightEuler = new Vector3(30f, -45f, -90f), leftEuler = new Vector3(0f, 35f, -90f);
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
