using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Boneco agachado / a deslizar: achata o corpo (estilo cartoon) e inclina-o para trás no slide, com pó no chão.
    /// Corre depois do CharacterRig (que repõe escala e rotação no Update), por isso só ajusta por cima.
    /// Adicionado em runtime pelo <see cref="PlayerMotor"/>.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class CrouchPose : MonoBehaviour
    {
        PlayerMotor m_Motor;
        Transform m_Rig;
        float m_Lean;
        float m_NextDust;
        Vector3 m_BaseScale = Vector3.one, m_SetScale = new Vector3(-1f, -1f, -1f);
        Quaternion m_BaseRot = Quaternion.identity, m_SetRot = new Quaternion(0f, 0f, 0f, 0f);

        void Awake() => m_Motor = GetComponent<PlayerMotor>();

        void LateUpdate()
        {
            if (m_Motor == null) return;
            float blend = m_Motor.CrouchBlend;
            bool sliding = m_Motor.IsSliding;
            m_Lean = Mathf.MoveTowards(m_Lean, sliding ? 1f : 0f, Time.deltaTime * 8f);
            if (blend <= 0.001f && m_Lean <= 0.001f)
            {
                if (m_Rig != null && m_Rig.localScale == m_SetScale) { m_Rig.localScale = m_BaseScale; m_Rig.localRotation = m_BaseRot; }
                m_SetScale = new Vector3(-1f, -1f, -1f);
                return;
            }

            if (m_Rig == null || m_Rig.parent != transform) m_Rig = FindRig();
            if (m_Rig != null)
            {
                // Se ninguém repôs a pose desde o último frame (rig não animada), parte da base guardada: nunca acumula
                var baseScale = m_Rig.localScale == m_SetScale ? m_BaseScale : m_Rig.localScale;
                var baseRot = m_Rig.localRotation == m_SetRot ? m_BaseRot : m_Rig.localRotation;
                m_BaseScale = baseScale;
                m_BaseRot = baseRot;
                float k = m_Motor.CrouchScale;
                float widen = Mathf.Lerp(1f, 1.08f, blend);
                m_SetScale = new Vector3(baseScale.x * widen, baseScale.y * k, baseScale.z * widen);
                m_SetRot = baseRot * Quaternion.Euler(-14f * m_Lean, 0f, 0f);
                m_Rig.localScale = m_SetScale;
                m_Rig.localRotation = m_SetRot;
            }

            if (sliding && Time.time >= m_NextDust)
            {
                m_NextDust = Time.time + 0.05f;
                var v = m_Motor.HorizontalVelocity;
                var pos = transform.position + Random.insideUnitSphere * 0.25f;
                pos.y = transform.position.y + 0.05f;
                Fx.Puff(pos, -v * 0.08f + Vector3.up * 0.6f, new Color(0.85f, 0.8f, 0.7f, 0.6f), 0.22f, 0.4f);
            }
        }

        Transform FindRig()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c.gameObject.activeSelf && c.name.StartsWith("Rig_")) return c;
            }
            return null;
        }
    }
}
