# N.Cage — braços em 1.ª pessoa com o arco (Blender → Unity)

- `NCage_FPS_build.py` — monta a cena no Blender a partir do personagem (`GameJam/3d models/characters/n cage character.fbx`),
  do arco composto (`arco/base.fbx`) e da flecha (`flecha/base.fbx`): braços, unhas presas à pele (Surface Deform),
  corda puxada por um gancho, 3 flechas na corda e a animação (30 fps): Parado 1–40, Carregar 41–78, Puxar 79–95,
  Mirar 96–110, Atirar 111–125, Parado 126–150. Pasta dos modelos: variável de ambiente `GJ_MODELS`.
  Modos: `frames`, `eevee <frames>`, `anim`, `close`/`nails <frames>`, `save <ficheiro.blend>`.
- `NCage_FPS_export_unity.py` — gera `Assets/_Game/Resources/Characters/trapper_fpview.fbx`: um esqueleto com os dois
  braços, o osso `Arco`, o osso `Corda` (a deformação do gancho passa a pesos de pele) e as 3 flechas; takes
  `Parado`, `Carregar`, `Mirar` (ciclo) e `Atirar` (flechas escondidas: no jogo voam os projéteis); marcadores
  `CamRef*` e `Ponta` (de onde saem as flechas). A pose do frame 1 é a pose de repouso (sem escala nos ossos).
- No Unity: `BakedBowArms` (Scripts/Visual), ligado no `ViewModelRig` para o estilo `Bow` (trapper). Materiais: toon do
  personagem na pele e os toons do arco/flecha do jogo (`Resources/Weapons/Bow`, `Arrow`) — mesma textura e UV.
