# FPS-LAN — Plano de implementação dos Brawlers

Fonte de design: `FPS-LAN_Brawler_Specifications.pdf` (projeto Claude). Este documento descreve a arquitetura técnica e a ordem de implementação.

> Propriedade intelectual: Colt, Dynamike, Bo e Emz são referências de design. No código e nos dados os Brawlers são identificados pelo papel (`gunslinger`, `artillery`, `trapper`, `sprayer`); nomes visíveis e visuais são originais e editáveis em cada `BrawlerDefinition`.

## 1. Diagnóstico inicial (2026-10-04)

**Existia e mantém-se:** rede host/clientes (NGO 2.13, LAN por IP), movimento com autoridade do dono, dano/morte/respawn/kills no host, Deathmatch, `PlayerCommands` (humanos e bots usam os mesmos comandos), bots, HUD/placar, troca de personagem.

**Lacunas face à especificação:**
1. Personagens hardcoded em `HeroCatalog` em vez de dados (`BrawlerDefinition`).
2. Sem FP/TP próprios por personagem (uma arma genérica; corpo = cápsula); sem eventos de apresentação sincronizados.
3. Mecânicas em falta: hitscan, atraso de explosão, 2 explosivos por ataque, knockback, coberturas destrutíveis, aviso/discrição das minas, spray em ticks dependente da distância.
4. Cada máquina calculava a direção do ataque pela sua própria vista → mensagens de ataque passam a levar origem, direção, alvo e seed.
5. Números da especificação dão TTK muito baixo → usados como ponto de partida e afinados em playtest.

## 2. Arquitetura

### Dados (`Assets/_Game/Data/Brawlers/`)
```
BrawlerDefinition (ScriptableObject)
├─ Stats                 vida, regeneração
├─ MovementProfile       andar, sprint, salto
├─ PrimaryAttackDefinition  AttackDefinition + cargas, recarga, cooldown
├─ SuperDefinition       AttackDefinition + dano necessário para carregar
├─ Abilities[]           (gadgets — futuro)
├─ FirstPersonPresentation / ThirdPersonPresentation
├─ AnimationSet          parâmetros procedurais (ou Animator no futuro)
├─ VFXSet / SFXSet
└─ BotProfile

AttackDefinition = AttackDelivery (ScriptableObject polimórfico) + EffectPayload
  Deliveries: Hitscan · Projectile · ArcProjectile · ConeVolume · PlaceTraps · SelfAura
  EffectPayload: dano (+ multiplicador por distância) · splash · DoT · slow · knockback
                 · zona no impacto · atraso de explosão · dano a cobertura
```
`BrawlerRegistry` (em `Resources/`) lista os Brawlers; a rede continua a enviar só o índice.

### Combate (`CombatSystem`)
- Instâncias **autoritativas** (host, invisíveis) decidem o resultado; instâncias **visuais** em todas as máquinas.
- Mensagem de ataque: origem, direção, ponto-alvo (arcos) e seed. O host valida (cargas, cooldown, Super, distância da origem ao jogador) e resolve.
- `EffectResolver`: dano, splash, DoT, slow, knockback, cobertura.
- `StatusEffects` (NetworkBehaviour no Player): slow, DoT; knockback enviado pelo host ao dono, que o aplica ao movimento (limitado).
- `DestructibleCover`: NetworkObject só para coberturas com estado; o resto do mapa fica estático.

### Apresentação FP/TP (Fase 3)
`BrawlerPresentation` cria a rig FP (humano local) ou TP (outros/bots) a partir dos dados; `PresentationEvents` (ataque com índice/cano, Super, recarga, hit, morte, locomoção) a partir das mensagens existentes; animação procedural nas rigs de primitivas, trocável por Animator + modelos depois.

### Novo Brawler
Novo `BrawlerDefinition` + combinação de deliveries/efeitos existentes + rig. Só é preciso código se a mecânica for nova (nova classe de `AttackDelivery`).

## 3. Os 4 Brawlers (pontos de partida da especificação)

| Papel (referência) | Vida | Principal | Super |
|---|---|---|---|
| gunslinger (Colt) | 100 | Projectile ×6, 18 dano, canos alternados, recarga 1,3 s | Rajada maior, perfura, destrói cobertura |
| artillery (Dynamike) | 110 | ArcProjectile ×2, atraso, splash 45, recarga 1,4 s | Barril: área grande, knockback, destrói cobertura |
| trapper (Bo) | 125 | Projectile ×3 em leque, 30 dano + explosão pequena, recarga 1,7 s | PlaceTraps ×3: minas discretas, aviso, explosão |
| sprayer (Emz) | 120 | ConeVolume em ticks, dano depende da distância, recarga 2 s | SelfAura: DoT + slow, segue o jogador |

## 4. Ordem
1. Base de Brawler (dados, registry, gerador, migração do HeroCatalog).
2. Combate partilhado (CombatSystem, EffectResolver, StatusEffects, hitscan, fuse, knockback, cobertura, mensagens determinísticas).
3. Apresentação FP/TP + sincronização.
4. Colt · 5. Dynamike · 6. Bo · 7. Emz.
8. Polish VFX/SFX + bots por perfil.
9. Teste 2–4 jogadores e balanceamento.

Cada fase: implementar → compilar → corrigir → testar no Editor → verificar sistemas antigos → Multiplayer Play Mode (≥2 jogadores).

## 5. Riscos
- Sem arte: rigs de primitivas transmitem timing/intenção, não qualidade final.
- Coberturas destrutíveis exigem objetos de rede na cena (só onde necessário).
- TTK baixo com os números iniciais.

## 6. Fase visual "Arena dos Brawlers" (2026-10-05)

Ver `NOVIDADES.md` para o resumo para jogadores. Notas técnicas:

- **Apresentação FP/TP implementada** (`Scripts/Visual`): `CharacterRig` (TP, rig de primitivas com animação procedural,
  peças juntas por osso via `MeshMerge`), `ViewModelRig` (FP, câmara própria na camada `ViewModel`), `WeaponModels`
  (modelos por `RigStyle`), `ProjectileVisuals` (aspeto dos projéteis por `ProjectileStyle`), `Fx` (partículas partilhadas
  com `Emit`, luzes, tracers, `CameraShake`). Tudo só visual; o combate continua autoritativo no host.
- **Dados novos**: `ThirdPersonPresentation.look` (`CharacterLook`: cores, `HeadGear`, proporções),
  `AttackVisuals.style` (`ProjectileStyle`), `BotProfile.superIsEscape`, `RigStyle.Shotgun/Sniper`.
- **Mecânica nova**: `LeapDelivery` — `AttackDelivery.OnOwnerFire` (novo gancho, corre só em quem controla o atacante)
  lança o jogador com `PlayerMotor.Launch`; as bombas são projéteis em arco normais (`CombatSystem.LaunchArcFrom`).
- **Mundo**: `Bush` (esconde a > 3,4 m, revelado ao atacar/levar dano; bots respeitam), `JumpPad` (usa `PlayerMotor.Launch`),
  `MapInfo` (limites + waypoints dos bots). Barreiras invisíveis na camada *Ignore Raycast*: travam o movimento,
  os tiros passam (`CombatMath.ShotMask`).
- **Escudo de spawn**: `PlayerHealth.ProtectedUntil` (hora do servidor), termina ao atacar.
- **Shaders** (`Assets/_Game/Shaders`): `FPSLAN/Toon`, `FPSLAN/ToonOutline`, `FPSLAN/FxAdditive`, `FPSLAN/FxAlpha`;
  materiais base em `Resources/Art` (gerados pelo `ProjectSetup`) para entrarem no build.
- **Conteúdo versionado**: `ProjectSetup.SetupVersion` + `Data/setup_version.txt`; ao subir a versão o Editor regenera
  dados, materiais, prefab e cena sozinho.
- `CombatSystem.HeadshotHeight` passou de 1,45 para 1,38 m (cabeças maiores nas rigs chibi).
