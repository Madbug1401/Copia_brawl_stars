using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPSLAN
{
    /// <summary>Indicador de direção mostrado no HUD pelas habilidades do pet do jogador local.</summary>
    public struct PetIndicator
    {
        /// <summary>Ângulo (graus) em relação para onde o jogador olha: 0 = em frente, 90 = direita.</summary>
        public float angle;
        public float strength;
        public bool alert;
    }

    /// <summary>
    /// Pet que acompanha um jogador (visual em todas as máquinas, sem colisores nem rede própria: a espécie, o nível e a
    /// skin vêm do loadout sincronizado do dono). Segue o dono a uma distância adequada, contorna paredes seguindo o
    /// rasto do dono e teletransporta-se discretamente se ficar preso ou longe. Animações procedurais: idle, andar,
    /// correr, celebrar (pirueta a partir do nível 5) e reação quando o dono sofre dano.
    /// No jogador local corre também as habilidades de informação (Cão: deteção; Galo: alerta).
    /// </summary>
    public class PetFollower : MonoBehaviour
    {
        const float TeleportDistance = 9f;
        const float StuckTeleportTime = 1.6f;
        const int MaxBreadcrumbs = 14;

        public static readonly List<PetIndicator> LocalIndicators = new List<PetIndicator>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LocalIndicators.Clear();

        PlayerNetwork m_Owner;
        PetDefinition m_Def;
        PetModel m_Model;
        int m_Level;
        int m_SkinHash;

        Vector3 m_Pos;
        float m_Yaw;
        float m_GroundY;
        float m_Speed;
        float m_Phase;
        float m_SideSign = 1f;
        float m_NextProbe, m_NextGround;
        bool m_PathBlocked;
        float m_StuckTime;
        Vector3 m_LastProgressPos;
        readonly Queue<Vector3> m_Crumbs = new Queue<Vector3>();
        float m_NextCrumb;

        float m_Celebrate = -1f;
        bool m_CelebrateTrick;
        float m_React;
        bool m_Visible = true;
        float m_NextAbilityScan;
        float m_NextBark;
        float m_NextEffect;
        readonly HashSet<ulong> m_Detected = new HashSet<ulong>();

        static readonly RaycastHit[] s_Hits = new RaycastHit[12];

        public PetDefinition Definition => m_Def;
        public int Level => m_Level;

        public static PetFollower Create(PlayerNetwork owner, PetDefinition def, int level, SkinDefinition skin)
        {
            var go = new GameObject("PetFollower_" + (def != null ? def.petId : "pet"));
            var f = go.AddComponent<PetFollower>();
            f.Init(owner, def, level, skin);
            return f;
        }

        void Init(PlayerNetwork owner, PetDefinition def, int level, SkinDefinition skin)
        {
            m_Owner = owner;
            m_Def = def;
            m_Level = Mathf.Clamp(level, 1, 10);
            m_SkinHash = skin != null ? skin.StableHash : 0;
            m_Model = PetVisualBuilder.Build(def, m_Level, skin, transform, ShadowCastingMode.On);
            m_SideSign = (owner != null && owner.Id % 2 == 0) ? 1f : -1f;
            var start = owner != null ? owner.transform.position + owner.transform.rotation * new Vector3(m_SideSign * 0.9f, 0f, -1f) : Vector3.zero;
            m_Pos = start;
            m_GroundY = start.y;
            m_LastProgressPos = start;
            transform.position = start;
            Fx.Puff(start + Vector3.up * 0.3f, Vector3.up, Art.WithAlpha(Color.white, 0.6f), 0.5f, 0.4f);
        }

        /// <summary>A mesma configuração? (evita reconstruir o modelo sem necessidade)</summary>
        public bool Matches(PetDefinition def, int level, int skinHash) =>
            def == m_Def && PetDefinition.StageFor(level) == PetDefinition.StageFor(m_Level) &&
            PetProgressionManager.HasMilestone(def, level, PetMilestoneType.Cosmetic) == m_Model.aura &&
            PetProgressionManager.HasMilestone(def, level, PetMilestoneType.Animation) == m_Model.trickUnlocked &&
            skinHash == m_SkinHash;

        public void Celebrate()
        {
            m_Celebrate = 0f;
            m_CelebrateTrick = m_Model != null && m_Model.trickUnlocked;
            if (m_Visible) Sfx.PlayAt(Sound.PetHappy, m_Pos + Vector3.up * 0.4f, 0.8f);
        }

        public void ReactToOwnerHurt()
        {
            m_React = 1f;
        }

        public void Despawn()
        {
            if (m_Visible) Fx.Puff(m_Pos + Vector3.up * 0.3f, Vector3.up, Art.WithAlpha(Color.white, 0.6f), 0.5f, 0.4f);
            if (m_Owner != null && m_Owner.IsLocalHuman) LocalIndicators.Clear();
            Destroy(gameObject);
        }

        // ================================================================== update

        void Update()
        {
            if (m_Owner == null || !m_Owner.IsSpawned || m_Model == null)
            {
                Destroy(gameObject);
                return;
            }
            float dt = Time.deltaTime;
            bool ownerDead = m_Owner.Health.IsDead;
            bool hidden = ownerDead || (m_Owner.Visuals != null && m_Owner.Visuals.IsConcealedForLocal);
            SetVisible(!hidden);

            if (ownerDead)
            {
                // Volta a aparecer junto ao dono quando ele reaparecer
                m_Crumbs.Clear();
                TeleportNear(m_Owner.transform.position, false);
                return;
            }

            Follow(dt);
            Animate(dt);
            if (m_Owner.IsLocalHuman) RunLocalAbility();
            EmitEffects();
        }

        void Follow(float dt)
        {
            var ownerT = m_Owner.transform;
            var ownerPos = ownerT.position;

            // Rasto do dono (caminho sem paredes por onde o pet pode ir)
            if (Time.time >= m_NextCrumb)
            {
                m_NextCrumb = Time.time + 0.25f;
                if (m_Crumbs.Count == 0 || (Last(m_Crumbs) - ownerPos).sqrMagnitude > 0.6f)
                {
                    m_Crumbs.Enqueue(ownerPos);
                    while (m_Crumbs.Count > MaxBreadcrumbs) m_Crumbs.Dequeue();
                }
            }

            var desired = ownerPos + ownerT.rotation * new Vector3(m_SideSign * 0.95f, 0f, -1.05f);
            float distToOwner = Flat(ownerPos - m_Pos).magnitude;

            if (distToOwner > TeleportDistance || m_StuckTime > StuckTeleportTime)
            {
                TeleportNear(ownerPos, true);
                return;
            }

            // De vez em quando: o caminho direto está livre? senão segue o rasto
            if (Time.time >= m_NextProbe)
            {
                m_NextProbe = Time.time + 0.15f;
                m_PathBlocked = Blocked(m_Pos, desired);
            }
            var target = desired;
            if (m_PathBlocked)
            {
                while (m_Crumbs.Count > 0 && Flat(m_Crumbs.Peek() - m_Pos).sqrMagnitude < 0.36f) m_Crumbs.Dequeue();
                if (m_Crumbs.Count > 0) target = m_Crumbs.Peek();
            }

            var to = Flat(target - m_Pos);
            float dist = to.magnitude;
            float ownerSpeed = m_Owner.Motor != null ? m_Owner.Motor.HorizontalVelocity.magnitude : 6f;
            float maxSpeed = Mathf.Max(7f, ownerSpeed * 1.3f + 1.5f);
            float wanted = dist < 0.25f ? 0f : Mathf.Min(maxSpeed, dist * 3.2f);
            m_Speed = Mathf.MoveTowards(m_Speed, wanted, 30f * dt);
            if (dist > 0.001f) m_Pos += to / dist * Mathf.Min(dist, m_Speed * dt);

            // Preso: não se aproxima mesmo querendo
            if (wanted > 1f)
            {
                if ((m_Pos - m_LastProgressPos).sqrMagnitude < 0.0004f) m_StuckTime += dt;
                else m_StuckTime = Mathf.Max(0f, m_StuckTime - dt);
            }
            else m_StuckTime = 0f;
            m_LastProgressPos = m_Pos;

            // Chão
            if (Time.time >= m_NextGround)
            {
                m_NextGround = Time.time + 0.1f;
                if (FindGround(m_Pos, out float gy)) m_GroundY = gy;
                else m_GroundY = ownerPos.y;
            }
            m_Pos.y = Mathf.Lerp(m_Pos.y, m_GroundY, 1f - Mathf.Exp(-14f * dt));

            // Orientação: para onde anda; parado, olha para onde o dono olha
            float targetYaw = m_Speed > 0.4f && dist > 0.05f ? Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg : ownerT.eulerAngles.y;
            m_Yaw = Mathf.MoveTowardsAngle(m_Yaw, targetYaw, 540f * dt);
            transform.SetPositionAndRotation(m_Pos, Quaternion.Euler(0f, m_Yaw, 0f));
        }

        void TeleportNear(Vector3 ownerPos, bool effect)
        {
            var ownerT = m_Owner.transform;
            var spot = ownerPos + ownerT.rotation * new Vector3(m_SideSign * 0.9f, 0f, -0.9f);
            if (Blocked(ownerPos, spot)) spot = ownerPos + ownerT.rotation * new Vector3(-m_SideSign * 0.9f, 0f, -0.9f);
            if (Blocked(ownerPos, spot)) spot = ownerPos;
            if (effect && m_Visible) Fx.Puff(m_Pos + Vector3.up * 0.3f, Vector3.up, Art.WithAlpha(Color.white, 0.5f), 0.45f, 0.35f);
            m_Pos = spot;
            m_GroundY = FindGround(spot, out float gy) ? gy : ownerPos.y;
            m_Pos.y = m_GroundY;
            m_StuckTime = 0f;
            m_Speed = 0f;
            m_LastProgressPos = m_Pos;
            m_Crumbs.Clear();
            transform.position = m_Pos;
            if (effect && m_Visible) Fx.Puff(m_Pos + Vector3.up * 0.3f, Vector3.up, Art.WithAlpha(Color.white, 0.5f), 0.45f, 0.35f);
        }

        // ================================================================== animação

        void Animate(float dt)
        {
            var m = m_Model;
            float time = Time.time;
            float move01 = Mathf.Clamp01(m_Speed / 6f);
            m_Phase += dt * (5f + m_Speed * 2.2f);
            float swing = Mathf.Sin(m_Phase);
            float bob = Mathf.Abs(Mathf.Cos(m_Phase));

            var bodyPos = Vector3.zero;
            var bodyRot = Quaternion.identity;

            if (m.species == PetSpecies.Rabbit)
            {
                // Coelho: anda aos saltinhos
                bodyPos.y = bob * 0.16f * move01;
                for (int i = 0; i < m.legs.Count; i++) m.legs[i].localRotation = Quaternion.Euler(-35f * bob * move01, 0f, 0f);
            }
            else
            {
                bodyPos.y = bob * 0.035f * move01 + Mathf.Sin(time * 2.5f) * 0.006f * (1f - move01);
                for (int i = 0; i < m.legs.Count; i++)
                {
                    float s = (i == 0 || i == 3) ? swing : -swing;
                    if (m.legs.Count == 2) s = i == 0 ? swing : -swing;
                    m.legs[i].localRotation = Quaternion.Euler(s * 38f * move01, 0f, 0f);
                }
            }

            // Cabeça: olha à volta parado; galo bica para a frente ao andar
            if (m.head != null)
            {
                float look = Mathf.Sin(time * 0.7f + m_SideSign) * 25f * (1f - move01);
                float peck = m.species == PetSpecies.Rooster ? Mathf.Sin(m_Phase) * 12f * move01 : 0f;
                m.head.localRotation = Quaternion.Euler(peck - m_React * 25f, look, 0f);
            }
            if (m.tail != null)
            {
                float wag = m.species == PetSpecies.Dog ? Mathf.Sin(time * (move01 > 0.1f ? 18f : 9f)) * 30f : Mathf.Sin(time * 2f) * 10f;
                m.tail.localRotation = Quaternion.Euler(0f, wag, 0f);
            }
            for (int i = 0; i < m.ears.Count; i++)
                m.ears[i].localRotation = Quaternion.Euler(Mathf.Sin(time * 3f + i) * 6f + move01 * 18f, 0f, (i == 0 ? -1f : 1f) * 18f);
            if (m.wingL != null)
            {
                float flap = (m_Celebrate >= 0f ? Mathf.Sin(time * 30f) * 40f : 0f) + move01 * Mathf.Sin(time * 14f) * 12f;
                m.wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                m.wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
            }

            // Reação ao dano do dono: salto curto
            if (m_React > 0f)
            {
                bodyPos.y += Mathf.Sin(m_React * Mathf.PI) * 0.15f;
                m_React = Mathf.MoveTowards(m_React, 0f, dt * 3.5f);
            }

            // Celebrar: salto com volta (a partir do nível 5 faz uma pirueta)
            if (m_Celebrate >= 0f)
            {
                m_Celebrate += dt / 0.8f;
                float k = Mathf.Clamp01(m_Celebrate);
                bodyPos.y += Mathf.Sin(k * Mathf.PI) * 0.45f;
                bodyRot = m_CelebrateTrick ? Quaternion.Euler(-360f * k, 0f, 0f) : Quaternion.Euler(0f, 360f * k, 0f);
                if (m_Celebrate >= 1f) m_Celebrate = -1f;
            }

            m.body.localPosition = bodyPos;
            m.body.localRotation = bodyRot;
        }

        void EmitEffects()
        {
            if (!m_Visible || Time.time < m_NextEffect) return;
            var m = m_Model;
            if (!m.aura && m.effect == SkinEffect.None) return;
            m_NextEffect = Time.time + 0.18f;
            var p = m_Pos + Vector3.up * (0.3f * m.scale) + Random.insideUnitSphere * 0.3f;
            if (m.aura) Fx.Glow(p, Vector3.up * 0.6f, Color.Lerp(m.accent, new Color(1f, 0.9f, 0.4f), 0.5f), 0.07f, 0.6f);
            if (m.effect == SkinEffect.Sparkles) Fx.Glow(p, Random.insideUnitSphere * 0.2f, Color.white, 0.05f, 0.4f);
            else if (m.effect == SkinEffect.Bubbles) Fx.Glow(p, Vector3.up * 0.5f, new Color(0.6f, 1f, 0.95f), 0.06f, 0.6f);
        }

        void SetVisible(bool visible)
        {
            if (visible == m_Visible) return;
            m_Visible = visible;
            foreach (var r in m_Model.renderers) if (r != null) r.enabled = visible;
        }

        // ================================================================== habilidades de informação (jogador local)

        void RunLocalAbility()
        {
            if (Time.time < m_NextAbilityScan) return;
            m_NextAbilityScan = Time.time + 0.25f;
            LocalIndicators.Clear();
            var mods = m_Owner.Effective.Value;
            var ability = mods.PetAbility;
            if (ability != PetAbilityType.Detection && ability != PetAbilityType.Alert) return;
            float radius = Mathf.Clamp(mods.petAbilityValue, 0f, 25f);
            var me = m_Owner.transform.position;
            float aimYaw = m_Owner.Motor.Yaw;
            bool newThreat = false;

            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == m_Owner || !p.IsSpawned || p.Health.IsDead || MatchManager.AreTeammates(m_Owner, p)) continue;
                var d = p.transform.position - me;
                float dist = Flat(d).magnitude;
                if (dist > radius) continue;
                float angle = Mathf.DeltaAngle(aimYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);

                if (ability == PetAbilityType.Detection)
                {
                    // Cão: fareja adversários próximos (mesmo escondidos), mas só mostra a direção
                    LocalIndicators.Add(new PetIndicator { angle = angle, strength = 1f - dist / Mathf.Max(1f, radius), alert = false });
                    if (m_Detected.Add(p.Id)) newThreat = true;
                }
                else if (Mathf.Abs(angle) > 100f && CombatMath.HasLineOfSight(p, m_Owner))
                {
                    // Galo: avisa de quem está nas costas e com linha de visão
                    LocalIndicators.Add(new PetIndicator { angle = angle, strength = 1f, alert = true });
                    if (m_Detected.Add(p.Id)) newThreat = true;
                }
            }
            if (LocalIndicators.Count == 0) m_Detected.Clear();

            if (newThreat && Time.time >= m_NextBark)
            {
                m_NextBark = Time.time + (ability == PetAbilityType.Alert ? 6f : 4f);
                Sfx.Play2D(ability == PetAbilityType.Alert ? Sound.PetAlert : Sound.PetHappy, 0.55f);
                m_React = 1f;
            }
        }

        // ================================================================== utilitários

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static Vector3 Last(Queue<Vector3> q)
        {
            Vector3 last = Vector3.zero;
            foreach (var v in q) last = v;
            return last;
        }

        /// <summary>Há uma parede entre dois pontos (à altura do pet)? Ignora jogadores.</summary>
        static bool Blocked(Vector3 from, Vector3 to)
        {
            var a = from + Vector3.up * 0.35f;
            var d = Flat(to - from);
            float dist = d.magnitude;
            if (dist < 0.05f) return false;
            int n = Physics.RaycastNonAlloc(a, d / dist, s_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_Hits[i].collider;
                if (c == null || c.GetComponentInParent<PlayerHealth>() != null) continue;
                return true;
            }
            return false;
        }

        static bool FindGround(Vector3 around, out float y)
        {
            y = around.y;
            int n = Physics.RaycastNonAlloc(around + Vector3.up * 1.2f, Vector3.down, s_Hits, 4f, CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_Hits[i];
                if (h.collider == null || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                    found = true;
                }
            }
            return found;
        }
    }
}
