# FPS-LAN — Novidades: "Arena dos Brawlers" (evolução visual + 2 Brawlers + mapa novo)

## Como abrir

1. Abre o projeto no Unity (6000.6). Ao compilar, o projeto **regenera sozinho** o conteúdo novo
   (versão de conteúdo `ProjectSetup.SetupVersion = 2`):
   dados dos 6 Brawlers, materiais cartoon, prefab `Player` e a cena `Arena` com o mapa **Pradaria**.
   - Se por algum motivo não correr: menu **FPS-LAN → Gerar dados dos Brawlers** e depois
     **FPS-LAN → Regenerar Arena + Player**.
2. Abre a cena `Assets/_Game/Scenes/Arena.unity`, carrega **Play** e escolhe **TREINO (com bots)** ou **HOST**.

> A camada `ViewModel` (n.º 8) foi adicionada em *Project Settings → Tags and Layers*: é usada pela câmara
> das mãos/arma em primeira pessoa (a arma nunca atravessa paredes).

## Controlos

| Tecla | Ação |
|---|---|
| WASD / rato | mover / olhar |
| Shift | sprint |
| Espaço | saltar |
| Botão esquerdo | ataque principal |
| Botão direito / E | **Super** (quando a barra circular estiver cheia) |
| Tab | placar |
| Esc | pausa: trocar de Brawler, sensibilidade, bots |

## O que mudou

### Estilo visual (cartoon / arcade)
- **Shaders próprios** (`Assets/_Game/Shaders`): cel shading com 2 bandas de luz, sombras tingidas, rim light,
  brilho especular duro, **contorno escuro** nas personagens/armas/props, mosaico no chão de relva,
  flash branco ao levar dano e transparência por *dithering* (arbustos).
- Céu procedural, nevoeiro claro, sol quente com sombras suaves, colinas, montanhas com neve e nuvens a deslizar.

### Personagens (terceira pessoa)
- Cada Brawler tem agora um **boneco chibi** (cabeça grande, olhos expressivos, sobrancelhas, acessório de cabeça
  e arma grande do seu estilo), gerado por código a partir do `BrawlerDefinition` (`thirdPerson.look`).
- **Animação procedural**: idle a respirar, andar/correr com balanço, salto, pontaria a seguir o olhar,
  ataques por estilo (revólveres alternados, arremesso das dinamites, puxar/soltar a corda do arco,
  lata de spray a tremer, coice da caçadeira/sniper), pose de Super, reação a dano, morte (cai e desaparece num
  "puf" com estrelas) e **reaparecer** com feixe de luz e "pop".
- As peças de cada osso são juntadas numa só malha (`MeshMerge`) para poupar draw calls.

### Primeira pessoa
- Mãos + arma de cada estilo, numa câmara própria: **dois revólveres** (recuo alternado, tambor a girar e
  revólver a rodar na mão ao recarregar), **duas dinamites** com rastilho a faiscar (arremesso + nova dinamite),
  **arco** (a mão puxa a corda, 3 flechas no nock), **lata de spray**, **caçadeira** (pump) e **sniper** (mira com lente).
- Bob ao andar, *sway* com o rato, inclinação no sprint, aterragem, brilho a pulsar quando o Super está pronto.

### Disparos, projéteis e explosões
- Balas brilhantes com rasto, bagos de caçadeira, **flechas**, **dinamites a girar** que ficam no chão a faiscar
  e a piscar antes de explodir, **barril** do Super, bombas, minas com luz a piscar, tracers do sniper.
- **Explosões** com clarão, bola de fogo, fumo, faíscas, destroços que saltam, anel no chão, marca de queimado,
  luz e **abanão de câmara**. Estrelas de impacto ao acertar, clarão do cano com luz, nuvem de spray em partículas,
  auras/zonas com anel a rodar.
- Caixotes destrutíveis partem-se em pedaços com pó.

### Mapa novo: Pradaria (52×52 m)
- Simetria rotacional de 4 lados (justo para todos-contra-todos), 8 spawns protegidos (cantos + meios),
  plataforma central com 4 rampas e totem com gema a rodar, lagos (bloqueiam o movimento, os tiros passam por cima),
  rochedos, muros, vedações, caixotes e barris destrutíveis, caminhos de terra, flores/relva/cogumelos.
- **Arbustos**: quem está dentro fica **invisível** para adversários a mais de 3,4 m (os bots também não o veem),
  até atacar ou levar dano. O arbusto onde estás fica translúcido para ti.
- **Plataformas de salto**: 4 plataformas que lançam o jogador em arco até à plataforma central.
- O layout é dados (`Editor/ArenaLayout.cs`); o `ProjectSetup` transforma cada peça em geometria.

### Novos Brawlers
| Nome | Papel | Principal | Super |
|---|---|---|---|
| **Brasa** | Caçadeira (curto alcance) | leque de 6 bagos, muito forte de perto | rajada gigante que empurra e destrói cobertura |
| **Lince** | Atiradora (sniper) | tiro instantâneo que dói mais ao longe (headshots!) | **salto de fuga** para onde apontas, deixando bombas para trás |

Os 4 anteriores ganharam visual próprio: **Dex** (chapéu de aba larga), **Cardo** (óculos de aviador, cabelo espetado,
cinto de dinamites), **Vigia** (capuz de raposa, aljava), **Névoa** (máscara de gás, gorro, depósito às costas).

### Jogabilidade "arcade"
- **Escudo de spawn** (2,2 s; termina ao atacar).
- **Números de dano** a flutuar (somam numa rajada; amarelo = headshot, vermelho = eliminação).
- Avisos: "LUTEM!", "ELIMINAÇÃO!", "DUPLA!", "SUPER PRONTO!".
- Linha de trajetória + marcador no chão para ataques em arco, minas e salto.
- Bots: respeitam os arbustos, patrulham pontos do mapa e a Lince usa o Super para fugir.

### Interface
- Menu com **cartões de Brawler** e **pré-visualização 3D** do boneco a rodar (com atributos Vida/Alcance/Dano).
- HUD: retrato, barra de vida grande, **munição segmentada por baixo da mira**, **botão de Super circular** que
  enche, cronómetro, líder, kill feed colorido, vinhetas (dano, vida baixa, abrandado, escondido no arbusto).

### Som
- Sons procedurais refeitos (com filtro passa-baixo): revólver, caçadeira, sniper, explosão grave, salto,
  reaparecer, Super pronto, "puf", caixotes a partir, etc.

## Ficheiros principais
- `Scripts/Visual/`: `Art` (materiais/texturas), `Fx` (partículas, luzes, tracers, abanão de câmara),
  `CharacterRig` (boneco 3.ª pessoa), `ViewModelRig` (1.ª pessoa), `WeaponModels`, `ProjectileVisuals`, `MeshMerge`.
- `Scripts/World/`: `Bush`, `JumpPad`, `MapInfo`, `Spinner`.
- `Scripts/Combat/Deliveries/LeapDelivery.cs`: Super de salto (movimento do dono + bombas resolvidas no host).
- `Editor/ArenaLayout.cs` + `Editor/ProjectSetup.cs`: mapa e geração da cena.

## Notas / limitações
- Tudo é gerado por código com primitivas (sem assets de terceiros): personagens, nomes, sons e efeitos são originais.
  Brawl Stars é só referência de design.
- Feito sem o Editor do Unity disponível: o código foi compilado contra as DLLs de referência do Unity e os shaders
  foram validados (48 variantes) com os includes oficiais do Unity — mas **não foi testado em Play Mode**.
  Se algo não estiver certo no primeiro arranque, a consola indica o ficheiro; os sistemas de rede e combate
  mantêm a mesma lógica autoritativa de antes.
- O código antigo `ProjectileSystem`/`HeroCatalog` continua no projeto mas já não é usado pela cena nova.
