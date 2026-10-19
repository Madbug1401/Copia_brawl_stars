using System;
using System.Reflection;
using FPSLAN;
using UnityEngine;

/// <summary>Executar com unity command run_script --file Tools/Validation/IntroValidation.cs --entry IntroValidation.Main.</summary>
public static class IntroValidation
{
    public static string Main()
    {
        var world = Resources.Load<Texture2D>("UI/Menu/bg_world");
        var regional = Resources.Load<Texture2D>("UI/Menu/bg_cape_verde");
        var logo = Resources.Load<Texture2D>("UI/Menu/team_logo");
        var gameLogo = Resources.Load<Texture2D>("UI/Menu/game_logo");
        Require(world != null && regional != null && logo != null && gameLogo != null, "Faltam imagens da intro.");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var flight = typeof(GameUI).GetMethod("IntroFlight", flags);
        var maxZoom = typeof(GameUI).GetMethod("IntroMaxZoom", flags);
        var bgView = typeof(GameUI).GetMethod("BgView", flags);
        var destination = new Vector2(156f / 360f, 106f / 180f);
        float previous = 0f;
        for (int i = 0; i <= 1000; i++)
        {
            float t = i / 1000f;
            var sample = new object[] { t, Vector2.zero, 0f, 0f };
            flight.Invoke(null, sample);
            var focus = (Vector2)sample[1];
            float zoom = (float)sample[2], descent = (float)sample[3];
            Require(!float.IsNaN(zoom) && zoom >= previous - 0.0001f, "Zoom recuou durante a descida.");
            Require(descent >= -0.0001f && descent <= 1.0001f, "Desfoque fora do intervalo.");
            if (t >= 0.32f) Require(Vector2.Distance(focus, destination) < 0.00001f, "Foco saiu de Cabo Verde.");
            previous = zoom;
        }
        foreach (float t in new[] { 0.35f, 0.70f, 1f })
        {
            var sample = new object[] { t, Vector2.zero, 0f, 0f };
            flight.Invoke(null, sample);
            Require(Mathf.Abs((float)sample[3]) < 0.00001f, "Desfoque não terminou na pausa/chegada.");
        }
        foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 21f / 9f, 9f / 16f })
        {
            float zoom = Mathf.Min(56f, (float)maxZoom.Invoke(null, new object[] { aspect }));
            var view = (Rect)bgView.Invoke(null, new object[] {
                new Rect(0, 0, 810 * aspect, 810), world, destination, zoom, Vector2.zero });
            // Limites do arquipélago com margem: lon -25.5..-22.5, lat 14.75..17.25.
            Require(view.Contains(new Vector2(154.5f / 360f, 104.75f / 180f)) &&
                view.Contains(new Vector2(157.5f / 360f, 107.25f / 180f)), "O enquadramento cortou ilhas.");
        }
        return "PASS: logos BRABUZ e Chiefs & Gems carregados; 1001 amostras de voo; foco, pausas e chegada; todas as ilhas em 16:9, 4:3, 21:9 e 9:16.";
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
