using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    public enum PieceKind
    {
        Ground,
        Path,
        Wall,       // pedra (bloqueia tudo)
        Crate,      // caixas empilhadas estáticas
        Fence,      // vedação baixa (salta-se)
        Rock,       // rochedo
        Bush,       // arbusto (atravessa-se, esconde)
        Water,      // lago (trava o movimento, os tiros passam)
        Destructible, // caixote destrutível (NetworkObject)
        Barrel,     // barril destrutível (NetworkObject)
        JumpPad,
        Spawn,
        Platform,
        Ramp,
        Totem,
        Tree,       // decoração fora da arena
        Boundary,   // parede exterior
        Flower,
        Grass,
        Mushroom,
        Waypoint,
    }

    public struct Piece
    {
        public PieceKind kind;
        public Vector3 center;   // para Ramp: ponto da borda alta
        public Vector3 size;     // para Ramp: (largura, altura, comprimento)
        public float yaw;
        public Vector3 target;   // JumpPad: destino
        public float value;      // JumpPad: tempo de voo; Destructible: vida
        public int variant;

        /// <summary>Ocupa o chão (bloqueia decoração/waypoints)?</summary>
        public bool Blocks => kind == PieceKind.Wall || kind == PieceKind.Crate || kind == PieceKind.Fence || kind == PieceKind.Rock
                              || kind == PieceKind.Water || kind == PieceKind.Destructible || kind == PieceKind.Barrel || kind == PieceKind.Platform
                              || kind == PieceKind.Ramp || kind == PieceKind.Totem || kind == PieceKind.JumpPad || kind == PieceKind.Spawn;
    }

    /// <summary>
    /// Mapa "Pradaria" (≈52x52 m): arena de relva com simetria rotacional de 4 lados (justo para todos-contra-todos),
    /// plataforma central com 4 rampas e totem, lagos, arbustos para emboscadas, caixotes e barris destrutíveis,
    /// plataformas de salto para o centro e spawns protegidos nos cantos e nos meios.
    /// É só dados — o ProjectSetup transforma cada peça em geometria.
    /// </summary>
    public static partial class ArenaLayout
    {
        public const float Half = 26f;

        public static List<Piece> Build()
        {
            var list = new List<Piece>();

            // ---------------- chão e caminhos
            list.Add(new Piece { kind = PieceKind.Ground, center = new Vector3(0f, -0.5f, 0f), size = new Vector3(Half * 2f + 40f, 1f, Half * 2f + 40f) });

            // ---------------- centro (não rodado)
            list.Add(new Piece { kind = PieceKind.Platform, center = new Vector3(0f, 0.75f, 0f), size = new Vector3(7f, 1.5f, 7f) });
            list.Add(new Piece { kind = PieceKind.Totem, center = new Vector3(0f, 1.5f, 0f), size = new Vector3(1.2f, 2.6f, 1.2f) });

            for (int k = 0; k < 4; k++)
            {
                var q = new Quadrant(list, k);

                // Rampa para a plataforma (borda alta em z = 3.5, desce para +z)
                q.Ramp(new Vector3(0f, 0f, 3.5f), 3f, 1.5f, 4.6f);
                // Muretes em L nos cantos da plataforma
                q.Add(PieceKind.Wall, new Vector3(2.55f, 2.0f, 3.2f), new Vector3(1.9f, 1.0f, 0.5f), 1);
                q.Add(PieceKind.Wall, new Vector3(3.2f, 2.0f, 2.55f), new Vector3(0.5f, 1.0f, 1.9f), 1);

                // Spawns: canto + meio do lado
                q.Spawn(new Vector3(21.5f, 0.05f, 21.5f));
                q.Spawn(new Vector3(0f, 0.05f, 23f));

                // Escudo do spawn do meio (caixas) e L de pedra a proteger o spawn do canto
                q.Add(PieceKind.Crate, new Vector3(0f, 0.9f, 19.6f), new Vector3(5f, 1.8f, 1.2f));
                q.Add(PieceKind.Wall, new Vector3(17.6f, 1.1f, 15.0f), new Vector3(1.2f, 2.2f, 6.2f));
                q.Add(PieceKind.Wall, new Vector3(14.6f, 1.1f, 17.6f), new Vector3(5f, 2.2f, 1.2f));

                // Lago com margem de pedras
                q.Add(PieceKind.Water, new Vector3(20f, 0f, 6.5f), new Vector3(5f, 0f, 5.2f));

                // Arbustos
                q.Add(PieceKind.Bush, new Vector3(9.5f, 0f, 21.8f), new Vector3(6f, 1.5f, 2.6f));
                q.Add(PieceKind.Bush, new Vector3(11.2f, 0f, 7.2f), new Vector3(2.6f, 1.5f, 4.2f));
                q.Add(PieceKind.Bush, new Vector3(6.2f, 0f, 13.6f), new Vector3(3.2f, 1.5f, 2.4f));

                // Cobertura média entre o spawn do meio e o centro
                q.Add(PieceKind.Wall, new Vector3(-4.6f, 0.75f, 11.5f), new Vector3(3.2f, 1.5f, 1f), 2);
                q.Add(PieceKind.Rock, new Vector3(12.5f, 0.9f, 12.5f), new Vector3(2.2f, 1.8f, 2.2f));

                // Destrutíveis
                q.Destructible(new Vector3(4.8f, 0.7f, 9f), new Vector3(2.4f, 1.4f, 1.2f), 160);
                q.Barrel(new Vector3(14.6f, 0.6f, 3.2f));
                q.Barrel(new Vector3(15.6f, 0.6f, 4.0f));
                q.Destructible(new Vector3(22.8f, 0.6f, 15.5f), new Vector3(1.2f, 1.2f, 1.2f), 110);

                // Vedação baixa a guiar para o centro
                q.Add(PieceKind.Fence, new Vector3(7.5f, 0.5f, 17f), new Vector3(4f, 1f, 0.25f));

                // Plataforma de salto: do corredor leste até à plataforma central
                q.JumpPad(new Vector3(23.8f, 0.05f, 10.8f), new Vector3(1.6f, 1.55f, 1.1f), 1.35f);

                // Caminho de terra do spawn do meio ao centro
                q.Add(PieceKind.Path, new Vector3(0f, 0.01f, 13.5f), new Vector3(3.2f, 0.02f, 10f));
            }

            // ---------------- limites e decoração exterior
            for (int k = 0; k < 4; k++)
            {
                var q = new Quadrant(list, k);
                q.Add(PieceKind.Boundary, new Vector3(0f, 1.5f, Half + 1f), new Vector3(Half * 2f + 4f, 3f, 2f));
                for (int i = 0; i < 9; i++)
                {
                    float x = -Half + 3f + i * 6.2f;
                    q.Add(PieceKind.Tree, new Vector3(x + ((i * 37) % 5 - 2) * 0.5f, 0f, Half + 4.5f + (i % 3) * 1.6f), new Vector3(1f, 1f, 1f), i % 3);
                }
            }

            Scatter(list, Half, 1234);
            Waypoints(list, Half);
            // Fundo das rampas (os bots sobem à plataforma a partir daqui)
            for (int k = 0; k < 4; k++)
                list.Add(new Piece { kind = PieceKind.Waypoint, center = new Quadrant(list, k).R(new Vector3(0f, 0f, 9.2f)), size = Vector3.one });
            return list;
        }

        // ------------------------------------------------------------------ simetria

        class Quadrant
        {
            readonly List<Piece> m_List;
            readonly int m_K;

            public Quadrant(List<Piece> list, int k)
            {
                m_List = list;
                m_K = k;
            }

            public Vector3 R(Vector3 p)
            {
                switch (m_K)
                {
                    case 1: return new Vector3(p.z, p.y, -p.x);
                    case 2: return new Vector3(-p.x, p.y, -p.z);
                    case 3: return new Vector3(-p.z, p.y, p.x);
                    default: return p;
                }
            }

            public float Yaw => m_K * 90f;

            public void Add(PieceKind kind, Vector3 center, Vector3 size, int variant = 0)
            {
                m_List.Add(new Piece { kind = kind, center = R(center), size = size, yaw = Yaw, variant = variant });
            }

            public void Ramp(Vector3 edge, float width, float height, float run)
            {
                m_List.Add(new Piece { kind = PieceKind.Ramp, center = R(edge), size = new Vector3(width, height, run), yaw = Yaw });
            }

            public void Spawn(Vector3 pos)
            {
                var p = R(pos);
                float yaw = Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;
                m_List.Add(new Piece { kind = PieceKind.Spawn, center = p, size = new Vector3(1.6f, 0.1f, 1.6f), yaw = yaw });
            }

            public void Destructible(Vector3 center, Vector3 size, int health)
            {
                m_List.Add(new Piece { kind = PieceKind.Destructible, center = R(center), size = size, yaw = Yaw, value = health });
            }

            public void Barrel(Vector3 center)
            {
                m_List.Add(new Piece { kind = PieceKind.Barrel, center = R(center), size = new Vector3(0.9f, 1.2f, 0.9f), yaw = Yaw, value = 80 });
            }

            public void JumpPad(Vector3 pos, Vector3 target, float flight)
            {
                m_List.Add(new Piece { kind = PieceKind.JumpPad, center = R(pos), size = new Vector3(2.2f, 0.1f, 2.2f), yaw = Yaw, target = R(target), value = flight });
            }
        }

        // ------------------------------------------------------------------ geometria auxiliar

        /// <summary>Retângulo ocupado no chão (meias medidas em X/Z já rodadas, com margem).</summary>
        public static bool Overlaps(Piece p, Vector3 point, float margin)
        {
            var d = point - p.center;
            float rad = -p.yaw * Mathf.Deg2Rad;
            float lx = d.x * Mathf.Cos(rad) + d.z * Mathf.Sin(rad);
            float lz = -d.x * Mathf.Sin(rad) + d.z * Mathf.Cos(rad);
            Vector2 half;
            if (p.kind == PieceKind.Ramp)
            {
                // A rampa estende-se da borda (z local 0) para +z local até 'run'
                lz -= p.size.z * 0.5f;
                half = new Vector2(p.size.x * 0.5f, p.size.z * 0.5f);
            }
            else half = new Vector2(p.size.x * 0.5f, p.size.z * 0.5f);
            return Mathf.Abs(lx) <= half.x + margin && Mathf.Abs(lz) <= half.y + margin;
        }

        static bool Free(List<Piece> list, float half, Vector3 point, float margin, bool avoidBushes, bool avoidPaths)
        {
            if (Mathf.Abs(point.x) > half - 0.8f || Mathf.Abs(point.z) > half - 0.8f) return false;
            foreach (var p in list)
            {
                if (p.Blocks && Overlaps(p, point, margin)) return false;
                if (avoidBushes && p.kind == PieceKind.Bush && Overlaps(p, point, margin)) return false;
                if (avoidPaths && p.kind == PieceKind.Path && Overlaps(p, point, 0.2f)) return false;
            }
            return true;
        }

        /// <summary>Flores, tufos de relva e cogumelos espalhados (determinístico, com simetria).</summary>
        static void Scatter(List<Piece> list, float half, int seed)
        {
            var rng = new System.Random(seed);
            var extra = new List<Piece>();
            for (int i = 0; i < 70; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * half, 0f, (float)rng.NextDouble() * half);
                double roll = rng.NextDouble();
                var kind = roll < 0.45 ? PieceKind.Grass : (roll < 0.85 ? PieceKind.Flower : PieceKind.Mushroom);
                int variant = rng.Next(0, 4);
                for (int k = 0; k < 4; k++)
                {
                    var q = new Quadrant(extra, k);
                    var w = q.R(p);
                    if (!Free(list, half, w, 0.4f, true, true)) continue;
                    extra.Add(new Piece { kind = kind, center = w, size = Vector3.one, yaw = (float)(i * 47 % 360), variant = variant });
                }
            }
            list.AddRange(extra);
        }

        /// <summary>Pontos de patrulha dos bots: grelha com folga para obstáculos.</summary>
        static void Waypoints(List<Piece> list, float half)
        {
            var extra = new List<Piece>();
            for (float x = -half + 3f; x <= half - 3f; x += 4f)
                for (float z = -half + 3f; z <= half - 3f; z += 4f)
                {
                    var p = new Vector3(x, 0f, z);
                    if (!Free(list, half, p, 1.1f, false, false)) continue;
                    extra.Add(new Piece { kind = PieceKind.Waypoint, center = p, size = Vector3.one });
                }
            list.AddRange(extra);
        }

        /// <summary>Desloca o mapa inteiro (cada mapa vive na mesma cena, afastado dos outros).</summary>
        public static void Offset(List<Piece> list, Vector3 offset)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                p.center += offset;
                if (p.kind == PieceKind.JumpPad) p.target += offset;
                list[i] = p;
            }
        }
    }
}
