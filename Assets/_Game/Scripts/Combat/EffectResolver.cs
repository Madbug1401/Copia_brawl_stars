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

        /// <summary>Impacto direto num jogador (projétil, hitscan).</summary>
        public static void DirectHit(AttackContext ctx, PlayerHealth victim, Vector3 point, float travelDistance)
        {
            if (victim == null || victim.IsDead) return;
            var e = ctx.Effect;

            bool headshot = e.canHeadshot && point.y - victim.transform.position.y >= CombatSystem.HeadshotHeight;
            float mult = e.DistanceMultiplier(travelDistance, ctx.Range) * (headshot ? e.headshotMultiplier : 1f);
            int dmg = Mathf.RoundToInt(e.damage * mult);

            Damage(ctx, victim, dmg, headshot);
            ApplyStatuses(ctx, victim, point - ctx.direction);
        }

        /// <summary>Impacto no cenário: dano à cobertura atingida.</summary>
        public static void SurfaceHit(AttackContext ctx, Collider col)
        {
            if (col == null || ctx.Effect.coverDamage <= 0) return;
            var cover = col.GetComponentInParent<DestructibleCover>();
            if (cover != null) cover.ServerDamage(ctx.Effect.coverDamage);
        }

        /// <summary>Explosão: dano de área, estados, knockback e cobertura no raio.</summary>
        public static void Explosion(AttackContext ctx, Vector3 center)
        {
            var e = ctx.Effect;
            if (e.splashRadius <= 0.01f) return;

            s_Victims.Clear();
            s_Covers.Clear();
            int n = Physics.OverlapSphereNonAlloc(center, e.splashRadius, s_Overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_Overlap[i];
                var h = col.GetComponentInParent<PlayerHealth>();
                if (h != null)
                {
                    if (!h.IsDead && h.NetworkObjectId != ctx.attackerId && !s_Victims.Contains(h)) s_Victims.Add(h);
                    continue;
                }
                var cover = col.GetComponentInParent<DestructibleCover>();
                if (cover != null && !s_Covers.Contains(cover)) s_Covers.Add(cover);
            }

            foreach (var h in s_Victims)
            {
                if (e.splashDamage > 0) Damage(ctx, h, e.splashDamage, false);
                ApplyStatuses(ctx, h, center);
            }
            if (e.coverDamage > 0)
                foreach (var c in s_Covers) c.ServerDamage(e.coverDamage);
        }

        /// <summary>Tick de uma zona/aura.</summary>
        public static void ZoneTick(AttackContext ctx, PlayerHealth victim, ZoneSettings zone)
        {
            if (victim == null || victim.IsDead) return;
            if (zone.tickDamage > 0) Damage(ctx, victim, zone.tickDamage, false);
            var status = victim.GetComponent<StatusEffects>();
            if (status != null && zone.slowFactor < 1f) status.ServerApplySlow(zone.slowFactor, zone.slowDuration);
        }

        /// <summary>Tick de um volume em cone: dano depende da distância à origem.</summary>
        public static void ConeTick(AttackContext ctx, PlayerHealth victim, float distance)
        {
            if (victim == null || victim.IsDead) return;
            var e = ctx.Effect;
            int dmg = Mathf.RoundToInt(e.damage * e.DistanceMultiplier(distance, ctx.Range));
            Damage(ctx, victim, dmg, false);
            ApplyStatuses(ctx, victim, ctx.origin);
        }

        static void ApplyStatuses(AttackContext ctx, PlayerHealth victim, Vector3 from)
        {
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

        /// <summary>Dano creditado ao atacante (carrega o Super se não vier do Super).</summary>
        public static void Damage(AttackContext ctx, PlayerHealth victim, int amount, bool headshot)
        {
            DamageRaw(victim, amount, ctx.attackerId, ctx.isSuper, headshot);
        }

        public static void DamageRaw(PlayerHealth victim, int amount, ulong attackerId, bool fromSuper, bool headshot)
        {
            if (victim == null || amount <= 0) return;
            bool killed = victim.ServerApplyDamage(amount, attackerId);
            var attacker = PlayerNetwork.Find(attackerId);
            if (attacker != null && attacker.IsSpawned && attacker.Health != victim)
                attacker.Combat.ServerOnDealtDamage(amount, fromSuper, headshot, killed);
        }
    }
}
