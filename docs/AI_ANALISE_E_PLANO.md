# Coin Catcher — Análise do Estado Atual e Plano de Implementação

*Gerado a partir da leitura direta do código-fonte em `C:\SweetDayGit\Assets\Scripts` (33 scripts), do `README.md`, do `Packages/manifest.json`, do `PlayerControls.inputactions`, do briefing do projeto e do histórico salvo em memória. Data: 14/09/2026.*

## 1. Resumo executivo

O projeto está bem mais avançado do que um protótipo: networking server-authoritative com Netcode for GameObjects 2.10, lobby interativo com slots, split-screen dinâmico, suporte mobile e um sistema de moedas/backpack funcional já existem e, na leitura do código, estão implementados de forma consistente com o que o briefing descreve — com três exceções importantes detalhadas na seção 2, que valem a pena resolver antes de empilhar features novas.

O achado mais relevante desta análise é que **parte do que o briefing descreve como "já corrigido" ou "já aplicado" não corresponde ao código atual**. Isso é normal em projetos solo que evoluem rápido, mas significa que o documento de briefing precisa ser realinhado ao código — e é exatamente o que a seção 2 resolve.

## 2. Divergências entre o briefing do projeto e o código atual

Estas três divergências são o ponto de partida mais importante, porque são precisamente o tipo de coisa que causa retrabalho ou bugs reintroduzidos quando se confia apenas na documentação.

### 2.1 O refactor SOLID do GameManager não foi concluído

O briefing descreve o `GameManager` como dividido em três componentes (`GameManager.cs` orquestrador fino, `GameSpawnSystem.cs` para spawn, `GameStateSystem.cs` para estado/score). Na prática:

- `GameSpawnSystem.cs` (305 bytes) e `GameStateSystem.cs` (302 bytes) são o template padrão vazio que o Unity gera ao criar um script novo (`Start()`/`Update()` sem corpo). Não têm nenhum método, nenhuma lógica.
- `GameManager.cs` continua um único arquivo de 755 linhas contendo NetworkVariables, todos os RPCs, spawn de jogadores, countdown, pause, reset/restart completo e até reposicionamento de câmeras via ClientRpc.

Ou seja: o split nunca aconteceu de fato, só os arquivos vazios foram criados. Isso não quebra nada em runtime (os dois arquivos vazios são inofensivos), mas é uma dívida técnica que vale resolver antes de o `GameManager` crescer ainda mais com as próximas features (power-ups, traps).

### 2.2 O fix documentado de "botões do menu mortos" parece ter sido revertido

A tabela de bugs conhecidos do briefing diz: *"Botões do menu mortos ao retornar → causa: MenuManager com DontDestroyOnLoad e referências stale → fix: DontDestroyOnLoad removido."*

No código atual, `MenuManager.Awake()` ainda chama `DontDestroyOnLoad(gameObject)`. O fix documentado não está presente.

O que existe hoje, em vez disso, é um mecanismo diferente: `MenuButtonHolder.ReconnectAllButtonsInScene()`, chamado por `NetworkConnectionManager.DelayedUIResetCoroutine()` um segundo depois de qualquer `Disconnect()`, que varre a cena por botões e os re-conecta ao `MenuManager` (que sobrevive entre cenas). Isso provavelmente funciona no fluxo principal (host/cliente desconectando via `Disconnect()`), mas é um band-aid sobre o problema original, não a correção documentada — e cobre apenas os caminhos que passam por `Disconnect()`. Qualquer outro caminho de volta ao MenuScene (por exemplo, se no futuro alguém adicionar uma forma de voltar ao menu sem passar por `NetworkConnectionManager.Disconnect`) reintroduzirá o bug original.

**Recomendação:** escolher um dos dois padrões conscientemente — ou remover o `DontDestroyOnLoad` do `MenuManager` (fix original, mais simples) ou manter o `MenuManager` persistente e formalizar o `MenuButtonHolder` como o mecanismo oficial de reconexão (documentando isso no briefing e garantindo que todo caminho de retorno ao menu passe por ele). Hoje os dois coexistem sem que isso esteja documentado, o que é o pior dos dois mundos.

### 2.3 Contagem de jogadores no lobby tem duas fontes de verdade conflitantes

Em `NetworkConnectionManager`, dois mecanismos escrevem nas mesmas NetworkVariables (`totalPlayers`, `totalGuards`):

- `HandleClientConnected`/`HandleClientDisconnected` incrementam/decrementam `totalPlayers` ou `totalGuards` de acordo com o `PlayerType` real de cada cliente.
- `UpdatePlayerCounter()` — chamado em `OnNetworkSpawn`, em `DelayedLobbyUpdateCoroutine` e via `UpdatePlayerCountServerRpc` (esta última chamada pelo próprio `GameManager.SpawnAllPlayersAndStartGame`) — **sobrescreve** `totalPlayers.Value` com a contagem bruta de todos os clientes conectados (runners + catchers juntos) e força `totalGuards.Value = 0` incondicionalmente, com o comentário "detailed split shown by LobbyStateManager slots".

Como as duas escritas coexistem, qualquer chamada a `UpdatePlayerCounter()` depois de catchers terem entrado zera a contagem de catchers, afetando: o texto "Players: X/6" exibido em `LobbyManager`/`LobbySetupPanel`, e o cálculo de `TotalConnectedPlayers` usado em `ApprovalCheck` para decidir se a sessão está cheia. Isso pode permitir mais jogadores do que o limite ou exibir um contador incorreto — vale investigar com um teste de 2+ catchers entrando na sessão.

## 3. Outros pontos técnicos a corrigir (por prioridade)

1. **RPCs abertas sem validação (`RpcInvokePermission.Everyone`)** — `GameManager.AddScoreServerRpc`, `CoinSpawner.SpawnSingleCoinServerRpc` e `ButtonSpawner.NotifyServerEnteredServerRpc` aceitam ser chamadas diretamente por qualquer cliente conectado, com qualquer valor de parâmetro, sem checar se quem chamou tinha de fato o direito de fazer aquilo (ex.: `AddScoreServerRpc(9999)` daria vitória instantânea se chamado por um cliente malicioso). Para um jogo local entre amigos o risco é baixo, mas é bom ter isso mapeado caso o jogo algum dia rode com desconhecidos via lobby público.

2. **`Guard.cs` tem um caminho de interpolação de posição morto** — `_networkPosition`/`_networkRotation` são atribuídos uma única vez em `OnNetworkSpawn` e nunca mais atualizados, mas `Update()` continua fazendo `Lerp` na posição de qualquer Guard não-owner em direção a esse valor congelado, a cada frame, além de aplicar gravidade incondicionalmente para não-owners (diferente de `PlayerMovement`, que pula ambos quando `!IsOwner`). Se o prefab do Guard também tiver um `NetworkTransform` fazendo a sincronização real (padrão comum), esse código morto pode brigar com ele e causar tremulação/afundamento visual do Catcher nas telas dos outros jogadores. Vale checar o prefab no Editor e remover esse trecho morto ou de fato alimentá-lo com dados de rede.

3. **Dois caminhos de restart divergentes** — `ResetGame()` (privado, acionado por `GameManager.ResetGameServerRpc`, usado pelo botão de restart do menu de pause e pelo `LobbyManager.OnRestartGameButtonClicked`) não reseta o `ButtonManager` (zonas de botão ficam no estado em que estavam). Já `FullRestartSequence()` (acionado por `FullRestartGameServerRpc`, usado no botão de restart da tela de fim de jogo) chama `ButtonManager.FullReset()` + `RespawnInitialButtons()` corretamente. Ter dois caminhos de reinício com comportamento diferente é uma fonte fácil de bug — provavelmente vale unificar em um só (o `FullRestartSequence` é o mais completo).

4. **Comentário XML órfão em `LobbyStateManager.cs`** — há um bloco de documentação ("Re-applies the slot assignments persisted...") sem nenhum método associado, logo acima de `OnDestroy()`, indício de que um método foi removido sem limpar o comentário. Pequeno, mas vale um `Edit` rápido de faxina.

5. **Fluxo de conexão duplicado em `NetworkConnectionManager`** — `StartHostWithScene`/`StartClientWithCode` (que carregam a cena de jogo diretamente) parecem ser código legado não mais usado pela UI atual: `MenuManager` só chama `StartSessionAndGoToLobby`/`JoinSessionByCode` (o fluxo que vai para a `LobbyScene` primeiro, batendo com o fluxo documentado no briefing). Se de fato não há mais nenhum caller de `StartHostWithScene`/`StartClientWithCode`, são ~90 linhas de lógica de Relay/Lobby duplicada que dá pra remover, reduzindo a superfície de manutenção.

6. **Mistura de API antiga e nova de RPC do Netcode** — o briefing padroniza RPCs de servidor no estilo novo `[Rpc(SendTo.Server, InvokePermission = ...)]`, e é isso que `GameManager`, `LobbyStateManager` e `DuoButtonSpawner` usam. Mas `PlayerMovement` (`CollectCoinServerRpc`, `DropCoinServerRpc`, `DashServerRpc`), `Guard.AttackServerRpc` e parte do `ButtonSpawner` ainda usam os atributos antigos `[ServerRpc]`/`[ClientRpc]`. Funciona (Netcode 2.10 suporta os dois estilos), mas é uma inconsistência de convenção que vale unificar, especialmente dado que "código limpo e consistente" é algo que você valoriza.

## 4. O que já está sólido (não mexer sem necessidade)

- O padrão de cooldown `if (IsServer && Value > 0f)` fora do `if (IsOwner)` está corretamente aplicado tanto em `PlayerMovement.DashCooldownRemaining` quanto em `Guard.AttackCooldownRemaining` — o bug documentado de cooldown não resetar para clients está de fato corrigido e não regrediu.
- A coleta de moedas via `List<Coin> _nearbyCoins` + `GetNextNearbyCoin()` também está correta e corresponde ao briefing.
- `DuoButtonSpawner` é o script mais bem escrito do projeto: usa `HashSet<ulong>` para evitar contagem duplicada, tem o gate `netObj.IsOwner` correto antes de disparar RPCs de cliente para servidor, e separa claramente estado de rede de estado local de UI.
- `NetworkObjectReference` em `ProcessPlayerHitWithReferenceServerRpc` evita corretamente a race condition que o briefing menciona.
- O sistema de controles (`ControlSetupManager` + `LocalPlayerManager`) é sofisticado para um projeto solo — confirmei que os nomes dos Control Schemes no `PlayerControls.inputactions` ("Keyboard", "Gamepad") batem exatamente com a lógica de auto-detecção por nome em `FindKeyboardScheme`/`FindGamepadScheme`, então não há descompasso ali.

## 5. Outras referências verificadas para o desenvolvimento

Além do repositório GitHub e da pasta local, verifiquei o seguinte como contexto adicional:

- **`Packages/manifest.json`**: confirma Unity 6, Netcode for GameObjects **2.10.0**, Input System **1.19.0**, Unity Services Multiplayer **2.1.3** (Relay/Lobby), URP **17.4.0**, e o pacote de terceiros `com.tuatmcc.unityjoycon` (suporte a Joy-Con). Importante ter essas versões em mente ao pedir sugestões de código, porque a API do Netcode mudou bastante entre versões 1.x e 2.x.
- **`Assets/PlayerControls.inputactions`**: Action Maps `Runner` (Move/Dash/Collect/Drop), `Catcher` (Move/Attack) e `UI` (Join/Navigate/Submit/Cancel/Point/Click) — bate com o que o código espera.
- **Repositório GitHub (`MayorinBR/SweetDay`)**: não há Issues abertas nem fechadas — não está sendo usado como issue tracker. Não consegui ler o histórico de commits a partir desta sessão (acesso à API do GitHub não habilitado aqui, e a página de commits bloqueia scraping), então se quiser um retrospecto de "o que mudou recentemente" o mais direto é rodar `git log` você mesmo ou me dar acesso ao repositório.
- **Knowledge base do Project "Coin Catcher"**: só contém o próprio código sincronizado do GitHub (pasta `Assets/Scripts/`, filtrado); não há nenhum documento de design (GDD), TODO list ou spec separada. As "instruções do projeto" que você escreveu são, hoje, o único documento de design existente.
- **Pacotes de assets de terceiros na pasta `Assets/`**: `kenney`, `Palmov Island`, `Pandazole_Ultimate_Pack`, `Shawk Studios` — vale manter uma nota das licenças desses packs se em algum momento pensar em publicar o jogo (portfolio/uso pessoal geralmente é livre, mas redistribuição/venda pode ter restrições).
- **Memória de sessões anteriores**: menciona materiais de portfólio já criados (apresentação `.pptx`, diagramas, ícones, legendas) que não estão presentes nesta sessão — se for atualizar o portfólio, esses arquivos precisam ser regerados ou re-anexados.

**Sugestão prática:** como não existe um GDD/TODO separado, pode valer a pena eu criar um documento vivo dentro deste Project (spec curta de cada feature planejada: power-ups, traps, gimmicks, modos alternativos) antes de começar a implementar cada uma — isso evita que o "briefing" fique desatualizado como aconteceu com o refactor do GameManager.

## 6. Plano de implementação — próximos passos

### Fase 0 — Corrigir dívida técnica atual (antes de qualquer feature nova)

Sequência sugerida, da mais barata/isolada para a mais estrutural:

1. Corrigir a duplicidade de contagem de jogadores em `NetworkConnectionManager` (seção 2.3) — é o bug com maior chance de já estar afetando partidas com 2 catchers.
2. Unificar os dois caminhos de restart (`ResetGame` vs `FullRestartSequence`) em um só.
3. Decidir e aplicar o padrão de `MenuManager`/`DontDestroyOnLoad` (seção 2.2).
4. Verificar o prefab do Guard no Editor quanto a `NetworkTransform` e remover/corrigir a interpolação morta (seção 3.2).
5. Limpar código morto: comentário órfão em `LobbyStateManager`, e `StartHostWithScene`/`StartClientWithCode` se confirmados não utilizados.
6. Ou completar o split SOLID do `GameManager` (mover spawn para `GameSpawnSystem`, estado/score/countdown/restart para `GameStateSystem`) ou apagar os dois arquivos vazios e assumir conscientemente que o `GameManager` fica monolítico por ora — o estado atual de "arquivos fantasmas" é o pior cenário.
7. Padronizar todos os RPCs no estilo novo `[Rpc(SendTo.Server, ...)]`.

### Fase 1 — QA e validação em rede real

Isso bate com a fase "QA" que já aparece como atual no seu roadmap de portfólio. Sugestão de roteiro de teste manual, focado nos pontos mais frágeis encontrados acima:

- Partida com 2 Catchers + 4 Runners simultâneos (online, via Relay) — validar contador de jogadores e full-check do lobby.
- Um jogador desconectando no meio da partida (Runner e depois Catcher) — validar `OnClientDisconnectedDuringGame` e liberação de slot.
- Restart via botão de pause vs. restart via tela de fim de jogo — comparar se as zonas de botão voltam ao estado correto nos dois casos.
- Ida e volta Menu → Lobby → Jogo → Menu repetidas vezes seguidas — validar se os botões do menu continuam clicáveis (o ponto da seção 2.2).
- Teste em build mobile real (não só Editor) do fluxo de ataque do Catcher (`_isSwinging`) e coleta de moeda.

### Fase 2 — Power-ups (próxima feature do roadmap)

Sugestão de arquitetura, seguindo o mesmo padrão OCP que vocês já usam em `IButtonZone`:

- Interface `IPowerUp` (ou `ScriptableObject` abstrato `PowerUpDefinition`) com um método `Apply(PlayerMovement or Guard)` / `Remove(...)`, para não precisar de `if/else` crescente por tipo de power-up.
- Um spawner de power-ups reaproveitando a lógica de `CoinSpawner.TryFindSpawnPosition` (raycast + `OverlapSphere` para evitar overlap), só trocando o prefab.
- Efeitos com duração usam o mesmo padrão de `NetworkVariable<float>` + decremento `if (IsServer && Value > 0f)` que vocês já validaram para dash/attack cooldown — reaproveitar em vez de inventar um padrão novo.

### Fase 3 — Sistema de armadilhas do Catcher

- Reaproveitar o padrão de cooldown networked (igual ataque).
- Limite de 3 armadilhas ativas por Catcher sugere um `NetworkList<T>` ou até 3 `NetworkVariable` fixas (igual ao truque de 6 slots do `LobbyStateManager` para evitar o erro `CS8377`) guardando posição + id do dono.
- Visibilidade "só para Catchers" pode ser resolvida no client via um `ClientRpc` direcionado (`RpcTarget.Single`) só para os clientes Catcher, em vez de esconder por camada de renderização (mais simples de restringir).

### Fase 4 — Áudio (Audio Mixer, BGM dinâmico, SFX por ação)

- Fase relativamente isolada do resto do código de gameplay — bom momento para introduzir um `AudioManager` centralizado como singleton leve (mesmo padrão `Instance` que já é usado em `GameManager`/`UIManager`/etc.), evitando `FindAnyObjectByType` espalhado feito nos outros managers.

### Fase 5 — Modos alternativos, customização, multilíngue

- Deixar por último, pois dependem de as fases anteriores estarem estáveis (especialmente power-ups e traps, que provavelmente vão interagir com "Master Collector"/"Escape Gate").
- Multilíngue via `ScriptableObject` (já planejado) é direto de encaixar depois, sem afetar a arquitetura de rede.

## 7. Como usar este documento

Salvei uma cópia deste documento no Project "Coin Catcher" no Claude, então ele fica disponível nas próximas conversas sem precisar re-analisar o código do zero. Recomendo tratar a seção 3 (bugs) como a lista de tarefas da Fase 0, e usar a seção 6 como guia geral — mas sem se prender demais à ordem exata caso alguma prioridade sua mude.
