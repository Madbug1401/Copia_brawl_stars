using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Lava do mapa: máscara em células de 0,4 m gerada pelo mesmo script do Blender que desenha as superfícies
    /// (Tools/Blender/Fogo/Fogo_Map.py → fogo_layout.json), por isso o volume de dano coincide com a lava visível.
    /// O HOST aplica o dano de contacto (por segundo, enquanto os pés estão na lava); estar ao lado, em terreno seco,
    /// não faz dano. Todas as máquinas mostram faíscas e fumo nos pés de quem está na lava.
    /// </summary>
    public class LavaArea : MonoBehaviour
    {
        public static LavaArea Instance { get; private set; }

        [Header("Máscara (do Blender)")]
        public int nx, ny;
        public float cell = 0.4f;
        [Tooltip("Canto da grelha em coordenadas do Blender (X, Y).")]
        public float x0, y0;
        [Tooltip("O FBX troca o sinal de X e Z (Blender → Unity).")]
        public bool flipX = true, flipZ = true;
        public float surfaceOut = -0.22f, surfaceIn = -1.72f;
        [Tooltip("Uma linha por Y: '0' seco, '1' lava exterior, '2' lava da cratera.")]
        public string[] rows = new string[0];

        [Header("Dano (host)")]
        public float damagePerSecond = 24f;
        [Tooltip("Os pés contam como dentro da lava até esta altura acima da superfície.")]
        public float depthTolerance = 0.4f;

        byte[] m_Mask;
        float m_Tick;
        readonly Dictionary<ulong, float> m_Accum = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, float> m_NextFx = new Dictionary<ulong, float>();
        readonly List<Vector3> m_Cells = new List<Vector3>();

        public IReadOnlyList<Vector3> LavaCells => m_Cells;

        void Awake()
        {
            Instance = this;
            Decode();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Decode()
        {
            m_Mask = new byte[Mathf.Max(0, nx * ny)];
            m_Cells.Clear();
            for (int j = 0; j < ny && j < rows.Length; j++)
            {
                var row = rows[j];
                for (int i = 0; i < nx && i < row.Length; i++)
                {
                    byte v = (byte)(row[i] - '0');
                    m_Mask[j * nx + i] = v;
                    if (v == 0) continue;
                    float bx = x0 + (i + 0.5f) * cell, by = y0 + (j + 0.5f) * cell;
                    m_Cells.Add(new Vector3(flipX ? -bx : bx, v == 2 ? surfaceIn : surfaceOut, flipZ ? -by : by));
                }
            }
        }

        /// <summary>Há lava por baixo deste ponto (X/Z)? Devolve a altura da superfície.</summary>
        public bool IsLava(Vector3 p, out float surfaceY)
        {
            surfaceY = 0f;
            if (m_Mask == null || m_Mask.Length == 0) return false;
            float bx = flipX ? -p.x : p.x, by = flipZ ? -p.z : p.z;
            int i = Mathf.FloorToInt((bx - x0) / cell), j = Mathf.FloorToInt((by - y0) / cell);
            if (i < 0 || j < 0 || i >= nx || j >= ny) return false;
            byte v = m_Mask[j * nx + i];
            if (v == 0) return false;
            surfaceY = v == 2 ? surfaceIn : surfaceOut;
            return true;
        }

        /// <summary>Pés de um jogador dentro da lava (não basta estar por cima, a saltar alto).</summary>
        public bool FeetInLava(Vector3 feet) => IsLava(feet, out float s) && feet.y < s + depthTolerance;

        /// <summary>Bots: desvia a direção de andar para não entrar na lava (se já estiver dentro, deixa sair).</summary>
        public Vector3 SteerAway(Vector3 pos, Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.01f || FeetInLava(pos)) return dir;
            if (!IsLava(pos + dir.normalized * 1.3f, out _)) return dir;
            foreach (float a in new[] { 35f, -35f, 70f, -70f, 110f, -110f })
            {
                var d = Quaternion.Euler(0f, a, 0f) * dir;
                if (!IsLava(pos + d.normalized * 1.3f, out _)) return d;
            }
            return dir;
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return;
            float dt = Time.deltaTime;

            // Efeito local nos pés de quem está na lava (todas as máquinas)
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                var feet = p.transform.position;
                if (!FeetInLava(feet)) continue;
                m_NextFx.TryGetValue(p.Id, out float next);
                if (Time.time < next) continue;
                m_NextFx[p.Id] = Time.time + 0.18f;
                Fx.Sparks(feet + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.15f), 3, 2.5f);
                if (Random.value < 0.35f) Fx.Smoke(feet + Vector3.up * 0.3f, Vector3.up * 0.8f, 0.6f, 1.2f, 0.35f);
            }

            if (!nm.IsServer) return;
            m_Tick += dt;
            if (m_Tick < 0.25f) return;
            float step = m_Tick;
            m_Tick = 0f;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || !p.IsSpawned || p.Health.IsDead) continue;
                if (!FeetInLava(p.transform.position)) { m_Accum.Remove(p.Id); continue; }
                m_Accum.TryGetValue(p.Id, out float acc);
                acc += damagePerSecond * step;
                int dmg = Mathf.FloorToInt(acc);
                if (dmg > 0)
                {
                    acc -= dmg;
                    EffectResolver.DamageRaw(p.Health, dmg, ulong.MaxValue, false, false);
                }
                m_Accum[p.Id] = acc;
            }
        }
    }
}
