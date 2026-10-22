using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Ataque principal (cargas que recarregam sozinhas) e Super (carrega ao causar dano e/ou com o tempo), a partir do BrawlerDefinition.
    ///
    /// Fluxo de rede:
    /// 1. Quem controla o jogador (dono humano, ou host para bots) mostra logo o ataque e envia a INTENÇÃO
    ///    (origem, direção, ponto-alvo, seed) ao host.
    /// 2. O host valida (vivo, cargas/cooldown no lado do servidor, Super carregado, origem perto do jogador,
    ///    alvo dentro do alcance) e simula a instância autoritativa — só ela causa dano.
    /// 3. O host reenvia a mesma intenção aos outros clientes, que mostram o mesmo ataque.
    ///
    /// Supers do tipo "modo" (SuperActivation.Mode): ativar o Super liga um modo validado no host em que o ataque
    /// principal usa o ataque do Super, com um número máximo de disparos (limita o dano total) e duração opcional.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        const float MaxOriginError = 3f;

        /// <summary>0..1 — decidido pelo servidor.</summary>
        public readonly NetworkVariable<float> SuperCharge = new NetworkVariable<float>(0f);
        /// <summary>Modo do Super ativo (Minigun, tiro fatal...) — decidido pelo servidor.</summary>
        public readonly NetworkVariable<bool> ModeActive = new NetworkVariable<bool>(false);
        /// <summary>Hora do servidor em que o modo termina (0 = sem limite de tempo).</summary>
        public readonly NetworkVariable<double> ModeEndTime = new NetworkVariable<double>(0);
        public readonly NetworkVariable<int> ModeShotsLeft = new NetworkVariable<int>(0);

        PlayerNetwork m_Player;

        // Lado do dono (previsão local para resposta imediata)
        float m_Ammo;
        float m_Cooldown;
        float m_MeleeCooldown;
        bool m_LocalMode;
        int m_LocalModeShots;
        float m_LocalModeStart, m_LocalModeEnd, m_LocalModeReadyAt;
        bool m_LocalModeFired;

        // Lado do servidor (validação)
        float m_ServerAmmo;
        float m_ServerLastFire = -10f;
        float m_ServerLastMelee = -10f;
        float m_ServerModeStart;
        float m_ServerLastModeShot = -10f;
        bool m_ServerModeFired;

        public float Ammo => m_Ammo;
        public bool SuperReady => SuperCharge.Value >= 0.999f;
        public bool CanAttackNow => m_Cooldown <= 0f && m_Ammo >= 1f;

        /// <summary>O modo do Super está ativo (dono: previsão local; outros: estado do servidor).</summary>
        public bool InSuperMode => IsOwner ? m_LocalMode : ModeActive.Value;
        /// <summary>Segundos que faltam do modo (0 se sem limite ou inativo). Só exato no dono.</summary>
        public float ModeRemaining => InSuperMode && m_LocalModeEnd > 0f ? Mathf.Max(0f, m_LocalModeEnd - Time.time) : 0f;
        public float ModeDuration => Brawler != null ? Brawler.super.mode.duration : 0f;
        public int ModeShotsRemaining => IsOwner ? m_LocalModeShots : ModeShotsLeft.Value;
        /// <summary>A animação de ativação do modo ainda está a decorrer (0..1).</summary>
        public float ModeActivation01
        {
            get
            {
                var b = Brawler;
                if (!InSuperMode || b == null || b.super.mode.activationDelay <= 0f) return 1f;
                return Mathf.Clamp01((Time.time - m_LocalModeStart) / b.super.mode.activationDelay);
            }
        }

        BrawlerDefinition Brawler => m_Player.Brawler;

        void Awake() => m_Player = GetComponent<PlayerNetwork>();

        public override void OnNetworkSpawn()
        {
            int charges = Brawler != null ? Brawler.primary.maxCharges : 3;
            if (IsOwner) m_Ammo = charges;
            if (IsServer) m_ServerAmmo = charges;
            m_Player.HeroId.OnValueChanged += OnBrawlerChanged;
            ModeActive.OnValueChanged += OnModeActiveChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Player.HeroId.OnValueChanged -= OnBrawlerChanged;
            ModeActive.OnValueChanged -= OnModeActiveChanged;
        }

        void OnBrawlerChanged(int previous, int current)
        {
            if (IsOwner)
            {
                m_Ammo = Brawler.primary.maxCharges;
                m_Cooldown = 0f;
                m_MeleeCooldown = 0f;
                EndLocalMode();
            }
            if (IsServer)
            {
                m_ServerAmmo = Brawler.primary.maxCharges;
                ServerEndMode();
            }
        }

        void OnModeActiveChanged(bool previous, bool current)
        {
            // Os outros veem a transformação quando o host confirma; o dono já a mostrou ao ativar
            if (!IsOwner) m_Player.Visuals?.OnSuperModeChanged(current);
        }

        float ReloadTime(BrawlerDefinition b) => Mathf.Max(0.05f, b.primary.reloadTimePerCharge * m_Player.ReloadTimeMultiplier);

        void Update()
        {
            if (!IsSpawned) return;
            var b = Brawler;
            if (b == null) return;
            float dt = Time.deltaTime;

            if (IsServer) ServerUpdate(b, dt);

            if (!IsOwner) return;

            if (m_Ammo < b.primary.maxCharges)
            {
                int before = Mathf.FloorToInt(m_Ammo);
                m_Ammo = Mathf.Min(b.primary.maxCharges, m_Ammo + dt / ReloadTime(b));
                if (Mathf.FloorToInt(m_Ammo) > before && m_Player.IsLocalHuman)
                {
                    Sfx.Play2D(Sound.Reload, 0.6f);
                    m_Player.Visuals?.OnChargeReloaded();
                }
            }

            m_Cooldown -= dt;
            m_MeleeCooldown -= dt;
            UpdateLocalMode(b);

            if (m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;

            var cmd = m_Player.ReadCommands();

            if (cmd.meleePressed && m_MeleeCooldown <= 0f)
            {
                var melee = b.melee ?? new MeleeProfile();
                m_MeleeCooldown = Mathf.Max(0.1f, melee.cooldown);
                MeleeLocal();
            }

            // Ativar/cancelar um Super de modo não depende do cooldown do ataque
            if (cmd.superPressed && b.super.attack != null && b.super.IsMode)
            {
                if (!m_LocalMode && SuperReady) ActivateModeLocal(b);
                else if (m_LocalMode && b.super.mode.cancellable && !m_LocalModeFired) CancelModeLocal();
                return;
            }

            if (m_Cooldown > 0f) return;

            if (m_LocalMode)
            {
                // Durante o modo, o ataque principal usa o ataque do Super (ex.: rajadas da Minigun)
                if (cmd.fire && Time.time >= m_LocalModeReadyAt && m_LocalModeShots > 0)
                {
                    m_LocalModeShots--;
                    m_LocalModeFired = true;
                    m_Cooldown = Mathf.Max(0.05f, b.super.mode.shotCooldown);
                    FireLocal(true);
                    if (m_LocalModeShots <= 0) EndLocalMode();
                }
                return;
            }

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

        // ================================================================== modo do Super (dono)

        void ActivateModeLocal(BrawlerDefinition b)
        {
            var mode = b.super.mode;
            m_LocalMode = true;
            m_LocalModeFired = false;
            m_LocalModeShots = Mathf.Max(1, mode.maxShots);
            m_LocalModeStart = Time.time;
            m_LocalModeReadyAt = Time.time + Mathf.Max(0f, mode.activationDelay);
            m_LocalModeEnd = mode.duration > 0f ? Time.time + mode.activationDelay + mode.duration : 0f;
            m_Player.Visuals?.OnSuperModeChanged(true);
            if (m_Player.IsLocalHuman)
            {
                Sfx.Play2D(b.super.attack.visuals.sound == Sound.Minigun ? Sound.MinigunSpin : Sound.SuperReady, 0.8f);
                if (!string.IsNullOrEmpty(mode.label)) GameUI.Toast(mode.label.ToUpperInvariant() + "!", Color.Lerp(b.vfx.superColor, Color.white, 0.3f));
            }
            ActivateModeRpc();
        }

        void CancelModeLocal()
        {
            EndLocalMode();
            CancelModeRpc();
            if (m_Player.IsLocalHuman) GameUI.Toast("ESPECIAL GUARDADO", new Color(0.8f, 0.9f, 1f));
        }

        void EndLocalMode()
        {
            if (!m_LocalMode) return;
            m_LocalMode = false;
            m_LocalModeShots = 0;
            m_Player.Visuals?.OnSuperModeChanged(false);
        }

        void UpdateLocalMode(BrawlerDefinition b)
        {
            if (!m_LocalMode) return;
            bool timeUp = m_LocalModeEnd > 0f && Time.time >= m_LocalModeEnd;
            // O host terminou o modo (ex.: tempo, morte): depois de dar tempo à confirmação de ativação chegar
            bool serverEnded = !ModeActive.Value && Time.time - m_LocalModeStart > 0.75f;
            if (timeUp || serverEnded || m_Player.Health.IsDead || !b.super.IsMode) EndLocalMode();
        }

        // ================================================================== servidor

        void ServerUpdate(BrawlerDefinition b, float dt)
        {
            if (m_ServerAmmo < b.primary.maxCharges)
                m_ServerAmmo = Mathf.Min(b.primary.maxCharges, m_ServerAmmo + dt / ReloadTime(b));

            bool alive = !m_Player.Health.IsDead;
            // Carga automática com o tempo (ex.: Lince), parada durante o modo
            if (alive && b.super.passiveChargePerSecond > 0f && !ModeActive.Value && SuperCharge.Value < 1f &&
                MatchManager.IsCombatActive && !MatchManager.IsMatchOver)
            {
                bool wasReady = SuperReady;
                SuperCharge.Value = Mathf.Min(1f, SuperCharge.Value + b.super.passiveChargePerSecond * m_Player.SuperChargeMultiplier * dt);
                if (!wasReady && SuperReady && !m_Player.IsBot) SuperReadyRpc();
            }

            if (ModeActive.Value)
            {
                bool expired = ModeEndTime.Value > 0 && NetworkManager.ServerTime.Time > ModeEndTime.Value;
                if (expired || !alive || !b.super.IsMode || MatchManager.IsMatchOver) ServerEndMode();
            }
        }

        [Rpc(SendTo.Server)]
        void ActivateModeRpc()
        {
            var b = Brawler;
            if (b == null || !b.super.IsMode || b.super.attack == null || ModeActive.Value) return;
            if (m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            if (SuperCharge.Value < 0.999f) return;

            var mode = b.super.mode;
            m_ServerModeStart = Time.time;
            m_ServerModeFired = false;
            ModeShotsLeft.Value = Mathf.Max(1, mode.maxShots);
            // Tolerância de latência no fim; o número de disparos é que limita o dano
            ModeEndTime.Value = mode.duration > 0f ? NetworkManager.ServerTime.Time + mode.activationDelay + mode.duration + 0.35f : 0;
            ModeActive.Value = true;
            if (!mode.consumeOnFirstShot) SuperCharge.Value = 0f;
            m_Player.Health.ServerNotifyAttacked();
        }

        [Rpc(SendTo.Server)]
        void CancelModeRpc()
        {
            var b = Brawler;
            if (b == null || !ModeActive.Value || !b.super.mode.cancellable || m_ServerModeFired) return;
            ServerEndMode();
        }

        void ServerEndMode()
        {
            if (!IsServer) return;
            ModeActive.Value = false;
            ModeShotsLeft.Value = 0;
            ModeEndTime.Value = 0;
        }

        /// <summary>Servidor: o dono foi eliminado.</summary>
        public void ServerOnOwnerDied() => ServerEndMode();

        AttackDefinition AttackFor(bool super) => super ? Brawler.super.attack : Brawler.primary.attack;

        AttackContext MakeContext(AttackDefinition attack, bool super, bool authoritative, Vector3 origin, Vector3 direction, Vector3 target, int seed)
        {
            var ctx = new AttackContext
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
            if (!authoritative) m_Player.Visuals?.ApplySkinColors(ctx);
            return ctx;
        }

        void FireLocal(bool super)
        {
            var attack = AttackFor(super);
            var origin = m_Player.Motor.EyePosition;
            var dir = m_Player.Motor.AimDirection;
            var target = CombatMath.AimPoint(m_Player, attack.Range);
            int seed = Random.Range(int.MinValue, int.MaxValue);

            var ctx = MakeContext(attack, super, false, origin, dir, target, seed);
            CombatSystem.Instance.StartAttack(ctx);
            attack.delivery.OnOwnerFire(m_Player, ctx);
            m_Player.Visuals?.OnAttackStarted(attack, super);
            FireRpc(super, origin, dir, target, seed, ViewTime());
        }

        /// <summary>
        /// Cliente: hora (relógio do servidor) a que estava a ver os outros jogadores neste frame. O host usa-a para os
        /// recuar ao validar o ataque (compensação de latência). Host e bots: -1 (testam no próprio PC).
        /// </summary>
        double ViewTime()
        {
            if (IsServer) return -1;
            double sum = 0; int n = 0;
            foreach (var p in PlayerNetwork.All)
            {
                if (p == null || p == m_Player || !p.IsSpawned || p.Motor == null || p.Motor.IsOwner) continue;
                sum += p.Motor.RenderTime;
                n++;
            }
            return n > 0 ? sum / n : -1;
        }

        void MeleeLocal()
        {
            var origin = m_Player.Motor.EyePosition;
            var direction = m_Player.Motor.AimDirection;
            m_Player.Visuals?.OnMeleeStarted();
            Sfx.PlayAt(Sound.Melee, origin, m_Player.IsLocalHuman ? 0.75f : 1f);
            MeleeRpc(origin, direction, ViewTime());
        }

        [Rpc(SendTo.Server)]
        void MeleeRpc(Vector3 origin, Vector3 direction, double viewTime)
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
            bool rewound = LagCompensation.Begin(m_Player, LagCompensation.LagFor(m_Player, viewTime));
            try { CombatSystem.Instance.TraceMelee(m_Player, origin, direction, profile); }
            finally { if (rewound) LagCompensation.End(); }
            RemoteMeleeRpc();
        }

        [Rpc(SendTo.NotOwner)]
        void RemoteMeleeRpc()
        {
            m_Player.Visuals?.OnMeleeStarted();
            Sfx.PlayAt(Sound.Melee, m_Player.Motor.EyePosition, 0.8f);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(bool super, Vector3 origin, Vector3 direction, Vector3 target, int seed, double viewTime)
        {
            var b = Brawler;
            if (b == null || m_Player.Health.IsDead || !MatchManager.IsCombatActive || MatchManager.IsMatchOver) return;
            var attack = AttackFor(super);
            if (attack == null) return;

            // --- Validação no host ---
            if (super && b.super.IsMode)
            {
                // Disparo do modo: só com o modo ativo, depois da ativação, com disparos restantes e sem spam
                var mode = b.super.mode;
                if (!ModeActive.Value || ModeShotsLeft.Value <= 0) return;
                if (Time.time - m_ServerModeStart < mode.activationDelay * 0.5f) return;
                if (Time.time - m_ServerLastModeShot < Mathf.Max(0.05f, mode.shotCooldown) * 0.6f) return;
                m_ServerLastModeShot = Time.time;
                ModeShotsLeft.Value--;
                if (mode.consumeOnFirstShot && !m_ServerModeFired) SuperCharge.Value = 0f;
                m_ServerModeFired = true;
                if (ModeShotsLeft.Value <= 0) ServerEndMode();
            }
            else if (super)
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
            var serverCtx = MakeContext(attack, super, true, origin, direction, target, seed);
            serverCtx.lag = LagCompensation.LagFor(m_Player, viewTime);
            CombatSystem.Instance.StartAttack(serverCtx);
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

        // ================================================================== armadilhas (sincronizadas pelo host)

        /// <summary>Servidor: avisa todas as máquinas da posição validada de uma armadilha.</summary>
        public void ServerBroadcastTrapPlaced(bool super, int seed, int index, Vector3 position)
        {
            if (IsServer) TrapPlacedRpc(super, seed, (byte)Mathf.Clamp(index, 0, 255), position);
        }

        [Rpc(SendTo.Everyone)]
        void TrapPlacedRpc(bool super, int seed, byte index, Vector3 position)
        {
            if (Brawler == null || CombatSystem.Instance == null) return;
            CombatSystem.Instance.SpawnTrapVisual(m_Player, AttackFor(super), super, seed, index, position);
        }

        /// <summary>Servidor: evento decidido no host (disparo, explosão, imobilização, expirou, destruída).</summary>
        public void ServerBroadcastTrapEvent(int seed, int index, TrapEvent evt, ulong victimId)
        {
            if (IsServer) TrapEventRpc(seed, (byte)Mathf.Clamp(index, 0, 255), (byte)evt, victimId);
        }

        [Rpc(SendTo.Everyone)]
        void TrapEventRpc(int seed, byte index, byte evt, ulong victimId)
        {
            if (CombatSystem.Instance == null) return;
            CombatSystem.Instance.OnTrapEvent(m_Player.Id, seed, index, (TrapEvent)evt, victimId);
            if ((TrapEvent)evt == TrapEvent.Sprung)
            {
                var victim = PlayerNetwork.Find(victimId);
                if (victim != null && victim.IsLocalHuman) GameUI.Toast("ROOTED!", new Color(0.55f, 1f, 0.45f));
            }
        }

        // ================================================================== confirmações

        /// <summary>Servidor: chamado pelo EffectResolver quando um ataque deste jogador acerta alguém.</summary>
        public void ServerOnDealtDamage(int damage, bool fromSuper, bool headshot, bool killed, bool executed, Vector3 victimPosition)
        {
            if (!fromSuper && Brawler != null)
            {
                float required = Mathf.Max(1f, Brawler.super.chargeRequired);
                bool wasReady = SuperCharge.Value >= 0.999f;
                // Durante um modo (ex.: Minigun) o Super não volta a carregar
                if (!ModeActive.Value)
                    SuperCharge.Value = Mathf.Min(1f, SuperCharge.Value + damage * m_Player.SuperChargeMultiplier / required);
                if (!wasReady && SuperCharge.Value >= 0.999f && !m_Player.IsBot) SuperReadyRpc();
            }
            if (!m_Player.IsBot) HitConfirmRpc(damage, headshot, killed, executed, victimPosition);
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(int damage, bool headshot, bool killed, bool executed, Vector3 victimPosition)
        {
            if (!m_Player.IsLocalHuman) return;
            GameUI.ShowHitMarker(headshot, killed);
            GameUI.AddDamageNumber(victimPosition + Vector3.up * 2.1f, damage, headshot, killed);
            if (executed)
            {
                // O tiro fatal já tocou no disparo; o acerto usa a confirmação abaixo.
                GameUI.ShowHeadshot(true);
            }
            else if (headshot) GameUI.ShowHeadshot(false);

            if (killed)
            {
                Sfx.Play2D(Sound.Kill, 0.9f);
                GameUI.AnnounceKill();
                m_Player.Visuals?.OnOwnerScored();
            }
            else Sfx.Play2D(headshot ? Sound.HeadHit : Sound.Hit, 0.7f);
        }

        [Rpc(SendTo.Owner)]
        void SuperReadyRpc()
        {
            if (!m_Player.IsLocalHuman) return;
            Sfx.Play2D(Sound.SuperReady, 0.8f);
            var b = Brawler;
            string label = b != null && b.super.IsMode && !string.IsNullOrEmpty(b.super.mode.label) ? b.super.mode.label.ToUpperInvariant() + " PRONTO!" : "SUPER PRONTO!";
            GameUI.Toast(label, new Color(1f, 0.8f, 0.2f));
        }

        /// <summary>Servidor: feedback de uma habilidade de pet (ex.: escudo do Porco) para toda a gente.</summary>
        public void ServerPetAbilityFeedback(PetAbilityType ability)
        {
            if (IsServer) PetAbilityRpc((byte)ability);
        }

        [Rpc(SendTo.Everyone)]
        void PetAbilityRpc(byte ability)
        {
            m_Player.Visuals?.OnPetAbility((PetAbilityType)ability);
            if (m_Player.IsLocalHuman && (PetAbilityType)ability == PetAbilityType.Protection)
                GameUI.Toast("ESCUDO DE LAMA!", new Color(1f, 0.7f, 0.8f));
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
            SuperCharge.Value = 0f;
            ServerEndMode();
        }
    }
}
