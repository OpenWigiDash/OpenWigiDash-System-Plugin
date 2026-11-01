# System para o WigiDash (OpenWigiDashManager)

Plugin de widgets do sistema para o painel WigiDash da G.SKILL, no
[OpenWigiDashManager](https://github.com/OpenWigiDash/OpenWigiDashManager) (.NET 10, SDK 3.0). Substitui o
plugin "System" do Stream Deck.

## Widgets

| Widget | O que faz |
| --- | --- |
| Open Application | Abre um app com o ícone dele. Toque abre (ou traz para a frente, se já está aberto); long-press fecha ou força o fechamento; uma bolinha verde no canto mostra que o app está aberto |

Tamanhos: Mini (meia célula) e Small (o 1x1 da grade antiga).

Próximos: troca da placa de som do Windows e os outros do System do Stream Deck (Website, Hotkey, Text,
Multimedia). Ver [docs/PLANO.md](docs/PLANO.md).

## Open Application

A lista de apps é a mesma de "Todos os apps" do menu Iniciar (`shell:AppsFolder`): programas comuns e apps da
Microsoft Store juntos, com o ícone que o Windows mostra.

O widget guarda o **id do app no Iniciar**, não o caminho do exe. Apps Electron (Discord, Slack, etc.) instalam
cada versão numa pasta `app-x.y.z` e apagam a anterior; quem guarda esse caminho fica com o ícone quebrado depois
da atualização (o problema do plugin do Stream Deck). Aqui o Windows resolve o alvo atual toda vez: ao carregar,
ao acordar a tela, a cada minuto e sempre que o app abre.

Opções:

- **Título**: texto embaixo do ícone (vazio = só o ícone).
- **App**: busca pelo nome, com ícone.
- **Long press**: não fazer nada, fechar o app (como clicar no X) ou forçar o fechamento.
- **Trazer para a frente se já estiver aberto**: desligado, o toque sempre abre de novo.

## Requisitos

- Windows 10 ou 11.
- [OpenWigiDashManager](https://github.com/OpenWigiDash/OpenWigiDashManager) instalado pelo
  `OpenWigiDash-Setup.exe` da [release](https://github.com/OpenWigiDash/OpenWigiDashManager/releases) dele.

## Desenvolvimento

```powershell
dotnet test tests/SystemPlugin.Tests
.\deploy.ps1 -RestartManager   # publica em %LOCALAPPDATA%\OpenWigiDash\Widgets\OpenWigiDash.SystemPlugin
```

Para compilar contra o SDK do repo do Manager clonado ao lado: `-p:UseLocalOpenWigiSdk=true`.

Publicação: workflow **Release Widget** (`release.yml`) na `main`, com a versão sem o "v".
