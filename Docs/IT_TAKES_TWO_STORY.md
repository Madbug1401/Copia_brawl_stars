# IT TAKES TWO - The Pelourinho Heist

A história é jogada dentro de IT TAKES TWO. O menu principal mantém PLAY ONLINE, PLAY LOCAL e QUIT.

## Jogar

1. Escolher PLAY ONLINE ou PLAY LOCAL > criar servidor.
2. Selecionar IT TAKES TWO. A missão usa Cidade Velha / Pelourinho automaticamente.
3. Escolher Lince ou Riot e iniciar. A solo, o parceiro bot recebe a personagem complementar.
4. Sobreviver às cinco ondas. Ficar perto ativa o Laço; eliminar uma onda cura e reabastece a dupla.
5. Quando o parceiro cair, aproximar-se e manter **F**, sem mover ou atacar. Soltar a tecla, afastar-se, mover ou sofrer dano interrompe a reanimação. A tecla pode ser alterada em Definições > Controlos.
6. Depois da última onda, cada pessoa ocupa uma das placas azuis. As duas ficam verdes e a barra deve chegar a 100%. A solo, o bot procura a placa oposta à do humano.
7. Aproximar-se da Gem dourada com o parceiro a menos de 4 metros. A recuperação termina a missão e revela uma pista sobre outras Gems.

Se os dois caírem, a missão falha. Vitória e derrota reiniciam a missão após pelo menos 14 segundos, permitindo ler o desfecho. Esc abre a pausa e permite sair.

## Cooperativo LAN

O host aceita dois humanos. O segundo entra pelo código/IP, tal como nos outros modos. Quando entra, substitui o parceiro bot; se sair, o bot regressa. O primeiro humano mantém a escolha de Lince/Riot, e o segundo recebe a outra personagem. A missão não permite trocar a dupla nem adicionar bots pelo menu de pausa.

Etapa, onda, placas, progresso do mecanismo e fala atual são NetworkVariables do MatchManager. Quem entra a meio recebe o estado atual. Os marcadores e objetos decorativos são reconstruídos localmente a partir desse estado.

## História e âmbito

As referências são `Better_Together_Historias_das_Personagens.pdf` e `it takes two.pdf`, na pasta `GameJam/storyTelling` fornecida pelo utilizador.

A adaptação preserva Riot impulsiva e provocadora, Lince observador e estratégico, a relação do casal e as falas de perigo/reanimação. As falas novas entre ondas e no epílogo foram escritas para esta implementação. As quatro etapas são The Infiltration, The Resistance, The Puzzle e The Discovery.

Esta versão reutiliza as ondas de combate existentes para manter o desenvolvimento simples. O mecanismo liberta a Gem; não altera a geometria do mapa. As falas aparecem como legendas, sem dobragem nem cinemáticas. XP, desbloqueios persistentes e novas missões ficam para desenvolvimento posterior; não são prometidos como funcionalidades disponíveis.

## Ajustes no Inspector

No MatchManager: `coopTotalWaves`, `coopIntermissionSeconds`, regras do Laço e reanimação; posições `coopPlateA`, `coopPlateB`, `coopGemPosition`; `coopPlateRadius`, `coopPuzzleSeconds`, raios de recolha e presença do parceiro. A primeira preparação e os resultados duram pelo menos 14 segundos.

## Código

- `MatchManager.Coop.cs`: ondas, parceiro, Laço e reanimação.
- `MatchManager.CoopStory.cs`: etapas, dupla, falas, mecanismo, Gem e visuais.
- `GameUI.CoopStory.cs`: objetivo, legendas, marcadores e desfecho.
- `GameUI.MainMenu.cs`: seleção da missão e dupla inicial.
- `PlayerNetwork`, `PlayerHealth`, `PlayerCommands`, `InputReader`, `GameInputSettings`: interação e eventos da dupla.
- `BotBrain`: acompanhar o humano, ajudar na placa e manter a interação para reanimar.
- `ConnectionManager`: mapa da missão e limite de dois humanos.

## Validação

Compilação C# com o compilador e as referências do Unity: sem erros. A validação de execução usa uma cópia temporária do projeto em `Temp/CoopValidation/Project` e a porta 28777, para não interromper o Editor nem ocupar a porta normal do jogo.

O Unity 6000.6.0f1 compilou os scripts e o código de rede. O teste de execução terminou com **32 verificações aprovadas e código de saída 0**: arranque do host, dupla complementar, desativação do Pique-Gemas durante a missão, interação obrigatória para reanimar, interrupção por dano ou ao soltar a tecla, regresso com metade da vida e fala correta, cinco ondas com 2 a 6 guardas, transição para o puzzle, placas simultâneas, reinício da barra ao sair, escolha da placa oposta pelo bot, criação dos objetos da missão, recolha dependente da presença do parceiro, vitória, reinício e derrota conjunta.

O teste manipula posições e elimina os guardas por código para validar as regras; não representa uma partida completa jogada. Na inicialização da cópia, o Unity registou uma exceção interna em `UnityEditor.Search.SearchDatabase`; as verificações da missão passaram. O relatório original encontra-se em `Temp/CoopValidation/report.txt`.

Uma partida com duas pessoas em computadores diferentes, a leitura visual do HUD e o equilíbrio das ondas ainda exigem teste jogado.
