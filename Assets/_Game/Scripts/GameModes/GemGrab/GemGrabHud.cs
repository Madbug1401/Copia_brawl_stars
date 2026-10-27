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

        void Awake()
        {
            m_Mgr = GetComponent<GemGrabManager>();
            useGUILayout = false;   // só usa GUI.* (sem GUILayout): o Unity deixa de enviar o evento Layout
        }

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
            if (!GameUI.IsGameplayOverlayVisible || !GemGrabManager.IsActive || m_Mgr == null) return;
            var match = MatchManager.Instance;
            var me = PlayerNetwork.Local;
            if (me == null || match == null || match.Phase.Value != MatchPhase.Playing) return;
            float s = MenuTheme.Scale, cx = Screen.width * 0.5f;
            Styles(s);
            m_Mid.font = GameTypography.Numbers;
            m_Small.font = GameTypography.Body;
            bool blue = me.TeamId.Value == 0;
            int own = m_Mgr.TeamTotal(blue ? 0 : 1), enemy = m_Mgr.TeamTotal(blue ? 1 : 0);
            var area = new Rect(cx - 205 * s, GameUI.HudObjectiveTop(s), 410 * s, 66 * s);
            MenuTheme.Panel(area);
            Shadowed(new Rect(area.x + 8*s, area.y + 5*s, 190*s, 24*s), "YOUR TEAM  " + own + "/" + m_Mgr.winGems, m_Small, TeamPresentation.Ally);
            Shadowed(new Rect(cx + 8*s, area.y + 5*s, 190*s, 24*s), "ENEMY  " + enemy + "/" + m_Mgr.winGems, m_Small, TeamPresentation.Enemy);
            string status = "CARRYING " + m_Mgr.CarriedBy(me.Id) + " GEMS";
            if (m_Mgr.CountdownTeam.Value >= 0)
                status = (m_Mgr.CountdownTeam.Value == me.TeamId.Value ? "DEFEND · WIN IN " : "STOP ENEMY · ") + Mathf.CeilToInt(m_Mgr.CountdownRemaining) + "s";
            Shadowed(new Rect(area.x + 8*s, area.y + 34*s, area.width - 16*s, 24*s), status, m_Small, Color.white);
        }
    }
}
