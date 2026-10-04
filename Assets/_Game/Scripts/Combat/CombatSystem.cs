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
        public const float HeadshotHeight = 1.45f;

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
            public Transform visual;
            public readonly List<PlayerHealth> pierced = new List<PlayerHealth>();
        }

        class Fuse
        {
            public AttackContext ctx;
            public Vector3 position;
            public float timer, total;
            public Transform visual;
        }

        class Zone
        {
            public AttackContext ctx;
            public ZoneSettings settings;
            public Vector3 position;
            public PlayerNetwork follow;
            public float age, nextTick;
            public Transform visual;
        }

        class Trap
        {
            public AttackContext ctx;
            public TrapSettings settings;
            public Vector3 position;
            public float age, triggeredAt = -1f;
            public bool ownerView;
            public Transform visual;
        }

        class Cone
        {
            public AttackContext ctx;
            public ConeVolumeDelivery delivery;
            public Vector3 visualOrigin;
            public float age, nextTick, nextPuff;
            public int puffCount;
        }

        class Fx
        {
            public Transform t;
            public float age, life, size;
            public Vector3 velocity;
            public bool grow;
            public Stack<Transform> pool;
        }

        readonly List<PendingAttack> m_Pending = new List<PendingAttack>();
        readonly List<Projectile> m_Projectiles = new List<Projectile>();
        readonly List<Fuse> m_Fuses = new List<Fuse>();
        readonly List<Zone> m_Zones = new List<Zone>();
        readonly List<Trap> m_Traps = new List<Trap>();
        readonly List<Cone> m_Cones = new List<Cone>();
        readonly List<Fx> m_Fx = new List<Fx>();

        readonly Stack<Transform> m_SpherePool = new Stack<Transform>();
        readonly Stack<Transform> m_CubePool = new Stack<Transform>();
        readonly Stack<Transform> m_DiskPool = new Stack<Transform>();
        readonly Stack<Projectile> m_ProjectilePool = new Stack<Projectile>();

        readonly RaycastHit[] m_Hits = new RaycastHit[32];
        MaterialPropertyBlock m_Block;

        void Awake()
        {
            Instance = this;
            m_Block = new MaterialPropertyBlock();
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
            foreach (var f in m_Fuses) Release(f.visual, m_CubePool);
            m_Fuses.Clear();
            foreach (var z in m_Zones) Release(z.visual, m_DiskPool);
            m_Zones.Clear();
            foreach (var t in m_Traps) Release(t.visual, m_DiskPool);
            m_Traps.Clear();
            m_Cones.Clear();
            foreach (var fx in m_Fx) Release(fx.t, fx.pool);
            m_Fx.Clear();
        }

        // ------------------------------------------------------------------ primitivas (usadas pelas deliveries)

        public void TraceHitscan(AttackContext ctx, Vector3 dir, float range, float radius, bool pierce, int shotIndex)
        {
            Vector3 origin = ctx.origin;
            Vector3 end = origin + dir * range;
            bool hitSomething = false;

            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, radius), dir, m_Hits, range, ~0, QueryTriggerInteraction.Ignore);
            SortHits(count);
            var pierced = new List<PlayerHealth>();

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                if (hit.collider == null || IsShooter(ctx, hit.collider)) continue;
                Vector3 point = hit.distance <= 0f ? origin : hit.point;
                var health = hit.collider.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    if (health.IsDead || pierced.Contains(health)) continue;
                    if (ctx.authoritative) EffectResolver.DirectHit(ctx, health, point, hit.distance);
                    else SpawnImpact(point, ctx.attack.visuals.color, 0.12f, 0.3f);
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
                SpawnTracer(Muzzle(ctx, shotIndex), end, ctx.attack.visuals.color, ctx.attack.visuals.scale);
                ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, shotIndex);
                if (hitSomething) SpawnImpact(end, ctx.attack.visuals.color, 0.15f, 0.3f);
            }
            if (hitSomething) Detonate(ctx, end, false);
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
            Vector3 start = ctx.authoritative ? ctx.origin : Muzzle(ctx, shotIndex);
            var velocity = d.Velocity(start, target, out float flight);
            if (!ctx.authoritative) ctx.shooter?.Visuals?.MuzzleFlash(ctx.attack.visuals.color, shotIndex);

            var p = NewProjectile(ctx, start, velocity);
            p.gravity = d.gravity;
            p.remaining = float.MaxValue;
            p.radius = d.radius;
            p.isArc = true;
            p.ignorePlayers = true;
            p.fuse = d.fuseDelay;
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

        public void SpawnTrap(AttackContext ctx, Vector3 position, TrapSettings settings)
        {
            if (m_Traps.Count > 48) return;
            var t = new Trap
            {
                ctx = ctx,
                settings = settings,
                position = GroundAt(position),
                ownerView = ctx.shooter != null && ctx.shooter.IsLocalHuman,
            };
            if (!ctx.authoritative)
            {
                t.visual = Take(m_DiskPool, PrimitiveType.Cylinder);
                bool discreet = settings.hiddenFromEnemies && !t.ownerView;
                float d = discreet ? 0.32f : 0.6f;
                t.visual.localScale = new Vector3(d, 0.06f, d);
                t.visual.position = t.position + Vector3.up * 0.04f;
                SetColor(t.visual, discreet ? settings.color * 0.3f : settings.color * 0.7f);
                SpawnImpact(t.position + Vector3.up * 0.2f, settings.color, 0.15f, 0.4f);
            }
            m_Traps.Add(t);
        }

        public void SpawnZone(AttackContext ctx, Vector3 position, ZoneSettings settings, PlayerNetwork follow)
        {
            if (m_Zones.Count > 48 || settings == null) return;
            var z = new Zone { ctx = ctx, settings = settings, position = position, follow = follow };
            if (!ctx.authoritative)
            {
                z.visual = Take(m_DiskPool, PrimitiveType.Cylinder);
                z.visual.localScale = new Vector3(settings.radius * 2f, 0.02f, settings.radius * 2f);
                z.visual.position = position + Vector3.up * 0.03f;
                SetColor(z.visual, settings.color * 0.45f);
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
                if (f.visual != null)
                {
                    float blink = Mathf.Sin((f.total - f.timer) * 30f) > 0f ? 1.6f : 0.7f;
                    SetColor(f.visual, f.ctx.attack.visuals.color * blink);
                }
                if (f.timer <= 0f)
                {
                    Detonate(f.ctx, f.position + Vector3.up * 0.3f, true);
                    Release(f.visual, m_CubePool);
                    m_Fuses.RemoveAt(i);
                }
            }

            for (int i = m_Cones.Count - 1; i >= 0; i--)
                if (!StepCone(m_Cones[i], dt)) m_Cones.RemoveAt(i);

            for (int i = m_Zones.Count - 1; i >= 0; i--)
            {
                if (!StepZone(m_Zones[i], dt))
                {
                    Release(m_Zones[i].visual, m_DiskPool);
                    m_Zones.RemoveAt(i);
                }
            }

            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                if (!StepTrap(m_Traps[i], dt))
                {
                    Release(m_Traps[i].visual, m_DiskPool);
                    m_Traps.RemoveAt(i);
                }
            }

            StepFx(dt);
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
                p.visual = Take(m_SpherePool, PrimitiveType.Sphere);
                float w = 0.13f * ctx.attack.visuals.scale;
                p.visual.localScale = new Vector3(w, w, Mathf.Max(w, 0.6f * Mathf.Min(1.6f, ctx.attack.visuals.scale)));
                p.visual.position = start;
                if (velocity.sqrMagnitude > 0.001f) p.visual.rotation = Quaternion.LookRotation(velocity);
                SetColor(p.visual, ctx.attack.visuals.color * 1.6f);
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

            int count = Physics.SphereCastNonAlloc(p.position, p.radius, dir, m_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
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

                    if (p.ctx.authoritative) EffectResolver.DirectHit(p.ctx, health, point, p.traveled + hit.distance);
                    else SpawnImpact(point, p.ctx.attack.visuals.color, 0.12f, 0.35f);

                    p.pierced.Add(health);
                    if (p.pierce) continue;

                    Impact(p, point, null);
                    return false;
                }

                if (p.ctx.authoritative) EffectResolver.SurfaceHit(p.ctx, col);
                Impact(p, point, col);
                return false;
            }

            p.position += disp;
            p.traveled += dist;
            if (!p.isArc) p.remaining -= dist;

            if (p.visual != null)
            {
                p.visual.position = p.position;
                if (p.velocity.sqrMagnitude > 0.001f) p.visual.rotation = Quaternion.LookRotation(p.velocity);
            }

            bool alive = (p.isArc || p.remaining > 0f) && p.life < p.maxLife && p.position.y > -30f;
            if (!alive && p.isArc) Impact(p, p.position, null);
            return alive;
        }

        void Impact(Projectile p, Vector3 point, Collider surface)
        {
            if (p.isArc && p.fuse > 0f)
            {
                var f = new Fuse { ctx = p.ctx, position = GroundAt(point), timer = p.fuse, total = p.fuse };
                if (!p.ctx.authoritative)
                {
                    f.visual = Take(m_CubePool, PrimitiveType.Cube);
                    float s = 0.12f * p.ctx.attack.visuals.scale;
                    f.visual.localScale = new Vector3(s, s * 2.2f, s);
                    f.visual.position = f.position + Vector3.up * s;
                    f.visual.rotation = Quaternion.Euler(0f, p.life * 400f, 75f);
                }
                m_Fuses.Add(f);
                return;
            }

            if (!p.ctx.attack.effect.HasSplash && surface != null && !p.ctx.authoritative)
                SpawnImpact(point, p.ctx.attack.visuals.color, 0.18f, 0.35f);

            Detonate(p.ctx, point, false);
        }

        /// <summary>Explosão (se o efeito tiver splash) e zona no impacto (se configurada).</summary>
        void Detonate(AttackContext ctx, Vector3 point, bool always)
        {
            var e = ctx.attack.effect;
            if (e.HasSplash)
            {
                if (ctx.authoritative) EffectResolver.Explosion(ctx, point);
                else
                {
                    SpawnImpact(point, ctx.attack.visuals.color, 0.32f, e.splashRadius * 1.5f);
                    Sfx.PlayAt(ctx.attack.visuals.impactSound, point, 1f);
                }
            }
            else if (always && !ctx.authoritative)
            {
                SpawnImpact(point, ctx.attack.visuals.color, 0.2f, 0.6f);
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
                c.nextPuff += 0.035f;
                var rng = ctx.RngFor(1000 + c.puffCount++);
                float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * halfAngle;
                float pitch = (float)(rng.NextDouble() * 2.0 - 1.0) * halfAngle * 0.5f;
                var dir = Quaternion.LookRotation(ctx.direction) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                bool local = ctx.shooter != null && ctx.shooter.IsLocalHuman;
                var start = c.visualOrigin + ctx.direction * (local ? 0.9f : 0.2f);
                float speed = d.range / Mathf.Max(0.05f, d.expandTime + 0.15f);
                SpawnPuff(start, dir * speed, ctx.attack.visuals.color, d.expandTime + 0.25f, 0.35f * ctx.attack.visuals.scale);
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
                z.visual.position = z.position + Vector3.up * 0.03f;
                float fade = Mathf.Clamp01((s.duration - z.age) / 0.5f);
                float d = s.radius * 2f * (0.85f + 0.15f * fade);
                z.visual.localScale = new Vector3(d, 0.02f, d);
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

        bool StepTrap(Trap t, float dt)
        {
            var s = t.settings;
            t.age += dt;
            if (t.age > s.lifetime || t.ctx.shooter == null) return false; // expirou ou o dono saiu

            bool armed = t.age >= s.armTime;
            if (armed && t.triggeredAt < 0f && AnyEnemyWithin(t.position, s.triggerRadius, t.ctx.attackerId))
            {
                t.triggeredAt = t.age;
                if (!t.ctx.authoritative) Sfx.PlayAt(Sound.MineArm, t.position, 1f);
            }

            if (t.visual != null)
            {
                if (t.triggeredAt >= 0f)
                {
                    // Aviso: pisca e cresce
                    float k = (t.age - t.triggeredAt) / Mathf.Max(0.05f, s.warningTime);
                    float d = Mathf.Lerp(0.6f, 1.2f, k);
                    t.visual.localScale = new Vector3(d, 0.08f, d);
                    SetColor(t.visual, Mathf.Sin(t.age * 50f) > 0f ? new Color(1f, 0.2f, 0.15f) : s.color);
                }
                else if (armed && t.ownerView)
                {
                    float pulse = 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(t.age * 4f));
                    SetColor(t.visual, s.color * pulse);
                }
            }

            if (t.triggeredAt >= 0f && t.age >= t.triggeredAt + s.warningTime)
            {
                Detonate(t.ctx, t.position + Vector3.up * 0.4f, true);
                return false;
            }
            return true;
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
            if (Physics.Raycast(point + Vector3.up * 0.6f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
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

        // ------------------------------------------------------------------ visuais (pooled)

        Transform Take(Stack<Transform> pool, PrimitiveType type)
        {
            while (pool.Count > 0)
            {
                var t = pool.Pop();
                if (t == null) continue;
                t.gameObject.SetActive(true);
                return t;
            }
            var go = GameObject.CreatePrimitive(type);
            go.name = "FX_" + type;
            var c = go.GetComponent<Collider>();
            c.enabled = false;
            Destroy(c);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        static void Release(Transform t, Stack<Transform> pool)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            pool.Push(t);
        }

        void ReleaseProjectile(Projectile p)
        {
            Release(p.visual, m_SpherePool);
            p.visual = null;
            p.ctx = null;
            m_ProjectilePool.Push(p);
        }

        void SpawnImpact(Vector3 point, Color color, float life, float size)
        {
            if (m_Fx.Count > 120) return;
            var t = Take(m_CubePool, PrimitiveType.Cube);
            t.position = point;
            t.rotation = Random.rotation;
            t.localScale = Vector3.one * size;
            SetColor(t, color);
            m_Fx.Add(new Fx { t = t, life = life, size = size, pool = m_CubePool });
        }

        void SpawnPuff(Vector3 start, Vector3 velocity, Color color, float life, float size)
        {
            if (m_Fx.Count > 120) return;
            var t = Take(m_SpherePool, PrimitiveType.Sphere);
            t.position = start;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one * size * 0.4f;
            SetColor(t, color * 0.9f);
            m_Fx.Add(new Fx { t = t, life = life, size = size, velocity = velocity, grow = true, pool = m_SpherePool });
        }

        void SpawnTracer(Vector3 from, Vector3 to, Color color, float scale)
        {
            if (m_Fx.Count > 120) return;
            var t = Take(m_CubePool, PrimitiveType.Cube);
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) { Release(t, m_CubePool); return; }
            t.position = from + d * 0.5f;
            t.rotation = Quaternion.LookRotation(d);
            float w = 0.025f * Mathf.Max(0.5f, scale);
            t.localScale = new Vector3(w, w, len);
            SetColor(t, color * 1.8f);
            m_Fx.Add(new Fx { t = t, life = 0.07f, size = -1f, pool = m_CubePool });
        }

        void StepFx(float dt)
        {
            for (int i = m_Fx.Count - 1; i >= 0; i--)
            {
                var fx = m_Fx[i];
                fx.age += dt;
                if (fx.age >= fx.life || fx.t == null)
                {
                    Release(fx.t, fx.pool);
                    m_Fx.RemoveAt(i);
                    continue;
                }
                float k = fx.age / fx.life;
                if (fx.velocity != Vector3.zero)
                {
                    fx.t.position += fx.velocity * dt;
                    fx.velocity *= 1f - Mathf.Clamp01(dt * 2.5f);
                }
                if (fx.size > 0f)
                {
                    float s = fx.grow ? Mathf.Lerp(fx.size * 0.4f, fx.size * 2.2f, k) * (1f - k * 0.3f) : fx.size * (1f - k) + 0.05f;
                    fx.t.localScale = Vector3.one * s;
                }
            }
        }

        void SetColor(Transform t, Color c)
        {
            var r = t.GetComponent<Renderer>();
            r.GetPropertyBlock(m_Block);
            m_Block.SetColor("_Color", c);
            m_Block.SetColor("_BaseColor", c);
            m_Block.SetColor("_EmissionColor", c);
            r.SetPropertyBlock(m_Block);
        }
    }
}
