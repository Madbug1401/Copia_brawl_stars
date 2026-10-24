using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Braços da 1.ª pessoa com esqueleto (gerados no Blender a partir do modelo de cada personagem, com a manga, o
    /// punho e a pele da roupa dele — ver Tools/Blender/FPArms). Cada braço: UpperArm → ForeArm → Hand → 5 dedos × 3
    /// falanges. A mão vai ao sítio da mão estilizada de cada arma (que continua a ser o "alvo" afinado por arma), o
    /// cotovelo dobra por IK de dois ossos e os dedos fecham conforme a pose (pistola, punho, lata, apoio).
    /// </summary>
    public sealed class FPSkinnedArms : MonoBehaviour
    {
        /// <summary>Comprimento pulso → nós dos dedos na vista (m). Os modelos têm mãos grandes; isto põe-nas à escala.</summary>
        public static float PalmLength = 0.092f;
        /// <summary>Braço e antebraço na vista (m). Os personagens são cartoon (braços curtos, mãos enormes): os ossos
        /// esticam-se ao longo do comprimento (sem engordar) e a mão encolhe para PalmLength.</summary>
        public static float UpperLength = 0.27f, ForeLength = 0.29f;
        public static float MaxUpperStretch = 2.2f, MaxForeStretch = 1.2f;
        /// <summary>Ombros (espaço do balanço da arma), fora do ecrã.</summary>
        public static Vector3 ShoulderR = new Vector3(0.3f, -0.44f, -0.22f), ShoulderL = new Vector3(-0.3f, -0.44f, -0.22f);

        /// <summary>Raio de um dedo (m, com a mão à escala PalmLength) e flexão extra nos nós.</summary>
        public static float FingerRadius = 0.0095f, WrapExtra = 6f;

        /// <summary>Meio ângulo (graus) da corda de comprimento l num círculo de raio R.</summary>
        static float HalfArc(float l, float R) => Mathf.Asin(Mathf.Clamp(l / (2f * Mathf.Max(R, 1e-4f)), 0f, 1f)) * Mathf.Rad2Deg;

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Pinky" };

        // ângulos de flexão (graus) por pose: [dedo][falange]; dedo 0 = polegar
        static readonly float[][] Pistol = { new[] { 0f, 12f, 10f }, new[] { 12f, 22f, 12f }, new[] { 72f, 88f, 55f }, new[] { 76f, 88f, 55f }, new[] { 80f, 85f, 50f } };
        static readonly float[][] Fist = { new[] { 0f, 25f, 20f }, new[] { 78f, 95f, 60f }, new[] { 80f, 95f, 60f }, new[] { 82f, 95f, 60f }, new[] { 84f, 92f, 58f } };
        static readonly float[][] Can = { new[] { 0f, 10f, 8f }, new[] { 8f, 30f, 25f }, new[] { 48f, 55f, 38f }, new[] { 50f, 55f, 38f }, new[] { 54f, 55f, 36f } };
        static readonly float[][] Support = { new[] { 0f, 8f, 6f }, new[] { 34f, 36f, 22f }, new[] { 36f, 38f, 24f }, new[] { 38f, 38f, 24f }, new[] { 40f, 36f, 22f } };

        class Arm
        {
            public Transform upper, fore, hand;
            public Transform[][] fingers = new Transform[5][];
            public Quaternion[][] fingerRest = new Quaternion[5][];
            public Vector3[][] flexAxis = new Vector3[5][];
            public Quaternion upperRest, foreRest;
            public Vector3 upperAxis, foreAxis, foreUp, handF, handU;
            public float L1, L2, lp;
            public Vector3 upperScale0, foreScale0;
            public float[][] seg = new float[5][];   // comprimento das falanges (mundo)
            public Renderer renderer;
            public bool ok;
        }

        readonly Arm m_R = new Arm(), m_L = new Arm();
        Transform m_Sway;

        /// <summary>Instancia os braços do personagem (Resources/Characters/&lt;brawlerId&gt;_fphands) no balanço da arma.</summary>
        public static FPSkinnedArms TryCreate(BrawlerDefinition b, Transform sway, int layer)
        {
            if (b == null || sway == null) return null;
            var prefab = Resources.Load<GameObject>(CharacterRig.ModelResourceFolder + b.brawlerId + "_fphands");
            if (prefab == null) return null;
            var go = Instantiate(prefab, sway, false);
            go.name = "SkinnedArms";
            var arms = go.GetComponent<FPSkinnedArms>() ?? go.AddComponent<FPSkinnedArms>();
            if (!arms.Init(sway)) { Destroy(go); return null; }
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return arms;
        }

        bool Init(Transform sway)
        {
            m_Sway = sway;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            bool r = Setup(m_R, "R"), l = Setup(m_L, "L");
            return r || l;
        }

        Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        bool Setup(Arm a, string s)
        {
            a.upper = Find(s + "_UpperArm");
            a.fore = Find(s + "_ForeArm");
            a.hand = Find(s + "_Hand");
            if (a.upper == null || a.fore == null || a.hand == null) return false;
            for (int f = 0; f < 5; f++)
            {
                a.fingers[f] = new Transform[3];
                a.fingerRest[f] = new Quaternion[3];
                a.flexAxis[f] = new Vector3[3];
                for (int i = 0; i < 3; i++) a.fingers[f][i] = Find(s + "_" + FingerNames[f] + (i + 1));
            }
            // ossos soltos (filhos da raiz): cada um é posto no mundo e pode esticar sem deformar os seguintes
            float L1m = Vector3.Distance(a.upper.position, a.fore.position);
            float L2m = Vector3.Distance(a.fore.position, a.hand.position);
            var mid0 = a.fingers[2][0];
            float lpm = mid0 != null ? Vector3.Distance(a.hand.position, mid0.position) : L2m * 0.5f;
            a.upperAxis = a.upper.InverseTransformDirection(a.fore.position - a.upper.position).normalized;
            a.foreAxis = a.fore.InverseTransformDirection(a.hand.position - a.fore.position).normalized;
            a.upper.SetParent(transform, true);
            a.fore.SetParent(transform, true);
            a.hand.SetParent(transform, true);
            float k = m_Sway.lossyScale.x;
            Stretch(a.upper, a.upperAxis, UpperLength * k / Mathf.Max(1e-4f, L1m));
            Stretch(a.fore, a.foreAxis, ForeLength * k / Mathf.Max(1e-4f, L2m));
            a.hand.localScale *= PalmLength * k / Mathf.Max(1e-4f, lpm);
            a.upperScale0 = a.upper.localScale;
            a.foreScale0 = a.fore.localScale;
            a.upperRest = a.upper.localRotation;
            a.foreRest = a.fore.localRotation;
            a.L1 = UpperLength * k;
            a.L2 = ForeLength * k;
            var mid = a.fingers[2][0];
            var idx = a.fingers[1][0];
            var pin = a.fingers[4][0];
            if (mid == null || idx == null || pin == null) return false;
            var F = (mid.position - a.hand.position).normalized;
            var U = Vector3.ProjectOnPlane(idx.position - pin.position, F).normalized;
            a.lp = Vector3.Distance(mid.position, a.hand.position);
            for (int f = 0; f < 5; f++)
            {
                a.seg[f] = new float[3];
                var c0 = a.fingers[f][0]; var c1 = a.fingers[f][1]; var c2 = a.fingers[f][2];
                float l1 = c0 != null && c1 != null ? Vector3.Distance(c0.position, c1.position) : a.lp * 0.4f;
                float l2 = c1 != null && c2 != null ? Vector3.Distance(c1.position, c2.position) : l1 * 0.62f;
                a.seg[f][0] = l1; a.seg[f][1] = l2; a.seg[f][2] = l2 * 0.82f;
            }
            a.handF = a.hand.InverseTransformDirection(F);
            a.handU = a.hand.InverseTransformDirection(U);
            a.foreUp = a.fore.InverseTransformDirection(U);
            // lado da palma: o polegar fica do lado da palma
            var P = Vector3.Cross(F, U).normalized;
            var th = a.fingers[0][2] != null ? a.fingers[0][2] : a.fingers[0][0];
            if (th != null && Vector3.Dot(th.position - a.hand.position, P) < 0f) P = -P;
            for (int f = 0; f < 5; f++)
                for (int i = 0; i < 3; i++)
                {
                    var b = a.fingers[f][i];
                    if (b == null) continue;
                    a.fingerRest[f][i] = b.localRotation;
                    Vector3 d;
                    if (i < 2 && a.fingers[f][i + 1] != null) d = a.fingers[f][i + 1].position - b.position;
                    else d = b.position - b.parent.position;
                    var axis = Vector3.Cross(d.normalized, P).normalized;
                    a.flexAxis[f][i] = b.InverseTransformDirection(axis);
                }
            a.renderer = null;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name.EndsWith("_" + s)) a.renderer = smr;
            a.ok = true;
            return true;
        }

        /// <summary>Estica o osso ao longo do seu eixo (escala local no eixo mais alinhado).</summary>
        static void ScaleAlong(Transform t, Vector3 axisLocal, Vector3 baseScale, float f)
        {
            t.localScale = baseScale;
            if (f != 1f) Stretch(t, axisLocal, f);
        }

        static void Stretch(Transform t, Vector3 axisLocal, float f)
        {
            var a = new Vector3(Mathf.Abs(axisLocal.x), Mathf.Abs(axisLocal.y), Mathf.Abs(axisLocal.z));
            var sc = t.localScale;
            if (a.x >= a.y && a.x >= a.z) sc.x *= f; else if (a.y >= a.z) sc.y *= f; else sc.z *= f;
            t.localScale = sc;
        }

        /// <summary>
        /// Põe cada braço na mão estilizada correspondente (null = esconde o braço). Os cotovelos (mundo) dão o plano
        /// de dobra; os ombros ficam fora do ecrã.
        /// </summary>
        public void Pose(Transform rightHand, Transform leftHand, Vector3 elbowR, Vector3 elbowL, Vector3? shoulderR = null)
        {
            Solve(m_R, rightHand, elbowR, shoulderR ?? ShoulderR);
            Solve(m_L, leftHand, elbowL, ShoulderL);
        }

        void Solve(Arm a, Transform target, Vector3 elbowHint, Vector3 shoulderLocal)
        {
            if (!a.ok) return;
            bool show = target != null && target.gameObject.activeInHierarchy;
            if (a.renderer != null && a.renderer.enabled != show) a.renderer.enabled = show;
            if (!show) return;
            var tag = target.GetComponent<HandPoseTag>();
            float r = tag != null ? tag.radius : 0.022f;
            var pose = tag != null ? tag.pose : HandPose.Pistol;

            // moldura da mão estilizada: dedos para a frente (do pulso para os nós) e o lado do indicador (+Y da pega)
            var wristT = target.Find("Wrist");
            Vector3 Ft = wristT != null ? -wristT.forward : target.forward;
            Vector3 Ut = Vector3.ProjectOnPlane(target.up, Ft).normalized;
            float sc = Mathf.Abs(target.lossyScale.y);
            // nós dos dedos à frente-fora da pega, à distância do eixo = raio da pega + meio dedo: os dedos dão a volta
            // à pega encostados à superfície (ângulos calculados abaixo a partir do comprimento de cada falange)
            float fr = FingerRadius * a.lp / Mathf.Max(1e-4f, PalmLength * m_Sway.lossyScale.x);
            float wrapR = r * sc + fr;
            // TransformPoint: a mão esquerda é a direita espelhada (escala -1 em X), e o espelho tem de entrar aqui
            var km = target.TransformPoint(new Vector3(Mathf.Cos(18f * Mathf.Deg2Rad), 0f, Mathf.Sin(18f * Mathf.Deg2Rad)) * (wrapR / Mathf.Max(1e-4f, sc)));
            float lpw = a.lp;
            var wrist = km - Ft * lpw;
            var handRot = Quaternion.LookRotation(Ft, Ut) * Quaternion.Inverse(Quaternion.LookRotation(a.handF, a.handU));

            // IK de dois ossos: ombro → cotovelo → pulso
            var S = m_Sway.TransformPoint(shoulderLocal);
            float L1 = a.L1, L2 = a.L2;
            var sw = wrist - S;
            float c = sw.magnitude;
            // alvo longe (arco, mão de apoio no guarda-mão): primeiro alonga o braço (fora do ecrã), depois um pouco o
            // antebraço; só no fim aproxima o ombro (puxá-lo para a câmara faz um antebraço gigante à frente da cara)
            float su = Mathf.Clamp((c / 0.985f - L2) / L1, 1f, MaxUpperStretch);
            L1 *= su;
            float sf = Mathf.Clamp((c / 0.985f - L1) / L2, 1f, MaxForeStretch);
            L2 *= sf;
            ScaleAlong(a.upper, a.upperAxis, a.upperScale0, su);
            ScaleAlong(a.fore, a.foreAxis, a.foreScale0, sf);
            float maxReach = (L1 + L2) * 0.985f;
            if (c > maxReach)
            {
                // o ombro aproxima-se do pulso (fica fora do ecrã de qualquer forma)
                S = wrist - sw.normalized * maxReach;
                sw = wrist - S;
                c = maxReach;
            }
            c = Mathf.Max(c, Mathf.Abs(L1 - L2) + 1e-3f);
            var dir = sw / Mathf.Max(c, 1e-5f);
            float cosA = Mathf.Clamp((L1 * L1 + c * c - L2 * L2) / (2f * L1 * c), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            var bend = Vector3.ProjectOnPlane(elbowHint - S, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(-m_Sway.up, dir);
            bend.Normalize();
            var E = S + dir * (L1 * cosA) + bend * (L1 * sinA);

            a.upper.position = S;
            a.upper.localRotation = a.upperRest;
            var cur = a.upper.rotation * a.upperAxis;
            a.upper.rotation = Quaternion.FromToRotation(cur, E - S) * a.upper.rotation;
            a.fore.position = E;
            // antebraço: aponta ao pulso e torce com a mão (sem "embrulho de rebuçado" no pulso)
            var fwd = (wrist - E).normalized;
            var up = Vector3.ProjectOnPlane(Ut, fwd);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(handRot * a.handU, fwd);
            a.fore.rotation = Quaternion.LookRotation(fwd, up.normalized) * Quaternion.Inverse(Quaternion.LookRotation(a.foreAxis, a.foreUp));
            a.hand.rotation = handRot;
            a.hand.position = wrist;

            // dedos: cada falange é uma corda do círculo de raio wrapR (o dedo abraça a pega); o polegar e o
            // indicador no gatilho (pose pistola) seguem a tabela da pose
            var table = pose == HandPose.Fist ? Fist : pose == HandPose.Can ? Can : pose == HandPose.Support ? Support : Pistol;
            // polegar: aponta a um ponto do outro lado da pega (pistola/lata/punho) e dobra pouco
            var t1 = a.fingers[0][0];
            if (t1 != null)
            {
                t1.localRotation = a.fingerRest[0][0];
                Vector3 tl;   // espaço da mão estilizada (eixo da pega = +Y, nós em +X, frente = +Z)
                switch (pose)
                {
                    case HandPose.Fist: tl = new Vector3(-0.2f, 0.9f, 1.0f); break;      // por cima dos dedos fechados
                    case HandPose.Support: tl = new Vector3(-0.7f, 0.6f, 0.9f); break;   // ao longo do cano
                    default: tl = new Vector3(-0.85f, 0.75f, 0.75f); break;              // do lado de lá da pega, para a frente
                }
                var tw = target.TransformPoint(Vector3.Scale(tl, new Vector3(wrapR, wrapR + 0.012f, wrapR)) / Mathf.Max(1e-4f, sc));
                var tdir = a.fingers[0][1] != null ? a.fingers[0][1].position - t1.position : t1.forward;
                var want = tw - t1.position;
                if (want.sqrMagnitude > 1e-8f) t1.rotation = Quaternion.FromToRotation(tdir, want) * t1.rotation;
            }
            for (int f = 0; f < 5; f++)
            {
                bool fixedPose = f == 0 || (f == 1 && (pose == HandPose.Pistol || pose == HandPose.Can));
                float h0 = HalfArc(a.seg[f][0], wrapR), h1 = HalfArc(a.seg[f][1], wrapR), h2 = HalfArc(a.seg[f][2], wrapR);
                for (int i = (f == 0 ? 1 : 0); i < 3; i++)
                {
                    var bn = a.fingers[f][i];
                    if (bn == null) continue;
                    float ang = fixedPose ? table[f][i] : Mathf.Min(105f, i == 0 ? h0 + WrapExtra : i == 1 ? h0 + h1 : h1 + h2);
                    bn.localRotation = a.fingerRest[f][i] * Quaternion.AngleAxis(ang, a.flexAxis[f][i]);
                }
            }
        }
    }
}
