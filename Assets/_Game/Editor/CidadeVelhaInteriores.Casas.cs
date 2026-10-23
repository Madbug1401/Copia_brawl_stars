using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN.EditorTools
{
    /// <summary>
    /// Interiores das restantes casas da Cidade Velha (Pelourinho): sobrados de esquina (2 pisos, varandas), casas térreas
    /// da fila e edifícios das bolsas laterais. Cada casa tem um tema: bar, tasca de grogue com alambique, mercearia,
    /// barbearia, restaurante com palco de morna, casa de família, apartamento, estúdio.
    /// As aberturas (portas, janelas, varandas) saem dos módulos do FBX, por isso a ferramenta acompanha alterações no
    /// script do Blender. A cobertura, a chaminé, os candeeiros e a cantaria (cunhais, cornija, friso) originais ficam;
    /// o bloco de reboco maciço é trocado por paredes com vãos e interior.
    /// </summary>
    public static partial class CidadeVelhaInteriores
    {
        enum Tema { Bar, Mercearia, Casa, Apartamento, Tasca, Estudio, Barbearia, Restaurante, CasaFamilia, CasaTerrea }

        class Abertura
        {
            public int face;        // 0 = z mín · 1 = z máx · 2 = x mín · 3 = x máx
            public int kind;        // 0 porta · 1 janela térrea · 2 janela de cima · 3 porta de varanda
            public Vector3 pos;     // posição do módulo (mundo)
            public float along;     // coordenada ao longo da parede (mundo: x nas faces 0/1, z nas faces 2/3)
            public Vector3 local;   // posição no referencial da planta (x ao longo da frente, z para dentro)
        }

        class Casa
        {
            public string name;
            public float x0, x1, z0, z1, h, y2;
            public bool twoStoreys;
            public Material plaster, shutter;
            public Color inner;
            public Tema ground, upper;
            public int front;
            public Matrix4x4 lay;               // referencial da planta: x ao longo da frente (0 = meio), z para dentro
            public float W, D;
            public int s;                       // lado (±1) da parede lateral com porta (se houver)
            public float sd = -1f;              // profundidade da porta lateral
            public float fd;                    // x da porta da frente
            public List<Abertura> ops = new List<Abertura>();
            public List<Rect> keep = new List<Rect>();
            public List<Rect> keepUp = new List<Rect>();
            public System.Random rng;
            public float iw => W * 0.5f - T;
            public float zb => D - T;
        }

        static readonly (string name, string plaster, Tema ground, Tema upper, Color inner)[] CasasDef =
        {
            ("Esquina_EN", "Ocre", Tema.Bar, Tema.Apartamento, new Color(0.96f, 0.88f, 0.72f)),
            ("Esquina_WN", "Terracota", Tema.Bar, Tema.Apartamento, new Color(0.93f, 0.82f, 0.74f)),
            ("Esquina_ES", "Azul", Tema.Mercearia, Tema.Apartamento, new Color(0.86f, 0.92f, 0.96f)),
            ("Esquina_WS", "Rosa", Tema.Casa, Tema.Apartamento, new Color(0.98f, 0.9f, 0.9f)),
            ("Fila_EN", "Terracota", Tema.Tasca, Tema.Tasca, new Color(0.95f, 0.87f, 0.74f)),
            ("Fila_ES", "Rosa", Tema.CasaTerrea, Tema.CasaTerrea, new Color(0.97f, 0.93f, 0.86f)),
            ("Fila_WN", "Ocre", Tema.Estudio, Tema.Estudio, new Color(0.9f, 0.95f, 0.92f)),
            ("Fila_WS", "Azul", Tema.Barbearia, Tema.Barbearia, new Color(0.92f, 0.95f, 0.98f)),
            ("Bolsa_E", "Branco", Tema.Restaurante, Tema.Restaurante, new Color(0.98f, 0.93f, 0.82f)),
            ("Bolsa_W", "Branco", Tema.CasaFamilia, Tema.CasaFamilia, new Color(0.9f, 0.94f, 0.88f)),
        };

        static readonly Color Chrome = new Color(0.78f, 0.8f, 0.82f);
        static readonly Color CvBlue = new Color(0.0f, 0.25f, 0.6f);
        static readonly Color CvRed = new Color(0.8f, 0.12f, 0.15f);
        static readonly Color CvYellow = new Color(0.98f, 0.8f, 0.1f);

        // =====================================================================================================

        static void Casas(Transform group, Transform cen, Mats M, int colLayer, StringBuilder log)
        {
            int total = 0;
            var root = new GameObject("Casas").transform;
            root.SetParent(group, false);
            foreach (var d in CasasDef)
            {
                var c = ReadCasa(cen, d.name, d.plaster, M, log);
                if (c == null) continue;
                c.ground = d.ground;
                c.upper = d.upper;
                c.inner = d.inner;
                c.rng = new System.Random(d.name.GetHashCode() & 0x7fffffff);
                HideOriginal(cen, c);
                ClearBushes(cen.root, c, log);
                var go = new GameObject(d.name).transform;
                go.SetParent(root, false);
                var k = new InteriorKit();
                SetupKit(k, M);
                k.Floor(0.03f, c.x0, c.z0 - 1.5f, c.x1, c.z1 + 1.5f);
                if (c.twoStoreys) k.Floor(c.y2, c.x0 - 1.5f, c.z0 - 1.5f, c.x1 + 1.5f, c.z1 + 1.5f);
                Shell(k, M, c);
                k.Push(c.lay);
                Interior(k, M, c);
                k.Pop();
                int tris = k.Build(go, "CVI_" + d.name, SaveMesh, m => m == M.sombra ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided);
                MakeColliders(go, k, colLayer);
                MakeLights(go, k);
                total += tris;
                log.AppendLine($"{d.name}: {Nome(c.ground)}{(c.twoStoreys ? " + " + Nome(c.upper) + " no 1.º andar" : "")} · {tris} tri · {k.Cols.Count} colisores");
            }
            log.AppendLine($"Casas: {total} triângulos no total.");
        }

        /// <summary>Percursos de validação (mundo) para cada casa: entra pela porta da frente, sobe ao 1.º andar e sai
        /// para a varanda (sobrados de esquina), ou atravessa a casa e sai pela porta lateral (casas térreas).</summary>
        public static List<(string name, Vector3 start, Vector3[] pts, float expectY)> CasaRoutes(Transform cen)
        {
            var list = new List<(string, Vector3, Vector3[], float)>();
            foreach (var d in CasasDef)
            {
                var c = ReadCasa(cen, d.name, d.plaster, null, new StringBuilder());
                if (c == null) continue;
                Vector3 W(float x, float z) => c.lay.MultiplyPoint3x4(new Vector3(x, 0f, z));
                Vector3 N(float x, float z) => W(x * c.s, z);       // coordenadas nativas da escada
                var start = W(c.fd, -1.3f);
                if (c.twoStoreys)
                {
                    list.Add((d.name + ": porta → escada → 1.º andar → varanda da frente", start, new[]
                    {
                        W(c.fd, 1.2f), N(0.25f, 4.1f), N(0.25f, 5.25f), N(-4.65f, 5.25f), N(-4.7f, 3.5f), N(-0.3f, 3.6f),
                        W(c.fd, 1.0f), W(c.fd, -0.55f),
                    }, c.y2));
                    if (c.sd > 0f)
                        list.Add((d.name + ": 1.º andar → varanda lateral", W(c.fd, 1.0f) + Vector3.up * c.y2, new[]
                        {
                            W(c.s * (c.iw - 1.2f), c.sd), W(c.s * (c.iw + 0.55f), c.sd),
                        }, c.y2));
                }
                else if (c.sd > 0f)
                    list.Add((d.name + ": porta da frente → interior → porta lateral", start, new[]
                    {
                        W(c.fd, 1.2f), W(c.fd, c.sd), W(c.s * (c.iw - 1.0f), c.sd), W(c.s * (c.iw + 1.2f), c.sd),
                    }, 0f));
                else
                    list.Add((d.name + ": porta → sala → porta", start, new[] { W(c.fd, 1.5f), W(c.fd, -1.3f) }, 0f));
            }
            return list;
        }

        /// <summary>Os arbustos (esconderijos) encostados à fachada entravam pela parede e apareciam dentro da casa:
        /// passam para fora da parede, sem mudar de tamanho nem sair do sítio ao longo da fachada.</summary>
        static void ClearBushes(Transform root, Casa c, StringBuilder log)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("BUSH_") || t.parent == null || t.parent.name.StartsWith("BUSH_")) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                const float m = 0.12f;
                if (b.max.x <= c.x0 + 0.01f || b.min.x >= c.x1 - 0.01f || b.max.z <= c.z0 + 0.01f || b.min.z >= c.z1 - 0.01f) continue;
                // empurra para fora pela face com menor deslocamento
                float[] mv = { c.z0 - m - b.max.z, c.z1 + m - b.min.z, c.x0 - m - b.max.x, c.x1 + m - b.min.x };
                int f = 0;
                for (int i = 1; i < 4; i++) if (Mathf.Abs(mv[i]) < Mathf.Abs(mv[f])) f = i;
                t.position += f < 2 ? new Vector3(0f, 0f, mv[f]) : new Vector3(mv[f], 0f, 0f);
                log.AppendLine($"Arbusto {t.name}: afastado {Mathf.Abs(mv[f]):0.00} m da fachada de {c.name} (entrava na casa).");
            }
        }

        static string Nome(Tema t)
        {
            switch (t)
            {
                case Tema.Bar: return "bar";
                case Tema.Mercearia: return "mercearia";
                case Tema.Casa: return "casa (sala e cozinha)";
                case Tema.Apartamento: return "apartamento";
                case Tema.Tasca: return "tasca de grogue com alambique";
                case Tema.Estudio: return "estúdio";
                case Tema.Barbearia: return "barbearia";
                case Tema.Restaurante: return "restaurante com palco de morna";
                case Tema.CasaFamilia: return "casa de família (sala, cozinha e quarto)";
                default: return "casa térrea";
            }
        }

        // ------------------------------------------------------------------ leitura do edifício a partir do FBX

        static Casa ReadCasa(Transform cen, string name, string plaster, Mats M, StringBuilder log)
        {
            var col = FindDeep(cen, "COL_Box_BLD_" + name);
            var corpo = FindDeep(cen, "BLD_" + name + "_Corpo");
            var bc = col != null ? col.GetComponent<BoxCollider>() : null;
            if (bc == null || corpo == null) { log.AppendLine("Aviso: não encontrei " + name); return null; }
            var center = col.TransformPoint(bc.center);
            var size = Vector3.Scale(bc.size, col.lossyScale);
            var c = new Casa { name = name };
            c.x0 = center.x - size.x * 0.5f;
            c.x1 = center.x + size.x * 0.5f;
            c.z0 = center.z - size.z * 0.5f;
            c.z1 = center.z + size.z * 0.5f;
            c.h = corpo.GetComponent<Renderer>().bounds.max.y;
            c.twoStoreys = name.StartsWith("Esquina");
            c.y2 = c.h * 0.5f;
            if (M != null) c.plaster = plaster == "Ocre" ? M.ocre : plaster == "Terracota" ? M.terracota : plaster == "Rosa" ? M.rosa : plaster == "Azul" ? M.azulRb : M.reboco;
            c.shutter = M?.verde;

            foreach (var t in cen.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!n.StartsWith("BLD_" + name + "_")) continue;
                int kind = n.Contains("_PortaVaranda_") ? 3 : n.Contains("_Porta_") ? 0 : n.Contains("_Janela2_") ? 2 : n.Contains("_Janela_") ? 1 : -1;
                if (kind < 0) continue;
                if (kind == 1 && M != null)
                {
                    var r = t.GetComponent<MeshRenderer>();
                    if (r != null && r.sharedMaterials.Length > 1 && r.sharedMaterials[1] != null)
                        c.shutter = r.sharedMaterials[1].name.Contains("Azul") ? M.azul : M.verde;
                }
                var p = t.position;
                float[] dist = { Mathf.Abs(p.z - c.z0), Mathf.Abs(p.z - c.z1), Mathf.Abs(p.x - c.x0), Mathf.Abs(p.x - c.x1) };
                int f = 0;
                for (int i = 1; i < 4; i++) if (dist[i] < dist[f]) f = i;
                c.ops.Add(new Abertura { face = f, kind = kind, pos = p, along = f < 2 ? p.x : p.z });
            }

            // frente = face com porta mais comprida
            float best = -1f;
            foreach (var o in c.ops)
            {
                if (o.kind != 0) continue;
                float len = o.face < 2 ? c.x1 - c.x0 : c.z1 - c.z0;
                if (len > best) { best = len; c.front = o.face; }
            }
            c.W = c.front < 2 ? c.x1 - c.x0 : c.z1 - c.z0;
            c.D = c.front < 2 ? c.z1 - c.z0 : c.x1 - c.x0;
            float mid = c.front < 2 ? (c.x0 + c.x1) * 0.5f : (c.z0 + c.z1) * 0.5f;
            c.lay = FaceFrame(c, c.front) * Matrix4x4.Translate(new Vector3(AlongLocal(c.front, mid), 0f, 0f));
            var inv = c.lay.inverse;
            foreach (var o in c.ops)
            {
                o.local = inv.MultiplyPoint3x4(o.pos);
                if (o.kind == 0 && o.face == c.front) c.fd = o.local.x;
                if (o.kind == 0 && o.face != c.front)
                {
                    c.s = o.local.x > 0f ? 1 : -1;
                    c.sd = o.local.z;
                }
            }
            if (c.s == 0) c.s = c.fd > 0.1f ? -1 : 1;      // sem porta lateral: "lado livre" é o oposto da porta

            // zonas a deixar livres (portas a abrir e passagem)
            c.keep.Add(Rect.MinMaxRect(c.fd - 1.25f, 0f, c.fd + 0.95f, 1.9f));
            if (c.sd > 0f)
            {
                float xa = c.s * (c.iw - 1.9f), xb = c.s * c.iw;
                c.keep.Add(Rect.MinMaxRect(Mathf.Min(xa, xb), c.sd - 1.25f, Mathf.Max(xa, xb), c.sd + 0.95f));
            }
            if (c.sd > 0f)
            {
                // corredor livre entre a porta da frente e a porta lateral
                c.keep.Add(Rect.MinMaxRect(c.fd - 0.5f, 0f, c.fd + 0.5f, c.sd + 0.5f));
                float xa = c.fd, xb = c.s * c.iw;
                c.keep.Add(Rect.MinMaxRect(Mathf.Min(xa, xb), c.sd - 0.5f, Mathf.Max(xa, xb), c.sd + 0.5f));
            }
            if (c.twoStoreys)
            {
                // escada (lado −s, ao fundo) + chegada
                c.keep.Add(SRect(c, -5.25f, 4.45f, 1.2f, c.D));
                c.keepUp.Add(SRect(c, -5.25f, 3.4f, -0.6f, c.D));
                c.keepUp.Add(Rect.MinMaxRect(-1.0f, 0f, 1.0f, 1.7f));
                if (c.sd > 0f)
                {
                    float xa = c.s * (c.iw - 1.7f), xb = c.s * c.iw;
                    c.keepUp.Add(Rect.MinMaxRect(Mathf.Min(xa, xb), c.sd - 1.0f, Mathf.Max(xa, xb), c.sd + 1.0f));
                }
            }
            return c;
        }

        /// <summary>Retângulo dado em coordenadas "nativas" (lado da escada = −x) espelhado pelo lado s.</summary>
        static Rect SRect(Casa c, float xa, float za, float xb, float zb)
        {
            float a = xa * c.s, b = xb * c.s;
            return Rect.MinMaxRect(Mathf.Min(a, b), za, Mathf.Max(a, b), zb);
        }

        static Matrix4x4 FaceFrame(Casa c, int f)
        {
            switch (f)
            {
                case 0: return Matrix4x4.TRS(new Vector3(0f, 0f, c.z0), Quaternion.identity, Vector3.one);
                case 1: return Matrix4x4.TRS(new Vector3(0f, 0f, c.z1), Quaternion.Euler(0f, 180f, 0f), Vector3.one);
                case 2: return Matrix4x4.TRS(new Vector3(c.x0, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), Vector3.one);
                default: return Matrix4x4.TRS(new Vector3(c.x1, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
            }
        }

        static float AlongLocal(int f, float worldAlong) => f == 0 ? worldAlong : f == 1 ? -worldAlong : f == 2 ? -worldAlong : worldAlong;

        static void HideOriginal(Transform cen, Casa c)
        {
            foreach (var t in cen.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!n.StartsWith("BLD_" + c.name + "_")) continue;
                if (n.Contains("_Porta") || n.Contains("_Janela") || n.Contains("_Varanda_")) t.gameObject.SetActive(false);
            }
            var col = FindDeep(cen, "COL_Box_BLD_" + c.name);
            if (col != null) col.gameObject.SetActive(false);
            // corpo: fica só a cantaria (cunhais, cornija, friso) — o reboco maciço e o embasamento passam a ser feitos aqui
            var corpo = FindDeep(cen, "BLD_" + c.name + "_Corpo");
            var mf = corpo.GetComponent<MeshFilter>();
            var mr = corpo.GetComponent<MeshRenderer>();
            int keep = -1;
            var mats = mr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) if (mats[i] != null && mats[i].name.Contains("Pedra_Clara")) keep = i;
            if (keep < 0) return;
            var src = mf.sharedMesh;
            var l2w = corpo.localToWorldMatrix;
            var w2l = l2w.inverse;
            // o interior (recuado da espessura da parede): a cantaria não pode entrar na sala
            float ins = T + 0.02f;
            float ax0 = c.x0 + ins, ax1 = c.x1 - ins, az0 = c.z0 + ins, az1 = c.z1 - ins;
            bool Inside(Vector3 w) => w.x > ax0 && w.x < ax1 && w.z > az0 && w.z < az1 && w.y > 0.02f && w.y < c.h - 0.02f;
            var vs = src.vertices;
            var wv = new Vector3[vs.Length];
            for (int i = 0; i < vs.Length; i++) wv[i] = l2w.MultiplyPoint3x4(vs[i]);
            var tris = src.GetTriangles(keep);
            var outT = new List<int>(tris.Length);
            for (int i = 0; i < tris.Length; i += 3)
            {
                var cc = (wv[tris[i]] + wv[tris[i + 1]] + wv[tris[i + 2]]) / 3f;
                if (Inside(cc)) continue;       // faces que atravessam a sala (friso/cornija maciços)
                outT.Add(tris[i]); outT.Add(tris[i + 1]); outT.Add(tris[i + 2]);
            }
            // vértices que ficaram dentro da sala (cunhais compridos) encostam à face interior da parede mais próxima
            for (int i = 0; i < wv.Length; i++)
            {
                var w = wv[i];
                if (!Inside(w)) continue;
                float dx0 = w.x - ax0, dx1 = ax1 - w.x, dz0 = w.z - az0, dz1 = az1 - w.z;
                float m = Mathf.Min(Mathf.Min(dx0, dx1), Mathf.Min(dz0, dz1));
                if (m == dx0) w.x = ax0; else if (m == dx1) w.x = ax1; else if (m == dz0) w.z = az0; else w.z = az1;
                vs[i] = w2l.MultiplyPoint3x4(w);
            }
            var mesh = new Mesh { name = "CVI_" + c.name + "_Cantaria" };
            mesh.vertices = vs;
            mesh.normals = src.normals;
            if (src.uv.Length == src.vertexCount) mesh.uv = src.uv;
            mesh.SetTriangles(outT, 0);
            mesh.RecalculateBounds();
            mf.sharedMesh = SaveMesh(mesh);
            mr.sharedMaterials = new[] { mats[keep] };
        }

        // ------------------------------------------------------------------ casca (mundo)

        static void Shell(InteriorKit k, Mats M, Casa c)
        {
            float h = c.h;
            var holes = new List<Vector4>[4];
            var cols = new List<Vector4>[4];
            var doors = new List<Vector4>[4];
            for (int f = 0; f < 4; f++) { holes[f] = new List<Vector4>(); cols[f] = new List<Vector4>(); doors[f] = new List<Vector4>(); }
            foreach (var o in c.ops)
            {
                Vector4 hv;
                if (o.kind == 0) hv = H(o.along, 1.3f, 0f, 2.3f);
                else if (o.kind == 3) hv = H(o.along, 1.1f, c.y2, c.y2 + 2.0f);
                else hv = H(o.along, 1.0f, o.pos.y, o.pos.y + 1.3f);
                holes[o.face].Add(hv);
                if (o.kind != 1) cols[o.face].Add(hv);
                if (o.kind == 0) doors[o.face].Add(new Vector4(hv.x, hv.y, -1f, 1f));
            }
            // paredes
            WallX(k, c.plaster, c.z0, c.z0 + T, c.x0, c.x1, 0f, h, holes[0], cols[0], c.name + "_F0");
            WallX(k, c.plaster, c.z1 - T, c.z1, c.x0, c.x1, 0f, h, holes[1], cols[1], c.name + "_F1");
            WallZ(k, c.plaster, c.x0, c.x0 + T, c.z0 + T, c.z1 - T, 0f, h, holes[2], cols[2], c.name + "_F2");
            WallZ(k, c.plaster, c.x1 - T, c.x1, c.z0 + T, c.z1 - T, 0f, h, holes[3], cols[3], c.name + "_F3");
            // embasamento de pedra (interrompido nas portas)
            WallX(k, M.pedraMuro, c.z0 - 0.04f, c.z0, c.x0 - 0.04f, c.x1 + 0.04f, 0f, 0.55f, doors[0], null, null);
            WallX(k, M.pedraMuro, c.z1, c.z1 + 0.04f, c.x0 - 0.04f, c.x1 + 0.04f, 0f, 0.55f, doors[1], null, null);
            WallZ(k, M.pedraMuro, c.x0 - 0.04f, c.x0, c.z0, c.z1, 0f, 0.55f, doors[2], null, null);
            WallZ(k, M.pedraMuro, c.x1, c.x1 + 0.04f, c.z0, c.z1, 0f, 0.55f, doors[3], null, null);
            // forro interior (pintado / azulejo nos bares)
            bool tiles = c.ground == Tema.Bar || c.ground == Tema.Tasca || c.ground == Tema.Restaurante || c.ground == Tema.Barbearia;
            float gTop = c.twoStoreys ? c.y2 - 0.2f : h;
            Lining(k, M, c, holes, 0.03f, gTop, c.inner, tiles);
            if (c.twoStoreys) Lining(k, M, c, holes, c.y2, h, Color.Lerp(c.inner, Color.white, 0.45f), false);
            // chão, teto e colisores
            Material floor = c.ground == Tema.Bar || c.ground == Tema.Casa || c.ground == Tema.Tasca ? M.mosaico : c.ground == Tema.Mercearia || c.ground == Tema.Barbearia || c.ground == Tema.Restaurante ? M.ladrilho : M.soalho;
            k.Box(floor, new Vector3(c.x0 + T, -0.06f, c.z0 + T), new Vector3(c.x1 - T, 0.03f, c.z1 - T));
            k.Col(c.name + "_Chao", new Vector3(c.x0, -0.4f, c.z0), new Vector3(c.x1, 0.03f, c.z1));
            k.C = new Color(0.96f, 0.95f, 0.92f);
            k.Box(M.reboco, new Vector3(c.x0 + T, h - 0.06f, c.z0 + T), new Vector3(c.x1 - T, h, c.z1 - T));
            k.C = Color.white;
            k.Col(c.name + "_Teto", new Vector3(c.x0, h - 0.06f, c.z0), new Vector3(c.x1, h + 1.3f, c.z1));
            // aberturas (molduras, portas abertas, janelas com portadas abertas, varandas)
            foreach (var o in c.ops)
            {
                k.Push(FaceFrame(c, o.face));
                float cx = AlongLocal(o.face, o.along);
                if (o.kind == 0)
                {
                    DoorFrame(k, M, cx, 0f, 1.3f, 2.3f, true);
                    k.Box(M.pedraClara, new Vector3(cx - 0.9f, 0f, -0.45f), new Vector3(cx + 0.9f, 0.13f, 0.02f));
                    k.Col(c.name + "_Degrau", new Vector3(cx - 0.9f, 0f, -0.45f), new Vector3(cx + 0.9f, 0.13f, 0.02f));
                    k.Box(M.pedraClara, new Vector3(cx - 0.65f, 0f, 0f), new Vector3(cx + 0.65f, 0.06f, T));
                    DoorLeaf(k, M, c.shutter, new Vector3(cx - 0.65f, 0.06f, T), 1.3f, 2.22f, -100f, true, c.name + "_Porta");
                }
                else if (o.kind == 3)
                {
                    DoorFrame(k, M, cx, c.y2, 1.1f, 2.0f, false);
                    k.Box(M.soalho, new Vector3(cx - 0.55f, c.y2 - 0.2f, 0f), new Vector3(cx + 0.55f, c.y2, T));
                    DoorLeaf(k, M, c.shutter, new Vector3(cx - 0.55f, c.y2 + 0.01f, T), 0.55f, 1.96f, -95f, true, c.name + "_PV_A");
                    DoorLeaf(k, M, c.shutter, new Vector3(cx + 0.55f, c.y2 + 0.01f, T), 0.55f, 1.96f, 95f, false, c.name + "_PV_B");
                    BalconyAt(k, M, c.name, cx, c.y2);
                }
                else WindowAt(k, M, c.shutter, cx, o.pos.y, o.kind == 1, c.inner);
                k.Pop();
            }
        }

        static void Lining(InteriorKit k, Mats M, Casa c, List<Vector4>[] holes, float y0, float y1, Color col, bool tiles)
        {
            const float L = 0.025f;
            float ix0 = c.x0 + T, ix1 = c.x1 - T, iz0 = c.z0 + T, iz1 = c.z1 - T;
            void Band(Material m, float ya, float yb)
            {
                WallX(k, m, iz0, iz0 + L, ix0, ix1, ya, yb, holes[0], null, null);
                WallX(k, m, iz1 - L, iz1, ix0, ix1, ya, yb, holes[1], null, null);
                WallZ(k, m, ix0, ix0 + L, iz0 + L, iz1 - L, ya, yb, holes[2], null, null);
                WallZ(k, m, ix1 - L, ix1, iz0 + L, iz1 - L, ya, yb, holes[3], null, null);
            }
            if (tiles)
            {
                k.C = Color.white;
                Band(M.azulejo, y0, y0 + 1.15f);
                k.C = new Color(0.25f, 0.4f, 0.7f);
                Band(M.pintado, y0 + 1.15f, y0 + 1.21f);     // remate azul
                k.C = col;
                Band(M.reboco, y0 + 1.21f, y1);
            }
            else
            {
                k.C = new Color(col.r * 0.55f, col.g * 0.5f, col.b * 0.45f);
                Band(M.pintado, y0, y0 + 0.12f);              // rodapé
                k.C = col;
                Band(M.reboco, y0 + 0.12f, y1);
            }
            k.C = Color.white;
        }

        static void WindowAt(InteriorKit k, Mats M, Material shutter, float cx, float y0, bool bars, Color inner)
        {
            float w = 1.0f, hh = 1.3f;
            StoneFrame(k, M, cx, y0, w, hh, true);
            OpenShuttersFlat(k, M, shutter, cx, y0, w, hh);
            k.Box(M.barro, new Vector3(cx - 0.43f, y0 - 0.32f, -0.32f), new Vector3(cx + 0.43f, y0 - 0.12f, -0.12f));
            for (int i = 0; i < 5; i++)
            {
                float x = cx - 0.34f + i * 0.17f;
                k.Ellipsoid(M.folhas, new Vector3(x, y0 - 0.05f, -0.22f), new Vector3(0.1f, 0.08f, 0.09f), 6, 4);
                k.Sphere(i % 2 == 0 ? M.florRosa : M.florVerm, new Vector3(x + 0.03f, y0 + 0.02f, -0.27f), 0.045f, 6, 3);
            }
            // peitoril e cortinas por dentro
            k.C = new Color(0.94f, 0.92f, 0.86f);
            k.Box(M.pedraClara, new Vector3(cx - 0.6f, y0 - 0.05f, T - 0.02f), new Vector3(cx + 0.6f, y0, T + 0.16f));
            k.C = Iron;
            k.Rod(M.pintado, new Vector3(cx - 0.75f, y0 + hh + 0.12f, T + 0.08f), new Vector3(cx + 0.75f, y0 + hh + 0.12f, T + 0.08f), 0.012f, 5);
            var cur = Color.Lerp(inner, new Color(0.95f, 0.95f, 0.92f), 0.6f);
            foreach (var sgn in new[] { -1f, 1f })
                for (int i = 0; i < 4; i++)
                {
                    float x0 = cx + sgn * (0.52f + i * 0.06f), x1 = x0 + sgn * 0.06f;
                    k.C = i % 2 == 0 ? cur : cur * 0.92f;
                    k.Box(M.pintado, new Vector3(Mathf.Min(x0, x1), y0 - 0.02f, T + 0.06f + (i % 2) * 0.02f), new Vector3(Mathf.Max(x0, x1), y0 + hh + 0.1f, T + 0.075f + (i % 2) * 0.02f));
                }
            if (bars)
            {
                k.C = Iron;
                for (float x = cx - w * 0.5f + 0.125f; x < cx + w * 0.5f - 0.05f; x += 0.125f)
                    k.Rod(M.pintado, new Vector3(x, y0, T * 0.5f), new Vector3(x, y0 + hh, T * 0.5f), 0.012f, 4, false);
                k.Box(M.pintado, new Vector3(cx - w * 0.5f, y0 + 0.6f, T * 0.5f - 0.012f), new Vector3(cx + w * 0.5f, y0 + 0.63f, T * 0.5f + 0.012f));
            }
            k.C = Color.white;
        }

        static void BalconyAt(InteriorKit k, Mats M, string name, float cx, float y)
        {
            float x0 = cx - 1.2f, x1 = cx + 1.2f, z0 = -1.0f, hr = 1.0f;
            k.C = new Color(0.95f, 0.93f, 0.88f);
            k.Box(M.pedraClara, new Vector3(x0, y - 0.14f, z0), new Vector3(x1, y, 0.02f));
            k.Box(M.pedraClara, new Vector3(x0 - 0.04f, y - 0.2f, z0 - 0.04f), new Vector3(x1 + 0.04f, y - 0.14f, 0.02f));
            foreach (var x in new[] { x0 + 0.25f, cx, x1 - 0.25f })
            {
                k.Push(new Vector3(x, y - 0.2f, 0f), 90f);
                k.Prism(M.pedraClara, new[] { new Vector2(0f, 0f), new Vector2(0.75f, 0f), new Vector2(0.06f, -0.5f), new Vector2(0f, -0.5f) }, -0.09f, 0.09f);
                k.Pop();
            }
            k.C = Iron;
            void Rail(Vector3 a, Vector3 b)
            {
                k.Rod(M.pintado, a + Vector3.up * hr, b + Vector3.up * hr, 0.025f, 6);
                k.Rod(M.pintado, a + Vector3.up * 0.08f, b + Vector3.up * 0.08f, 0.015f, 4);
                k.Rod(M.pintado, a + Vector3.up * (hr - 0.18f), b + Vector3.up * (hr - 0.18f), 0.012f, 4);
                int n = Mathf.RoundToInt((b - a).magnitude / 0.12f);
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector3.Lerp(a, b, i / (float)n);
                    k.Rod(M.pintado, p, p + Vector3.up * hr, 0.01f, 4, false);
                }
            }
            var A = new Vector3(x0 + 0.03f, y, 0f);
            var B = new Vector3(x0 + 0.03f, y, z0 + 0.03f);
            var C = new Vector3(x1 - 0.03f, y, z0 + 0.03f);
            var D = new Vector3(x1 - 0.03f, y, 0f);
            Rail(A, B);
            Rail(B, C);
            Rail(C, D);
            k.C = new Color(0.72f, 0.38f, 0.24f);
            k.Lathe(M.pintado, new Vector3(x0 + 0.25f, y, z0 + 0.25f), new[] { new Vector2(0.11f, 0f), new Vector2(0.16f, 0.26f), new Vector2(0.17f, 0.28f) }, 10, true, true, true);
            k.C = new Color(0.28f, 0.55f, 0.22f);
            k.Sphere(M.pintado, new Vector3(x0 + 0.25f, y + 0.4f, z0 + 0.25f), 0.17f, 7, 4);
            k.C = new Color(0.95f, 0.3f, 0.45f);
            k.Sphere(M.pintado, new Vector3(x0 + 0.3f, y + 0.52f, z0 + 0.2f), 0.05f, 6, 3);
            k.C = Color.white;
            k.Col(name + "_Varanda", new Vector3(x0, y - 0.2f, z0), new Vector3(x1, y, 0f));
            k.Col(name + "_Varanda_F", new Vector3(x0, y, z0 - 0.02f), new Vector3(x1, y + hr + 0.05f, z0 + 0.06f));
            k.Col(name + "_Varanda_A", new Vector3(x0 - 0.02f, y, z0), new Vector3(x0 + 0.06f, y + hr + 0.05f, 0f));
            k.Col(name + "_Varanda_B", new Vector3(x1 - 0.06f, y, z0), new Vector3(x1 + 0.02f, y + hr + 0.05f, 0f));
        }

        // ------------------------------------------------------------------ interior (referencial da planta)

        static void Interior(InteriorKit k, Mats M, Casa c)
        {
            if (c.twoStoreys)
            {
                UpperFloorAndStair(k, M, c);
                GroundBeams(k, M, c);
            }
            else
            {
                k.C = WoodDark;
                for (float x = -c.iw + 0.6f; x < c.iw - 0.3f; x += 1.2f)
                    k.Box(M.madeira, new Vector3(x - 0.08f, c.h - 0.26f, T), new Vector3(x + 0.08f, c.h - 0.06f, c.zb));
                k.C = Color.white;
            }
            switch (c.ground)
            {
                case Tema.Bar: Bar(k, M, c); break;
                case Tema.Mercearia: Mercearia(k, M, c); break;
                case Tema.Casa: CasaSala(k, M, c); break;
                case Tema.Tasca: Tasca(k, M, c); break;
                case Tema.Estudio: Estudio(k, M, c); break;
                case Tema.Barbearia: Barbearia(k, M, c); break;
                case Tema.Restaurante: Restaurante(k, M, c); break;
                case Tema.CasaFamilia: CasaFamilia(k, M, c); break;
                default: CasaTerrea(k, M, c); break;
            }
            if (c.twoStoreys) Apartamento(k, M, c);
        }

        static bool Free(Casa c, Vector2 p, float rx, float rz, bool upper = false)
        {
            var r = Rect.MinMaxRect(p.x - rx, p.y - rz, p.x + rx, p.y + rz);
            if (r.xMin < -c.iw + 0.02f || r.xMax > c.iw - 0.02f || r.yMin < T || r.yMax > c.zb) return false;
            foreach (var kz in upper ? c.keepUp : c.keep) if (kz.Overlaps(r)) return false;
            return true;
        }

        /// <summary>Primeira posição livre de uma lista de candidatas (ou null).</summary>
        static Vector2? Pick(Casa c, float rx, float rz, bool upper, params Vector2[] cands)
        {
            foreach (var p in cands) if (Free(c, p, rx, rz, upper)) return p;
            return null;
        }

        static void Reserve(Casa c, Vector2 p, float rx, float rz, bool upper = false)
        {
            (upper ? c.keepUp : c.keep).Add(Rect.MinMaxRect(p.x - rx, p.y - rz, p.x + rx, p.y + rz));
        }

        static void GroundBeams(InteriorKit k, Mats M, Casa c)
        {
            float yb = c.y2 - 0.2f;
            var hole = SRect(c, -4.25f, c.zb - 1.05f, -0.75f, c.zb);
            k.C = WoodDark;
            for (float x = -c.iw + 0.55f; x < c.iw - 0.3f; x += 1.1f)
            {
                float zEnd = (x > hole.xMin - 0.15f && x < hole.xMax + 0.15f) ? hole.yMin : c.zb;
                k.Box(M.madeira, new Vector3(x - 0.08f, yb - 0.22f, T), new Vector3(x + 0.08f, yb, zEnd));
            }
            k.Box(M.madeira, new Vector3(hole.xMin - 0.1f, yb - 0.24f, hole.yMin - 0.18f), new Vector3(hole.xMax + 0.1f, yb, hole.yMin));
            k.C = Color.white;
        }

        static void UpperFloorAndStair(InteriorKit k, Mats M, Casa c)
        {
            float y2 = c.y2, iw = c.iw, zb = c.zb;
            var hole = SRect(c, -4.25f, zb - 1.05f, -0.75f, zb);
            // laje com o vão da escada
            k.Box(M.soalho, new Vector3(-iw, y2 - 0.2f, T), new Vector3(iw, y2, hole.yMin));
            k.Box(M.soalho, new Vector3(-iw, y2 - 0.2f, hole.yMin), new Vector3(hole.xMin, y2, zb));
            k.Box(M.soalho, new Vector3(hole.xMax, y2 - 0.2f, hole.yMin), new Vector3(iw, y2, zb));
            k.Col(c.name + "_Piso1_A", new Vector3(-iw, y2 - 0.2f, T), new Vector3(iw, y2, hole.yMin));
            k.Col(c.name + "_Piso1_B", new Vector3(-iw, y2 - 0.2f, hole.yMin), new Vector3(hole.xMin, y2, zb));
            k.Col(c.name + "_Piso1_C", new Vector3(hole.xMax, y2 - 0.2f, hole.yMin), new Vector3(iw, y2, zb));
            // escada de madeira (sobe para o lado −s, com patamar junto à parede)
            k.Push(Vector3.zero, Quaternion.identity, new Vector3(c.s, 1f, 1f));
            StairCasa(k, M, c, c.s, -0.3f, zb - 1.05f, zb, y2, 14);
            k.Pop();
            // guardas à volta do vão no 1.º andar
            float inner = c.s > 0 ? hole.xMax : hole.xMin;       // ponta do vão do lado do meio da casa
            float outer = c.s > 0 ? hole.xMin : hole.xMax;       // ponta do lado do patamar
            Balustrade(k, M, new Vector3(outer, y2, hole.yMin - 0.06f), new Vector3(inner, y2, hole.yMin - 0.06f), 0.95f, WoodDark);
            Balustrade(k, M, new Vector3(inner, y2, hole.yMin - 0.06f), new Vector3(inner, y2, zb - 0.04f), 0.95f, WoodDark);
            k.Col(c.name + "_GuardaA", new Vector3(hole.xMin, y2, hole.yMin - 0.15f), new Vector3(hole.xMax, y2 + 1.0f, hole.yMin));
            k.Col(c.name + "_GuardaB", new Vector3(inner - 0.07f, y2, hole.yMin), new Vector3(inner + 0.07f, y2 + 1.0f, zb));
            // teto do 1.º andar com vigas
            k.C = WoodDark;
            for (float x = -iw + 0.55f; x < iw - 0.3f; x += 1.1f)
                k.Box(M.madeira, new Vector3(x - 0.08f, c.h - 0.28f, T), new Vector3(x + 0.08f, c.h - 0.06f, zb));
            k.C = Color.white;
        }

        /// <summary>Escada de madeira (referencial nativo: sobe para −x a partir de xBot; z0 = lado aberto, z1 = parede).</summary>
        static void StairCasa(InteriorKit k, Mats M, Casa c, float sx, float xBot, float z0, float z1, float top, int n)
        {
            float rise = top / n, run = 0.28f;
            for (int i = 0; i < n; i++)
            {
                float xa = xBot - (i + 1) * run, xb = xBot - i * run, y = (i + 1) * rise;
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(xa, y - 0.05f, z0 + 0.04f), new Vector3(xb + 0.03f, y, z1 - 0.02f));
                k.C = WoodDark;
                k.Box(M.madeira, new Vector3(xb - 0.025f, y - rise, z0 + 0.06f), new Vector3(xb, y - 0.05f, z1 - 0.04f));
            }
            var a = new Vector3(xBot + run, 0f, 0f);
            var b = new Vector3(xBot - n * run, top, 0f);
            var dir = (b - a).normalized;
            foreach (var z in new[] { z0 + 0.04f, z1 - 0.05f })
            {
                k.Push(new Vector3(b.x, b.y - 0.12f, z), Quaternion.FromToRotation(Vector3.right, -dir), Vector3.one);
                k.Box(M.madeira, new Vector3(0f, -0.14f, -0.035f), new Vector3((b - a).magnitude, 0.1f, 0.035f));
                k.Pop();
            }
            // corrimão do lado aberto: balaústres até à barra, prumos nas pontas
            float zr = z0 + 0.06f;
            var ha = new Vector3(xBot, rise + 0.95f, zr);
            var hb = new Vector3(xBot - n * run, top + 0.95f, zr);
            for (int i = 0; i < n; i++)
            {
                float x = xBot - i * run - run * 0.5f, y = (i + 1) * rise;
                float ry = Mathf.Lerp(ha.y, hb.y, Mathf.InverseLerp(ha.x, hb.x, x));
                k.Push(new Vector3(x, y, zr), Quaternion.identity, new Vector3(0.75f, 1f, 0.75f));
                Baluster(k, M, Vector3.zero, ry - y - 0.01f);
                k.Pop();
            }
            k.Rod(M.madeira, ha, hb, 0.045f, 8);
            k.BoxB(M.madeira, new Vector3(xBot - 0.08f, 0.03f, zr), new Vector3(0.13f, rise + 1.05f, 0.13f));
            k.Sphere(M.madeira, new Vector3(xBot - 0.08f, rise + 1.12f, zr), 0.075f, 8, 4);
            k.C = Color.white;
            var p0 = new Vector3(xBot + run, 0f, 0f);
            var p1 = new Vector3(xBot - (n - 1) * run, top, 0f);
            var d = (p1 - p0).normalized;
            var r = Quaternion.LookRotation(d, Vector3.up);
            var mid = (p0 + p1) * 0.5f - (r * Vector3.up) * 0.1f;
            mid.z = (z0 + z1) * 0.5f;
            MirColRot(k, sx, c.name + "_Rampa", mid, new Vector3(z1 - z0, 0.2f, (p1 - p0).magnitude), r);
            var c0 = new Vector3(xBot - 0.45f, rise * 2.6f, zr);
            var c1 = new Vector3(xBot - n * run, top, zr);
            var rr = Quaternion.LookRotation((c1 - c0).normalized, Vector3.up);
            MirColRot(k, sx, c.name + "_Corrimao", (c0 + c1) * 0.5f + rr * Vector3.up * 0.55f, new Vector3(0.1f, 1.1f, (c1 - c0).magnitude), rr);
            // arrumação debaixo da escada
            Crate(k, M, new Vector3(xBot - 3.2f, 0.03f, (z0 + z1) * 0.5f), 0.55f, 12f);
            Barrel(k, M, new Vector3(xBot - 2.4f, 0.03f, (z0 + z1) * 0.5f + 0.1f), 0.28f, 0.75f);
            MirCol(k, sx, c.name + "_Arrumos", new Vector3(xBot - 3.6f, 0f, z0 + 0.2f), new Vector3(xBot - 2.0f, 0.8f, z1));
        }

        // =====================================================================================================
        // temas do rés-do-chão

        static void Bar(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb;
            // balcão ao fundo do lado s, garrafeira na parede do fundo
            float xa = s * 0.9f, xb = s * 3.2f;
            BarCounter(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), 3.15f, 3.75f, c.name);
            BackBar(k, M, Mathf.Min(s * 1.3f, s * 3.6f), Mathf.Max(s * 1.3f, s * 3.6f), zb, c.rng);
            for (int i = 0; i < 4; i++) BarStool(k, M, new Vector3(s * (1.2f + i * 0.65f), 0.03f, 2.75f), c.name + i);
            Reserve(c, new Vector2(s * 2.1f, 4.0f), 1.4f, 1.6f);
            // mesas redondas
            foreach (var p in new[] { new Vector2(-s * 2.4f, 1.9f), new Vector2(-s * 3.9f, 3.3f), new Vector2(s * 3.6f, 1.4f), new Vector2(-s * 2.0f, 3.4f) })
            {
                if (!Free(c, p, 1.0f, 1.0f)) continue;
                BistroTable(k, M, new Vector3(p.x, 0.03f, p.y), c.rng, c.name);
                for (int j = 0; j < 3; j++)
                {
                    float a = j * 2.1f + p.x;
                    var q = new Vector3(p.x + Mathf.Cos(a) * 0.62f, 0.03f, p.y + Mathf.Sin(a) * 0.62f);
                    Chair(k, M, q, Mathf.Atan2(p.x - q.x, p.y - q.z) * Mathf.Rad2Deg, WoodDark, false);
                }
                Reserve(c, p, 0.95f, 0.95f);
            }
            // paredes: bandeira de Cabo Verde, TV com futebol, quadro de ardósia, alvo de dardos
            Flag(k, M, new Vector3(-s * 1.95f, 1.75f, T + 0.03f), 0f, 1.1f);
            Chalkboard(k, M, new Vector3(s * 1.95f, 1.55f, T + 0.03f), 0f, c.rng);
            WallTV(k, M, new Vector3(-s * (c.iw - 0.03f), 2.0f, 2.0f), s * 90f, c.name);
            Dartboard(k, M, new Vector3(-s * (c.iw - 0.03f), 1.75f, 3.6f), s * 90f);
            Poster(k, M, new Vector3(-s * 3.0f, 2.1f, zb - 0.03f), 180f, c.rng);
            // luz: candeeiros pendentes sobre o balcão + ventoinha de teto
            foreach (var x in new[] { 1.4f, 2.1f, 2.8f }) Pendant(k, M, new Vector3(s * x, c.y2 - 0.42f, 3.45f), 0.55f, new Color(0.8f, 0.45f, 0.15f));
            CeilingFan(k, M, new Vector3(-s * 1.5f, c.y2 - 0.42f, 2.4f));
            Speaker(k, M, new Vector3(s * (c.iw - 0.2f), 2.2f, T + 0.35f), s * 90f + 180f + s * 45f);
            k.Light(c.name + "_Luz_Balcao", new Vector3(s * 2.1f, 2.15f, 3.3f), new Color(1f, 0.72f, 0.42f), 6.5f, 1.15f, LightShadows.Soft);
            k.Light(c.name + "_Luz_Sala", new Vector3(-s * 2.4f, 2.3f, 2.2f), new Color(1f, 0.82f, 0.6f), 6.5f, 0.75f, LightShadows.None);
        }

        static void Mercearia(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb;
            // prateleiras altas ao fundo (lado s) e balcão com caixa registadora
            ShopShelf(k, M, Mathf.Min(s * 1.3f, s * 4.6f), Mathf.Max(s * 1.3f, s * 4.6f), zb, 2.3f, c.rng, c.name);
            float xa = s * 1.1f, xb = s * 3.8f;
            ShopCounter(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), 3.1f, 3.7f, c.rng, c.name);
            Reserve(c, new Vector2(s * 2.6f, 4.0f), 2.0f, 1.6f);
            // frutas e legumes junto à parede −s
            for (int i = 0; i < 2; i++)
            {
                var p = new Vector2(-s * (c.iw - 0.45f), 1.1f + i * 1.25f);
                FruitStand(k, M, new Vector3(p.x, 0.03f, p.y), -s * 90f, c.rng, c.name + i);
                Reserve(c, p, 0.5f, 0.6f);
            }
            // sacos de arroz, feijão, milho e açúcar
            var sacks = new[] { new Color(0.85f, 0.8f, 0.65f), new Color(0.78f, 0.7f, 0.5f), new Color(0.9f, 0.88f, 0.8f), new Color(0.7f, 0.6f, 0.45f) };
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2(-s * (1.4f + i * 0.55f), 3.55f + (i % 2) * 0.15f);
                if (!Free(c, p, 0.3f, 0.3f)) continue;
                Sack(k, M, new Vector3(p.x, 0.03f, p.y), sacks[i], c.rng);
            }
            ColT(k, c.name + "_Sacos", SRect3(c, -3.6f, 3.2f, -1.1f, 3.95f, 0.75f));
            DrinkCooler(k, M, new Vector3(-s * 1.95f, 0.03f, T + 0.4f), 180f, c.name);
            Reserve(c, new Vector2(-s * 1.95f, T + 0.4f), 0.45f, 0.4f);
            // bananas penduradas, cartazes de preços
            Bananas(k, M, new Vector3(-s * 3.0f, c.y2 - 0.45f, 2.0f));
            Chalkboard(k, M, new Vector3(s * 1.95f, 1.6f, T + 0.03f), 0f, c.rng);
            Poster(k, M, new Vector3(-s * (c.iw - 0.03f), 2.1f, 3.6f), s * 90f, c.rng);
            CeilingFan(k, M, new Vector3(0f, c.y2 - 0.42f, 2.2f));
            k.Light(c.name + "_Luz_Loja", new Vector3(0.5f * s, 2.4f, 2.6f), new Color(1f, 0.92f, 0.78f), 8f, 1.0f, LightShadows.Soft);
        }

        static void CasaSala(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb;
            // cozinha ao fundo do lado s
            KitchenRun(k, M, Mathf.Min(s * 1.3f, s * 4.1f), Mathf.Max(s * 1.3f, s * 4.1f), zb, c.rng, c.name);
            Fridge(k, M, new Vector3(s * 4.62f, 0.03f, zb - 0.36f), 0f, c.name);
            Reserve(c, new Vector2(s * 2.7f, zb - 0.5f), 2.5f, 0.6f);
            Table(k, M, new Vector3(s * 2.5f, 0.03f, 3.0f), 1.5f, 0.85f, true);
            k.Col(c.name + "_Mesa", new Vector3(s * 2.5f - 0.75f, 0f, 2.58f), new Vector3(s * 2.5f + 0.75f, 0.8f, 3.42f));
            foreach (var q in new[] { new Vector3(s * 2.0f, 0.03f, 2.35f), new Vector3(s * 3.0f, 0.03f, 2.35f) })
                Chair(k, M, q, 0f, WoodMid, false);
            foreach (var q in new[] { new Vector3(s * 2.0f, 0.03f, 3.65f), new Vector3(s * 3.0f, 0.03f, 3.65f) })
                Chair(k, M, q, 180f, WoodMid, false);
            Reserve(c, new Vector2(s * 2.5f, 3.0f), 1.2f, 1.0f);
            // sala do lado −s
            TVStand(k, M, new Vector3(-s * 1.95f, 0.03f, T + 0.3f), 180f, c.name);
            Sofa(k, M, new Vector3(-s * 1.95f, 0.03f, 2.75f), 180f, 2.0f, new Color(0.3f, 0.45f, 0.6f), c.name);
            CoffeeTable(k, M, new Vector3(-s * 1.95f, 0.03f, 1.7f), 0f, c.rng, c.name);
            Armchair(k, M, new Vector3(-s * 4.3f, 0.03f, 1.8f), s * 90f, new Color(0.62f, 0.32f, 0.25f), c.name);
            Rug(k, M, new Vector3(-s * 2.2f, 0.03f, 2.0f), 2.8f, 2.0f, 2);
            Bookshelf(k, M, new Vector3(-s * (c.iw - 0.2f), 0.03f, 3.6f), -s * 90f, c.rng, c.name);
            Picture(k, M, new Vector3(-s * 1.95f, 1.9f, zb - 0.03f), 180f, 1);
            Plant(k, M, new Vector3(-s * 3.2f, 0.03f, T + 0.25f), 3);
            WallClock(k, M, new Vector3(s * 2.4f, 2.2f, zb - 0.03f), 180f);
            CeilingFan(k, M, new Vector3(-s * 1.95f, c.y2 - 0.42f, 2.2f));
            k.Light(c.name + "_Luz_Sala", new Vector3(-s * 2.0f, 2.3f, 2.3f), new Color(1f, 0.85f, 0.65f), 7f, 0.9f, LightShadows.Soft);
            k.Light(c.name + "_Luz_Cozinha", new Vector3(s * 2.6f, 2.3f, 3.6f), new Color(1f, 0.92f, 0.8f), 5.5f, 0.7f, LightShadows.None);
        }

        static void Tasca(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb, iw = c.iw;
            // balcãozinho ao fundo (lado −s) com garrafas de grogue e pontche
            float xa = -s * 0.35f, xb = -s * (iw - 0.05f);
            BarCounter(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), zb - 1.35f, zb - 0.85f, c.name);
            WallShelfBottles(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), zb, 1.5f, c.rng);
            Reserve(c, new Vector2((xa + xb) * 0.5f, zb - 0.7f), iw * 0.5f, 0.75f);
            // alambique de cobre no canto do fundo (lado s)
            Alambique(k, M, new Vector3(s * (iw - 0.75f), 0.03f, zb - 0.75f), -s, c.name);
            Reserve(c, new Vector2(s * (iw - 0.75f), zb - 0.75f), 0.8f, 0.8f);
            // barris de grogue, cana-de-açúcar
            Barrel(k, M, new Vector3(s * (iw - 0.35f), 0.03f, 0.68f), 0.3f, 0.82f);
            Barrel(k, M, new Vector3(s * (iw - 1.0f), 0.03f, 0.68f), 0.3f, 0.82f);
            ColT(k, c.name + "_Barris", SRect3(c, iw - 1.32f, T, iw, 1.0f, 0.85f));
            SugarCane(k, M, new Vector3(s * 0.4f, 0.03f, zb - 0.25f), c.rng);
            // mesa com bancos
            foreach (var p in new[] { new Vector2(c.fd - Mathf.Sign(c.fd) * 1.9f, 1.15f), new Vector2(-Mathf.Sign(c.fd) * 0.35f, 2.35f), new Vector2(0f, 2.4f), new Vector2(-c.fd * 0.9f, 1.55f) })
            {
                if (!Free(c, p, 0.85f, 0.8f)) continue;
                Table(k, M, new Vector3(p.x, 0.03f, p.y), 1.2f, 0.7f, false);
                k.Col(c.name + "_Mesa", new Vector3(p.x - 0.6f, 0f, p.y - 0.35f), new Vector3(p.x + 0.6f, 0.8f, p.y + 0.35f));
                GrogueSet(k, M, new Vector3(p.x, 0.81f, p.y), c.rng);
                Bench(k, M, new Vector3(p.x, 0.03f, p.y - 0.75f), 1.1f, 0f, WoodMid);
                Bench(k, M, new Vector3(p.x, 0.03f, p.y + 0.39f), 1.1f, 0f, WoodMid);
                Reserve(c, p, 0.8f, 0.85f);
                break;
            }
            Flag(k, M, new Vector3(-c.fd * 0.95f > 0 ? 0.2f : -0.2f, 2.35f, zb - 0.03f), 180f, 0.8f);
            Poster(k, M, new Vector3(-s * (iw - 0.03f), 1.9f, 1.6f), s * 90f, c.rng);
            StringLights(k, M, new Vector3(-iw + 0.2f, c.h - 0.35f, 0.6f), new Vector3(iw - 0.2f, c.h - 0.35f, zb - 0.6f), 14);
            Pendant(k, M, new Vector3(0f, c.h - 0.26f, 2.4f), 0.7f, new Color(0.75f, 0.4f, 0.15f));
            k.Light(c.name + "_Luz", new Vector3(0f, 2.3f, 2.5f), new Color(1f, 0.7f, 0.42f), 6f, 1.1f, LightShadows.Soft);
            k.Light(c.name + "_Luz_Lume", new Vector3(s * (iw - 0.75f), 0.5f, zb - 1.3f), new Color(1f, 0.45f, 0.15f), 3f, 1.0f, LightShadows.None);
        }

        static void CasaTerrea(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb, iw = c.iw;
            // quarto ao fundo do lado −s, cozinha ao fundo do lado s, mesa ao meio
            // cama com a cabeceira na parede lateral (−s), ao fundo — deixa livre o corredor entre as duas portas
            float bz = zb - 0.82f;
            Bed(k, M, new Vector3(-s * iw, 0.03f, bz), -s * 90f);
            ColT(k, c.name + "_Cama", SRect3(c, -iw, bz - 0.82f, -(iw - 2.1f), bz + 0.82f, 0.6f));
            Nightstand(k, M, new Vector3(-s * (iw - 0.25f), 0.03f, bz - 1.1f));
            Reserve(c, new Vector2(-s * (iw - 1.05f), bz), 1.1f, 0.9f);
            Wardrobe(k, M, new Vector3(-s * (iw - 0.32f), 0.03f, 1.4f), -s * 90f);
            ColT(k, c.name + "_Armario", SRect3(c, -(iw), 0.65f, -(iw - 0.65f), 2.15f, 2.2f, true));
            Reserve(c, new Vector2(-s * (iw - 0.32f), 1.4f), 0.4f, 0.8f);
            KitchenRun(k, M, Mathf.Min(s * 0.2f, s * (iw - 0.8f)), Mathf.Max(s * 0.2f, s * (iw - 0.8f)), zb, c.rng, c.name);
            Reserve(c, new Vector2(s * (iw * 0.5f), zb - 0.4f), iw * 0.55f, 0.5f);
            var tp = Pick(c, 0.85f, 0.65f, false, new Vector2(c.fd - Mathf.Sign(c.fd) * 2.15f, 1.05f), new Vector2(s * 1.3f, 2.2f), new Vector2(0.3f, 2.0f), new Vector2(s * 0.4f, 3.0f), new Vector2(-s * 0.4f, 3.0f));
            if (tp.HasValue)
            {
                var p = tp.Value;
                Table(k, M, new Vector3(p.x, 0.03f, p.y), 1.1f, 0.75f, true);
                k.Col(c.name + "_Mesa", new Vector3(p.x - 0.55f, 0f, p.y - 0.37f), new Vector3(p.x + 0.55f, 0.8f, p.y + 0.37f));
                Chair(k, M, new Vector3(p.x - 0.3f, 0.03f, p.y - 0.65f), 0f, WoodMid, false);
                Chair(k, M, new Vector3(p.x + 0.3f, 0.03f, p.y + 0.65f), 180f, WoodMid, false);
            }
            var rp = Pick(c, 0.4f, 0.4f, false, new Vector2(-Mathf.Sign(c.fd) * 2.5f, 0.85f), new Vector2(-s * 2.5f, 0.95f));
            if (rp.HasValue) RockingChair(k, M, new Vector3(rp.Value.x, 0.03f, rp.Value.y), 160f);
            Rug(k, M, new Vector3(-s * (iw - 2.55f), 0.03f, bz), 0.9f, 1.5f, 0);
            Picture(k, M, new Vector3(-s * (iw - 0.03f), 1.75f, bz), s * 90f, 2);
            WallClock(k, M, new Vector3(s * 1.2f, 2.2f, zb - 0.03f), 180f);
            Plant(k, M, new Vector3(-s * 0.4f, 0.03f, T + 0.25f), 5);
            HangingLamp(k, M, new Vector3(0f, c.h - 0.06f, 2.2f), 0.85f);
            k.Light(c.name + "_Luz", new Vector3(0f, 2.4f, 2.3f), new Color(1f, 0.82f, 0.58f), 7f, 1.0f, LightShadows.Soft);
        }

        static void Estudio(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb, iw = c.iw;
            // estúdio: cama de solteiro, sofá-cama, kitchenette, secretária com portátil
            Bed(k, M, new Vector3(-s * (iw - 0.6f), 0.03f, zb), 0f, 1.0f);
            ColT(k, c.name + "_Cama", SRect3(c, -(iw - 0.05f), zb - 2.05f, -(iw - 1.15f), zb, 0.6f, true));
            Reserve(c, new Vector2(-s * (iw - 0.6f), zb - 1.05f), 0.6f, 1.1f);
            Nightstand(k, M, new Vector3(-s * (iw - 1.45f), 0.03f, zb - 0.25f));
            KitchenRun(k, M, Mathf.Min(s * 0.3f, s * (iw - 0.1f)), Mathf.Max(s * 0.3f, s * (iw - 0.1f)), zb, c.rng, c.name);
            Reserve(c, new Vector2(s * (iw * 0.5f), zb - 0.4f), iw * 0.5f, 0.5f);
            var dp = new Vector2(-c.fd, T + 0.3f);
            DeskModern(k, M, new Vector3(dp.x, 0.03f, dp.y), 0f, c.name);
            Reserve(c, dp + new Vector2(0f, 0.35f), 0.65f, 0.7f);
            var sp = Pick(c, 1.0f, 0.45f, false, new Vector2(-Mathf.Sign(c.fd) * 1.2f, 2.3f), new Vector2(0.3f, 2.3f), new Vector2(-s * 1.4f, 2.0f));
            if (sp.HasValue)
            {
                Sofa(k, M, new Vector3(sp.Value.x, 0.03f, sp.Value.y), 0f, 1.8f, new Color(0.75f, 0.55f, 0.25f), c.name);
                Reserve(c, sp.Value, 1.0f, 0.5f);
                CoffeeTable(k, M, new Vector3(sp.Value.x, 0.03f, sp.Value.y + 0.95f), 0f, c.rng, c.name);
                Reserve(c, sp.Value + new Vector2(0f, 0.95f), 0.5f, 0.35f);
            }
            WallTV(k, M, new Vector3(-s * 0.3f, 1.55f, zb - 0.03f), 180f, c.name);
            Rug(k, M, new Vector3(-s * 0.8f, 0.03f, 1.9f), 2.0f, 1.4f, 1);
            Poster(k, M, new Vector3(-s * (iw - 0.03f), 1.8f, 1.2f), s * 90f, c.rng);
            Plant(k, M, new Vector3(s * 0.3f, 0.03f, T + 0.25f), 9);
            Pendant(k, M, new Vector3(0f, c.h - 0.26f, 2.3f), 0.6f, new Color(0.2f, 0.2f, 0.22f));
            k.Light(c.name + "_Luz", new Vector3(0f, 2.4f, 2.4f), new Color(1f, 0.88f, 0.7f), 7f, 1.0f, LightShadows.Soft);
        }

        static void Barbearia(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float zb = c.zb, iw = c.iw;
            // duas cadeiras de barbeiro com espelho na parede do fundo, bancos de espera junto à frente
            for (int i = 0; i < 2; i++)
            {
                float x = -s * (0.8f + i * 1.6f);
                BarberStation(k, M, new Vector3(x, 0.03f, zb), c.rng, c.name + i);
                BarberChair(k, M, new Vector3(x, 0.03f, zb - 1.25f), 0f, c.name + i);
                Reserve(c, new Vector2(x, zb - 0.9f), 0.8f, 0.9f);
            }
            var bp = Pick(c, 0.9f, 0.3f, false, new Vector2(s * 1.4f, T + 0.4f), new Vector2(-s * 1.6f, T + 0.4f), new Vector2(s * 1.5f, 2.2f));
            if (bp.HasValue)
            {
                Bench(k, M, new Vector3(bp.Value.x, 0.03f, bp.Value.y - 0.18f), 1.6f, 0f, WoodMid);
                Reserve(c, bp.Value, 0.85f, 0.3f);
            }
            Sink(k, M, new Vector3(s * (iw - 0.35f), 0.03f, zb - 0.35f), 0f);
            Reserve(c, new Vector2(s * (iw - 0.35f), zb - 0.35f), 0.4f, 0.4f);
            BarberPole(k, M, new Vector3(c.fd + (c.fd > 0 ? -1.0f : 1.0f), 1.4f, -0.05f));
            Poster(k, M, new Vector3(s * (iw - 0.03f), 1.9f, 1.2f), s * -90f, c.rng);
            Poster(k, M, new Vector3(-s * (iw - 0.03f), 1.9f, 1.4f), s * 90f, c.rng);
            Plant(k, M, new Vector3(s * 0.4f, 0.03f, zb - 0.3f), 11);
            // cartazes de cortes por cima dos espelhos, TV na parede, cabide, prateleira de toalhas e produtos
            Poster(k, M, new Vector3(-s * 0.8f, 2.65f, zb - 0.03f), 180f, c.rng);
            Poster(k, M, new Vector3(-s * 2.4f, 2.65f, zb - 0.03f), 180f, c.rng);
            WallTV(k, M, new Vector3(s * 1.7f, 2.2f, zb - 0.03f), 180f, c.name);
            CoatHooks(k, M, new Vector3(-s * (iw - 0.03f), 1.7f, 0.9f), s * 90f);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-0.55f, 1.95f, T + 0.02f), new Vector3(0.55f, 1.99f, T + 0.26f));
            for (int i = 0; i < 4; i++)
            {
                k.C = i % 2 == 0 ? Color.white : new Color(0.35f, 0.55f, 0.75f);
                k.Box(M.pintado, new Vector3(-0.5f + i * 0.13f, 1.99f, T + 0.05f), new Vector3(-0.39f + i * 0.13f, 2.11f, T + 0.24f));
            }
            for (int i = 0; i < 4; i++)
            {
                k.C = BottleCols[(i * 3) % BottleCols.Length];
                k.Lathe(M.pintado, new Vector3(0.1f + i * 0.11f, 1.99f, T + 0.14f), new[] { new Vector2(0.035f, 0f), new Vector2(0.035f, 0.16f), new Vector2(0.015f, 0.2f) }, 8);
            }
            k.C = Color.white;
            var tp = Pick(c, 0.35f, 0.35f, false, new Vector2(-s * 1.6f, 1.6f), new Vector2(0f, 1.9f));
            if (tp.HasValue)
            {
                k.C = WoodDark;
                k.Cyl(M.madeira, new Vector3(tp.Value.x, 0.03f, tp.Value.y), 0.04f, 0.5f, 8);
                k.Cyl(M.madeira, new Vector3(tp.Value.x, 0.53f, tp.Value.y), 0.3f, 0.03f, 14);
                k.C = new Color(0.85f, 0.3f, 0.25f);
                k.Box(M.pintado, new Vector3(tp.Value.x - 0.12f, 0.56f, tp.Value.y - 0.1f), new Vector3(tp.Value.x + 0.1f, 0.57f, tp.Value.y + 0.12f));
                k.C = Color.white;
                k.Col(c.name + "_Mesinha", new Vector3(tp.Value.x - 0.3f, 0f, tp.Value.y - 0.3f), new Vector3(tp.Value.x + 0.3f, 0.57f, tp.Value.y + 0.3f));
            }
            CeilingFan(k, M, new Vector3(0f, c.h - 0.06f, 2.4f));
            k.Light(c.name + "_Luz", new Vector3(-s * 1.7f, 2.5f, 2.8f), new Color(1f, 0.95f, 0.88f), 7f, 1.05f, LightShadows.Soft);
        }

        static void Restaurante(InteriorKit k, Mats M, Casa c)
        {
            float zb = c.zb, iw = c.iw;
            int e = c.fd > 0f ? -1 : 1;            // lado com mais espaço (oposto à porta)
            // balcão ao fundo, perto da porta; palco de morna na outra ponta
            float xa = -e * 0.2f, xb = -e * 3.4f;
            BarCounter(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), zb - 1.25f, zb - 0.7f, c.name);
            BackBar(k, M, Mathf.Min(xa, xb), Mathf.Max(xa, xb), zb, c.rng);
            Reserve(c, new Vector2((xa + xb) * 0.5f, zb - 0.6f), 1.7f, 0.8f);
            for (int i = 0; i < 3; i++) BarStool(k, M, new Vector3(-e * (0.7f + i * 0.9f), 0.03f, zb - 1.6f), c.name + i);
            Reserve(c, new Vector2(-e * 1.6f, zb - 1.6f), 1.5f, 0.3f);
            // palco
            Stage(k, M, new Vector3(e * (iw - 1.05f), 0.03f, c.D * 0.5f), e, c.name);
            Reserve(c, new Vector2(e * (iw - 1.05f), c.D * 0.5f), 1.1f, 1.7f);
            // mesas com toalhas
            foreach (var x in new[] { e * 3.5f, e * 1.4f, -e * 4.6f, -e * 5.9f, e * 0.0f })
            {
                var p = new Vector2(x, 1.55f);
                if (!Free(c, p, 0.75f, 0.95f)) continue;
                Table(k, M, new Vector3(p.x, 0.03f, p.y), 1.0f, 0.75f, true);
                k.Col(c.name + "_Mesa", new Vector3(p.x - 0.5f, 0f, p.y - 0.37f), new Vector3(p.x + 0.5f, 0.8f, p.y + 0.37f));
                Chair(k, M, new Vector3(p.x, 0.03f, p.y - 0.65f), 0f, WoodDark, false);
                Chair(k, M, new Vector3(p.x, 0.03f, p.y + 0.65f), 180f, WoodDark, false);
                Reserve(c, p, 0.7f, 0.95f);
            }
            Flag(k, M, new Vector3(-e * 1.8f, 2.9f, zb - 0.03f), 180f, 1.0f);
            Poster(k, M, new Vector3(e * 2.4f, 2.3f, zb - 0.03f), 180f, c.rng);
            Poster(k, M, new Vector3(-e * 5.4f, 2.0f, zb - 0.03f), 180f, c.rng);
            StringLights(k, M, new Vector3(-iw + 0.3f, c.h - 0.3f, 1.0f), new Vector3(iw - 0.3f, c.h - 0.3f, 1.0f), 22);
            foreach (var x in new[] { -4.5f, -1.5f, 1.5f, 4.5f }) Pendant(k, M, new Vector3(x, c.h - 0.26f, 2.1f), 0.8f, new Color(0.85f, 0.5f, 0.2f));
            k.Light(c.name + "_Luz_Palco", new Vector3(e * (iw - 1.2f), 2.6f, c.D * 0.5f), new Color(1f, 0.6f, 0.35f), 5.5f, 1.2f, LightShadows.Soft);
            k.Light(c.name + "_Luz_Sala", new Vector3(-e * 1.5f, 2.6f, 2.0f), new Color(1f, 0.82f, 0.6f), 7.5f, 0.9f, LightShadows.None);
        }

        static void CasaFamilia(InteriorKit k, Mats M, Casa c)
        {
            float zb = c.zb, iw = c.iw;
            int e = c.fd > 0f ? -1 : 1;
            // divisória com porta: quarto na ponta e, sala/cozinha no resto
            float px = e * 3.0f;
            var gap = new List<Vector4> { H(c.D * 0.5f + 0.4f, 0.9f, 0f, 2.1f) };
            k.C = Color.white;
            WallZ(k, M.reboco, px - 0.06f, px + 0.06f, T, zb, 0.03f, c.h - 0.06f, gap, gap, c.name + "_Divisoria");
            k.C = WoodMid;
            float gz = c.D * 0.5f + 0.4f;
            k.Box(M.madeira, new Vector3(px - 0.08f, 0.03f, gz - 0.5f), new Vector3(px + 0.08f, 2.18f, gz - 0.45f));
            k.Box(M.madeira, new Vector3(px - 0.08f, 0.03f, gz + 0.45f), new Vector3(px + 0.08f, 2.18f, gz + 0.5f));
            k.Box(M.madeira, new Vector3(px - 0.08f, 2.1f, gz - 0.5f), new Vector3(px + 0.08f, 2.18f, gz + 0.5f));
            k.C = Color.white;
            c.keep.Add(Rect.MinMaxRect(px - 1.0f, gz - 0.6f, px + 1.0f, gz + 0.6f));
            // quarto (ponta e)
            Bed(k, M, new Vector3(e * (iw - 1.85f), 0.03f, zb), 0f);
            k.Col(c.name + "_Cama", new Vector3(e * (iw - 1.85f) - 0.85f, 0f, zb - 2.1f), new Vector3(e * (iw - 1.85f) + 0.85f, 0.6f, zb));
            Nightstand(k, M, new Vector3(e * (iw - 0.6f), 0.03f, zb - 0.25f));
            Nightstand(k, M, new Vector3(e * (iw - 3.1f), 0.03f, zb - 0.25f));
            Chest(k, M, new Vector3(e * (iw - 1.85f), 0.03f, 1.05f), 180f, 0.9f, new Color(0.5f, 0.32f, 0.2f));
            Cradle(k, M, new Vector3(e * (iw - 0.6f), 0.03f, 1.0f), 90f);
            Picture(k, M, new Vector3(e * (iw - 1.85f), 1.95f, zb - 0.03f), 180f, 1);
            // cozinha junto à divisória, sala no meio
            float ka = px - e * 0.2f, kb = px - e * 3.2f;
            KitchenRun(k, M, Mathf.Min(ka, kb), Mathf.Max(ka, kb), zb, c.rng, c.name);
            Fridge(k, M, new Vector3(px - e * 3.6f, 0.03f, zb - 0.36f), 0f, c.name);
            Reserve(c, new Vector2((ka + kb) * 0.5f - e * 0.2f, zb - 0.45f), 2.0f, 0.55f);
            var tp = Pick(c, 0.9f, 0.9f, false, new Vector2(px - e * 1.7f, 1.6f), new Vector2(px - e * 2.3f, 1.7f));
            if (tp.HasValue)
            {
                var p = tp.Value;
                Table(k, M, new Vector3(p.x, 0.03f, p.y), 1.3f, 0.8f, true);
                k.Col(c.name + "_Mesa", new Vector3(p.x - 0.65f, 0f, p.y - 0.4f), new Vector3(p.x + 0.65f, 0.8f, p.y + 0.4f));
                Chair(k, M, new Vector3(p.x - 0.35f, 0.03f, p.y - 0.7f), 0f, WoodMid, false);
                Chair(k, M, new Vector3(p.x + 0.35f, 0.03f, p.y + 0.7f), 180f, WoodMid, false);
                Reserve(c, p, 0.85f, 0.9f);
            }
            float sx = -e * (iw - 2.0f);
            Sofa(k, M, new Vector3(sx, 0.03f, zb - 0.55f), 180f, 2.1f, new Color(0.55f, 0.3f, 0.35f), c.name);
            CoffeeTable(k, M, new Vector3(sx, 0.03f, zb - 1.55f), 0f, c.rng, c.name);
            Reserve(c, new Vector2(sx, zb - 1.0f), 1.1f, 1.0f);
            Bookshelf(k, M, new Vector3(-e * (iw - 0.2f), 0.03f, 1.2f), -e * 90f, c.rng, c.name);
            Rug(k, M, new Vector3(sx, 0.03f, zb - 1.4f), 2.4f, 1.6f, 0);
            WallClock(k, M, new Vector3(px - e * 0.07f - e * 0.0f, 2.2f, 1.0f), e > 0 ? -90f : 90f);
            Plant(k, M, new Vector3(-e * (iw - 0.3f), 0.03f, zb - 0.3f), 7);
            HangingLamp(k, M, new Vector3(sx, c.h - 0.06f, zb - 1.5f), 0.9f);
            k.Light(c.name + "_Luz_Sala", new Vector3(sx * 0.6f, 2.6f, 2.0f), new Color(1f, 0.84f, 0.62f), 7.5f, 1.0f, LightShadows.Soft);
            k.Light(c.name + "_Luz_Quarto", new Vector3(e * (iw - 1.6f), 2.6f, 2.0f), new Color(1f, 0.8f, 0.6f), 4.5f, 0.7f, LightShadows.None);
        }

        static void Apartamento(InteriorKit k, Mats M, Casa c)
        {
            int s = c.s;
            float y = c.y2, zb = c.zb, iw = c.iw;
            bool twin = c.ground == Tema.Mercearia || c.ground == Tema.Casa;
            // quarto do lado −s (cabeceira na parede lateral)
            if (twin)
            {
                foreach (var z in new[] { 1.0f, 2.45f })
                {
                    Bed(k, M, new Vector3(-s * iw, y, z), -s * 90f, 0.95f);
                    k.Col(c.name + "_Cama", new Vector3(Mathf.Min(-s * iw, -s * (iw - 2.05f)), y, z - 0.5f), new Vector3(Mathf.Max(-s * iw, -s * (iw - 2.05f)), y + 0.6f, z + 0.5f));
                }
            }
            else
            {
                Bed(k, M, new Vector3(-s * iw, y, 1.7f), -s * 90f);
                k.Col(c.name + "_Cama", new Vector3(Mathf.Min(-s * iw, -s * (iw - 2.05f)), y, 0.9f), new Vector3(Mathf.Max(-s * iw, -s * (iw - 2.05f)), y + 0.6f, 2.5f));
                Nightstand(k, M, new Vector3(-s * (iw - 0.25f), y, 0.6f));
                Nightstand(k, M, new Vector3(-s * (iw - 0.25f), y, 2.8f));
            }
            Reserve(c, new Vector2(-s * (iw - 1.05f), 1.7f), 1.1f, 1.2f, true);
            Rug(k, M, new Vector3(-s * (iw - 2.7f), y, 1.7f), 1.0f, 1.8f, 1);
            Picture(k, M, new Vector3(-s * (iw - 0.03f), y + 1.75f, 1.7f), s * 90f, twin ? 2 : 0);
            bool deepBalcony = c.sd > 3f;
            if (deepBalcony)
            {
                // varanda lateral ao fundo: roupeiro na frente, sala virada para a TV na parede do fundo
                Wardrobe(k, M, new Vector3(s * 1.85f, y, T + 0.32f), 180f);
                k.Col(c.name + "_Roupeiro", new Vector3(s * 1.85f - 0.75f, y, T), new Vector3(s * 1.85f + 0.75f, y + 2.25f, T + 0.65f));
                Reserve(c, new Vector2(s * 1.85f, T + 0.35f), 0.8f, 0.4f, true);
                TVStand(k, M, new Vector3(s * 1.9f, y, zb - 0.24f), 0f, c.name);
                Sofa(k, M, new Vector3(s * 1.9f, y, 3.55f), 0f, 2.0f, new Color(0.35f, 0.5f, 0.42f), c.name);
                CoffeeTable(k, M, new Vector3(s * 1.9f, y, 4.45f), 0f, c.rng, c.name);
                Rug(k, M, new Vector3(s * 1.9f, y, 4.2f), 2.4f, 1.6f, 2);
                Plant(k, M, new Vector3(s * 2.9f, y, T + 0.25f), 17);
            }
            else
            {
                // roupeiro e secretária na parede do fundo (lado s)
                Wardrobe(k, M, new Vector3(s * (iw - 0.85f), y, zb - 0.32f), 0f);
                k.Col(c.name + "_Roupeiro", new Vector3(s * (iw - 0.85f) - 0.75f, y, zb - 0.65f), new Vector3(s * (iw - 0.85f) + 0.75f, y + 2.25f, zb));
                Reserve(c, new Vector2(s * (iw - 0.85f), zb - 0.35f), 0.8f, 0.4f, true);
                Desk(k, M, new Vector3(s * 1.75f, y, zb - 0.35f));
                k.Col(c.name + "_Secretaria", new Vector3(s * 1.75f - 0.65f, y, zb - 0.7f), new Vector3(s * 1.75f + 0.65f, y + 0.8f, zb));
                Chair(k, M, new Vector3(s * 1.75f, y, zb - 1.0f), 0f, WoodMid, false);
                Reserve(c, new Vector2(s * 1.75f, zb - 0.6f), 0.7f, 0.65f, true);
            }
            // sala: sofá virado para a TV encostada à frente
            var tv = deepBalcony ? (Vector2?)null : Pick(c, 0.75f, 0.3f, true, new Vector2(-s * 1.9f, T + 0.3f), new Vector2(s * 1.95f, T + 0.3f));
            if (tv.HasValue)
            {
                TVStand(k, M, new Vector3(tv.Value.x, y, tv.Value.y), 180f, c.name);
                Reserve(c, tv.Value, 0.75f, 0.3f, true);
                var sofa = new Vector2(tv.Value.x, 2.75f);
                if (Free(c, sofa, 1.05f, 0.45f, true))
                {
                    Sofa(k, M, new Vector3(sofa.x, y, sofa.y), 180f, 2.0f, new Color(0.35f, 0.5f, 0.42f), c.name);
                    CoffeeTable(k, M, new Vector3(sofa.x, y, 1.7f), 0f, c.rng, c.name);
                    Rug(k, M, new Vector3(sofa.x, y, 2.0f), 2.6f, 1.8f, 2);
                }
            }
            Plant(k, M, new Vector3(-0.85f, y, T + 0.25f), 13);
            HangingLamp(k, M, new Vector3(0f, c.h - 0.06f, 2.3f), 0.8f);
            k.Light(c.name + "_Luz_Apto", new Vector3(0f, y + 2.2f, 2.5f), new Color(1f, 0.86f, 0.66f), 8f, 0.95f, LightShadows.None);
        }

        /// <summary>Caixa de colisor dada em coordenadas nativas (x multiplicado por s).</summary>
        static void ColS(InteriorKit k, Casa c, string n, float xa, float za, float xb, float zb, float hgt, float y0 = 0f)
        {
            float a = xa * c.s, b = xb * c.s;
            k.Col(n, new Vector3(Mathf.Min(a, b), y0, za), new Vector3(Mathf.Max(a, b), y0 + hgt, zb));
        }

        // (xa, za, xb, zb, h, já com sinal?) → caixa para k.Col
        static Bounds SRect3Bounds(Casa c, float xa, float za, float xb, float zb, float hgt, bool signed)
        {
            float a = signed ? xa * c.s : xa, b = signed ? xb * c.s : xb;
            var mn = new Vector3(Mathf.Min(a, b), 0f, za);
            var mx = new Vector3(Mathf.Max(a, b), hgt, zb);
            var bb = new Bounds();
            bb.SetMinMax(mn, mx);
            return bb;
        }

        static void ColT(InteriorKit k, string n, (Vector3 a, Vector3 b) r) => k.Col(n, r.a, r.b);

        static (Vector3, Vector3) SRect3(Casa c, float xa, float za, float xb, float zb, float hgt, bool signed = true)
        {
            var b = SRect3Bounds(c, xa, za, xb, zb, hgt, signed);
            return (b.min, b.max);
        }

        // =====================================================================================================
        // mobília nova

        static void BarCounter(InteriorKit k, Mats M, float xa, float xb, float z0, float z1, string name)
        {
            // balcão: frente para −z (z0), lado do empregado em z1
            float len = xb - xa;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(xa, 0.03f, z0 + 0.04f), new Vector3(xb, 1.02f, z1));
            k.C = WoodMid;
            for (float x = xa + 0.08f; x < xb - 0.05f; x += 0.16f)
                k.Box(M.madeira, new Vector3(x, 0.15f, z0 + 0.02f), new Vector3(x + 0.12f, 0.95f, z0 + 0.04f));
            k.C = new Color(0.35f, 0.22f, 0.14f);
            k.Box(M.madeira, new Vector3(xa - 0.06f, 1.02f, z0 - 0.08f), new Vector3(xb + 0.06f, 1.08f, z1 + 0.02f));
            k.Box(M.madeira, new Vector3(xa, 0.03f, z0 + 0.0f), new Vector3(xb, 0.15f, z0 + 0.06f));
            // varão de latão para os pés
            k.C = Brass;
            k.Rod(M.pintado, new Vector3(xa + 0.05f, 0.22f, z0 - 0.14f), new Vector3(xb - 0.05f, 0.22f, z0 - 0.14f), 0.022f, 8);
            for (float x = xa + 0.25f; x < xb - 0.1f; x += Mathf.Max(0.6f, len / 4f))
                k.Rod(M.pintado, new Vector3(x, 0.22f, z0 - 0.14f), new Vector3(x, 0.22f, z0 + 0.04f), 0.012f, 4);
            // torneiras de cerveja, copos, porta-guardanapos, cinzeiro
            float tx = xa + len * 0.35f;
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(tx - 0.25f, 1.08f, z1 - 0.32f), new Vector3(tx + 0.25f, 1.12f, z1 - 0.18f));
            for (int i = 0; i < 3; i++)
            {
                float x = tx - 0.16f + i * 0.16f;
                k.C = Chrome;
                k.Cyl(M.pintado, new Vector3(x, 1.08f, z1 - 0.25f), 0.025f, 0.3f, 8);
                k.Rod(M.pintado, new Vector3(x, 1.33f, z1 - 0.25f), new Vector3(x, 1.33f, z1 - 0.36f), 0.012f, 5);
                k.C = i == 0 ? new Color(0.9f, 0.7f, 0.1f) : i == 1 ? new Color(0.2f, 0.2f, 0.2f) : CvRed;
                k.Cyl(M.pintado, new Vector3(x, 1.38f, z1 - 0.25f), 0.02f, 0.14f, 6);
            }
            for (int i = 0; i < 4; i++)
            {
                float x = xa + len * (0.6f + i * 0.08f);
                k.C = new Color(0.85f, 0.92f, 0.95f);
                k.Lathe(M.pintado, new Vector3(x, 1.08f, z0 + 0.12f + (i % 2) * 0.08f), new[] { new Vector2(0.025f, 0f), new Vector2(0.033f, 0.13f), new Vector2(0.035f, 0.14f) }, 8, true, true, false);
                if (i % 2 == 0)
                {
                    k.C = new Color(0.95f, 0.75f, 0.2f);
                    k.Cyl(M.pintado, new Vector3(x, 1.085f, z0 + 0.12f), 0.027f, 0.1f, 8);
                }
            }
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(xa + 0.15f, 1.08f, z0 + 0.1f), new Vector3(xa + 0.27f, 1.2f, z0 + 0.16f));
            k.C = new Color(0.97f, 0.97f, 0.97f);
            k.Box(M.pintado, new Vector3(xa + 0.16f, 1.09f, z0 + 0.11f), new Vector3(xa + 0.26f, 1.18f, z0 + 0.15f));
            k.C = new Color(0.3f, 0.3f, 0.32f);
            k.Cyl(M.pintado, new Vector3(xb - 0.3f, 1.08f, z0 + 0.18f), 0.06f, 0.025f, 10);
            k.C = Color.white;
            k.Col(name + "_Balcao", new Vector3(xa - 0.06f, 0f, z0 - 0.08f), new Vector3(xb + 0.06f, 1.1f, z1));
            Blob(k, M, (xa + xb) * 0.5f, 0.03f, (z0 + z1) * 0.5f, len + 0.4f, z1 - z0 + 0.5f);
        }

        static void Bottle(InteriorKit k, Mats M, Vector3 b, int kind, Color col)
        {
            k.C = col;
            switch (kind % 4)
            {
                case 0: // vinho
                    k.Lathe(M.pintado, b, new[] { new Vector2(0.036f, 0f), new Vector2(0.037f, 0.2f), new Vector2(0.03f, 0.24f), new Vector2(0.013f, 0.27f), new Vector2(0.013f, 0.32f) }, 8);
                    break;
                case 1: // cerveja
                    k.Lathe(M.pintado, b, new[] { new Vector2(0.03f, 0f), new Vector2(0.03f, 0.13f), new Vector2(0.012f, 0.19f), new Vector2(0.012f, 0.23f) }, 8);
                    break;
                case 2: // garrafa quadrada (licor)
                    k.BoxB(M.pintado, b, new Vector3(0.07f, 0.18f, 0.07f));
                    k.Cyl(M.pintado, b + Vector3.up * 0.18f, 0.015f, 0.06f, 6);
                    break;
                default: // grogue (garrafa larga e baixa)
                    k.Lathe(M.pintado, b, new[] { new Vector2(0.045f, 0f), new Vector2(0.046f, 0.14f), new Vector2(0.025f, 0.18f), new Vector2(0.014f, 0.24f) }, 8);
                    break;
            }
            k.C = new Color(0.55f, 0.35f, 0.2f);
            float top = kind % 4 == 0 ? 0.32f : kind % 4 == 1 ? 0.23f : 0.24f;
            k.Cyl(M.pintado, b + Vector3.up * top, 0.014f, 0.025f, 6);
            k.C = new Color(0.95f, 0.92f, 0.8f);
            if (kind % 4 != 2) k.Cyl(M.pintado, b + Vector3.up * 0.06f, kind % 4 == 3 ? 0.047f : 0.037f, 0.06f, 8, true, false);
        }

        static readonly Color[] BottleCols =
        {
            new Color(0.2f, 0.45f, 0.2f), new Color(0.45f, 0.25f, 0.1f), new Color(0.85f, 0.9f, 0.9f), new Color(0.6f, 0.1f, 0.15f),
            new Color(0.15f, 0.3f, 0.5f), new Color(0.9f, 0.6f, 0.2f), new Color(0.3f, 0.15f, 0.08f),
        };

        static void BackBar(InteriorKit k, Mats M, float xa, float xb, float zWall, System.Random rng)
        {
            float z = zWall;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(xa, 0.03f, z - 0.5f), new Vector3(xb, 0.9f, z));
            k.C = new Color(0.3f, 0.2f, 0.13f);
            k.Box(M.madeira, new Vector3(xa - 0.02f, 0.9f, z - 0.52f), new Vector3(xb + 0.02f, 0.95f, z));
            // frigoríficos de bebidas (portas de vidro iluminadas)
            float half = (xb - xa) * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                float a = xa + 0.08f + i * half, b = a + half - 0.16f;
                k.C = new Color(0.12f, 0.12f, 0.13f);
                k.Box(M.pintado, new Vector3(a, 0.12f, z - 0.515f), new Vector3(b, 0.82f, z - 0.5f));
                k.C = Color.white;
                k.Box(M.vidroLuz, new Vector3(a + 0.05f, 0.17f, z - 0.52f), new Vector3(b - 0.05f, 0.77f, z - 0.515f));
            }
            // espelho e prateleiras de garrafas
            k.C = new Color(0.62f, 0.7f, 0.74f);
            k.Box(M.pintado, new Vector3(xa + 0.1f, 1.05f, z - 0.03f), new Vector3(xb - 0.1f, 2.3f, z - 0.01f));
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(xa, 1.0f, z - 0.05f), new Vector3(xa + 0.08f, 2.4f, z));
            k.Box(M.madeira, new Vector3(xb - 0.08f, 1.0f, z - 0.05f), new Vector3(xb, 2.4f, z));
            k.Box(M.madeira, new Vector3(xa, 2.35f, z - 0.12f), new Vector3(xb, 2.45f, z));
            for (int sIdx = 0; sIdx < 3; sIdx++)
            {
                float y = 1.15f + sIdx * 0.4f;
                k.C = new Color(0.75f, 0.85f, 0.85f);
                k.Box(M.pintado, new Vector3(xa + 0.08f, y - 0.02f, z - 0.24f), new Vector3(xb - 0.08f, y, z - 0.02f));
                k.C = Color.white;
                k.Box(M.vidroLuz, new Vector3(xa + 0.1f, y - 0.025f, z - 0.05f), new Vector3(xb - 0.1f, y - 0.02f, z - 0.03f));
                for (float x = xa + 0.16f; x < xb - 0.12f; x += 0.085f + (float)rng.NextDouble() * 0.05f)
                    Bottle(k, M, new Vector3(x, y, z - 0.13f + (float)rng.NextDouble() * 0.04f), rng.Next(4), BottleCols[rng.Next(BottleCols.Length)]);
            }
            k.C = Color.white;
        }

        static void WallShelfBottles(InteriorKit k, Mats M, float xa, float xb, float zWall, float y0, System.Random rng)
        {
            for (int sIdx = 0; sIdx < 2; sIdx++)
            {
                float y = y0 + sIdx * 0.42f;
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(xa + 0.1f, y - 0.04f, zWall - 0.24f), new Vector3(xb - 0.1f, y, zWall - 0.02f));
                for (float x = xa + 0.2f; x < xb - 0.15f; x += 0.1f + (float)rng.NextDouble() * 0.05f)
                    Bottle(k, M, new Vector3(x, y, zWall - 0.13f), rng.Next(3) == 0 ? 0 : 3, rng.Next(2) == 0 ? new Color(0.88f, 0.92f, 0.9f) : new Color(0.75f, 0.5f, 0.25f));
            }
            k.C = Color.white;
        }

        static void BarStool(InteriorKit k, Mats M, Vector3 b, string name)
        {
            k.C = new Color(0.2f, 0.2f, 0.21f);
            k.Cyl(M.pintado, b, 0.2f, 0.03f, 12);
            k.Cyl(M.pintado, b + Vector3.up * 0.03f, 0.035f, 0.68f, 8);
            k.C = Chrome;
            k.Torus(M.pintado, b + Vector3.up * 0.3f, 0.17f, 0.012f, 14, 4);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                k.Rod(M.pintado, b + Vector3.up * 0.3f, b + new Vector3(Mathf.Cos(a) * 0.17f, 0.3f, Mathf.Sin(a) * 0.17f), 0.008f, 4);
            }
            k.C = new Color(0.55f, 0.12f, 0.1f);
            k.Lathe(M.pintado, b + Vector3.up * 0.7f, new[] { new Vector2(0.17f, 0f), new Vector2(0.19f, 0.04f), new Vector2(0.18f, 0.08f), new Vector2(0f, 0.09f) }, 14, true, true, false);
            k.C = Color.white;
            k.Col(name + "_Banco", b + new Vector3(-0.2f, 0f, -0.2f), b + new Vector3(0.2f, 0.78f, 0.2f));
            Blob(k, M, b.x, b.y, b.z, 0.5f, 0.5f);
        }

        static void BistroTable(InteriorKit k, Mats M, Vector3 b, System.Random rng, string name)
        {
            k.C = new Color(0.18f, 0.18f, 0.19f);
            k.Cyl(M.pintado, b, 0.26f, 0.03f, 12);
            k.Cyl(M.pintado, b + Vector3.up * 0.03f, 0.04f, 0.7f, 8);
            k.C = WoodMid;
            k.Cyl(M.madeira, b + Vector3.up * 0.73f, 0.4f, 0.04f, 18);
            k.C = new Color(0.3f, 0.2f, 0.12f);
            k.Torus(M.pintado, b + Vector3.up * 0.75f, 0.4f, 0.02f, 18, 4);
            // em cima: garrafa e copos, cinzeiro
            int n = 1 + rng.Next(2);
            for (int i = 0; i < n; i++)
                Bottle(k, M, b + new Vector3(-0.1f + i * 0.14f, 0.77f, 0.05f - i * 0.1f), 1, BottleCols[rng.Next(3)]);
            k.C = new Color(0.85f, 0.92f, 0.95f);
            k.Lathe(M.pintado, b + new Vector3(0.15f, 0.77f, -0.1f), new[] { new Vector2(0.025f, 0f), new Vector2(0.033f, 0.12f) }, 8, true, true, false);
            k.C = Color.white;
            k.Col(name + "_Mesa", b + new Vector3(-0.4f, 0f, -0.4f), b + new Vector3(0.4f, 0.78f, 0.4f));
            Blob(k, M, b.x, b.y, b.z, 1.0f, 1.0f);
        }

        static void Sofa(InteriorKit k, Mats M, Vector3 b, float yaw, float len, Color col, string name)
        {
            // assento para +z local (as costas em −z)
            k.Push(b, yaw);
            float hl = len * 0.5f;
            k.C = new Color(0.2f, 0.14f, 0.1f);
            foreach (var x in new[] { -hl + 0.08f, hl - 0.08f })
                foreach (var z in new[] { -0.35f, 0.3f })
                    k.Cone(M.pintado, new Vector3(x, 0f, z), 0.025f, 0.035f, 0.1f, 6);
            k.C = col * 0.85f;
            k.Box(M.pintado, new Vector3(-hl, 0.1f, -0.42f), new Vector3(hl, 0.4f, 0.42f));
            k.Box(M.pintado, new Vector3(-hl, 0.4f, -0.42f), new Vector3(hl, 0.85f, -0.22f));
            foreach (var s in new[] { -1f, 1f })
                k.Box(M.pintado, new Vector3(s > 0 ? hl - 0.18f : -hl, 0.4f, -0.42f), new Vector3(s > 0 ? hl : -hl + 0.18f, 0.66f, 0.42f));
            k.C = col;
            int nCush = len > 1.5f ? 2 : 1;
            float cw = (len - 0.36f) / nCush;
            for (int i = 0; i < nCush; i++)
            {
                float x0 = -hl + 0.18f + i * cw;
                k.Box(M.pintado, new Vector3(x0 + 0.01f, 0.4f, -0.22f), new Vector3(x0 + cw - 0.01f, 0.53f, 0.4f));
                k.Push(new Vector3(x0 + cw * 0.5f, 0.53f, -0.2f), 0f, -12f, 0f);
                k.Box(M.pintado, new Vector3(-cw * 0.5f + 0.02f, 0f, 0f), new Vector3(cw * 0.5f - 0.02f, 0.38f, 0.14f));
                k.Pop();
            }
            // almofadas
            k.C = Color.Lerp(col, new Color(0.95f, 0.85f, 0.5f), 0.6f);
            k.Push(new Vector3(-hl + 0.38f, 0.66f, -0.1f), 20f, -20f, 8f);
            k.Box(M.pintado, new Vector3(-0.18f, -0.15f, -0.06f), new Vector3(0.18f, 0.15f, 0.06f));
            k.Pop();
            k.C = Color.Lerp(col, CvRed, 0.5f);
            k.Push(new Vector3(hl - 0.38f, 0.66f, -0.1f), -18f, -20f, -6f);
            k.Box(M.pintado, new Vector3(-0.17f, -0.14f, -0.06f), new Vector3(0.17f, 0.14f, 0.06f));
            k.Pop();
            k.C = Color.white;
            k.Col(name + "_Sofa", new Vector3(-hl, 0f, -0.42f), new Vector3(hl, 0.55f, 0.42f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, len + 0.3f, 1.1f, yaw);
        }

        static void Armchair(InteriorKit k, Mats M, Vector3 b, float yaw, Color col, string name)
        {
            k.Push(b, yaw);
            k.C = WoodDark;
            foreach (var x in new[] { -0.32f, 0.32f })
                foreach (var z in new[] { -0.32f, 0.32f })
                    k.Cone(M.madeira, new Vector3(x, 0f, z), 0.025f, 0.035f, 0.12f, 6);
            k.C = col * 0.85f;
            k.Box(M.pintado, new Vector3(-0.4f, 0.12f, -0.4f), new Vector3(0.4f, 0.4f, 0.4f));
            k.Box(M.pintado, new Vector3(-0.4f, 0.4f, -0.4f), new Vector3(0.4f, 0.95f, -0.22f));
            k.Box(M.pintado, new Vector3(-0.4f, 0.4f, -0.4f), new Vector3(-0.27f, 0.65f, 0.4f));
            k.Box(M.pintado, new Vector3(0.27f, 0.4f, -0.4f), new Vector3(0.4f, 0.65f, 0.4f));
            k.C = col;
            k.Box(M.pintado, new Vector3(-0.27f, 0.4f, -0.22f), new Vector3(0.27f, 0.53f, 0.38f));
            k.C = Color.white;
            k.Col(name + "_Poltrona", new Vector3(-0.4f, 0f, -0.4f), new Vector3(0.4f, 0.6f, 0.4f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.0f, 1.0f, yaw);
        }

        static void CoffeeTable(InteriorKit k, Mats M, Vector3 b, float yaw, System.Random rng, string name)
        {
            k.Push(b, yaw);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-0.5f, 0.38f, -0.28f), new Vector3(0.5f, 0.42f, 0.28f));
            k.Box(M.madeira, new Vector3(-0.45f, 0.1f, -0.24f), new Vector3(0.45f, 0.13f, 0.24f));
            foreach (var x in new[] { -0.45f, 0.45f })
                foreach (var z in new[] { -0.24f, 0.24f })
                    k.BoxB(M.madeira, new Vector3(x, 0f, z), new Vector3(0.05f, 0.38f, 0.05f));
            // revista, caneca, comando, fruteira
            k.C = new Color(0.85f, 0.3f, 0.25f);
            k.Push(new Vector3(-0.2f, 0.42f, 0.02f), 18f);
            k.Box(M.pintado, new Vector3(-0.12f, 0f, -0.16f), new Vector3(0.12f, 0.01f, 0.16f));
            k.Pop();
            k.C = new Color(0.95f, 0.95f, 0.93f);
            k.Cyl(M.pintado, new Vector3(0.18f, 0.42f, -0.08f), 0.04f, 0.09f, 8);
            k.C = new Color(0.12f, 0.12f, 0.12f);
            k.Box(M.pintado, new Vector3(0.12f, 0.42f, 0.08f), new Vector3(0.3f, 0.44f, 0.13f));
            k.C = new Color(0.85f, 0.55f, 0.2f);
            k.Sphere(M.pintado, new Vector3(0.0f, 0.46f, 0.15f), 0.04f, 6, 4);
            k.C = Color.white;
            k.Col(name + "_MesaCentro", new Vector3(-0.5f, 0f, -0.28f), new Vector3(0.5f, 0.44f, 0.28f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.2f, 0.75f, yaw);
        }

        static void TVStand(InteriorKit k, Mats M, Vector3 b, float yaw, string name)
        {
            // móvel da TV com a frente para −z local
            k.Push(b, yaw);
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-0.7f, 0f, -0.22f), new Vector3(0.7f, 0.5f, 0.22f));
            k.C = WoodMid;
            foreach (var x in new[] { -0.68f, 0.02f })
                k.Box(M.madeira, new Vector3(x, 0.05f, -0.235f), new Vector3(x + 0.66f, 0.45f, -0.22f));
            k.C = Brass;
            foreach (var x in new[] { -0.05f, 0.09f })
                k.Box(M.pintado, new Vector3(x - 0.02f, 0.24f, -0.25f), new Vector3(x + 0.02f, 0.26f, -0.235f));
            TV(k, M, new Vector3(0f, 0.5f, 0.02f));
            k.C = new Color(0.15f, 0.15f, 0.15f);
            k.BoxB(M.pintado, new Vector3(-0.58f, 0.5f, 0.0f), new Vector3(0.12f, 0.3f, 0.14f));
            k.C = Color.white;
            k.Col(name + "_MovelTV", new Vector3(-0.7f, 0f, -0.22f), new Vector3(0.7f, 0.55f, 0.22f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.6f, 0.7f, yaw);
        }

        static void TV(InteriorKit k, Mats M, Vector3 b)
        {
            // televisão com pé, ecrã para −z local (a dar futebol: relvado verde)
            k.C = new Color(0.08f, 0.08f, 0.09f);
            k.Box(M.pintado, new Vector3(-0.15f, 0f, -0.08f), new Vector3(0.15f, 0.02f, 0.08f));
            k.Box(M.pintado, new Vector3(-0.03f, 0.02f, -0.02f), new Vector3(0.03f, 0.12f, 0.02f));
            k.Box(M.pintado, new Vector3(-0.5f, 0.1f, -0.04f), new Vector3(0.5f, 0.68f, 0.03f));
            ScreenFootball(k, M, new Vector3(0f, 0.39f, -0.045f), 0.94f, 0.52f);
        }

        static void ScreenFootball(InteriorKit k, Mats M, Vector3 c, float w, float h)
        {
            k.C = new Color(0.25f, 0.65f, 0.3f);
            k.Box(M.pintado, new Vector3(c.x - w * 0.5f, c.y - h * 0.5f, c.z - 0.002f), new Vector3(c.x + w * 0.5f, c.y + h * 0.5f, c.z + 0.002f));
            k.C = Color.white;
            k.Box(M.pintado, new Vector3(c.x - 0.004f, c.y - h * 0.45f, c.z - 0.004f), new Vector3(c.x + 0.004f, c.y + h * 0.45f, c.z - 0.002f));
            k.Box(M.pintado, new Vector3(c.x - w * 0.47f, c.y + h * 0.36f, c.z - 0.004f), new Vector3(c.x - w * 0.2f, c.y + h * 0.44f, c.z - 0.002f));
        }

        static void WallTV(InteriorKit k, Mats M, Vector3 p, float yaw, string name)
        {
            // TV na parede (suporte), ecrã a olhar para +z local
            k.Push(p, yaw);
            k.C = new Color(0.08f, 0.08f, 0.09f);
            k.Box(M.pintado, new Vector3(-0.1f, -0.1f, 0f), new Vector3(0.1f, 0.1f, 0.12f));
            k.Box(M.pintado, new Vector3(-0.55f, -0.33f, 0.12f), new Vector3(0.55f, 0.33f, 0.17f));
            k.C = Color.white;
            k.Push(new Vector3(0f, 0f, 0.175f), 180f);
            ScreenFootball(k, M, Vector3.zero, 1.02f, 0.58f);
            k.Pop();
            k.Pop();
        }

        static void Bookshelf(InteriorKit k, Mats M, Vector3 b, float yaw, System.Random rng, string name)
        {
            k.Push(b, yaw);
            float w = 0.9f, d = 0.32f, hh = 1.9f;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0f, -d * 0.5f), new Vector3(-w * 0.5f + 0.03f, hh, d * 0.5f));
            k.Box(M.madeira, new Vector3(w * 0.5f - 0.03f, 0f, -d * 0.5f), new Vector3(w * 0.5f, hh, d * 0.5f));
            k.Box(M.madeira, new Vector3(-w * 0.5f, 0f, d * 0.5f - 0.02f), new Vector3(w * 0.5f, hh, d * 0.5f));
            for (int i = 0; i < 5; i++)
            {
                float y = 0.05f + i * 0.45f;
                k.C = WoodDark;
                k.Box(M.madeira, new Vector3(-w * 0.5f, y, -d * 0.5f), new Vector3(w * 0.5f, y + 0.03f, d * 0.5f));
                if (i == 4) break;
                float x = -w * 0.5f + 0.05f;
                while (x < w * 0.5f - 0.1f)
                {
                    float bw = 0.03f + (float)rng.NextDouble() * 0.03f, bh = 0.24f + (float)rng.NextDouble() * 0.12f;
                    if (rng.Next(7) == 0) { x += 0.12f; continue; }
                    k.C = new Color(0.2f + (float)rng.NextDouble() * 0.6f, 0.15f + (float)rng.NextDouble() * 0.4f, 0.12f + (float)rng.NextDouble() * 0.4f);
                    k.Box(M.pintado, new Vector3(x, y + 0.03f, -d * 0.5f + 0.03f), new Vector3(x + bw, y + 0.03f + bh, d * 0.5f - 0.05f));
                    x += bw + 0.004f;
                }
            }
            k.C = Color.white;
            k.Col(name + "_Estante", new Vector3(-w * 0.5f, 0f, -d * 0.5f), new Vector3(w * 0.5f, hh, d * 0.5f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.1f, 0.6f, yaw);
        }

        static void Fridge(InteriorKit k, Mats M, Vector3 b, float yaw, string name)
        {
            // frente para −z local
            k.Push(b, yaw);
            k.C = new Color(0.93f, 0.94f, 0.95f);
            k.Box(M.pintado, new Vector3(-0.33f, 0f, -0.32f), new Vector3(0.33f, 1.8f, 0.32f));
            k.C = new Color(0.85f, 0.86f, 0.88f);
            k.Box(M.pintado, new Vector3(-0.33f, 1.18f, -0.33f), new Vector3(0.33f, 1.2f, -0.32f));
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(0.24f, 1.28f, -0.37f), new Vector3(0.27f, 1.6f, -0.33f));
            k.Box(M.pintado, new Vector3(0.24f, 0.75f, -0.37f), new Vector3(0.27f, 1.1f, -0.33f));
            // ímanes e desenho de criança
            var rng = new System.Random(name.Length * 7);
            for (int i = 0; i < 5; i++)
            {
                k.C = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                float x = -0.25f + (float)rng.NextDouble() * 0.4f, y = 1.3f + (float)rng.NextDouble() * 0.4f;
                k.Box(M.pintado, new Vector3(x, y, -0.335f), new Vector3(x + 0.05f, y + 0.04f, -0.32f));
            }
            k.C = new Color(0.97f, 0.97f, 0.92f);
            k.Box(M.pintado, new Vector3(-0.22f, 0.8f, -0.333f), new Vector3(0.05f, 1.05f, -0.325f));
            k.C = Color.white;
            k.Col(name + "_Frigorifico", new Vector3(-0.33f, 0f, -0.32f), new Vector3(0.33f, 1.8f, 0.32f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 0.9f, 0.9f, yaw);
        }

        static void KitchenRun(InteriorKit k, Mats M, float xa, float xb, float zWall, System.Random rng, string name)
        {
            float z = zWall, len = xb - xa;
            // armários de baixo, bancada, lava-loiça, placa, azulejos, armários de cima
            k.C = new Color(0.9f, 0.88f, 0.82f);
            k.Box(M.pintado, new Vector3(xa, 0.1f, z - 0.58f), new Vector3(xb, 0.86f, z));
            k.C = new Color(0.25f, 0.24f, 0.24f);
            k.Box(M.pintado, new Vector3(xa, 0.03f, z - 0.52f), new Vector3(xb, 0.1f, z));
            int nd = Mathf.Max(1, Mathf.RoundToInt(len / 0.6f));
            for (int i = 0; i < nd; i++)
            {
                float a = xa + i * len / nd + 0.02f, b2 = xa + (i + 1) * len / nd - 0.02f;
                k.C = new Color(0.55f, 0.72f, 0.7f);
                k.Box(M.pintado, new Vector3(a, 0.14f, z - 0.6f), new Vector3(b2, 0.82f, z - 0.58f));
                k.C = Chrome;
                k.Box(M.pintado, new Vector3((a + b2) * 0.5f - 0.08f, 0.74f, z - 0.62f), new Vector3((a + b2) * 0.5f + 0.08f, 0.76f, z - 0.6f));
            }
            k.C = new Color(0.32f, 0.3f, 0.3f);
            k.Box(M.pintado, new Vector3(xa - 0.02f, 0.86f, z - 0.62f), new Vector3(xb + 0.02f, 0.9f, z));
            // lava-loiça com torneira
            float sx = xa + len * 0.3f;
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(sx - 0.3f, 0.9f, z - 0.52f), new Vector3(sx + 0.3f, 0.905f, z - 0.1f));
            k.C = new Color(0.55f, 0.57f, 0.6f);
            k.Box(M.pintado, new Vector3(sx - 0.25f, 0.905f, z - 0.48f), new Vector3(sx + 0.25f, 0.907f, z - 0.14f));
            k.C = Chrome;
            k.Rod(M.pintado, new Vector3(sx, 0.9f, z - 0.08f), new Vector3(sx, 1.15f, z - 0.08f), 0.015f, 6);
            k.Rod(M.pintado, new Vector3(sx, 1.15f, z - 0.08f), new Vector3(sx, 1.12f, z - 0.25f), 0.012f, 6);
            // placa a gás com panela
            float cx = xa + len * 0.72f;
            k.C = new Color(0.1f, 0.1f, 0.1f);
            k.Box(M.pintado, new Vector3(cx - 0.3f, 0.9f, z - 0.5f), new Vector3(cx + 0.3f, 0.92f, z - 0.08f));
            k.C = new Color(0.3f, 0.3f, 0.3f);
            foreach (var dx in new[] { -0.15f, 0.15f })
                foreach (var dz in new[] { -0.18f, -0.38f })
                    k.Torus(M.pintado, new Vector3(cx + dx, 0.925f, z + dz), 0.06f, 0.008f, 10, 3);
            k.C = new Color(0.6f, 0.62f, 0.65f);
            k.Lathe(M.pintado, new Vector3(cx - 0.15f, 0.93f, z - 0.38f), new[] { new Vector2(0.11f, 0f), new Vector2(0.12f, 0.15f), new Vector2(0.125f, 0.16f) }, 12, true, true, false);
            // azulejos atrás e armários de cima
            k.C = Color.white;
            k.Box(M.azulejo, new Vector3(xa, 0.9f, z - 0.05f), new Vector3(xb, 1.55f, z - 0.026f));
            k.C = new Color(0.9f, 0.88f, 0.82f);
            k.Box(M.pintado, new Vector3(xa, 1.6f, z - 0.35f), new Vector3(xb, 2.25f, z));
            for (int i = 0; i < nd; i++)
            {
                float a = xa + i * len / nd + 0.02f, b2 = xa + (i + 1) * len / nd - 0.02f;
                k.C = new Color(0.55f, 0.72f, 0.7f);
                k.Box(M.pintado, new Vector3(a, 1.63f, z - 0.37f), new Vector3(b2, 2.22f, z - 0.35f));
            }
            // escorredor com pratos, garrafão de água
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(sx + 0.35f, 0.9f, z - 0.4f), new Vector3(sx + 0.7f, 0.93f, z - 0.15f));
            for (int i = 0; i < 4; i++)
            {
                k.C = i % 2 == 0 ? new Color(0.95f, 0.95f, 0.93f) : new Color(0.3f, 0.5f, 0.8f);
                k.Push(new Vector3(sx + 0.42f + i * 0.07f, 1.02f, z - 0.27f), 90f, 0f, 90f);
                k.Cyl(M.pintado, Vector3.zero, 0.1f, 0.012f, 12);
                k.Pop();
            }
            k.C = new Color(0.6f, 0.8f, 0.95f);
            k.Lathe(M.pintado, new Vector3(xb - 0.2f, 0.9f, z - 0.3f), new[] { new Vector2(0.12f, 0f), new Vector2(0.13f, 0.3f), new Vector2(0.05f, 0.38f), new Vector2(0.03f, 0.42f) }, 10);
            k.C = Color.white;
            k.Col(name + "_Cozinha", new Vector3(xa - 0.02f, 0f, z - 0.62f), new Vector3(xb + 0.02f, 0.92f, z));
            Blob(k, M, (xa + xb) * 0.5f, 0.03f, z - 0.35f, len + 0.3f, 0.9f);
        }

        static void DrinkCooler(InteriorKit k, Mats M, Vector3 b, float yaw, string name)
        {
            // frigorífico de bebidas com porta de vidro, frente para −z local
            k.Push(b, yaw);
            k.C = CvRed;
            k.Box(M.pintado, new Vector3(-0.38f, 0f, -0.33f), new Vector3(0.38f, 1.85f, 0.33f));
            k.C = Color.white;
            k.Box(M.pintado, new Vector3(-0.36f, 1.6f, -0.345f), new Vector3(0.36f, 1.8f, -0.33f));
            k.Box(M.vidroLuz, new Vector3(-0.32f, 0.15f, -0.34f), new Vector3(0.32f, 1.55f, -0.335f));
            var rng = new System.Random(name.Length + 3);
            for (int sIdx = 0; sIdx < 4; sIdx++)
            {
                float y = 0.2f + sIdx * 0.34f;
                for (float x = -0.28f; x < 0.28f; x += 0.08f)
                    Bottle(k, M, new Vector3(x, y, -0.25f), 1, BottleCols[rng.Next(BottleCols.Length)]);
            }
            k.C = Color.white;
            k.Col(name + "_Frigo", new Vector3(-0.38f, 0f, -0.34f), new Vector3(0.38f, 1.85f, 0.33f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.0f, 0.9f, yaw);
        }

        static void Dartboard(InteriorKit k, Mats M, Vector3 p, float yaw)
        {
            // alvo de dardos (eixo do cilindro = normal da parede, +z local)
            k.Push(p, yaw, 90f, 0f);
            k.C = new Color(0.12f, 0.12f, 0.12f);
            k.Cyl(M.pintado, Vector3.zero, 0.24f, 0.04f, 20);
            k.C = new Color(0.9f, 0.86f, 0.7f);
            k.Cyl(M.pintado, new Vector3(0f, 0.04f, 0f), 0.2f, 0.004f, 20);
            k.C = new Color(0.1f, 0.4f, 0.15f);
            k.Torus(M.pintado, new Vector3(0f, 0.045f, 0f), 0.19f, 0.01f, 20, 3);
            k.C = CvRed;
            k.Torus(M.pintado, new Vector3(0f, 0.045f, 0f), 0.11f, 0.01f, 18, 3);
            k.Cyl(M.pintado, new Vector3(0f, 0.044f, 0f), 0.02f, 0.006f, 10);
            k.C = new Color(0.2f, 0.2f, 0.2f);
            for (int i = 0; i < 10; i++)
            {
                k.Push(Vector3.zero, i * 36f);
                k.Box(M.pintado, new Vector3(-0.002f, 0.044f, 0f), new Vector3(0.002f, 0.046f, 0.2f));
                k.Pop();
            }
            k.C = Color.white;
            k.Pop();
        }

        static void Flag(InteriorKit k, Mats M, Vector3 p, float yaw, float w)
        {
            // bandeira de Cabo Verde: azul, faixas branca/vermelha/branca, círculo de 10 estrelas amarelas
            float h = w * 0.6f;
            k.Push(p, yaw);
            k.C = CvBlue;
            k.Box(M.pintado, new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0.01f));
            float sy = -h * 0.5f + h * 0.25f;   // faixas a meio da metade de baixo
            float u = h / 12f;
            k.C = Color.white;
            k.Box(M.pintado, new Vector3(-w * 0.5f, sy - u * 1.5f, 0.01f), new Vector3(w * 0.5f, sy + u * 1.5f, 0.013f));
            k.C = CvRed;
            k.Box(M.pintado, new Vector3(-w * 0.5f, sy - u * 0.5f, 0.013f), new Vector3(w * 0.5f, sy + u * 0.5f, 0.016f));
            k.C = CvYellow;
            var cc = new Vector3(-w * 0.5f + w * 0.375f, sy, 0.016f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                var sp = cc + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * h * 0.28f;
                Star(k, M, sp, h * 0.035f);
            }
            k.C = Iron;
            k.Rod(M.pintado, new Vector3(-w * 0.5f - 0.05f, h * 0.5f + 0.03f, 0.02f), new Vector3(w * 0.5f + 0.05f, h * 0.5f + 0.03f, 0.02f), 0.01f, 5);
            k.C = Color.white;
            k.Pop();
        }

        static void Star(InteriorKit k, Mats M, Vector3 c, float r)
        {
            var poly = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float rr = i % 2 == 0 ? r : r * 0.42f;
                poly[i] = new Vector2(c.x + Mathf.Cos(a) * rr, c.y + Mathf.Sin(a) * rr);
            }
            // estrela não é convexa: centro + 5 triângulos
            for (int i = 0; i < 10; i += 2)
            {
                var a = poly[i];
                var b = poly[(i + 1) % 10];
                var d = poly[(i + 9) % 10];
                k.Prism(M.pintado, new[] { d, a, b, new Vector2(c.x, c.y) }, c.z, c.z + 0.003f);
            }
        }

        static void Poster(InteriorKit k, Mats M, Vector3 p, float yaw, System.Random rng)
        {
            // cartaz emoldurado (festas, morna, futebol): cores sólidas e "letras"
            k.Push(p, yaw);
            float w = 0.5f + (float)rng.NextDouble() * 0.2f, h = 0.7f;
            k.C = new Color(0.15f, 0.12f, 0.1f);
            k.Box(M.pintado, new Vector3(-w * 0.5f, -h * 0.5f, 0f), new Vector3(w * 0.5f, h * 0.5f, 0.02f));
            var bg = new[] { new Color(0.95f, 0.75f, 0.2f), new Color(0.2f, 0.45f, 0.7f), new Color(0.85f, 0.3f, 0.2f), new Color(0.25f, 0.6f, 0.4f) }[rng.Next(4)];
            k.C = bg;
            k.Box(M.pintado, new Vector3(-w * 0.5f + 0.03f, -h * 0.5f + 0.03f, 0.02f), new Vector3(w * 0.5f - 0.03f, h * 0.5f - 0.03f, 0.022f));
            k.C = Color.Lerp(bg, Color.white, 0.75f);
            k.Ellipsoid(M.pintado, new Vector3(0f, 0.06f, 0.024f), new Vector3(w * 0.25f, h * 0.18f, 0.003f), 12, 3);
            k.C = new Color(0.1f, 0.1f, 0.1f);
            for (int i = 0; i < 3; i++)
                k.Box(M.pintado, new Vector3(-w * 0.35f, -0.18f - i * 0.06f, 0.023f), new Vector3(w * (0.35f - i * 0.1f), -0.155f - i * 0.06f, 0.025f));
            k.C = Color.white;
            k.Pop();
        }

        static void Chalkboard(InteriorKit k, Mats M, Vector3 p, float yaw, System.Random rng)
        {
            k.Push(p, yaw);
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(-0.45f, -0.35f, 0f), new Vector3(0.45f, 0.35f, 0.03f));
            k.C = new Color(0.12f, 0.16f, 0.14f);
            k.Box(M.pintado, new Vector3(-0.4f, -0.3f, 0.03f), new Vector3(0.4f, 0.3f, 0.035f));
            k.C = new Color(0.92f, 0.92f, 0.88f);
            k.Box(M.pintado, new Vector3(-0.25f, 0.18f, 0.035f), new Vector3(0.25f, 0.22f, 0.037f));
            for (int i = 0; i < 5; i++)
            {
                float y = 0.08f - i * 0.08f;
                float wl = 0.15f + (float)rng.NextDouble() * 0.15f;
                k.Box(M.pintado, new Vector3(-0.34f, y, 0.035f), new Vector3(-0.34f + wl, y + 0.02f, 0.037f));
                k.Box(M.pintado, new Vector3(0.2f, y, 0.035f), new Vector3(0.33f, y + 0.02f, 0.037f));
            }
            k.C = Color.white;
            k.Pop();
        }

        static void Pendant(InteriorKit k, Mats M, Vector3 hang, float drop, Color shade)
        {
            k.C = new Color(0.1f, 0.1f, 0.1f);
            k.Rod(M.pintado, hang, hang - Vector3.up * drop, 0.006f, 4);
            k.Cyl(M.pintado, hang - Vector3.up * 0.03f, 0.05f, 0.03f, 8);
            var b = hang - Vector3.up * drop;
            k.C = shade;
            k.Lathe(M.pintado, b + Vector3.up * -0.2f, new[] { new Vector2(0.17f, 0f), new Vector2(0.14f, 0.08f), new Vector2(0.06f, 0.18f), new Vector2(0.03f, 0.22f), new Vector2(0f, 0.22f) }, 14, true, false, false);
            k.Lathe(M.pintado, b + Vector3.up * -0.2f, new[] { new Vector2(0.03f, 0.215f), new Vector2(0.06f, 0.175f), new Vector2(0.14f, 0.075f), new Vector2(0.165f, -0.003f) }, 14, true, false, false);
            k.C = Color.white;
            k.Sphere(M.vidroLuz, b + Vector3.up * -0.1f, 0.05f, 8, 5);
        }

        static void CeilingFan(InteriorKit k, Mats M, Vector3 hang)
        {
            k.C = new Color(0.85f, 0.83f, 0.78f);
            k.Rod(M.pintado, hang, hang - Vector3.up * 0.35f, 0.015f, 6);
            var b = hang - Vector3.up * 0.35f;
            k.Lathe(M.pintado, b + Vector3.up * -0.12f, new[] { new Vector2(0f, 0f), new Vector2(0.08f, 0.02f), new Vector2(0.1f, 0.08f), new Vector2(0.05f, 0.12f) }, 12);
            k.C = WoodMid;
            for (int i = 0; i < 5; i++)
            {
                k.Push(b + Vector3.up * -0.08f, i * 72f + 15f, 0f, 6f);
                k.Box(M.madeira, new Vector3(0.1f, -0.006f, -0.06f), new Vector3(0.62f, 0.006f, 0.06f));
                k.Pop();
            }
            k.C = Color.white;
            k.Sphere(M.vidroLuz, b + Vector3.up * -0.16f, 0.06f, 8, 4);
        }

        static void Speaker(InteriorKit k, Mats M, Vector3 p, float yaw)
        {
            k.Push(p, yaw);
            k.C = new Color(0.1f, 0.1f, 0.1f);
            k.Box(M.pintado, new Vector3(-0.15f, -0.22f, -0.12f), new Vector3(0.15f, 0.22f, 0.12f));
            k.C = new Color(0.25f, 0.25f, 0.26f);
            k.Push(new Vector3(0f, -0.06f, -0.125f), 0f, -90f, 0f);
            k.Cyl(M.pintado, Vector3.zero, 0.1f, 0.01f, 14);
            k.Pop();
            k.Push(new Vector3(0f, 0.14f, -0.125f), 0f, -90f, 0f);
            k.Cyl(M.pintado, Vector3.zero, 0.04f, 0.01f, 10);
            k.Pop();
            k.C = Color.white;
            k.Pop();
        }

        static void Guitar(InteriorKit k, Mats M, Vector3 b, float yaw, float lean)
        {
            // violão (morna): corpo em 8, braço, cabeça
            k.Push(b, yaw, lean, 0f);
            k.C = new Color(0.8f, 0.55f, 0.28f);
            k.Ellipsoid(M.pintado, new Vector3(0f, 0.22f, 0f), new Vector3(0.19f, 0.2f, 0.05f), 14, 4);
            k.Ellipsoid(M.pintado, new Vector3(0f, 0.48f, 0f), new Vector3(0.15f, 0.15f, 0.05f), 14, 4);
            k.C = new Color(0.12f, 0.08f, 0.05f);
            k.Push(new Vector3(0f, 0.38f, -0.05f), 0f, 90f, 0f);
            k.Cyl(M.pintado, Vector3.zero, 0.045f, 0.004f, 12);
            k.Pop();
            k.C = new Color(0.35f, 0.22f, 0.12f);
            k.Box(M.pintado, new Vector3(-0.025f, 0.6f, -0.03f), new Vector3(0.025f, 1.0f, 0.0f));
            k.Box(M.pintado, new Vector3(-0.04f, 1.0f, -0.025f), new Vector3(0.04f, 1.15f, 0.005f));
            k.C = new Color(0.9f, 0.9f, 0.85f);
            for (int i = 0; i < 3; i++)
                k.Box(M.pintado, new Vector3(-0.015f + i * 0.015f - 0.001f, 0.15f, -0.055f), new Vector3(-0.015f + i * 0.015f + 0.001f, 1.0f, -0.053f));
            k.C = Color.white;
            k.Pop();
        }

        static void Stage(InteriorKit k, Mats M, Vector3 c, int e, string name)
        {
            // estrado de madeira com microfone, violão, cavaquinho, banco e colunas — noite de morna
            float w = 1.9f, d = 3.2f;
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(c.x - w * 0.5f, 0.03f, c.z - d * 0.5f), new Vector3(c.x + w * 0.5f, 0.28f, c.z + d * 0.5f));
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(c.x - w * 0.5f - 0.01f, 0.03f, c.z - d * 0.5f - 0.01f), new Vector3(c.x - w * 0.5f + 0.02f, 0.28f, c.z + d * 0.5f + 0.01f));
            k.Col(name + "_Palco", new Vector3(c.x - w * 0.5f, 0f, c.z - d * 0.5f), new Vector3(c.x + w * 0.5f, 0.28f, c.z + d * 0.5f));
            var top = new Vector3(c.x, 0.28f, c.z);
            Stool(k, M, top + new Vector3(e * 0.3f, 0f, 0.2f));
            Guitar(k, M, top + new Vector3(e * 0.6f, 0f, -0.7f), e * 90f + 180f, -12f);
            Guitar(k, M, top + new Vector3(e * 0.55f, 0f, 0.95f), e * 90f + 180f, -15f);
            // microfone de pé
            k.C = new Color(0.12f, 0.12f, 0.12f);
            k.Cyl(M.pintado, top + new Vector3(-e * 0.3f, 0f, 0.2f), 0.12f, 0.02f, 10);
            k.Rod(M.pintado, top + new Vector3(-e * 0.3f, 0f, 0.2f), top + new Vector3(-e * 0.3f, 1.45f, 0.2f), 0.01f, 5);
            k.Sphere(M.pintado, top + new Vector3(-e * 0.3f, 1.5f, 0.2f), 0.035f, 8, 4);
            Speaker(k, M, top + new Vector3(e * 0.6f, 0.22f, -1.35f), e * 90f + 180f - e * 30f);
            Speaker(k, M, top + new Vector3(e * 0.6f, 0.22f, 1.35f), e * 90f + 180f + e * 30f);
            Flag(k, M, new Vector3(c.x + e * (w * 0.5f - 0.05f) + e * 0.0f, 2.0f, c.z), e > 0 ? -90f : 90f, 1.2f);
        }

        static void Alambique(InteriorKit k, Mats M, Vector3 b, float sg, string name)
        {
            // fornalha de pedra com lume, caldeira de cobre, capitel, pescoço de cisne até à serpentina no barril
            k.C = new Color(0.55f, 0.5f, 0.45f);
            k.Lathe(M.pedraMuro, b, new[] { new Vector2(0.5f, 0f), new Vector2(0.48f, 0.5f), new Vector2(0.42f, 0.55f) }, 12, false, true, true);
            k.C = Color.white;
            k.Box(M.fogo, b + new Vector3(-0.18f, 0.08f, -0.49f), b + new Vector3(0.18f, 0.33f, -0.46f));
            k.C = Iron;
            k.Box(M.pintado, b + new Vector3(-0.22f, 0.05f, -0.5f), b + new Vector3(0.22f, 0.08f, -0.45f));
            k.Box(M.pintado, b + new Vector3(-0.22f, 0.33f, -0.5f), b + new Vector3(0.22f, 0.36f, -0.45f));
            var copper = new Color(0.78f, 0.45f, 0.25f);
            k.C = copper;
            k.Lathe(M.pintado, b + Vector3.up * 0.55f, new[] { new Vector2(0.35f, 0f), new Vector2(0.42f, 0.15f), new Vector2(0.4f, 0.4f), new Vector2(0.3f, 0.5f), new Vector2(0.12f, 0.58f) }, 16);
            k.Lathe(M.pintado, b + Vector3.up * 1.13f, new[] { new Vector2(0.12f, 0f), new Vector2(0.2f, 0.12f), new Vector2(0.18f, 0.3f), new Vector2(0.06f, 0.42f), new Vector2(0f, 0.44f) }, 14);
            k.C = new Color(0.65f, 0.38f, 0.2f);
            k.Torus(M.pintado, b + Vector3.up * 0.95f, 0.405f, 0.02f, 18, 4);
            var neck0 = b + new Vector3(0f, 1.45f, 0f);
            var neck1 = b + new Vector3(sg * 0.5f, 1.25f, -0.05f);
            var neck2 = b + new Vector3(sg * 0.95f, 0.95f, -0.1f);
            k.C = copper;
            k.Rod(M.pintado, neck0, neck1, 0.04f, 8);
            k.Rod(M.pintado, neck1, neck2, 0.035f, 8);
            // barril da serpentina (água) e jarro de recolha
            var tank = b + new Vector3(sg * 0.95f, 0f, -0.1f);
            Barrel(k, M, tank, 0.28f, 0.9f);
            k.C = new Color(0.4f, 0.55f, 0.65f);
            k.Disc(M.pintado, tank + Vector3.up * 0.88f, 0.24f, 12, true);
            k.C = copper;
            k.Rod(M.pintado, tank + new Vector3(0f, 0.25f, -0.24f), tank + new Vector3(0f, 0.22f, -0.38f), 0.015f, 6);
            k.C = new Color(0.85f, 0.9f, 0.88f);
            k.Lathe(M.pintado, tank + new Vector3(0f, 0f, -0.4f), new[] { new Vector2(0.08f, 0f), new Vector2(0.1f, 0.12f), new Vector2(0.05f, 0.2f), new Vector2(0.04f, 0.22f) }, 10, true, true, false);
            k.C = Color.white;
            k.Col(name + "_Alambique", b + new Vector3(-0.5f, 0f, -0.5f), b + new Vector3(0.5f, 1.55f, 0.5f));
            k.Col(name + "_Serpentina", tank + new Vector3(-0.3f, 0f, -0.5f), tank + new Vector3(0.3f, 0.9f, 0.3f));
            Blob(k, M, b.x, b.y, b.z, 1.3f, 1.3f);
        }

        static void SugarCane(InteriorKit k, Mats M, Vector3 b, System.Random rng)
        {
            for (int i = 0; i < 9; i++)
            {
                float x = b.x + (float)rng.NextDouble() * 0.3f - 0.15f, z = b.z + (float)rng.NextDouble() * 0.12f - 0.06f;
                k.C = new Color(0.65f + (float)rng.NextDouble() * 0.1f, 0.6f, 0.3f);
                var top = new Vector3(x + 0.05f, b.y + 1.7f + (float)rng.NextDouble() * 0.3f, z + 0.1f);
                k.Rod(M.pintado, new Vector3(x, b.y, z), top, 0.02f, 5);
                k.C = new Color(0.35f, 0.55f, 0.2f);
                k.Rod(M.pintado, top, top + new Vector3(0.2f, -0.15f, 0.05f), 0.01f, 3, false);
            }
            k.C = Color.white;
        }

        static void GrogueSet(InteriorKit k, Mats M, Vector3 top, System.Random rng)
        {
            Bottle(k, M, top + new Vector3(0f, 0f, 0f), 3, new Color(0.88f, 0.92f, 0.9f));
            for (int i = 0; i < 3; i++)
            {
                k.C = new Color(0.85f, 0.92f, 0.95f);
                var p = top + new Vector3(-0.3f + i * 0.25f, 0f, i == 1 ? 0.18f : -0.15f);
                k.Lathe(M.pintado, p, new[] { new Vector2(0.022f, 0f), new Vector2(0.026f, 0.07f) }, 8, true, true, false);
                k.C = new Color(0.95f, 0.9f, 0.75f);
                k.Cyl(M.pintado, p + Vector3.up * 0.005f, 0.021f, 0.04f, 8);
            }
            k.C = new Color(0.9f, 0.85f, 0.4f);
            k.Sphere(M.pintado, top + new Vector3(0.35f, 0.03f, 0.1f), 0.03f, 6, 3);
            k.C = Color.white;
        }

        static void StringLights(InteriorKit k, Mats M, Vector3 a, Vector3 b, int n)
        {
            k.C = new Color(0.12f, 0.12f, 0.12f);
            Vector3 prev = a;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * Mathf.Sin(t * Mathf.PI * 3f) * Mathf.Sin(t * Mathf.PI * 3f) * 0.12f;
                k.Rod(M.pintado, prev, p, 0.004f, 3, false);
                prev = p;
            }
            k.C = Color.white;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI * 3f) * Mathf.Sin(t * Mathf.PI * 3f) * 0.12f + 0.05f);
                k.Sphere(M.vidroLuz, p, 0.025f, 6, 3);
            }
        }

        static void ShopShelf(InteriorKit k, Mats M, float xa, float xb, float zWall, float hh, System.Random rng, string name)
        {
            float z = zWall;
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(xa, 0.03f, z - 0.45f), new Vector3(xb, 0.1f, z));
            k.Box(M.madeira, new Vector3(xa, 0.03f, z - 0.03f), new Vector3(xb, hh, z));
            for (float x = xa; x <= xb + 0.01f; x += (xb - xa) / Mathf.Max(1, Mathf.RoundToInt((xb - xa) / 1.1f)))
                k.Box(M.madeira, new Vector3(x - 0.02f, 0.03f, z - 0.45f), new Vector3(x + 0.02f, hh, z));
            for (int sIdx = 0; sIdx < 5; sIdx++)
            {
                float y = 0.1f + sIdx * 0.44f;
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(xa, y - 0.03f, z - 0.45f), new Vector3(xb, y, z));
                float x = xa + 0.06f;
                while (x < xb - 0.15f)
                {
                    int kind = rng.Next(5);
                    if (kind == 0)
                    {
                        // latas
                        k.C = new[] { CvRed, new Color(0.9f, 0.75f, 0.1f), new Color(0.2f, 0.5f, 0.3f) }[rng.Next(3)];
                        for (int j = 0; j < 3; j++) k.Cyl(M.pintado, new Vector3(x + 0.04f, y, z - 0.12f - j * 0.1f), 0.035f, 0.11f, 8);
                        x += 0.09f;
                    }
                    else if (kind == 1)
                    {
                        // caixas de cereais / bolachas
                        k.C = new Color(0.3f + (float)rng.NextDouble() * 0.6f, 0.3f + (float)rng.NextDouble() * 0.5f, 0.2f + (float)rng.NextDouble() * 0.5f);
                        k.Box(M.pintado, new Vector3(x, y, z - 0.35f), new Vector3(x + 0.18f, y + 0.28f, z - 0.1f));
                        x += 0.2f;
                    }
                    else if (kind == 2)
                    {
                        // garrafas de óleo / água
                        Bottle(k, M, new Vector3(x + 0.05f, y, z - 0.2f), 0, new Color(0.9f, 0.8f, 0.3f));
                        Bottle(k, M, new Vector3(x + 0.13f, y, z - 0.25f), 0, new Color(0.6f, 0.8f, 0.95f));
                        x += 0.2f;
                    }
                    else if (kind == 3)
                    {
                        // pacotes de massa / arroz
                        k.C = new Color(0.95f, 0.9f, 0.7f);
                        k.Box(M.pintado, new Vector3(x, y, z - 0.3f), new Vector3(x + 0.1f, y + 0.2f, z - 0.15f));
                        k.C = new Color(0.2f, 0.35f, 0.75f);
                        k.Box(M.pintado, new Vector3(x - 0.002f, y + 0.12f, z - 0.302f), new Vector3(x + 0.102f, y + 0.17f, z - 0.15f));
                        x += 0.12f;
                    }
                    else x += 0.1f;
                }
            }
            k.C = Color.white;
            k.Col(name + "_Prateleiras", new Vector3(xa, 0f, z - 0.45f), new Vector3(xb, hh, z));
        }

        static void ShopCounter(InteriorKit k, Mats M, float xa, float xb, float z0, float z1, System.Random rng, string name)
        {
            k.C = new Color(0.75f, 0.78f, 0.72f);
            k.Box(M.pintado, new Vector3(xa, 0.03f, z0), new Vector3(xb, 0.95f, z1));
            k.C = WoodMid;
            k.Box(M.madeira, new Vector3(xa - 0.04f, 0.95f, z0 - 0.04f), new Vector3(xb + 0.04f, 1.0f, z1 + 0.02f));
            // caixa registadora
            float rx = xa + 0.4f;
            k.C = new Color(0.25f, 0.25f, 0.27f);
            k.Box(M.pintado, new Vector3(rx - 0.2f, 1.0f, z0 + 0.15f), new Vector3(rx + 0.2f, 1.12f, z0 + 0.45f));
            k.Push(new Vector3(rx, 1.12f, z0 + 0.32f), 0f, -30f, 0f);
            k.Box(M.pintado, new Vector3(-0.18f, 0f, -0.1f), new Vector3(0.18f, 0.03f, 0.1f));
            k.Pop();
            k.C = new Color(0.4f, 0.75f, 0.45f);
            k.Box(M.pintado, new Vector3(rx - 0.1f, 1.17f, z0 + 0.42f), new Vector3(rx + 0.1f, 1.24f, z0 + 0.425f));
            // balança
            float bx = (xa + xb) * 0.5f;
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(bx - 0.15f, 1.0f, z0 + 0.15f), new Vector3(bx + 0.15f, 1.1f, z0 + 0.4f));
            k.Lathe(M.pintado, new Vector3(bx, 1.1f, z0 + 0.28f), new[] { new Vector2(0.03f, 0f), new Vector2(0.17f, 0.04f), new Vector2(0.18f, 0.06f) }, 12, true, true, false);
            // frascos de rebuçados e cesto de pão
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(xb - 0.25f - i * 0.18f, 1.0f, z0 + 0.15f);
                k.C = new[] { CvRed, CvYellow, new Color(0.4f, 0.75f, 0.4f) }[i];
                k.Cyl(M.pintado, p, 0.06f, 0.12f, 10);
                k.C = new Color(0.85f, 0.92f, 0.95f);
                k.Lathe(M.pintado, p, new[] { new Vector2(0.07f, 0f), new Vector2(0.075f, 0.18f), new Vector2(0.05f, 0.2f) }, 10, true, true, false);
            }
            k.C = new Color(0.75f, 0.55f, 0.3f);
            k.Lathe(M.pintado, new Vector3(bx + 0.45f, 1.0f, z0 + 0.35f), new[] { new Vector2(0.14f, 0f), new Vector2(0.2f, 0.1f) }, 12, true, true, false);
            k.C = new Color(0.85f, 0.62f, 0.32f);
            for (int i = 0; i < 4; i++) k.Ellipsoid(M.pintado, new Vector3(bx + 0.38f + i * 0.05f, 1.1f, z0 + 0.32f + (i % 2) * 0.06f), new Vector3(0.06f, 0.04f, 0.1f), 7, 4);
            k.C = Color.white;
            k.Col(name + "_Balcao", new Vector3(xa - 0.04f, 0f, z0 - 0.04f), new Vector3(xb + 0.04f, 1.05f, z1 + 0.02f));
            Blob(k, M, (xa + xb) * 0.5f, 0.03f, (z0 + z1) * 0.5f, xb - xa + 0.4f, z1 - z0 + 0.4f);
        }

        static void FruitStand(InteriorKit k, Mats M, Vector3 b, float yaw, System.Random rng, string name)
        {
            // estrado inclinado com 3 caixotes de fruta/legumes, frente para −z local
            k.Push(b, yaw);
            k.C = WoodLight;
            k.Box(M.madeira, new Vector3(-0.55f, 0f, -0.3f), new Vector3(0.55f, 0.55f, 0.35f));
            var produce = new[] { new Color(0.95f, 0.6f, 0.1f), new Color(0.85f, 0.15f, 0.1f), new Color(0.95f, 0.85f, 0.2f), new Color(0.3f, 0.6f, 0.2f), new Color(0.55f, 0.3f, 0.5f) };
            for (int i = 0; i < 3; i++)
            {
                float x = -0.36f + i * 0.36f;
                k.Push(new Vector3(x, 0.55f, 0.02f), 0f, -15f, 0f);
                k.C = WoodMid;
                k.Box(M.madeira, new Vector3(-0.17f, 0f, -0.25f), new Vector3(0.17f, 0.12f, 0.25f));
                var col = produce[rng.Next(produce.Length)];
                for (int j = 0; j < 10; j++)
                {
                    k.C = col * (0.85f + 0.2f * (float)rng.NextDouble());
                    k.Sphere(M.pintado, new Vector3(-0.12f + (j % 4) * 0.08f, 0.15f, -0.18f + (j / 4) * 0.12f), 0.045f, 6, 4);
                }
                k.Pop();
            }
            k.C = Color.white;
            k.Col(name + "_Fruta", new Vector3(-0.55f, 0f, -0.32f), new Vector3(0.55f, 0.85f, 0.35f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.3f, 0.9f, yaw);
        }

        static void Sack(InteriorKit k, Mats M, Vector3 b, Color col, System.Random rng)
        {
            k.C = col;
            k.Ellipsoid(M.pintado, b + Vector3.up * 0.3f, new Vector3(0.24f, 0.32f, 0.2f), 10, 5);
            k.Cone(M.pintado, b + Vector3.up * 0.55f, 0.1f, 0.03f, 0.12f, 8);
            k.C = new Color(0.55f, 0.45f, 0.3f);
            k.Torus(M.pintado, b + Vector3.up * 0.58f, 0.05f, 0.012f, 8, 3);
            k.C = new Color(0.9f, 0.85f, 0.6f);
            k.Ellipsoid(M.pintado, b + Vector3.up * 0.62f, new Vector3(0.09f, 0.03f, 0.09f), 8, 3);
            k.C = Color.white;
            Blob(k, M, b.x, b.y, b.z, 0.6f, 0.55f);
        }

        static void Bananas(InteriorKit k, Mats M, Vector3 hang)
        {
            k.C = new Color(0.55f, 0.45f, 0.3f);
            k.Rod(M.pintado, hang, hang - Vector3.up * 0.35f, 0.006f, 3);
            var b = hang - Vector3.up * 0.4f;
            k.C = new Color(0.35f, 0.55f, 0.2f);
            k.Cyl(M.pintado, b - Vector3.up * 0.35f, 0.025f, 0.4f, 6);
            k.C = new Color(0.95f, 0.85f, 0.25f);
            for (int r = 0; r < 4; r++)
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI / 3f + r * 0.4f;
                    var p = b + new Vector3(Mathf.Cos(a) * 0.08f, -0.05f - r * 0.08f, Mathf.Sin(a) * 0.08f);
                    k.Rod(M.pintado, p, p + new Vector3(Mathf.Cos(a) * 0.1f, 0.06f, Mathf.Sin(a) * 0.1f), 0.018f, 5);
                }
            k.C = Color.white;
        }

        static void BarberChair(InteriorKit k, Mats M, Vector3 b, float yaw, string name)
        {
            k.Push(b, yaw);
            k.C = Chrome;
            k.Cyl(M.pintado, Vector3.zero, 0.3f, 0.04f, 16);
            k.Cyl(M.pintado, Vector3.up * 0.04f, 0.07f, 0.35f, 10);
            k.C = new Color(0.55f, 0.08f, 0.1f);
            k.Box(M.pintado, new Vector3(-0.28f, 0.4f, -0.28f), new Vector3(0.28f, 0.55f, 0.28f));
            k.Push(new Vector3(0f, 0.55f, -0.26f), 0f, 12f, 0f);
            k.Box(M.pintado, new Vector3(-0.26f, 0f, -0.08f), new Vector3(0.26f, 0.65f, 0.04f));
            k.C = new Color(0.15f, 0.15f, 0.15f);
            k.Box(M.pintado, new Vector3(-0.12f, 0.68f, -0.06f), new Vector3(0.12f, 0.82f, 0.04f));
            k.Pop();
            k.C = Chrome;
            foreach (var s in new[] { -1f, 1f })
            {
                k.Box(M.pintado, new Vector3(s * 0.3f - 0.03f, 0.55f, -0.25f), new Vector3(s * 0.3f + 0.03f, 0.75f, 0.25f));
                k.C = new Color(0.55f, 0.08f, 0.1f);
                k.Box(M.pintado, new Vector3(s * 0.3f - 0.05f, 0.75f, -0.25f), new Vector3(s * 0.3f + 0.05f, 0.8f, 0.25f));
                k.C = Chrome;
            }
            k.Box(M.pintado, new Vector3(-0.2f, 0.12f, 0.35f), new Vector3(0.2f, 0.16f, 0.55f));
            k.Rod(M.pintado, new Vector3(0f, 0.4f, 0.2f), new Vector3(0f, 0.16f, 0.45f), 0.02f, 6);
            k.C = Color.white;
            k.Col(name + "_Cadeira", new Vector3(-0.35f, 0f, -0.35f), new Vector3(0.35f, 0.8f, 0.45f));
            k.Pop();
            Blob(k, M, b.x, b.y, b.z, 1.0f, 1.1f, yaw);
        }

        static void BarberStation(InteriorKit k, Mats M, Vector3 wallP, System.Random rng, string name)
        {
            // bancada estreita + espelho grande na parede do fundo (+z), com tesouras, pentes, frascos
            float z = wallP.z, x = wallP.x;
            k.C = new Color(0.92f, 0.92f, 0.9f);
            k.Box(M.pintado, new Vector3(x - 0.65f, 0.75f, z - 0.35f), new Vector3(x + 0.65f, 0.8f, z));
            k.C = WoodDark;
            k.Box(M.madeira, new Vector3(x - 0.62f, 0.03f, z - 0.32f), new Vector3(x + 0.62f, 0.75f, z));
            k.C = Chrome;
            k.Box(M.pintado, new Vector3(x - 0.55f, 1.0f, z - 0.03f), new Vector3(x + 0.55f, 2.1f, z - 0.01f));
            k.C = new Color(0.7f, 0.8f, 0.85f);
            k.Box(M.pintado, new Vector3(x - 0.5f, 1.05f, z - 0.035f), new Vector3(x + 0.5f, 2.05f, z - 0.03f));
            for (int i = 0; i < 4; i++)
            {
                k.C = BottleCols[rng.Next(BottleCols.Length)];
                k.Lathe(M.pintado, new Vector3(x - 0.45f + i * 0.12f, 0.8f, z - 0.15f), new[] { new Vector2(0.03f, 0f), new Vector2(0.03f, 0.14f), new Vector2(0.012f, 0.18f) }, 8);
            }
            k.C = new Color(0.1f, 0.1f, 0.1f);
            k.Box(M.pintado, new Vector3(x + 0.15f, 0.8f, z - 0.2f), new Vector3(x + 0.33f, 0.81f, z - 0.16f));
            k.C = Chrome;
            k.Rod(M.pintado, new Vector3(x + 0.38f, 0.81f, z - 0.25f), new Vector3(x + 0.52f, 0.81f, z - 0.15f), 0.006f, 4);
            k.Rod(M.pintado, new Vector3(x + 0.38f, 0.81f, z - 0.15f), new Vector3(x + 0.52f, 0.81f, z - 0.25f), 0.006f, 4);
            k.C = new Color(0.1f, 0.1f, 0.12f);
            k.Box(M.pintado, new Vector3(x - 0.05f, 0.8f, z - 0.28f), new Vector3(x + 0.05f, 0.88f, z - 0.2f));
            k.C = Color.white;
            k.Col(name + "_Bancada", new Vector3(x - 0.65f, 0f, z - 0.35f), new Vector3(x + 0.65f, 0.82f, z));
        }

        static void Sink(InteriorKit k, Mats M, Vector3 b, float yaw)
        {
            k.Push(b, yaw);
            k.C = new Color(0.95f, 0.95f, 0.95f);
            k.Lathe(M.pintado, new Vector3(0f, 0f, 0f), new[] { new Vector2(0.1f, 0f), new Vector2(0.08f, 0.6f), new Vector2(0.25f, 0.82f), new Vector2(0.27f, 0.9f) }, 14, true, true, false);
            k.C = Chrome;
            k.Rod(M.pintado, new Vector3(0f, 0.9f, 0.25f), new Vector3(0f, 1.05f, 0.25f), 0.012f, 5);
            k.Rod(M.pintado, new Vector3(0f, 1.05f, 0.25f), new Vector3(0f, 1.02f, 0.12f), 0.01f, 5);
            k.C = Color.white;
            k.Pop();
        }

        static void BarberPole(InteriorKit k, Mats M, Vector3 p)
        {
            // poste de barbeiro na fachada (lado de fora, z negativo)
            k.C = Chrome;
            k.Box(M.pintado, p + new Vector3(-0.03f, -0.05f, 0.0f), p + new Vector3(0.03f, 0.05f, 0.05f));
            var c = p + new Vector3(0f, 0f, -0.1f);
            k.Cyl(M.pintado, c + Vector3.down * 0.4f, 0.07f, 0.05f, 10);
            k.Cyl(M.pintado, c + Vector3.up * 0.35f, 0.07f, 0.05f, 10);
            for (int i = 0; i < 6; i++)
            {
                k.C = i % 3 == 0 ? CvRed : i % 3 == 1 ? Color.white : CvBlue;
                k.Cyl(M.pintado, c + Vector3.down * (0.35f - i * 0.117f), 0.055f, 0.117f, 10, true, false);
            }
            k.C = Color.white;
        }

        static void DeskModern(InteriorKit k, Mats M, Vector3 b, float yaw, string name)
        {
            // secretária encostada (costas para −z local) com portátil, candeeiro, cadeira
            k.Push(b, yaw);
            k.C = new Color(0.95f, 0.95f, 0.93f);
            k.Box(M.pintado, new Vector3(-0.6f, 0.72f, -0.3f), new Vector3(0.6f, 0.76f, 0.3f));
            k.C = new Color(0.2f, 0.2f, 0.22f);
            foreach (var x in new[] { -0.56f, 0.56f })
                k.Box(M.pintado, new Vector3(x - 0.02f, 0.03f, -0.28f), new Vector3(x + 0.02f, 0.72f, 0.28f));
            k.C = new Color(0.65f, 0.66f, 0.7f);
            k.Box(M.pintado, new Vector3(-0.17f, 0.76f, -0.05f), new Vector3(0.17f, 0.78f, 0.18f));
            k.Push(new Vector3(0f, 0.78f, -0.05f), 0f, -15f, 0f);
            k.Box(M.pintado, new Vector3(-0.17f, 0f, -0.01f), new Vector3(0.17f, 0.23f, 0.0f));
            k.C = new Color(0.25f, 0.45f, 0.75f);
            k.Box(M.pintado, new Vector3(-0.15f, 0.02f, 0.0f), new Vector3(0.15f, 0.21f, 0.003f));
            k.Pop();
            k.C = new Color(0.15f, 0.15f, 0.15f);
            k.Cyl(M.pintado, new Vector3(0.45f, 0.76f, -0.15f), 0.07f, 0.02f, 10);
            k.Rod(M.pintado, new Vector3(0.45f, 0.78f, -0.15f), new Vector3(0.4f, 1.15f, 0.0f), 0.01f, 5);
            k.Cone(M.pintado, new Vector3(0.38f, 1.05f, 0.05f), 0.08f, 0.03f, 0.12f, 10, true, false);
            k.C = Color.white;
            k.Col(name + "_Secretaria", new Vector3(-0.6f, 0f, -0.3f), new Vector3(0.6f, 0.78f, 0.3f));
            k.Pop();
            Chair(k, M, b + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.6f), yaw + 180f, WoodDark, true);
            Blob(k, M, b.x, b.y, b.z, 1.4f, 0.8f, yaw);
        }
    }
}
