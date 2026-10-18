# Introdução

A abertura do menu novo (`GameUI.UseNewMenu`) toca uma vez por execução:

1. Splash nativo do Unity (mantido nas Player Settings).
2. Logo da equipa, com entrada e saída suaves (3,2 s).
3. Mapa mundial completo (1,1 s), seguido de três descidas aéreas até Cabo Verde (6,2 s), com duas pausas de estabilização, desfoque radial e nuvens.
4. Foco em Cabo Verde (0,7 s) e transição por nuvens para o fundo do menu (1,2 s).

O relógio aguarda `SplashScreen.isFinished` na build. No Editor, a sequência começa ao entrar em Play. Usa tempo não escalado e não se repete ao regressar de uma partida ou mudar de cena. Espaço, Enter, Esc e o botão PULAR passam diretamente ao menu, consumindo o evento para não ativar botões por acidente. Uma sessão de rede já iniciada passa diretamente ao menu.

## Logo da equipa

Inclui um logo provisório **ATLANTIC STUDIO**, criado com formas vetoriais pelo script `Tools/Build-IntroTeamLogo.ps1`. Substituir pelo logo verdadeiro em `Assets/_Game/Resources/UI/Menu/team_logo.png`, de preferência com transparência e margem livre em redor. Também pode ser atribuído ao campo **Intro Team Logo** do GameUI no Inspector. A proporção é preservada. Enquanto o ficheiro estiver ausente, essa etapa é omitida e a Console informa o caminho esperado.

## Mapa mundial

`bg_world.png` é equiretangular (4096 × 2048). `bg_cape_verde.png` cobre longitude −31° a −14° e latitude 9° a 24° em maior detalhe; a transição mantém as mesmas coordenadas geográficas. O centro de Cabo Verde é longitude −24°, latitude 16°, convertido para UV com origem em baixo à esquerda. A curva de aceleração, o zoom logarítmico, o recorte UV, os anéis e as nuvens são partilhados com a seleção de mapas. A vista inicial ajusta-se ao ecrã para mostrar o mundo inteiro, mesmo em 4:3 ou em formato vertical.

Dados cartográficos: [Natural Earth, land 1:50m](https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_50m_land.geojson), em [domínio público](https://www.naturalearthdata.com/about/terms-of-use/). A build inclui a imagem, sem downloads ou dependências cartográficas em runtime.

Para regenerar a imagem, na raiz do projeto:

```powershell
./Tools/Build-IntroWorldMap.ps1
./Tools/Build-IntroWorldMap.ps1 -Regional
```

O script guarda o GeoJSON em `Temp/intro-map`. Também aceita `-GeoJsonPath` para trabalhar com um ficheiro local.

## Verificação no Unity

Para verificar recursos, trajetória, pausas e enquadramento usando as texturas e funções reais do jogo (não exige Play):

```powershell
unity command run_script --file Tools/Validation/IntroValidation.cs --entry IntroValidation.Main
```

Entrar em Play na Arena; verificar a ordem da sequência e a entrada dos botões apenas no fim. Repetir em 16:9, 4:3 e formato vertical. Testar cada atalho de PULAR e o botão. Iniciar e terminar uma partida: a abertura não deve repetir. Numa build, verificar que o logo da equipa só aparece depois do splash Unity.
