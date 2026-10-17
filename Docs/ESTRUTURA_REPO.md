# Estrutura do repositório e responsáveis

> Regra de ouro: **cada pessoa só altera a sua área e a sua cena de teste.**
> Precisas de mexer na área de outro? Avisa essa pessoa primeiro (ou pede-lhe a alteração).

## Quem é dono de quê

| Área | Pastas | Responsável |
|---|---|---|
| Base partilhada | `Scripts/Core/`, `Scripts/Multiplayer/` | **Nico** (alterações só com aviso à equipa) |
| Mapa, visual e ferramentas | `Scripts/World/`, `Scripts/Visual/`, `Editor/`, `Shaders/`, `Materials/` | **Nico** |
| Gameplay | `Scripts/Player/`, `Scripts/Combat/`, `Scripts/Brawlers/` | **Ryan** |
| Sistemas e regras | `Scripts/GameModes/`, `Scripts/Bots/` | **Fragoso** |
| Interface | `Scripts/UI/` | *a definir* |
| Cena principal | `Scenes/Arena.unity` | **Nico** (integração — mais ninguém grava esta cena) |
| Cenas de teste | `Scenes/Sandbox/<Nome>_Test.unity` | cada um a sua |
| Configuração do projeto | `ProjectSettings/`, `Packages/` | **Nico** (nunca fazer commit destas pastas sem combinar) |

## Árvore

```
Assets/_Game/
  Art/           Characters/<Personagem> · Environment/<Ilha> · Weapons · UI   (criar quando chegar o 1.º ficheiro)
  Animations/    Audio/ (Music · SFX · Voices)    Prefabs/ (Characters · Weapons · Environment)
  Materials/     Shaders/
  Scripts/
    Core/ Multiplayer/ World/ Visual/   ← Nico
    Player/ Combat/ Brawlers/           ← Ryan
    GameModes/ Bots/                    ← Fragoso
    UI/                                 ← a definir
  Scenes/
    Arena.unity                         ← Nico
    Sandbox/ Ryan_Test · Fragoso_Test · Nico_Test
  Data/          ScriptableObjects (dados dos Brawlers, testes)
  Resources/     só o que o código carrega em runtime (vai TODO para a build)
  Editor/        ferramentas do editor (geração da Arena, arbustos, build)
  Bootstrap/     instalação automática de pacotes
  Documentation/
Docs/            documentação da equipa (este ficheiro, arquitetura, referência)
```

**Fora do Git (Drive da equipa):** builds, PNG de origem, modelos 3D brutos gerados por IA, vídeos, ficheiros PSD.

## Regras de pastas e assets

1. **Não criar pastas vazias.** O Unity gera um `.meta` para cada pasta; vazia, o Git não a guarda e os outros recebem `.meta` órfãos. Cria a pasta junto com o primeiro ficheiro.
2. **Mover/renomear ficheiros só dentro do Unity** (Project window), nunca no Explorador do Windows — o `.meta` tem de ir junto, senão as referências partem.
3. **Commit sempre do ficheiro + o seu `.meta`.**
4. **Modelos 3D gerados a partir de PNG:** só entra no repositório a versão otimizada (FBX, ≤ 30k triângulos por personagem, ≤ 5k por objeto, texturas ≤ 2048), já como prefab em `Prefabs/`. O original fica no Drive.
5. **Nomes originais.** Nada de nomes, personagens ou arte de outros jogos.
6. **Cenas de teste (`Sandbox/`) são cópias da Arena** para cada um testar sem tocar na cena principal. Para pôr algo na Arena, faz um prefab e pede ao Nico para o integrar.
