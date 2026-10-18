using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Eventos de uma armadilha decididos no host e enviados a todas as máquinas.</summary>
    public enum TrapEvent : byte
    {
        Triggered,
        Detonated,
        Sprung,
        Expired,
        Destroyed,
    }

    /// <summary>
    /// Infraestrutura de combate partilhada por todos os Brawlers (substitui o ProjectileSystem).
    ///
    /// Cada ataque é simulado duas vezes, a partir da MESMA intenção (origem, direção, ponto-alvo, seed):
    /// - instância autoritativa (só no host, invisível): é a única que aplica dano/estados via EffectResolver;
    /// - instância visual (todas as máquinas): projéteis, tracers, sprays, zonas, minas e explosões.
    ///
    /// As armadilhas são a exceção: as posições, o disparo, a explosão e a remoção são decididos no host e
    /// enviados a todos (PlayerCombat.TrapPlacedRpc/TrapEventRpc), para que sejam objetos sincronizados.
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
            public int shotIndex;
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
            public bool authoritative;
            public int seed, index;
            public ulong ownerId;
            public float spawnedAt;
            public ProjVisual visual;
        }

        class Cone
        {
            public AttackContext ctx;
            public ConeVolumeDelivery delivery;
            public float age, nextTick;
            public int ticksDone;
            public SprayConeVisual visual;
            public readonly List<ulong> hitIds = new List<ulong>();
            public readonly List<int> hitCounts = new List<int>();
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
        readonly List<Trap> m_TrapScratch = new List<Trap>();
        readonly List<Cone> m_Cones = new List<Cone>();
        readonly Stack<SprayConeVisual> m_SprayPool = new Stack<SprayConeVisual>();

        readonly Stack<ZoneVisual> m_ZonePool = new Stack<ZoneVisual>();
        readonly Stack<Projectile> m_ProjectilePool = new Stack<Projectile>();

        readonly RaycastHit[] m_Hits = new RaycastHit[32];
        readonly List<PlayerHealth> m_HitscanPierced = new List<PlayerHealth>();
        static readonly RaycastHit[] s_GroundHits = new RaycastHit[16];
        static readonly Collider[] s_Overlap = new Collider[16];

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
            foreach (var t in m_Traps) ReleaseTrapVisual(t);
            m_Traps.Clear();
            foreach (var cone in m_Cones) ReleaseSpray(cone);
            m_Cones.Clear();
        }

        /// <summary>Armadilhas ativas (visuais e autoritativas) — usado por testes e UI.</summary>
        public int TrapCount(ulong ownerId, bool authoritative)
        {
            int n = 0;
            foreach (var t in m_Traps) if (t.ownerId == ownerId && t.authoritative == authoritative) n++;
            return n;
        }

        // ------------------------------------------------------------------ primitivas (usadas pelas deliveries)

        public void TraceHitscan(AttackContext ctx, Vector3 dir, float range, float radius, bool pierce, int shotIndex)
        {
            Vector3 origin = ctx.origin;
            Vector3 end = origin + dir * range;
            bool hitSomething = false;
            var color = ctx.VisualColor;

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
                    if (ctx.authoritative) EffectResolver.DirectHit(ctx, health, point, hit.distance, shotIndex, origin, dir);
                    else Fx.HitStar(point, color, 0.9f);
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
                Fx.TracerLine(from, end, color, w, 0.14f);
                Fx.TracerLine(from, end, Color.white, w * 0.35f, 0.08f);
                ctx.shooter?.Visuals?.MuzzleFlash(color, shotIndex);
                if (hitSomething) Fx.Impact(end, color, 1.2f);
            }
            if (hitSomething) Detonate(ctx, end, false);
        }

        /// <summary>Facada autoritativa: o primeiro jogador ou obstáculo bloqueia o golpe. Também destrói armadilhas inimigas.</summary>
        public void TraceMelee(PlayerNetwork attacker, Vector3 origin, Vector3 direction, MeleeProfile profile)
        {
            if (attacker == null || profile == null || !attacker.IsServer) return;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : attacker.Motor.AimDirection;
            float reach = Mathf.Max(0.5f, profile.range);
            ServerDestroyTrapsNearSegment(origin, direction, reach + 0.6f, Mathf.Max(0.8f, profile.radius * 3f), attacker.Id, true);

            int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.05f, profile.radius), direction,
                m_Hits, reach, CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
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
        }

        public void LaunchProjectile(AttackContext ctx, Vector3 dir, float speed, float range, float radius, bool pierce, int shotIndex, float gravity = 0f)
        {
            Vector3 start = ctx.origin;
            Vector3 velocity = dir * speed;

            if (!ctx.authoritative)
            {
                // O visual sai do cano e converge para a trajetória real
                start = Muzzle(ctx, shotIndex);
                var converge = ctx.origin + dir * Mathf.Min(range, 20f);
                velocity = (converge - start).normalized * speed;
                ctx.shooter?.Visuals?.MuzzleFlash(ctx.VisualColor, shotIndex);
            }

            var p = NewProjectile(ctx, start, velocity);
            p.remaining = range;
            p.radius = radius;
            p.pierce = pierce;
            p.gravity = Mathf.Max(0f, gravity);
            p.shotIndex = shotIndex;
            p.maxLife = range / Mathf.Max(1f, speed) + 0.5f;
            m_Projectiles.Add(p);
        }

        public void LaunchArc(AttackContext ctx, ArcProjectileDelivery d, Vector3 target, int shotIndex)
        {
            Vector3 start = ctx.authoritative ? ctx.origin : Muzzle(ctx, shotIndex);
            if (!ctx.authoritative) ctx.shooter?.Visuals?.MuzzleFlash(ctx.VisualColor, shotIndex);
            LaunchArcFrom(ctx, start, target, d.gravity, d.speed, d.minFlightTime, d.maxFlightTime, d.radius, d.fuseDelay, d.hitsPlayers, shotIndex);
        }

        /// <summary>Projétil em arco genérico (passa por cima de jogadores, a não ser que hitsPlayers; explode ao aterrar ou após o fuse).</summary>
        public void LaunchArcFrom(AttackContext ctx, Vector3 start, Vector3 target, float gravity, float horizontalSpeed, float minTime, float maxTime, float radius, float fuse,
            bool hitsPlayers = false, int shotIndex = 0)
        {
            var velocity = CombatMath.ArcVelocity(start, target, gravity, horizontalSpeed, minTime, maxTime, out float flight);
            var p = NewProjectile(ctx, start, velocity);
            p.gravity = gravity;
            p.remaining = float.MaxValue;
            p.radius = radius;
            p.isArc = true;
            p.ignorePlayers = !hitsPlayers;
            p.fuse = fuse;
            p.shotIndex = shotIndex;
            p.maxLife = flight + 1.5f;
            m_Projectiles.Add(p);
        }

        public void SpawnCone(AttackContext ctx, ConeVolumeDelivery d)
        {
            var cone = new Cone
            {
                ctx = ctx,
                delivery = d,
                age = 0f,
                // Primeiro tick quando o spray já chegou ao alcance (não desperdiçar um tick com alcance 0)
                nextTick = d.FirstTickTime,
            };
            m_Cones.Add(cone);
            if (!ctx.authoritative)
            {
                cone.visual = m_SprayPool.Count > 0 ? m_SprayPool.Pop() : new SprayConeVisual(transform);
                cone.visual.Begin(ctx, d, Muzzle(ctx, 0), Muzzle(ctx, 1));
                ctx.shooter?.Visuals?.MuzzleFlash(ctx.VisualColor, 0);
                if (d.dualStream) ctx.shooter?.Visuals?.MuzzleFlash(ctx.VisualColor, 1);
            }
        }

        public void SpawnZone(AttackContext ctx, Vector3 position, ZoneSettings settings, PlayerNetwork follow)
        {
            if (m_Zones.Count > 48 || settings == null) return;
            var z = new Zone { ctx = ctx, settings = settings, position = position, follow = follow };
            if (!ctx.authoritative)
            {
                z.visual = TakeZone();
                z.visual.root.position = position + Vector3.up * 0.04f;
                Art.SetColor(z.visual.discR, Art.WithAlpha(settings.color, 0.24f));
                Art.SetColor(z.visual.ringR, Art.WithAlpha(Color.Lerp(settings.color, Color.white, 0.3f), 0.9f));
                Art.SetColor(z.visual.edgeR, Art.WithAlpha(settings.color, 0.65f));
                PoseZone(z, 0f);
                Fx.Ring(position, settings.color, settings.radius * 2.2f, 0.35f);
            }
            m_Zones.Add(z);
        }

        // ================================================================== armadilhas (sincronizadas pelo host)

        /// <summary>
        /// Procura um sítio válido para uma armadilha perto de 'desired': chão firme (pouco inclinado), espaço livre
        /// (não dentro de paredes nem de barreiras de água) e dentro do mapa. Recua em direção ao dono se for preciso.
        /// </summary>
        public static bool TryFindTrapPosition(Vector3 desired, Vector3 ownerFeet, TrapSettings settings, out Vector3 position)
        {
            float clearance = settings != null ? Mathf.Max(0.05f, settings.placementClearance) : 0.3f;
            const int steps = 6;
            for (int k = 0; k <= steps; k++)
            {
                var candidate = Vector3.Lerp(desired, ownerFeet, k / (float)steps);
                candidate = MapInfo.ClampInside(candidate, 1f);
                if (!FindGround(candidate, out var ground, out var normal)) continue;
                if (normal.y < 0.7f) continue; // parede ou rampa demasiado inclinada
                if (IsSpaceBlocked(ground + Vector3.up * (clearance + 0.08f), clearance)) continue;
                position = ground;
                return true;
            }
            position = desired;
            return false;
        }

        static bool FindGround(Vector3 around, out Vector3 point, out Vector3 normal)
        {
            point = around;
            normal = Vector3.up;
            int n = Physics.RaycastNonAlloc(around + Vector3.up * 1.2f, Vector3.down, s_GroundHits, 8f, CombatMath.ShotMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = s_GroundHits[i];
                if (h.collider == null || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
                if (h.distance >= best) continue;
                best = h.distance;
                point = h.point;
                normal = h.normal;
                found = true;
            }
            return found;
        }

        /// <summary>Há geometria (incluindo barreiras invisíveis de água/limites) neste espaço? Ignora jogadores.</summary>
        static bool IsSpaceBlocked(Vector3 center, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, s_Overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = s_Overlap[i];
                if (c == null || c.GetComponentInParent<PlayerHealth>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>Host: cria a armadilha autoritativa n.º index na posição já validada e avisa todas as máquinas.</summary>
        public void ServerSpawnTrap(AttackContext ctx, PlaceTrapsDelivery delivery, int index, Vector3 position)
        {
            if (ctx == null || !ctx.authoritative || delivery == null) return;
            var settings = delivery.trap;

            // Limite por dono: as mais antigas desaparecem
            int cap = Mathf.Max(1, settings.maxActivePerOwner);
            while (CountAuthoritative(ctx.attackerId) >= cap)
            {
                Trap oldest = null;
                foreach (var t in m_Traps)
                    if (t.authoritative && t.ownerId == ctx.attackerId && (oldest == null || t.spawnedAt < oldest.spawnedAt)) oldest = t;
                if (oldest == null) break;
                ServerRemoveTrap(oldest, TrapEvent.Expired, ulong.MaxValue);
            }
            if (m_Traps.Count > 96) return;

            var trap = new Trap
            {
                ctx = ctx,
                settings = settings,
                position = position,
                authoritative = true,
                seed = ctx.seed,
                index = index,
                ownerId = ctx.attackerId,
                spawnedAt = Time.time,
            };
            m_Traps.Add(trap);
            if (ctx.shooter != null && ctx.shooter.IsSpawned) ctx.shooter.Combat.ServerBroadcastTrapPlaced(ctx.isSuper, ctx.seed, index, position);
        }

        int CountAuthoritative(ulong ownerId)
        {
            int n = 0;
            foreach (var t in m_Traps) if (t.authoritative && t.ownerId == ownerId) n++;
            return n;
        }

        /// <summary>Todas as máquinas: o host confirmou uma armadilha nesta posição (visual).</summary>
        public void SpawnTrapVisual(PlayerNetwork owner, AttackDefinition attack, bool super, int seed, int index, Vector3 position)
        {
            if (owner == null || attack == null || !(attack.delivery is PlaceTrapsDelivery d)) return;
            if (FindTrap(owner.Id, seed, index, false) != null) return; // mensagem repetida
            var ctx = new AttackContext
            {
                shooter = owner,
                attackerId = owner.Id,
                attack = attack,
                isSuper = super,
                authoritative = false,
                origin = position,
                direction = Vector3.forward,
                target = position,
                seed = seed,
            };
            owner.Visuals?.ApplySkinColors(ctx);
            var local = PlayerNetwork.Local;
            var t = new Trap
            {
                ctx = ctx,
                settings = d.trap,
                position = position,
                authoritative = false,
                seed = seed,
                index = index,
                ownerId = owner.Id,
                spawnedAt = Time.time,
                ownerView = owner.IsLocalHuman || (local != null && MatchManager.AreTeammates(owner, local)),
            };
            bool discreet = d.trap.hiddenFromEnemies && !t.ownerView;
            t.visual = d.trap.kind == TrapKind.Snare
                ? ProjectileVisuals.TakeSnare(d.trap.color, position, discreet)
                : ProjectileVisuals.TakeMine(d.trap.color, position, discreet);
            Fx.Ring(position, d.trap.color, 1.6f, 0.35f);
            Fx.Sparks(position + Vector3.up * 0.2f, d.trap.color, 5, 3f);
            m_Traps.Add(t);
        }

        /// <summary>Visual imediato de quem colocou (as armadilhas reais chegam com a confirmação do host).</summary>
        public void TrapPlacementFeedback(AttackContext ctx, TrapSettings settings)
        {
            if (ctx.authoritative) return;
            Fx.Ring(ctx.target, settings != null ? settings.color : ctx.VisualColor, 2.4f, 0.4f);
            ctx.shooter?.Visuals?.MuzzleFlash(ctx.VisualColor, 0);
        }

        /// <summary>Todas as máquinas: evento decidido no host para uma armadilha.</summary>
        public void OnTrapEvent(ulong ownerId, int seed, int index, TrapEvent evt, ulong victimId)
        {
            var t = FindTrap(ownerId, seed, index, false);
            if (t == null) return;
            switch (evt)
            {
                case TrapEvent.Triggered:
                    if (t.triggeredAt < 0f)
                    {
                        t.triggeredAt = t.age;
                        Sfx.PlayAt(Sound.MineArm, t.position, 1f);
                    }
                    break;
                case TrapEvent.Detonated:
                    Detonate(t.ctx, t.position + Vector3.up * 0.4f, true);
                    RemoveVisualTrap(t);
                    break;
                case TrapEvent.Sprung:
                {
                    var victim = PlayerNetwork.Find(victimId);
                    var at = victim != null ? victim.transform.position : t.position;
                    Fx.Ring(at, t.settings.color, 2.2f, 0.4f);
                    Fx.Sparks(at + Vector3.up * 0.3f, t.settings.color, 10, 4f);
                    Sfx.PlayAt(Sound.Snare, at, 1f);
                    RemoveVisualTrap(t);
                    break;
                }
                case TrapEvent.Destroyed:
                    Fx.Debris(t.position + Vector3.up * 0.15f, t.settings.color, 6, 3f);
                    Fx.Smoke(t.position + Vector3.up * 0.2f, Vector3.up * 0.4f, 0.5f, 0.5f, 0.5f);
                    Sfx.PlayAt(Sound.Crate, t.position, 0.6f);
                    RemoveVisualTrap(t);
                    break;
                default:
                    Fx.Smoke(t.position + Vector3.up * 0.2f, Vector3.up * 0.3f, 0.4f, 0.4f, 0.4f);
                    RemoveVisualTrap(t);
                    break;
            }
        }

        Trap FindTrap(ulong ownerId, int seed, int index, bool authoritative)
        {
            foreach (var t in m_Traps)
                if (t.authoritative == authoritative && t.ownerId == ownerId && t.seed == seed && t.index == index)
                    return t;
            return null;
        }

        void RemoveVisualTrap(Trap t)
        {
            ReleaseTrapVisual(t);
            m_Traps.Remove(t);
        }

        void ReleaseTrapVisual(Trap t)
        {
            if (t.visual == null) return;
            if (t.settings != null && t.settings.kind == TrapKind.Snare) ProjectileVisuals.ReleaseSnare(t.visual);
            else ProjectileVisuals.ReleaseMine(t.visual);
            t.visual = null;
        }

        void ServerRemoveTrap(Trap t, TrapEvent reason, ulong victimId)
        {
            m_Traps.Remove(t);
            var owner = t.ctx != null ? t.ctx.shooter : null;
            if (owner != null && owner.IsSpawned) owner.Combat.ServerBroadcastTrapEvent(t.seed, t.index, reason, victimId);
        }

        /// <summary>Host: explosões inimigas destroem armadilhas (sem as detonar), conforme as regras de cada uma.</summary>
        public void ServerDestroyTrapsInRadius(Vector3 center, float radius, ulong attackerId)
        {
            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                var t = m_Traps[i];
                if (!t.authoritative || !t.settings.destroyedByEnemyExplosions || !IsEnemyTrap(t, attackerId)) continue;
                if ((t.position - center).sqrMagnitude <= radius * radius) ServerRemoveTrap(t, TrapEvent.Destroyed, ulong.MaxValue);
            }
        }

        /// <summary>Host: a facada inimiga destrói armadilhas ao longo do golpe.</summary>
        public void ServerDestroyTrapsNearSegment(Vector3 origin, Vector3 dir, float length, float radius, ulong attackerId, bool melee)
        {
            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                var t = m_Traps[i];
                if (!t.authoritative || !IsEnemyTrap(t, attackerId)) continue;
                if (melee && !t.settings.destroyedByEnemyMelee) continue;
                var p = t.position + Vector3.up * 0.15f;
                float along = Mathf.Clamp(Vector3.Dot(p - origin, dir), 0f, length);
                var closest = origin + dir * along;
                // A facada é dada de pé: aceita armadilhas no chão por baixo da linha do golpe
                var d = p - closest;
                if (d.y < 0f) d.y = Mathf.Min(0f, d.y + 1.6f);
                if (d.sqrMagnitude <= radius * radius) ServerRemoveTrap(t, TrapEvent.Destroyed, ulong.MaxValue);
            }
        }

        /// <summary>Host: remove as armadilhas de um dono (ex.: eliminado, com removeOnOwnerDeath).</summary>
        public void ServerOnOwnerDied(ulong ownerId)
        {
            for (int i = m_Traps.Count - 1; i >= 0; i--)
            {
                var t = m_Traps[i];
                if (t.authoritative && t.ownerId == ownerId && t.settings.removeOnOwnerDeath) ServerRemoveTrap(t, TrapEvent.Expired, ulong.MaxValue);
            }
        }

        static bool IsEnemyTrap(Trap t, ulong attackerId)
        {
            if (t.ownerId == attackerId) return false;
            return !MatchManager.AreTeammates(PlayerNetwork.Find(t.ownerId), PlayerNetwork.Find(attackerId));
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
                if (!StepCone(m_Cones[i], dt))
                {
                    ReleaseSpray(m_Cones[i]);
                    m_Cones.RemoveAt(i);
                }

            for (int i = m_Zones.Count - 1; i >= 0; i--)
            {
                if (!StepZone(m_Zones[i], dt))
                {
                    ReleaseZone(m_Zones[i].visual);
                    m_Zones.RemoveAt(i);
                }
            }

            // Cópia: uma explosão ou um evento do host pode remover outras armadilhas durante o passo
            m_TrapScratch.Clear();
            m_TrapScratch.AddRange(m_Traps);
            foreach (var t in m_TrapScratch)
            {
                if (!m_Traps.Contains(t)) continue;
                bool keep = t.authoritative ? StepTrapServer(t, dt) : StepTrapVisual(t, dt);
                if (!keep && m_Traps.Remove(t)) ReleaseTrapVisual(t);
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
            p.shotIndex = 0;
            p.pierced.Clear();
            p.visual = null;

            if (!ctx.authoritative)
            {
                var style = ctx.styleOverride != ProjectileStyle.Auto ? ctx.styleOverride : ProjectileVisuals.Resolve(ctx.attack);
                float scale = ctx.attack.visuals.scale;
                // Os estilos com modelo próprio já têm tamanho legível; a escala só exagera balas/orbes
                if (style == ProjectileStyle.Dynamite || style == ProjectileStyle.Barrel || style == ProjectileStyle.Bomb ||
                    style == ProjectileStyle.Arrow || style == ProjectileStyle.Tnt || style == ProjectileStyle.Grenade)
                    scale = 1f;
                p.visual = ProjectileVisuals.Take(style, ctx.VisualColor, scale, start, velocity);
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

                    if (p.ctx.authoritative) EffectResolver.DirectHit(p.ctx, health, point, p.traveled + hit.distance, p.shotIndex, p.position, dir);
                    else Fx.HitStar(point, p.ctx.VisualColor, Mathf.Clamp(p.ctx.attack.visuals.scale, 0.7f, 1.5f));

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

            if (p.visual != null) ProjectileVisuals.Step(p.visual, p.position, p.velocity, dt);

            bool alive = (p.isArc || p.remaining > 0f) && p.life < p.maxLife && p.position.y > -30f;
            if (!alive && p.isArc) Impact(p, p.position, null);
            return alive;
        }

        void Impact(Projectile p, Vector3 point, Collider surface)
        {
            if (p.isArc && p.fuse > 0f)
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
                Fx.Impact(point, p.ctx.VisualColor, Mathf.Clamp(p.ctx.attack.visuals.scale, 0.7f, 1.6f));

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
                    Fx.Explosion(point, e.splashRadius, ctx.VisualColor, e.splashRadius < 1.6f ? 0.6f : 1f);
                    Sfx.PlayAt(ctx.attack.visuals.impactSound, point, Mathf.Clamp(0.5f + e.splashRadius * 0.2f, 0.6f, 1.2f));
                }
            }
            else if (always && !ctx.authoritative)
            {
                Fx.Impact(point, ctx.VisualColor, 1.5f);
            }

            if (e.zoneOnImpact != null && e.zoneOnImpact.enabled)
            {
                SpawnZone(ctx, GroundAt(point), e.zoneOnImpact, null);
                if (!ctx.authoritative) Sfx.PlayAt(Sound.PoisonHiss, point, 0.8f);
            }
        }

        // ------------------------------------------------------------------ cone

        bool StepCone(Cone c, float dt)
        {
            var d = c.delivery;
            var ctx = c.ctx;
            c.age += dt;

            if (ctx.shooter != null && (!ctx.shooter.IsSpawned || ctx.shooter.Health.IsDead)) return false;

            float halfAngle = d.angle * 0.5f;
            int maxTicks = d.ScheduledTicks;

            if (ctx.authoritative)
            {
                // Todos os ticks agendados até agora (um frame lento não perde dano)
                while (c.ticksDone < maxTicks && c.age >= c.nextTick)
                {
                    float tickTime = c.nextTick;
                    c.nextTick += Mathf.Max(0.05f, d.tickInterval);
                    c.ticksDone++;
                    float reach = d.range * Mathf.Clamp01(tickTime / Mathf.Max(0.01f, d.expandTime));
                    ConeTickAll(c, reach, halfAngle);
                }
            }

            if (!ctx.authoritative) c.visual?.Pose(c.age);

            return c.age < d.duration;
        }

        void ReleaseSpray(Cone cone)
        {
            if (cone.visual == null) return;
            cone.visual.Release();
            m_SprayPool.Push(cone.visual);
            cone.visual = null;
        }

        void ConeTickAll(Cone c, float reach, float halfAngle)
        {
            var ctx = c.ctx;
            int cap = c.delivery.maxTicksPerTarget;
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

                // Cada alvo é contado uma vez por tick e no máximo 'cap' ticks por ataque
                int slot = c.hitIds.IndexOf(pl.Id);
                if (slot < 0)
                {
                    c.hitIds.Add(pl.Id);
                    c.hitCounts.Add(0);
                    slot = c.hitIds.Count - 1;
                }
                if (cap > 0 && c.hitCounts[slot] >= cap) continue;
                c.hitCounts[slot]++;
                EffectResolver.ConeTick(ctx, pl.Health, dist);
            }
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

        bool StepTrapServer(Trap t, float dt)
        {
            var s = t.settings;
            t.age += dt;
            var owner = t.ctx.shooter;
            if (owner == null || !owner.IsSpawned) return false; // dono saiu: todos removem sozinhos
            if (t.age > s.lifetime)
            {
                ServerRemoveTrap(t, TrapEvent.Expired, ulong.MaxValue);
                return false;
            }
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver) return true;

            bool armed = t.age >= s.armTime;
            if (armed && t.triggeredAt < 0f)
            {
                var victim = NearestEnemy(t.position, s.triggerRadius, owner, s.kind == TrapKind.Snare);
                if (victim != null)
                {
                    t.triggeredAt = t.age;
                    if (s.kind == TrapKind.Snare)
                    {
                        // Prende logo quem a pisou (raiz + estados do ataque), e desaparece
                        EffectResolver.Snare(t.ctx, victim.Health, t.position);
                        ServerRemoveTrap(t, TrapEvent.Sprung, victim.Id);
                        return false;
                    }
                    owner.Combat.ServerBroadcastTrapEvent(t.seed, t.index, TrapEvent.Triggered, victim.Id);
                }
            }

            if (s.kind == TrapKind.Explosive && t.triggeredAt >= 0f && t.age >= t.triggeredAt + s.warningTime)
            {
                Detonate(t.ctx, t.position + Vector3.up * 0.4f, true);
                ServerRemoveTrap(t, TrapEvent.Detonated, ulong.MaxValue);
                return false;
            }
            return true;
        }

        bool StepTrapVisual(Trap t, float dt)
        {
            var s = t.settings;
            t.age += dt;
            // Segurança: se uma mensagem do host se perder, a armadilha desaparece pouco depois do fim da vida
            if (t.ctx.shooter == null || t.age > s.lifetime + 2f) return false;

            bool armed = t.age >= s.armTime;
            if (t.visual != null)
            {
                if (s.kind == TrapKind.Snare) ProjectileVisuals.PoseSnare(t.visual, armed, t.ownerView, t.age);
                else ProjectileVisuals.PoseMine(t.visual, armed, t.triggeredAt >= 0f, t.ownerView, t.age);
            }
            return true;
        }

        static PlayerNetwork NearestEnemy(Vector3 center, float radius, PlayerNetwork owner, bool mustBeRootable)
        {
            PlayerNetwork best = null;
            float bestD = float.MaxValue;
            foreach (var pl in PlayerNetwork.All)
            {
                if (pl == null || !pl.IsSpawned || pl == owner || pl.Health.IsDead) continue;
                if (MatchManager.AreTeammates(owner, pl)) continue;
                if (!Within(center, pl.transform.position, radius)) continue;
                if (mustBeRootable && (pl.Status == null || !pl.Status.ServerCanBeRooted)) continue;
                float d = (pl.transform.position - center).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
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
            // Últimos segundos: a borda pisca para avisar que a zona vai desaparecer
            if (s.duration - z.age < 1.5f)
                Art.SetColor(v.edgeR, Art.WithAlpha(s.color, (Mathf.Sin(z.age * 18f) > 0f ? 0.8f : 0.3f) * fade));

            // Nuvem/partículas dentro da zona
            if (dt > 0f && z.age >= z.nextPuff)
            {
                z.nextPuff = z.age + 0.06f;
                var r = Random.insideUnitCircle * s.radius * 0.9f;
                var c = Art.WithAlpha(s.color, 0.55f * fade);
                Fx.Puff(z.position + new Vector3(r.x, 0.2f, r.y), Vector3.up * Random.Range(0.4f, 1.2f), c, Random.Range(0.6f, 1.1f), 0.9f);
                if (Random.value < 0.5f)
                    Fx.Glow(z.position + new Vector3(-r.y, 0.1f, r.x), Vector3.up * Random.Range(1f, 2.2f), Color.Lerp(s.secondaryColor, Color.white, 0.25f), 0.1f, 0.7f);
            }
        }
    }
}
