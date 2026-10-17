# Pré-visualizações (aproximadas)

Estas imagens **não são capturas do Unity**: foram geradas fora do Editor, correndo o código real das rigs
(`CharacterRig`, `ViewModelRig`, `WeaponModels`) e do gerador do mapa (`ProjectSetup` + `ArenaLayout`) contra uma
imitação mínima da API do Unity e desenhando a geometria exportada com um rasterizador simples.
Servem para validar formas, proporções e composição. No jogo há cel shading, contornos, sombras, céu,
nevoeiro e efeitos.

- `mapa_pradaria.png` — vista geral do mapa Pradaria.
- `mapa_planta.png` — planta (vista de cima) com spawns, plataformas de salto e pontos de patrulha dos bots.
- `mapa_bosque_planta.png` — planta esquemática do mapa Bosque (spawns, torres, rampas, arbustos, lago, plataformas de salto).
  Desenhada a partir de uma cópia em Python dos dados de `ArenaLayoutBosque.cs` (sem flores nem pontos de patrulha).
- `brawlers_3a_pessoa.png` — os 6 Brawlers (da esquerda: Lince, Brasa, Névoa, Vigia, Cardo, Dex).
- `armas_1a_pessoa.png` — mãos e armas em primeira pessoa (Dex, Cardo, Vigia / Névoa, Brasa, Lince).
