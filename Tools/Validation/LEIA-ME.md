# Verificação visual do Lince

Com o Unity aberto neste projeto, entrar em Play e executar:

```powershell
unity command run_script --file Tools/Validation/LinceVisualValidation.cs --entry LinceVisualValidation.Main
```

O teste cria e remove uma sessão isolada, confirma que os cinco FBX são humanoides sem malha, mede as mãos na arma ao longo da locomoção e fotografa poses, disparo, dano, morte e primeira pessoa em `Temp/lince-validation`. Não grava cenas. As mãos provisórias são removidas no fim do primeiro frame antes de capturar a primeira pessoa.
