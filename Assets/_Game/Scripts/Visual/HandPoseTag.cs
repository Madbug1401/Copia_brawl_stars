using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Marca numa mão estilizada da 1.ª pessoa: como a mão agarra (pose), o raio da pega e o lado. Os braços com
    /// esqueleto (<see cref="FPSkinnedArms"/>) usam-na para pôr a mão do modelo no mesmo sítio e fechar os dedos.
    /// </summary>
    public sealed class HandPoseTag : MonoBehaviour
    {
        public HandPose pose;
        public float radius;
        public bool left;
    }
}
