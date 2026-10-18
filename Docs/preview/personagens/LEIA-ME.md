# Modelos dos personagens

Integração dos cinco FBX entregues em 18/10/2026. Os originais permanecem na pasta GameJam; as cópias do projeto conservam o mesmo conteúdo.

| Ficheiro entregue | Personagem | ID existente | Armamento atual |
|---|---|---|---|
| ekko character.fbx | Ekko | gunslinger | Duas SMG |
| lupus marinus character.fbx | Lupus Marinus | artillery | Dois explosivos |
| ms character.fbx | Mr.S | sprayer | Duas latas de spray |
| n cage character.fbx | N.Cage | trapper | Arco |
| riot character.fbx | Riot | shotgunner | Caçadeira |

Cada personagem tem o seu avatar humanoide, material com a textura incorporada no FBX, prefab em Resources/Characters e malha de braços própria para primeira pessoa. Os IDs e a ordem do registo de rede foram mantidos. As texturas são importadas como Texture2D, com limite de 2048, e cada modelo tem aproximadamente 20 mil triângulos.

Como estes FBX não incluem animações específicas de combate, os controladores usam os clips humanoides neutros já existentes de parado, andar, correr, saltar e morrer. O ataque e o dano mantêm os efeitos e recuos do estilo do personagem. A pose Rifle e a sniper do Lince não são impostas aos outros modelos.

Reconstruir os cinco no Unity: FPS-LAN → Personagens: montar os cinco modelos do elenco. O menu individual do Mr.S também usa o modelo entregue. O Lince não é reconstruído por esta operação.

Verificação em Play: Tools/Validation/RosterVisualValidation.cs, método RosterVisualValidation.Main, através do comando run_script da ferramenta local do Unity. A sessão é temporária e não grava cenas. O teste verifica os avatares, texturas, escala, armas duplas, alvos das mãos, locomoção, salto, ataque, dano, morte, reaparecimento e primeira pessoa em repouso, disparo e sprint. As capturas completas ficam em Temp/roster-validation. Ver validacao.txt e elenco.png para a última execução.
