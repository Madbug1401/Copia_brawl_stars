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

        /// <summary>
        /// Impacto direto num jogador (projétil, hitscan). shotIndex permite dano exato por disparo (ex.: flechas);
        /// rayOrigin/rayDir permitem uma deteção fiável de headshots.
        /// </summary>
        public static void DirectHit(AttackContext ctx, PlayerHealth victim, Vector3 point, float travelDistance,
            int shotIndex = -1, Vector3 rayOrigin = default, Vector3 rayDir = default)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;
            var e = ctx.Effect;

            bool headshot = e.canHeadshot && CombatMath.IsHeadshot(victim, point, rayOrigin, rayDir);

            // Tiro fatal: headshot válido (linha de visão já garantida pelo traço) elimina de imediato
            if (headshot && e.executeOnHeadshot)
            {
                Execute(ctx, victim);
                return;
            }

            int dmg = e.DirectDamage(shotIndex, travelDistance, ctx.Range, headshot);
            Damage(ctx, victim, dmg, headshot);
            ApplyStatuses(ctx, victim, point - ctx.direction);
        }

        /// <summary>Soma dos bagos de uma caçadeira num jogador (dano já calculado com a distância de cada bago).</summary>
        public static void PelletHit(AttackContext ctx, PlayerHealth victim, Vector3 point, int damage, bool headshot)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim) || damage <= 0) return;
            Damage(ctx, victim, damage, headshot);
            ApplyStatuses(ctx, victim, point - ctx.direction);
        }

        /// <summary>Eliminação imediata validada no host (ignora escudos de pet, respeita a proteção de spawn).</summary>
        static void Execute(AttackContext ctx, PlayerHealth victim)
        {
            int remaining = Mathf.Max(1, victim.Health.Value + victim.Shield.Value);
            DamageRaw(victim, remaining, ctx.attackerId, ctx.isSuper, true, true);
        }

        /// <summary>Impacto no cenário: dano à cobertura atingida.</summary>
        public static void SurfaceHit(AttackContext ctx, Collider col)
        {
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver || col == null || ctx.Effect.coverDamage <= 0) return;
            var cover = col.GetComponentInParent<DestructibleCover>();
            if (cover != null) cover.ServerDamage(ctx.Effect.coverDamage);
        }

        /// <summary>Explosão: dano de área (com queda pela distância ao centro), estados, knockback e cobertura no raio.</summary>
        public static void Explosion(AttackContext ctx, Vector3 center)
        {
            var e = ctx.Effect;
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver || e.splashRadius <= 0.01f) return;

            // Explosões inimigas destroem armadilhas no raio (regras de cada armadilha)
            if (CombatSystem.Instance != null) CombatSystem.Instance.ServerDestroyTrapsInRadius(center, e.splashRadius, ctx.attackerId);

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
                    float dist = DistanceToBody(center, h.transform.position);
                    int dmg = e.SplashDamageAt(dist);
                    if (dmg > 0) Damage(ctx, h, dmg, false);
                }
                ApplyStatuses(ctx, h, center);
            }
            if (e.coverDamage > 0)
                foreach (var c in s_Covers) c.ServerDamage(e.coverDamage);
        }

        /// <summary>Distância do centro de uma explosão ao corpo (cápsula de 1,8 m) de um jogador.</summary>
        public static float DistanceToBody(Vector3 center, Vector3 feet)
        {
            const float radius = 0.4f;
            float y = Mathf.Clamp(center.y, feet.y + radius, feet.y + 1.8f - radius);
            var axis = new Vector3(feet.x, y, feet.z);
            return Mathf.Max(0f, Vector3.Distance(center, axis) - radius);
        }

        /// <summary>Tick de uma zona/aura. Zonas do mesmo atacante não acumulam (um tick por intervalo).</summary>
        public static void ZoneTick(AttackContext ctx, PlayerHealth victim, ZoneSettings zone)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;
            var status = victim.GetComponent<StatusEffects>();
            if (zone.nonStackingPerAttacker && status != null && !status.ServerTryZoneTick(ctx.attackerId, zone.tickInterval)) return;
            if (zone.tickDamage > 0) Damage(ctx, victim, zone.tickDamage, false);
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

        /// <summary>Armadilha de imobilização: prende o alvo e aplica os estados do ataque (ex.: veneno).</summary>
        public static void Snare(AttackContext ctx, PlayerHealth victim, Vector3 trapPosition)
        {
            if (victim == null || victim.IsDead || !CanAffectPlayer(ctx, victim)) return;
            if (ctx.Effect.damage > 0) Damage(ctx, victim, ctx.Effect.damage, false);
            ApplyStatuses(ctx, victim, trapPosition);
        }

        static void ApplyStatuses(AttackContext ctx, PlayerHealth victim, Vector3 from)
        {
            if (!CanAffectPlayer(ctx, victim)) return;
            var e = ctx.Effect;
            var status = victim.GetComponent<StatusEffects>();
            if (status == null || victim.IsDead) return;

            if (e.slowFactor < 1f && e.slowDuration > 0f) status.ServerApplySlow(e.slowFactor, e.slowDuration);
            if (e.rootDuration > 0f) status.ServerApplyRoot(e.rootDuration, e.rootImmunity);
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

        /// <summary>Dano creditado ao atacante (carrega o Super se não vier do Super). Aplica o multiplicador de dano das melhorias.</summary>
        public static void Damage(AttackContext ctx, PlayerHealth victim, int amount, bool headshot)
        {
            var attacker = PlayerNetwork.Find(ctx.attackerId);
            if (attacker != null && amount > 0) amount = Mathf.Max(1, Mathf.RoundToInt(amount * attacker.DamageMultiplier));
            DamageRaw(victim, amount, ctx.attackerId, ctx.isSuper, headshot);
        }

        public static void DamageRaw(PlayerHealth victim, int amount, ulong attackerId, bool fromSuper, bool headshot, bool execute = false)
        {
            if (victim == null || amount <= 0 || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            var attacker = PlayerNetwork.Find(attackerId);
            var victimPlayer = victim.GetComponent<PlayerNetwork>();
            if (MatchManager.AreTeammates(attacker, victimPlayer)) return;

            bool killed = victim.ServerApplyDamage(amount, attackerId, execute, out int applied);
            if (applied <= 0 && !killed) return;
            MatchStatsTracker.ServerOnDamage(attacker, victimPlayer, applied, headshot);
            if (attacker != null && attacker.IsSpawned && attacker.Health != victim)
                attacker.Combat.ServerOnDealtDamage(applied, fromSuper, headshot, killed, execute, victim.transform.position);
        }

        static bool CanAffectPlayer(AttackContext ctx, PlayerHealth victim)
        {
            if (!MatchManager.IsCombatActive || MatchManager.IsMatchOver) return false;
            var attacker = PlayerNetwork.Find(ctx.attackerId);
            return !MatchManager.AreTeammates(attacker, victim.GetComponent<PlayerNetwork>());
        }
    }
}
