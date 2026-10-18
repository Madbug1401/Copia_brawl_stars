# Integração de Cidade Velha — 2026-10-18

## Como jogar

No menu, escolher PLAY ONLINE ou PLAY LOCAL > criar servidor, selecionar GEM GRAB, escolher a ilha de Santiago no carrossel e premir JOGAR / CRIAR SERVIDOR. O host carrega CidadeVelha antes de iniciar a partida. Os restantes jogadores entram pelo código/IP do host.

A Arena continua disponível em Pradaria. GEM GRAB requer Santiago. A Cidade Velha também respeita os restantes modos escolhidos no menu.

## Trabalho preservado

O commit fcabe86 guarda o trabalho local de cooperativo, Mr.S, animações Rifle do Lince e alterações associadas. A configuração de ícones Android em ProjectSettings.asset ficou fora dos commits.

A integração inclui mapa/cidade-velha e nico/integracao. Fragoso, Fragoso-cloud, Bosque, Buracona e selecao-mapas não foram integradas. Os nomes seguem a decisão da equipa: gunslinger = Ekko; sprayer = Mr.S; shotgunner = Riot; artillery = Lupus Marinus; trapper = N.Cage; sniper = Lince. IDs e equilíbrio não foram alterados nesta correção de nomes.

## Verificação no Unity 6000.6.0f1

- Compilação no Editor concluída sem erros.
- Início de partida através do método usado pelo botão do menu: Arena > CidadeVelha, GemGrab, host ativo, jogador local + 3 bots, 3 gemas disponíveis na observação e um NetworkManager.
- Captura da câmara confirmou o mapa e as gemas visíveis.
- Saída da partida e início de ItTakesTwo na Arena: host ativo, modo cooperativo correto, 4 jogadores, um NetworkManager e um AudioListener ativo.
- Preferências de mapa e modo anteriores restauradas após os testes; Editor saiu do Play.

Ainda falta verificar a entrada de um segundo PC por LAN, uma partida completa e uma build distribuível. A captura de teste está em Temp/cidade-velha-teste.png (ficheiro local ignorado pelo Git).

A etiqueta backup/main-antes-merge-2026-10-18 preserva o main anterior (6477848).
