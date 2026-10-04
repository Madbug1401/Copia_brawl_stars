using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// [DEPRECADO — substituído pelo CombatSystem na Fase 2. Mantido até à validação; não é usado.]
    /// Simula todos os ataques sem NetworkObjects (barato e simples em LAN):
    /// - instâncias "autoritativas" só existem no servidor e causam dano (invisíveis);
    /// - instâncias "visuais" existem em todas as máquinas e só servem para mostrar.
    /// Suporta: projéteis retos, rajadas, leques, granadas em arco, explosões, estilhaços, zonas, minas e auras.
    /// </summary>
    public class ProjectileSystem : MonoBehaviour
    {
        public static ProjectileSystem Instance { get; private set; }

        public float headshotHeight = 1.45f;
        public float headshotMultiplier = 1.5f;

        class PendingAttack
        {
            public PlayerNetwork shooter;
            public AttackSpec spec;
            public bool isSuper;
            public bool authoritative;
            public int fired;
            public float nextTime;
        }

        class Projectile
        {
            public Vector3 position;
            public Vector3 velocity;
            public float gravity;
            public float remaining;
            public float life;
            public AttackSpec spec;
            public ulong shooterId;
            public PlayerNetwork shooter;
            public bool authoritative;
            public bool isSuper;
            public Transform visual;
            public readonly List<PlayerHealth> pierced = new List<PlayerHealth>();
        }

        class Zone
        {
            public ZoneSpec spec;
            public Vector3 position;
            public PlayerNetwork follow;
            public ulong ownerId;
            public PlayerNetwork owner;
            public bool authoritative;
            public bool isSuper;
            public float age;
            public float nextTick;
            public bool armedSoundPlayed;
            public Transform visual;
        }

        struct ImpactFx
        {
            public Transform t;
            public float age;
            public float life;
            public float size;
        }

        readonly List<PendingAttack> m_Pending = new List<PendingAttack>();
        readonly List<Projectile> m_Live = new List<Projectile>();
        readonly List<Zone> m_Zones = new List<Zone>();
        readonly List<ImpactFx> m_Impacts = new List<ImpactFx>();

        readonly Stack<Transform> m_VisualPool = new Stack<Transform>();
        readonly Stack<Transform> m_DiskPool = new Stack<Transform>();
        readonly Stack<Transform> m_ImpactPool = new Stack<Transform>();
        readonly Stack<Projectile> m_ProjectilePool = new Stack<Projectile>();

        readonly RaycastHit[] m_Hits = new RaycastHit[24];
        readonly Collider[] m_Overlap = new Collider[32];
        readonly List<PlayerHealth> m_Victims = new List<PlayerHealth>();
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

        // =====================================================================================
        // API
        // =====================================================================================

        /// <summary>Inicia um ataque. authoritative=true só deve ser usado no servidor.</summary>
        public void StartAttack(PlayerNetwork shooter, AttackSpec spec, bool isSuper, bool authoritative)
        {
            if (shooter == null || spec == null) return;

            if (!authoritative)
                Sfx.PlayAt(spec.sound, shooter.Motor.EyePosition, shooter.IsLocalHuman ? 0.7f : 1f);

            switch (spec.kind)
            {
                case AttackKind.Mines:
                    PlaceMines(shooter, spec, isSuper, authoritative);
                    break;

                case AttackKind.Aura:
                    if (spec.zone != null)
                        CreateZone(spec.zone, shooter.transform.position, shooter, shooter, isSuper, authoritative);
                    break;

                default:
                    m_Pending.Add(new PendingAttack
                    {
                        shooter = shooter,
                        spec = spec,
                        isSuper = isSuper,
                        authoritative = authoritative,
                        fired = 0,
                        nextTime = Time.time
                    });
                    break;
            }
        }

        public void ClearAll()
        {
            m_Pending.Clear();
            for (int i = m_Live.Count - 1; i >= 0; i--) ReleaseProjectile(m_Live[i]);
            m_Live.Clear();
            for (int i = m_Zones.Count - 1; i >= 0; i--) ReleaseZone(m_Zones[i]);
            m_Zones.Clear();
        }

        // =====================================================================================
        // Update
        // =====================================================================================

        void Update()
        {
            float now = Time.time;
            float dt = Time.deltaTime;

            // 1) Rajadas pendentes
            for (int i = m_Pending.Count - 1; i >= 0; i--)
            {
                var a = m_Pending[i];
                if (a.shooter == null || !a.shooter.IsSpawned || a.shooter.Health.IsDead)
                {
                    m_Pending.RemoveAt(i);
                    continue;
                }

                bool simultaneous = a.spec.interval <= 0f;
                while (a.fired < a.spec.projectiles && (simultaneous || now >= a.nextTime))
                {
                    SpawnShot(a, a.fired);
                    a.fired++;
                    a.nextTime += a.spec.interval;
                }

                if (a.fired >= a.spec.projectiles) m_Pending.RemoveAt(i);
            }

            // 2) Projéteis
            if (m_Live.Count > 0)
            {
                Physics.SyncTransforms();
                for (int i = m_Live.Count - 1; i >= 0; i--)
                {
                    var p = m_Live[i];
                    if (!Step(p, dt))
                    {
                        ReleaseProjectile(p);
                        m_Live.RemoveAt(i);
                    }
                }
            }

            // 3) Zonas / minas / auras
            for (int i = m_Zones.Count - 1; i >= 0; i--)
            {
                var z = m_Zones[i];
                if (!UpdateZone(z, dt))
                {
                    ReleaseZone(z);
                    m_Zones.RemoveAt(i);
                }
            }

            // 4) Efeitos de impacto
            for (int i = m_Impacts.Count - 1; i >= 0; i--)
            {
                var fx = m_Impacts[i];
                fx.age += dt;
                if (fx.age >= fx.life)
                {
                    fx.t.gameObject.SetActive(false);
                    m_ImpactPool.Push(fx.t);
                    m_Impacts.RemoveAt(i);
                    continue;
                }
                float k = 1f - fx.age / fx.life;
                fx.t.localScale = Vector3.one * (fx.size * k + 0.05f);
                m_Impacts[i] = fx;
            }
        }

        // =====================================================================================
        // Projéteis
        // =====================================================================================

        void SpawnShot(PendingAttack a, int index)
        {
            var spec = a.spec;
            var shooter = a.shooter;
            Vector3 origin = shooter.Motor.EyePosition;

            Vector3 velocity;
            float gravity = 0f;
            float remaining = spec.range;
            Vector3 start = origin;

            if (spec.kind == AttackKind.Lob)
            {
                var target = CombatMath.AimPoint(shooter, spec.range);
                if (!a.authoritative && shooter.Visuals != null) start = shooter.Visuals.MuzzlePosition;
                velocity = CombatMath.LobVelocity(start, target, spec, out _);
                gravity = spec.gravity;
                remaining = spec.range * 4f; // o arco é mais comprido que a distância
            }
            else
            {
                Vector3 aim = shooter.Motor.AimDirection;
                float yawOffset = spec.spreadDegrees * Mathf.Sin(index * 2.399f);
                float pitchOffset = spec.spreadDegrees * 0.5f * Mathf.Cos(index * 1.7f);
                if (spec.fanDegrees > 0f && spec.projectiles > 1)
                    yawOffset += Mathf.Lerp(-spec.fanDegrees * 0.5f, spec.fanDegrees * 0.5f, index / (float)(spec.projectiles - 1));

                var rot = Quaternion.LookRotation(aim) * Quaternion.Euler(pitchOffset, yawOffset, 0f);
                Vector3 dir = rot * Vector3.forward;
                velocity = dir * spec.speed;

                if (!a.authoritative && shooter.Visuals != null)
                {
                    // O visual sai da arma e converge para a trajetória real
                    Vector3 muzzle = shooter.Visuals.MuzzlePosition;
                    Vector3 target = origin + dir * Mathf.Min(spec.range, 20f);
                    start = muzzle;
                    velocity = (target - muzzle).normalized * spec.speed;
                }
            }

            var p = NewProjectile(spec, shooter, a.isSuper, a.authoritative, start, velocity, gravity, remaining);
            if (!a.authoritative) shooter.Visuals?.MuzzleFlash(spec.color);
            m_Live.Add(p);
        }

        Projectile NewProjectile(AttackSpec spec, PlayerNetwork shooter, bool isSuper, bool authoritative,
            Vector3 position, Vector3 velocity, float gravity, float remaining)
        {
            var p = m_ProjectilePool.Count > 0 ? m_ProjectilePool.Pop() : new Projectile();
            p.position = position;
            p.velocity = velocity;
            p.gravity = gravity;
            p.remaining = remaining;
            p.life = 0f;
            p.spec = spec;
            p.shooter = shooter;
            p.shooterId = shooter != null ? shooter.Id : ulong.MaxValue;
            p.authoritative = authoritative;
            p.isSuper = isSuper;
            p.pierced.Clear();
            p.visual = null;

            if (!authoritative)
            {
                p.visual = GetVisual(spec, isSuper);
                p.visual.position = position;
                if (velocity.sqrMagnitude > 0.001f) p.visual.rotation = Quaternion.LookRotation(velocity);
            }
            return p;
        }

        /// <summary>Avança um projétil. Devolve false quando termina.</summary>
        bool Step(Projectile p, float dt)
        {
            p.life += dt;
            Vector3 g = Vector3.down * p.gravity;
            Vector3 disp = p.velocity * dt + 0.5f * g * dt * dt;
            p.velocity += g * dt;

            float dist = disp.magnitude;
            if (p.gravity <= 0f && dist > p.remaining)
            {
                disp = disp / dist * p.remaining;
                dist = p.remaining;
            }
            if (dist <= 0.0001f) return false;
            Vector3 dir = disp / disp.magnitude;

            int count = Physics.SphereCastNonAlloc(p.position, p.spec.radius, dir, m_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
            SortHits(count);

            for (int i = 0; i < count; i++)
            {
                var hit = m_Hits[i];
                var col = hit.collider;
                if (col == null) continue;

                Vector3 point = hit.distance <= 0f ? p.position : hit.point;
                var health = col.GetComponentInParent<PlayerHealth>();

                if (health != null)
                {
                    if (health.NetworkObjectId == p.shooterId) continue;
                    if (health.IsDead) continue;
                    if (p.pierced.Contains(health)) continue;

                    if (p.authoritative && p.spec.damage > 0)
                    {
                        bool headshot = point.y - health.transform.position.y >= headshotHeight;
                        int dmg = Mathf.RoundToInt(p.spec.damage * (headshot ? headshotMultiplier : 1f));
                        ServerDamage(health, dmg, p.shooterId, p.shooter, p.isSuper, headshot);
                    }
                    else if (!p.authoritative)
                    {
                        SpawnImpact(point, p.spec.color, 0.12f, 0.35f);
                    }

                    if (p.spec.pierce)
                    {
                        p.pierced.Add(health);
                        continue;
                    }

                    Detonate(p, point);
                    return false;
                }

                // Cenário
                if (!p.authoritative && !IsExplosive(p.spec))
                    SpawnImpact(point, p.spec.color, 0.18f, 0.35f);
                Detonate(p, point);
                return false;
            }

            p.position += disp;
            p.remaining -= dist;

            if (p.visual != null)
            {
                p.visual.position = p.position;
                if (p.velocity.sqrMagnitude > 0.001f) p.visual.rotation = Quaternion.LookRotation(p.velocity);
            }

            bool alive = p.remaining > 0f && p.life < 4f && p.position.y > -30f;
            if (!alive && p.spec.kind == AttackKind.Lob) Detonate(p, p.position); // granada no fim do alcance
            return alive;
        }

        static bool IsExplosive(AttackSpec s) => s.explosionRadius > 0f || s.splitCount > 0 || s.zone != null;

        /// <summary>Explosão, estilhaços e/ou zona no ponto de impacto.</summary>
        void Detonate(Projectile p, Vector3 point)
        {
            var spec = p.spec;
            if (!IsExplosive(spec)) return;

            if (spec.explosionRadius > 0f)
            {
                if (p.authoritative)
                    ServerExplosion(point, spec.explosionRadius, spec.explosionDamage, p.shooterId, p.shooter, p.isSuper, null);
                else
                {
                    SpawnImpact(point, spec.color, 0.3f, spec.explosionRadius * 1.6f);
                    Sfx.PlayAt(Sound.Explosion, point, 0.9f);
                }
            }

            if (spec.split != null && spec.splitCount > 0)
            {
                var from = point + Vector3.up * 0.35f;
                for (int i = 0; i < spec.splitCount; i++)
                {
                    float ang = i * Mathf.PI * 2f / spec.splitCount;
                    var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    var child = NewProjectile(spec.split, p.shooter, p.isSuper, p.authoritative, from, dir * spec.split.speed, 0f, spec.split.range);
                    child.shooterId = p.shooterId;
                    m_Live.Add(child);
                }
            }

            if (spec.zone != null)
            {
                var ground = GroundAt(point);
                CreateZone(spec.zone, ground, null, p.shooter, p.isSuper, p.authoritative, p.shooterId);
            }
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

        Vector3 GroundAt(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 0.6f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<PlayerHealth>() == null)
                return hit.point;
            return point;
        }

        // =====================================================================================
        // Dano (servidor)
        // =====================================================================================

        void ServerDamage(PlayerHealth victim, int dmg, ulong attackerId, PlayerNetwork attacker, bool fromSuper, bool headshot)
        {
            if (victim == null || dmg <= 0) return;
            bool killed = victim.ServerApplyDamage(dmg, attackerId);
            if (attacker == null) attacker = PlayerNetwork.Find(attackerId);
            if (attacker != null && attacker.IsSpawned)
                attacker.Combat.ServerOnDealtDamage(dmg, fromSuper, headshot, killed);
        }

        void ServerExplosion(Vector3 center, float radius, int damage, ulong attackerId, PlayerNetwork attacker, bool fromSuper, ZoneSpec slowFrom)
        {
            m_Victims.Clear();
            int n = Physics.OverlapSphereNonAlloc(center, radius, m_Overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = m_Overlap[i].GetComponentInParent<PlayerHealth>();
                if (h == null || h.IsDead || h.NetworkObjectId == attackerId || m_Victims.Contains(h)) continue;
                m_Victims.Add(h);
            }
            foreach (var h in m_Victims)
            {
                ServerDamage(h, damage, attackerId, attacker, fromSuper, false);
                if (slowFrom != null) h.ServerApplySlow(slowFrom.slowFactor, slowFrom.slowDuration);
            }
        }

        // =====================================================================================
        // Zonas, minas, auras
        // =====================================================================================

        void PlaceMines(PlayerNetwork shooter, AttackSpec spec, bool isSuper, bool authoritative)
        {
            if (spec.zone == null) return;
            var center = GroundAt(CombatMath.AimPoint(shooter, spec.range));
            float yaw = shooter.Motor.Yaw * Mathf.Deg2Rad;
            for (int i = 0; i < spec.mineCount; i++)
            {
                float ang = yaw + i * Mathf.PI * 2f / Mathf.Max(1, spec.mineCount);
                var offset = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (spec.mineCount > 1 ? spec.mineSpread : 0f);
                var pos = GroundAt(center + offset);
                CreateZone(spec.zone, pos, null, shooter, isSuper, authoritative);
                if (!authoritative) SpawnImpact(pos + Vector3.up * 0.2f, spec.color, 0.15f, 0.5f);
            }
        }

        void CreateZone(ZoneSpec spec, Vector3 position, PlayerNetwork follow, PlayerNetwork owner, bool isSuper, bool authoritative, ulong ownerIdOverride = ulong.MaxValue)
        {
            if (m_Zones.Count > 64) return;

            var z = new Zone
            {
                spec = spec,
                position = position,
                follow = follow,
                owner = owner,
                ownerId = ownerIdOverride != ulong.MaxValue ? ownerIdOverride : (owner != null ? owner.Id : ulong.MaxValue),
                authoritative = authoritative,
                isSuper = isSuper,
                age = 0f,
                nextTick = 0f,
            };

            if (!authoritative)
            {
                z.visual = GetDisk();
                float d = spec.isMine ? 0.55f : spec.radius * 2f;
                z.visual.localScale = new Vector3(d, spec.isMine ? 0.08f : 0.02f, d);
                SetColor(z.visual.GetComponent<Renderer>(), spec.isMine ? spec.color * 0.6f : spec.color * 0.45f);
                z.visual.position = position + Vector3.up * (spec.isMine ? 0.06f : 0.03f);
            }

            m_Zones.Add(z);
        }

        bool UpdateZone(Zone z, float dt)
        {
            var spec = z.spec;
            z.age += dt;

            if (z.follow != null)
            {
                if (!z.follow.IsSpawned || z.follow.Health.IsDead) return false;
                z.position = z.follow.transform.position;
            }

            if (z.visual != null)
            {
                z.visual.position = z.position + Vector3.up * (spec.isMine ? 0.06f : 0.03f);
                if (spec.isMine && z.age >= spec.armTime)
                {
                    float pulse = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(z.age * 6f));
                    SetColor(z.visual.GetComponent<Renderer>(), spec.color * (0.8f + pulse));
                    if (!z.armedSoundPlayed)
                    {
                        z.armedSoundPlayed = true;
                        Sfx.PlayAt(Sound.MineArm, z.position, 0.6f);
                    }
                }
                else if (!spec.isMine)
                {
                    float fade = Mathf.Clamp01((spec.duration - z.age) / 0.5f);
                    z.visual.localScale = new Vector3(spec.radius * 2f * (0.85f + 0.15f * fade), 0.02f, spec.radius * 2f * (0.85f + 0.15f * fade));
                }
            }

            if (spec.isMine)
            {
                if (z.age >= spec.armTime && AnyEnemyWithin(z, spec.triggerRadius))
                {
                    if (z.authoritative)
                        ServerExplosion(z.position + Vector3.up * 0.5f, spec.mineExplosionRadius, spec.mineDamage, z.ownerId, z.owner, z.isSuper, null);
                    else
                    {
                        SpawnImpact(z.position + Vector3.up * 0.5f, spec.color, 0.35f, spec.mineExplosionRadius * 1.5f);
                        Sfx.PlayAt(Sound.Explosion, z.position, 1f);
                    }
                    return false;
                }
                return z.age < spec.duration;
            }

            // Zona de área: dano + abrandamento por tick
            if (z.authoritative && z.age >= z.nextTick)
            {
                z.nextTick += Mathf.Max(0.1f, spec.tickInterval);
                foreach (var pl in PlayerNetwork.All)
                {
                    if (pl == null || !pl.IsSpawned || pl.Id == z.ownerId || pl.Health.IsDead) continue;
                    if (!Within(z.position, pl.transform.position, spec.radius)) continue;
                    ServerDamage(pl.Health, spec.tickDamage, z.ownerId, z.owner, z.isSuper, false);
                    pl.Health.ServerApplySlow(spec.slowFactor, spec.slowDuration);
                }
            }

            return z.age < spec.duration;
        }

        bool AnyEnemyWithin(Zone z, float radius)
        {
            foreach (var pl in PlayerNetwork.All)
            {
                if (pl == null || !pl.IsSpawned || pl.Id == z.ownerId || pl.Health.IsDead) continue;
                if (Within(z.position, pl.transform.position, radius)) return true;
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

        // =====================================================================================
        // Visuais
        // =====================================================================================

        Transform GetVisual(AttackSpec spec, bool isSuper)
        {
            Transform t;
            if (m_VisualPool.Count > 0)
            {
                t = m_VisualPool.Pop();
                t.gameObject.SetActive(true);
            }
            else
            {
                t = MakePrimitive(PrimitiveType.Sphere, "Projectile");
            }

            float w = 0.13f * spec.visualScale;
            float len = spec.kind == AttackKind.Lob ? w : (isSuper ? 1.1f : 0.7f);
            t.localScale = new Vector3(w, w, Mathf.Max(w, len));
            SetColor(t.GetComponent<Renderer>(), spec.color * 1.6f);
            return t;
        }

        Transform GetDisk()
        {
            if (m_DiskPool.Count > 0)
            {
                var d = m_DiskPool.Pop();
                d.gameObject.SetActive(true);
                return d;
            }
            return MakePrimitive(PrimitiveType.Cylinder, "Zone");
        }

        Transform MakePrimitive(PrimitiveType type, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var c = go.GetComponent<Collider>();
            c.enabled = false;
            Destroy(c);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        void ReleaseProjectile(Projectile p)
        {
            if (p.visual != null)
            {
                p.visual.gameObject.SetActive(false);
                m_VisualPool.Push(p.visual);
                p.visual = null;
            }
            p.shooter = null;
            p.spec = null;
            m_ProjectilePool.Push(p);
        }

        void ReleaseZone(Zone z)
        {
            if (z.visual != null)
            {
                z.visual.gameObject.SetActive(false);
                m_DiskPool.Push(z.visual);
                z.visual = null;
            }
        }

        void SpawnImpact(Vector3 point, Color color, float life, float size)
        {
            if (m_Impacts.Count > 80) return;

            Transform t;
            if (m_ImpactPool.Count > 0)
            {
                t = m_ImpactPool.Pop();
                t.gameObject.SetActive(true);
            }
            else
            {
                t = MakePrimitive(PrimitiveType.Cube, "Impact");
            }

            t.position = point;
            t.rotation = Random.rotation;
            t.localScale = Vector3.one * size;
            SetColor(t.GetComponent<Renderer>(), color);
            m_Impacts.Add(new ImpactFx { t = t, age = 0f, life = life, size = size });
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(m_Block);
            m_Block.SetColor("_Color", c);
            m_Block.SetColor("_BaseColor", c);
            m_Block.SetColor("_EmissionColor", c);
            r.SetPropertyBlock(m_Block);
        }
    }
}
