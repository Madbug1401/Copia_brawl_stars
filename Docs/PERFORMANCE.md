# Performance do FPS-LAN

Como medir, o que já foi medido e onde está o custo. Medições de 26–29/10/2026, num i7-4600U (2 núcleos/4 threads) com
Intel HD Graphics e 8 GB de RAM: um portátil modesto, o tipo de máquina onde a performance mais conta.

## Medir: benchmark por linha de comando

O build tem um modo de benchmark (`Assets/_Game/Scripts/Core/BenchmarkRunner.cs`). Sem `-benchmark` não faz nada.

```
FPS-LAN.exe -benchmark santiago 5 60 -benchmarkQuality 1 -benchmarkRes 1280x720 -benchmarkOut C:\tmp\resultado.txt
```

- Argumentos: mapa (`pradaria`, `santiago`, `sal`, `fogo`), número de bots e segundos de medição (depois de 10 s de aquecimento).
- `-benchmarkQuality N`: nível de qualidade (0 Very Low … 5 Ultra). `-benchmarkRes LxA`: resolução em janela.
- `-benchmarkOverride antiAliasing=0,shadows=0,...`: muda uma definição para medir o seu custo
  (`antiAliasing`, `pixelLightCount`, `shadows`, `shadowDistance`, `shadowResolution`, `lodBias`).
- `-benchmarkSeed N`: semente dos spawns e dos bots (12345 por omissão).
- O VSync fica desligado e nada é gravado nas preferências. A vista é uma câmara própria que dá uma volta ao centro do
  mapa, para que corridas iguais vejam o mesmo (com a câmara do jogador, um spawn dentro de uma casa ou na praça mudava
  o resultado até 40%). Corridas repetidas variam ±4%.
- O relatório tem FPS médio, tempos de frame (p50/p95/p99/máximo), frames acima de 16,7/33,3 ms, tempo de carregamento
  do mapa, recolhas do GC e o hardware.

Para comparar duas versões: o mesmo mapa, qualidade e resolução, várias corridas de cada intercaladas (A, B, A, B),
com o PC sem mais nada a correr e sem o jogo aberto noutra janela (ocupa a porta 7777 e a partida não arranca).

## Custo por nível de qualidade

Cidade Velha, 6 jogadores, 1280×720, câmara orbital:

| Nível | FPS | p95 |
|---|---:|---:|
| Very Low | 83 | 19,6 ms |
| Low | 79 | 21,0 ms |
| Medium | 63 | 28,1 ms |
| High | 52 | 32,5 ms |
| Very High | 37 | 39,9 ms |
| Ultra | 38 | 40,2 ms |

Com a câmara do jogador, a 1280×720, Ultra ficava em ~13–16 FPS. Por isso o primeiro arranque escolhe o nível pelo
hardware (`GraphicsOptions.RecommendedQuality`): gráficas integradas em Low/Medium, dedicadas em Medium/High/Very High.
Quem já escolheu um nível não é afetado.

## Onde está o custo

- **GPU primeiro.** Nos frames lentos a CPU espera ~37 ms pela placa (`DXGI.WaitOnSwapChain`).
- **Sombras**: High sem sombras sobe de 51 para 73 FPS (+43%). É o maior custo gráfico.
- **MSAA** no Very High/Ultra: desligá-lo dá +13%. Sombras do High no Very High: +8%. Uma luz por píxel a menos: ~0.
- **Arbustos procedurais** (`LeafBush`): ~30 mil triângulos cada e projetam sombra (365 mil na Cidade Velha).
  Em jogo têm LOD: de perto a malha completa (idêntica), mais longe uma malha com 38% dos triângulos; a troca segue o
  `lodBias` de cada nível (Ultra ≈ 47 m, High ≈ 23 m, Low ≈ 9 m). Medido: Low +2,9% FPS, High +4% FPS e p95 −9%.
- **CPU**: a interface é toda IMGUI (`OnGUI`), o maior custo de CPU e de lixo de memória que resta (~1,5 ms, ~6 KB e
  ~135 alocações por frame no jogo; também ~95 das ~195 SetPass calls por frame em Low). O GC é incremental e o heap pequeno (~4 MB), por isso não causa picos visíveis.
- **Criação de objetos em combate**: os pools de projéteis e efeitos enchem-se nas primeiras utilizações (~9 ms no total,
  espalhados pelos primeiros tiros). Depois disso, 2 em 2000 frames criam um componente (0,12 ms): sem fugas.

## Já otimizado

- IMGUI sem o evento Layout durante a partida, texto sem alocações por chamada (−58% de tempo do HUD).
- Descoberta LAN: interfaces de rede numa thread de fundo (o host engasgava uma vez por segundo).
- Leitura de input sem concatenação de strings nem reflexão por frame.
- Texturas das personagens em BC7 (37 → 11 MB cada no Editor, PSNR 51–54 dB face ao original).
- Fontes de som da lava em pausa quando inaudíveis (custo dos loops de ambiente −54%).
- Pacote Sentis (não usado) removido: build 366 → 324 MB, avisos de build 493 → 2.
- LOD dos arbustos (ver acima).
- Preferências em cache (`Core/Prefs.cs`): cada `PlayerPrefs.GetInt`/`HasKey` ia ao registo do Windows (~16 µs) e o input,
  o HUD e as definições liam teclas e opções ~30 vezes por frame. Ler uma tecla passou de 19 µs para 0,12 µs;
  no jogo (build de desenvolvimento, Low) `PlayerMotor` 2,29 → 0,56 ms, `PlayerCombat` 1,65 → <0,1 ms,
  HUD 1,86 → 1,46 ms por frame: +16% FPS. Todo o código do jogo usa `Prefs` em vez de `PlayerPrefs`.
- Imagens do menu e da intro (sem compressão) libertadas quando deixam de ser precisas: a intro ao terminar, as do
  menu ao entrar numa partida (os retratos do HUD ficam); no regresso ao menu voltam a carregar-se.

## Ideias medidas mas não aplicadas

- Sombras mais curtas/baratas e MSAA desligado nos níveis altos: ganhos acima, mas mudam o aspeto. Decisão de arte.
- Colisões pré-cozinhadas no build (`Prebake Collision Meshes`): os mapas já carregam em 0,8–0,9 s; ganho esperado pequeno.
- Áudio de ambiente em ADPCM: metade do custo de descodificação, mas perda clara de fidelidade (SNR 5–13 dB contra 25 dB).
