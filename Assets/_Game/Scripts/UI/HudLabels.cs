using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>World labels share a placement budget and stay outside fixed HUD regions.</summary>
    public static class HudLabels
    {
        static int s_Frame = -1;
        static readonly Dictionary<string, Rect> s_Placed = new Dictionary<string, Rect>();
        static readonly List<Rect> s_Reserved = new List<Rect>();

        public static bool Place(string key, Rect desired, out Rect result)
        {
            float s = MenuTheme.Scale;
            if (s_Frame != Time.frameCount)
            {
                s_Frame = Time.frameCount;
                s_Placed.Clear(); s_Reserved.Clear();
                s_Reserved.Add(new Rect(Screen.width / 2f - 230*s, 0, 460*s, 205*s));
                s_Reserved.Add(new Rect(0, Screen.height - 230*s, 530*s, 230*s));
                s_Reserved.Add(new Rect(Screen.width - 240*s, Screen.height - 210*s, 240*s, 210*s));
                if (MatchManager.IsCoopActive)
                {
                    s_Reserved.Add(new Rect(0, 50*s, 340*s, 155*s));
                    s_Reserved.Add(new Rect(Screen.width / 2f - 210*s, Screen.height - 130*s, 420*s, 100*s));
                }
            }
            if (s_Placed.TryGetValue(key, out result)) return result.width > 0;
            desired.x = Mathf.Clamp(desired.x, 6*s, Mathf.Max(6*s, Screen.width - desired.width - 6*s));
            for (int attempt = 0; attempt < 4; attempt++)
            {
                bool clear = desired.y > 6*s && desired.yMax < Screen.height - 6*s;
                foreach (var r in s_Reserved) if (r.Overlaps(desired)) { clear = false; break; }
                foreach (var r in s_Placed.Values) if (r.width > 0 && r.Overlaps(desired)) { clear = false; break; }
                if (clear) { s_Placed[key] = result = desired; return true; }
                desired.y -= desired.height + 5*s;
            }
            s_Placed[key] = result = new Rect();
            return false;
        }
    }
}
