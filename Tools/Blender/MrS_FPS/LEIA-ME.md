# Mr. S — braços em 1.ª pessoa (Blender → Unity)

- `MrS_FPS_build.py` — monta a cena no Blender a partir do personagem (`GameJam/3d models/characters/ms character.fbx`)
  e da lata (`GameJam/3d models/arma ms/base.fbx`): braços, unhas, polegares a carregar no bico, latas, spray e as
  animações Parado (1–60), Pintar (61–130) e Recarregar/abanar (131–210). Ajustar `CHAR`, `CAN`, `TEX` e `OUT` no topo.
  Modos: `still`, `grip`, `frames`, `anim`, `save <ficheiro.blend>`.
- `MrS_FPS_export_unity.py` — gera `Assets/_Game/Resources/Characters/sprayer_fpview.fbx`: só o braço esquerdo (o direito é
  o mesmo com escala X = -1 no Unity), takes `L_*`/`R_*` (Idle, Spray, Shake), marcadores da lata e da câmara.
  A pose do frame 1 é a pose de repouso (sem escala nos ossos).
- No Unity: `BakedViewArms` (Scripts/Visual) e `FpViewArmsImport` (importação Legacy). Ligado no `ViewModelRig` para o
  estilo `DualSprayCans` quando existe `Resources/Characters/<brawlerId>_fpview`.
