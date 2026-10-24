# Braços da 1.ª pessoa com esqueleto (FP Arms)

Cada Brawler tem agora braços próprios para a vista em 1.ª pessoa, tirados do seu modelo 3D. A manga é a da roupa
que o personagem veste. As mãos têm dedos verdadeiros com ossos.

| Brawler     | Personagem   | O que se vê no FPS                          |
|-------------|--------------|---------------------------------------------|
| gunslinger  | Ekko         | braços nus, pele do modelo                  |
| trapper     | NCage        | braços nus (arco)                           |
| artillery   | LupusMarinus | braços nus (dinamite)                       |
| sprayer     | MrS          | mangas de ganga + mãos                      |
| shotgunner  | Riot         | mangas táticas escuras + luvas              |
| sniper      | Lince        | manga do fato escuro + mão                  |

## 1. Blender: `build_fp_arms.py`

```
python3 build_fp_arms.py -- [Ekko NCage ...]     (bpy como módulo)
blender -b -P build_fp_arms.py -- [Ekko ...]      (Blender instalado)
```

O script faz isto para cada lado (R/L) de cada personagem:

1. Lê o rig SmartRig/Mixamo do FBX original (ombro, cotovelo, pulso).
2. Corta do corpo o braço e o antebraço, com a roupa. O critério é peso do braço ≥ 0,5.
3. Fecha o buraco do pulso.
4. Faz uma mão nova com palma, eminência tenar e cinco dedos de três falanges, com as proporções do modelo.
   - A textura da mão é a pele do próprio personagem.
   - No Riot a textura é a luva.
5. Cria o esqueleto: `R_UpperArm`, `R_ForeArm`, `R_Hand` e `R_{Thumb,Index,Middle,Ring,Pinky}{1..3}`, e o mesmo com `L_`.
6. Atribui os pesos:
   - braço e antebraço à mão, com transições suaves no cotovelo e no pulso;
   - dedos por "bone heat";
   - palma rígida.
7. Exporta `Assets/_Game/Art/Characters/<Nome>/FP/<Nome>_FPArms.fbx`.

Os `.blend` intermédios não vão para o repositório. O script volta a gerá-los quando é corrido.

## 2. Unity

1. Corre o menu **FPS-LAN → Personagens: braços da 1.ª pessoa com esqueleto (todos)** (`FPArmsBuilder`).
   - O menu configura a importação dos FBX: Generic, sem animação, legível.
   - Aplica o material Toon do personagem.
   - Grava `Assets/_Game/Resources/Characters/<id>_fphands.prefab`.
2. Em jogo, o `ViewModelRig` carrega esse prefab (`FPSkinnedArms.TryCreate`) e esconde as mãos estilizadas.
   - As mãos estilizadas continuam a ser os alvos invisíveis de cada pose.
   - Para desligar: `ViewModelRig.UseSkinnedArms = false`.

O `FPSkinnedArms` posiciona os braços em cada frame:

- **IK de dois ossos:** vai do ombro (fora do ecrã) ao cotovelo e ao pulso.
  - O plano de dobra vem dos cotovelos do `ViewModelRig`.
  - Se o alvo estiver longe (arco, guarda-mão da sniper), o braço alonga-se primeiro (até `MaxUpperStretch`).
  - A seguir alonga-se um pouco o antebraço (até `MaxForeStretch`).
  - Só no fim o ombro avança.
- **Dedos:** abraçam a pega da arma.
  - Cada falange é uma corda do círculo com o raio da pega mais meio dedo.
  - O raio vem do `HandPoseTag` que `StylizedHands` põe em cada mão.
- **Poses especiais:**
  - indicador no gatilho (Pistol);
  - punho fechado (Fist);
  - lata de spray (Can);
  - mão de apoio (Support).
- **Mão esquerda:** usa a mão estilizada espelhada. Os cálculos usam `TransformPoint` para respeitar o espelho.

Valores ajustáveis (estáticos em `FPSkinnedArms`): `PalmLength`, `UpperLength`, `ForeLength`, `ShoulderR/L`,
`FingerRadius`, `WrapExtra`, `MaxUpperStretch`, `MaxForeStretch`.
