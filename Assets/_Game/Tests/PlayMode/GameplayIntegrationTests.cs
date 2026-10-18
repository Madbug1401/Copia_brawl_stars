using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPSLAN.Tests
{
    /// <summary>
    /// Integração em Play Mode com a cena Arena real: host + bots, ataques resolvidos pelo pipeline autoritativo
    /// (CombatSystem/EffectResolver/StatusEffects), especiais em modo pelos mesmos comandos que um jogador,
    /// armadilhas sincronizadas e recompensas de fim de partida enviadas pelo host.
    /// Os jogadores de teste ficam numa plataforma acima do mapa para os resultados não dependerem do cenário.
    /// </summary>
    public class GameplayIntegrationTests
    {
        const float FloorY = 150f;
        const ushort TestPort = 7797;
        GameObject m_Floor;
        GameExperienceContext m_Ctx;
        int m_SavedHero;

        // Índices na BrawlerRegistry (ordem estável da rede)
        const int Ekko = 0, LupusMarinus = 1, NCage = 2, MrS = 3, Riot = 4, Lince = 5;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            m_SavedHero = GameSession.LocalHeroId;
            // Perfil em memória (não toca no perfil real) com os assets reais de Resources
            m_Ctx = new GameExperienceContext(Resources.Load<ProgressionConfig>(ProgressionConfig.ResourcePath),
                Resources.Load<PetCatalog>(PetCatalog.ResourcePath), Resources.Load<SkinCatalog>(SkinCatalog.ResourcePath),
                Resources.Load<QuestCatalog>(QuestCatalog.ResourcePath), new MemoryProfileStorage(), "playmode", new SystemClock());
            GameExperience.Override(m_Ctx);

            SceneManager.LoadScene("Arena");
            yield return null;
            yield return null;
            MatchManager.MenuSelectedMode = GameMode.Deathmatch;
            MatchManager.MenuSelectedCompetitive = false;
            GameSession.LocalHeroId = Lince;
            Assert.IsNotNull(ConnectionManager.Instance, "ConnectionManager na cena");
            // Porta própria do teste: não colide com um jogo/servidor LAN aberto na porta 7777
            Assert.IsTrue(ConnectionManager.Instance.Host(TestPort), "host arrancou");
            float t = 0f;
            while (PlayerNetwork.Local == null && t < 10f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Assert.IsNotNull(PlayerNetwork.Local, "jogador local criado");

            m_Floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Floor.name = "TestFloor";
            m_Floor.transform.position = new Vector3(0f, FloorY - 0.5f, 0f);
            m_Floor.transform.localScale = new Vector3(40f, 1f, 40f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (m_Floor != null) Object.Destroy(m_Floor);
            if (ConnectionManager.Instance != null) ConnectionManager.Instance.Leave();
            GameSession.LocalHeroId = m_SavedHero;
            yield return null;
            yield return null;
        }

        // ================================================================== utilitários

        static PlayerNetwork AddBot(int hero)
        {
            Assert.IsTrue(BotManager.AddBot(hero), "bot adicionado");
            return PlayerNetwork.All.Where(p => p != null && p.IsBot).OrderBy(p => p.Id).Last();
        }

        /// <summary>Bot sem IA, sem pet nem melhorias (resultados exatos), parado numa posição.</summary>
        static void Prepare(PlayerNetwork p, Vector3 feet, float yaw, bool motorOn = false)
        {
            if (p.Bot != null) Object.Destroy(p.Bot);
            p.ScriptedCommands = () => default;
            p.Loadout.Value = new PlayerLoadout { characterLevel = 1 };
            p.ServerRefreshEffectiveStats();
            p.Motor.enabled = motorOn;
            Place(p, feet, yaw);
            Ready(p);
        }

        static void Place(PlayerNetwork p, Vector3 feet, float yaw)
        {
            var cc = p.GetComponent<CharacterController>();
            bool was = cc.enabled;
            cc.enabled = false;
            p.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = was;
            Physics.SyncTransforms();
        }

        static void Ready(PlayerNetwork p)
        {
            p.Health.ProtectedUntil.Value = 0;
            p.Status.ServerClear();
            p.Health.Shield.Value = 0;
            p.Health.Health.Value = p.Health.MaxHealth;
        }

        static AttackContext Ctx(PlayerNetwork shooter, bool super, Vector3 aim, int seed = 1234)
        {
            var b = shooter.Brawler;
            var origin = shooter.Motor.EyePosition;
            return new AttackContext
            {
                shooter = shooter,
                attackerId = shooter.Id,
                attack = super ? b.super.attack : b.primary.attack,
                isSuper = super,
                authoritative = true,
                origin = origin,
                direction = (aim - origin).normalized,
                target = aim,
                seed = seed,
            };
        }

        static int Damage(PlayerNetwork victim, int before) => before - victim.Health.Health.Value;

        static Vector3 Head(PlayerNetwork p) => p.transform.position + Vector3.up * CombatMath.HeadCenterHeight;
        static Vector3 Chest(PlayerNetwork p) => p.transform.position + Vector3.up * 1.0f;

        static PlayerCommands Aim(PlayerNetwork self, Vector3 point, bool fire, bool super)
        {
            var d = point - self.Motor.EyePosition;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return new PlayerCommands { absoluteAim = true, yaw = yaw, pitch = pitch, fire = fire, superPressed = super };
        }

        int DamageDealt(PlayerNetwork p) =>
            MatchStatsTracker.ServerBuildResult(p, "probe", GameMode.Deathmatch, false, false, false, 0, NetworkManager.Singleton.ServerTime.Time,
                m_Ctx.Config, m_Ctx.PetCatalog).damageDealt;

        static IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        static Vector3 F(float x, float z) => new Vector3(x, FloorY, z);

        // ================================================================== teste

        [UnityTest]
        public IEnumerator SixBrawlersSpecialsProgressionAndSync()
        {
            var host = PlayerNetwork.Local;
            Assert.AreEqual("Lince", host.Brawler.displayName);
            // Pet equipado por defeito (cão) sincronizado no loadout do dono e criado em jogo
            Assert.AreEqual(m_Ctx.PetCatalog.Find("pet_dog").StableHash, host.Loadout.Value.petHash);
            yield return Wait(0.3f);
            Assert.Greater(Object.FindObjectsByType<PetFollower>(FindObjectsSortMode.None).Length, 0, "pet a seguir o jogador");

            var ekko = AddBot(Ekko);
            var lm = AddBot(LupusMarinus);
            var ncage = AddBot(NCage);
            var mrs = AddBot(MrS);
            var riot = AddBot(Riot);
            var lince = AddBot(Lince);
            var lm2 = AddBot(LupusMarinus);
            yield return Wait(0.6f); // spawns iniciais

            Assert.AreEqual(120, ekko.Health.MaxHealth);
            Assert.AreEqual(100, lm.Health.MaxHealth);
            Assert.AreEqual(110, ncage.Health.MaxHealth);
            Assert.AreEqual(160, mrs.Health.MaxHealth);
            Assert.AreEqual(135, riot.Health.MaxHealth);
            Assert.AreEqual(85, lince.Health.MaxHealth);

            foreach (var p in new[] { ekko, lm, ncage, mrs, riot, lince, lm2 }) Prepare(p, F(15f, 15f), 0f);
            host.ScriptedCommands = () => default;
            host.Motor.enabled = false;
            Place(host, F(-15f, -15f), 0f);
            Ready(host);
            yield return null;

            // ---------------- Lince: 38 corpo / 50 cabeça (hitscan validado no host)
            Place(lince, F(0f, 0f), 0f);
            Place(ekko, F(0f, 8f), 180f);
            Ready(ekko);
            int hp = ekko.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(lince, false, Head(ekko)));
            Assert.AreEqual(50, Damage(ekko, hp), "headshot da Lince");
            Ready(ekko);
            hp = ekko.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(lince, false, Chest(ekko)));
            Assert.AreEqual(38, Damage(ekko, hp), "corpo da Lince");

            // ---------------- Tiro fatal: corpo não elimina, cabeça elimina; paredes bloqueiam
            Ready(ekko);
            hp = ekko.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(lince, true, Chest(ekko)));
            Assert.AreEqual(38, Damage(ekko, hp), "tiro fatal no corpo = dano normal");
            Assert.IsFalse(ekko.Health.IsDead);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = F(0f, 4f) + Vector3.up * 1.5f;
            wall.transform.localScale = new Vector3(3f, 3f, 0.4f);
            Physics.SyncTransforms();
            Ready(ekko);
            CombatSystem.Instance.StartAttack(Ctx(lince, true, Head(ekko)));
            Assert.IsFalse(ekko.Health.IsDead, "a parede bloqueia o tiro fatal");
            Object.DestroyImmediate(wall);
            Physics.SyncTransforms();
            CombatSystem.Instance.StartAttack(Ctx(lince, true, Head(ekko)));
            Assert.IsTrue(ekko.Health.IsDead, "headshot com tiro fatal elimina");
            yield return Wait(3.4f); // reaparece
            Assert.IsFalse(ekko.Health.IsDead);
            Prepare(ekko, F(15f, 15f), 0f);

            // ---------------- Riot: disparo completo de perto = 54
            Place(riot, F(-6f, 0f), 0f);
            Place(mrs, F(-6f, 1.4f), 180f);
            Ready(mrs);
            hp = mrs.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(riot, false, Chest(mrs)));
            yield return Wait(0.4f);
            Assert.AreEqual(54, Damage(mrs, hp), "caçadeira de perto");

            // ---------------- N.Cage: três flechas = 100
            Place(ncage, F(6f, 0f), 0f);
            Place(riot, F(6f, 3f), 180f);
            Ready(riot);
            hp = riot.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(ncage, false, Chest(riot)));
            yield return Wait(0.4f);
            Assert.AreEqual(100, Damage(riot, hp), "três flechas no mesmo alvo");

            // ---------------- Ekko: rajada de perto = 60 (5 balas, alterna as mãos)
            Place(ekko, F(-12f, 0f), 0f);
            Place(mrs, F(-12f, 3f), 180f);
            Ready(mrs);
            hp = mrs.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(ekko, false, Chest(mrs)));
            yield return Wait(0.6f);
            Assert.AreEqual(60, Damage(mrs, hp), "rajada do Ekko de perto");

            // ---------------- L.M: TNT com acerto direto = 40
            Place(lm, F(12f, -8f), 0f);
            Place(ncage, F(12f, -2f), 180f);
            Ready(ncage);
            hp = ncage.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(lm, false, Chest(ncage)));
            yield return Wait(1.3f);
            Assert.AreEqual(40, Damage(ncage, hp), "TNT acerto direto");

            // ---------------- Mr.S: spray completo de perto = 56 (entre 50 e 60)
            Place(mrs, F(-6f, -8f), 0f);
            Place(lince, F(-6f, -6f), 180f);
            Ready(lince);
            hp = lince.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(mrs, false, Chest(lince)));
            yield return Wait(0.9f);
            int spray = Damage(lince, hp);
            Assert.That(spray, Is.InRange(50, 60), "spray do Mr.S");

            // ---------------- N.Cage: armadilhas prendem 5 s, 40 de veneno, sem cadeias
            Place(ncage, F(0f, -14f), 0f);
            var snareCtx = Ctx(ncage, true, F(0f, -8f));
            CombatSystem.Instance.StartAttack(snareCtx);
            Assert.AreEqual(3, CombatSystem.Instance.TrapCount(ncage.Id, true), "três armadilhas no host");
            yield return null;
            Assert.AreEqual(3, CombatSystem.Instance.TrapCount(ncage.Id, false), "visuais sincronizados pelo host");
            yield return Wait(0.8f); // arma
            var snares = (PlaceTrapsDelivery)ncage.Brawler.super.attack.delivery;
            Prepare(riot, snares.DesiredPosition(snareCtx, 0), 0f, true);
            int riotHp = riot.Health.Health.Value;
            riot.ScriptedCommands = () => new PlayerCommands { move = new Vector2(0f, 1f), sprint = true, absoluteAim = true, yaw = 90f };
            yield return Wait(0.3f);
            Assert.IsTrue(riot.Status.IsRooted, "preso");
            var rootedAt = riot.transform.position;
            yield return Wait(1.0f);
            Assert.Less(Vector3.Distance(Flat(rootedAt), Flat(riot.transform.position)), 0.05f, "não se mexe preso");
            Assert.AreEqual(2, CombatSystem.Instance.TrapCount(ncage.Id, true), "a armadilha desaparece ao disparar");
            yield return Wait(4.2f);
            Assert.IsFalse(riot.Status.IsRooted, "solta-se ao fim de 5 s");
            Assert.AreEqual(40, riotHp - riot.Health.Health.Value, "40 de veneno no total");
            // Imune logo a seguir: outra armadilha não o volta a prender
            riot.ScriptedCommands = () => default;
            Place(riot, snares.DesiredPosition(snareCtx, 1), 0f);
            yield return Wait(0.3f);
            Assert.IsFalse(riot.Status.IsRooted, "sem cadeia de imobilizações");
            Assert.AreEqual(2, CombatSystem.Instance.TrapCount(ncage.Id, true));
            riot.Motor.enabled = false;
            Place(riot, F(15f, 15f), 0f);

            // ---------------- L.M: cinco minas com sensor; aviso e explosão; destruídas por explosão/facada inimiga
            Place(lm, F(-14f, 10f), 0f);
            var minesCtx = Ctx(lm, true, F(-14f, 16f));
            CombatSystem.Instance.StartAttack(minesCtx);
            Assert.AreEqual(5, CombatSystem.Instance.TrapCount(lm.Id, true), "cinco minas");
            yield return null;
            Assert.AreEqual(5, CombatSystem.Instance.TrapCount(lm.Id, false), "visuais sincronizados pelo host");
            var mines = (PlaceTrapsDelivery)lm.Brawler.super.attack.delivery;
            yield return Wait(1.0f); // armam
            var mine0 = mines.DesiredPosition(minesCtx, 0);
            var outward = Flat(mine0 - minesCtx.target).normalized;
            Place(mrs, mine0 + outward * 0.6f, 0f);
            Ready(mrs);
            hp = mrs.Health.Health.Value;
            yield return Wait(0.2f);
            Assert.AreEqual(hp, mrs.Health.Health.Value, "aviso antes da explosão");
            yield return Wait(0.5f);
            Assert.Greater(Damage(mrs, hp), 0, "a mina explodiu");
            Assert.AreEqual(4, CombatSystem.Instance.TrapCount(lm.Id, true), "as outras minas do mesmo dono ficam");
            // Explosão de um inimigo (outro L.M) destrói minas no raio
            Place(lm2, F(-20f, 16f), 90f);
            Ready(lm2);
            var boom = Ctx(lm2, false, mines.DesiredPosition(minesCtx, 2));
            EffectResolver.Explosion(boom, mines.DesiredPosition(minesCtx, 2));
            Assert.Less(CombatSystem.Instance.TrapCount(lm.Id, true), 4, "explosão inimiga destrói minas");
            Place(mrs, F(15f, 15f), 0f);

            // ---------------- Mr.S: granadas de veneno, zona de 12 s, sem acumulação do mesmo atacante
            Place(mrs, F(10f, 8f), 0f);
            Place(riot, F(10f, 14f), 0f);
            Ready(riot);
            int victimHp = riot.Health.Health.Value;
            CombatSystem.Instance.StartAttack(Ctx(mrs, true, F(10f, 14f)));
            yield return Wait(1.5f); // voo + aterrar + primeiro tick
            int afterLanding = riot.Health.Health.Value;
            yield return Wait(3f);
            int perThreeSeconds = afterLanding - riot.Health.Health.Value;
            Assert.That(perThreeSeconds, Is.InRange(16, 32), "≈8/s mesmo com duas zonas sobrepostas (sem acumulação)");
            Assert.IsTrue(riot.Status.HasDot.Value == false, "zona: dano por tick, não DoT");
            yield return Wait(8.6f); // passa dos 12 s da zona
            Assert.IsFalse(riot.Health.IsDead);
            int endHp = riot.Health.Health.Value;
            yield return Wait(1.3f);
            Assert.AreEqual(endHp, riot.Health.Health.Value, "a zona desaparece aos 12 s");
            Assert.Greater(victimHp - endHp, 48, "a zona durou mais de 6 ticks");
            Place(riot, F(15f, 15f), 0f);

            // ---------------- Ekko: Minigun pelo mesmo caminho de um jogador (ativar → rajadas), máx. 200
            Prepare(ekko, F(-10f, -12f), 0f, true);
            Place(riot, F(-10f, -7f), 180f);
            Ready(riot);
            ekko.Combat.SuperCharge.Value = 1f;
            int pressFrame = Time.frameCount + 2;
            bool sawMode = false, modeEnded = false;
            // Dispara só enquanto a Minigun está ativa: depois do modo, o ataque principal voltaria a carregar o Super
            ekko.ScriptedCommands = () => Aim(ekko, Chest(riot), Time.frameCount > pressFrame + 1 && !modeEnded, Time.frameCount == pressFrame);
            int dealtBefore = DamageDealt(ekko);
            int minigunDamage = 0;
            float chargeAtEnd = -1f;
            float start = Time.time;
            while (Time.time - start < 5f)
            {
                if (ekko.Combat.ModeActive.Value) sawMode = true;
                else if (sawMode && !modeEnded)
                {
                    modeEnded = true;
                    minigunDamage = DamageDealt(ekko) - dealtBefore;
                    chargeAtEnd = ekko.Combat.SuperCharge.Value;
                }
                Ready(riot); // mantém o alvo vivo para somar todo o dano
                yield return null;
            }
            Assert.IsTrue(sawMode, "modo Minigun ativado");
            Assert.IsTrue(modeEnded, "a Minigun terminou");
            Assert.IsFalse(ekko.Combat.ModeActive.Value, "volta às metralhadoras");
            Assert.AreEqual(0f, chargeAtEnd, 0.001f, "especial gasto");
            Assert.AreEqual(0f, ekko.Combat.SuperCharge.Value, 0.001f, "não recarrega sem disparar");
            Assert.Greater(minigunDamage, 100, "a Minigun acertou");
            Assert.LessOrEqual(minigunDamage, 200, "nunca mais de 200");
            ekko.ScriptedCommands = () => default;

            // ---------------- Lince: armar, cancelar (guarda a carga), armar e eliminar com headshot
            Prepare(lince, F(4f, -16f), 0f, true);
            Place(mrs, F(4f, -6f), 180f);
            Ready(mrs);
            lince.Combat.SuperCharge.Value = 1f;
            int press = -1;
            lince.ScriptedCommands = () => Aim(lince, Head(mrs), false, Time.frameCount == press);
            press = Time.frameCount + 1;
            yield return null;
            yield return null;
            yield return Wait(0.3f);
            Assert.IsTrue(lince.Combat.ModeActive.Value, "tiro fatal armado");
            Assert.AreEqual(1f, lince.Combat.SuperCharge.Value, 0.001f, "a carga só é gasta ao disparar");
            press = Time.frameCount + 1;
            yield return null;
            yield return null;
            yield return Wait(0.3f);
            Assert.IsFalse(lince.Combat.ModeActive.Value, "cancelado");
            Assert.AreEqual(1f, lince.Combat.SuperCharge.Value, 0.001f, "carga guardada");
            press = Time.frameCount + 1;
            yield return null;
            yield return null;
            yield return Wait(0.5f);
            Assert.IsTrue(lince.Combat.ModeActive.Value, "armado outra vez");
            Ready(mrs);
            lince.ScriptedCommands = () => Aim(lince, Head(mrs), true, false);
            yield return Wait(0.3f);
            lince.ScriptedCommands = () => default;
            Assert.IsTrue(mrs.Health.IsDead, "eliminação imediata com headshot");
            Assert.AreEqual(0f, lince.Combat.SuperCharge.Value, 0.03f);
            Assert.IsFalse(lince.Combat.ModeActive.Value);
            yield return Wait(0.2f);

            // ---------------- Skin equipada viaja no loadout (todos veem a mesma skin)
            m_Ctx.Inventory.UnlockSkin("skin_lince_snow");
            m_Ctx.Skins.Equip(SkinTarget.Brawler, "sniper", "skin_lince_snow");
            yield return null;
            Assert.AreEqual(m_Ctx.SkinCatalog.Find("skin_lince_snow").StableHash, host.Loadout.Value.skinHash);

            // ---------------- Fim de partida: o host valida e envia o resultado só ao dono, uma vez
            Ready(ekko);
            Place(host, F(-3f, 12f), 0f);
            Place(ekko, F(-3f, 18f), 180f);
            CombatSystem.Instance.StartAttack(Ctx(host, false, Chest(ekko))); // atividade do jogador local
            yield return Wait(0.2f);
            var match = MatchManager.Instance;
            string matchId = match.MatchId.Value.ToString();
            match.PhaseEndTime.Value = NetworkManager.Singleton.ServerTime.Time;
            float waitStart = Time.time;
            while (m_Ctx.Progression.LastReport == null && Time.time - waitStart < 5f) yield return null;
            var report = m_Ctx.Progression.LastReport;
            Assert.IsNotNull(report, "resultado recebido do host");
            Assert.AreEqual(matchId, report.matchId);
            Assert.IsTrue(report.participated, "participação válida");
            Assert.Greater(report.xpGained, 0);
            Assert.AreEqual("sniper", report.brawlerId);
            Assert.Greater(m_Ctx.Profile.playerXp, 0);
            long xp = m_Ctx.Profile.playerXp;
            Assert.IsTrue(m_Ctx.Progression.ApplyMatchResult(report.data).duplicate, "não repete XP");
            Assert.AreEqual(xp, m_Ctx.Profile.playerXp);
            Assert.Greater(m_Ctx.PetProgression.Xp("pet_dog"), 0, "o pet equipado ganhou XP");
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
