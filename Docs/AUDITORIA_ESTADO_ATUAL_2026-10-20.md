# Auditoria do estado atual — Chiefs & Gems / FPS-LAN

Data: 20 de outubro de 2026. Âmbito: checkout atual, incluindo alterações locais por integrar.

## Parecer

O projeto está numa fase de protótipo avançado/alpha com bastante conteúdo e sistemas de suporte. Tem uma identidade reconhecível: combate FPS com personagens distintas, mapas de Cabo Verde, partidas LAN e companheiros. A prioridade agora deve ser consolidar regras, equilíbrio e experiência completa de uma sessão.

O maior retorno vem de corrigir a progressão, fechar falhas de autoridade e validar partidas reais. Acrescentar personagens ou modos neste momento aumenta a superfície de manutenção sem resolver esses problemas.

Esta avaliação é uma auditoria de código, configuração, assets textuais, documentação e resultados históricos disponíveis. Não foi feita uma nova partida jogada, captura visual, medição com Profiler ou execução da suíte Unity nesta auditoria. Havia processos Unity ativos; não foram interrompidos. Não atribuo FPS, qualidade visual final, estabilidade LAN real ou aprovação dos testes atuais sem essa medição. Problemas abaixo são identificados como confirmados por leitura, riscos ou propostas.

## Base observada

- Unity 6000.6.0f1; Netcode for GameObjects 2.13.3.
- 109 ficheiros C# de runtime, com cerca de 23,7 mil linhas não vazias segundo a contagem PowerShell.
- Seis personagens: Ekko, Mr.S, Riot, Lupus Marinus, N.Cage e Lince; ataques e especiais diferenciados.
- Seis modos no enum: Deathmatch, Wipeout, Knockout, Showdown, It Takes Two e Gem Rush.
- Quatro cenas habilitadas para build: Arena, Cidade Velha, Buracona e Fogo. Há também três cenas sandbox.
- Host/join LAN, descoberta de servidores, IP e porta, timeout e mensagens de ligação. Limite de quatro clientes humanos; cooperativo a dois.
- Bots em três dificuldades; combate, desvio local de obstáculos, resgate cooperativo e reação à tempestade/lava.
- Vida, regeneração, assistências, proteção de spawn, estados, cobertura destrutível, projéteis, cones, zonas e armadilhas.
- Perfil local, backup, checksum, XP global/personagem/pet, moedas, melhorias, skins, missões, conquistas e alimentação UPRA.
- A configuração inicial tem seis pets, dezoito skins, três missões diárias, três semanais e oito conquistas/desafios.
- O cooperativo tem cinco ondas por defeito, resgate, laço entre parceiros e uma fase de puzzle com duas placas.
- A pasta de build identifica uma versão de 18/10/2026; a árvore atual contém alterações posteriores nas armas e ferramentas. Essa build não prova o comportamento dos ficheiros atuais.

Pontos positivos a preservar: definições de ataques separadas da resolução de efeitos; dano e resultados calculados no host; limites de atributos; normalização competitiva; IDs estáveis; pooling parcial de efeitos; buffers físicos sem alocação em vários caminhos; recuperação do perfil e testes de regras.

## Problemas prioritários

P1 = corrigir antes de considerar a progressão/regras estáveis. P2 = corrigir na consolidação da alpha. P3 = acabamento ou expansão. As prioridades refletem impacto no jogo; não significam que todo o problema provoque crash.

### A01 — P1 — Compras de melhorias sem benefício

**Confirmado nas fórmulas/configuração.** Há cinco níveis compráveis, mas cinco dos seis atributos atingem o teto no nível quatro.

| Atributo | Bónus nível 4 | Bónus nível 5 | Ganho da última compra |
|---|---:|---:|---:|
| Vida | 12% | 12% | 0 |
| Dano | 8% | 8% | 0 |
| Regeneração | 24% | 25% | 1 ponto percentual |
| Velocidade | 4% | 4% | 0 |
| Recarga, bónus usado na fórmula | 10% | 10% | 0 |
| Eficiência do especial | 15% | 15% | 0 |

O custo pretendido do quinto nível é 700 moedas. São 3.500 moedas por personagem em cinco compras sem ganho; 21.000 para seis personagens. Isto não inclui o quinto nível de regeneração, que ainda altera o resultado.

**Correção:** alinhar quantidade de níveis e tetos; mostrar na compra o valor efetivo antes/depois; impedir compras com delta zero. Se já houve compras, prever reembolso/migração. Não aumentar todos os tetos automaticamente: isso altera o equilíbrio PvP.

Evidência: `ProgressionConfig.cs`, `CharacterUpgradeManager.TryUpgrade`, `StatResolver.BonusFor`, `Resources/GameExperience/ProgressionConfig.asset:50`.

### A02 — P1 — Progressão declarada pelo cliente não é progressão comprovada

**Confirmado na fronteira de confiança.** `PlayerNetwork.Loadout` é escrito pelo dono. O host limita níveis e bónus, mas recebe do cliente o nível de personagem, melhorias, pet e nível do pet. O cliente pode declarar o máximo permitido sem demonstrar que o ganhou. Em UPRA, o stock também é declarado pelo cliente; o consumo acontece no retorno ao dono.

O limite impede números arbitrários, mas não comprova propriedade ou saldo. O save tem checksum para corrupção; o próprio código reconhece que não oferece proteção contra manipulação.

**Correção proporcional ao objetivo LAN:** assumir explicitamente perfis locais de confiança nas partidas casuais; validar e congelar o loadout no host por vida/partida; manter um saldo de sessão para consumíveis. Para torneios, usar atributos normalizados e equipamento cosmético. Backend só se houver necessidade real de progressão confiável entre máquinas; não é requisito para uma boa experiência LAN entre amigos.

Evidência: `PlayerNetwork.cs:35`, `EffectiveStats.cs:113`, `PlayerNetwork.PetFood.cs`, `SaveManager.cs`.

### A03 — P1 — Movimento recebido do dono sem validação de plausibilidade

**Confirmado por leitura.** `PlayerMotor.State` permite escrita pelo dono; o host interpola posição recebida. Não encontrei nesse caminho validação de deslocamento máximo, limites do mapa, valores não finitos ou atravessamento de paredes. A validação de origem dos tiros usa a posição que o host conhece, também originada no cliente.

**Impacto:** um cliente modificado pode falsear deslocamento e aproximar a origem validada dos ataques de posições ilegais. A boa resposta local atual não resolve essa integridade.

**Correção:** pelo menos validar estado finito, velocidade/deslocamento e transições autorizadas de respawn, salto, plataforma e knockback. Se o projeto evoluir para competitivo rigoroso, considerar simulação autoritativa com previsão e reconciliação. Medir antes o custo dessa mudança.

Evidência: `PlayerMotor.cs:35`, `PushState`, `RemoteUpdate`, `PlayerCombat.FireRpc`.

### A04 — P1 — Troca de personagem pode restaurar vida e munição durante a partida

**Confirmado no código.** `ChangeHero` bloqueia apenas cooperativo e Gem Rush. `PlayerHealth.OnHeroChanged` enche vida, recarrega e repõe o especial quando o jogador está vivo. A seleção em `ProgressionUI` também só aplica o bloqueio nesses dois modos. Além disso, `HeroId` é escrito pelo dono; uma regra apenas na função de UI não protege o host.

**Correção:** autorizar a troca no servidor, apenas em lobby, morto ou intervalo expressamente permitido. Aplicar a personagem no spawn seguinte. Definir o comportamento para todos os modos, incluindo competitivo.

Evidência: `PlayerNetwork.cs:190`, `PlayerHealth.cs:84`, `ProgressionUI.cs:454`.

### A05 — P1 — Participação por movimento não é amostrada no cooperativo

**Confirmado no fluxo.** `MatchManager.Update` entra em `ServerCoopUpdate` e retorna antes da chamada periódica a `MatchStatsTracker.ServerSample`. O ciclo cooperativo não faz essa chamada. A validação de participação exige tempo e dano ou distância; um participante que só ajuda no puzzle/resgate pode terminar com atividade considerada insuficiente.

**Correção:** amostrar atividade também no cooperativo e reconhecer contribuições próprias do modo, como resgates e puzzle. Medir atividade durante a fase jogável, sem contar espera como contribuição.

Evidência: `MatchManager.cs:125`, `MatchManager.Coop.cs:125`, `XPManager.IsParticipationValid`.

### A06 — P1 — Estado de combate pode atravessar partidas e rondas PvP

**Ausência de limpeza confirmada; efeito em partida precisa de reprodução.** O início genérico da partida e o início de ronda Knockout não chamam `CombatSystem.ClearAll`. Já existe essa limpeza no início cooperativo e na fase de puzzle. Há minas com 25 segundos de vida e armadilhas com 30, configuradas para persistir à morte do dono. Os intervalos de resultados/ronda podem ser menores.

**Risco:** minas, zonas ou projéteis antigos permanecem na ronda seguinte; posições perigosas e mortes podem ser atribuídas a ações da ronda anterior.

**Correção:** um reset autoritativo da ronda, com notificação a todos os clientes, que limpe ataques pendentes, zonas, projéteis e armadilhas. Testar duas partidas e três rondas consecutivas. Só limpar no host não remove automaticamente os visuais dos clientes.

Evidência: `MatchManager.cs:195`, `MatchManager.cs:249`, `CombatSystem.ClearAll`, assets `artillery_Super_Delivery` e `trapper_Super_Delivery`.

### A07 — P2 — Bots não têm estratégia própria para Gem Rush

**Confirmado na lógica inspecionada.** O cérebro procura inimigos, patrulha, trata tempestade/lava, resgate e objetivo cooperativo. Não existe uma decisão específica para recolher gemas, guardar o portador, recuar com dez gemas ou interromper a contagem inimiga.

Podem apanhar gemas por proximidade, mas isso não equivale a jogar o objetivo. O seu desempenho não representa o equilíbrio de uma partida humana.

**Correção:** máquina de prioridades por modo; papéis simples de coletor, proteção e pressão; objetivo acima da perseguição irrestrita. Para os mapas mais complexos, avaliar navegação por grafo/NavMesh em vez de apenas contornar o obstáculo imediato.

Evidência: `Bots/BotBrain.cs`, `GemGrabManager.cs:384`.

### A08 — P2 — Imunidade de spawn protege dano, mas não uniformemente os estados

**Confirmado nas verificações.** `PlayerHealth.ServerApplyDamage` rejeita dano durante proteção. `EffectResolver.ApplyStatuses` pode continuar depois dessa rejeição e aplica slow/root/DOT/knockback sem consultar `IsProtected`. A rotina de zona também pode aplicar slow independentemente do dano.

**Correção:** definir se a proteção é só de dano ou também de controlo. Recomendo proteção completa até atacar ou expirar, com feedback visual claro. Cobrir raiz, veneno, slow e impulso nos testes.

Evidência: `PlayerHealth.ServerApplyDamage`, `EffectResolver.cs:131`, `EffectResolver.ZoneTick`.

### A09 — P2 — Um slow expirado pode influenciar o próximo

**Confirmado na ordem das operações.** `ServerApplySlow` atualiza `SlowUntil` antes de consultar `IsSlowed` ao escolher `SlowFactor`. Com um slow anterior já expirado, a consulta passa a indicar ativo e pode preservar o fator antigo mais forte. O resultado depende do histórico até um `ServerClear`.

**Correção:** guardar o estado ativo antes de alterar o prazo; se o efeito anterior expirou, começar pelo novo fator. Definir também se reaplicação fraca prolonga o efeito forte.

Evidência: `Combat/StatusEffects.cs:50`.

### A10 — P2 — Asset de custos contém uma sequência suspeita

**Anomalia textual confirmada; consequência no Unity não validada.** `ProgressionConfig.asset:51` termina `upgradeCosts` com `...bc020000i`. O `i` não pertence a uma representação hexadecimal normal de um array compacto. A classe declara `{100, 200, 350, 500, 700}`, mas não assumi que o asset carregado preserva esses custos.

**Correção:** abrir e validar o array efetivo no Inspector e num teste com `Resources.Load`, voltar a serializar o asset e testar os cinco preços. Os cálculos económicos desta auditoria usam os valores declarados na classe como intenção de design.

### A11 — P2 — Recompensas estão desequilibradas entre PvP e treino

**Confirmado nas fórmulas.** Eliminações/assistências contra bots reduzem apenas o XP dessas ações. Moedas por eliminação, XP do pet por eliminação e métricas de missões/conquistas continuam com contagem integral. Participação, vitória e objetivos também não têm uma redução global de treino.

**Impacto:** reduzir XP de bots não impede que o caminho mais fácil concentre moedas, pets e desbloqueios. Isso pode ser aceitável num jogo LAN casual, mas tem de ser uma decisão de design.

**Correção:** definir claramente treino, cooperativo e PvP; recompensar cada um pelo esforço/tempo esperado. Separar conquistas de habilidade PvP de conquistas de participação. Evitar apenas cortar recompensas sem oferecer um percurso divertido para quem joga sozinho.

### A12 — P2 — Progresso e equipamento são atribuídos pelo estado no fim

**Confirmado na composição do resultado.** O relatório usa o brawler e pet atuais no final e credita-lhes o resultado acumulado. Como a troca de personagem é possível em vários modos, pode-se jogar quase toda a partida com uma personagem e entregar o XP a outra no fim. O mesmo princípio exige uma regra para mudanças de pet.

**Correção:** bloquear equipamento durante a partida ou contabilizar tempo/contribuição por personagem e pet. Mostrar como o XP foi dividido.

Evidência: `MatchStatsTracker.cs:121`, `PlayerProgressionManager.Apply`.

## Progressão e economia

### Ritmo calculado

As curvas atuais exigem 34.300 XP para jogador 50, 2.160 XP para personagem 10 e 1.620 XP para pet 10. Foram recalculadas a partir das fórmulas, não estimadas por sensação.

Cenário ilustrativo: partida válida, vitória, cinco eliminações, duas assistências e dois objetivos; pet equipado. Não representa uma média de telemetria.

| Recompensa | Contra humanos | Mesmas ações contra bots |
|---|---:|---:|
| XP da partida | 227 | 191 |
| Moedas | 66 | 66 |
| XP do pet | 80 | 80 |

Mantendo artificialmente esse cenário em todas as partidas:

- Personagem 10: 10 partidas; pet 10: 21; jogador 50: 152.
- Todas as melhorias de uma personagem: 11.100 moedas, ou 169 partidas só com as 66 moedas de resultado.
- Retirando as cinco últimas compras sem benefício: 7.600 moedas, ou 116 partidas.
- Seis personagens com todas as compras: 66.600 moedas, ou 1.010 partidas.

Missões, conquistas e prémios de nível reduzem essas quantidades; skins, pet comprado e UPRA podem aumentá-las. Vitórias alternadas e partidas sem essas ações também mudam o resultado. A duração real é indispensável para converter isto em horas.

**Diagnóstico:** a curva de personagem termina muito antes da economia de melhorias. Pode funcionar como objetivo de longo prazo, mas a UI anuncia novos níveis e depois deixa um bloqueio económico grande. A mesma moeda serve poder e aparência, tornando uma skin uma oportunidade perdida de melhorar atributos.

### Direção proposta

1. Manter o elenco inicial disponível para LAN: facilita convidar amigos e formar equipas. Progredir em domínio e personalização em vez de bloquear o acesso ao núcleo do jogo.
2. Tratar competitivo normalizado como regra explícita e visível antes de entrar. O booleano atual desliga vantagens; não constitui ranking, matchmaking ou competição completa.
3. Para casual, reduzir a distância prática entre novato e veterano. Vida +12% e dano +8% juntos já alteram trocas; o quociente teórico 1,12 × 1,08 é cerca de 1,21 antes de pets, regeneração e outros fatores. Isto ilustra potencial, não prova uma vantagem universal de 21% em todas as armas.
4. Fazer cada compra ter benefício claro. Preferir menos degraus consistentes a cinco degraus com tetos antecipados.
5. Simular a economia com derrotas, poucos abates, jogo de objetivo, bots e cooperativo; medir partidas por melhoria e por cosmético. Só depois fixar preços novos.
6. Criar maestria por personagem com objetivos próprios: dano efetivo, controlo útil, resgates ou precisão; recompensas sobretudo visuais. Não obrigar todos a procurar headshots ou eliminações.
7. Dar um objetivo seguinte visível: próxima recompensa, progresso e ação necessária. Há intervalos de dez níveis entre alguns prémios tardios; enriquecer marcos com conteúdo reconhecível.
8. Dar utilidade efetiva aos títulos/emotes. Encontrei IDs concedidos, guardados e listados, mas não um sistema de equipar título nem uma ação de executar o emote no runtime inspecionado.
9. Variar missões. As três diárias atuais são fixas; introduzir rotação controlada, opções de objetivo/apoio e eventual substituição de uma missão. Evitar que missões incentivem abandonar o objetivo da equipa.
10. Prever o fim da progressão: maestria cosmética, estatísticas pessoais e desafios opcionais. Não encontrei um ciclo implementado de prestígio/endgame após os tetos.

### Integridade e qualidade de vida

- Reconexão/saída: resultados são enviados aos jogadores ainda presentes no fim; o estado do participante é removido ao sair. Definir o que acontece a uma interrupção de rede e evitar perda completa de uma sessão longa.
- O anti-AFK atual aceita dano positivo ou 15 metros e tempo mínimo. Depois disso, não avalia presença ativa contínua. É um filtro inicial, não prova participação durante toda a partida.
- Diárias/semanais usam UTC e relógio local; isso é simples para offline, mas manipulável. Mostrar o reset com clareza e só exigir tempo confiável se existir economia competitiva.
- A lista de partidas processadas conserva 200 IDs e a de recompensas 600 chaves. A deduplicação é limitada, não uma garantia permanente contra replay de resultados antigos. Relevância maior se vier a existir importação/backend.
- `SaveManager.Save` regista erro mas não devolve falha ao chamador; `GameExperienceContext.Save` emite `ProfileChanged` mesmo assim. Mostrar falha/repetição de gravação para que a UI não dê falsa confiança de persistência.

## Combate, equilíbrio e sensação de jogo

Não há evidência suficiente para declarar uma personagem dominante só pelos danos. Alcance, exposição, tamanho de mapa, acerto real, recarga e objetivos mudam o equilíbrio.

Prioridades de avaliação:

- Medir tempo para eliminar e número de cargas contra todas as vidas base, com e sem melhorias/pets. Comparar dano de rajada, sustentado e custo de errar.
- Testar N.Cage: uma raiz de cinco segundos pode ser muito punitiva num FPS; a imunidade posterior já existe, mas é preciso medir tempo sem controlo e opções reais de resposta.
- Testar o tiro fatal do Lince: precisa de aviso legível, custo de falhar e alternativas de posicionamento. Não concluir que um headshot letal é automaticamente desequilibrado.
- Testar veneno e zonas longas em entradas estreitas. Na explosão atual não há teste de oclusão entre centro e vítima; validar se dano através de paredes é intencional. Se não for, acrescentar regra de cobertura.
- A tolerância do host aceita intervalos menores que o cooldown local, com fatores 0,6/0,65 em vários ataques. Rever limites com testes de rajadas e jitter; não classificar como aumento ilimitado de dano, porque cargas e tiros do especial também limitam o ataque.
- O tiro é previsto visualmente e depois validado pelo host. Falta validar a experiência quando é recusado: munição aparente, feedback e correção. Não encontrei rewind/histórico de hitboxes para compensação de latência.
- Medir host versus cliente em Wi-Fi. Uma sensação boa no host não demonstra boa sensação no cliente.

Conteúdo a acrescentar com retorno alto: campo de treino com alvos e números compreensíveis, explicação breve de cada especial, indicação de origem do dano, leitura clara de raiz/veneno e feedback de contribuição no objetivo. Limitar efeitos de câmara com opção de reduzir tremor e flashes.

## Mapas e modos

- Validar cada combinação permitida de modo/mapa; mapa habilitado na build não demonstra equilíbrio em todos os modos.
- Criar uma tabela de tempos entre spawn, centro, gema e rotas laterais. Medir os dois lados; simetria visual não garante tempos equivalentes.
- No spawn PvP, a pontuação atual usa distância a outros vivos, incluindo aliados, e escolhe entre três candidatos. Acrescentar visibilidade inimiga, ocupação e reserva de posições; o caminho cooperativo já tem verificações de espaço que o geral não tem.
- Testar fronteiras, mar, buracos, lava, jump pads, degraus e locais onde gemas possam ficar inacessíveis. O retorno automático após queda deve ter regra consistente por modo, especialmente Showdown.
- Gem Rush deve recompensar recolha útil, proteção do portador e interrupção da contagem. O contador atual cresce por recolha, inclusive de gemas largadas; procurar ciclos de recolha artificial.
- Showdown precisa de regra clara para quem entra a meio de uma partida eliminatória: espectador até à próxima, em vez de possível entrada com vida em fase avançada.
- Consolidar primeiro dois modos representativos, por exemplo Deathmatch para combate e Gem Rush para equipa. Manter os restantes disponíveis conforme estabilidade, mas não medir sucesso pela quantidade de opções.
- O cooperativo atual tem uma estrutura inicial funcional. Para ganhar profundidade, acrescentar objetivos complementares, alterações de arena e tipos de ameaça com comportamentos diferentes. Aumentar apenas a quantidade/dificuldade de bots tem retorno limitado.

## Interface, onboarding, acessibilidade e áudio

Já existem HUD, marcadores de equipa, resultados, remapeamento, sensibilidade e volume principal. A documentação anterior regista trabalho de limpeza dos menus; esta auditoria não fez nova avaliação visual.

O que falta ou merece expansão:

- Primeiro arranque com controlos, objetivo do modo e especial; treino rápido sem penalizar o perfil real.
- Lobby de preparação com jogadores, mapa/modo, competitivo/casual, equipa e estado pronto. Atualmente a estrutura de fases é essencialmente Playing/Ended, com intervalos específicos.
- Opções gráficas acessíveis: qualidade, sombras, efeitos, resolução, VSync e limite de FPS. Há presets no projeto, mas o menu inspecionado apresenta perfil/controlos/gameplay/áudio e não oferece uma página gráfica.
- FOV geral, inversão do eixo vertical, escala do HUD/texto, opções de mira e alternativas ao uso exclusivo de cor para equipas.
- Volumes separados para música, efeitos e vozes; intensidade de tremor/flashes; legendas e prioridade de avisos sonoros. O menu de áudio observado tem apenas volume principal.
- Placar ajustado ao modo: gemas, objetivos e resgates; a ordenação atual é por eliminações e mortes.
- Resultados com tempo suficiente para ler e botão de continuar/revanche; o padrão genérico reinicia após oito segundos.
- Política de idioma coerente. Há uma camada de tradução legada e mensagens de runtime ainda em português; usar um catálogo se houver intenção de suportar português e inglês.
- Conferir consistência entre thumbnails de mapas, modelos das armas, personagens e ambiente. Não assumir defeito visual a partir dos nomes dos assets: precisa de captura atual.

## Desempenho e otimização

**Não foi medido um gargalo.** As observações seguintes são candidatos fundamentados no código, ordenados pelo custo provável de investigar/corrigir. Usar Development Build no hardware alvo e call stacks de `GC.Alloc`; o comportamento do Editor pode diferir da build. Referência: [Profiler Unity 6000.6](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/window).

1. **UI/GC:** o placar faz `Where/OrderBy/ThenBy/ToList` dentro do desenho IMGUI; existem strings e opções de layout recriadas. Guardar listas/estilos e atualizar dados quando mudam. Medir antes de reescrever toda a UI para outra tecnologia.
2. **Combate:** projéteis têm pooling e queries NonAlloc, o que é positivo; ataques pendentes, fusíveis, zonas, cones e contextos ainda podem alocar. Auditar picos de especiais simultâneos e só estender pooling onde a medição justificar.
3. **Física:** `Physics.SyncTransforms` no ciclo do combate e casts por projétil devem aparecer no perfil. Há buffers fixos de 16/32/48 resultados sem deteção explícita de saturação em vários caminhos: testar zonas densas para não perder colisões silenciosamente.
4. **Zonas:** `StepZone` processa um tick por frame com `if`, ao contrário do catch-up em `while` do cone. Abaixo da cadência de tick, zonas podem perder dano previsto. Corrigir com catch-up limitado e verificar dano total em diferentes frame times.
5. **Rede:** `PushState` altera o estado local por frame; a sincronização efetiva depende do tick/configuração do Netcode. Medir pacotes e bytes por cliente, em vez de assumir um pacote por frame. Avaliar limiares/quantização e prioridade de updates.
6. **Áudio:** a inicialização sintetiza fallbacks para muitos sons antes de usar o catálogo. Carregar primeiro os clips disponíveis e gerar reserva apenas quando faltar, ou pré-aquecer no carregamento. Medir o primeiro disparo/primeira entrada.
7. **Câmaras de preview:** desligar quando ocultas e limitar renderização quando não precisa de animação. `BrawlerPreview.Dispose` chama `Release` na RenderTexture, mas não destrói explicitamente o objeto; completar a gestão e testar abrir/fechar e trocar cenas repetidamente.
8. **Mapas/arte:** medir draw calls, materiais únicos, triângulos, sombras, transparência e memória por mapa. Aplicar partilha de materiais, LOD, culling e compressão a partir dos resultados; a presença de vários `new Material` não prova sozinha uma fuga de memória.
9. **Qualidade:** existem seis presets e o índice atual é 5; falta garantir um preset apropriado aos PCs fracos e expô-lo ao jogador. Não escolher o padrão final sem testar o equipamento alvo.

Metas propostas para validação, não resultados obtidos: perfil de 60 FPS em equipamento de referência com p95 de frame time até cerca de 16,7 ms; registar também p99, stalls de GC e memória. Definir outro alvo se o hardware da LAN não o suportar. Após 20 transições de mapa/partida, memória deve estabilizar, sem crescimento contínuo inexplicado.

## Testes, build e manutenção

Existe uma boa base de testes de Editor para dados, matemática, saves, progressão, pets, áudio e LAN, mais testes PlayMode de combate e áudio. A release de 18/10 documenta 130 testes Editor e dois PlayMode aprovados; são resultados históricos. Os ficheiros JSON de áudio encontrados também são históricos e cobrem apenas esse subconjunto.

O teste de integração lido usa host e bots numa plataforma acima da Arena. É útil para regras de combate e sincronização local, mas não testa um cliente remoto em outro computador, geometria dos quatro mapas ou todos os modos. Grande parte dos testes de progressão usa configurações criadas por código; o PlayMode usa Resources reais.

Acrescentar testes que encontrem falhas de produto:

- Cada melhoria comprável aumenta efetivamente o atributo; custos carregados do asset são os esperados.
- Personagem/equipamento não mudam fora dos momentos autorizados; mudança não cura em combate.
- Jogador cooperativo com contribuição de apoio é elegível para recompensa.
- Armadilhas/zonas não atravessam rondas e partidas; reset coincide no host e cliente.
- Estados respeitam proteção; slow expirado não influencia aplicação nova.
- Dano de zona tem o mesmo total a 15/30/60 FPS e sob frames longos controlados.
- Host e cliente em processos separados: ligação, dano, morte, respawn, troca de cena, desconexão e resultados.
- Gem Rush com portador a desligar-se, empate, contagem interrompida e entrada a meio.
- Build instalada noutro PC; sem Editor nem dependência de ficheiros externos da pasta do projeto.

Operação LAN a validar manualmente: dois a quatro PCs, Wi-Fi e cabo, versão incompatível, porta ocupada, firewall, host a sair, cliente a regressar e sessão de 30–60 minutos. Ausência de migração do host pode ser uma decisão aceitável para o âmbito, mas deve haver mensagem clara e recuperação da sessão/perfil.

Não encontrei uma pasta `.github` de workflows no checkout. Automatizar build/testes e identificação de versão tem retorno alto; não prova que inexista CI noutro serviço. Há avisos de APIs obsoletas nos logs consultados, sobretudo em ferramentas/testes; resolver gradualmente, sem confundir avisos com falhas de compilação.

A documentação de 18/10 que menciona apenas três mapas está desatualizada face às quatro cenas/build manifest. Consolidar um documento de versão e um procedimento reproduzível de build. Rever licenças/proveniência de fontes, modelos e áudio; HK Modular é uma pendência já registada nos próprios documentos.

## Plano recomendado de execução

| Ordem | Entrega | Critério de conclusão |
|---|---|---|
| 1 | Corrigir progressão básica e asset de custos | Todas as compras têm delta útil; cinco preços validados via Resources; perfil antigo preservado |
| 2 | Fechar regras de sessão | Troca de personagem validada no host; reset completo de ronda; proteção de spawn consistente |
| 3 | Recompensar corretamente cada modo | Coop de apoio conta; política PvE/PvP explícita; personagem/pet recebem XP pelo uso correto |
| 4 | Validar multiplayer real | Dois a quatro PCs completam partidas e reinícios sem divergência de regras/resultados |
| 5 | Medir e otimizar | Capturas CPU/GPU/GC/rede por mapa; corrigir os três custos mais relevantes medidos |
| 6 | Melhorar entrada e leitura | Tutorial curto, opções gráficas/acessibilidade, resultados claros e preparação de partida |
| 7 | Aprofundar conteúdo | IA de objetivo, maestria cosmética, títulos/emotes utilizáveis e variação cooperativa |

Não recomendo como prioridade imediata: mais moedas, battle pass, backend completo, vários personagens adicionais, novos modos ou mais mapas. Primeiro demonstrar que duas pessoas querem repetir uma sessão com o conteúdo já existente, que as recompensas fazem sentido e que o cliente tem uma experiência tão previsível como o host.

## Referências técnicas externas consultadas

- [Unity: NetworkVariables e permissões](https://docs-multiplayer.unity3d.com/netcode/current/basics/networkvariable/) — enquadramento das permissões; a avaliação concreta de confiança foi feita sobre o código do projeto.
- [Unity 6000.6: janela do Profiler](https://docs.unity.com/en-us/engine/6000.6/manual/analysis/profiler/window) — CPU/memória e call stacks de alocações para a medição proposta.

Nenhum ficheiro de gameplay foi alterado durante esta auditoria. As intervenções propostas são trabalho futuro; este documento não afirma que tenham sido implementadas ou testadas.
