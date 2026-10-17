using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Que ataque um disparo usa (viaja pela rede como byte).</summary>
    public enum AttackSlot : byte
    {
        Primary,
        /// <summary>Ataque do Super (Instant) ou tiro armado (ArmedShot).</summary>
        Super,
        /// <summary>Ataque do modo temporário (PrimaryOverride, ex.: Minigun).</summary>
        Mode,
    }

    /// <summary>
    /// Ataque principal (cargas que recarregam sozinhas) e Super, a partir do BrawlerDefinition.
    /// Tipos de Super (SuperDefinition.kind):
    /// - Instant: dispara logo o ataque do Super (comportamento original).
    /// - PrimaryOverride: durante X segundos o disparo usa o ataque do Super com cadência e orçamento de ataques
    ///   próprios (Minigun do Ekko) — o host conta os ataques, por isso o dano total potencial é limitado.
    /// - ArmedShot: o Super arma o próximo disparo (Tiro fatal da Lince); carrega sozinho com o tempo.
    ///
    /// Fluxo de rede:
    /// 1. Quem controla o jogador (dono humano, ou host para bots) mostra logo o ataque e envia a INTENÇÃO
    ///    (origem, direção, ponto-alvo, seed) ao host.
    /// 2. O host valida (vivo, cargas/cooldown/modo/armado no lado do servidor, origem perto do jogador,
    ///    alvo dentro do alcance) e simula a instância autoritativa — só ela causa dano.
    /// 3. O host reenvia a mesma intenção aos outros clientes, que mostram o mesmo ataque.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        const float MaxOriginError = 3f;

        /// <summary>0..1 — decidido pelo servidor.</summary>
        public readonly NetworkVariable<float> SuperCharge = new NetworkVariable<float>(0f);
        /// <summary>Hora do servidor em que o modo temporário acaba (0 = sem modo).</summary>
        public readonly NetworkVariable<double> ModeUntil = new NetworkVariable<double>(0);
        /// <summary>Ataques que ainda restam no modo temporário.</summary>
        public readonly NetworkVariable<int> ModeAttacksLeft = new NetworkVariable<int>(0);
        /// <summary>Tiro especial armado (ArmedShot).</summary>
        public readonly NetworkVariable<bool> SuperArmed = new NetworkVariable<bool>(false);

        PlayerNetwork m_Player;

        // Lado do dono (previsão local para resposta imediata)
        float m_Ammo;
        float m_Cooldown;
        float m_MeleeCooldown;
        int m_ModeShotsFired;
        double m_ModeSeenUntil;
        bool m_ArmedShotSent;
        float m_ArmToggleCooldown;

        // Lado do servidor (validação)
        float m_ServerAmmo;
        float m_ServerLastFire = -10f;
        float m_ServerLastModeFire = -10f;
        float m_ServerLastMelee = -10f;
        float m_ServerCharge;
        double m_ServerArmedUntil;

        public float Ammo => m_Ammo;
        public bool SuperReady => SuperCharge.Value >= 0.999f;
        public bool CanAttackNow => m_Cooldown <= 0f && (m_Ammo >= 1f || InMode || SuperArmed.Value);

        BrawlerDefinition Brawler => m_Player.Brawler;
        SuperKind Kind => Brawler != null ? Brawler.super.kind : SuperKind.Instant;

        double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

        /// <summary>Modo temporário ativo (Minigun).</summary>
        public bool InMode => IsSpawned && ModeAttacksLeft.Value > 0 && ServerNow < ModeUntil.Value;
        public float ModeRemaining => InMode ? (float)(ModeUntil.Value - ServerNow) : 0f;
        public float ModeFraction => InMode && Brawler != null ? Mathf.Clamp01(ModeRemaining / Mathf.Max(0.1f, Brawler.super.modeDuration)) : 0f;
        public bool IsArmed => SuperArmed.Value;

        float ReloadTime(BrawlerDefinition b) => Mathf.Max(0.05f, b.primary.reloadTimePerCharge * m_Player.Health.Stats.reloadTimeMultiplier);

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            int charges = Brawler != null ? Brawler.primary.maxCharges : 3;
            if (IsOwner) m_Ammo = charges;
            if (IsServer) m_ServerAmmo = charges;
            m_Player.HeroId.OnValueChanged += OnBrawlerChanged;
            ModeUntil.OnValueChanged += OnModeChanged;
            SuperArmed.OnValueChanged += OnArmedChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnBrawlerChanged;
            ModeUntil.OnValueChanged -= OnModeChanged;
            SuperArmed.OnValueChanged -= OnArmedChanged;
        }

        void OnBrawlerChanged(int previous, int current)
        {
            if (IsOwner)
            {
                m_Ammo = Brawler.primary.maxCharges;
                m_Cooldown = 0f;
                m_MeleeCooldown = 0f;
            }
            if (IsServer)
            {
                m_ServerAmmo = Brawler.primary.maxCharges;
                ServerEndSpecialStates();
            }
        }

        void OnModeChanged(double previous, double current)
        {
            bool active = current > ServerNow;
            if (active)
            {
                m_ModeShotsFired = 0;
                m_ModeSeenUntil = current;
            }
            m_Player.Visuals?.SetModeActive(active, Brawler != null ? Brawler.super.modeRig : RigStyle.Minigun);
            if (active && m_Player.IsLocalHuman)
            {
                GameUI.Toast("MINIGUN!", new Color(1f, 0.55f, 0.2f));
                Sfx.Play2D(Sound.MinigunSpin, 0.9f);
            }
            else if (active) Sfx.PlayAt(Sound.MinigunSpin, transform.position + Vector3.up, 0.9f);
        }

        void OnArmedChanged(bool previous, bool current)
        {
            if (!current) m_ArmedShotSent = false;
            m_Player.Visuals?.SetArmed(current);
            if (current && m_Player.IsLocalHuman)
            {
                GameUI.Toast("TIRO FATAL ARMADO", new Color(1f, 0.25f, 0.25f));
                Sfx.Play2D(Sound.SuperReady, 0.7f);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            var b = Brawler;
            if (b == null) return;
            float dt = Time.deltaTime;

            if (IsServer) ServerTick(b, dt);

            if (!IsOwner) return;

            if (m_Ammo < b.primary.maxCharges)
            {
                int before = Mathf.FloorToInt(m_Ammo);
                m_Ammo = Mathf.Min(b.primary.maxCharges, m_Ammo + dt / ReloadTime(b));
                if (Mathf.FloorToInt(m_Ammo) > before && m_Player.IsLocalHuman)
                {
                    Sfx.Play2D(b.firstPerson.rig == RigStyle.Shotgun ? Sound.ShotgunPump : Sound.Reload, 0.6f);
                    m_Player.Visuals?.OnChargeReloaded();
                }
            }

            m_Cooldown -= dt;
            m_MeleeCooldown -= dt;
            m_ArmToggleCooldown -= dt;

            if (m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;

            var cmd = m_Player.ReadCommands();

            if (cmd.meleePressed && m_MeleeCooldown <= 0f)
            {
                var melee = b.melee ?? new MeleeProfile();
                m_MeleeCooldown = Mathf.Max(0.1f, melee.cooldown);
                MeleeLocal();
            }

            // --- Super ---
            if (cmd.superPressed && b.super.attack != null)
            {
                switch (b.super.kind)
                {
                    case SuperKind.PrimaryOverride:
                        if (SuperReady && !InMode) ActivateModeRpc();
                        break;
                    case SuperKind.ArmedShot:
                        if (m_ArmToggleCooldown <= 0f && (SuperArmed.Value || SuperReady))
                        {
                            m_ArmToggleCooldown = 0.3f;
                            SetArmedRpc(!SuperArmed.Value);
                        }
                        break;
                    default:
                        if (SuperReady && m_Cooldown <= 0f)
                        {
                            m_Cooldown = b.primary.cooldown;
                            FireLocal(AttackSlot.Super);
                            return;
                        }
                        break;
                }
            }

            if (m_Cooldown > 0f) return;

            // --- Modo temporário: o disparo usa o ataque do Super, sem gastar cargas ---
            if (InMode && m_ModeSeenUntil == ModeUntil.Value)
            {
                if (cmd.fire && m_ModeShotsFired < b.super.modeAttackBudget)
                {
                    m_ModeShotsFired++;
                    m_Cooldown = Mathf.Max(0.05f, b.super.modeFireInterval);
                    FireLocal(AttackSlot.Mode);
                }
                return;
            }

            if (!cmd.fire) return;

            // --- Tiro armado: o próximo disparo usa o ataque do Super ---
            if (SuperArmed.Value && !m_ArmedShotSent)
            {
                m_ArmedShotSent = true;
                m_Cooldown = b.primary.cooldown;
                FireLocal(AttackSlot.Super);
                return;
            }

            if (b.primary.attack != null)
            {
                if (m_Ammo >= 1f)
                {
                    m_Ammo -= 1f;
                    m_Cooldown = b.primary.cooldown;
                    FireLocal(AttackSlot.Primary);
                }
                else
                {
                    m_Cooldown = 0.2f;
                    if (m_Player.IsLocalHuman) Sfx.Play2D(Sound.Empty);
                }
            }
        }

        void ServerTick(BrawlerDefinition b, float dt)
        {
            if (m_ServerAmmo < b.primary.maxCharges)
                m_ServerAmmo = Mathf.Min(b.primary.maxCharges, m_ServerAmmo + dt / ReloadTime(b));

            if (m_Player.Health.IsDead)
            {
                ServerEndSpecialStates();
                return;
            }

            // Fim do modo temporário
            if (ModeUntil.Value > 0 && (ServerNow >= ModeUntil.Value || ModeAttacksLeft.Value <= 0))
            {
                ModeUntil.Value = 0;
                ModeAttacksLeft.Value = 0;
            }

            // Tiro armado desarma sozinho (a carga não se perde)
            if (SuperArmed.Value && ServerNow >= m_ServerArmedUntil) SuperArmed.Value = false;

            // Carga automática ao longo do tempo (Tiro fatal)
            if (b.super.autoChargePerSecond > 0f && m_ServerCharge < 1f && MatchManager.IsCombatActive && !MatchManager.IsMatchOver)
                ServerAddCharge(b.super.autoChargePerSecond * m_Player.Health.Stats.superChargeMultiplier * dt);
        }

        /// <summary>Servidor: soma carga ao Super (escreve na rede só a cada 1% para poupar tráfego).</summary>
        void ServerAddCharge(float amount)
        {
            if (amount <= 0f) return;
            bool wasReady = m_ServerCharge >= 0.999f;
            m_ServerCharge = Mathf.Min(1f, m_ServerCharge + amount);
            if (m_ServerCharge >= 0.999f) m_ServerCharge = 1f;
            if (Mathf.Abs(m_ServerCharge - SuperCharge.Value) >= 0.01f || m_ServerCharge >= 1f)
                SuperCharge.Value = m_ServerCharge;
            if (!wasReady && m_ServerCharge >= 0.999f && !m_Player.IsBot) SuperReadyRpc();
        }

        void ServerEndSpecialStates()
        {
            if (ModeUntil.Value != 0) ModeUntil.Value = 0;
            if (ModeAttacksLeft.Value != 0) ModeAttacksLeft.Value = 0;
            if (SuperArmed.Value) SuperArmed.Value = false;
        }

        AttackDefinition AttackFor(AttackSlot slot) => slot == AttackSlot.Primary ? Brawler.primary.attack : Brawler.super.attack;

        AttackContext MakeContext(AttackDefinition attack, AttackSlot slot, bool authoritative, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            return new AttackContext
            {
                shooter = m_Player,
                attackerId = m_Player.Id,
                attack = attack,
                isSuper = slot != AttackSlot.Primary,
                authoritative = authoritative,
                origin = origin,
                direction = direction,
                target = target,
                seed = seed,
                damageMultiplier = m_Player.Health.Stats.damageMultiplier,
            };
        }

        void FireLocal(AttackSlot slot)
        {
            var attack = AttackFor(slot);
            if (attack == null) return;
            var origin = m_Player.Motor.EyePosition;
            var dir = m_Player.Motor.AimDirection;
            var target = CombatMath.AimPoint(m_Player, attack.Range);
            int seed = Random.Range(int.MinValue, int.MaxValue);

            var ctx = MakeContext(attack, slot, false, origin, dir, target, seed);
            CombatSystem.Instance.StartAttack(ctx);
            attack.delivery.OnOwnerFire(m_Player, ctx);
            m_Player.Visuals?.OnAttackStarted(attack, slot != AttackSlot.Primary);
            FireRpc((byte)slot, origin, dir, target, seed);
        }

        void MeleeLocal()
        {
            var origin = m_Player.Motor.EyePosition;
            var direction = m_Player.Motor.AimDirection;
            m_Player.Visuals?.OnMeleeStarted();
            Sfx.PlayAt(Sound.Melee, origin, m_Player.IsLocalHuman ? 0.75f : 1f);
            MeleeRpc(origin, direction);
        }

        [Rpc(SendTo.Server)]
        void MeleeRpc(Vector3 origin, Vector3 direction)
        {
            var b = Brawler;
            if (b == null || m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            var profile = b.melee ?? new MeleeProfile();
            if (Time.time - m_ServerLastMelee < Mathf.Max(0.1f, profile.cooldown) * 0.65f) return;
            m_ServerLastMelee = Time.time;

            var serverEye = m_Player.Motor.EyePosition;
            if (!IsFinite(origin) || (origin - serverEye).sqrMagnitude > MaxOriginError * MaxOriginError) origin = serverEye;
            if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f) direction = m_Player.Motor.AimDirection;
            direction.Normalize();

            m_Player.Health.ServerNotifyAttacked();
            CombatSystem.Instance.TraceMelee(m_Player, origin, direction, profile);
            RemoteMeleeRpc();
        }

        [Rpc(SendTo.NotOwner)]
        void RemoteMeleeRpc()
        {
            m_Player.Visuals?.OnMeleeStarted();
            Sfx.PlayAt(Sound.Melee, m_Player.Motor.EyePosition, 0.8f);
        }

        /// <summary>Servidor: liga o modo temporário (Minigun). Gasta o Super inteiro.</summary>
        [Rpc(SendTo.Server)]
        void ActivateModeRpc()
        {
            var b = Brawler;
            if (b == null || b.super.kind != SuperKind.PrimaryOverride || b.super.attack == null) return;
            if (m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            if (m_ServerCharge < 0.999f || InMode) return;
            m_ServerCharge = 0f;
            SuperCharge.Value = 0f;
            ModeAttacksLeft.Value = Mathf.Max(1, b.super.modeAttackBudget);
            ModeUntil.Value = ServerNow + Mathf.Max(0.5f, b.super.modeDuration);
            m_Player.Health.ServerNotifyAttacked();
        }

        /// <summary>Servidor: arma/desarma o tiro especial. Armar exige o Super carregado.</summary>
        [Rpc(SendTo.Server)]
        void SetArmedRpc(bool armed)
        {
            var b = Brawler;
            if (b == null || b.super.kind != SuperKind.ArmedShot || b.super.attack == null) return;
            if (armed)
            {
                if (m_Player.Health.IsDead || m_ServerCharge < 0.999f || !MatchManager.IsCombatActive) return;
                m_ServerArmedUntil = ServerNow + Mathf.Max(1f, b.super.armedWindow);
            }
            SuperArmed.Value = armed;
        }

        [Rpc(SendTo.Server)]
        void FireRpc(byte slotByte, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            var b = Brawler;
            if (b == null || m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            if (slotByte > (byte)AttackSlot.Mode) return;
            var slot = (AttackSlot)slotByte;
            var attack = AttackFor(slot);
            if (attack == null) return;

            // --- Validação no host ---
            switch (slot)
            {
                case AttackSlot.Super:
                    if (m_ServerCharge < 0.999f) return;
                    if (b.super.kind == SuperKind.ArmedShot)
                    {
                        if (!SuperArmed.Value) return;
                        SuperArmed.Value = false;
                    }
                    else if (b.super.kind != SuperKind.Instant) return;
                    m_ServerCharge = 0f;
                    SuperCharge.Value = 0f;
                    m_ServerLastFire = Time.time;
                    break;

                case AttackSlot.Mode:
                    // Pequena tolerância de latência no fim do modo; nunca acima do orçamento de ataques
                    if (b.super.kind != SuperKind.PrimaryOverride || ModeAttacksLeft.Value <= 0) return;
                    if (ServerNow > ModeUntil.Value + 0.25) return;
                    if (Time.time - m_ServerLastModeFire < b.super.modeFireInterval * 0.6f) return;
                    m_ServerLastModeFire = Time.time;
                    ModeAttacksLeft.Value--;
                    break;

                default:
                    // Tolerância para latência/arredondamentos, sem permitir spam
                    if (m_ServerAmmo < 0.8f) return;
                    if (Time.time - m_ServerLastFire < b.primary.cooldown * 0.6f) return;
                    m_ServerAmmo -= 1f;
                    m_ServerLastFire = Time.time;
                    break;
            }

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
            CombatSystem.Instance.StartAttack(MakeContext(attack, slot, true, origin, direction, target, seed));
            RemoteFireRpc(slotByte, origin, direction, target, seed);
        }

        /// <summary>A mesma intenção para toda a gente exceto o dono (que já mostrou o ataque).</summary>
        [Rpc(SendTo.NotOwner)]
        void RemoteFireRpc(byte slotByte, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            if (Brawler == null || slotByte > (byte)AttackSlot.Mode) return;
            var slot = (AttackSlot)slotByte;
            var attack = AttackFor(slot);
            if (attack == null) return;
            CombatSystem.Instance.StartAttack(MakeContext(attack, slot, false, origin, direction, target, seed));
            m_Player.Visuals?.OnAttackStarted(attack, slot != AttackSlot.Primary);
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        /// <summary>Servidor: chamado pelo EffectResolver quando um ataque deste jogador acerta alguém.</summary>
        public void ServerOnDealtDamage(int damage, bool fromSuper, bool headshot, bool killed, Vector3 victimPosition)
        {
            var b = Brawler;
            if (!fromSuper && b != null && b.super.chargeFromDamage)
            {
                float required = Mathf.Max(1f, b.super.chargeRequired);
                ServerAddCharge(damage / required * m_Player.Health.Stats.superChargeMultiplier);
            }
            if (!m_Player.IsBot) HitConfirmRpc(damage, headshot, killed, victimPosition);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(int damage, bool headshot, bool killed, Vector3 victimPosition)
        {
            if (!m_Player.IsLocalHuman) return;
            GameUI.ShowHitMarker(headshot, killed);
            GameUI.AddDamageNumber(victimPosition + Vector3.up * 2.1f, damage, headshot, killed);
            if (headshot) GameUI.AnnounceHeadshot();
            if (killed)
            {
                Sfx.Play2D(Sound.Kill, 0.9f);
                GameUI.AnnounceKill();
            }
            else Sfx.Play2D(headshot ? Sound.Headshot : Sound.Hit, 0.7f);
        }

        [Rpc(SendTo.Owner)]
        void SuperReadyRpc()
        {
            if (!m_Player.IsLocalHuman) return;
            Sfx.Play2D(Sound.SuperReady, 0.8f);
            string label = Kind == SuperKind.ArmedShot ? "TIRO FATAL PRONTO!" : Kind == SuperKind.PrimaryOverride ? "MINIGUN PRONTA!" : "SUPER PRONTO!";
            GameUI.Toast(label, new Color(1f, 0.8f, 0.2f));
        }

        // ------------------------------------------------------------------ armadilhas (eventos do host)

        /// <summary>Servidor: avisa os clientes de um evento numa armadilha deste jogador (disparou, explodiu, prendeu, destruída).</summary>
        public void ServerSendTrapEvent(int key, CombatSystem.TrapEvent evt, ulong victimId)
        {
            if (!IsServer || !IsSpawned) return;
            TrapEventRpc(key, evt, victimId);
        }

        [Rpc(SendTo.NotServer)]
        void TrapEventRpc(int key, CombatSystem.TrapEvent evt, ulong victimId)
        {
            if (CombatSystem.Instance != null) CombatSystem.Instance.ApplyTrapEvent(m_Player.Id, key, evt, victimId);
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

        public void ServerResetSuper()
        {
            m_ServerCharge = 0f;
            SuperCharge.Value = 0f;
            ServerEndSpecialStates();
        }
    }
}
