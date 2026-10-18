# Buracona no terceiro mapa do menu

## Integração

A branch mapa/buracona (ef39821) foi integrada por cima da Cidade Velha e do trabalho local preservado. O terceiro cartão mantém a chave sal, as coordenadas e a imagem já reservadas no menu, passando a carregar Scenes/Buracona.unity. A cena entra nas Build Settings depois de CidadeVelha.

GEM GRAB funciona em Santiago e no Sal. Pradaria mantém a Arena. Mapas ainda indisponíveis são recusados pelo gestor de ligação. Os construtores partilhados de Cidade Velha e Buracona respeitam o modo escolhido no menu; o valor serializado de GemGrab foi corrigido para 5, preservando ItTakesTwo = 4.

Foram mantidas as alterações da branch ao emissor aquático, água animada, seleção de locais acessíveis para largar gemas e recuperação de quedas abaixo de -1,6 m na Buracona. Os ajustes locais do Lince anteriores ao merge foram guardados em 4fc2852. Outras alterações locais em curso ficaram fora deste merge.

## Verificações realizadas em 2026-10-18

- Compilação pelo Unity 6000.6.0f1 concluída sem erros.
- Partida iniciada pelo método usado no botão Jogar: cena Buracona, host ativo, modo GemGrab, 4 jogadores (humano + 3 bots), 3 gemas observadas, 8 pontos de aterragem, um NetworkManager e dois componentes de água animada.
- 363 GUIDs de metadados em Assets/_Game verificados sem duplicação.
- Referências externas da cena correspondem aos componentes do pacote Netcode: NetworkManager, UnityTransport e NetworkObject.

A sessão de Play foi reiniciada após a recompilação para evitar estado transitório. Testes adicionais de retorno de queda, transições entre os três mapas e LAN dependem de uma sessão estável do Editor; há trabalho simultâneo noutro fluxo do projeto.
