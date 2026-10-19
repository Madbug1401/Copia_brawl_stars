# Integração dos sons do FPS-LAN

Foram usados os 25 ficheiros enviados em 18/10/2026, preservando os originais em GameJam/Audio. O projeto contém 36 clips preparados para 26 efeitos dos 38 originais. As pastas seguem as categorias do pacote, com nomes estáveis ligados ao enum Sound.

## Onde editar

- Clips: `Assets/_Game/Resources/Audio/Sfx/`.
- Associações, volume e variação de tom: `Assets/_Game/Resources/Audio/SfxCatalog.asset`.
- Rastreabilidade: `Tools/Audio/sound_pack_manifest.json` contém o nome original, SHA-256, intervalo extraído, ganho e caminho de cada clip.

## Tratamento

- WAV PCM16 mono a 48 kHz, com cabeçalhos válidos, sem normalização adicional do Unity.
- Importação PCM, Decompress On Load e pré-carregamento para efeitos curtos.
- Cortes de silêncio e fades curtos; níveis individuais com margem abaixo de 0 dBFS.
- Dois disparos de revólver, oito passos de relva e três trechos de rajada de minigun. A seleção evita repetir a mesma variação consecutivamente.
- Rotação da minigun extraída antes dos tiros e limitada aos 0,4 s de ativação. Rajadas de 0,29 s acompanham o intervalo de 0,3 s do especial.
- Granada: apenas o primeiro evento da sequência é usado no lançamento. A explosão posterior usa o ficheiro EXPLOSAO, acionado pelo impacto do jogo.
- O ficheiro ARMADILHA GAZ VENENO corresponde ao gás/veneno; não substitui o fecho metálico da armadilha.
- O clique do menu tem um efeito próprio, para não tocar uma recarga de arma nos botões. A animação da shotgun deixa de duplicar a recarga já emitida por PlayerCombat.
- O tiro fatal toca no disparo; a confirmação de execução usa o som de eliminação, sem repetir o estrondo da arma.
- Vozes 2D para feedback local e vozes 3D para o mundo, sem Doppler. Ao reutilizar uma voz ocupada, o som anterior é parado antes de mudar a posição.

## Mapeamento

| Original (relativo à pasta enviada) | Efeito do jogo | Clips |
|---|---|---:|
| armas/REVOLVER.wav | Shot | 2 |
| armas/SHOTGUN.wav | Shotgun | 1 |
| armas/SNIPER.wav | Sniper | 1 |
| armas/SUBMATRELHADORA.wav | Smg | 1 |
| armas/Minigun_Sound_Test_SFM.wav | MinigunSpin | 1 |
| armas/Minigun_Sound_Test_SFM.wav | Minigun | 3 |
| explosivos e armadilhas/ARMADILHA GAZ VENENO.wav | PoisonHiss | 1 |
| explosivos e armadilhas/ARMAR MINA.wav | MineArm | 1 |
| explosivos e armadilhas/EXPLOSAO.wav | Explosion | 1 |
| impactos e combates/ACERTO NO CORPO.wav | Hit | 1 |
| impactos e combates/CAIXA A PARTIR.wav | Crate | 1 |
| impactos e combates/ELIMINACAO.wav | Kill | 1 |
| impactos e combates/HEADSHOT.wav | HeadHit | 1 |
| impactos e combates/TIRO FATAL.wav | FatalShot | 1 |
| manuseamento/GATILHO SEM MUNICAO.wav | Empty | 1 |
| manuseamento/RECARGA.wav | Reload | 1 |
| outros ataques/ARCO E FLECHA.wav | Bow | 1 |
| outros ataques/GOLPE DE CORPO.wav | Melee | 1 |
| outros ataques/Grenade_Sound_Effect_Free_Fire_Whatsapp_Status_Grenade_Bomb_Sound_Effect_shorts.wav | Lob | 1 |
| outros ataques/SPRAY.wav | Spray | 1 |
| personagem/Footsteps_Grass_Sound_Effect_HD.wav | Step | 8 |
| personagem/MORTE.wav | Death | 1 |
| personagem/REACAO DOR.wav | Hurt | 1 |
| recompensa/DESBLOQUEAR.wav | Unlock | 1 |
| recompensa/RECEBER RECOMPENSA.wav | Reward | 1 |
| recompensa/SUBIR NIVEL.wav | LevelUp | 1 |

## Ainda com som provisório

SuperShot (disparo especial genérico), Launch (impulso), Respawn (reaparecer), SuperReady (especial pronto), Poof (fumo), Twirl (rodar o revólver / gema), Snare (fecho da armadilha), Victory, QuestComplete, PetHappy, PetAlert e Evolve. Não foram fornecidas gravações específicas para estes 12 efeitos. O novo UiClick também é gerado por código, intencionalmente curto.

As músicas e as falas existentes continuam nas suas pastas de Audio/Music e Audio/Voices. Os passos enviados são de relva; sons distintos para pedra, areia ou madeira ainda exigem gravações e deteção de superfície.

## Reproduzir a importação

Com Python e numpy:

```powershell
python Tools/Audio/import_sound_pack.py 'CAMINHO_DA_PASTA_ORIGINAL'
unity recompile
unity command run_script --file Tools/Audio/BuildSoundCatalog.cs --entry BuildSoundCatalog.Main
```

O segundo comando deve terminar antes do terceiro. O script do catálogo é executado no Editor via Unity Pipeline e usa Newtonsoft.Json. Os GUIDs existentes são preservados ao voltar a preparar os WAVs.

## Verificações

Os testes AudioAssetTests verificam associações, IDs serializados, importadores, duração, canais e amostras dos 36 clips. AudioPlaybackTests verifica a seleção sem repetição, o volume, o tom estável do feedback musical, a posição 3D e os sons provisórios.

Resultado em 18/10/2026: 2 testes de Editor aprovados e 1 teste de Play Mode aprovado (0 falhas). A verificação dos WAVs também confirmou cabeçalhos, níveis, fades e as 36 referências do catálogo. A avaliação auditiva final durante uma partida continua útil para afinar a mistura ao gosto da equipa.
