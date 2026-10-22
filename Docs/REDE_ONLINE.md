# Rede online — arquitetura e resultados (22/10/2026)

## Como funciona
- **Topologia:** um jogador é o host (servidor + jogador); os outros ligam-se pelo Unity Relay (código de sala) ou pela LAN.
- **Movimento:** cada jogador simula o próprio movimento (sem atraso nos comandos) e envia snapshots 30×/s por mensagens
  **não fiáveis** (`PlayerMotor.SubmitStateRpc` → host → `RelayStateRpc`). Um pacote perdido é substituído pelo seguinte.
- **Interpolação:** os outros jogadores são desenhados atrás da última informação, entre dois snapshots reais. O atraso
  adapta-se ao jitter medido (50–200 ms). O relógio usa o pacote mais rápido do último segundo, não o relógio do Netcode.
- **Combate:** decidido no host (dano, munição, cooldown, Super). O cliente envia a intenção do ataque + a hora a que
  via os outros (`ViewTime`).
- **Compensação de latência (`LagCompensation`):** ao testar um ataque de um cliente, o host recua os outros jogadores para
  onde estavam nessa hora (histórico de 1 s por jogador) e repõe-nos logo a seguir. Aplica-se a tiros instantâneos,
  projéteis em voo, explosões com espoleta, cones e facadas. Recuo máximo: 400 ms.
- **Ping:** medido pelo jogo (ping-pong 2×/s), mostrado no HUD e no placar. O valor do transporte não é usado (fica
  errado com perda de pacotes).
- **Host lento:** o HUD avisa todos quando o host está abaixo de 30 FPS (`MatchManager.HostFps`).
- **Queda do host:** detetada em 10 s. Pedidos ao Relay repetem até 2× com internet lenta.

## Resultados medidos (Editor = cliente, build = host, mesmo PC, Relay europe-west4)
- Snapshots a ~30/s; perda de 1 pacote não trava o movimento.
- Ping ~320–480 ms (dois jogos no mesmo PC fraco; entre PCs diferentes deve ser menor).
- Teste A/B com sniper (tiro instantâneo) a apontar exatamente ao que o cliente vê, alvos em movimento, 20 disparos:
  **com compensação 16/20**, **sem compensação 7/20**. Sonda: `Tests/PlayMode/NetAimProbe.cs`.

## Testes manuais
- `CHIEFS&GEMS.exe -hostonline 3` cria uma sala com 3 bots e escreve `[Online] ROOMCODE=...` no log.
- `CHIEFS&GEMS.exe -joinroom CODIGO` entra direto numa sala.
- `-nolagcomp` (no host) desliga a compensação para comparação.
- `-screen-fullscreen 0 -screen-width 800 -screen-height 450` abre em janela (se não houver resolução guardada).

## Limites conhecidos
- O host continua com uma pequena vantagem (vê e testa no próprio PC, sem viagem).
- Acima de ~250 ms de ping o recuo chega ao limite e é preciso antecipar um pouco.
- Cada jogador decide a própria posição: adequado entre amigos; para jogo público seria preciso validar o movimento.
