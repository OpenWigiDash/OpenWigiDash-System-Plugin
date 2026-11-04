# System para o WigiDash (OpenWigiDashManager)

Plugin de widgets do sistema para o painel WigiDash da G.SKILL, no
[OpenWigiDashManager](https://github.com/OpenWigiDash/OpenWigiDashManager) (.NET 10, SDK 3.0). Substitui o
plugin "System" do Stream Deck.

## Widgets

| Widget | O que faz |
| --- | --- |
| Open Application | Abre um app com o ícone dele. Toque abre (ou traz para a frente, se já está aberto); long-press fecha ou força o fechamento; uma bolinha verde no canto mostra que o app está aberto |
| Audio Device | Troca a placa de som padrão do Windows (saída ou entrada). Cada toque passa para o próximo de uma lista (carrossel), long-press volta para o primeiro |
| Execute | Roda um programa (.exe, .bat, .cmd, .ps1, atalho) com argumentos e pasta de execução, ou um comando inline no cmd/PowerShell. O tile fica verde quando dá certo e vermelho quando falha |
| System Info | Uma métrica do PC (CPU, GPU, RAM, VRAM, rede, disco, ping) com o valor grande e um minigráfico em tempo real, como o System Vitals do Stream Deck. Sem programa de terceiros |

Tamanhos: Mini (meia célula) e Small (o 1x1 da grade antiga). O System Info também tem Wide e Large.

Próximos: os outros do System do Stream Deck (Website, Hotkey, Text, Multimedia). Ver [docs/PLANO.md](docs/PLANO.md).

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
- **Ícone do PC**: troca o ícone do app por uma imagem (.png, .jpg, .bmp, .gif) ou pelo ícone de um .ico, .exe ou
  .dll. O arquivo é copiado para o widget, então pode ser apagado depois. "Usar o ícone do app" volta ao padrão.
  Serve para apps que só trazem ícone pequeno: o PresentMon da Intel, por exemplo, vai só até 48 px e fica borrado
  quando ampliado no tile.
- **Long press**: não fazer nada, fechar o app (como clicar no X) ou forçar o fechamento.
- **Trazer para a frente se já estiver aberto**: desligado, o toque sempre abre de novo.

## Audio Device

- **Direção**: saída ou entrada. **Papel**: padrão, comunicações ou os dois.
- **Dispositivos**: lista de 2 ou mais; cada toque vai para o próximo ligado, dando a volta. Bolinhas no topo do tile
  mostram qual está em uso.
- **Reconhecer o dispositivo**: Fuzzy (padrão) acha o headset sem fio mesmo quando o dongle USB muda de porta e o
  Windows cria outro id com nome "2- ..."; Exato usa só o id.
- **Ícone** por dispositivo: automático pelo nome, um da lista (alto-falante, fone, headset, microfone, webcam,
  monitor...) ou uma imagem sua.
- O tile acompanha quando o padrão é trocado por fora (Painel de Som, outro app).

## Execute

Botão que roda alguma coisa no PC. Não tem o contexto de um app como o Open Application, então o ícone, o título e o
fundo são escolhidos à mão.

- **Executar**: programa ou arquivo, ou comando.
  - **Programa**: caminho (com "Procurar…" ou colado; aceita `%USERPROFILE%` e aspas), **Argumentos** e **Pasta de
    execução** (vazia = a pasta do arquivo). `.exe`, `.bat` e `.cmd` rodam direto; `.ps1` roda no PowerShell; atalhos,
    pastas, links e documentos abrem pelo Windows, como um duplo clique.
  - **Comando**: no cmd (várias linhas rodam em sequência), no Windows PowerShell ou no PowerShell 7 (script inteiro,
    com várias linhas). Pasta de execução vazia = a pasta do usuário.
- **Acionar com**: toque (padrão) ou long-press. No long-press o toque não faz nada, o que evita disparar sem querer
  ações como bloquear o PC.
- **Janela**: normal, minimizada, maximizada ou oculta. Com janela, comando e `.ps1` podem **manter a janela aberta**.
- **Esperar terminar e conferir o resultado**: verde se sair com código 0, vermelho se não; enquanto roda, um relógio
  azul no canto, e o long-press encerra. Desligado, verde = abriu. Ao mudar para Comando, já vem ligado e sem janela.
- **Executar como administrador**: o Windows mostra o UAC no PC; "Não" no UAC fica vermelho.
- **Aparência**: título, ícone da lista (inclui uma categoria "Ações": terminal, código, raio, pasta...), imagem do PC
  (como no Open Application), tamanho da imagem e cor de fundo. Automático = o ícone do programa, ou terminal para
  scripts e comandos.
- **Testar**: roda dali mesmo e mostra o resultado, o código de saída e, sem janela, o fim do que o programa escreveu.

Um toque enquanto ainda está rodando é ignorado, para não abrir duas vezes.

## System Info

Um widget só, que vira CPU, GPU, rede ou disco conforme a opção **Monitor**. Tudo vem do próprio Windows (contadores
de desempenho, DXGI e o kernel gráfico), sem admin e sem HWiNFO, LibreHardwareMonitor ou parecidos.

| Métrica | De onde vem |
| --- | --- |
| CPU %, RAM %, Disco % de atividade, Rede ↓/↑ | Contadores do Windows (os mesmos do Gerenciador de Tarefas) |
| GPU %, VRAM % | Contadores `GPU Engine` e `GPU Adapter Memory` |
| GPU °C, GPU % do limite de energia | Kernel gráfico (`D3DKMT`), driver WDDM 2.9+ |
| CPU W | `Energy Meter` (RAPL), só em PCs que publicam isso para o Windows |
| GPU W | `nvml.dll` que vem com o driver NVIDIA (sem ela, a opção some) |
| Ping | Google, Cloudflare ou um host seu |

Temperatura do CPU não existe: o Windows não lê o sensor do processador sem um driver de kernel de terceiros.

Aparência como no System Vitals: seis estilos de gráfico, temas prontos (azul-marinho, verde-limão, azul), a paleta de
32 cores dele, tamanho, cor e contorno do título e do valor, unidade em sobrescrito, "Base ↑" e alerta por cor acima
de um valor. Toque abre o Gerenciador de Tarefas (ou outro programa). Fonte Rajdhani (licença OFL, em `Fonts/`).

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
