using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN.EditorTools
{
    public static partial class ArenaLayout
    {
        public const float BosqueHalf = 23f;

        /// <summary>
        /// Mapa "Bosque" (≈46x46 m): clareira de floresta ao fim da tarde, com a mesma simetria rotacional de 4 lados.
        /// Identidade: muitos arbustos (emboscadas e flancos escondidos), lago no centro que corta a linha de tiro
        /// a direito (obriga a contornar), e 4 torres de madeira nas diagonais com rampa e muretes virados para o centro
        /// (jogo vertical). Plataformas de salto levam do spawn do meio ao topo da torre mais próxima.
        /// </summary>
        public static List<Piece> BuildBosque()
        {
            const float half = BosqueHalf;
            var list = new List<Piece>();

            // ---------------- chão (variante 1 = relva de floresta) e lago central
            list.Add(new Piece { kind = PieceKind.Ground, center = new Vector3(0f, -0.5f, 0f), size = new Vector3(half * 2f + 40f, 1f, half * 2f + 40f), variant = 1 });
            list.Add(new Piece { kind = PieceKind.Water, center = Vector3.zero, size = new Vector3(5.5f, 0f, 5.5f) });

            for (int k = 0; k < 4; k++)
            {
                var q = new Quadrant(list, k);

                // Spawns: canto + meio do lado, com escudo de caixas e L de pedra
                q.Spawn(new Vector3(18.5f, 0.05f, 18.5f));
                q.Spawn(new Vector3(0f, 0.05f, 20.5f));
                q.Add(PieceKind.Crate, new Vector3(0f, 0.9f, 17.2f), new Vector3(4.4f, 1.8f, 1.2f));
                q.Add(PieceKind.Wall, new Vector3(14.8f, 1.1f, 12.6f), new Vector3(1.2f, 2.2f, 5f));
                q.Add(PieceKind.Wall, new Vector3(13.4f, 1.1f, 15.5f), new Vector3(4f, 2.2f, 1.2f));

                // Torre na diagonal: rampa a descer para fora e mureta em L virada para o centro
                q.Add(PieceKind.Platform, new Vector3(9f, 0.9f, 9f), new Vector3(5f, 1.8f, 5f));
                q.Ramp(new Vector3(9f, 0f, 11.5f), 2.6f, 1.8f, 4.4f);
                q.Add(PieceKind.Wall, new Vector3(6.75f, 2.3f, 7.8f), new Vector3(0.5f, 1f, 2.4f), 1);
                q.Add(PieceKind.Wall, new Vector3(7.8f, 2.3f, 6.75f), new Vector3(2.4f, 1f, 0.5f), 1);

                // Margem do lago: arbusto e rochedo
                q.Add(PieceKind.Bush, new Vector3(0f, 0f, 4.6f), new Vector3(3.6f, 1.5f, 1.8f));
                q.Add(PieceKind.Rock, new Vector3(3.9f, 0.8f, 3.9f), new Vector3(1.5f, 1.6f, 1.5f));

                // Arbustos do bosque
                q.Add(PieceKind.Bush, new Vector3(4.5f, 0f, 14f), new Vector3(2.4f, 1.5f, 4.4f));
                q.Add(PieceKind.Bush, new Vector3(13f, 0f, 8.2f), new Vector3(2.4f, 1.5f, 3.6f));
                q.Add(PieceKind.Bush, new Vector3(19f, 0f, 6f), new Vector3(2.6f, 1.5f, 5.5f));
                q.Add(PieceKind.Bush, new Vector3(9.5f, 0f, 19.6f), new Vector3(5f, 1.5f, 2.4f));

                // Cobertura baixa
                q.Add(PieceKind.Crate, new Vector3(11.5f, 0.75f, 3.2f), new Vector3(3.2f, 1.5f, 1.2f));
                q.Add(PieceKind.Fence, new Vector3(3.2f, 0.5f, 7.6f), new Vector3(3.5f, 1f, 0.25f));
                q.Add(PieceKind.Rock, new Vector3(20f, 0.9f, 15.5f), new Vector3(2f, 1.8f, 2f));

                // Destrutíveis
                q.Destructible(new Vector3(4.2f, 0.7f, 9.6f), new Vector3(2.4f, 1.4f, 1.2f), 160);
                q.Barrel(new Vector3(15f, 0.6f, 4.6f));
                q.Barrel(new Vector3(16f, 0.6f, 5.4f));
                q.Destructible(new Vector3(21f, 0.6f, 11.8f), new Vector3(1.2f, 1.2f, 1.2f), 110);

                // Plataforma de salto: do spawn do meio ao topo da torre
                q.JumpPad(new Vector3(3f, 0.05f, 19.8f), new Vector3(9f, 1.85f, 9f), 1.0f);

                // Trilho de terra do spawn do meio ao lago
                q.Add(PieceKind.Path, new Vector3(0f, 0.01f, 11.5f), new Vector3(2.6f, 0.02f, 8f));
            }

            // ---------------- limites e floresta exterior (duas filas de árvores, mais pinheiros)
            for (int k = 0; k < 4; k++)
            {
                var q = new Quadrant(list, k);
                q.Add(PieceKind.Boundary, new Vector3(0f, 1.5f, half + 1f), new Vector3(half * 2f + 4f, 3f, 2f));
                for (int row = 0; row < 2; row++)
                    for (int i = 0; i < 10; i++)
                    {
                        float x = -half + 1.5f + i * 5.2f + row * 2.6f;
                        float z = half + 4.2f + row * 3.4f + (i % 3) * 0.8f;
                        q.Add(PieceKind.Tree, new Vector3(x + ((i * 37) % 5 - 2) * 0.4f, 0f, z), new Vector3(1f, 1f, 1f), (i + row) % 3 == 0 ? 0 : 2);
                    }
            }

            Scatter(list, half, 4321);
            Waypoints(list, half);
            // Fundo das rampas das torres (os bots sobem a partir daqui)
            for (int k = 0; k < 4; k++)
                list.Add(new Piece { kind = PieceKind.Waypoint, center = new Quadrant(list, k).R(new Vector3(9f, 0f, 17.2f)), size = Vector3.one });
            return list;
        }
    }
}
