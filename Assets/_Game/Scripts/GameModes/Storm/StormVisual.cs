using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Apresentação da tempestade de cinza em cada máquina (não decide nada): parede de nuvem cilíndrica no limite
    /// da zona, cinza a cair junto da parede, ecrã acinzentado e aviso quando o jogador local está fora, e um
    /// painel com a contagem ("fecha em 0:12" / "a fechar" / "zona final") e a distância à zona segura.
    /// </summary>
    [RequireComponent(typeof(StormManager))]
    public class StormVisual : MonoBehaviour
    {
        public Color wallColor = new Color(0.4f, 0.36f, 0.34f, 0.78f);
        public float wallBottom = -12f, wallHeight = 60f;

        StormManager m_Storm;
        Transform m_Wall;
        MeshRenderer m_WallRenderer;
        MaterialPropertyBlock m_Mpb;
        Texture2D m_AshTex, m_White;
        float m_AshCarry;
        GUIStyle m_Big, m_Small;
        static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");

        void Awake()
        {
            m_Storm = GetComponent<StormManager>();
            m_Mpb = new MaterialPropertyBlock();
            useGUILayout = false;   // só usa GUI.* (sem GUILayout): o Unity deixa de enviar o evento Layout
        }

        void Start()
        {
            m_AshTex = AshTexture();
            var shader = Shader.Find("FPSLAN/FxAlpha") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { mainTexture = m_AshTex, color = wallColor };
            if (mat.HasProperty("_VertexColor")) mat.SetFloat("_VertexColor", 0f);
            var go = new GameObject("ParedeTempestade");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = CylinderMesh(64);
            m_WallRenderer = go.AddComponent<MeshRenderer>();
            m_WallRenderer.sharedMaterial = mat;
            m_WallRenderer.shadowCastingMode = ShadowCastingMode.Off;
            m_WallRenderer.receiveShadows = false;
            m_Wall = go.transform;
            go.SetActive(false);
            m_White = Texture2D.whiteTexture;
        }

        static Texture2D AshTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Cinza" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = Mathf.PerlinNoise(x * 0.07f, y * 0.11f) * 0.6f + Mathf.PerlinNoise(x * 0.23f + 7f, y * 0.29f) * 0.4f;
                    byte c = (byte)(200 + 55 * v);
                    px[y * n + x] = new Color32(c, c, c, (byte)(150 + 105 * v));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Mesh CylinderMesh(int seg)
        {
            var v = new Vector3[(seg + 1) * 2];
            var uv = new Vector2[v.Length];
            var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = p;
                v[i * 2 + 1] = p + Vector3.up;
                uv[i * 2] = new Vector2(i * 8f / seg, 0f);
                uv[i * 2 + 1] = new Vector2(i * 8f / seg, 1f);
            }
            for (int i = 0; i < seg; i++)
            {
                int k = i * 6, a = i * 2;
                tri[k] = a; tri[k + 1] = a + 1; tri[k + 2] = a + 2;
                tri[k + 3] = a + 2; tri[k + 4] = a + 1; tri[k + 5] = a + 3;
            }
            var m = new Mesh { name = "Tempestade", vertices = v, uv = uv, triangles = tri };
            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(4f, 4f, 4f));
            return m;
        }

        void LateUpdate()
        {
            bool on = StormManager.IsActive;
            if (m_Wall == null) return;
            if (m_Wall.gameObject.activeSelf != on) m_Wall.gameObject.SetActive(on);
            if (!on) return;
            float r = m_Storm.CurrentRadius;
            m_Wall.position = new Vector3(m_Storm.center.x, m_Storm.center.y + wallBottom, m_Storm.center.z);
            m_Wall.localScale = new Vector3(r, wallHeight, r);
            m_WallRenderer.GetPropertyBlock(m_Mpb);
            m_Mpb.SetVector(MainTexSt, new Vector4(r * 0.25f, 1.5f, Time.time * 0.02f, Time.time * -0.05f));
            m_WallRenderer.SetPropertyBlock(m_Mpb);

            // cinza a cair junto à parede, perto do jogador local (pouca, para não tapar a mira)
            var me = PlayerNetwork.Local;
            if (me == null || !me.IsSpawned) return;
            var p = me.transform.position;
            var d = p - m_Storm.center;
            d.y = 0f;
            float toWall = Mathf.Abs(d.magnitude - r);
            if (toWall < 12f)
            {
                m_AshCarry += Time.deltaTime * 6f;
                while (m_AshCarry >= 1f)
                {
                    m_AshCarry -= 1f;
                    var dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
                    var side = new Vector3(-dir.z, 0f, dir.x);
                    var at = m_Storm.center + dir * r + side * Random.Range(-10f, 10f) + Vector3.up * Random.Range(0.5f, 4f);
                    at.y = p.y + Random.Range(0.3f, 3.5f);
                    Fx.Smoke(at, new Vector3(Random.Range(-0.3f, 0.3f), -0.2f, Random.Range(-0.3f, 0.3f)), Random.Range(1.4f, 2.4f), 2.5f, 0.3f);
                }
            }
        }

        void OnGUI()
        {
            if (!StormManager.IsActive || m_Storm == null) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            m_Storm.Evaluate(nm.ServerTime.Time, out int phase, out bool shrinking, out float r, out float target, out float until, out int dps);
            float s = Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);
            if (m_Big == null)
            {
                m_Big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                m_Small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            }
            m_Big.fontSize = Mathf.RoundToInt(17 * s);
            m_Small.fontSize = Mathf.RoundToInt(13 * s);

            var me = PlayerNetwork.Local;
            bool outside = me != null && me.IsSpawned && !me.Health.IsDead && !m_Storm.IsSafe(me.transform.position, r);
            if (outside)
            {
                var old = GUI.color;
                GUI.color = new Color(0.35f, 0.3f, 0.28f, 0.28f + 0.06f * Mathf.Sin(Time.time * 4f));
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), m_White);
                GUI.color = old;
            }

            string line;
            bool final = phase >= m_Storm.phases.Length - 1 && !shrinking;
            if (final) line = "FINAL ZONE · INSIDE THE VOLCANO";
            else if (shrinking) line = $"ASH STORM CLOSING IN · {Clock(until)}";
            else line = $"ASH STORM CLOSES IN {Clock(until)}";
            float w = 560 * s, h = 30 * s;
            var rect = new Rect(Screen.width / 2f - w / 2f, 72 * s, w, h);
            var bg = GUI.color;
            GUI.color = new Color(0.08f, 0.06f, 0.06f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height + 22 * s), m_White);
            GUI.color = shrinking || final ? new Color(1f, 0.62f, 0.3f) : new Color(0.92f, 0.88f, 0.84f);
            GUI.Label(rect, line, m_Big);
            GUI.color = outside ? new Color(1f, 0.45f, 0.35f) : new Color(0.85f, 0.82f, 0.78f);
            string sub;
            if (me != null && me.IsSpawned)
            {
                var d = me.transform.position - m_Storm.center;
                d.y = 0f;
                float away = Mathf.Max(0f, d.magnitude - r);
                sub = outside ? (away > 0.5f ? $"OUTSIDE THE SAFE ZONE · {away:0} m · −{dps} HP/s" : $"TOO HIGH · −{dps} HP/s")
                              : $"safe zone: {r:0} m · next: {target:0} m";
            }
            else sub = $"safe zone: {r:0} m";
            GUI.Label(new Rect(rect.x, rect.y + h - 4 * s, rect.width, 24 * s), sub, m_Small);
            GUI.color = bg;
        }

        static string Clock(float sec)
        {
            sec = Mathf.Max(0f, sec);
            return $"{(int)(sec / 60)}:{(int)(sec % 60):00}";
        }
    }
}
