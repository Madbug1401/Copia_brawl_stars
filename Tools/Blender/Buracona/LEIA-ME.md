# Mapa Pique-Gemas — Buracona (ilha do Sal)

Segundo mapa de identidade cabo-verdiana: a piscina natural da Buracona (costa oeste do Sal) adaptada para arena FPS.
É uma **adaptação para gameplay**, não uma reconstrução geográfica: planalto de rocha vulcânica escura, cristas e
penedos de basalto como coberturas, arbustos nas zonas verdes da planta e, no centro, **o buraco com água
turquesa modelado em 3D** (borda rochosa, paredes em gruta, superfície, espuma e fundo de areia visível).
As gemas **nascem debaixo de água** — ficção do jogo — e saltam em arco para 8 pontos de terreno seco à volta.

Ficheiros desta pasta: `Buracona_Map.py` (gera tudo), `Buracona.blend` (resultado, texturas embutidas),
`buracona_layout.json` (dados para a análise), `analisar_rotas.py` + `analise_rotas.txt` (verificação 2D das rotas).
Imagens em `Docs/preview/buracona/`.

## Fluxo

```
planta + fotografia → Buracona_Map.py (Blender) → texturas procedurais → timeline de demonstração
→ FBX (estático / gerador / água / ondulação animada) → FPS-LAN ▸ Mapas ▸ Construir Buracona (Unity)
→ prefabs, materiais cartoon, colisores, arbustos, spawns; efeitos da água refeitos em C#
```

## 1. Blender (4.2 LTS ou mais recente; testado em 5.2.2)

```
blender --background --python Buracona_Map.py -- --out ./out --export --save-blend --render
```

ou no Blender: *Scripting → Open → Buracona_Map.py → Run Script*. O script importa as primitivas de
`../CidadeVelha/cidade_velha_pelourinho.py` e o gerador de arbustos `../CidadeVelha/leafbush_port.py`
(mantém as duas pastas lado a lado).

| Opção | Por defeito | Para quê |
|---|---|---|
| `--detail 1·2·3` | 2 | segmentos da borda, penedos e pedras soltas, envolvente |
| `--t-idle --t-warn --t-emerge --t-arc --t-float` | 1.5 / 1.8 / 0.6 / 1.1 / 3.0 s | fases da demonstração |
| `--seed` | 1960 | irregularidade da rocha, arbustos (mesma seed → mesmo mapa) |
| `--export` `--save-blend` `--render` `--anim-preview` | | FBX, .blend, imagens, sequência da água |

Cria a cena **`BU_Buracona`** (collection raiz `BU_Buracona`) e só mexe nela.

| Collection | Conteúdo |
|---|---|
| `Ground` | `GRD_Chao` (com o buraco recortado), manchas de areia, chão dos spawns; `Envolvente`: mar, falésia, montes, salinas (sem jogo) |
| `VolcanicRocks` | penedos soltos na borda, pedras pequenas decorativas (sem colisão) |
| `BuraconaPool` | `POOL_Buraco`: borda irregular, paredes em gruta (mais largas por baixo), fundo |
| `Water` | `WTR_Agua` (superfície), `WTR_Espuma` (anel junto à rocha), `GEN_Agua_Brilho` (brilho turquesa do emissor) |
| `Cover` | `CVR_*`: cristas de basalto (2,4 m), penedos altos e rochas baixas (1,1 m, meia cobertura) |
| `Bushes` | `BUSH_<nome>_S<seed>_D<densidade%>` (zona) + `BSHV_*` (folhas do `LeafBush` do jogo, só pré-visualização) |
| `GemGenerator` | `GEM_Spawn` (debaixo de água, y −1,45), `GEM_Land_00…07` (r 5,9 m, z 0,6), `GEN_Marca_*` |
| `Gems` | `Demo_Gema` |
| `Collision` | `COL_Box_*`, `COL_Tri_Chao`, `COL_Tri_Buraco_Interior`, `COL_Mesh_Buraco_Barreira_Pass` (só wireframe) |
| `Markers` | `SPAWN_Blue_*` / `SPAWN_Red_*` (5 + 5), `WP_*` (waypoints dos bots) |
| `Preview` | câmaras, sol, ondulação, bolhas, gotas do salpico, jogador de demonstração |

Câmaras: `CAM_Planta` (ortográfica, rotas), `CAM_Geral` (perspetiva), `CAM_Emissor` (aproximação à água, ativa).

### Demonstração animada

Abrir `Buracona.blend`, *Play* (frames 1–~270 a 30 fps). Marcadores da timeline:
1 Água em repouso (a textura desliza e a superfície oscila 1,5 cm) · 2-3 Brilho turquesa por baixo da superfície,
três anéis de ondulação e bolhas · 4 A gema emerge com salpico curto (coroa de gotas) · 5 Arco por cima da borda até
`GEM_Land_05` (a marca acende) · 6 Flutua e roda. O jogador azul de demonstração aproxima-se da aterragem.

### O que passa (e o que não passa) para o Unity

| Elemento | No Blender | No FBX | No Unity |
|---|---|---|---|
| Chão, rocha, borda, cristas, envolvente | malhas | ✅ `Buracona_Static.fbx` | prefab `PF_Buracona` |
| Superfície, espuma, brilho | malhas | ✅ `Buracona_Agua.fbx` | prefab `PF_Buracona_Agua` (shader `FPSLAN/FxAlpha`, sem reflexos/refração) |
| Deslizar da textura da água / oscilação | nó Mapping animado + keyframes | ❌ (animação de material) | **refeito em C#**: `WaterSurface` (MaterialPropertyBlock: offset, oscilação, pulsar) |
| Brilho do emissor (emissão animada) | animação de material | ❌ | `GemGrabManager` (emissão idle/aviso via MaterialPropertyBlock) |
| Ondulação (`ACT_Ondulacao_0…2`) | Actions de escala | ✅ `Buracona_Emissor_Anim.fbx` (só referência) | **refeita em C#**: `GemGrabVisuals` estilo `Agua` → `Fx.Ring` a crescer |
| Bolhas (`PS_Bolhas`) | sistema de partículas | ❌ | `Fx.Glow` a subir, mais rápidas perto da geração |
| Salpico (gotas) | objetos animados | ❌ (demo) | `Fx.Ring` + `Fx.Debris` + `Fx.Puff` + luz + som curto |
| Gema: emergir, arco, flutuar | Action de demonstração | ❌ | `PF_Gema` (o mesmo da Cidade Velha); movimento calculado a partir do estado do host |
| Arbustos | zona + folhas da porta do `LeafBush.cs` | ✅ só a zona | `Bush` + `LeafBush` do jogo com a mesma seed e densidade |
| Texturas procedurais | imagens geradas (embutidas no .blend) | ❌ (PNG à parte) | `Art/Environment/Sal/Buracona/Textures` |

Nenhum armature: rocha e água não se deformam.

## 2. Unity (6000.6, Built-in RP, NGO 2.13)

1. FBX em `Assets/_Game/Art/Environment/Sal/Buracona/Models/`, PNG em `.../Buracona/Textures/` (já no repositório).
2. Requer `PF_Gema.prefab` (criado pela Cidade Velha; já está no repositório).
3. Menu **FPS-LAN ▸ Mapas ▸ Construir Buracona (Pique-Gemas)** → cria/atualiza `Materials/M_BU_*.mat`,
   `Prefabs/Environment/PF_Buracona.prefab`, `PF_Buracona_Agua.prefab` e `Scenes/Buracona.unity`.
   Usa o construtor partilhado `GemMapBuilder` (o mesmo da Cidade Velha); não toca em Arena.unity, Player.prefab
   nem no ProjectSetup. Se a cena não estiver nas Build Settings, é acrescentada **no fim** (a ordem existente fica).
4. **FPS-LAN ▸ Mapas ▸ Abrir cena Buracona** → Play → Host / Treino.

### Buraco e movimento

- `COL_Mesh_Buraco_Barreira_Pass`: cilindro convexo de raio 4,75 m (= onde a rocha da borda encontra o chão),
  de −5 m a +25 m: nenhum salto, impulso ou salto de habilidade passa por cima. Layer *Ignore Raycast*: trava o
  movimento mas **os tiros atravessam por cima da água**. Não há barreiras afastadas da rocha.
- `COL_Tri_Chao` (chão com o buraco recortado) e `COL_Tri_Buraco_Interior` (paredes e fundo).
- Se alguém entrar por uma falha (ex.: teletransporte), `MapInfo.fallRecoveryY = −1,6` faz o `PlayerMotor` voltar
  a um spawn da equipa — o mesmo mecanismo que já existia para quedas (antes fixo em −20).
- Gemas largadas ao morrer junto à borda: `GemGrabManager.ValidGemSpot` rejeita pontos dentro da barreira, chão
  abaixo de 1 m do jogador (água/fundo) ou acima de 0,45 m (topo de rochas).

## 3. Verificar rotas sem Unity

```
python analisar_rotas.py out/buracona_layout.json out/Renders/planta_rotas.png out/Renders/planta_rotas_analise.png
```

Resultado com a seed por defeito (`analise_rotas.txt`): spawns ligados, 8 aterragens alcançáveis e fora da
borda/arbustos, **0 de 25 pares de spawns com linha de tiro direta**, 3 rotas no meio do mapa — esquerda e direita
por fora dos penedos laterais (gargalo 3,4 m, 54 m ≈ 8,4 s spawn→spawn) e centro pelo anel da água (gargalo 3,0 m,
49 m ≈ 7,6 s) —, distâncias às gemas iguais para as duas equipas (≈ 23 m a mais próxima, 27 m em média),
22 arbustos (351 m²). Não substitui o teste com o jogador.

## Escala

Planta 800 × 960 px a 0,05 m/px → 40 × 48 m (a Cidade Velha tem 40 × 50 m; mesmas velocidades: andar ≈ 6,5 m/s,
CharacterController r 0,4 m / 1,8 m). Cristas 2,4 m (acima dos olhos e do salto), rochas baixas 1,1 m, arbustos 2 m.
