using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Ring = FPSLAN.OrganicMesh.Ring;

namespace FPSLAN
{
    /// <summary>O que cobre o antebraço na 1.ª pessoa.</summary>
    public enum ArmWear
    {
        Bare,       // braço nu (camisola de alças / colete sem mangas)
        Rolled,     // manga arregaçada perto do cotovelo
        Suit,       // casaco de fato, com o punho da camisa à vista
        Denim,      // casaco de ganga, punho largo com botão
        Tactical,   // fato tático com luva sem dedos e correia
    }

    /// <summary>Cores e constituição das mãos/braços na 1.ª pessoa (tiradas dos modelos 3D de cada personagem).</summary>
    public struct HandPalette
    {
        public Color skin, sleeve, cuff, glove, trim;
        public ArmWear wear;
        public bool fingerless;     // luva sem dedos (palma e 1.ª falange com a cor da luva)
        public bool knucklePlates;  // placas nos nós dos dedos (luva tática)
        /// <summary>Constituição: 1 = normal, &gt;1 = antebraços mais musculados.</summary>
        public float build;
    }

    /// <summary>Como a mão fecha à volta do objeto.</summary>
    public enum HandPose
    {
        Pistol,     // punho de pistola: indicador estendido no gatilho, polegar do outro lado
        Can,        // lata: indicador por cima, a carregar na válvula
        Fist,       // punho fechado à volta de um cilindro (dinamite, arco, corda)
        Support,    // mão de apoio por baixo do cano
    }

    /// <summary>
    /// Mãos e antebraços estilizados da vista em 1.ª pessoa: cada mão e cada antebraço é UMA malha orgânica (tubos
    /// suaves com cor por vértice, ver <see cref="OrganicMesh"/>) com o mesmo cel shading e contorno das armas.
    /// Substituem os braços dos modelos 3D (gerados a partir de imagens, com dedos fundidos), que não conseguem
    /// segurar a arma de forma convincente.
    ///
    /// Espaço da mão: origem no eixo do objeto agarrado (cilindro de raio r), +Y ao longo desse eixo, +Z para onde
    /// apontam os dedos/antebraço (o pulso fica em -Z), palma do lado +X (mão direita). A mão esquerda é espelhada.
    /// Espaço do antebraço: +Y do pulso para o cotovelo, +X para o lado do polegar.
    /// </summary>
    public static class StylizedHands
    {
        // ------------------------------------------------------------------ paletas por personagem

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        /// <summary>Cores das mãos/braços do personagem (manga, punho, pele, luva) iguais às do seu modelo.</summary>
        public static HandPalette For(BrawlerDefinition b, CharacterLook look)
        {
            var p = new HandPalette
            {
                skin = look != null ? look.skin : new Color(0.85f, 0.62f, 0.45f),
                sleeve = look != null ? look.outfit : Color.gray,
                wear = ArmWear.Suit,
                build = 1f,
            };
            p.cuff = Art.Darken(p.sleeve, 0.8f);
            p.glove = Art.Darken(look != null ? look.gear : Color.gray, 0.85f);
            p.trim = Art.Darken(p.sleeve, 0.65f);
            switch (b != null ? b.brawlerId : null)
            {
                // Nota: o cel shading clareia bastante a cor base (~1.5-1.8x nas zonas iluminadas), por isso os tons
                // de pele aqui são mais escuros do que parecem no ecrã. Referência: Mr.S (#5A3122) bate com o modelo.
                case "gunslinger":  // Ekko: colete sem mangas, braços nus, o mais musculado e de pele mais escura
                    p.skin = Hex("#45251A"); p.wear = ArmWear.Bare; p.build = 1.7f; break;
                case "artillery":   // Lupus Marinus: camisa creme com as mangas arregaçadas
                    p.skin = Hex("#C58663"); p.sleeve = Hex("#EEE6D3"); p.cuff = Hex("#D8CCB2"); p.wear = ArmWear.Rolled; p.build = 1.42f; break;
                case "trapper":     // N.Cage: camisola de alças, braços nus e musculados
                    p.skin = Hex("#5C3726"); p.wear = ArmWear.Bare; p.build = 1.6f; break;
                case "sprayer":     // Mr.S: casaco de ganga azul-claro com botões dourados
                    p.skin = Hex("#5A3122"); p.sleeve = Hex("#8CB6E4"); p.cuff = Hex("#6E9DD3"); p.trim = Hex("#E2B54A"); p.wear = ArmWear.Denim; break;
                case "shotgunner":  // Riot: fato tático preto, luvas sem dedos com placas nos nós
                    p.skin = Hex("#50301F"); p.sleeve = Hex("#2E2D34"); p.cuff = Hex("#45444D"); p.glove = Hex("#1C1B21"); p.trim = Hex("#6B6A75");
                    p.fingerless = true; p.knucklePlates = true; p.wear = ArmWear.Tactical; p.build = 1.38f; break;
                case "sniper":      // Lince: fato azul-escuro, punho da camisa branco com botão de punho dourado
                    p.skin = Hex("#7A4A31"); p.sleeve = Hex("#272C4E"); p.cuff = Hex("#F2F2F4"); p.trim = Hex("#E2B54A"); p.build = 1.42f; break;
            }
            return p;
        }

        // ------------------------------------------------------------------ mão

        /// <summary>Altura do topo da lata (pose Can) no espaço da mão; o indicador vai até lá.</summary>
        public const float DefaultCanTop = 0.095f;

        static readonly float[] FingerY = { 0.027f, 0.009f, -0.009f, -0.026f };
        static readonly float[] FingerLen = { 1f, 1.07f, 1f, 0.8f };
        static readonly float[] FingerRad = { 0.0105f, 0.011f, 0.0105f, 0.0092f };
        static readonly float[] SegLen = { 0.042f, 0.027f, 0.022f };

        const int FingerSides = 10;

        /// <summary>
        /// Constrói uma mão à volta de um cilindro de raio 'radius'. Devolve a raiz (no eixo do cilindro) com um filho
        /// "Wrist" no pulso, de onde sai o antebraço.
        /// </summary>
        /// <param name="indexTip">Pose Can: ponta do indicador (espaço da mão) — o dedo passa por cima do bocal até lá.</param>
        /// <param name="valveTop">Pose Can com indexTip: altura do topo do bocal (espaço da mão).</param>
        public static Transform BuildHand(Transform parent, HandPose pose, bool left, float radius, HandPalette pal, float canTop = DefaultCanTop,
            Vector3? indexTip = null, float valveTop = 0f, float thumbDrop = 0f)
        {
            var root = new GameObject(left ? "HandL" : "HandR").transform;
            root.SetParent(parent, false);
            root.gameObject.layer = parent.gameObject.layer;
            if (left) root.localScale = new Vector3(-1f, 1f, 1f);
            var tag = root.gameObject.AddComponent<HandPoseTag>();
            tag.pose = pose; tag.radius = radius; tag.left = left;
            float r = radius;
            var palmCol = pal.fingerless ? pal.glove : pal.skin;

            // nós dos dedos à frente-direita do cilindro; pulso para trás, ao longo da tangente
            const float knuckleAngle = 18f;
            float rk = r + 0.017f;
            var radial = Dir(knuckleAngle);
            var back = new Vector3(Mathf.Sin(knuckleAngle * Mathf.Deg2Rad), 0f, -Mathf.Cos(knuckleAngle * Mathf.Deg2Rad));
            var km = radial * rk;
            var wrist = km + back * 0.088f + radial * 0.006f + new Vector3(0f, -0.004f, 0f);
            var wristT = new GameObject("Wrist").transform;
            wristT.SetParent(root, false);
            wristT.localPosition = wrist;
            wristT.localRotation = Quaternion.LookRotation(back, Vector3.up);

            // A malha só depende da pose, das medidas e da paleta (a mão esquerda é a mesma, espelhada pela escala):
            // gera-se uma vez e reutiliza-se nos próximos spawns, sem engasgar o jogo.
            string key = "H|" + pose + "|" + r.ToString("F4") + "|" + canTop.ToString("F4") + "|" +
                (indexTip.HasValue ? indexTip.Value.ToString("F4") + "|" + valveTop.ToString("F4") : "-") + "|" + thumbDrop.ToString("F3") + "|" + PaletteKey(pal);
            if (TryAttachCached(key, root, "HandMesh", "HandDetail")) return root;
            var mesh = new OrganicMesh();
            var detail = new OrganicMesh();

            // palma / costas da mão: achatada, mais larga nos nós dos dedos do que no pulso
            {
                var dir = (km - wrist).normalized;
                var off = radial * 0.002f;
                mesh.Tube(OrganicMesh.Smooth(new[]
                {
                    new Ring(wrist - dir * 0.004f + off, 0.026f, 0.0135f, palmCol),
                    new Ring(Vector3.Lerp(wrist, km, 0.4f) + off, 0.037f, 0.0165f, palmCol),
                    new Ring(Vector3.Lerp(wrist, km, 0.78f) + off, 0.0405f, 0.016f, palmCol),
                    new Ring(km + dir * 0.006f + off - new Vector3(0f, 0.001f, 0f), 0.038f, 0.0125f, palmCol),
                }, 3), Vector3.up, true, true, 14, 3);
                // base do polegar (eminência tenar), do lado de dentro
                mesh.Ellipsoid(Vector3.Lerp(km, wrist, 0.62f) - radial * 0.008f + new Vector3(0f, 0.022f, 0f),
                    Quaternion.LookRotation(km - wrist, Vector3.up), new Vector3(0.016f, 0.018f, 0.024f), palmCol, 12);
            }

            // dedos
            for (int f = 0; f < 4; f++)
            {
                float fr = FingerRad[f];
                float y = FingerY[f];
                var k = radial * (rk - 0.002f) + new Vector3(0f, y, 0f);
                Vector3 a, b2, c;
                bool wraps = false;
                if (f == 0 && pose == HandPose.Pistol)
                {
                    // indicador ao longo da arma, a dobrar para o gatilho
                    a = k + new Vector3(-0.004f, 0.03f, 0.03f);
                    b2 = a + new Vector3(-0.012f, 0.008f, 0.016f);
                    c = b2 + new Vector3(-0.01f, -0.006f, 0.005f);
                }
                else if (f == 0 && pose == HandPose.Can && indexTip.HasValue)
                {
                    // sobe por trás da lata, a falange do meio tapa o furo de trás do bocal e a ponta do dedo
                    // carrega em cima do bocal (como quem pulveriza)
                    var hole = indexTip.Value;
                    var kh = new Vector3(k.x, 0f, k.z).normalized;
                    var th = new Vector3(hole.x, 0f, hole.z).normalized;
                    var mid = (kh + th).normalized;
                    a = mid * (r * 0.75f + fr + 0.004f) + new Vector3(0f, canTop - 0.006f, 0f);
                    b2 = hole;
                    c = -th * 0.006f + new Vector3(0f, valveTop + fr * 0.55f, 0f);
                }
                else if (f == 0 && pose == HandPose.Can)
                {
                    // indicador por cima da lata, na válvula
                    a = Point(knuckleAngle + 22f, canTop - 0.012f, r + fr + 0.003f);
                    b2 = Point(knuckleAngle + 40f, canTop + 0.01f, r * 0.7f);
                    c = Point(knuckleAngle + 60f, canTop + 0.016f, r * 0.3f);
                }
                else
                {
                    // dedo a envolver o cilindro (a curvatura ajusta-se ao raio)
                    float rf = r + fr + 0.003f;
                    float total = 0f; foreach (var s in SegLen) total += s * FingerLen[f];
                    float maxSweep = (pose == HandPose.Support ? 150f : 205f) - knuckleAngle;
                    float k01 = Mathf.Min(1f, maxSweep * Mathf.Deg2Rad * rf / total);
                    float ang = knuckleAngle;
                    var pts = new Vector3[3];
                    for (int s = 0; s < 3; s++)
                    {
                        ang += SegLen[s] * FingerLen[f] * k01 / rf * Mathf.Rad2Deg;
                        pts[s] = Point(ang, y - s * 0.002f, rf);
                    }
                    a = pts[0]; b2 = pts[1]; c = pts[2];
                    wraps = true;
                }
                Finger(mesh, detail, k, a, b2, c, fr, pal, wraps);
                if (pal.knucklePlates)
                    detail.Ellipsoid(k + radial * (fr * 0.95f), Quaternion.LookRotation(radial, Vector3.up),
                        new Vector3(fr * 1.05f, fr * 1.1f, fr * 0.45f), pal.cuff, 10);
            }

            // polegar: da base da palma à volta das costas do cilindro (do lado oposto aos dedos)
            {
                float tr = 0.0118f;
                float rt = r + tr + 0.002f;
                var thenar = Vector3.Lerp(km, wrist, 0.62f) - radial * 0.008f + new Vector3(0f, 0.022f, 0f);
                var t0 = Vector3.Lerp(km, wrist, 0.6f) - radial * 0.012f + new Vector3(0f, 0.03f, 0f);
                float ang = -35f;
                var ctrl = new List<Ring>();
                var baseCol = pal.fingerless ? pal.glove : pal.skin;
                if (pose == HandPose.Pistol)
                {
                    // a membrana entre o polegar e o indicador cobre as costas do punho; o polegar vai pelo lado
                    // esquerdo, a apontar para a frente (por cima do dedo médio)
                    // membrana: larga ao longo do punho (rx, eixo +Y) mas fina (ry), para não parecer uma placa
                    ctrl.Add(new Ring(thenar, 0.018f, 0.014f, palmCol));
                    ctrl.Add(new Ring(Point(-60f, 0.01f, r + 0.011f), 0.017f, 0.0115f, palmCol));
                    ctrl.Add(new Ring(Point(-95f, 0.013f, r + 0.0105f), 0.015f, 0.0115f, palmCol));
                    ctrl.Add(new Ring(Point(-122f, 0.018f, rt + 0.002f), tr * 1.18f, tr * 1.08f, baseCol));
                    ctrl.Add(new Ring(Point(-150f, 0.023f, rt), tr * 1.04f, tr, pal.skin));
                    ctrl.Add(new Ring(Point(-180f, 0.025f, rt), tr * 0.96f, tr * 0.93f, pal.skin));
                    ctrl.Add(new Ring(Point(-203f, 0.022f, rt - 0.001f), tr * 0.86f, tr * 0.82f, pal.skin));
                }
                else if (pose == HandPose.Support)
                {
                    // mão de apoio: polegar deitado ao longo do cano (+Y), do lado de dentro
                    ctrl.Add(new Ring(thenar, 0.018f, 0.016f, palmCol));
                    ctrl.Add(new Ring(t0, tr * 1.3f, tr * 1.2f, baseCol));
                    ctrl.Add(new Ring(Point(-48f, 0.058f, rt), tr * 1.06f, tr * 1.02f, pal.skin));
                    ctrl.Add(new Ring(Point(-55f, 0.083f, rt), tr * 0.97f, tr * 0.95f, pal.skin));
                    ctrl.Add(new Ring(Point(-60f, 0.102f, rt - 0.002f), tr * 0.88f, tr * 0.85f, pal.skin));
                }
                else
                {
                    // punho fechado: o polegar dá a volta por cima dos dedos e a ponta curva para dentro
                    // (lata agarrada mais acima: o polegar fica mais baixo, no corpo da lata e não no ombro)
                    float ty = -thumbDrop;
                    t0 += new Vector3(0f, ty * 0.6f, 0f);
                    var t1 = Point(ang -= 0.03f / rt * Mathf.Rad2Deg * 0.8f, 0.036f + ty, rt);
                    var t2 = Point(ang -= 0.025f / rt * Mathf.Rad2Deg, 0.039f + ty, rt);
                    var t3 = Point(ang -= 0.02f / rt * Mathf.Rad2Deg, 0.037f + ty, rt - 0.003f);
                    ctrl.Add(new Ring(thenar, 0.018f, 0.016f, palmCol));
                    ctrl.Add(new Ring(t0, tr * 1.3f, tr * 1.2f, baseCol));
                    ctrl.Add(new Ring(t1, tr * 1.06f, tr * 1.02f, pal.skin));
                    ctrl.Add(new Ring(t2, tr * 0.97f, tr * 0.95f, pal.skin));
                    ctrl.Add(new Ring(t3, tr * 0.88f, tr * 0.85f, pal.skin));
                }
                var tube = OrganicMesh.Smooth(ctrl, 3);
                mesh.Tube(tube, Vector3.up, true, true, FingerSides, 3);
                AddNail(detail, tube, pal, Vector3.zero, true);
            }

            // pulso: liga a mão ao antebraço (um pouco mais grosso nos personagens musculados)
            {
                // mesma secção que o início do antebraço (ArmAt), para a junção não fazer degrau
                var s0 = ArmAt(0f, Mathf.Max(0.6f, pal.build));
                var wc = pal.fingerless || pal.wear == ArmWear.Tactical ? pal.glove : pal.skin;
                mesh.Tube(OrganicMesh.Smooth(new[]
                {
                    new Ring(wrist - back * 0.02f, 0.025f, 0.0155f, palmCol),
                    new Ring(wrist, s0.x * 0.92f, s0.y * 0.9f, wc),
                    new Ring(wrist + back * 0.012f, s0.x * 0.9f, s0.y * 0.88f, wc),
                }, 2), Vector3.up, false, true, 14, 2, 0.45f);
            }
            StoreAndAttach(key, root, mesh, detail, "HandMesh", "HandDetail");
            return root;
        }

        // ------------------------------------------------------------------ cache de malhas

        static readonly Dictionary<string, (Mesh main, Mesh detail)> s_MeshCache = new Dictionary<string, (Mesh, Mesh)>();

        static string PaletteKey(HandPalette p) =>
            ColorUtility.ToHtmlStringRGB(p.skin) + ColorUtility.ToHtmlStringRGB(p.sleeve) + ColorUtility.ToHtmlStringRGB(p.cuff) +
            ColorUtility.ToHtmlStringRGB(p.glove) + ColorUtility.ToHtmlStringRGB(p.trim) + (int)p.wear + (p.fingerless ? "f" : "") +
            (p.knucklePlates ? "k" : "") + p.build.ToString("F3");

        /// <summary>Se a malha já foi gerada, anexa-a (partilhada) e devolve true.</summary>
        static bool TryAttachCached(string key, Transform root, string mainName, string detailName)
        {
            if (!s_MeshCache.TryGetValue(key, out var e) || e.main == null) return false;
            OrganicMesh.AttachShared(root, mainName, e.main, true);
            if (e.detail != null) OrganicMesh.AttachShared(root, detailName, e.detail, true);
            return true;
        }

        /// <summary>Gera as malhas, guarda-as na cache (ficam para o resto da sessão) e anexa-as.</summary>
        static void StoreAndAttach(string key, Transform root, OrganicMesh mesh, OrganicMesh detail, string mainName, string detailName)
        {
            var main = mesh.ToMesh(mainName);
            var det = detail.IsEmpty ? null : detail.ToMesh(detailName);
            s_MeshCache[key] = (main, det);
            OrganicMesh.AttachShared(root, mainName, main, true);
            if (det != null) OrganicMesh.AttachShared(root, detailName, det, true);
        }

        static Vector3 Dir(float deg) => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0f, Mathf.Sin(deg * Mathf.Deg2Rad));
        static Vector3 Point(float deg, float y, float rad) => Dir(deg) * rad + new Vector3(0f, y, 0f);

        /// <summary>Dedo contínuo (nó → 3 falanges → ponta arredondada), mais grosso no nó e a afinar até à ponta.</summary>
        static void Finger(OrganicMesh mesh, OrganicMesh detail, Vector3 k, Vector3 a, Vector3 b, Vector3 c, float fr, HandPalette pal, bool wraps)
        {
            var first = pal.fingerless ? pal.glove : pal.skin;
            var tube = OrganicMesh.Smooth(new[]
            {
                new Ring(k, fr * 1.18f, fr * 1.1f, first),
                new Ring(Vector3.Lerp(k, a, 0.55f), fr * 1.0f, fr * 0.97f, first),
                new Ring(a, fr * 1.04f, fr * 1.0f, pal.skin),     // articulação ligeiramente mais grossa
                new Ring(Vector3.Lerp(a, b, 0.5f), fr * 0.93f, fr * 0.9f, pal.skin),
                new Ring(b, fr * 0.95f, fr * 0.92f, pal.skin),
                new Ring(c, fr * 0.86f, fr * 0.8f, pal.skin),
            }, 2);
            mesh.Tube(tube, Vector3.up, true, true, FingerSides, 3);
            AddNail(detail, tube, pal, wraps ? new Vector3(c.x, 0f, c.z) : Vector3.zero, wraps);
        }

        /// <summary>Unha na ponta do dedo, do lado de fora (afastado do objeto agarrado).</summary>
        static void AddNail(OrganicMesh detail, List<Ring> tube, HandPalette pal, Vector3 outwardHint, bool enabled)
        {
            if (!enabled || tube.Count < 3) return;
            var tip = tube[tube.Count - 1];
            var prev = tube[tube.Count - 3];
            var t = (tip.p - prev.p);
            if (t.sqrMagnitude < 1e-10f) return;
            t.Normalize();
            var outward = outwardHint.sqrMagnitude > 1e-8f ? outwardHint : new Vector3(tip.p.x, 0f, tip.p.z);
            outward -= t * Vector3.Dot(outward, t);
            if (outward.sqrMagnitude < 1e-8f) return;
            outward.Normalize();
            float fr = tip.ry;
            var nail = Color.Lerp(pal.skin, new Color(1f, 0.86f, 0.8f), 0.28f);
            detail.Ellipsoid(tip.p + t * (fr * 0.3f) + outward * (fr * 0.62f), Quaternion.LookRotation(t, outward),
                new Vector3(fr * 0.72f, fr * 0.42f, fr * 0.85f), nail, 8);
        }

        // ------------------------------------------------------------------ antebraço

        const float ArmLength = 0.62f;
        const int ArmSides = 18;

        // Perfil anatómico do antebraço (pulso → cotovelo): y, meia-largura, meia-espessura, desvio para o lado do
        // polegar (ventre do músculo braquiorradial).
        static readonly Vector4[] ArmProfile =
        {
            new Vector4(0.00f, 0.0285f, 0.0205f, 0f),
            new Vector4(0.04f, 0.0315f, 0.0235f, 0f),
            new Vector4(0.10f, 0.0385f, 0.0305f, 0.002f),
            new Vector4(0.17f, 0.0450f, 0.0365f, 0.004f),
            new Vector4(0.25f, 0.0475f, 0.0390f, 0.005f),
            new Vector4(0.34f, 0.0465f, 0.0400f, 0.004f),
            new Vector4(0.45f, 0.0450f, 0.0410f, 0.002f),
            new Vector4(0.62f, 0.0460f, 0.0430f, 0f),
        };

        /// <summary>Meia-largura/espessura e desvio do braço nu a uma distância 'y' do pulso.</summary>
        static Vector3 ArmAt(float y, float build)
        {
            int i = 0;
            while (i < ArmProfile.Length - 2 && ArmProfile[i + 1].x < y) i++;
            var p0 = ArmProfile[i]; var p1 = ArmProfile[i + 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(p0.x, p1.x, y));
            // a musculatura engrossa sobretudo o ventre do antebraço; o pulso cresce menos (forma em "V")
            float muscle = Mathf.Clamp01(build - 1f);
            float m = 1f + (build - 1f) * Mathf.Lerp(0.45f - 0.15f * muscle, 1f, Mathf.SmoothStep(0f, 1f, y / 0.2f));
            float rx = Mathf.Lerp(p0.y, p1.y, t) * m, ry = Mathf.Lerp(p0.z, p1.z, t) * m, off = Mathf.Lerp(p0.w, p1.w, t) * m;
            if (muscle > 0f)
            {
                // braquiorradial (lado do polegar) bem marcado e flexores mais perto do cotovelo
                float br = Gauss(y, 0.2f, 0.07f), fl = Gauss(y, 0.3f, 0.08f);
                rx += muscle * (0.011f * br + 0.004f * fl);
                ry += muscle * (0.004f * br + 0.009f * fl);
                off += muscle * 0.007f * br;
            }
            return new Vector3(rx, ry, off);
        }

        /// <summary>
        /// Antebraço ao longo de +Y local (do pulso para o cotovelo), com a roupa de cada personagem. Por defeito é
        /// comprido (o cotovelo fica fora do ecrã); com um comprimento anatómico (~0.3) acaba num cotovelo arredondado.
        /// </summary>
        public static Transform BuildForearm(Transform parent, HandPalette pal, float length = ArmLength)
        {
            var arm = new GameObject("Forearm").transform;
            arm.SetParent(parent, false);
            arm.gameObject.layer = parent.gameObject.layer;
            var mesh = new OrganicMesh();
            var detail = new OrganicMesh();
            float b = Mathf.Max(0.6f, pal.build);
            string key = "F|" + PaletteKey(pal) + "|" + length.ToString("F3");
            if (TryAttachCached(key, arm, "ForearmMesh", "ForearmDetail")) return arm;

            // pele: só o que fica à vista (o braço por baixo de uma manga comprida não é preciso)
            float skinEnd = pal.wear == ArmWear.Bare ? length : pal.wear == ArmWear.Rolled ? Mathf.Min(0.4f, length) : 0.06f;
            {
                var rings = new List<Ring>();
                int steps = Mathf.Max(3, Mathf.CeilToInt(skinEnd / 0.025f));
                // começa um pouco dentro do pulso da mão, mais fino, e fecha arredondado (sem aresta à vista)
                rings.Add(new Ring(new Vector3(0f, -0.006f, 0f), ArmAt(0f, b).x * 0.86f, ArmAt(0f, b).y * 0.84f, pal.skin));
                for (int i = 0; i <= steps; i++)
                {
                    float y = Mathf.Lerp(0f, skinEnd, i / (float)steps);
                    var s = ArmAt(y, b);
                    rings.Add(new Ring(new Vector3(s.z, y, 0f), s.x, s.y, pal.skin));
                }
                mesh.Tube(OrganicMesh.Smooth(rings, 1), Vector3.right, true, true, ArmSides, 3, 0.4f);
                // antebraço curto (cotovelo à vista): cotovelo arredondado, com o olécrano a sair um pouco para trás
                if (pal.wear == ArmWear.Bare && length < ArmLength - 0.01f)
                {
                    var e = ArmAt(length, b);
                    mesh.Ellipsoid(new Vector3(e.z * 0.5f, length, -e.y * 0.12f), Quaternion.identity, new Vector3(e.x * 0.97f, e.y * 1.25f, e.y * 1.05f), pal.skin, 16);
                }
            }

            switch (pal.wear)
            {
                case ArmWear.Rolled:
                    // manga arregaçada: rolo grosso e irregular, e a manga até ao cotovelo
                    {
                        var rings = new List<Ring>();
                        for (int i = 0; i <= 10; i++)
                        {
                            float y = Mathf.Lerp(0.3f, 0.41f, i / 10f);
                            var s = ArmAt(y, b);
                            float roll = 0.011f + 0.004f * Mathf.Sin(i / 10f * Mathf.PI * 3f);
                            var col = i % 4 == 3 ? pal.cuff : pal.sleeve;
                            rings.Add(new Ring(new Vector3(s.z, y, 0f), s.x + roll, s.y + roll, col));
                        }
                        Sleeve(mesh, 0.4f, length, b, 0.008f, pal.sleeve, 0f);
                        mesh.Tube(Hem(rings), Vector3.right, true, false, ArmSides, 2, 0.05f);
                    }
                    break;

                case ArmWear.Suit:
                    // punho da camisa (branco) a sair da manga do casaco, com botão de punho
                    {
                        var shirt = new List<Ring>();
                        for (int i = 0; i <= 4; i++)
                        {
                            float y = Mathf.Lerp(-0.006f, 0.045f, i / 4f);
                            var s = ArmAt(Mathf.Max(0f, y), b);
                            float e = i == 0 ? 0.0035f : 0.0055f;
                            shirt.Add(new Ring(new Vector3(s.z, y, 0f), s.x + e, s.y + e, pal.cuff));
                        }
                        mesh.Tube(Hem(shirt), Vector3.right, true, false, ArmSides, 2, 0.05f);
                        var cl = ArmAt(0.012f, b);
                        detail.Ellipsoid(new Vector3(cl.z + cl.x + 0.004f, 0.012f, 0f), Quaternion.identity, new Vector3(0.003f, 0.0055f, 0.0055f), pal.trim, 8);
                        Sleeve(mesh, 0.028f, length, b, 0.011f, pal.sleeve, 0f, Art.Darken(pal.sleeve, 0.8f));
                    }
                    break;

                case ArmWear.Denim:
                    // punho largo de ganga com botão dourado; a manga faz pregas
                    {
                        var cuff = new List<Ring>();
                        for (int i = 0; i <= 4; i++)
                        {
                            float y = Mathf.Lerp(0.004f, 0.05f, i / 4f);
                            var s = ArmAt(y, b);
                            // costura (linha mais escura) junto à manga
                            var c = i == 4 ? Art.Darken(pal.cuff, 0.78f) : pal.cuff;
                            cuff.Add(new Ring(new Vector3(s.z * 0.6f, y, 0f), s.x + 0.017f, s.y + 0.017f, c));
                        }
                        mesh.Tube(Hem(cuff), Vector3.right, true, false, ArmSides, 2, 0.05f);
                        var cb = ArmAt(0.027f, b);
                        detail.Ellipsoid(new Vector3(cb.z * 0.6f + cb.x + 0.018f, 0.027f, 0f), Quaternion.identity, new Vector3(0.003f, 0.0065f, 0.0065f), pal.trim, 8);
                        // a manga nasce por dentro do punho (mais estreita na entrada): sem bainhas sobrepostas
                        Sleeve(mesh, 0.038f, length, b, 0.012f, pal.sleeve, 1f);
                    }
                    break;

                case ArmWear.Tactical:
                    // punho almofadado da luva por cima do pulso, manga tática com uma correia
                    {
                        var g = new List<Ring>();
                        for (int i = 0; i <= 5; i++)
                        {
                            float y = Mathf.Lerp(-0.018f, 0.05f, i / 5f);
                            var s = ArmAt(Mathf.Max(0f, y), b);
                            float pad = 0.006f + 0.0025f * Mathf.Sin(i / 5f * Mathf.PI);
                            g.Add(new Ring(new Vector3(s.z, y, 0f), s.x + pad, s.y + pad, pal.glove));
                        }
                        mesh.Tube(Hem(g), Vector3.right, true, false, ArmSides, 2, 0.05f);
                        Sleeve(mesh, 0.035f, length, b, 0.009f, pal.sleeve, 0f);
                        var strap = new List<Ring>();
                        for (int i = 0; i <= 3; i++)
                        {
                            float y = Mathf.Lerp(0.15f, 0.185f, i / 3f);
                            var s = ArmAt(y, b);
                            strap.Add(new Ring(new Vector3(s.z, y, 0f), s.x + 0.0135f, s.y + 0.0135f, pal.cuff));
                        }
                        mesh.Tube(strap, Vector3.right, false, false, ArmSides);
                        var sb = ArmAt(0.167f, b);
                        detail.Ellipsoid(new Vector3(sb.z + sb.x + 0.014f, 0.167f, 0f), Quaternion.identity, new Vector3(0.004f, 0.012f, 0.011f), pal.trim, 8);
                    }
                    break;
            }

            StoreAndAttach(key, arm, mesh, detail, "ForearmMesh", "ForearmDetail");
            return arm;
        }

        /// <summary>
        /// Braço (do cotovelo ao ombro) ao longo de +Y: bíceps à frente (+Z), tríceps atrás e deltoide no ombro.
        /// Só é preciso quando o cotovelo fica à vista (braço que puxa a corda do arco).
        /// </summary>
        public static Transform BuildUpperArm(Transform parent, HandPalette pal, float length = 0.34f)
        {
            var arm = new GameObject("UpperArm").transform;
            arm.SetParent(parent, false);
            arm.gameObject.layer = parent.gameObject.layer;
            float b = Mathf.Max(0.6f, pal.build);
            string key = "U|" + PaletteKey(pal) + "|" + length.ToString("F3");
            if (TryAttachCached(key, arm, "UpperArmMesh", null)) return arm;
            float muscle = Mathf.Clamp01(b - 1f);
            float k = 1f + (b - 1f) * 0.8f;
            var col = pal.wear == ArmWear.Bare ? pal.skin : pal.sleeve;
            var rings = new List<Ring>();
            for (int i = 0; i <= 14; i++)
            {
                float y = length * i / 14f;
                float t = y / length;
                float rx = Mathf.Lerp(0.046f, 0.058f, t) + 0.006f * Gauss(t, 0.45f, 0.2f);
                float ry = Mathf.Lerp(0.044f, 0.054f, t) + (0.008f + 0.012f * muscle) * Gauss(t, 0.45f, 0.17f);
                rx += 0.008f * Gauss(t, 0.92f, 0.12f) * (1f + muscle);   // deltoide
                // o bíceps faz volume à frente: desloca o centro um pouco para +Z
                float fwd = (0.004f + 0.006f * muscle) * Gauss(t, 0.45f, 0.17f);
                rings.Add(new Ring(new Vector3(0f, y, fwd), rx * k, ry * k, col));
            }
            var mesh = new OrganicMesh();
            mesh.Tube(rings, Vector3.right, true, true, ArmSides, 3, 0.7f);
            StoreAndAttach(key, arm, mesh, new OrganicMesh(), "UpperArmMesh", null);
            return arm;
        }

        /// <summary>Coloca um segmento (+Y local) de 'from' a 'to', com a frente (+Z) virada para 'front'.</summary>
        public static void PoseLimb(Transform limb, Vector3 from, Vector3 to, Vector3 front)
        {
            if (limb == null) return;
            var d = to - from;
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            var z = front - d * Vector3.Dot(front, d);
            limb.position = from;
            limb.rotation = z.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(z.normalized, d) : Quaternion.FromToRotation(Vector3.up, d);
        }

        /// <summary>Manga de tecido (mais solta e lisa do que o braço), de y0 até ao cotovelo, com bainha na entrada.</summary>
        static void Sleeve(OrganicMesh mesh, float y0, float y1, float build, float ease, Color col, float folds, Color? hem = null)
        {
            var rings = new List<Ring>();
            int steps = Mathf.Max(4, Mathf.CeilToInt((y1 - y0) / 0.02f));
            for (int i = 0; i <= steps; i++)
            {
                float y = Mathf.Lerp(y0, y1, i / (float)steps);
                var s = ArmAt(y, build);
                // o tecido não segue o músculo: suaviza o perfil e alarga para o cotovelo
                float loose = ease + 0.006f * Mathf.SmoothStep(0f, 1f, (y - y0) / 0.25f);
                float fold = folds * 0.0045f * (Gauss(y, y0 + 0.07f, 0.018f) + Gauss(y, y0 + 0.15f, 0.02f) * 0.8f + Gauss(y, y0 + 0.24f, 0.022f) * 0.6f);
                var c = hem.HasValue && y - y0 < 0.012f ? hem.Value : col;
                rings.Add(new Ring(new Vector3(s.z * 0.6f, y, 0f), s.x + loose + fold, s.y + loose + fold * 0.8f, c));
            }
            // a ponta do cotovelo fecha: um tubo aberto deixa ver o contorno (faces de trás) por dentro, a preto
            mesh.Tube(Hem(rings), Vector3.right, true, true, ArmSides, 2, 0.05f);
        }

        static float Gauss(float x, float mu, float sigma) => Mathf.Exp(-(x - mu) * (x - mu) / (2f * sigma * sigma));

        /// <summary>
        /// Entrada de uma manga: um rebordo do tecido e, por dentro, uma tampa escura quase plana (usar com
        /// capStart e capLength pequeno). O braço atravessa a tampa e "sai" da manga. Não se dobra o tubo para dentro:
        /// numa dobra as normais ficam ambíguas e o contorno faz dentes pretos.
        /// </summary>
        static List<Ring> Hem(List<Ring> rings)
        {
            if (rings == null || rings.Count < 2) return rings;
            var r0 = rings[0];
            var o = new List<Ring>
            {
                new Ring(r0.p, r0.rx * 0.88f, r0.ry * 0.88f, Art.Darken(r0.c, 0.4f)),
                new Ring(r0.p, r0.rx * 0.99f, r0.ry * 0.99f, Art.Darken(r0.c, 0.85f)),
            };
            o.AddRange(rings);
            return o;
        }

        /// <summary>Coloca o antebraço no pulso da mão, a apontar para o cotovelo (fora do ecrã), com a largura
        /// alinhada com a mão (lado do polegar = +X).</summary>
        public static void PoseForearm(Transform arm, Transform hand, Vector3 elbowWorld)
        {
            if (arm == null || hand == null) return;
            var wrist = hand.Find("Wrist");
            var p = wrist != null ? wrist.position : hand.position;
            var d = elbowWorld - p;
            if (d.sqrMagnitude < 1e-6f) return;
            d.Normalize();
            var x = hand.up - d * Vector3.Dot(hand.up, d);
            arm.position = p;
            arm.rotation = x.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(Vector3.Cross(x.normalized, d), d) : Quaternion.FromToRotation(Vector3.up, d);
            if (arm.gameObject.activeSelf != hand.gameObject.activeInHierarchy) arm.gameObject.SetActive(hand.gameObject.activeInHierarchy);
        }
    }
}
