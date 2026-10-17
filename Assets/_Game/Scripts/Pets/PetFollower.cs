using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>
    /// Pet em jogo (só visual, em cada máquina): segue o dono ao lado e um pouco atrás, contorna paredes,
    /// teleporta-se se ficar preso ou muito longe, e reage (salta quando o dono leva dano, festeja eliminações).
    /// Não tem colisores, por isso nunca bloqueia tiros nem movimento. Esconde-se quando o dono morre ou quando o
    /// dono está escondido num arbusto para quem está a ver (não denuncia ninguém).
    /// Para o dono local trata também das passivas de informação (radar do Cão, alerta do Galo).
    /// </summary>
    public class PetFollower : MonoBehaviour
    {
        const float TeleportDistance = 14f;
        const float StuckSeconds = 1.2f;

        /// <summary>Inimigos detetados pelo radar do pet do jogador local (para o HUD).</summary>
        public static readonly List<PlayerNetwork> LocalRadarTargets = new List<PlayerNetwork>();
        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LocalRadarTargets.Clear();

        PlayerNetwork m_Owner;
        PetDefinition m_Def;
        PetRig m_Rig;
        Vector3 m_Velocity;
        float m_Yaw;
        float m_StuckTime;
        float m_LastDistance;
        float m_NextProbe;
        Vector3 m_Detour;
        float m_AnimUntil;
        int m_LastHealth;
        int m_LastKills;
        bool m_WasHidden = true;
        float m_NextRadar;
        float m_NextAlert;
        readonly HashSet<ulong> m_AlertedNear = new HashSet<ulong>();

        public string Key { get; private set; } = "";
        public PetDefinition Definition => m_Def;

        public static PetFollower Create(PlayerNetwork owner, PetDefinition def, int level, SkinDefinition skin)
        {
            var go = new GameObject("PetFollower_" + owner.Id);
            var f = go.AddComponent<PetFollower>();
            f.Init(owner, def, level, skin);
            return f;
        }

        void Init(PlayerNetwork owner, PetDefinition def, int level, SkinDefinition skin)
        {
            m_Owner = owner;
            m_Def = def;
            int stage = PetDefinition.StageForLevel(level);
            Key = def.PetId + ":" + stage + ":" + (skin != null ? skin.skinId : "");
            m_Rig = PetRig.Build(def, stage, skin, transform, ShadowCastingMode.On);
            var side = owner.transform.right * def.sideOffset - owner.transform.forward * def.followDistance * 0.6f;
            transform.position = Ground(owner.transform.position + side);
            m_Yaw = owner.transform.eulerAngles.y;
            m_LastHealth = owner.Health.Health.Value;
            m_LastKills = owner.Kills.Value;
        }

        public void Dispose()
        {
            m_Rig?.Destroy();
            if (m_Owner != null && m_Owner.IsLocalHuman) LocalRadarTargets.Clear();
            Destroy(gameObject);
        }

        Vector3 Ground(Vector3 p)
        {
            // Raio para baixo que ignora jogadores (os pets não têm colisores)
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.5f, Vector3.down, s_Hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = p;
            for (int i = 0; i < n; i++)
            {
                var h = s_Hits[i];
                if (h.collider.GetComponentInParent<PlayerNetwork>() != null) continue;
                if (h.distance < best) { best = h.distance; point = h.point; }
            }
            return best < float.MaxValue ? point : new Vector3(p.x, m_Owner != null ? m_Owner.transform.position.y : p.y, p.z);
        }

        bool Blocked(Vector3 from, Vector3 dir, float dist)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.3f, dir, s_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (s_Hits[i].collider.GetComponentInParent<PlayerNetwork>() == null) return true;
            return false;
        }

        void LateUpdate()
        {
            if (m_Owner == null || !m_Owner.IsSpawned || m_Def == null || m_Rig == null) return;
            float dt = Time.deltaTime;
            var ownerPos = m_Owner.transform.position;

            // ---------------- visibilidade (morte, arbustos, partida terminada não esconde)
            var vis = m_Owner.Visuals;
            bool hidden = m_Owner.Health.IsDead || (vis != null && vis.IsConcealedForLocal);
            if (hidden != m_WasHidden)
            {
                if (!hidden)
                {
                    // Reaparece ao lado do dono
                    transform.position = Ground(ownerPos + m_Owner.transform.right * m_Def.sideOffset);
                    m_Velocity = Vector3.zero;
                    Fx.Poof(transform.position + Vector3.up * 0.3f, m_Def.mainColor);
                }
                else if (!m_WasHidden && !m_Owner.Health.IsDead) Fx.Puff(transform.position + Vector3.up * 0.3f, Vector3.up, new Color(0.3f, 0.7f, 0.3f, 0.5f), 0.5f, 0.4f);
                m_WasHidden = hidden;
                m_Rig.SetVisible(!hidden);
            }
            if (hidden)
            {
                m_LastHealth = m_Owner.Health.Health.Value;
                return;
            }

            // ---------------- reações
            int hp = m_Owner.Health.Health.Value;
            if (hp < m_LastHealth && Time.time >= m_AnimUntil)
            {
                m_Rig.SetAnim(PetAnim.React);
                m_AnimUntil = Time.time + 0.5f;
            }
            m_LastHealth = hp;
            int kills = m_Owner.Kills.Value;
            if (kills > m_LastKills)
            {
                m_Rig.SetAnim(PetAnim.Celebrate);
                m_AnimUntil = Time.time + 1.2f;
                if (m_Owner.IsLocalHuman) Sfx.Play2D(Sound.PetHappy, 0.5f);
                else Sfx.PlayAt(Sound.PetHappy, transform.position, 0.6f);
            }
            m_LastKills = kills;

            // ---------------- seguir: ao lado e atrás do dono (fora da linha de tiro)
            var fwd = m_Owner.transform.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var target = ownerPos + right * m_Def.sideOffset - fwd * m_Def.followDistance * 0.6f;
            var pos = transform.position;
            var to = target - pos;
            to.y = 0f;
            float dist = to.magnitude;
            float ownerDist = Vector3.Distance(new Vector3(ownerPos.x, 0f, ownerPos.z), new Vector3(pos.x, 0f, pos.z));

            // Muito longe (salto, teleporte, jump pad) ou preso → teletransporte com "puf"
            if (ownerDist > 3f && dist > 0.5f)
            {
                if (dist >= m_LastDistance - 0.02f) m_StuckTime += dt;
                else m_StuckTime = Mathf.Max(0f, m_StuckTime - dt);
            }
            else m_StuckTime = 0f;
            m_LastDistance = dist;
            if (ownerDist > TeleportDistance || m_StuckTime > StuckSeconds || Mathf.Abs(ownerPos.y - pos.y) > 4f)
            {
                Fx.Poof(pos + Vector3.up * 0.3f, m_Def.mainColor);
                transform.position = Ground(target);
                Fx.Poof(transform.position + Vector3.up * 0.3f, m_Def.mainColor);
                m_StuckTime = 0f;
                m_Velocity = Vector3.zero;
                return;
            }

            float speed = 0f;
            if (dist > 0.35f)
            {
                var dir = to / dist;
                // Contorna paredes: testa a direção direta e, se bater, ±50° (verificado poucas vezes por segundo)
                if (Time.time >= m_NextProbe)
                {
                    m_NextProbe = Time.time + 0.2f;
                    m_Detour = Vector3.zero;
                    if (Blocked(pos, dir, Mathf.Min(dist, 1.2f)))
                    {
                        var left = Quaternion.Euler(0f, -50f, 0f) * dir;
                        var rightDir = Quaternion.Euler(0f, 50f, 0f) * dir;
                        if (!Blocked(pos, left, 1.2f)) m_Detour = left;
                        else if (!Blocked(pos, rightDir, 1.2f)) m_Detour = rightDir;
                    }
                }
                if (m_Detour != Vector3.zero) dir = m_Detour;
                bool run = ownerDist > 4f || m_Owner.Motor.IsSprinting;
                speed = Mathf.Min(run ? m_Def.runSpeed : m_Def.walkSpeed, dist * 4f);
                m_Velocity = Vector3.Lerp(m_Velocity, dir * speed, 1f - Mathf.Exp(-10f * dt));
            }
            else m_Velocity = Vector3.Lerp(m_Velocity, Vector3.zero, 1f - Mathf.Exp(-8f * dt));

            pos += m_Velocity * dt;
            var ground = Ground(pos);
            pos.y = Mathf.Lerp(pos.y, ground.y, 1f - Mathf.Exp(-15f * dt));
            transform.position = pos;

            // Vira-se para onde anda; parado olha para onde o dono olha
            var look = m_Velocity.sqrMagnitude > 0.2f ? m_Velocity : fwd;
            float targetYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            m_Yaw = Mathf.LerpAngle(m_Yaw, targetYaw, 1f - Mathf.Exp(-10f * dt));
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);

            // ---------------- animação
            float v = new Vector3(m_Velocity.x, 0f, m_Velocity.z).magnitude;
            if (Time.time >= m_AnimUntil)
                m_Rig.SetAnim(v > m_Def.walkSpeed * 0.9f ? PetAnim.Run : v > 0.4f ? PetAnim.Walk : PetAnim.Idle);
            m_Rig.Tick(dt, Mathf.Clamp01(v / Mathf.Max(1f, m_Def.runSpeed)));

            if (m_Owner.IsLocalHuman) UpdateLocalPassives();
        }

        /// <summary>Radar (Cão) e alerta (Galo): só informação, só para o dono. Desligado em competitivo pelas regras.</summary>
        void UpdateLocalPassives()
        {
            var stats = m_Owner.Health.Stats;
            if (stats.petRadarRadius > 0f && Time.time >= m_NextRadar)
            {
                m_NextRadar = Time.time + 0.25f;
                LocalRadarTargets.Clear();
                float r2 = stats.petRadarRadius * stats.petRadarRadius;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || p == m_Owner || !p.IsSpawned || p.Health.IsDead || MatchManager.AreTeammates(p, m_Owner)) continue;
                    // Nunca revela quem está escondido num arbusto
                    if (p.Visuals != null && p.Visuals.IsConcealedForLocal) continue;
                    if ((p.transform.position - m_Owner.transform.position).sqrMagnitude <= r2) LocalRadarTargets.Add(p);
                }
            }
            else if (stats.petRadarRadius <= 0f && LocalRadarTargets.Count > 0) LocalRadarTargets.Clear();

            if (stats.petAlertRadius > 0f)
            {
                float r2 = stats.petAlertRadius * stats.petAlertRadius;
                var aim = m_Owner.Motor.AimDirection;
                aim.y = 0f;
                foreach (var p in PlayerNetwork.All)
                {
                    if (p == null || p == m_Owner || !p.IsSpawned || p.Health.IsDead || MatchManager.AreTeammates(p, m_Owner)) continue;
                    var d = p.transform.position - m_Owner.transform.position;
                    bool near = d.sqrMagnitude <= r2 && !(p.Visuals != null && p.Visuals.IsConcealedForLocal);
                    if (!near) { m_AlertedNear.Remove(p.Id); continue; }
                    if (m_AlertedNear.Contains(p.Id)) continue;
                    m_AlertedNear.Add(p.Id);
                    d.y = 0f;
                    bool behind = Vector3.Angle(aim, d) > 100f;
                    if (behind && Time.time >= m_NextAlert)
                    {
                        m_NextAlert = Time.time + 4f;
                        m_Rig.SetAnim(PetAnim.Alert);
                        m_AnimUntil = Time.time + 0.9f;
                        Sfx.Play2D(Sound.PetHappy, 0.7f);
                        GameUI.Toast(m_Def.DisplayName + ": inimigo atrás de ti!", new Color(1f, 0.6f, 0.2f));
                    }
                }
            }
        }
    }
}
