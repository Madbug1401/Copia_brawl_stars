using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Infraestrutura de combate partilhada por todos os Brawlers (substitui o ProjectileSystem).
    ///
    /// Cada ataque é simulado duas vezes, a partir da MESMA intenção (origem, direção, ponto-alvo, seed):
    /// - instância autoritativa (só no host, invisível): é a única que aplica dano/estados via EffectResolver;
    /// - instância visual (todas as máquinas): projéteis, tracers, sprays, zonas, minas e explosões.
    ///
    /// Primitivas genéricas usadas pelas AttackDelivery: hitscan, projétil, arco (com fuse), cone persistente,
    /// armadilhas, zonas/auras. Nenhum código específico de personagem.
    /// </summary>
    public class CombatSystem : MonoBehaviour
    {
        public const float HeadshotHeight = 1.38f;

        public static CombatSystem Instance { get; private set; }

        // ------------------------------------------------------------------ entidades

        class PendingAttack
        {
            public AttackContext ctx;
            public int fired;
            public float nextTime;
        }

        class Projectile
        {
            public AttackContext ctx;
            public Vector3 position, velocity;
            public float gravity, remaining, traveled, radius, life, maxLife, fuse;
            public bool pierce, ignorePlayers, isArc;
            public ProjVisual visual;
            public readonly List<PlayerHealth> pierced = new List<PlayerHealth>();
        }

        class Fuse
        {
            public AttackContext ctx;
            public Vector3 position;
            public float timer, total;
            public ProjVisual visual;
        }

        class Zone
        {
            public AttackContext ctx;
            public ZoneSettings settings;
            public Vector3 position;
            public PlayerNetwork follow;
            public float age, nextTick, nextPuff;
            public ZoneVisual visual;
        }

        class Trap
        {
            public AttackContext ctx;
            public TrapSettings settings;
            public Vector3 position;
            public float age, triggeredAt = -1f;
            public bool ownerView;
            public ProjVisual visual;
            /// <summary>Identificador determinístico (igual em todas as máquinas) usado nos eventos do host.</summary>
            public int key;
            public int serial;
            /// <summary>Visual: o host mandou explodir/remover.</summary>
            public byte pendingEvent;
        }

        /// <summary>Eventos de armadilhas enviados pelo host aos clientes.</summary>
        public enum TrapEvent : byte
        {
            None = 0,
            Triggered = 1,
            Exploded = 2,
            Snared = 3,
            Destroyed = 4,
        }

        class Cone
        {
            public AttackContext ctx;
            public ConeVolumeDelivery delivery;
            public Vector3 visualOrigin;
            public float age, nextTick, nextPuff;
            public int puffCount;
        }

        /// <summary>Visual de uma zona/aura: disco no chão + anel que roda + borda.</summary>
        public class ZoneVisual
        {
            public Transform root, disc, ring, edge;
            public Renderer discR, ringR, edgeR;
        }

        readonly List<PendingAttack> m_Pending = new List<PendingAttack>();
        readonly List<Projectile> m_Projectiles = new List<Projectile>();
        readonly List<Fuse> m_Fuses = new List<Fuse>();
        readonly List<Zone> m_Zones = new List<Zone>();
        readonly List<Trap> m_Traps = new List<Trap>();
        readonly List<Cone> m_Cones = new List<Cone>();

        readonly Stack<ZoneVisual> m_ZonePool = new Stack<ZoneVisual>();
        readonly Stack<Projectile> m_ProjectilePool = new Stack<Projectile>();

        readonly RaycastHit[] m_Hits = new RaycastHit[32];
        readonly List<PlayerHealth> m_HitscanPierced = new List<PlayerHealth>();
        int m_TrapSerial;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================== API pública

        /// <summary>Inicia um ataque a partir de uma intenção. ctx.authoritative=true só no host.</summary>
        public void StartAttack(AttackContext ctx)
        {
            if (ctx == null || ctx.attack == null || ctx.attack.delivery == null) return;
            if (ctx.direction.sqrMagnitude < 0.0001f) ctx.direction = Vector3.forward;
            ctx.direction.Normalize();

            if (!ctx.authoritative)
                Sfx.PlayAt(ctx.attack.visuals.sound, ctx.origin, ctx.shooter != null && ctx.shooter.IsLocalHuman ? 0.7f : 1f);

            var p = new PendingAttack { ctx = ctx, fired = 0, nextTime = Time.time };
            m_Pending.Add(p);
            ProcessPending(p, Time.time); // o primeiro disparo sai já neste frame
            if (p.fired >= ctx.attack.delivery.ShotCount) m_Pending.Remove(p);
        }

        public void ClearAll()
        {
            m_Pending.Clear();
            foreach (var p in m_Projectiles) ReleaseProjectile(p);
            m_Projectiles.Clear();
            foreach (var f in m_Fuses) ProjectileVisuals.Release(f.visual);
            m_Fuses.Clear();
            foreach (var z in m_Zones) ReleaseZone(z.visual);
            m_Zones.Clear();
            foreach (var t in m_Traps) ProjectileVisuals.ReleaseMine(t.visual);
            m_Traps.Clear();
            m_Cones.Clear();
            EffectResolver.ResetMatchState();
        }

        // ------------------------------------------------------------------ primitivas (usadas pelas deliveries)

        public void TraceHitscan(AttackContext ctx, Vector3 dir, float range, float radius, bool pierce, int shotIndex)
        {
            Vector3 origin = ctx.origin;
            Vector3 end = origin + dir * range;
            bool hitSomething = false;

            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, radius), dir, m_Hits, range, CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
            SortHits(count);
            var pierced = m_HitscanPierced;
            pierced.Clear();

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                if (hit.collider == null || IsShooter(ctx, hit.collider)) continue;
                Vector3 point = hit.distance <= 0f ? origin : hit.point;
                var health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    if (health.IsDead || pierced.Contains(health)) continue;
                    if (ctx.authoritative)
                        EffectResolver.DirectHit(ctx, health, point, hit.distance, CombatMath.IsHeadshot(health.transform, origin, dir, point));
                    else Fx.HitStar(point, ctx.attack.visuals.color, 0.9f);
                    pierced.Add(health);
                    if (pierce) continue;
                    end = point;
                    hitSomething = true;
                    break;
                }

                if (ctx.authoritative) EffectResolver.SurfaceHit(ctx, hit.collider);
                end = point;
                hitSomething = true;
                break;
            }

            if (!ctx.authoritative)
            {
                var from = Muzzle(ctx, shotIndex);
                float w = 0.07f * Mathf.Max(0.6f, ctx.attack.visuals.scale);
                Fx.TracerLine(from, end, ctx.attack.visuals.color, w, 0.14f);
                Fx.TracerLine(from, end, Color.white, w * 0.35f, 0.08f);
                ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, shotIndex);
                if (hitSomething) Fx.Impact(end, ctx.attack.visuals.color, 1.2f);
            }
            if (hitSomething) Detonate(ctx, end, false);
        }

        /// <summary>Facada autoritativa: o primeiro jogador ou obstáculo bloqueia o golpe.</summary>
        public void TraceMelee(PlayerNetwork attacker, Vector3 origin, Vector3 direction, MeleeProfile profile)
        {
            if (attacker == null || profile == null || !attacker.IsServer) return;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : attacker.Motor.AimDirection;
            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.05f, profile.radius), direction,
                m_Hits, Mathf.Max(0.5f, profile.range), CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
            SortHits(count);

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<PlayerNetwork>() == attacker) continue;
                var health = hit.collider.GetComponentInParent<PlayerHealth>();
                if (health != null)
                {
                    if (health.IsDead) continue;
                    EffectResolver.DamageRaw(health, Mathf.Max(1, profile.damage), attacker.NetworkObjectId, false, false);
                    break;
                }

                var cover = hit.collider.GetComponentInParent<DestructibleCover>();
                if (cover != null) cover.ServerDamage(Mathf.Max(1, profile.damage));
                break;
            }

            // Facada perto de uma armadilha inimiga desativa-a
            ServerDestroyTraps(origin + direction * Mathf.Max(0.5f, profile.range) * 0.8f, 1.4f, attacker.Id, true);
        }

        public void LaunchProjectile(AttackContext ctx, Vector3 dir, float speed, float range, float radius, bool pierce, int shotIndex)
        {
            Vector3 start = ctx.origin;
            Vector3 velocity = dir * speed;

            if (!ctx.authoritative)
            {
                // O visual sai do cano e converge para a trajetória real
                start = Muzzle(ctx, shotIndex);
                var converge = ctx.origin + dir * Mathf.Min(range, 20f);
                velocity = (converge - start).normalized * speed;
                ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, shotIndex);
            }

            var p = NewProjectile(ctx, start, velocity);
            p.remaining = range;
            p.radius = radius;
            p.pierce = pierce;
            p.maxLife = range / Mathf.Max(1f, speed) + 0.5f;
            m_Projectiles.Add(p);
        }

        public void LaunchArc(AttackContext ctx, ArcProjectileDelivery d, Vector3 target, int shotIndex)
        {
            // A trajetória autoritativa sai sempre da origem validada; o visual sai da mão e converge para o mesmo alvo
            Vector3 start = ctx.authoritative ? ctx.origin : Muzzle(ctx, shotIndex);
            if (!ctx.authoritative) ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, shotIndex);
            LaunchArcFrom(ctx, start, target, d.gravity, d.speed, d.minFlightTime, d.maxFlightTime, d.radius, d.fuseDelay, d.explodeOnPlayerContact);
        }

        /// <summary>
        /// Projétil em arco genérico: explode ao aterrar ou após o fuse. Por omissão passa por cima dos jogadores;
        /// com hitPlayers explode logo ao acertar diretamente num inimigo (ex.: TNT).
        /// </summary>
        public void LaunchArcFrom(AttackContext ctx, Vector3 start, Vector3 target, float gravity, float horizontalSpeed, float minTime, float maxTime, float radius, float fuse, bool hitPlayers = false)
        {
            var velocity = CombatMath.ArcVelocity(start, target, gravity, horizontalSpeed, minTime, maxTime, out float flight);
            var p = NewProjectile(ctx, start, velocity);
            p.gravity = gravity;
            p.remaining = float.MaxValue;
            p.radius = radius;
            p.isArc = true;
            p.ignorePlayers = !hitPlayers;
            p.fuse = fuse;
            p.maxLife = flight + 1.5f;
            m_Projectiles.Add(p);
        }

        public void SpawnCone(AttackContext ctx, ConeVolumeDelivery d)
        {
            m_Cones.Add(new Cone
            {
                ctx = ctx,
                delivery = d,
                visualOrigin = ctx.authoritative ? ctx.origin : Muzzle(ctx, 0),
                age = 0f,
                // Primeiro tick quando o spray já tem alcance (não desperdiçar um tick com alcance 0)
                nextTick = Mathf.Min(d.tickInterval, d.expandTime) * 0.75f,
                nextPuff = 0f,
            });
            if (!ctx.authoritative) ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, 0);
        }

        /// <summary>
        /// Coloca uma armadilha. Posições inválidas (dentro de paredes, água, fora da arena, sem chão) são ignoradas
        /// de forma igual em todas as máquinas. A chave (seed + índice) liga a instância visual aos eventos do host.
        /// </summary>
        public void SpawnTrap(AttackContext ctx, Vector3 position, TrapSettings settings, int index = 0)
        {
            if (m_Traps.Count > 64 || settings == null) return;
            if (!CombatMath.TryGetTrapPosition(position, out var ground)) return;

            // Limite de armadilhas ativas por dono (as mais antigas desaparecem)
            if (settings.maxActivePerOwner > 0)
            {
                int active = 0;
                for (int i = 0; i < m_Traps.Count; i++)
                    if (m_Traps[i].ctx.authoritative == ctx.authoritative && m_Traps[i].ctx.attackerId == ctx.attackerId && m_Traps[i].settings == settings) active++;
                for (int i = 0; i < m_Traps.Count && active >= settings.maxActivePerOwner; i++)
                {
                    var old = m_Traps[i];
                    if (old.ctx.authoritative != ctx.authoritative || old.ctx.attackerId != ctx.attackerId || old.settings != settings) continue;
                    if (!old.ctx.authoritative) Fx.Poof(old.position + Vector3.up * 0.2f, settings.color);
                    ProjectileVisuals.ReleaseMine(old.visual);
                    m_Traps.RemoveAt(i);
                    i--;
                    active--;
                }
            }

            var t = new Trap
            {
                ctx = ctx,
                settings = settings,
                position = ground,
                ownerView = ctx.shooter != null && ctx.shooter.IsLocalHuman,
                key = TrapKey(ctx.seed, index),
                serial = ++m_TrapSerial,
            };
            if (!ctx.authoritative)
            {
                bool discreet = settings.hiddenFromEnemies && !t.ownerView && !IsFriendlyToLocal(ctx);
                t.visual = settings.effect == TrapEffect.Snare
                    ? ProjectileVisuals.TakeSnare(settings.color, t.position, discreet)
                    : ProjectileVisuals.TakeMine(settings.color, t.position, discreet);
                Fx.Ring(t.position, settings.color, 1.6f, 0.35f);
                Fx.Sparks(t.position + Vector3.up * 0.2f, settings.color, 5, 3f);
            }
            m_Traps.Add(t);
        }

        public static int TrapKey(int seed, int index) => unchecked(seed * 31 + index * 7919 + 1);

        static bool IsFriendlyToLocal(AttackContext ctx)
        {
            var me = PlayerNetwork.Local;
            return me != null && ctx.shooter != null && (me == ctx.shooter || MatchManager.AreTeammates(me, ctx.shooter));
        }

        /// <summary>Cliente/host (visual): aplica um evento de armadilha decidido pelo host.</summary>
        public void ApplyTrapEvent(ulong attackerId, int key, TrapEvent evt, ulong victimId)
        {
            for (int i = 0; i < m_Traps.Count; i++)
            {
                var t = m_Traps[i];
                if (t.ctx.authoritative || t.ctx.attackerId != attackerId || t.key != key) continue;
                switch (evt)
                {
                    case TrapEvent.Triggered:
                        if (t.triggeredAt < 0f)
                        {
                            t.triggeredAt = t.age;
                            Sfx.PlayAt(Sound.MineArm, t.position, 1f);
                        }
                        break;
                    case TrapEvent.Exploded:
                    case TrapEvent.Snared:
                    case TrapEvent.Destroyed:
                        t.pendingEvent = (byte)evt;
                        if (evt == TrapEvent.Snared)
                        {
                            var victim = PlayerNetwork.Find(victimId);
                            if (victim != null) t.position = victim.transform.position;
                        }
                        break;
                }
                return;
            }
        }

        /// <summary>Host: destrói/desativa armadilhas inimigas num raio (explosões e facadas).</summary>
        public void ServerDestroyTraps(Vector3 center, float radius, ulong byPlayer, bool melee)
        {
            var by = PlayerNetwork.Find(byPlayer);
            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                var t = m_Traps[i];
                if (!t.ctx.authoritative || t.ctx.attackerId == byPlayer) continue;
                if (melee ? !t.settings.defusedByEnemyMelee : !t.settings.destroyedByEnemyExplosions) continue;
                if (by != null && t.ctx.shooter != null && MatchManager.AreTeammates(by, t.ctx.shooter)) continue;
                if ((t.position - center).sqrMagnitude > radius * radius) continue;
                BroadcastTrapEvent(t, TrapEvent.Destroyed, 0);
                m_Traps.RemoveAt(i);
            }
        }

        void BroadcastTrapEvent(Trap t, TrapEvent evt, ulong victimId)
        {
            ApplyTrapEvent(t.ctx.attackerId, t.key, evt, victimId); // instância visual do próprio host
            if (t.ctx.shooter != null && t.ctx.shooter.IsSpawned) t.ctx.shooter.Combat.ServerSendTrapEvent(t.key, evt, victimId);
        }

        public void SpawnZone(AttackContext ctx, Vector3 position, ZoneSettings settings, PlayerNetwork follow)
        {
            if (m_Zones.Count > 48 || settings == null) return;
            var z = new Zone { ctx = ctx, settings = settings, position = position, follow = follow };
            if (!ctx.authoritative)
            {
                z.visual = TakeZone();
                z.visual.root.position = position + Vector3.up * 0.04f;
                Art.SetColor(z.visual.discR, Art.WithAlpha(settings.color, 0.22f));
                Art.SetColor(z.visual.ringR, Art.WithAlpha(Color.Lerp(settings.color, Color.white, 0.3f), 0.9f));
                var edge = settings.secondaryColor.a > 0.01f ? settings.secondaryColor : settings.color;
                Art.SetColor(z.visual.edgeR, Art.WithAlpha(edge, 0.75f));
                PoseZone(z, 0f);
                Fx.Ring(position, settings.color, settings.radius * 2.2f, 0.35f);
            }
            m_Zones.Add(z);
        }

        // ================================================================== Update

        void Update()
        {
            float now = Time.time;
            float dt = Time.deltaTime;

            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                var p = m_Pending[i];
                if (!ProcessPending(p, now)) m_Pending.RemoveAt(i);
            }

            if (m_Projectiles.Count > 0 || m_Cones.Count > 0) Physics.SyncTransforms();

            for (int i = m_Projectiles.Count - 1; i >= 0; i--)
            {
                var p = m_Projectiles[i];
                if (!StepProjectile(p, dt))
                {
                    ReleaseProjectile(p);
                    m_Projectiles.RemoveAt(i);
                }
            }

            for (int i = m_Fuses.Count - 1; i >= 0; i--)
            {
                var f = m_Fuses[i];
                f.timer -= dt;
                if (f.visual != null) ProjectileVisuals.Blink(f.visual, 1f - f.timer / Mathf.Max(0.01f, f.total));
                if (f.timer <= 0f)
                {
                    Detonate(f.ctx, f.position + Vector3.up * 0.3f, true);
                    ProjectileVisuals.Release(f.visual);
                    m_Fuses.RemoveAt(i);
                }
            }

            for (int i = m_Cones.Count - 1; i >= 0; i--)
                if (!StepCone(m_Cones[i], dt)) m_Cones.RemoveAt(i);

            for (int i = m_Zones.Count - 1; i >= 0; i--)
            {
                if (!StepZone(m_Zones[i], dt))
                {
                    ReleaseZone(m_Zones[i].visual);
                    m_Zones.RemoveAt(i);
                }
            }

            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                if (!StepTrap(m_Traps[i], dt))
                {
                    ProjectileVisuals.ReleaseMine(m_Traps[i].visual);
                    m_Traps.RemoveAt(i);
                }
            }
        }

        bool ProcessPending(PendingAttack p, float now)
        {
            var ctx = p.ctx;
            if (ctx.shooter != null && (!ctx.shooter.IsSpawned || ctx.shooter.Health.IsDead)) return false;
            var d = ctx.attack.delivery;
            int total = d.ShotCount;
            bool simultaneous = d.ShotInterval <= 0f;
            while (p.fired < total && (simultaneous || now >= p.nextTime))
            {
                d.FireShot(this, ctx, p.fired);
                p.fired++;
                p.nextTime += d.ShotInterval;
            }
            return p.fired < total;
        }

        // ------------------------------------------------------------------ projéteis

        Projectile NewProjectile(AttackContext ctx, Vector3 start, Vector3 velocity)
        {
            var p = m_ProjectilePool.Count > 0 ? m_ProjectilePool.Pop() : new Projectile();
            p.ctx = ctx;
            p.position = start;
            p.velocity = velocity;
            p.gravity = 0f;
            p.remaining = 0f;
            p.traveled = 0f;
            p.life = 0f;
            p.fuse = 0f;
            p.pierce = false;
            p.ignorePlayers = false;
            p.isArc = false;
            p.pierced.Clear();
            p.visual = null;

            if (!ctx.authoritative)
            {
                var style = ctx.styleOverride != ProjectileStyle.Auto ? ctx.styleOverride : ProjectileVisuals.Resolve(ctx.attack);
                float scale = ctx.attack.visuals.scale;
                // Os estilos com modelo próprio já têm tamanho legível; a escala só exagera balas/orbes
                if (style == ProjectileStyle.Dynamite || style == ProjectileStyle.Barrel || style == ProjectileStyle.Bomb || style == ProjectileStyle.Arrow)
                    scale = 1f;
                p.visual = ProjectileVisuals.Take(style, ctx.attack.visuals.color, scale, start, velocity);
            }
            return p;
        }

        bool StepProjectile(Projectile p, float dt)
        {
            p.life += dt;
            Vector3 g = Vector3.down * p.gravity;
            Vector3 disp = p.velocity * dt + 0.5f * g * dt * dt;
            p.velocity += g * dt;

            float dist = disp.magnitude;
            if (!p.isArc && dist > p.remaining)
            {
                disp = disp / Mathf.Max(0.0001f, dist) * p.remaining;
                dist = p.remaining;
            }
            if (dist <= 0.0001f) return false;
            Vector3 dir = disp / dist;

            int count = Physics.SphereCastNonAlloc(p.position, p.radius, dir, m_Hits, dist, CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
            SortHits(count);

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                var col = hit.collider;
                if (col == null || IsShooter(p.ctx, col)) continue;
                Vector3 point = hit.distance <= 0f ? p.position : hit.point;

                var health = col.GetComponentInParent<PlayerHealth>();
                if (health != null)
                {
                    if (p.ignorePlayers || health.IsDead || p.pierced.Contains(health)) continue;

                    if (p.ctx.authoritative)
                        EffectResolver.DirectHit(p.ctx, health, point, p.traveled + hit.distance, CombatMath.IsHeadshot(health.transform, p.position, dir, point));
                    else Fx.HitStar(point, p.ctx.attack.visuals.color, Mathf.Clamp(p.ctx.attack.visuals.scale, 0.7f, 1.5f));

                    p.pierced.Add(health);
                    if (p.pierce) continue;

                    Impact(p, point, null, true);
                    return false;
                }

                if (p.ctx.authoritative) EffectResolver.SurfaceHit(p.ctx, col);
                Impact(p, point, col);
                return false;
            }

            p.position += disp;
            p.traveled += dist;
            if (!p.isArc) p.remaining -= dist;

            if (p.visual != null) ProjectileVisuals.Step(p.visual, p.position, p.velocity, dt);

            bool alive = (p.isArc || p.remaining > 0f) && p.life < p.maxLife && p.position.y > -30f;
            if (!alive && p.isArc) Impact(p, p.position, null);
            return alive;
        }

        void Impact(Projectile p, Vector3 point, Collider surface, bool hitPlayer = false)
        {
            // Acerto direto num jogador: explode já (sem rastilho)
            if (p.isArc && p.fuse > 0f && !hitPlayer)
            {
                var f = new Fuse { ctx = p.ctx, position = GroundAt(point), timer = p.fuse, total = p.fuse };
                if (!p.ctx.authoritative && p.visual != null)
                {
                    // O mesmo objeto fica no chão a arder até explodir
                    f.visual = p.visual;
                    p.visual = null;
                    ProjectileVisuals.Land(f.visual, f.position + Vector3.up * 0.12f);
                    Fx.Smoke(f.position + Vector3.up * 0.1f, Vector3.up * 0.3f, 0.5f, 0.5f, 0.45f);
                }
                m_Fuses.Add(f);
                return;
            }

            if (!p.ctx.attack.effect.HasSplash && surface != null && !p.ctx.authoritative)
                Fx.Impact(point, p.ctx.attack.visuals.color, Mathf.Clamp(p.ctx.attack.visuals.scale, 0.7f, 1.6f));

            Detonate(p.ctx, point, false);
        }

        /// <summary>Explosão (se o efeito tiver splash) e zona no impacto (se configurada).</summary>
        void Detonate(AttackContext ctx, Vector3 point, bool always)
        {
            var e = ctx.attack.effect;
            if (e.HasSplash)
            {
                if (ctx.authoritative)
                {
                    EffectResolver.Explosion(ctx, point);
                    ServerDestroyTraps(point, e.splashRadius, ctx.attackerId, false);
                }
                else
                {
                    Fx.Explosion(point, e.splashRadius, ctx.attack.visuals.color, e.splashRadius < 1.6f ? 0.6f : 1f);
                    Sfx.PlayAt(ctx.attack.visuals.impactSound, point, Mathf.Clamp(0.5f + e.splashRadius * 0.2f, 0.6f, 1.2f));
                }
            }
            else if (always && !ctx.authoritative)
            {
                Fx.Impact(point, ctx.attack.visuals.color, 1.5f);
            }

            if (e.zoneOnImpact != null && e.zoneOnImpact.enabled)
                SpawnZone(ctx, GroundAt(point), e.zoneOnImpact, null);
        }

        // ------------------------------------------------------------------ cone

        bool StepCone(Cone c, float dt)
        {
            var d = c.delivery;
            var ctx = c.ctx;
            c.age += dt;

            if (ctx.shooter != null && (!ctx.shooter.IsSpawned || ctx.shooter.Health.IsDead)) return false;

            float reach = d.range * Mathf.Clamp01(c.age / Mathf.Max(0.01f, d.expandTime));
            float halfAngle = d.angle * 0.5f;

            if (ctx.authoritative && c.age >= c.nextTick)
            {
                c.nextTick += Mathf.Max(0.05f, d.tickInterval);
                foreach (var pl in PlayerNetwork.All)
                {
                    if (pl == null || !pl.IsSpawned || pl.Id == ctx.attackerId || pl.Health.IsDead) continue;
                    var chest = pl.transform.position + Vector3.up * 1.1f;
                    var v = chest - ctx.origin;
                    float dist = v.magnitude;
                    if (dist > reach + 0.5f || dist < 0.01f) continue;
                    float allowed = halfAngle + Mathf.Atan2(0.5f, dist) * Mathf.Rad2Deg;
                    if (Vector3.Angle(ctx.direction, v) > allowed) continue;
                    if (CombatMath.Raycast(ctx.origin, v / dist, dist, ctx.shooter, out var hit) &&
                        !hit.collider.transform.IsChildOf(pl.transform)) continue; // atrás de uma parede
                    EffectResolver.ConeTick(ctx, pl.Health, dist);
                }
            }

            if (!ctx.authoritative && c.age < d.duration * 0.85f && c.age >= c.nextPuff)
            {
                c.nextPuff += 0.022f;
                var rng = ctx.RngFor(1000 + c.puffCount++);
                float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * halfAngle;
                float pitch = (float)(rng.NextDouble() * 2.0 - 1.0) * halfAngle * 0.5f;
                var dir = Quaternion.LookRotation(ctx.direction) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                bool local = ctx.shooter != null && ctx.shooter.IsLocalHuman;
                // Duas latas: as nuvens alternam entre as duas mãos
                var from = ctx.shooter != null && ctx.shooter.Visuals != null ? ctx.shooter.Visuals.GetMuzzle(c.puffCount) : c.visualOrigin;
                var start = from + ctx.direction * (local ? 0.9f : 0.2f);
                float speed = d.range / Mathf.Max(0.05f, d.expandTime + 0.15f) * 1.25f;
                var col = Color.Lerp(ctx.attack.visuals.color, Color.white, (float)rng.NextDouble() * 0.35f);
                col.a = local ? 0.4f : 0.7f;
                float life = d.expandTime + 0.35f;
                Fx.Puff(start, dir * speed, col, (local ? 0.45f : 0.6f) * ctx.attack.visuals.scale, life);
                if (c.puffCount % 3 == 0)
                    Fx.Glow(start + dir * 0.3f, dir * speed * 0.9f, Color.Lerp(ctx.attack.visuals.color, Color.white, 0.5f), 0.12f, life * 0.8f);
            }

            return c.age < d.duration;
        }

        // ------------------------------------------------------------------ zonas / armadilhas

        bool StepZone(Zone z, float dt)
        {
            var s = z.settings;
            z.age += dt;
            if (z.ctx.shooter == null) return false; // dono saiu da partida

            if (z.follow != null)
            {
                if (!z.follow.IsSpawned || z.follow.Health.IsDead) return false;
                z.position = z.follow.transform.position;
            }

            if (z.visual != null)
            {
                z.visual.root.position = z.position + Vector3.up * 0.04f;
                PoseZone(z, dt);
            }

            if (z.ctx.authoritative && z.age >= z.nextTick)
            {
                z.nextTick += Mathf.Max(0.1f, s.tickInterval);
                foreach (var pl in PlayerNetwork.All)
                {
                    if (pl == null || !pl.IsSpawned || pl.Id == z.ctx.attackerId || pl.Health.IsDead) continue;
                    if (!Within(z.position, pl.transform.position, s.radius)) continue;
                    EffectResolver.ZoneTick(z.ctx, pl.Health, s);
                }
            }

            return z.age < s.duration;
        }

        /// <summary>
        /// Armadilhas: só a instância autoritativa (host) deteta inimigos e decide; as instâncias visuais seguem os
        /// eventos que o host envia (aviso, explosão, imobilização, destruição). A expiração é igual em todas.
        /// </summary>
        bool StepTrap(Trap t, float dt)
        {
            var s = t.settings;
            t.age += dt;
            if (t.age > s.lifetime || t.ctx.shooter == null)
            {
                if (!t.ctx.authoritative && t.ctx.shooter != null) Fx.Poof(t.position + Vector3.up * 0.2f, s.color);
                return false; // expirou ou o dono saiu
            }

            bool armed = t.age >= s.armTime;

            if (t.ctx.authoritative)
            {
                if (armed && t.triggeredAt < 0f)
                {
                    var victim = FindTrapVictim(t);
                    if (victim != null)
                    {
                        if (s.effect == TrapEffect.Snare)
                        {
                            if (EffectResolver.Snare(t.ctx, victim.Health, s))
                            {
                                BroadcastTrapEvent(t, TrapEvent.Snared, victim.Id);
                                return false; // a armadilha desaparece depois de ativada
                            }
                        }
                        else
                        {
                            t.triggeredAt = t.age;
                            BroadcastTrapEvent(t, TrapEvent.Triggered, victim.Id);
                        }
                    }
                }
                if (s.effect == TrapEffect.Explode && t.triggeredAt >= 0f && t.age >= t.triggeredAt + s.warningTime)
                {
                    BroadcastTrapEvent(t, TrapEvent.Exploded, 0);
                    Detonate(t.ctx, t.position + Vector3.up * 0.4f, true);
                    return false;
                }
                return true;
            }

            // ---------------- visual
            if (t.visual != null)
            {
                if (s.effect == TrapEffect.Snare) ProjectileVisuals.PoseSnare(t.visual, armed, t.ownerView, t.age);
                else ProjectileVisuals.PoseMine(t.visual, armed, t.triggeredAt >= 0f, t.ownerView, t.age);
            }

            switch ((TrapEvent)t.pendingEvent)
            {
                case TrapEvent.Exploded:
                    Detonate(t.ctx, t.position + Vector3.up * 0.4f, true);
                    return false;
                case TrapEvent.Snared:
                    Fx.Ring(t.position, s.color, 2.6f, 0.5f);
                    Fx.Sparks(t.position + Vector3.up * 0.4f, s.color, 14, 5f);
                    Fx.Puff(t.position + Vector3.up * 0.5f, Vector3.up, Art.WithAlpha(new Color(0.55f, 0.9f, 0.35f), 0.7f), 1.2f, 0.8f);
                    Sfx.PlayAt(Sound.Snare, t.position, 1f);
                    return false;
                case TrapEvent.Destroyed:
                    Fx.Poof(t.position + Vector3.up * 0.2f, s.color);
                    Fx.Sparks(t.position + Vector3.up * 0.2f, s.color, 8, 4f);
                    Sfx.PlayAt(Sound.Crate, t.position, 0.6f);
                    return false;
            }
            return true;
        }

        /// <summary>Host: inimigo mais próximo dentro do raio de deteção (as armadilhas de imobilização ignoram quem está imune).</summary>
        static PlayerNetwork FindTrapVictim(Trap t)
        {
            PlayerNetwork best = null;
            float bestDist = float.MaxValue;
            var owner = t.ctx.shooter;
            foreach (var pl in PlayerNetwork.All)
            {
                if (pl == null || !pl.IsSpawned || pl.Id == t.ctx.attackerId || pl.Health.IsDead) continue;
                if (owner != null && MatchManager.AreTeammates(owner, pl)) continue;
                if (!Within(t.position, pl.transform.position, t.settings.triggerRadius)) continue;
                if (t.settings.effect == TrapEffect.Snare && (pl.Status == null || !pl.Status.CanBeRooted || pl.Health.IsProtected)) continue;
                float d = (pl.transform.position - t.position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = pl;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ utilitários

        static bool IsShooter(AttackContext ctx, Collider col)
        {
            if (ctx.shooter == null) return false;
            var h = col.GetComponentInParent<PlayerHealth>();
            return h != null && h.NetworkObjectId == ctx.attackerId;
        }

        static Vector3 Muzzle(AttackContext ctx, int shotIndex)
        {
            if (ctx.shooter != null && ctx.shooter.Visuals != null) return ctx.shooter.Visuals.GetMuzzle(shotIndex);
            return ctx.origin;
        }

        static bool AnyEnemyWithin(Vector3 center, float radius, ulong ownerId)
        {
            foreach (var pl in PlayerNetwork.All)
            {
                if (pl == null || !pl.IsSpawned || pl.Id == ownerId || pl.Health.IsDead) continue;
                if (Within(center, pl.transform.position, radius)) return true;
            }
            return false;
        }

        static bool Within(Vector3 center, Vector3 feet, float radius)
        {
            var d = feet - center;
            if (d.y < -1.5f || d.y > 2.5f) return false;
            d.y = 0f;
            return d.sqrMagnitude <= radius * radius;
        }

        public static Vector3 GroundAt(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 0.6f, Vector3.down, out var hit, 30f, CombatMath.ShotMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerHealth>() == null)
                return hit.point;
            return point;
        }

        void SortHits(int count)
        {
            for (int i = 1; i < count; i++)
            {
                var key = m_Hits[i];
                int j = i - 1;
                while (j >= 0 && m_Hits[j].distance > key.distance)
                {
                    m_Hits[j + 1] = m_Hits[j];
                    j--;
                }
                m_Hits[j + 1] = key;
            }
        }

        void ReleaseProjectile(Projectile p)
        {
            ProjectileVisuals.Release(p.visual);
            p.visual = null;
            p.ctx = null;
            m_ProjectilePool.Push(p);
        }

        // ------------------------------------------------------------------ visuais de zonas (pooled)

        ZoneVisual TakeZone()
        {
            while (m_ZonePool.Count > 0)
            {
                var v = m_ZonePool.Pop();
                if (v == null || v.root == null) continue;
                v.root.gameObject.SetActive(true);
                return v;
            }
            var z = new ZoneVisual();
            z.root = new GameObject("Zone").transform;
            z.root.SetParent(transform, false);
            z.disc = Art.FxPart(PrimitiveType.Cylinder, z.root, Vector3.zero, new Vector3(1f, 0.005f, 1f), Color.white, false);
            z.discR = z.disc.GetComponent<Renderer>();
            z.ring = Art.FxQuad(z.root, Art.RingTexture, Color.white, true);
            z.ring.localPosition = Vector3.up * 0.02f;
            z.ringR = z.ring.GetComponent<Renderer>();
            z.edge = Art.FxQuad(z.root, Art.RingTexture, Color.white, false);
            z.edge.localPosition = Vector3.up * 0.01f;
            z.edgeR = z.edge.GetComponent<Renderer>();
            return z;
        }

        void ReleaseZone(ZoneVisual v)
        {
            if (v == null || v.root == null) return;
            v.root.gameObject.SetActive(false);
            m_ZonePool.Push(v);
        }

        void PoseZone(Zone z, float dt)
        {
            var v = z.visual;
            var s = z.settings;
            float grow = Mathf.Clamp01(z.age / 0.2f);
            float fade = Mathf.Clamp01((s.duration - z.age) / 0.5f);
            float d = s.radius * 2f * Mathf.Lerp(0.3f, 1f, grow) * (0.85f + 0.15f * fade);
            v.disc.localScale = new Vector3(d, 0.005f, d);
            v.edge.localScale = new Vector3(d * 1.02f, 1f, d * 1.02f);
            float pulse = 0.92f + 0.08f * Mathf.Sin(z.age * 6f);
            v.ring.localScale = new Vector3(d * pulse, 1f, d * pulse);
            v.ring.localRotation = Quaternion.Euler(0f, z.age * 70f, 0f);

            // Nuvem/partículas dentro da zona
            if (dt > 0f && z.age >= z.nextPuff)
            {
                z.nextPuff = z.age + 0.06f;
                var r = Random.insideUnitCircle * s.radius * 0.9f;
                var tint = s.secondaryColor.a > 0.01f && Random.value < 0.4f ? s.secondaryColor : s.color;
                var c = Art.WithAlpha(tint, 0.55f * fade);
                Fx.Puff(z.position + new Vector3(r.x, 0.2f, r.y), Vector3.up * Random.Range(0.4f, 1.2f), c, Random.Range(0.6f, 1.1f), 0.9f);
                if (Random.value < 0.5f)
                    Fx.Glow(z.position + new Vector3(-r.y, 0.1f, r.x), Vector3.up * Random.Range(1f, 2.2f), Color.Lerp(s.color, Color.white, 0.4f), 0.1f, 0.7f);
            }
        }
    }
}
