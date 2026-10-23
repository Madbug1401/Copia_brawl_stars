using System;
using System.Collections;
using System.IO;
using System.Reflection;
using FPSLAN;
using UnityEditor;
using UnityEngine;

public static class IntroLogoCapture
{
    public static string Main()
    {
        if (!Application.isPlaying) throw new Exception("Entrar em Play primeiro.");
        var ui = UnityEngine.Object.FindAnyObjectByType<GameUI>();
        if (ui == null) throw new Exception("GameUI ausente.");
        EditorApplication.ExecuteMenuItem("Window/General/Game");
        ui.StartCoroutine(Capture(ui));
        return "Captura da abertura agendada em Temp/intro-logos.";
    }

    static IEnumerator Capture(GameUI ui)
    {
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var started = typeof(GameUI).GetField("m_IntroStartedAt", instanceFlags);
        var complete = typeof(GameUI).GetField("s_OpeningIntroComplete", BindingFlags.Static | BindingFlags.NonPublic);
        var dir = Path.GetFullPath("Temp/intro-logos");
        Directory.CreateDirectory(dir);
        // Garante um instante inicial positivo ao posicionar o relogio da intro.
        yield return new WaitForSecondsRealtime(2);
        float[] times = { 1.6f, 4.8f, 7.8f, 16.5f };
        string[] names = { "01-brabuz", "02-chiefs-and-gems", "03-world", "04-menu" };
        for (int i = 0; i < times.Length; i++)
        {
            for (int frame = 0; frame < 3; frame++)
            {
                complete.SetValue(null, false);
                started.SetValue(ui, Time.unscaledTime - times[i]);
                yield return new WaitForEndOfFrame();
            }
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, names[i] + ".png"), shot.EncodeToPNG());
            UnityEngine.Object.Destroy(shot);
        }
        complete.SetValue(null, true);
        File.WriteAllText(Path.Combine(dir, "result.txt"), "PASS: capturados BRABUZ, Chiefs & Gems, mundo e menu pela abertura real do GameUI.");
    }
}
