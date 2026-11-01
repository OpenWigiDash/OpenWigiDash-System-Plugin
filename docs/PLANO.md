# Plano do plugin System

Substituto do plugin "System" do Stream Deck no OpenWigiDashManager, já no SDK 3.0
(`OpenWigiDash.Sdk` 3.0.0-preview.1). Pacote no nuget.org: `OpenWigiDash.System`.

## Widgets

| Ordem | Widget | Estado |
| --- | --- | --- |
| 1 | Open Application | Primeira versão (1.0.0-preview.1) |
| 2 | Audio Device (trocar a placa de som padrão do Windows) | Feito (mesma versão 1.0.0-preview.1) |
| 3+ | Website, Hotkey, Hotkey Switch, Open, Close, Text, Multimedia | Depois, conforme o uso |

## Open Application

Igual ao do Stream Deck (prints na thread): título, app, ação do long-press e "trazer para a frente se já estiver
aberto". Tamanhos Mini e Small.

### Como acha os apps

- **Lista**: `shell:AppsFolder` (a lista "Todos os apps" do Iniciar). Traz os programas comuns, pelos atalhos do
  Iniciar, e os apps da Store/UWP juntos, com o nome que o Windows mostra.
- **O que fica salvo**: o nome do item nessa pasta (`AppChoice.Id`). É um AUMID (`Familia_hash!App` na Store,
  `com.squirrel.Discord.Discord` no Electron) ou um caminho com pasta conhecida
  (`{GUID}\Steam\steam.exe`). Nunca o caminho do exe com a versão.
- **Resolução** (`AppCatalog.Resolve`): o shell responde o AUMID e o alvo atual do atalho
  (`System.Link.TargetParsingPath` e os argumentos). Feita ao carregar, ao acordar, a cada minuto e quando o app abre.
- **Abrir**: `shell:AppsFolder\<id>` pelo shell, o mesmo que clicar no Iniciar. Serve para Store e para atalho.
- **Ícone**: `IShellItemImageFactory` no item do Iniciar (256 px, com alfa). Copia em PNG em
  `%LOCALAPPDATA%\OpenWigiDash\WidgetData\System\icons` para quando o shell não responde.

### O bug do Electron

O Stream Deck guarda o exe (`...\Discord\app-1.0.9034\Discord.exe`). O Squirrel instala a versão nova em outra
pasta e apaga a antiga: o ícone some e o botão para de abrir. Aqui:

- o id do Iniciar não muda com a versão, e o atalho aponta para `Update.exe --processStart Discord.exe`;
- para saber se está aberto, procuramos `Discord.exe` em qualquer pasta dentro da do `Update.exe`;
- quando o app abre (o fim normal de uma atualização), o alvo e o ícone são lidos de novo.

### Indicador de app aberto

Um monitor só (`AppMonitor`, numa thread STA) para todas as instâncias: a cada 1,5 s fotografa processos e janelas
uma vez e compara com cada app. Para quando todas as instâncias dormem. Casamento (`AppMatcher`):

- Store: AUMID do processo (`GetApplicationUserModelId`);
- janela que declara o AUMID do app (Electron, Chrome, UWP pelo ApplicationFrameHost);
- mesmo exe do atalho;
- Squirrel: o exe do `--processStart` dentro da pasta do `Update.exe`;
- launcher que abre outro exe na mesma pasta (EA app), menos em pastas do Windows ou direto em Program Files.

Desenho: bolinha verde com anel escuro no canto inferior direito, como no Stream Deck.

### Toque

- **Toque**: aberto e com janela, traz para a frente (restaura se minimizado; o truque do Alt libera o
  `SetForegroundWindow`). Aberto só na bandeja, ou com a opção desligada, abre de novo: apps de instância única
  (Steam, Discord) mostram a janela.
- **Long-press**: fechar manda `WM_CLOSE` às janelas; forçar mata os processos (com a árvore).

### O que só dá para testar no PC

A UI e as chamadas do shell só rodam no Windows. Roteiro:

1. `.\deploy.ps1 -RestartManager` e adicionar o Open Application numa página.
2. A lista mostra os apps do Iniciar com ícone, incluindo Xbox e outros da Store.
3. Steam, EA, Ubisoft Connect, Epic, Xbox e Discord: ícone certo, toque abre, bolinha aparece e some.
4. App aberto e minimizado: toque restaura e traz para a frente.
5. Long-press nos três modos.
6. Discord: atualizar (ou trocar a pasta `app-*`) e conferir que ícone e botão continuam.

## Audio Device (placa de som)

Substitui o "Toggle Audio Device" do Stream Deck (prints na thread), com o que faltava nele.

**Opções**, como no Stream Deck: título, direção (entrada ou saída), papel (padrão, comunicações ou os dois) e
como reconhecer o dispositivo.

**Carrossel**: em vez de só primário e secundário, uma lista de 2 ou mais dispositivos. Cada toque passa para o
próximo da lista; long-press pode voltar para o primeiro. Com dois na lista é o toggle de hoje.

**Dispositivo que muda de id** (headset sem fio quando o dongle USB troca de porta): o Windows cria outro endpoint,
com id novo e às vezes nome com prefixo ("2- JBL Quantum 800"). Guardamos o id, o nome amigável e o nome da
interface (o adaptador), e procuramos nesta ordem:

1. **Exato**: o mesmo id.
2. **Fuzzy** (padrão): mesmo nome depois de normalizar (sem o prefixo "N- ", sem diferença de maiúsculas e
   espaços), preferindo o que está ativo; se houver mais de um, o de mesma interface.
3. Sem nenhum, o tile mostra o dispositivo como ausente e pula para o próximo da lista.

A lista de dispositivos é relida quando o Windows avisa que algo mudou (`IMMNotificationClient`), então o tile
também acompanha quando o padrão é trocado por fora.

**Ícone**: o SDK não tem uma coleção de ícones como a do Stream Deck. Proposta para a primeira versão:

- ícone por dispositivo da lista, escolhido entre os do Windows (alto-falante, fone, headset, microfone, webcam,
  monitor/HDMI, da fonte Segoe Fluent Icons) ou o próprio ícone que o Windows mostra para o dispositivo;
- ou uma imagem sua, importada pelo `IWidgetFiles`.

**Técnica**: Core Audio (`IMMDeviceEnumerator`) para listar e ser avisado de mudanças, e a interface não documentada
`IPolicyConfig` para definir o padrão, a mesma que EarTrumpet, SoundSwitch e os plugins do Stream Deck usam.

Hoje esse código seria só do System: o "Set Audio Device" do plugin Discord troca o dispositivo dentro do Discord,
pelo RPC dele. Vale levar ao SDK quando um segundo plugin precisar.

## O que levar para o SDK (proposta, nada mudado ainda)

Código que já se repete ou vai se repetir entre os plugins. A sugestão é **não** pôr isto no `OpenWigi.Sdk`: ele é
o contrato, carregado uma vez pelo host e compartilhado por todos os plugins, e cada ajuste num helper obrigaria
uma versão nova do host. Melhor um pacote auxiliar à parte, que cada plugin leva na própria pasta.

| Pacote sugerido | O quê | Onde se repete hoje |
| --- | --- | --- |
| `OpenWigiDash.Sdk.Drawing` | `Bitmap` para `WidgetFrame` (Bgra32) | Discord (`ButtonRenderer.ToFrame`), Stream Deck Mirror (`Frames.FromBitmap`), System (`TileRenderer.ToFrame`) |
| | Texto com contorno que diminui até caber; fonte de ícones do Windows (Segoe Fluent ou MDL2) | Discord, System |
| | Selos do tile: indicador de "aberto/ativo" (bolinha com anel), contador, barra de nível | System (bolinha), Discord (contador, barra) |
| `OpenWigiDash.Sdk.Windows` | Lista de apps do Iniciar (`shell:AppsFolder`, inclui Store/UWP), resolução pelo id/AUMID | System; útil para um futuro "Close" e para atalhos de outros plugins |
| | Ícone de app ou arquivo com alfa (`IShellItemImageFactory` + `GetDIBits`) | System; o Stream Deck Mirror já pega ícone de janela |
| | Processos e janelas de um app (AUMID, Squirrel, Store) e "trazer para a frente" | System; o Stream Deck Mirror procura e mostra/esconde janela |
| | Thread STA para o shell | System |
| SDK (contrato) + Manager | Coleção de ícones e seletor de ícone/imagem do host, para todos os widgets (como o do Stream Deck), com a escolha salva pela instância | Placa de som, Open Application (ícone próprio no lugar do do app), Discord |
| `OpenWigiDash.Sdk.Testing` | `FakeContext`, `FakeHost`, configurações em memória | Discord (`FakeContext.cs`) e System (cópia do mesmo arquivo) |

Ordem sugerida: `Testing` e o `Bitmap` para quadro primeiro (pequenos e já duplicados), depois o pacote do Windows
quando o segundo plugin precisar dele. Antes de mexer, a decisão de pacote separado ou dentro do SDK é do Victor.

## Publicação

- Repo `OpenWigiDash/OpenWigiDash-System-Plugin`, `main` em .NET 10.
- CI (`ci.yml`): testes e `dotnet pack` em cada PR, no Windows.
- Release (`release.yml`): roda à mão na `main` com a versão (ex.: `1.0.0-preview.1`); publica no nuget.org por
  Trusted Publishing e cria a tag e a release. Falta, antes da primeira: a política de Trusted Publishing no
  nuget.org para este repo e workflow, e a variável `NUGET_USER` no repo (como nos outros plugins).
- Depois da primeira publicação: incluir `OpenWigiDash.System` no `plugins/verified.json` do Manager.
