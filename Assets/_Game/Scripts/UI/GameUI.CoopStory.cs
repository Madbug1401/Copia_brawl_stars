using UnityEngine;

namespace FPSLAN
{
    public partial class GameUI
    {
        void DrawCoopStory(PlayerNetwork me, MatchManager match, Rect top, float s)
        {
            float w = Mathf.Min(790 * s, Screen.width - 32 * s);
            float x = (Screen.width - w) / 2f;
            var objective = new Rect(x, top.yMax + 36 * s, w, 68 * s);
            InkPanel(objective, new Color(0.03f, 0.07f, 0.14f, 0.88f), 2 * s);
            Outlined(new Rect(x, objective.y + 4 * s, w, 23 * s), match.CoopStageTitle + "  ·  PELOURINHO", m_TinyCenter, Gold, 1f);
            var wrap = new GUIStyle(m_Center) { wordWrap = true, fontSize = Mathf.RoundToInt(16 * s), alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(x + 12 * s, objective.y + 26 * s, w - 24 * s, 38 * s), match.CoopObjective, wrap);

            if (match.CoopStage.Value == CoopMissionStage.Puzzle)
            {
                var bar = new Rect(x + w * 0.2f, objective.yMax + 8 * s, w * 0.6f, 22 * s);
                InkPanel(bar, PanelDark, 2 * s);
                if (match.CoopPuzzleProgress.Value > 0f)
                    Panel(new Rect(bar.x, bar.y, bar.width * match.CoopPuzzleProgress.Value, bar.height), CoopGreen);
                int active = ((match.CoopPlates.Value & 1) != 0 ? 1 : 0) + ((match.CoopPlates.Value & 2) != 0 ? 1 : 0);
                Outlined(bar, $"PLACAS {active}/2  ·  {Mathf.RoundToInt(match.CoopPuzzleProgress.Value * 100)}%", m_TinyCenter, Color.white, 1f);
            }

            bool storyVisible = match.NetworkManager != null && match.NetworkManager.ServerTime.Time < match.CoopStoryUntil.Value;
            if (storyVisible)
            {
                var caption = new Rect(x, Screen.height * 0.73f, w, 100 * s);
                InkPanel(caption, new Color(0.025f, 0.035f, 0.07f, 0.94f), 2 * s);
                GUI.Label(new Rect(x + 18 * s, caption.y + 8 * s, w - 36 * s, caption.height - 16 * s), match.CoopStoryText, wrap);
            }

            if (me.Health.IsDead || match.CoopStage.Value < CoopMissionStage.Puzzle) return;
            var cam = me.Visuals != null ? me.Visuals.PlayerCamera : null;
            if (cam == null) return;
            if (match.CoopStage.Value == CoopMissionStage.Puzzle)
            {
                DrawCoopObjectiveMarker(cam, me, match.coopPlateA, "PLACA A", (match.CoopPlates.Value & 1) != 0 ? CoopGreen : Color.cyan, s);
                DrawCoopObjectiveMarker(cam, me, match.coopPlateB, "PLACA B", (match.CoopPlates.Value & 2) != 0 ? CoopGreen : Color.cyan, s);
            }
            else DrawCoopObjectiveMarker(cam, me, match.coopGemPosition, "GEM DOURADA", Gold, s);
        }

        void DrawCoopObjectiveMarker(Camera cam, PlayerNetwork me, Vector3 point, string title, Color tint, float s)
        {
            var sp = cam.WorldToScreenPoint(point + Vector3.up * 0.7f);
            float x = Mathf.Clamp(sp.z > 0 ? sp.x : Screen.width - sp.x, 110 * s, Screen.width - 110 * s);
            float y = Mathf.Clamp(sp.z > 0 ? Screen.height - sp.y : Screen.height - 190 * s, 220 * s, Screen.height - 190 * s);
            int distance = Mathf.RoundToInt(Vector3.Distance(me.transform.position, point));
            var box = new Rect(x - 102 * s, y - 20 * s, 204 * s, 40 * s);
            InkPanel(box, new Color(0.02f, 0.06f, 0.12f, 0.85f), 2 * s);
            Outlined(box, title + "  ·  " + distance + " m", m_TinyCenter, tint, 1f);
        }

        void DrawCoopStoryResults(MatchManager match, float s)
        {
            bool win = match.CoopVictory.Value;
            float w = Mathf.Min(860 * s, Screen.width - 32 * s);
            var panel = new Rect((Screen.width - w) / 2f, Screen.height * 0.21f, w, 330 * s);
            InkPanel(panel, PanelDark, 3 * s);
            Outlined(new Rect(panel.x, panel.y + 18 * s, w, 48 * s), win ? "GEM RECUPERADA!" : "NINGUÉM FICA PARA TRÁS", m_BigCenter, win ? Gold : new Color(1f, 0.5f, 0.4f), 2f);
            Outlined(new Rect(panel.x, panel.y + 68 * s, w, 30 * s), MatchManager.CoopMissionTitle, m_Center, CoopGreen, 1f);
            var wrap = new GUIStyle(m_Center) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(panel.x + 28 * s, panel.y + 108 * s, w - 56 * s, 126 * s), match.CoopStoryText, wrap);
            Outlined(new Rect(panel.x, panel.y + 242 * s, w, 30 * s), win ? "Two players. One mission. Better Together." : "Protege o parceiro e mantém a interação para o reanimar.", m_Center, Color.white, 1f);
            Outlined(new Rect(panel.x, panel.y + 286 * s, w, 26 * s), "Nova tentativa em " + Mathf.CeilToInt(match.TimeRemaining) + "s  ·  Esc para sair", m_TinyCenter, Color.white, 1f);
        }
    }
}
