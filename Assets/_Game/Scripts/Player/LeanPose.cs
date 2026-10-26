using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Boneco a inclinar (Q/E), estilo Rainbow Six Siege: o tronco roda para o lado e a anca desliza o que faltar
    /// para a cabeça visível ficar exatamente onde está o hitbox da cabeça inclinada (o que se vê é o que se acerta).
    /// Serve para a rig de primitivas e para modelos humanoides (mede o próprio esqueleto, qualquer tamanho).
    /// Corre depois do CharacterRig/Animator e do CrouchPose; nunca acumula entre frames.
    /// Adicionado em runtime pelo <see cref="PlayerMotor"/>.
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public class LeanPose : MonoBehaviour
    {
        const float SpineRoll = 22f;      // graus do tronco com a inclinação máxima
        const float MaxHipShift = 0.3f;   // m

        static readonly Quaternion Unset = new Quaternion(0f, 0f, 0f, 0f);
        static readonly Vector3 UnsetPos = new Vector3(float.NaN, 0f, 0f);

        PlayerMotor m_Motor;
        PlayerHealth m_Health;
        Transform m_Rig, m_Spine, m_Hips, m_Head;
        Quaternion m_SpineBase, m_SpineSet = Unset;
        Vector3 m_HipsBase, m_HipsSet = UnsetPos;

        void Awake()
        {
            m_Motor = GetComponent<PlayerMotor>();
            m_Health = GetComponent<PlayerHealth>();
        }

        void LateUpdate()
        {
            if (m_Motor == null) return;
            float l = m_Motor.LeanAmount;
            bool dead = m_Health != null && m_Health.IsDead;
            if (Mathf.Abs(l) < 0.001f || dead)
            {
                // Repõe se ninguém animou desde a última vez (rig parada)
                if (m_Spine != null && m_Spine.localRotation == m_SpineSet) m_Spine.localRotation = m_SpineBase;
                if (m_Hips != null && m_Hips.localPosition == m_HipsSet) m_Hips.localPosition = m_HipsBase;
                m_SpineSet = Unset;
                m_HipsSet = UnsetPos;
                return;
            }

            if (m_Rig == null || m_Rig.parent != transform || !m_Rig.gameObject.activeSelf) FindBones();
            if (m_Spine == null) return;

            // Parte da pose animada deste frame (ou da base guardada se ninguém a repôs)
            m_SpineBase = m_Spine.localRotation == m_SpineSet ? m_SpineBase : m_Spine.localRotation;
            m_Spine.localRotation = m_SpineBase;
            if (m_Hips != null)
            {
                m_HipsBase = m_Hips.localPosition == m_HipsSet ? m_HipsBase : m_Hips.localPosition;
                m_Hips.localPosition = m_HipsBase;
            }

            var right = transform.right;
            float headBefore = m_Head != null ? Vector3.Dot(m_Head.position - transform.position, right) : 0f;

            float e = Mathf.Sign(l) * Mathf.SmoothStep(0f, 1f, Mathf.Abs(l));
            // Roda em torno da direção para onde o jogador olha: o topo vai para o lado da inclinação
            m_Spine.rotation = Quaternion.AngleAxis(-SpineRoll * e, transform.forward) * m_Spine.rotation;
            m_SpineSet = m_Spine.localRotation;

            if (m_Head != null && m_Hips != null)
            {
                float moved = Vector3.Dot(m_Head.position - transform.position, right) - headBefore;
                float want = Vector3.Dot(m_Motor.LeanHeadOffset, right);
                float shift = Mathf.Clamp(want - moved, -MaxHipShift, MaxHipShift);
                m_Hips.position += right * shift;
                m_HipsSet = m_Hips.localPosition;
            }
        }

        void FindBones()
        {
            m_Rig = null; m_Spine = m_Hips = m_Head = null;
            m_SpineSet = Unset; m_HipsSet = UnsetPos;
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.gameObject.activeSelf && c.name.StartsWith("Rig_")) { m_Rig = c; break; }
            }
            if (m_Rig == null) return;
            var anim = m_Rig.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                m_Spine = anim.GetBoneTransform(HumanBodyBones.Spine) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
                m_Hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                m_Head = anim.GetBoneTransform(HumanBodyBones.Head);
            }
            if (m_Spine == null) m_Spine = FindDeep(m_Rig, "Spine");
            if (m_Hips == null) m_Hips = FindDeep(m_Rig, "Hips");
            if (m_Head == null) m_Head = FindDeep(m_Rig, "Head");
            // A anca tem de estar acima do tronco na hierarquia (senão mexê-la não leva a cabeça)
            if (m_Hips != null && m_Spine != null && !m_Spine.IsChildOf(m_Hips)) m_Hips = null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
