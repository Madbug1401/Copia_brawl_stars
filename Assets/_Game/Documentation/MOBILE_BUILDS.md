# Builds mobile — FPS-LAN

## Controlos táteis

As builds para telemóvel usam orientação horizontal. Durante a partida:

- Joystick virtual à esquerda: movimento.
- Arrastar no lado direito: apontar.
- ATIRAR: ataque principal (premir e manter).
- SUPER: ativar o Super.
- SALTO e CORRER: movimento.
- PLACAR: mostrar a tabela de resultados enquanto estiver premido.
- PAUSA: trocar de Brawler, ajustar bots ou sair.

No menu principal ou no menu de pausa, abre **Definições → HUD** para
arrastar o joystick e cada botão para a posição desejada. **Repor** restaura
as posições iniciais; as alterações ficam guardadas localmente no dispositivo.

O menu de ligação adapta-se ao espaço disponível: usa colunas em ecrãs
horizontais largos e uma lista vertical com scroll em ecrãs mais estreitos.
Para jogar com amigos na mesma rede Wi-Fi, o host cria uma sala e os outros
introduzem o IP indicado no jogo.
A ligação usa UDP na porta 7777; permitir o tráfego na rede local/firewall
quando o sistema operativo o pedir.

No editor do HUD, todos os controlos aparecem sobre o painel de definições;
**Menu inicial** termina a partida ativa e regressa ao menu.
Na aba **HUD**, o tamanho dos botões pode ser ajustado entre 70% e 140%;
o joystick conserva o seu tamanho. As posições guardadas são mantidas dentro
da área segura do ecrã quando a escala muda.

## Mira e definições

As definições estão organizadas por abas. Em **Jogabilidade**, a opção
**Inverter eixo vertical** altera o sentido da mira tátil; desligada, arrastar
o dedo para cima olha para cima. A sensibilidade tátil, a sensibilidade ao
mirar, o zoom de precisão do Lince e o volume geral também podem ser ajustados.
As opções ficam guardadas no dispositivo.

O Lince tem uma mira de precisão: mantém **MIRAR** no HUD móvel ou o botão
direito do rato no PC para aproximar a visão e reduzir a sensibilidade da mira.
O botão tátil **MIRAR** é reposicionável na aba **HUD** e só aparece quando o
Lince está selecionado. Em **Jogabilidade**, escolhe entre **Manter premido**
e **Tocar para alternar**; a opção é guardada localmente.

## Controlos no PC

Abre **Definições → Teclas** no menu principal ou durante a pausa para
alterar movimento, ataque, Super, mirar, salto, corrida, placar e pausa. Clica numa
ação e prime a tecla ou botão do rato pretendido. Uma tecla não pode ser
atribuída a duas ações; **Repor teclas** restaura os atalhos padrão. Por
predefinição, o botão direito mira e E ativa o Super. A sensibilidade pode ser
ajustada na aba **Jogabilidade**.

As preferências de teclas, sensibilidade e layout do HUD são locais ao jogador
e persistem entre sessões.

## Android

O Unity 6000.6.0f1 precisa dos módulos Android Build Support, Android SDK & NDK
Tools e OpenJDK. A build de distribuição local pode ser um APK; uma publicação
na Google Play normalmente usa Android App Bundle e requer a configuração
adequada da conta de distribuição. O identificador inicial da app é
`com.fpslan.game`; antes de publicar, substitui-o pelo identificador da tua
organização e não o alteres entre atualizações.

Com os módulos instalados, executa no Windows a partir da raiz do projeto:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath (Get-Location).Path `
  -buildTarget Android `
  -executeMethod FPSLAN.EditorTools.MobileBuildSupport.BuildAndroidPlayer `
  -logFile "$env:TEMP\FPS-LAN-android-build.log"
```

O APK é criado em `build android/FPS-LAN.apk`.

### Testar diretamente num Android por USB

Para instalar e abrir a versão de desenvolvimento sem copiar o APK
manualmente, liga o telemóvel por USB, ativa **Opções de programador →
Depuração USB** e aceita a autorização apresentada no telemóvel. No Unity,
seleciona Android como plataforma ativa e executa **FPS-LAN → Build & Run
Android (USB)**. O Unity compila, instala e abre o jogo no dispositivo.

O comando `adb devices -l` deve mostrar o dispositivo como `device`; se
aparecer como `unauthorized`, confirma a autorização RSA no ecrã do telemóvel.
Este fluxo automatiza o envio, mas alterações ao código continuam a exigir
uma nova compilação. Para testar rapidamente entrada tátil sem gerar uma
build a cada iteração, também é possível configurar **Unity Remote** nas
preferências do Unity.

## iOS

O Unity gera um projeto Xcode. É necessário macOS com Xcode para compilar,
assinar e instalar a aplicação num iPhone/iPad. A build inclui a explicação
de privacidade exigida pelo iOS para acesso à rede local; a assinatura e o
perfil de provisionamento dependem da conta Apple de distribuição. O
identificador inicial é `com.fpslan.game`; para publicar, configura um
identificador registado na tua conta Apple.

O método de build gera o projeto em `build iOS/FPS-LAN-iOS`. Abre-o no Xcode
num Mac para definir assinatura e equipa antes de criar o IPA.

## Multijogador entre plataformas

Android, iOS e Windows usam o mesmo Unity Netcode/Unity Transport, modo host
por IP e porta UDP 7777. Os dispositivos têm de estar na mesma rede local;
o host suporta até quatro jogadores. É necessário testar a ligação real em
dispositivos físicos, pois regras de Wi-Fi e firewall podem variar.
