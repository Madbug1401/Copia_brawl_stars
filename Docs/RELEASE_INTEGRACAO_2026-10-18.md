# Versão integrada de 18/10/2026

Destino: `nico/integracao`. A publicação desta versão não altera a `main`.

Esta versão consolida as funcionalidades locais: UPRA e alimentação dos pets durante a partida, identificação SITA nas latas e novo cone visual do spray da Ms, menus em inglês, HUD e pausa simplificados, identificação das equipas, spawns separados em It Takes Two e atualização seletiva dos patrocinadores em Cidade Velha. Inclui descoberta de servidores LAN, ligação por IP e porta, 36 clips de áudio tratados e a abertura com os logos BRABUZ e Chiefs & Gems.

Os mapas incluídos são Arena, Cidade Velha e Buracona. A build Windows x64 é gerada em staging e substitui `build windows` apenas depois de compilada e verificada. A versão anterior fica guardada em `Temp`. Os executáveis permanecem fora do Git, conforme a política do repositório; `VERSAO.txt` na build identifica o commit correspondente.

## Validação

- 130 testes de Editor aprovados: regras de jogo, progressão, UPRA, spray, recursos de áudio e interpretação de endereços LAN.
- 2 testes de Play Mode aprovados: integração dos seis personagens, especiais, progressão e sincronização; reprodução, variações e fallback dos efeitos sonoros.
- Introdução: ambos os logos carregados, 1001 amostras de voo e enquadramento em 16:9, 4:3, 21:9 e 9:16 aprovados.
- O comando de disparo do teste de Minigun consulta o estado ativo no próprio frame; evita um tiro normal involuntário depois de terminar o especial, mantendo as verificações de dano e carga.

O teste de arranque local não substitui uma partida LAN entre dois computadores. A opção manual de configurar a firewall não é executada pela validação.

HK Modular continua dependente do ficheiro/licença da fonte; esta versão preserva a fonte de substituição já usada no projeto.
