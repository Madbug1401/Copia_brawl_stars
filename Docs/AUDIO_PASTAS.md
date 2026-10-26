# Pastas de áudio — onde pôr cada ficheiro

Guia para os ~330 sons da "Lista de Sons em Falta" (26/10/2026). Todas as pastas estão em
`Assets/_Game/Resources/Audio/`. Os IDs (A01, B02…) são os da lista.

Os ficheiros `.gitkeep` só existem para o Git guardar as pastas vazias; podem ser apagados quando a pasta tiver sons.

## Efeitos (`Sfx/`)

| Pasta | IDs | Conteúdo |
|---|---|---|
| `Sfx/SubstituirProvisorios/` | A01–A13 | Os 13 sons gerados por código (SuperShot, Launch, Respawn, UiClick…). Prioridade: entram sem mudar código. |
| `Sfx/Interface/Menu/` | B01–B17, B25–B28 | Botões, separadores, sliders, carrossel de personagens, modo/mapa, pausa, placar, intro. |
| `Sfx/Interface/Rede/` | B18–B24 | Connecting_loop, Connected, ConnectFail, Disconnected, PlayerJoined/Left, ServerFound. |
| `Sfx/Interface/Progressao/` | B29–B37 | Skins, pets, upgrades, XP, missões, recompensas raras, Toast. |
| `Sfx/Partida/Comum/` | C01–C22 | Contagem, início/fim, derrota, kill feed, vida baixa, dano, etc. |
| `Sfx/Partida/KnockoutWipeout/` | C23–C28 | Rondas e pontos de equipa. |
| `Sfx/Partida/Showdown/` | C29–C33 | Tempestade. |
| `Sfx/Partida/GemRush/` | C34–C41 | Gemas e contagem de vitória. |
| `Sfx/Partida/ItTakesTwo/` | C42–C53 | Vagas, reanimar, placas, missão. |
| `Sfx/ArmasGerais/Corpo-a-corpo/` | D01–D03 | MeleeHit, MeleeHitWall, WeaponEquip. |
| `Sfx/ArmasGerais/Impactos/` | D04–D14 | BulletWhizz, Impact*, Ricochet, Cover*, ExplosionDistant. |
| `Sfx/ArmasGerais/Super/` | D15 | SuperActivate. |
| `Sfx/PorPersonagem/Ekko/` | D16–D20 | `Ekko_*` |
| `Sfx/PorPersonagem/Riot/` | D21–D24 | `Riot_*` |
| `Sfx/PorPersonagem/Lince/` | D25–D30 | `Lince_*` |
| `Sfx/PorPersonagem/MrS/` | D31–D35 | `MrS_*` |
| `Sfx/PorPersonagem/Lupus/` | D36–D41 | `Lupus_*` |
| `Sfx/PorPersonagem/NCage/` | D42–D50 | `NCage_*` |
| `Sfx/Movimento/Passos/<Superfície>/` | E01–E08 | Pedra, Madeira, Areia, Cascalho, Vulcanico, Metal, Agua, CamadaSprint. |
| `Sfx/Movimento/Acoes/` | E09–E20, E23 | Salto, aterragem, agachar, slide, jump pad, arbustos, Knockback. |
| `Sfx/Movimento/Agua/` | E21–E22 | WaterEnter/Exit, WaterWade_loop. |
| `Sfx/EstadosAmbiente/` | E24–E32 | Veneno, escudo, lava, cair do mapa, invisível no arbusto. |
| `Sfx/Pets/` | secção I | Sons por espécie de pet. |

As pastas antigas (`Armas`, `ExplosivosArmadilhas`, `ImpactosCombate`, `Manuseamento`, `OutrosAtaques`,
`Personagem`, `Recompensas`) continuam com os 26 efeitos já entregues. Não mover esses ficheiros:
o `SfxCatalog.asset` aponta para eles.

## Vozes (`Voices/<chave>/`)

Uma pasta por personagem: `riot`, `ekko`, `lince`, `mrs`, `lupus`, `ncage`. Nome do ficheiro:
`Voice_<chave>_<Tipo>_##.wav` (ex.: `Voice_riot_Kill_03.wav`). As falas de seleção existentes
(`Voices/riot.mp3`, etc.) ficam onde estão, porque o `MenuAudio` carrega-as por esse caminho.
Riot grava-se sempre com voz feminina.

## Música (`Music/Mapas/`)

Músicas dos mapas. `Music/menu.wav` e `Music/server.mp3` ficam onde estão (carregadas pelo `MenuAudio`).

## Regras de entrega (resumo da lista)

- WAV 48 kHz, 16 ou 24 bits. Mono para sons 3D; estéreo só para UI, música e ambientes.
- Sem reverb pesado nem música de fundo. Cortes limpos com fade de 5–10 ms.
- Loops com sufixo `_loop`; variações `Nome_01.wav`, `Nome_02.wav`…
- Tom arcade/cartoon, nunca militar realista. Vozes em inglês.

## Atenção

- O `AudioImportSettings` força **mono** em tudo o que está dentro de `Sfx/`. Os sons de UI em estéreo
  (`Sfx/Interface/`) vão ser convertidos para mono ao importar até esse script ser ajustado.
- Pôr o ficheiro na pasta não basta para o som tocar: cada efeito novo precisa de entrar no
  `SfxCatalog.asset` (e, para efeitos novos, no enum `Sound` e no código que o dispara).
