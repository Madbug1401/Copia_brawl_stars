using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Aplica efeitos de combate. Só corre no host: é aqui que se decide dano, estados, knockback e cobertura.
    /// </summary>
    public static class EffectResolver
    {
        static readonly Collider[] s_Overlap = new Collider[48];
        static readonly List<PlayerHealth> s_Victims = new List<PlayerHealth>();
        static readonly List<DestructibleCover> s_Covers = new List<DestructibleCover>();
        /// <summary>Último tick de zonas "não acumuláveis" por (atacante, vítima).</summary>
        static readonly Dictionary<(ulong, ulong), float> s_ZoneTicks = new Dictionary<(ulong, ulong), float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_ZoneTicks.Clear();

        /// <summary>Esquece o histórico das zonas (início de partida).</summary>
        public static void ResetMatchState() => s_ZoneTicks.Clear();

        /// <summary>Impacto direto num jogador (projétil, hitscan). O headshot é decidido pelo chamador (host).</summary>
        public static void DirectHit(AttackContext ctx, PlayerHealth victim, Vector3 point, float travelDistance, bool headshot)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;
            var e = ctx.Effect;
            headshot &= e.canHeadshot;

            float distanceMult = e.DistanceMultiplier(travelDistance, ctx.Range);
            int dmg;
            if (headshot && e.headshotExecutes)
            {
                // Tiro fatal: eliminação imediata, só com acerto válido na cabeça (sem multiplicadores nem limites)
                Damage(ctx, victim, Mathf.Max(1, victim.Health.Value), true, true);
                ApplyStatuses(ctx, victim, point - ctx.direction);
                return;
            }
            if (headshot && e.headshotDamage > 0) dmg = Mathf.RoundToInt(e.headshotDamage * distanceMult);
            else dmg = Mathf.RoundToInt(e.damage * distanceMult * (headshot ? e.headshotMultiplier : 1f));

            Damage(ctx, victim, dmg, headshot);
            ApplyStatuses(ctx, victim, point - ctx.direction);
        }

        /// <summary>Compatibilidade: headshot só pela altura do impacto.</summary>
        public static void DirectHit(AttackContext ctx, PlayerHealth victim, Vector3 point, float travelDistance)
        {
            bool head = victim != null && point.y - victim.transform.position.y >= CombatSystem.HeadshotHeight;
            DirectHit(ctx, victim, point, travelDistance, head);
        }

        /// <summary>Impacto no cenário: dano à cobertura atingida.</summary>
        public static void SurfaceHit(AttackContext ctx, Collider col)
        {
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver || col == null || ctx.Effect.coverDamage <= 0) return;
            var cover = col.GetComponentInParent<DestructibleCover>();
            if (cover != null) cover.ServerDamage(ctx.Effect.coverDamage);
        }

        /// <summary>Explosão: dano de área (com redução até à borda), estados, knockback e cobertura no raio.</summary>
        public static void Explosion(AttackContext ctx, Vector3 center)
        {
            var e = ctx.Effect;
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver || e.splashRadius <= 0.01f) return;

            s_Victims.Clear();
            s_Covers.Clear();
            int n = Physics.OverlapSphereNonAlloc(center, e.splashRadius, s_Overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_Overlap[i];
                var h = col.GetComponentInParent<PlayerHealth>();
                if (h != null)
                {
                    if (!h.IsDead && h.NetworkObjectId != ctx.attackerId && CanAffectPlayer(ctx, h) && !s_Victims.Contains(h))
                        s_Victims.Add(h);
                    continue;
                }
                var cover = col.GetComponentInParent<DestructibleCover>();
                if (cover != null && !s_Covers.Contains(cover)) s_Covers.Add(cover);
            }

            foreach (var h in s_Victims)
            {
                if (e.splashDamage > 0)
                {
                    float dist = CombatMath.DistanceToBody(h, center);
                    float k = Mathf.Lerp(1f, Mathf.Clamp01(e.splashEdgeMultiplier), Mathf.Clamp01(dist / e.splashRadius));
                    Damage(ctx, h, Mathf.Max(1, Mathf.RoundToInt(e.splashDamage * k)), false);
                }
                ApplyStatuses(ctx, h, center);
            }
            if (e.coverDamage > 0)
                foreach (var c in s_Covers) c.ServerDamage(e.coverDamage);
        }

        /// <summary>Tick de uma zona/aura.</summary>
        public static void ZoneTick(AttackContext ctx, PlayerHealth victim, ZoneSettings zone)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;

            if (zone.stacking == ZoneStacking.OnePerAttacker)
            {
                // Zonas sobrepostas do mesmo atacante não multiplicam o dano: no máximo um tick por intervalo
                var key = (ctx.attackerId, victim.NetworkObjectId);
                float now = Time.time;
                if (s_ZoneTicks.TryGetValue(key, out float last) && now - last < Mathf.Max(0.1f, zone.tickInterval) * 0.95f) return;
                s_ZoneTicks[key] = now;
            }

            if (zone.tickDamage > 0) Damage(ctx, victim, zone.tickDamage, false);
            var status = victim.GetComponent<StatusEffects>();
            if (status != null && zone.slowFactor < 1f) status.ServerApplySlow(zone.slowFactor, zone.slowDuration);
        }

        /// <summary>Tick de um volume em cone: dano depende da distância à origem.</summary>
        public static void ConeTick(AttackContext ctx, PlayerHealth victim, float distance)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;
            var e = ctx.Effect;
            int dmg = Mathf.RoundToInt(e.damage * e.DistanceMultiplier(distance, ctx.Range));
            Damage(ctx, victim, dmg, false);
            ApplyStatuses(ctx, victim, ctx.origin);
        }

        /// <summary>Armadilha de imobilização: prende o alvo e aplica o veneno ao longo da duração.</summary>
        public static bool Snare(AttackContext ctx, PlayerHealth victim, TrapSettings trap)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim) || victim.IsProtected) return false;
            var status = victim.GetComponent<StatusEffects>();
            if (status == null || !status.ServerApplyRoot(trap.rootDuration, trap.rootImmunityAfter)) return false;
            if (trap.poisonTotalDamage > 0 && trap.rootDuration > 0f)
                status.ServerApplyDot(trap.poisonTotalDamage / trap.rootDuration, trap.rootDuration, ctx.attackerId, ctx.isSuper);
            MatchStatsTracker.ServerOnDamage(PlayerNetwork.Find(ctx.attackerId), victim.GetComponent<PlayerNetwork>(), 0, false);
            return true;
        }

        static void ApplyStatuses(AttackContext ctx, PlayerHealth victim, Vector3 from)
        {
            if (!CanAffectPlayer(ctx, victim)) return;
            var e = ctx.Effect;
            var status = victim.GetComponent<StatusEffects>();
            if (status == null || victim.IsDead) return;

            if (e.slowFactor < 1f && e.slowDuration > 0f) status.ServerApplySlow(e.slowFactor, e.slowDuration);
            if (e.dotDamagePerSecond > 0f && e.dotDuration > 0f) status.ServerApplyDot(e.dotDamagePerSecond, e.dotDuration, ctx.attackerId, ctx.isSuper);

            if (e.knockback > 0f || e.knockbackUp > 0f)
            {
                var dir = victim.transform.position - from;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f) dir = ctx.direction;
                dir.y = 0f;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
                status.ServerApplyKnockback(dir * e.knockback + Vector3.up * e.knockbackUp);
            }
        }

        /// <summary>
        /// Dano creditado ao atacante (carrega o Super se não vier do Super). Aplica o multiplicador de progressão do
        /// atacante e o limite de dano por alvo do ataque.
        /// </summary>
        public static void Damage(AttackContext ctx, PlayerHealth victim, int amount, bool headshot, bool execute = false)
        {
            if (victim == null || amount <= 0) return;
            if (!execute)
            {
                if (ctx.damageMultiplier > 0f && !Mathf.Approximately(ctx.damageMultiplier, 1f))
                    amount = Mathf.Max(1, Mathf.RoundToInt(amount * ctx.damageMultiplier));
                amount = ctx.ClampToBudget(victim.NetworkObjectId, amount);
                if (amount <= 0) return;
            }
            DamageRaw(victim, amount, ctx.attackerId, ctx.isSuper, headshot);
        }

        public static void DamageRaw(PlayerHealth victim, int amount, ulong attackerId, bool fromSuper, bool headshot)
        {
            if (victim == null || amount <= 0 || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            var attacker = PlayerNetwork.Find(attackerId);
            if (MatchManager.AreTeammates(attacker, victim.GetComponent<PlayerNetwork>())) return;

            int before = victim.Health.Value;
            bool killed = victim.ServerApplyDamage(amount, attackerId);
            int dealt = Mathf.Max(0, before - victim.Health.Value);
            if (killed) dealt = Mathf.Max(dealt, before);
            if (dealt > 0) MatchStatsTracker.ServerOnDamage(attacker, victim.GetComponent<PlayerNetwork>(), dealt, headshot);
            if (attacker != null && attacker.IsSpawned && attacker.Health != victim && dealt > 0)
                attacker.Combat.ServerOnDealtDamage(dealt, fromSuper, headshot, killed, victim.transform.position);
        }

        static bool CanAffectPlayer(AttackContext ctx, PlayerHealth victim)
        {
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver) return false;
            var attacker = PlayerNetwork.Find(ctx.attackerId);
            return !MatchManager.AreTeammates(attacker, victim.GetComponent<PlayerNetwork>());
        }
    }
}
