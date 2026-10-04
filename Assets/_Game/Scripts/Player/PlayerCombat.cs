using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Ataque principal (cargas que recarregam sozinhas) e Super (carrega ao causar dano), a partir do BrawlerDefinition.
    ///
    /// Fluxo de rede:
    /// 1. Quem controla o jogador (dono humano, ou host para bots) mostra logo o ataque e envia a INTENÇÃO
    ///    (origem, direção, ponto-alvo, seed) ao host.
    /// 2. O host valida (vivo, cargas/cooldown no lado do servidor, Super carregado, origem perto do jogador,
    ///    alvo dentro do alcance) e simula a instância autoritativa — só ela causa dano.
    /// 3. O host reenvia a mesma intenção aos outros clientes, que mostram o mesmo ataque.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        const float MaxOriginError = 3f;

        /// <summary>0..1 — decidido pelo servidor.</summary>
        public readonly NetworkVariable<float> SuperCharge = new NetworkVariable<float>(0f);

        PlayerNetwork m_Player;

        // Lado do dono (previsão local para resposta imediata)
        float m_Ammo;
        float m_Cooldown;

        // Lado do servidor (validação)
        float m_ServerAmmo;
        float m_ServerLastFire = -10f;

        public float Ammo => m_Ammo;
        public bool SuperReady => SuperCharge.Value >= 0.999f;
        public bool CanAttackNow => m_Cooldown <= 0f && m_Ammo >= 1f;

        BrawlerDefinition Brawler => m_Player.Brawler;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            int charges = Brawler != null ? Brawler.primary.maxCharges : 3;
            if (IsOwner) m_Ammo = charges;
            if (IsServer) m_ServerAmmo = charges;
            m_Player.HeroId.OnValueChanged += OnBrawlerChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnBrawlerChanged;
        }

        void OnBrawlerChanged(int previous, int current)
        {
            if (IsOwner)
            {
                m_Ammo = Brawler.primary.maxCharges;
                m_Cooldown = 0f;
            }
            if (IsServer) m_ServerAmmo = Brawler.primary.maxCharges;
        }

        void Update()
        {
            if (!IsSpawned) return;
            var b = Brawler;
            if (b == null) return;
            float dt = Time.deltaTime;

            if (IsServer && m_ServerAmmo < b.primary.maxCharges)
                m_ServerAmmo = Mathf.Min(b.primary.maxCharges, m_ServerAmmo + dt / Mathf.Max(0.05f, b.primary.reloadTimePerCharge));

            if (!IsOwner) return;

            if (m_Ammo < b.primary.maxCharges)
            {
                int before = Mathf.FloorToInt(m_Ammo);
                m_Ammo = Mathf.Min(b.primary.maxCharges, m_Ammo + dt / Mathf.Max(0.05f, b.primary.reloadTimePerCharge));
                if (Mathf.FloorToInt(m_Ammo) > before && m_Player.IsLocalHuman) Sfx.Play2D(Sound.Reload, 0.6f);
            }

            m_Cooldown -= dt;

            if (m_Player.Health.IsDead || MatchManager.IsMatchOver || m_Cooldown > 0f) return;

            var cmd = m_Player.ReadCommands();

            if (cmd.superPressed && SuperReady && b.super.attack != null)
            {
                m_Cooldown = b.primary.cooldown;
                FireLocal(true);
            }
            else if (cmd.fire && b.primary.attack != null)
            {
                if (m_Ammo >= 1f)
                {
                    m_Ammo -= 1f;
                    m_Cooldown = b.primary.cooldown;
                    FireLocal(false);
                }
                else
                {
                    m_Cooldown = 0.2f;
                    if (m_Player.IsLocalHuman) Sfx.Play2D(Sound.Empty);
                }
            }
        }

        AttackDefinition AttackFor(bool super) => super ? Brawler.super.attack : Brawler.primary.attack;

        AttackContext MakeContext(AttackDefinition attack, bool super, bool authoritative, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            return new AttackContext
            {
                shooter = m_Player,
                attackerId = m_Player.Id,
                attack = attack,
                isSuper = super,
                authoritative = authoritative,
                origin = origin,
                direction = direction,
                target = target,
                seed = seed,
            };
        }

        void FireLocal(bool super)
        {
            var attack = AttackFor(super);
            var origin = m_Player.Motor.EyePosition;
            var dir = m_Player.Motor.AimDirection;
            var target = CombatMath.AimPoint(m_Player, attack.Range);
            int seed = Random.Range(int.MinValue, int.MaxValue);

            CombatSystem.Instance.StartAttack(MakeContext(attack, super, false, origin, dir, target, seed));
            m_Player.Visuals?.OnAttackStarted(attack, super);
            FireRpc(super, origin, dir, target, seed);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(bool super, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            var b = Brawler;
            if (b == null || m_Player.Health.IsDead || MatchManager.IsMatchOver) return;
            var attack = AttackFor(super);
            if (attack == null) return;

            // --- Validação no host ---
            if (super)
            {
                if (SuperCharge.Value < 0.999f) return;
                SuperCharge.Value = 0f;
            }
            else
            {
                // Tolerância para latência/arredondamentos, sem permitir spam
                if (m_ServerAmmo < 0.8f) return;
                if (Time.time - m_ServerLastFire < b.primary.cooldown * 0.6f) return;
                m_ServerAmmo -= 1f;
            }
            m_ServerLastFire = Time.time;

            // A origem tem de estar perto do jogador visto pelo host
            var serverEye = m_Player.Motor.EyePosition;
            if (!IsFinite(origin) || (origin - serverEye).sqrMagnitude > MaxOriginError * MaxOriginError) origin = serverEye;
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f) direction = m_Player.Motor.AimDirection;
            direction.Normalize();

            // O ponto-alvo (arcos/armadilhas) fica dentro do alcance
            float maxRange = attack.Range + 1.5f;
            if (!IsFinite(target)) target = origin + direction * attack.Range;
            var toTarget = target - origin;
            if (toTarget.magnitude > maxRange) target = origin + toTarget.normalized * attack.Range;

            m_Player.Health.ServerNotifyAttacked();
            CombatSystem.Instance.StartAttack(MakeContext(attack, super, true, origin, direction, target, seed));
            RemoteFireRpc(super, origin, direction, target, seed);
        }

        /// <summary>A mesma intenção para toda a gente exceto o dono (que já mostrou o ataque).</summary>
        [Rpc(SendTo.NotOwner)]
        void RemoteFireRpc(bool super, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            var attack = AttackFor(super);
            if (attack == null) return;
            CombatSystem.Instance.StartAttack(MakeContext(attack, super, false, origin, direction, target, seed));
            m_Player.Visuals?.OnAttackStarted(attack, super);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        /// <summary>Servidor: chamado pelo EffectResolver quando um ataque deste jogador acerta alguém.</summary>
        public void ServerOnDealtDamage(int damage, bool fromSuper, bool headshot, bool killed)
        {
            if (!fromSuper && Brawler != null)
            {
                float required = Mathf.Max(1f, Brawler.super.chargeRequired);
                SuperCharge.Value = Mathf.Min(1f, SuperCharge.Value + damage / required);
            }
            if (!m_Player.IsBot) HitConfirmRpc(damage, headshot, killed);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(int damage, bool headshot, bool killed)
        {
            if (!m_Player.IsLocalHuman) return;
            GameUI.ShowHitMarker(headshot, killed);
            if (killed) Sfx.Play2D(Sound.Kill, 0.9f);
            else Sfx.Play2D(headshot ? Sound.HeadHit : Sound.Hit, 0.7f);
        }

        public void ServerRefill()
        {
            if (Brawler != null) m_ServerAmmo = Brawler.primary.maxCharges;
            RefillRpc();
        }

        [Rpc(SendTo.Owner)]
        void RefillRpc()
        {
            if (Brawler != null) m_Ammo = Brawler.primary.maxCharges;
            m_Cooldown = 0f;
        }

        public void ServerResetSuper() => SuperCharge.Value = 0f;
    }
}
