# Como trabalhamos com o Git

Equipa no repositório: **Nico** (team leader, integra e valida), **Ryan** (gameplay), **Fragoso** (sistemas).
Quem é dono de cada pasta: ver [`ESTRUTURA_REPO.md`](ESTRUTURA_REPO.md).

## Branches

| Branch | De quem | Para quê |
|---|---|---|
| `main` | Nico | Versão estável. **Só o Nico faz merge para aqui**, depois de testar. |
| `ryan/gameplay` | Ryan | Player, Combat, Brawlers |
| `fragoso/sistemas` | Fragoso | GameModes, Bots, regras/gemas |
| `nico/integracao` | Nico | Core, mapa, visual, ferramentas |

Ninguém faz commit diretamente na `main` (exceto o Nico ao juntar).

## Preparar o PC (uma vez)

```bash
git clone https://github.com/Madbug1401/Copia_brawl_stars.git
cd Copia_brawl_stars
git checkout ryan/gameplay        # ou fragoso/sistemas / nico/integracao

# Merge inteligente de cenas/prefabs do Unity
git config merge.unityyamlmerge.driver "\"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge.exe\" merge -p %O %B %A %A"
```

Se já tinhas o projeto: fecha o Unity, `git checkout main`, `git pull`, depois `git checkout <a tua branch>`.

## O dia a dia (Ryan e Fragoso)

1. **Antes de começar:** fecha o Unity e traz o que já está na `main`:
   ```bash
   git fetch origin
   git merge origin/main
   ```
2. **Trabalha só na tua área** e testa na tua cena `Scenes/Sandbox/<Nome>_Test.unity`.
3. **Commits pequenos e frequentes**, com mensagem clara:
   ```bash
   git add <ficheiros>          # nunca "git add ." sem olhar para o git status
   git commit -m "Gemas: recolha e contagem por equipa"
   git push origin ryan/gameplay
   ```
4. **Quando algo estiver pronto**, avisa o Nico ("está pronto na minha branch: X").

## Juntar (Nico)

```bash
git checkout main
git pull origin main
git fetch origin
git merge --no-ff origin/ryan/gameplay      # um de cada vez
# abrir Unity → sem erros na consola → Play na Arena → (opcional) build
git push origin main
```
Depois de juntar, avisa a equipa para fazer `git merge origin/main` na sua branch.

Se o merge der conflito e não for óbvio, **não forces**: `git merge --abort` e resolvam juntos.

## Checklist antes de cada commit

- [ ] O Unity está sem erros vermelhos na consola.
- [ ] Testei na minha cena Sandbox (Play).
- [ ] `git status` só mostra ficheiros da **minha** área (+ os respetivos `.meta`).
- [ ] Não incluí `ProjectSettings/`, `Packages/` nem `Scenes/Arena.unity` (só o Nico).
- [ ] Cada ficheiro novo vai com o seu `.meta`.
- [ ] Nada de builds, vídeos, PSD ou modelos brutos (vão para o Drive).

## Quando algo corre mal

| Problema | O que fazer |
|---|---|
| Alterações em `ProjectSettings/` que não fizeste | `git checkout -- ProjectSettings/` (com o Unity fechado) |
| Conflito numa cena/prefab | Confirma que configuraste o UnityYAMLMerge; se persistir, fica com a versão de quem é dono e refaz a tua parte |
| `index.lock: File exists` | Fecha outros programas Git; apaga `.git/index.lock` |
| Unity mostra scripts em falta depois do pull | Fecha e reabre o Unity; se persistir, avisa o Nico |
