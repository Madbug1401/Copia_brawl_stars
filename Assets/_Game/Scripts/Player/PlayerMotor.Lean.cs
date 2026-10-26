using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Inclinar (estilo Rainbow Six Siege): Q esquerda / E direita.
    /// - A câmara (e a arma, que é filha dela) desliza para o lado e roda um pouco; os tiros saem dessa posição,
    ///   por isso dá para espreitar uma esquina mostrando só a cabeça e um ombro.
    /// - Justo para quem leva o tiro: a cabeça e o ombro inclinados têm hitbox próprio (esferas filhas do jogador),
    ///   os headshots usam a cabeça deslocada e a compensação de latência recua também a inclinação.
    /// - Encurta junto a paredes (não se espreita através delas). Inclinar corta o sprint; também inclina no slide.
    /// - Manter premido (por defeito) ou alternar (SETTINGS > GAMEPLAY), como no R6.
    /// O dono simula; os outros recebem a inclinação no snapshot (<see cref="MotorState.Lean"/>).
    /// </summary>
    public partial class PlayerMotor
    {
        // Inclinar (Q/E). Não serializados: valem sempre os valores daqui (o prefab não guarda cópias antigas).
        [Tooltip("Deslocamento lateral máximo dos olhos (m).")]
        [System.NonSerialized] public float leanOffset = 0.52f;
        [Tooltip("Rotação da câmara (graus) com a inclinação máxima.")]
        [System.NonSerialized] public float leanRoll = 18f;
        [Tooltip("Quanto os olhos descem (m) com a inclinação máxima.")]
        [System.NonSerialized] public float leanDrop = 0.06f;
        [Tooltip("Velocidade da transição (inclinação por segundo; 7 ≈ 0,15 s até ao fim).")]
        [System.NonSerialized] public float leanSpeed = 7f;

        const float LeanHeadRadius = 0.27f, LeanBodyRadius = 0.3f;
        const float LeanBodyHeight = 1.12f;   // centro do ombro inclinado (de pé)
        const float LeanHitboxMin = 0.12f;    // abaixo disto a cápsula normal já cobre o corpo

        float m_Lean;              // −1 esquerda … 0 … +1 direita (dono: simulado; outros: do snapshot)
        int m_LeanToggleDir;       // modo alternar: lado escolhido
        int m_LeanLastPressed = 1; // as duas teclas premidas: ganha a última
        float m_HitboxLean;        // inclinação aplicada ao hitbox agora (no recuo da compensação: a do passado)
        float m_SavedHitboxLean;
        bool m_LeanRewound;
        Transform m_LeanHead, m_LeanBody;
        SphereCollider m_LeanHeadCol, m_LeanBodyCol;
        static readonly RaycastHit[] s_LeanHits = new RaycastHit[12];

        /// <summary>−1 inclinado à esquerda … +1 à direita.</summary>
        public float LeanAmount => m_Lean;
        /// <summary>A inclinar (mais de metade).</summary>
        public bool IsLeaning => Mathf.Abs(m_Lean) > 0.5f;

        /// <summary>Deslocamento (mundo) do centro da cabeça por causa da inclinação. Usado nos headshots.</summary>
        public Vector3 LeanHeadOffset =>
            transform.right * (m_HitboxLean * leanOffset * 0.95f) + Vector3.down * (Mathf.Abs(m_HitboxLean) * leanDrop);

        /// <summary>Curva suave (entra e sai devagar) para a câmara.</summary>
        static float LeanEase(float l) => Mathf.Sign(l) * Mathf.SmoothStep(0f, 1f, Mathf.Abs(l));

        /// <summary>Graus a somar ao roll da câmara (positivo = roda para a esquerda).</summary>
        float LeanCameraRoll => -leanRoll * LeanEase(m_Lean);

        void LeanAwake()
        {
            if (m_LeanHead != null) return;
            m_LeanHead = MakeLeanCollider("LeanHitbox_Head", LeanHeadRadius, out m_LeanHeadCol);
            m_LeanBody = MakeLeanCollider("LeanHitbox_Shoulder", LeanBodyRadius, out m_LeanBodyCol);
            if (GetComponent<LeanPose>() == null) gameObject.AddComponent<LeanPose>();
            SetHitboxLean(0f);
        }

        Transform MakeLeanCollider(string name, float radius, out SphereCollider col)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.enabled = false;
            // Só para tiros/explosões: o próprio movimento não choca com a cabeça inclinada
            if (m_Controller != null) Physics.IgnoreCollision(m_Controller, col, true);
            return go.transform;
        }

        // ------------------------------------------------------------------ dono

        /// <summary>Lado pedido pelo jogador neste frame (−1, 0, +1). Também trata o modo alternar e o sprint.</summary>
        int ResolveLeanDir(in PlayerCommands cmd, bool alive)
        {
            if (!alive || m_Launched)
            {
                m_LeanToggleDir = 0;
                return 0;
            }

            if (cmd.leanToggle)
            {
                if (cmd.leanLeftPressed) m_LeanToggleDir = m_LeanToggleDir == -1 ? 0 : -1;
                if (cmd.leanRightPressed) m_LeanToggleDir = m_LeanToggleDir == 1 ? 0 : 1;
                // Correr (sprint para a frente) cancela a inclinação, como no R6 — mas não durante o slide
                if (!m_Sliding && cmd.sprint && cmd.move.y > 0.1f && !cmd.leanLeftPressed && !cmd.leanRightPressed) m_LeanToggleDir = 0;
                return m_LeanToggleDir;
            }

            m_LeanToggleDir = 0;
            if (cmd.leanLeftPressed) m_LeanLastPressed = -1;
            if (cmd.leanRightPressed) m_LeanLastPressed = 1;
            if (cmd.leanLeft && cmd.leanRight) return m_LeanLastPressed;
            if (cmd.leanLeft) return -1;
            if (cmd.leanRight) return 1;
            return 0;
        }

        void UpdateLean(int dir, float dt)
        {
            float target = dir;
            if (dir != 0) target *= LeanRoom(dir);
            // A afastar-se de uma parede é imediato; inclinar segue a velocidade normal
            float speed = Mathf.Abs(target) < Mathf.Abs(m_Lean) && Mathf.Sign(target) == Mathf.Sign(m_Lean) && dir != 0
                ? leanSpeed * 3f : leanSpeed;
            m_Lean = Mathf.MoveTowards(m_Lean, target, speed * dt);
            ApplyLeanPose();
        }

        /// <summary>Fração da inclinação que cabe antes de bater numa parede (0–1).</summary>
        float LeanRoom(int dir)
        {
            if (cameraRoot == null) return 1f;
            var origin = transform.position + Vector3.up * cameraRoot.localPosition.y;
            var side = transform.right * dir;
            const float probe = 0.14f;
            int n = Physics.SphereCastNonAlloc(origin, probe, side, s_LeanHits, leanOffset + 0.08f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = leanOffset + 0.08f;
            for (int i = 0; i < n; i++)
            {
                var h = s_LeanHits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance <= 0f) return 0f; // já encostado
                if (h.distance < best) best = h.distance;
            }
            return Mathf.Clamp01((best - 0.04f) / Mathf.Max(0.05f, leanOffset));
        }

        // ------------------------------------------------------------------ outros (e host)

        void ApplyRemoteLean(sbyte lean)
        {
            m_Lean = Mathf.Clamp(lean / 127f, -1f, 1f);
            ApplyLeanPose();
        }

        sbyte PackLean() => (sbyte)Mathf.RoundToInt(Mathf.Clamp(m_Lean, -1f, 1f) * 127f);

        /// <summary>Olhos deslocados para o lado (câmara do dono; origem dos tiros validada no host) e hitbox.</summary>
        void ApplyLeanPose()
        {
            if (cameraRoot != null)
            {
                float e = LeanEase(m_Lean);
                var p = cameraRoot.localPosition;
                p.x = leanOffset * e;
                p.y -= leanDrop * Mathf.Abs(e); // a altura base já foi posta pelo agachar neste frame
                cameraRoot.localPosition = p;
            }
            if (!m_LeanRewound) SetHitboxLean(m_Lean);
        }

        void SetHitboxLean(float lean)
        {
            m_HitboxLean = lean;
            if (m_LeanHead == null) return;
            bool on = Mathf.Abs(lean) > LeanHitboxMin && m_Controller != null && m_Controller.enabled &&
                      (m_Player == null || m_Player.Health == null || !m_Player.Health.IsDead);
            if (m_LeanHeadCol.enabled != on) { m_LeanHeadCol.enabled = on; m_LeanBodyCol.enabled = on; }
            if (!on) return;
            float drop = CrouchDrop;
            m_LeanHead.localPosition = new Vector3(lean * leanOffset * 0.95f,
                CombatMath.HeadCenterHeight - drop - Mathf.Abs(lean) * leanDrop, 0f);
            m_LeanBody.localPosition = new Vector3(lean * leanOffset * 0.55f, LeanBodyHeight * CrouchScale, 0f);
        }

        void ResetLean()
        {
            m_Lean = 0f;
            m_LeanToggleDir = 0;
            m_LeanRewound = false;
            if (cameraRoot != null)
            {
                var p = cameraRoot.localPosition;
                p.x = 0f;
                cameraRoot.localPosition = p;
            }
            SetHitboxLean(0f);
        }

        // ------------------------------------------------------------------ compensação de latência (host)

        /// <summary>Host: põe o hitbox da inclinação como estava na hora 'time' (ver <see cref="LagCompensation"/>).</summary>
        internal void LagRewindLean(double time)
        {
            if (!TryGetLeanAt(time, out var lean)) return;
            m_SavedHitboxLean = m_HitboxLean;
            m_LeanRewound = true;
            SetHitboxLean(lean);
        }

        internal void LagRestoreLean()
        {
            if (!m_LeanRewound) return;
            m_LeanRewound = false;
            SetHitboxLean(m_SavedHitboxLean);
        }

        bool TryGetLeanAt(double time, out float lean)
        {
            lean = m_HitboxLean;
            int n = m_ServerHistory.Count;
            if (!IsServer || n == 0) return false;
            sbyte packed;
            if (time <= m_ServerHistory[0].Time) packed = m_ServerHistory[0].State.Lean;
            else
            {
                packed = m_ServerHistory[n - 1].State.Lean;
                for (int i = 1; i < n; i++)
                {
                    var b = m_ServerHistory[i];
                    if (time > b.Time) continue;
                    var a = m_ServerHistory[i - 1];
                    float k = (float)((time - a.Time) / System.Math.Max(1e-4, b.Time - a.Time));
                    packed = Blend(a, b, k).Lean;
                    break;
                }
            }
            lean = Mathf.Clamp(packed / 127f, -1f, 1f);
            return true;
        }
    }
}
