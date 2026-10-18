# Mapa de sobrevivência — Fogo (ilha do Fogo)

Terceiro mapa de identidade cabo-verdiana, para o modo **SHOWDOWN** (último vivo): um planalto de cinza e escória
com rios de lava, rodeado de mato seco, e no centro **o vulcão** (Pico do Fogo estilizado) com uma **arena de rocha
dentro da cratera**. A tempestade de cinza fecha por fases e a **zona final fica dentro do vulcão**.
É uma adaptação para gameplay, não uma reconstrução geográfica.

Referências em `referencias/`: `planta_fogo.png` (planta tipo Brawl Stars: círculo vermelho = vulcão,
verde = água → lava, azul = arbustos). A fotografia de lava serviu de guia para o material (crosta preta,
fendas vermelhas, fluxo laranja). O "ficheiro do mapa" referido no pedido não foi recebido; o mapa segue a planta.

Ficheiros: `Fogo_Map.py` (gera tudo), `Fogo.blend` (resultado, texturas embutidas), `fogo_layout.json`
(máscara da lava, entradas, volcão — lido pelo construtor do Unity). Imagens em `Docs/preview/fogo/`.

## Fluxo

```
planta → Fogo_Map.py (Blender) → relevo + vulcão + lava + coberturas + arbustos + marcadores
→ FBX (estático / lava / referência dos efeitos) + fogo_layout.json
→ Unity: FPS-LAN ▸ Mapas ▸ Construir Fogo (sobrevivência) → Scenes/Fogo.unity + prefabs PF_Fogo, PF_Fogo_Lava
→ FPS-LAN ▸ Mapas ▸ Validar Fogo (percursos, lava, zona final, tiros, desempenho)
```

## 1. Blender (testado com bpy 5.2.2)

```
blender --background --python Fogo_Map.py -- --out ./out --export --save-blend [--render] [--anim-preview]
```

Opções: `--detail 1|2|3`, `--seed`, `--samples`, `--res`, `--plan caminho.png`. O script reutiliza as primitivas de
`../CidadeVelha/cidade_velha_pelourinho.py` e de `../Buracona/Buracona_Map.py` (mantém as pastas lado a lado).
Depois de gerar, copia `out/FBX/*.fbx` para `Assets/_Game/Art/Environment/Fogo/Models/` e `out/fogo_layout.json`
para `Assets/_Game/Art/Environment/Fogo/Data/`.

Collections (raiz `FG_Fogo`): `FG_Terrain` (relevo único, simplificado nas zonas planas), `Volcano` (fragas, arcos e
penedos das entradas), `FG_Lava` (rios, cratera, escória da margem), `FG_Cover` (muros, vedações, pedras, pilares),
`Bushes` (zonas `BUSH_*` + folhagem `BSHM_*`), `Markers` (spawns, waypoints, jatos, luzes, fumarolas),
`Collision` (colisor do chão = mesma malha do relevo, caixas `COL_Box_*`, limites), `FX` (demonstração).
Câmaras: `CAM_Planta` (ortográfica), `CAM_Geral`, `CAM_Interior`, `CAM_Lava`.

Regras de desenho aplicadas pelo script:
- vulcão com crista (~9 m) e **4 desfiladeiros iguais** a 45°/135°/225°/315° (6,4 m de largura), rampas abaixo de 46°;
- a arena interior fica 1,5 m abaixo do planalto, em rocha seca; a lava da cratera são 4 poças laterais e o **eixo de cada
  entrada é sempre rocha** (ponte natural até ao centro);
- nada da planta tapa os corredores das entradas, nem fica pilar em cima de um spawn;
- leito da lava 0,38 m abaixo do chão com margem a 43°: sai-se da lava **a andar** (sem ilhotas secas dentro da lava).

### Efeitos que só existem no Blender (demonstração) e como foram refeitos no Unity

| No Blender (timeline de 8 s, `--anim-preview`) | No jogo |
|---|---|
| fluxo da lava (mapeamento animado), pulsação da emissão | shader `FPSLAN/Lava` (UV em XZ do mundo, duas camadas de calor, crosta que desliza devagar, pulsação) |
| bolhas `FX_Bolha_*` que crescem e rebentam | `LavaFx`: bolhas com pooling (máx. 14), só perto da câmara |
| gotas `FX_Gota_*` em arco | `LavaFx`: gotas balísticas (máx. 36) que voltam à lava ou desaparecem no chão |
| jato `FX_Jato` + anel de aviso | `LavaJets` (rede): 1,6 s de aviso (anel, bolhas, a superfície incha) → 1,1 s de explosão de lava (língua que sobe e cai + até 160 gotas irregulares em pool, a arrefecer de laranja para vermelho escuro) com dano; horário determinístico |
| fumo das fumarolas, brilho da cratera | `VolcanoAmbience`: fumo moderado, luz laranja a pulsar, brasas nas entradas |

Materiais Principled/emissão do Blender não passam no FBX tal como estão: o construtor do Unity cria materiais
cartoon `M_FG_*` (FPSLAN/Toon) com as mesmas texturas e o material de lava próprio.

## 2. Unity

1. Abrir o projeto, esperar pela compilação.
2. **FPS-LAN ▸ Mapas ▸ Construir Fogo (sobrevivência)** — cria/atualiza materiais, prefabs, `Scenes/Fogo.unity` e acrescenta a
   cena às Build Settings. Não mexe na Arena, Cidade Velha nem Buracona.
3. **FPS-LAN ▸ Mapas ▸ Validar Fogo** — relatório na consola (ver abaixo).
4. No jogo: menu → modo **SHOWDOWN** → mapa **Fogo**. Os outros modos sem gemas (Deathmatch…) também abrem o Fogo,
   sem tempestade.

Sistemas (todos controlados pelo host; estado replicado mínimo, quem entra a meio fica sincronizado):
- `StormManager` + `StormVisual` — tempestade de cinza só em Showdown. Fases (espera / fecho / raio / dano):
  40 s / 30 s / 31 m / 4 · 28 / 26 / 21 m / 6 · 24 / 24 / 12,5 m / 9 · 20 / 18 / 7,5 m / 13 · 16 / 14 / 4,5 m / 18 (depois 24/s).
  Centro = centro da arena interior; a partir de 13 m a zona segura tem teto de 3,6 m (não conta o topo de rochas).
  Painel com contagem, aviso e ecrã acinzentado fora da zona. Bots seguem fora → boca → faixa → interior.
- `LavaArea` — máscara de 0,4 m gerada pelo mesmo script que desenha a lava; dano 24/s só com os pés na lava.
- `LavaJets`, `LavaFx`, `VolcanoAmbience` — ver tabela acima.
- Reinício da partida reinicia tempestade e jatos (`MatchManager.ServerStartMatch`).

## 3. Validação automática (`FogoValidation`)

Percursos com um CharacterController igual ao do jogador (raio 0,4, altura 1,8, rampa 46°, degrau 0,35):
as 4 entradas × 2 faixas até ao centro; ligação de todo o chão ao centro (grelha 0,4 m); saída da lava;
superfícies acima do teto dentro da zona final; linhas de tiro pelas entradas e por cima da lava; resumo da tempestade;
triângulos/renderers/materiais e arbustos sem colisores.
