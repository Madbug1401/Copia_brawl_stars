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
