using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    public partial class GameUI
    {
        // Pode ser atribuído no Inspector ou colocado em Resources/UI/Menu/team_logo.png.
        [SerializeField] Texture2D m_IntroTeamLogo;
        [SerializeField] Texture2D m_IntroGameLogo;

        const float IntroLogoDuration = 3.2f;
        const float IntroWorldHold = 1.1f;
        const float IntroFlightDuration = 6.2f;
        const float IntroArrivalHold = 0.7f;
        const float IntroDissolveDuration = 1.2f;
        const float IntroWorldZoom = 1f;
        const float IntroCapeVerdeZoom = 56f;
        // Equiretangular: longitude -24°, latitude 16°; UV com origem em baixo à esquerda.
        static readonly Vector2 IntroCapeVerdeUV = new Vector2(156f / 360f, 106f / 180f);
        // Cobertura do mapa regional em UV mundiais: lon -31..-14, lat 9..24.
        static readonly Rect IntroRegionUV = new Rect(149f / 360f, 99f / 180f, 17f / 360f, 15f / 180f);

        static bool s_OpeningIntroComplete;
        float m_IntroStartedAt = -1f;
        bool m_IntroLoaded;
        bool m_IntroTeamLogoFromResources, m_IntroGameLogoFromResources;
        Texture2D m_IntroWorld;
        Texture2D m_IntroRegion;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOpeningIntro() => s_OpeningIntroComplete = false;

        /// <summary>Uma vez por execução: Unity → BRABUZ → Chiefs & Gems → mundo → Cabo Verde → menu.</summary>
        bool DrawOpeningIntro()
        {
            if (s_OpeningIntroComplete) return false;
            if (NM != null && NM.IsListening)
            {
                CompleteOpeningIntro();
                return false;
            }

            if (!m_IntroLoaded)
            {
                m_IntroLoaded = true;
                if (m_IntroTeamLogo == null) { m_IntroTeamLogo = Resources.Load<Texture2D>("UI/Menu/team_logo"); m_IntroTeamLogoFromResources = true; }
                if (m_IntroGameLogo == null) { m_IntroGameLogo = Resources.Load<Texture2D>("UI/Menu/game_logo"); m_IntroGameLogoFromResources = true; }
                m_IntroWorld = MenuTexture("bg_world");
                m_IntroRegion = MenuTexture("bg_cape_verde");
                if (m_IntroTeamLogo == null)
                    Debug.LogWarning("Intro: falta o logo da equipa. Coloca-o em Resources/UI/Menu/team_logo.png ou atribui-o no GameUI.");
                if (m_IntroGameLogo == null)
                    Debug.LogWarning("Intro: falta o logo do jogo. Coloca-o em Resources/UI/Menu/game_logo.jpg ou atribui-o no GameUI.");
            }

            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var oldColor = GUI.color;
            var oldEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                // O relógio só começa depois do splash nativo; no Editor, começa ao entrar em Play.
                if (!Application.isEditor && !SplashScreen.isFinished)
                {
                    IntroTexture(screen, Texture2D.whiteTexture, Color.black);
                    return true;
                }
                if (m_IntroStartedAt < 0f)
                {
                    IntroTexture(screen, Texture2D.whiteTexture, Color.white);
                    if (Event.current.type == EventType.Repaint) m_IntroStartedAt = Time.unscaledTime;
                    return true;
                }

                float elapsed = Time.unscaledTime - m_IntroStartedAt;
                float teamDuration = m_IntroTeamLogo != null ? IntroLogoDuration : 0f;
                float gameDuration = m_IntroGameLogo != null ? IntroLogoDuration : 0f;
                float logoDuration = teamDuration + gameDuration;
                float mapTime = elapsed - logoDuration;
                float dissolveAt = IntroWorldHold + IntroFlightDuration + IntroArrivalHold;
                float dissolve = Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((mapTime - dissolveAt) / IntroDissolveDuration));
                m_MScreenT = 0f; // os botões só animam a entrada depois da viagem

                if (elapsed >= logoDuration + dissolveAt + IntroDissolveDuration)
                {
                    CompleteOpeningIntro();
                    DrawMenuBackground();
                    return true;
                }

                bool showingLogo = mapTime < 0f;
                IntroTexture(screen, Texture2D.whiteTexture, showingLogo ? Color.white : Color.black);
                if (elapsed < teamDuration) DrawIntroLogo(screen, m_IntroTeamLogo, elapsed, false);
                else if (showingLogo) DrawIntroLogo(screen, m_IntroGameLogo, elapsed - teamDuration, true);
                else if (m_IntroWorld != null) DrawIntroWorld(screen, mapTime, dissolve);
                else DrawMenuBackground(); // recurso ausente nunca bloqueia a entrada no jogo

                if (elapsed > 0.5f)
                {
                    GUI.color = Color.white;
                    MText(new Rect(24 * K, Screen.height - 58 * K, 360 * K, 32 * K),
                        "SPACE / ENTER / ESC TO SKIP", m_MT12, showingLogo ? Color.gray : MMuted);
                    var skip = new Rect(Screen.width - 150 * K, Screen.height - 62 * K, 126 * K, 38 * K);
                    // Mantém o botão legível também sobre os cartões brancos dos logos.
                    if (showingLogo) IntroTexture(skip, Texture2D.whiteTexture, MDeep);
                    GUI.color = Color.white;
                    bool keySkip = Event.current.type == EventType.KeyDown &&
                        (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.Space ||
                         Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
                    if (GUI.Button(skip, "SKIP", m_MT14) | PadNav.Item(skip, "BACK") || keySkip)
                    {
                        CompleteOpeningIntro();
                        if (keySkip) Event.current.Use();
                    }
                }
                // Nunca deixa o mesmo clique/tecla passar para os botões do menu.
                return true;
            }
            finally
            {
                GUI.color = oldColor;
                GUI.enabled = oldEnabled;
            }
        }

        void CompleteOpeningIntro()
        {
            s_OpeningIntroComplete = true;
            m_MScreenT = 0f;
            m_MLastTime = Time.unscaledTime;
            ReleaseIntroTextures();
        }

        /// <summary>A intro só corre uma vez por execução: os mapas e logótipos dela (sem compressão) deixam de ser precisos.</summary>
        void ReleaseIntroTextures()
        {
            UnloadMenuTexture("bg_world");
            UnloadMenuTexture("bg_cape_verde");
            m_IntroWorld = m_IntroRegion = null;
            // Só os carregados por Resources: um logótipo atribuído no Inspector pertence à cena
            if (m_IntroTeamLogoFromResources && m_IntroTeamLogo != null) Resources.UnloadAsset(m_IntroTeamLogo);
            if (m_IntroGameLogoFromResources && m_IntroGameLogo != null) Resources.UnloadAsset(m_IntroGameLogo);
            if (m_IntroTeamLogoFromResources) m_IntroTeamLogo = null;
            if (m_IntroGameLogoFromResources) m_IntroGameLogo = null;
        }

        void DrawIntroLogo(Rect screen, Texture2D logo, float elapsed, bool gameLogo)
        {
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.65f)) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 2.45f) / 0.75f)));
            float scale = Mathf.Lerp(0.96f, 1f, BgTravelEase(elapsed / IntroLogoDuration));
            float width = screen.width * (gameLogo ? 0.64f : 0.7f) * scale;
            float height = screen.height * (gameLogo ? 0.76f : 0.48f) * scale;
            var bounds = new Rect(screen.center.x - width / 2f, screen.center.y - height / 2f, width, height);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(bounds, logo, ScaleMode.ScaleToFit, true);
        }

        void DrawIntroWorld(Rect screen, float mapTime, float dissolve)
        {
            float flight = Mathf.Clamp01((mapTime - IntroWorldHold) / IntroFlightDuration);
            IntroFlight(flight, out var focus, out var zoom, out var descent);
            // A vista inicial mostra todos os continentes, incluindo em ecrãs estreitos.
            var worldBounds = FitIntroWorld(screen, flight);
            // Mantém todas as ilhas no enquadramento final, em formato vertical e ultrawide.
            float aspect = worldBounds.width / Mathf.Max(1f, worldBounds.height);
            zoom = Mathf.Min(zoom, IntroMaxZoom(aspect));
            var view = BgView(worldBounds, m_IntroWorld, focus,
                zoom, Vector2.zero);

            if (dissolve > 0f) DrawMenuBackground();
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mapTime / 0.7f)) * (1f - dissolve);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTextureWithTexCoords(worldBounds, m_IntroWorld, view);
            // Troca de escala nas mesmas coordenadas: as ilhas pequenas continuam nítidas na aproximação.
            float regionBlend = m_IntroRegion != null && IntroRegionUV.Contains(view.min) && IntroRegionUV.Contains(view.max)
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 32f, zoom)) : 0f;
            var regionView = new Rect((view.x - IntroRegionUV.x) / IntroRegionUV.width,
                (view.y - IntroRegionUV.y) / IntroRegionUV.height,
                view.width / IntroRegionUV.width, view.height / IntroRegionUV.height);
            if (regionBlend > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, fade * regionBlend);
                GUI.DrawTextureWithTexCoords(worldBounds, m_IntroRegion, regionView);
            }
            // Desfoque radial leve só durante as descidas; desaparece nas pausas e na chegada.
            var detail = regionBlend >= 1f ? m_IntroRegion : m_IntroWorld;
            var detailView = regionBlend >= 1f ? regionView : view;
            if (descent > 0.01f)
            {
                for (int i = 1; i <= 3; i++)
                {
                    float expansion = i * 0.0035f * descent;
                    var blurView = new Rect(detailView.x + detailView.width * expansion,
                        detailView.y + detailView.height * expansion,
                        detailView.width * (1f - 2f * expansion), detailView.height * (1f - 2f * expansion));
                    GUI.color = new Color(1f, 1f, 1f, fade * descent * 0.08f);
                    GUI.DrawTextureWithTexCoords(worldBounds, detail, blurView);
                }
            }
            IntroTexture(screen, m_Vignette, new Color(0.01f, 0.03f, 0.08f, fade * 0.48f));

            var point = new Vector2(worldBounds.x + (IntroCapeVerdeUV.x - view.x) / view.width * worldBounds.width,
                worldBounds.y + (1f - (IntroCapeVerdeUV.y - view.y) / view.height) * worldBounds.height);
            float lockIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mapTime / IntroWorldHold));
            float radius = Mathf.Lerp(36f, 60f, BgTravelEase(flight)) * K;
            var ring = MenuRingTexture();
            float pulse = Mathf.Repeat(mapTime * 0.65f, 1f);
            IntroRing(point, radius, ring, fade * lockIn * 0.85f);
            IntroRing(point, radius * (1f + pulse * 0.65f), ring, fade * lockIn * (1f - pulse) * 0.4f);
            IntroTexture(new Rect(point.x - 3 * K, point.y - 3 * K, 6 * K, 6 * K),
                Texture2D.whiteTexture, new Color(MCyan.r, MCyan.g, MCyan.b, fade * lockIn));

            // As mesmas nuvens do voo entre ilhas ligam o mapa mundial ao arquipélago ilustrado.
            var clouds = MenuTexture("fx_clouds");
            if (clouds != null)
            {
                float veil = descent * 0.42f + Mathf.Sin(dissolve * Mathf.PI) * 0.65f;
                GUI.color = new Color(1f, 1f, 1f, veil);
                float cloudScale = 1.2f + descent * 0.7f;
                GUI.DrawTexture(new Rect(screen.center.x - screen.width * cloudScale / 2f,
                    screen.center.y - screen.height * cloudScale / 2f,
                    screen.width * cloudScale, screen.height * cloudScale), clouds, ScaleMode.ScaleAndCrop, true);
            }
        }

        // Três descidas e duas pausas de estabilização, como uma câmara aérea a mudar de altitude.
        static float IntroMaxZoom(float aspect) =>
            Mathf.Min(360f * Mathf.Min(1f, aspect / 2f) / 3.8f,
                180f * Mathf.Min(1f, 2f / aspect) / 3.2f);

        static void IntroFlight(float t, out Vector2 focus, out float zoom, out float descent)
        {
            float local;
            focus = IntroCapeVerdeUV;
            if (t < 0.32f)
            {
                local = Mathf.Clamp01(t / 0.32f);
                focus = Vector2.Lerp(new Vector2(0.5f, 0.5f), IntroCapeVerdeUV, BgTravelEase(local));
                zoom = BgTravelZoom(IntroWorldZoom, 5.5f, local);
            }
            else if (t < 0.39f) { zoom = 5.5f; descent = 0f; return; }
            else if (t < 0.68f)
            {
                local = (t - 0.39f) / 0.29f;
                zoom = BgTravelZoom(5.5f, 32f, local);
            }
            else if (t < 0.75f) { zoom = 32f; descent = 0f; return; }
            else
            {
                local = Mathf.Clamp01((t - 0.75f) / 0.25f);
                zoom = BgTravelZoom(32f, IntroCapeVerdeZoom, local);
            }
            descent = Mathf.Max(0f, Mathf.Sin(local * Mathf.PI));
        }

        Rect FitIntroWorld(Rect screen, float flight)
        {
            float aspect = m_IntroWorld.width / (float)m_IntroWorld.height;
            float width = Mathf.Min(screen.width, screen.height * aspect);
            float height = width / aspect;
            var fitted = new Rect(screen.center.x - width / 2f, screen.center.y - height / 2f, width, height);
            float t = BgTravelEase(flight);
            return new Rect(Mathf.Lerp(fitted.x, screen.x, t), Mathf.Lerp(fitted.y, screen.y, t),
                Mathf.Lerp(fitted.width, screen.width, t), Mathf.Lerp(fitted.height, screen.height, t));
        }

        void IntroRing(Vector2 point, float radius, Texture ring, float alpha) =>
            IntroTexture(new Rect(point.x - radius, point.y - radius, radius * 2f, radius * 2f),
                ring, new Color(MCyan.r, MCyan.g, MCyan.b, alpha));

        static void IntroTexture(Rect rect, Texture texture, Color color)
        {
            if (texture == null) return;
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
        }
    }
}
