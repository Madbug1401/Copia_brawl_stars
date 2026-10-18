# Integração Fragoso em nico/integracao

Origem: `Fragoso`, commit `60895dc6fb9b554cdd5014ee3a934df36bba468d`.
Base: `47043e616973d561ac76c797df0fd55630de59af`.

## Resultado

- Integrados balanceamento dos seis personagens, especiais, progressão, atributos efetivos, skins, pets, missões e recompensas.
- Preservados Buracona, Cidade Velha, Arena e o cooperativo It Takes Two, incluindo atribuição de personagens, interação e reanimação.
- Dano aplica primeiro o escudo e subtrai a vida uma única vez. O rastreio de dano mantém a compatibilidade com a reanimação cooperativa.
- GemGrab regista objetivos e respeita o modo escolhido pelo host. A vitória cooperativa usa o mesmo envio autoritativo de recompensas, com proteção contra duplicação.
- Menus de progressão, abas, definições, pausa e resultados usam fonte, paleta, painéis de vidro, cantos arredondados, contornos ciano e brilho do menu principal. A progressão tem acesso direto no menu principal.
- O modo competitivo fica acessível na preparação da partida e desativado no cooperativo.
- Modelos existentes continuam suportados; skins e armas duplas respeitam os dois caminhos visuais.

## Verificação

- Unity 6000.6.0f1: compilação sem erros.
- Compilação separada dos 84 ficheiros C# de runtime preparados no index, excluindo trabalho local concorrente: sucesso.
- EditMode: 74 testes passaram, 0 falharam.
- PlayMode: `SixBrawlersSpecialsProgressionAndSync` passou, abrangendo os seis personagens, especiais, progressão e sincronização.
- Buracona: host, quatro jogadores, um NetworkManager, zero scripts em falta, gemas, oito pontos de aterragem e spawns das duas equipas; recuperação após queda passou.
- Cidade Velha: host com quatro jogadores e geração de gemas passou.
- Arena: Deathmatch com quatro jogadores passou; transição Buracona > Cidade Velha > Arena passou.
- Cooperativo: dois aliados, recompensa recebida na vitória e fim repetido sem duplicar a recompensa.
- Capturados 13 ecrãs de menus/abas e os ecrãs de pausa e resultado cooperativo. Corrigido fundo herdado dos botões e contraste de seleção.

Testes de progressão e fim de partida utilizaram armazenamento em memória. As capturas e relatórios de execução estão em `Temp/fragoso-menu-review` e `Temp/fragoso-*-tests.json` (não versionados).

Não foi realizado teste LAN entre dois computadores físicos nem gerada uma nova build Windows nesta integração.

A branch main permanece na base indicada. Alterações locais de introdução, novos modelos e novo HUD de outras sessões foram preservadas fora deste commit.
