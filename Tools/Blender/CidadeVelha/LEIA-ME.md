# Mapa Pique-Gemas — Cidade Velha / Pelourinho

Primeiro mapa de identidade cabo-verdiana: praça do Pelourinho (Cidade Velha, Santiago) adaptada para arena FPS.
É uma **adaptação para gameplay**, não uma reconstrução histórica. O **emissor de gemas** (halo sobre a cruz,
anel na base do fuste, marcas de aterragem, partículas) é **ficção do jogo** e está separado do monumento
(collection `GemGenerator`).

## Fluxo

```
PNG de referência → cidade_velha_pelourinho.py (Blender) → UV/texturas procedurais → Actions de demonstração
→ FBX (estático / gerador / emissor animado / gema) → FPS-LAN ▸ Mapas ▸ Construir Cidade Velha (Unity)
→ prefabs, materiais cartoon, colisores, spawns e efeitos reconstruídos em C#
```

## 1. Blender (Blender 4.2 LTS ou mais recente; testado em 5.2.2)

```
blender --background --python cidade_velha_pelourinho.py -- --out ./out --export --save-blend --render
```

ou no Blender: *Scripting → Open → cidade_velha_pelourinho.py → Run Script* (gera em `./out` junto do script).

| Opção | Por defeito | Para quê |
|---|---|---|
| `--width` / `--length` | 40 / 50 | área jogável (m); o layout escala em X/Y, o Pelourinho não |
| `--detail 1·2·3` | 2 | ornamentos, adereços, elos das correntes, ripas, partículas |
| `--wall-height` | 2.6 | muros de cobertura (acima dos olhos de quem está no topo dos degraus) |
| `--t-idle … --t-collect` | 1.5 / 1.6 / 0.7 / 1.1 / 3.0 / 0.8 s | duração de cada fase da demonstração |
| `--fps` | 30 | |
| `--export` `--save-blend` `--render` `--anim-preview` | | FBX, .blend, imagens, sequência da gema |

O script cria a cena **`CV_Pelourinho`** e só mexe nela (não apaga outras cenas nem objetos).

Collections: `Ground` (+ `Gameplay`: SPAWN_*, WP_*), `Pelourinho`, `Buildings`, `Cover`, `Props`, `GemGenerator`,
`Gems`, `Collision` (COL_Box_* / COL_Mesh_*, só wireframe), `Preview` (câmaras, luzes, objetos só de demonstração).
Módulos reutilizáveis (malhas partilhadas): `MOD_Janela_*`, `MOD_Porta_*`, `MOD_Varanda`, `MOD_Muro_2m`,
`MOD_Muro_Janela_2m`, `MOD_Parapeito_2m`; coberturas geradas por `roof_mesh()` (duas/quatro águas).

### Ver a demonstração animada

Abrir `out/CidadeVelha_Pelourinho.blend`, câmara ativa `CAM_Gemas`, *Play* (frames 1–286 a 30 fps).
Os marcadores da timeline marcam as fases: 1 Espera · 2 Aviso · 3 Surgimento · 4 Arco · 5 Flutuar/Rodar · 6 Recolha.
`CAM_Apresentacao` faz uma órbita lenta; `CAM_Planta` (ortográfica) serve para avaliar rotas; `CAM_Pelourinho` mostra o monumento.

### O que passa (e o que não passa) para o Unity

| Elemento | No Blender | No FBX | No Unity |
|---|---|---|---|
| Arquitetura, Pelourinho, adereços | malhas | ✅ `CidadeVelha_Static.fbx` | prefab `PF_CidadeVelha` |
| Arbustos `BUSH_<nome>_S<seed>` (zona) + `BSHV_*` (folhas) | zona + folhas geradas pela porta do `LeafBush.cs` (`leafbush_port.py`, mesma seed e mesmo gerador aleatório do .NET) | ✅ só a zona | `Bush` + `LeafBush` do jogo com a mesma seed (`BSHV_*` fica em Preview e não é exportado) |
| Colisores `COL_*` | caixas/prismas | ✅ (malhas) | convertidos em BoxCollider / MeshCollider convexo, malha removida |
| Spawns, waypoints, `GEM_Spawn`, `GEM_Land_00…07` | empties | ✅ (nós vazios) | SpawnPoint por equipa, MapInfo, GemGrabManager |
| Rotação do halo/anel (`ACT_Emissor_Espera`, `ACT_Emissor_Aviso`, `ACT_EmissorBase_Espera`) | NLA | ✅ `CidadeVelha_Emissor_Anim.fbx` (referência) | **refeito em C#** (GemGrabVisuals), sem Animator |
| Gema: `ACT_Gema_Surgir`, `ACT_Gema_Flutuar`, `ACT_Gema_Recolher` | Actions em faixas NLA | ✅ `CidadeVelha_Gem.fbx` (clips de referência) | movimento **calculado pelo estado do host** (arco, flutuação, rotação) |
| Demo (`ACT_Demo_Gema_*`, jogador azul, lascas) | Actions em coordenadas do mundo | ❌ não exportado | — |
| Partículas da coluna (`PS_Particulas_Coluna`) | sistema de partículas | ❌ | reconstruídas com `Fx.Glow` |
| Brilho do emissor / marcas (emissão animada) | animação de material | ❌ | `MaterialPropertyBlock` + `Fx.Ring`/`Fx.Light` |
| Texturas procedurais | imagens geradas | ❌ (PNG à parte em `out/Textures`) | `Art/Environment/Santiago/CidadeVelha/Textures` |

Não foi usado armature: nada no cenário precisa de deformação.

## 2. Unity (6000.6, Built-in RP, NGO 2.13)

1. Copiar `out/FBX/*.fbx` para `Assets/_Game/Art/Environment/Santiago/CidadeVelha/Models/` e
   `out/Textures/*.png` para `.../CidadeVelha/Textures/` (já estão no repositório nesta branch).
2. Menu **FPS-LAN ▸ Mapas ▸ Construir Cidade Velha (Pique-Gemas)**. Cria/atualiza:
   - `Art/.../CidadeVelha/Materials/M_CV_*.mat` (shader `FPSLAN/Toon` / `FPSLAN/ToonOutline`);
   - `Prefabs/Environment/PF_CidadeVelha.prefab` e `Prefabs/Environment/PF_Gema.prefab`;
   - `Scenes/CidadeVelha.unity` (NetworkManager, MatchManager em modo fixo Pique-Gemas, PiqueGemas, MapInfo, UI);
   - acrescenta a cena **no fim** das Build Settings (a Arena fica em primeiro).
   Não regenera nem grava Arena.unity, Player.prefab ou os materiais do ProjectSetup.
3. O diálogo final lista verificações: tamanho do chão (≈ 41 × 56 m, topo y = 0), lado das equipas,
   aterragens livres e com chão, materiais em falta.
4. **FPS-LAN ▸ Mapas ▸ Abrir cena Cidade Velha** → Play → Host (ou Treino com bots).

## Detalhe do cenário (`--detail 2` ou 3)

Floreiras nas janelas, soleiras de pedra nas portas, candeeiros de parede, chaminés, panos de terra nas bancas,
balaios, potes de barro e bananas, estendais de roupa sobre os corredores exteriores (acima das cabeças),
rosa de basalto na calçada da praça e, fora da área jogável, casas de palha, ruína de pedra, botes no mar,
palmeiras e árvores. Nada disto acrescenta colisores nem altera rotas (a análise de rotas dá o mesmo resultado).

## Arbustos (esconderijos)

12 arbustos de 2 m de altura — os mesmos do jogo (`LeafBush` + material `M_BushLeaves`) — substituem as árvores da área jogável: junções praça/corredor, cantos dos spawns e
faixas encostadas aos edifícios das bolsas laterais. Usam o sistema existente (`World/Bush.cs`):
atravessam-se, as balas passam, quem está dentro fica oculto para adversários a mais de 3,4 m e é revelado ao atacar
(1,3 s) ou ao levar dano (1 s). Ficam ocultos a personagem, a barra de vida, o nome, a coroa, o escudo, os passos,
o clarão do disparo, o contador de gemas por cima do portador e o efeito de recolha. Os colegas de equipa
continuam visíveis entre si. Os bots não escolhem alvos ocultos (`BotBrain`). Para quem está dentro,
o arbusto fica translúcido e aparece a vinheta verde.

## 3. Verificar rotas sem Unity

```
python analisar_rotas.py out/cidade_velha_layout.json out/Renders/planta_rotas.png out/Renders/planta_rotas_anotada.png
```

Rasteriza os colisores inflacionados pelo raio do CharacterController (0.4 m) e confirma ligação entre spawns,
acesso às 8 aterragens, 3 rotas independentes e largura mínima de cada rota. Não substitui o teste com o jogador.

## 4. Interiores: igreja e sobrado (Unity)

**FPS-LAN ▸ Mapas ▸ Cidade Velha: interiores (igreja e sobrado)** (`Editor/CidadeVelhaInteriores.cs`) troca os dois
blocos maciços do fundo da praça por edifícios com interior. Corre-se depois de reconstruir a Cidade Velha a partir do
Blender (como o menu da costa) e pode repetir-se. Os blocos originais ficam no prefab, inativos.

- **Igreja (−Z)**: nave com 8 bancos (livros, genuflexórios), passadeira vermelha, teia de comunhão, presbitério com
  altar (toalha com renda, sacrário, castiçais, missal), arco azul com fundo estrelado e crucifixo, 4 nichos com imagens,
  estandartes vermelho e azul, ventoinhas de parede, via-sacra, lustre de velas, lâmpada do Santíssimo, ambão, cadeiras,
  confessionário, pias de água benta, velas votivas, teto branco com vigas verde-água.
  **Coro alto** a 3,3 m sobre a entrada, com **duas escadas** (uma de cada lado) e balaustrada: os olhos ficam à altura
  do **óculo** da fachada (4,9 m), agora aberto, e há duas janelas do coro na fachada (x = ±5,5) — posições de atirador.
- **Sobrado (+Z)**: interior rústico em troncos. Rés-do-chão com fogão de ferro (panelas, tacho de cobre, chaleira,
  lume), lenha, prateleiras com louça e frascos, bancada, mesa posta com cadeiras e candeeiro de petróleo, berço,
  cadeiras de baloiço, baús, cristaleira, barris, relógio, quadros, vasos nas janelas (com grades).
  **Escada de madeira** (16 degraus) para o 1.º andar (quarto: cama com colcha de retalhos, guarda-roupa, lavatório,
  roca de fiar, secretária). As **5 janelas de cima e a varanda** (alargada, com guarda de ferro) abrem para a praça,
  viradas para a igreja.
- **Luz e sombras**: 3 luzes pontuais por edifício (altar e candeeiro da cozinha com sombras suaves), sol a entrar pelas
  janelas, oclusão nos vértices e sombras de contacto debaixo da mobília. Cada edifício é desenhado com uma malha por
  material (~40k triângulos cada).
- **Jogo**: colisores em caixa (paredes com vãos, rampas suaves nas escadas, móveis); não se sai pelas janelas nem pelo
  óculo, mas as balas passam. Os limites invisíveis N/S do mapa deixaram de atravessar as fachadas e o `MapInfo`
  passou a ter `extraAreas` (saltos e gemas largadas dentro dos edifícios não são puxados para a praça).

**FPS-LAN ▸ Mapas ▸ Validar interiores da Cidade Velha** anda com o CharacterController do jogador desde os spawns até
ao coro (pelas duas escadas), ao 1.º andar e à varanda, confirma que as janelas não deixam sair e testa as linhas de tiro
óculo/coro ↔ janelas/varanda. Imagens em `Docs/preview/cidade_velha/interior_*.jpg`.

### 4.1 Restantes casas do Pelourinho (`Editor/CidadeVelhaInteriores.Casas.cs`)

O mesmo menu dá interior às outras 10 casas. As aberturas saem dos módulos do FBX (`_Porta_`, `_Janela_`, `_Janela2_`,
`_PortaVaranda_`), por isso a ferramenta acompanha mudanças no script do Blender. Fica a cobertura, a chaminé, os
candeeiros e a cantaria (cunhais, cornija, friso — sem as faces que entravam na sala); o bloco de reboco maciço, as
portas e as janelas originais passam a inativos e são refeitos com paredes de 30 cm com vãos, forro interior, rodapé
ou azulejo, embasamento de pedra, portas abertas para dentro, janelas com portadas abertas, cortinas e floreiras.

| Casa | Tema |
|---|---|
| Esquina EN e WN (2 pisos) | **Bar**: balcão com torneiras de cerveja, garrafeira com espelho e frigoríficos iluminados, bancos altos, mesas redondas, TV com futebol, bandeira de Cabo Verde, quadro de ardósia, alvo de dardos, candeeiros pendentes, ventoinha · 1.º andar: **apartamento** |
| Esquina ES (2 pisos) | **Mercearia**: prateleiras de produtos, balcão com caixa registadora e balança, bancas de fruta, sacos de arroz/feijão/milho, frigorífico de bebidas, cacho de bananas · 1.º andar: apartamento com duas camas |
| Esquina WS (2 pisos) | **Casa de família**: cozinha (bancada, lava-loiça, placa a gás, frigorífico), mesa de jantar, sala com sofá, TV, estante · 1.º andar: apartamento |
| Fila EN | **Tasca de grogue** com **alambique** de cobre (fornalha acesa, serpentina), barris, cana-de-açúcar, balcão com garrafas de grogue e pontche, mesa com bancos, luzes de fiada |
| Fila ES | Casa térrea (quarto, cozinha, mesa, cadeira de baloiço) |
| Fila WN | Estúdio (cama, kitchenette, sofá, secretária com portátil, TV) |
| Fila WS | **Barbearia** (2 cadeiras de barbeiro, espelhos, bancada, lavatório, banco de espera, TV, poste de barbeiro na fachada) |
| Bolsa E | **Restaurante** com balcão e **palco de morna** (violões, microfone, colunas), mesas com toalha, luzes de fiada |
| Bolsa W | Casa de família com divisória: quarto (cama, berço, baú) e sala/cozinha |

- **Sobrados de esquina**: escada de madeira (14 degraus) junto à parede do fundo, vão com guarda no 1.º andar, porta da
  varanda da frente e da varanda lateral abertas (guarda de ferro, vaso). As janelas do 1.º andar ficam abertas para
  tiro; as do rés-do-chão têm grades.
- Os móveis respeitam zonas livres (abertura das portas, corredor entre a porta da frente e a lateral, escada) e cada
  casa tem 1–2 luzes pontuais (uma com sombras suaves). ~187k triângulos nas 10 casas, uma malha por material.
- Os arbustos (esconderijos) das bolsas entravam pela parede: são afastados para fora da fachada.
- **Patrocinadores**: o mesmo menu refaz sempre as placas SITA e UPRANIMAL (`Editor/SponsorBoards.cs`), também
  chamadas pelo construtor do mapa (`decorateMap`); funcionam com o bloco maciço da casa já desligado.

A validação percorre também cada casa: porta → escada → 1.º andar → varanda da frente, 1.º andar → varanda lateral
(esquinas), porta da frente → porta lateral (fila) e porta → sala → porta (bolsas). Fotografias em
`GameJam/Fotos/Cidade_Velha/Interiores_Casas/`.

### 4.2 Portas e desempenho

- **Portas** (`DoorUnit` / `DoorLeaf`): portas de duas folhas almofadadas (montantes, travessas, almofadas em relevo,
  dobradiças de ferro, puxador de latão, espelho de fechadura e argola), bandeira envidraçada com leque de ferro por
  cima, alizar de madeira por dentro e ombreiras rebocadas. Nas casas as folhas abrem **encostadas à parede interior**
  (deixam de tapar a passagem e a vista); só onde não há parede livre (canto, janela ao lado) ficam abertas a ~95°.
  Porta da rua 2,8 m (2,62 m nos sobrados de esquina, por baixo das vigas), porta da varanda 2,55 m. A bandeira tem
  colisor: só a parte das folhas é passagem.
- **Desempenho**: as 26 luzes pontuais dos interiores deixaram de ter sombras e de ser forçadas a "pixel";
  `InteriorLightCulling` (no grupo `Interiores`) deixa ligadas só as 6 mais próximas da câmara num raio de 16 m.
  Só a estrutura (reboco, cantaria, pedra, telha, troncos) projeta sombra do sol — a mobília não. Qualidade Ultra:
  sombras até 60 m com 2 cascatas (era 150 m / 4); Very High: 55 m. Medição no editor (1920×1080, Ultra, 4 vistas da
  Cidade Velha, média): 38,3 ms → 29,9 ms por imagem.

### 4.3 Correntes do Pelourinho (`Editor/CidadeVelhaCorrentes.cs`)

Menu **FPS-LAN ▸ Mapas ▸ Cidade Velha: correntes do Pelourinho (detalhe)**:

- **Malha nova:** troca só a malha dos 4 vãos `PRP_Corrente_0/2/4/6` do prefab. Ficam iguais o material `M_CV_Ferro`, os colisores e as flags estáticas.
- **Elos:** ovais de ferro entrelaçados, cada um rodado 90° em relação ao anterior, com uma pequena torção aleatória fixa. Medem 13 × 7,5 cm, com arame de 1,2 cm.
  - Seguem a curva da corrente, que desce 22 cm a meio.
  - São 184 elos no total.
- **Postes:** cada poste tem uma anilha dupla e um olhal virado para o vão, à altura de 0,62 m.
- **Ficheiros:** as malhas ficam em `Art/Environment/Santiago/CidadeVelha/Correntes/CV_Corrente_*.asset`. São cerca de 25 400 triângulos no total.
- **Depois de reconstruir a Cidade Velha a partir do Blender:** o FBX volta a trazer a corrente simples, por isso é preciso voltar a correr este menu.

### 4.4 Spawns do Deathmatch nas casas e na igreja, e chão sem faixas (`Editor/CidadeVelhaSpawnsDM.cs`)

Menu **FPS-LAN ▸ Mapas ▸ Cidade Velha: spawns do Deathmatch nas casas e na igreja (sem faixas azul e vermelha)**:

- **Faixas pintadas:** apaga do prefab `GRD_Spawn_Blue` e `GRD_Spawn_Red`, as faixas azul e vermelha do chão.
  - Os spawns das equipas do Pique-Gemas (`SPAWN_Blue_*` e `SPAWN_Red_*`) ficam nos mesmos sítios, só deixam de estar pintados.
- **Spawns nas casas e na igreja:** cria o grupo `Spawns_Deathmatch` com 32 `SpawnPoint` marcados como `deathmatchOnly`.
  - Cada piso das 10 casas tem 2 spawns. Os sobrados de esquina têm rés-do-chão e 1.º andar.
  - A igreja tem 3 spawns na nave (afastados pelo menos 4 m) e 1 no coro alto, virado para o altar.
  - Os pontos saem de uma grelha de 1 m dentro de cada edifício e são validados com a física do prefab: chão do piso por baixo, espaço livre para um jogador em pé e teto por cima.
  - Os spawns das casas e da nave ficam virados para a porta da frente.
- **Deathmatch:** o respawn é **aleatório** entre esses 32 pontos.
  - Primeiro escolhe entre os pontos livres e sem ninguém vivo a menos de 8 m.
  - Se não houver nenhum, escolhe entre os livres. Se ainda assim não houver, escolhe entre todos.
- **Outros modos:** o Pique-Gemas, o It Takes Two e os restantes ignoram estes pontos e funcionam como antes.
- **Depois de reconstruir a Cidade Velha a partir do Blender** (que volta a trazer as faixas), é preciso correr outra vez os interiores (4 e 4.1) e a seguir este menu.

### 4.5 Janelas com grades (tiros passam, jogadores não)

As janelas do rés-do-chão das casas (`_Janela_`, com grades) têm a parede de colisão aberta no vão. Em cada vão há um
colisor `COL_<casa>_Grade` na layer **Ignore Raycast** (2):

- **O jogador não passa:** o `CharacterController` colide com a layer 2.
- **Os tiros passam:** `CombatMath.ShotMask` ignora a layer 2, por isso dá para acertar em quem está do outro lado,
  entre as barras.
- **Projéteis e facada:** também passam pelo vão, porque usam a mesma máscara.

Gerado pelo menu dos interiores (4 e 4.1). Depois de o voltar a correr, corre também o 4.4 (spawns do Deathmatch).
