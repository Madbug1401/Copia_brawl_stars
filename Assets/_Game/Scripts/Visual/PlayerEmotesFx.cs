using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Visuais dos emotes de marca de um jogador (todas as máquinas):
    /// - UPRA: tigela UPRA no chão à frente do jogador, saco a despejar ração (nas mãos em 1.ª pessoa ou ao peito para
    ///   os outros), grãos a cair e o pet a comer. Guiado pelo estado sincronizado PetFeeding (sem rede extra).
    /// - SITA: lata de spray na cor escolhida e jato de tinta até à parede (o spray em si fica em <see cref="SprayDecals"/>).
    /// </summary>
    public class PlayerEmotesFx
    {
        const float SprayDuration = 0.8f;

        readonly PlayerNetwork m_Player;
        readonly PlayerVisuals m_Visuals;
        readonly int m_ViewLayer;

        // UPRA
        bool m_Feeding, m_Ate;
        Transform m_Bowl, m_BowlFood, m_Sack;
        bool m_SackFP;
        Vector3 m_BowlPos, m_Fwd;
        float m_NextGrain;
        struct Grain { public Transform t; public Vector3 v; }
        readonly List<Grain> m_Grains = new List<Grain>();

        // SITA
        float m_SprayAt = -10f;
        Color m_SprayColor;
        Vector3 m_SprayPoint;
        Transform m_Can;
        bool m_CanFP;
        float m_NextMist;

        // Bónus UPRA ativo: aura com a cor do bónus
        float m_NextAura;
        bool m_WasBoosted;

        public PlayerEmotesFx(PlayerNetwork player, PlayerVisuals visuals, int viewLayer)
        {
            m_Player = player;
            m_Visuals = visuals;
            m_ViewLayer = viewLayer;
        }

        /// <summary>Em 1.ª pessoa a arma sai de cena durante os emotes (as mãos estão ocupadas com o saco/lata).</summary>
        public bool HidesWeapon => m_Feeding || (Time.time - m_SprayAt < SprayDuration);

        /// <summary>0..1: quanto a câmara deve inclinar para baixo para ver o pet a comer.</summary>
        public float FeedCameraWeight
        {
            get
            {
                float t = m_Player.FeedAnimTime;
                if (t < 0f) return 0f;
                float d = PetFoodRules.FeedAnimDuration;
                return Mathf.SmoothStep(0f, 1f, Mathf.Min(t / 0.35f, (d - t) / 0.35f));
            }
        }

        bool Local => m_Player.IsLocalHuman;
        Transform CameraT => m_Visuals.PlayerCamera != null ? m_Visuals.PlayerCamera.transform : null;

        public void Tick(float dt, bool hidden, bool dead)
        {
            float t = dead ? -1f : m_Player.FeedAnimTime;
            if (t >= 0f && !m_Feeding) BeginFeed();
            if (m_Feeding)
            {
                if (t < 0f) EndFeed(!dead);
                else TickFeed(t, hidden);
            }
            TickGrains(dt);
            TickSpray(hidden, dead);
            TickBoostAura(hidden, dead);
        }

        // ================================================================== UPRA

        void BeginFeed()
        {
            m_Feeding = true;
            m_Ate = false;
            var pt = m_Player.transform;
            m_Fwd = Vector3.ProjectOnPlane(pt.forward, Vector3.up);
            if (m_Fwd.sqrMagnitude < 0.01f) m_Fwd = Vector3.forward;
            m_Fwd.Normalize();

            // Tigela ~1,2 m à frente (mais perto se houver uma parede)
            float dist = 1.2f;
            var chest = pt.position + Vector3.up * 0.6f;
            if (CombatMath.Raycast(chest, m_Fwd, dist + 0.4f, m_Player, out var wall)) dist = Mathf.Clamp(wall.distance - 0.4f, 0.55f, dist);
            m_BowlPos = CombatMath.GroundBelow(pt.position + m_Fwd * dist + Vector3.up * 0.6f, m_Player);

            m_Bowl = Branding.BuildBowl(null, 1.15f, out m_BowlFood);
            m_Bowl.SetPositionAndRotation(m_BowlPos, Quaternion.LookRotation(m_Fwd));
            m_Bowl.localScale = Vector3.zero;
            m_BowlFood.localScale = new Vector3(0.3f, 0.001f, 0.3f);

            if (Local && CameraT != null)
            {
                m_SackFP = true;
                m_Sack = Branding.BuildSack(CameraT, 0.45f, ShadowCastingMode.Off);
                if (m_ViewLayer >= 0) Branding.SetLayer(m_Sack, m_ViewLayer);
                m_Sack.localPosition = new Vector3(0.22f, -0.75f, 0.62f);
            }
            else
            {
                m_SackFP = false;
                m_Sack = Branding.BuildSack(null, 0.75f, ShadowCastingMode.Off);
            }

            Sfx.PlayAt(Sound.Crate, m_BowlPos + Vector3.up * 0.2f, 0.55f);
            Fx.Puff(m_BowlPos + Vector3.up * 0.15f, Vector3.up * 0.3f, Art.WithAlpha(Color.white, 0.5f), 0.5f, 0.35f);
            m_Visuals.Pet?.BeginFeeding(m_BowlPos);
        }

        void TickFeed(float t, bool hidden)
        {
            if (m_Bowl != null)
            {
                if (m_Bowl.gameObject.activeSelf == hidden) m_Bowl.gameObject.SetActive(!hidden);
                float pop = Mathf.Clamp01(t / 0.15f);
                m_Bowl.localScale = Vector3.one * 1.15f * (pop < 1f ? Mathf.Sin(pop * Mathf.PI * 0.5f) * 1.1f : 1f);
                // Enche enquanto se despeja (0,2–0,85 s) e esvazia enquanto o pet come (0,9–1,8 s)
                float fill = Mathf.Clamp01((t - 0.2f) / 0.65f) * (1f - 0.8f * Mathf.Clamp01((t - 0.9f) / 0.9f));
                m_BowlFood.localScale = new Vector3(0.3f + 0.05f * fill, Mathf.Max(0.001f, 0.09f * fill), 0.3f + 0.05f * fill);
            }

            // Saco: sobe, inclina para despejar, sai
            float raise = Mathf.SmoothStep(0f, 1f, t / 0.25f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.85f) / 0.3f));
            float pour = Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f));
            if (m_Sack != null)
            {
                bool show = !hidden && raise > 0.01f;
                if (m_Sack.gameObject.activeSelf != show) m_Sack.gameObject.SetActive(show);
                if (m_SackFP)
                {
                    m_Sack.localPosition = new Vector3(Mathf.Lerp(0.3f, 0.12f, pour), Mathf.Lerp(-0.85f, -0.36f, raise), 0.62f);
                    m_Sack.localRotation = Quaternion.Euler(18f * pour, -15f, 80f * pour + Mathf.Sin(t * 30f) * 3f * pour);
                }
                else
                {
                    var pt = m_Player.transform;
                    var right = Vector3.Cross(Vector3.up, m_Fwd);
                    m_Sack.position = pt.position + Vector3.up * Mathf.Lerp(0.6f, 1.05f, raise) + m_Fwd * 0.45f + right * 0.12f;
                    m_Sack.rotation = Quaternion.LookRotation(m_Fwd) * Quaternion.Euler(95f * pour, 0f, 0f);
                }
            }

            // Grãos a cair na tigela
            if (!hidden && pour > 0.5f && Time.time >= m_NextGrain)
            {
                m_NextGrain = Time.time + 0.035f;
                for (int i = 0; i < 2; i++) SpawnGrain();
            }

            if (!m_Ate && t >= 0.9f)
            {
                m_Ate = true;
                if (!hidden) Sfx.PlayAt(Sound.PetHappy, m_BowlPos + Vector3.up * 0.3f, 0.45f);
            }
        }

        void SpawnGrain()
        {
            if (m_Grains.Count > 40) return;
            var g = Art.Part(PrimitiveType.Sphere, null, m_BowlPos + Vector3.up * 0.75f - m_Fwd * 0.15f + Random.insideUnitSphere * 0.05f,
                Vector3.one * Random.Range(0.035f, 0.05f), Color.Lerp(Branding.GrainColor, Color.black, Random.value * 0.3f), false, default, ShadowCastingMode.Off);
            m_Grains.Add(new Grain { t = g, v = m_Fwd * Random.Range(0.1f, 0.35f) + Random.insideUnitSphere * 0.25f });
        }

        void TickGrains(float dt)
        {
            for (int i = m_Grains.Count - 1; i >= 0; i--)
            {
                var g = m_Grains[i];
                if (g.t == null) { m_Grains.RemoveAt(i); continue; }
                g.v += Vector3.down * 9.8f * dt;
                var p = g.t.position + g.v * dt;
                if (p.y <= m_BowlPos.y + 0.1f)
                {
                    Object.Destroy(g.t.gameObject);
                    m_Grains.RemoveAt(i);
                    continue;
                }
                g.t.position = p;
                m_Grains[i] = g;
            }
        }

        void EndFeed(bool celebrate)
        {
            m_Feeding = false;
            if (m_Bowl != null)
            {
                if (celebrate && m_Bowl.gameObject.activeSelf)
                {
                    Fx.Puff(m_BowlPos + Vector3.up * 0.2f, Vector3.up * 0.4f, Art.WithAlpha(Color.white, 0.55f), 0.6f, 0.4f);
                    Fx.Ring(m_BowlPos + Vector3.up * 0.05f, Branding.UpraBlue, 1.2f, 0.4f);
                }
                Object.Destroy(m_Bowl.gameObject);
            }
            if (m_Sack != null) Object.Destroy(m_Sack.gameObject);
            m_Bowl = m_BowlFood = m_Sack = null;
            m_Visuals.Pet?.EndFeeding(celebrate);
        }

        void TickBoostAura(bool hidden, bool dead)
        {
            bool on = !dead && m_Player.UpraActive;
            var col = PetFoodRules.BoostColor(m_Player.UpraSpecies);
            var feet = m_Player.transform.position;
            if (on && !m_WasBoosted && !hidden)
            {
                // Arranque do bónus: anel e brilho
                Fx.Ring(feet + Vector3.up * 0.05f, col, 2.2f, 0.5f);
                for (int i = 0; i < 10; i++) Fx.Glow(feet + Vector3.up * 1f + Random.insideUnitSphere * 0.6f, Vector3.up * 1.5f, col, 0.12f, 0.7f);
                if (Local) Sfx.Play2D(Sound.SuperReady, 0.5f);
                else Sfx.PlayAt(Sound.SuperReady, feet + Vector3.up, 0.6f);
            }
            m_WasBoosted = on;
            if (!on || hidden || Time.time < m_NextAura) return;
            m_NextAura = Time.time + (Local ? 0.2f : 0.09f);
            // No próprio jogador só junto aos pés (não tapa a vista); nos outros à volta do corpo
            var p = Local ? feet + Vector3.up * 0.1f + Random.insideUnitSphere * 0.5f : feet + Vector3.up * (0.4f + Random.value * 1.3f) + Random.insideUnitSphere * 0.45f;
            Fx.Glow(p, Vector3.up * 0.9f, col, Local ? 0.07f : 0.1f, 0.6f);
        }

        // ================================================================== SITA

        public void OnSpray(Vector3 point, int colorIndex)
        {
            m_SprayAt = Time.time;
            m_SprayPoint = point;
            m_SprayColor = Branding.SprayColorOf(colorIndex);
            m_NextMist = 0f;
            if (m_Can != null) Object.Destroy(m_Can.gameObject);
            if (Local && CameraT != null)
            {
                m_CanFP = true;
                m_Can = Branding.BuildSprayCan(CameraT, m_SprayColor);
                if (m_ViewLayer >= 0) Branding.SetLayer(m_Can, m_ViewLayer);
                Sfx.Play2D(Sound.Spray, 0.7f);
            }
            else
            {
                m_CanFP = false;
                m_Can = Branding.BuildSprayCan(null, m_SprayColor);
                m_Can.localScale = Vector3.one * 1.4f;
                Sfx.PlayAt(Sound.Spray, point, 0.9f);
            }
        }

        void TickSpray(bool hidden, bool dead)
        {
            if (m_Can == null) return;
            float t = Time.time - m_SprayAt;
            if (t >= SprayDuration || dead)
            {
                Object.Destroy(m_Can.gameObject);
                m_Can = null;
                return;
            }
            if (m_Can.gameObject.activeSelf == hidden) m_Can.gameObject.SetActive(!hidden);
            float raise = Mathf.SmoothStep(0f, 1f, t / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.6f) / 0.2f));
            float shake = t < 0.12f ? Mathf.Sin(t * 90f) * 0.02f : 0f;
            Vector3 nozzle;
            if (m_CanFP)
            {
                var cam = CameraT;
                m_Can.localPosition = new Vector3(0.2f, Mathf.Lerp(-0.6f, -0.24f, raise) + shake, 0.45f);
                m_Can.localRotation = Quaternion.Euler(-8f, -12f, 0f);
                nozzle = cam != null ? cam.position + cam.forward * 0.55f + cam.right * 0.17f - cam.up * 0.05f : m_Can.position;
            }
            else
            {
                var pt = m_Player.transform;
                var dir = Vector3.ProjectOnPlane(m_SprayPoint - pt.position, Vector3.up);
                if (dir.sqrMagnitude < 0.01f) dir = pt.forward;
                dir.Normalize();
                m_Can.position = pt.position + Vector3.up * (1.05f + shake) + dir * 0.42f + Vector3.Cross(Vector3.up, dir) * 0.18f;
                m_Can.rotation = Quaternion.LookRotation(dir);
                nozzle = m_Can.position + Vector3.up * 0.35f + dir * 0.05f;
            }

            // Jato de tinta até à parede
            if (!hidden && t > 0.06f && t < 0.5f && Time.time >= m_NextMist)
            {
                m_NextMist = Time.time + 0.02f;
                var to = m_SprayPoint - nozzle;
                for (int i = 0; i < 2; i++)
                {
                    float k = Random.value;
                    var p = nozzle + to * k + Random.insideUnitSphere * (0.04f + 0.18f * k);
                    Fx.Puff(p, to.normalized * 2.5f, Art.WithAlpha(m_SprayColor, 0.75f), 0.12f + 0.3f * k, 0.3f);
                }
            }
        }

        public void Dispose()
        {
            if (m_Bowl != null) Object.Destroy(m_Bowl.gameObject);
            if (m_Sack != null) Object.Destroy(m_Sack.gameObject);
            if (m_Can != null) Object.Destroy(m_Can.gameObject);
            foreach (var g in m_Grains) if (g.t != null) Object.Destroy(g.t.gameObject);
            m_Grains.Clear();
            m_Feeding = false;
        }
    }
}
