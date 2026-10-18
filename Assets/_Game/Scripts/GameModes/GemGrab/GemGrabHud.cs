using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// HUD mínimo do Pique-Gemas (IMGUI, como o GameUI): gemas que levo, gemas por equipa,
    /// contagem de vitória e número de gemas por cima de quem as transporta.
    /// O placar do topo do GameUI já mostra AZUL x – y VERMELHA (BlueScore/RedScore = gemas).
    /// </summary>
    [RequireComponent(typeof(GemGrabManager))]
    public class GemGrabHud : MonoBehaviour
    {
        static readonly Color Blue = new Color(0.35f, 0.62f, 1f);
        static readonly Color Red = new Color(1f, 0.38f, 0.32f);
        static readonly Color Gem = new Color(0.82f, 0.6f, 1f);

        GemGrabManager m_Mgr;
        GUIStyle m_Big, m_Mid, m_Small;
        int m_StyleHeight;

        void Awake() => m_Mgr = GetComponent<GemGrabManager>();

        void Styles(float s)
        {
            if (m_Big != null && m_StyleHeight == Screen.height) return;
            m_StyleHeight = Screen.height;
            m_Big = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            m_Mid = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(20 * s) };
            m_Small = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(15 * s) };
        }

        static void Shadowed(Rect r, string text, GUIStyle st, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f * c.a);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            GUI.color = c;
            GUI.Label(r, text, st);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (!GemGrabManager.IsActive || m_Mgr == null) return;
            var match = MatchManager.Instance;
            var me = PlayerNetwork.Local;
            float s = MenuTheme.Scale; // mesma escala e zona de objetivos do GameUI
            Styles(s);
            float cx = Screen.width / 2f;

            int blue = m_Mgr.TeamTotal(0), red = m_Mgr.TeamTotal(1);
            float objectiveTop = GameUI.HudObjectiveTop(s);
            var r = new Rect(cx - 220 * s, objectiveTop, 440 * s, 30 * s);
            Shadowed(new Rect(r.x, r.y, r.width / 2 - 10 * s, r.height), $"◆ {blue}", m_Mid, Blue);
            Shadowed(new Rect(cx + 10 * s, r.y, r.width / 2 - 10 * s, r.height), $"◆ {red}", m_Mid, Red);
            Shadowed(new Rect(cx - 30 * s, r.y, 60 * s, r.height), $"/{m_Mgr.winGems}", m_Small, Color.white);

            if (me != null)
            {
                int mine = m_Mgr.CarriedBy(me.Id);
                Shadowed(new Rect(cx - 200 * s, Screen.height - 150 * s, 400 * s, 30 * s),
                    mine > 0 ? $"Levas {mine} gema{(mine == 1 ? "" : "s")} — não morras!" : "Apanha gemas no Pelourinho",
                    m_Mid, mine > 0 ? Gem : new Color(1f, 1f, 1f, 0.7f));
            }

            if (m_Mgr.CountdownTeam.Value >= 0 && match != null && match.Phase.Value == MatchPhase.Playing)
            {
                bool blueWins = m_Mgr.CountdownTeam.Value == 0;
                int secs = Mathf.CeilToInt(m_Mgr.CountdownRemaining);
                string who = blueWins ? "EQUIPA AZUL" : "EQUIPA VERMELHA";
                bool mineTeam = me != null && me.TeamId.Value == m_Mgr.CountdownTeam.Value;
                Shadowed(new Rect(cx - 300 * s, (objectiveTop + 38 * s), 600 * s, 46 * s), $"{who} VENCE EM {secs}", m_Big, blueWins ? Blue : Red);
                Shadowed(new Rect(cx - 300 * s, (objectiveTop + 82 * s), 600 * s, 26 * s),
                    mineTeam ? "Protege os portadores!" : "Mata os portadores para parar a contagem!", m_Small, Color.white);
            }

            // Número de gemas por cima de cada portador visível
            var cam = Camera.main;
            if (cam == null) return;
            for (int i = 0; i < m_Mgr.Carriers.Count; i++)
            {
                var c = m_Mgr.Carriers[i];
                if (c.Count <= 0) continue;
                var p = PlayerNetwork.Find(c.PlayerId);
                if (p == null || p.Health.IsDead || p == me) continue;
                if (p.Visuals != null && p.Visuals.IsConcealedForLocal) continue; // escondido num arbusto: não denunciar
                var wp = p.transform.position + Vector3.up * 2.35f;
                var sp = cam.WorldToScreenPoint(wp);
                if (sp.z <= 0f || sp.z > 45f) continue;
                if (Physics.Linecast(cam.transform.position, wp, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<PlayerNetwork>() != p) continue;
                var col = p.TeamId.Value == 0 ? Blue : Red;
                Shadowed(new Rect(sp.x - 50 * s, Screen.height - sp.y - 14 * s, 100 * s, 28 * s), $"◆ {c.Count}", m_Mid, col);
            }
        }
    }
}
