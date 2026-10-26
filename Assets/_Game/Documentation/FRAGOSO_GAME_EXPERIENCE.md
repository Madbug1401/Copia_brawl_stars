# FPS-LAN — Game Experience e novos Brawlers (branch `Fragoso`)

Este documento resume a implementação da área de **Game Experience**: os seis Brawlers renomeados e reequilibrados,
os seus especiais, a progressão (XP, níveis, melhorias), os pets, as skins, as missões e as recompensas.

Todos os valores de balanceamento vivem em **ScriptableObjects** e podem ser alterados no Inspector sem mexer em código.

---

## 1. Personagens e valores finais

Os IDs técnicos (`brawlerId`), a ordem na `BrawlerRegistry` (usada pela rede) e os nomes das pastas foram preservados.
Só mudaram os nomes visíveis (`displayName`), por isso prefabs, saves e mensagens de rede antigos continuam válidos.

| ID técnico | Nome antigo | Nome novo | Vida |
|---|---|---|---|
| `gunslinger` | Dex | **Ekko** | 120 |
| `sprayer` | Névoa | **Mr.S** | 160 |
| `shotgunner` | Brasa | **Riot** | 135 (mantida) |
| `artillery` | Cardo | **Lupus Marinus (L.M)** | 100 |
| `trapper` | Vigia | **N.Cage** | 110 |
| `sniper` | Lince | **Lince** | 85 (mantida) |

### Ekko — duas metralhadoras compactas
- Principal: rajada automática de 5 balas com dispersão, recuo e alternância visual entre as duas armas.
  Dano por bala **12 de perto → 7 no alcance máximo** (queda a partir de uma fração configurável do alcance).
  Rajada completa de perto = **60**.
- Especial **Minigun** (modo temporário, exige carga): substitui o ataque, cadência maior, indicador de duração,
  som/efeitos próprios. Dano potencial máximo **200** (`maxShots × balas × dano`), nunca aplicado de uma vez.
  No fim regressa às metralhadoras.

### Mr.S — duas latas de spray
- Principal: cone de curto alcance com dois jatos; 4 ticks agendados com **limite de ticks por alvo**,
  para que vários colliders/partículas não multipliquem o dano. Total por ataque completo **50–60** (≈56).
- Especial **Granadas de veneno**: granadas em arco que criam uma zona tóxica de **12 s** com dano por tick.
  Zonas do mesmo atacante **não acumulam** (`nonStackingPerAttacker`). Zona verde/roxa visível, removida aos 12 s.

### Riot — shotgun
- Principal: caçadeira **pump** (como a Pump Shotgun do Fortnite): **10 bagos hitscan** num cone, 6 de dano cada
  (**60 até 6 m**). Queda com a distância: 10 m 80%, 13 m 60%, 15 m 49%, 18 m 35%, até 20% aos 31 m (alcance máximo).
  É preciso acertar **pelo menos 3 bagos** no mesmo jogador para haver dano. Recuo forte, som e animação de recarga.
- Especial: o Super de Brasa preservado (empurrão + destruição de cobertura), parâmetros configuráveis.

### Lupus Marinus (L.M) — TNT
- Principal: TNT em arco com acerto direto e explosão em área com queda pela distância.
  **40 por TNT** no centro; três acertos diretos = **120**.
- Especial: **5 minas** com sensor de proximidade, tempo de armar, aviso antes da explosão, duração máxima,
  limite de minas ativas por dono, colocação validada (sem paredes, precisa de chão).
  Podem ser destruídas por explosões/facadas inimigas. Sincronizadas pelo host.

### N.Cage — arco
- Principal: três flechas visíveis com gravidade numa só ação; cada flecha conta separadamente
  (podem acertar adversários diferentes; flechas que falham não contam). Três no mesmo alvo = **100**.
- Especial: **3 armadilhas**. Ao ativar: preso **2 s** (bloqueia o movimento, não o ataque nem a câmara),
  **16 de veneno** no total (8/s durante os 2 s), efeito visual de preso. Imunidade curta após soltar evita cadeias de armadilhas.
  A armadilha desaparece ao disparar ou ao expirar.

### Lince — sniper
- Principal: **55 no corpo**; na cabeça **80 a 1 m e mais quanto mais longe, até 150 a 60 m** (alcance 60 m), deteção de headshot pela zona da cabeça (de cima, de baixo e sem raio),
  indicador visual de headshot, a cadência mais lenta do jogo, mira própria (botão direito). A mirar, a **roda do
  rato** aproxima ou afasta o zoom da luneta (de 1,8x a 10,5x, mostrado no ecrã; fica guardado). A sensibilidade baixa
  com o zoom.
  **Luneta térmica**: a mirar, o clique na roda do rato (tecla configurável, "Thermal scope") liga/desliga a visão
  térmica — cenário frio azul-escuro, jogadores quentes (vermelho → branco), e os arbustos deixam de esconder quem está
  lá dentro. Só visual, no PC de quem mira (`ThermalScope` + `Resources/Shaders/FPSLAN_Thermal.shader`).
- Especial **Tiro fatal**: carrega passivamente com o tempo. Arma-se com o botão do Super (carregar outra vez cancela
  e guarda a carga). Headshot válido = eliminação imediata; no corpo = dano normal (55). Hitscan validado no host,
  respeita paredes e linha de visão, sem mira automática. A carga só é gasta ao disparar.

---

## 2. Progressão

Três sistemas separados: **XP global do jogador** (níveis 1–50), **XP por personagem** (1–10) e **XP por pet** (1–10).
Curvas de XP crescentes configuráveis em `Resources/GameExperience/ProgressionConfig.asset`.

| Fonte | XP |
|---|---|
| Participação válida | 50 |
| Eliminação | 12 (máx. 30 contadas) |
| Assistência | 6 (máx. 30) |
| Vitória | +75 |
| Objetivo | 15 (máx. 5) |

- Participação válida exige tempo mínimo em jogo, fração mínima da partida e distância percorrida (anti-AFK).
- Eliminações de bots valem 50 % (`xpVsBotsMultiplier`).
- O **host** calcula o resultado (`MatchStatsTracker`) e envia-o só ao dono; cada resultado tem `matchId` e é aplicado
  **uma única vez** (reabrir resultados, reiniciar ou reconectar não repete XP nem recompensas).
- Ecrã pós-partida: XP recebido e acumulado, barra animada, nível atual/seguinte, recompensas, personagem e estatísticas.

### Melhoria de atributos
`CharacterUpgradeManager` + `EffectiveStats`: vida, dano, regeneração, velocidade, recarga e eficiência do especial,
até ao nível 5 de melhoria (custos 100/200/350/500/700 moedas, 1 nível de melhoria por cada 2 níveis de personagem).
Os `BrawlerDefinition` nunca são alterados: os atributos efetivos são base × modificadores, calculados no host.
- **Casual**: melhorias ativas, mas cada uma limitada (`casualCap`, 12 % por omissão).
- **Competitiva**: melhorias e habilidades de pets desligadas (atributos normalizados). Escolhe-se no menu
  (botão CASUAL/COMPETITIVA).

### Persistência
`SaveManager` guarda o perfil em `Application.persistentDataPath`: envelope JSON versionado
`{ format, version, checksum, payload }`, escrita atómica, cópia de reserva e recuperação automática se o ficheiro
estiver corrompido ou o checksum não bater certo. Guarda ID do jogador, XP/níveis, personagens, melhorias, pets, skins,
equipados, moedas, missões e recompensas. Não guarda credenciais.

> **Aviso:** um save local é manipulável. Para progressão competitiva entre computadores é preciso migrar para um
> servidor autoritativo; a interface `IProfileStorage` já isola o armazenamento para essa migração.

---

## 3. Pets

| ID | Nome | Categoria | Habilidade | Desbloqueio |
|---|---|---|---|---|
| `pet_dog` | Faro (Cão) | Suporte | Deteta adversários próximos | inicial |
| `pet_cat` | Bigodes (Gato) | Agilidade | + velocidade pequena | nível 3 de jogador |
| `pet_pig` | Toucinho (Porco) | Defesa | escudo de lama com pouca vida | missão |
| `pet_rooster` | Alvorada (Galo) | Alerta | avisa ameaças nas costas | conquista 25 assistências |
| `pet_rabbit` | Pipoca (Coelho) | Mobilidade | ajuda a correr | 600 moedas |
| `pet_turtle` | Carapaça (Tartaruga) | Resistência | recupera vida mais depressa | desafio "Sem um arranhão" |

- `PetFollower`: segue o dono a distância, evita paredes, teleporta-se quando fica preso/longe, animações idle/andar/
  correr/celebrar, reage ao dano do dono, sem colisões com tiros nem jogadores. Visual cartoon gerado por código
  (`PetVisualBuilder`).
- Um pet equipado por jogador, sincronizado no `PlayerLoadout` (hash estável).
- Evolução 1–10 com estágios visuais **Jovem (1–3) / Desenvolvido (4–7) / Evoluído (8–10)**; marcos nos níveis
  3 (habilidade), 4 (visual), 5 (animação), 8 (acessório), 10 (aura cosmética). Habilidades com teto (`abilityMax`).
- Novos pets: basta criar um `PetDefinition` e adicioná-lo ao `PetCatalog` (sem mudar código).

---

## 4. Skins

`SkinCatalog` com 12 skins de Brawler (2 por personagem) e 6 de pet, raridades Comum/Rara/Épica/Lendária.
Mudam cores, roupa, acessórios, cor da arma, partículas, efeitos e estilo de animação — **nunca** dano, vida ou
velocidade. Desbloqueio por moedas, nível, conquista ou recompensa. A skin equipada viaja no `PlayerLoadout`, por isso
todos os jogadores veem a mesma skin.

---

## 5. Missões, conquistas e recompensas

- **Diárias**: 3 partidas, 10 eliminações, 2 partidas com pet.
- **Semanais**: 50 eliminações, 10 vitórias, 3 personagens diferentes.
- **Conquistas/desafios**: primeira eliminação, 100 eliminações, 25 assistências, 25 headshots, 10 vitórias,
  nível 10, partida sem morrer, 10 objetivos.
- Recompensas por nível de jogador (moedas, skins, título no nível 50) ficam pendentes até serem recolhidas no ecrã
  RECOMPENSAS. O `RewardManager` regista IDs já entregues para impedir duplicações.

---

## 6. Interfaces e controlos

No menu principal (e na pausa) há botões para **PERFIL, PERSONAGENS, NÍVEIS, SKINS, PETS, MISSÕES, RECOMPENSAS**
(`ProgressionUI`, estilo arcade em `ArcadeGUI`), com pré-visualização 3D de Brawlers/pets.
Feedback visual e sonoro para subida de nível, desbloqueios, evolução de pet, nova skin, missão concluída, vitória,
headshot e especial disponível.

| Tecla | Ação |
|---|---|
| Botão esquerdo | ataque principal (no modo Minigun/Tiro fatal dispara o especial) |
| Botão direito / E | Super; para Ekko ativa a Minigun, para Lince arma/cancela o Tiro fatal |

**Comando PlayStation** (DualShock 4 / DualSense, USB ou Bluetooth; Definições ▸ aba **CONTROLLER**: ligar/desligar,
sensibilidade, inverter Y e **botões de cada ação configuráveis** — escolhe a ação e carrega no botão; se já estava
em uso, as duas trocam). Botões por defeito:
L stick andar · R stick olhar (sensibilidade e inverter Y nas definições) · R2 atacar · L2 mirar · L1 Super ·
R1 facada · X saltar · L3 sprint · Quadrado reanimar · Triângulo comida do pet · Círculo roda de sprays ·
Touchpad marcador · Options pausa · Lince a mirar: cruzeta ↑/↓ zoom, R3 luneta térmica. Os menus usam o rato.
(`GamepadInput` + eixos `FPSLAN_*` no InputManager.)
| Q | Facada |

---

## 7. Ficheiros

**Novos**
- `Scripts/GameExperience/` — `GameExperienceContext`, `Definitions/`, `Inventory/InventoryManager`,
  `Match/MatchStatsTracker`, `Persistence/ProfileData` + `SaveManager`, `Pets/` (`PetDefinition`, `PetFollower`,
  `PetManager`, `PetVisualBuilder`), `Progression/` (`XPManager`, `PlayerProgressionManager`,
  `CharacterUpgradeManager`, `EffectiveStats`, `ProgressionConfig`), `Quests/`, `Rewards/RewardManager`, `Skins/`.
- `Scripts/UI/ProgressionUI.cs`, `Scripts/UI/ArcadeGUI.cs`
- `Editor/GameExperienceAssetBuilder.cs` (menu **FPS-LAN → Gerar dados de Game Experience**)
- `Resources/GameExperience/` — `ProgressionConfig`, `PetCatalog` + 6 pets, `SkinCatalog` + 18 skins, `QuestCatalog`
- `Tests/Editor/` (BrawlerDataTests, CombatMathTests, ProgressionTests) e `Tests/PlayMode/GameplayIntegrationTests`

**Alterados**
- Combate: `CombatSystem`, `CombatTypes`, `EffectResolver`, `StatusEffects`, entregas `Projectile`, `ArcProjectile`,
  `ConeVolume`, `PlaceTraps` (limites por alvo, headshots, queda de dano, minas/armadilhas sincronizadas,
  modos Minigun/Tiro fatal).
- Jogador: `PlayerCombat`, `PlayerHealth`, `PlayerMotor`, `PlayerNetwork` (loadout com pet/skin/nível, atributos
  efetivos no host, imobilização), `PlayerVisuals`.
- Visual: `CharacterRig`, `ViewModelRig`, `WeaponModels`, `ProjectileVisuals` (metralhadoras duplas, latas, minigun,
  TNT, granadas, flechas).
- `BrawlerDefinition`, `BrawlerRegistry`, `BrawlerAssetBuilder`, dados em `Data/Brawlers/*`, `MatchManager`,
  `GameUI`, `BrawlerPreview`, `BotBrain`, `BotManager`, `Sfx`, `PlayerCommands`.

---

## 8. Testes executados (Unity 6000.6.6f1, modo batch, nesta máquina)

- **EditMode: 74/74 passaram** — nomes, vidas, danos por distância, totais dos ataques múltiplos, headshots, os seis
  especiais, veneno, colocação de armadilhas, curvas de XP, XP por partida, anti-duplicação, gravação/carregamento,
  recuperação de save corrompido, checksum adulterado.
- **PlayMode** (`SixBrawlersSpecialsProgressionAndSync`, cena Arena real com host + bots): ver o resultado no relatório
  da sessão. Cobre os danos reais de todos os Brawlers, Tiro fatal com parede, armadilhas (5 s, 40 de veneno, sem
  cadeia), minas (aviso, explosão, destruição), zona de veneno de 12 s, Minigun ≤ 200, sincronização de pet/skin no
  loadout e XP de fim de partida aplicado uma vez.

Não testado automaticamente: duas máquinas reais em LAN (o teste usa host + bots no mesmo processo).

## 9. Limitações conhecidas
- Progressão guardada localmente (manipulável) — ver aviso na secção 2.
- Modelos de pets e skins são procedurais (primitivas + cores), sem assets externos.
- Animações são procedurais, não clips do Animator.

## 10. Próximos passos recomendados
- Teste em LAN com dois PCs (host + cliente) para validar a sincronização visual com latência real.
- Servidor autoritativo de perfis se houver competição entre computadores.
- Afinar valores com dados de partidas (ver `ProgressionConfig` e os assets em `Data/Brawlers`).
