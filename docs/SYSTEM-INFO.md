# Plano: widget System Info

Status: **primeira versão implementada** (OK do Victor em 2/11/2026). Falta o teste no PC (roteiro no fim).

Um widget só, configurável, que mostra uma métrica do PC com o valor grande e um minigráfico que se atualiza em
tempo real. Baseado no "System Vitals" do Stream Deck (prints na thread): o mesmo botão vira CPU, GPU, RAM, rede ou
disco conforme a opção "Monitor".

**Regra principal:** nada de software de terceiros (HWiNFO, LibreHardwareMonitor, AIDA64, drivers como WinRing0).
Só o que o Windows entrega para um processo comum, sem administrador.

## O que dá para ler sem terceiros

Legenda: **Sim** = API do Windows, documentada, sem admin. **Provável** = o Windows tem a API, mas depende do driver
ou do firmware; testar no PC antes de prometer. **Não** = só com driver de kernel ou programa de terceiros.

| Métrica | Dá? | De onde vem | Observações |
| --- | --- | --- | --- |
| CPU % de uso | **Sim** | PDH `\Processor Information(_Total)\% Processor Utility` | É o número do Gerenciador de Tarefas. Passa de 100 com turbo (134% no teste), então limitamos a 100 como ele faz. |
| CPU Watts | **Sim no PC do Victor** | PDH `\Energy Meter(*)\Power` (mW), instância `RAPL_Package0_PKG` | No teste (Ryzen 7 5700X): 48 W parado, 76,5 W com carga. Também há por núcleo. `_Total` fica em 0. Em outros PCs pode não existir; aí a opção some da lista. |
| CPU Temperatura | **Não** | PDH `\Thermal Zone Information(*)\Temperature` | No teste ficou parada em 290 K (16,9 °C) mesmo com 100% de CPU: é um valor fixo do ACPI. O WMI `MSAcpi_ThermalZoneTemperature` dá acesso negado sem admin. A temperatura real só sai com driver de kernel. Fica fora. |
| RAM % de uso | **Sim** | `GlobalMemoryStatusEx` (`dwMemoryLoad`, ou total menos disponível) | Trivial. |
| GPU % de uso | **Sim** | PDH `\GPU Engine(*)\Utilization Percentage` (WDDM 2.x, Windows 10 1709+) | É o que o Gerenciador de Tarefas usa: somamos por adaptador e tipo de engine e mostramos o maior (normalmente o 3D). Funciona com NVIDIA, AMD e Intel. Não precisa de terceiros, ao contrário do que o System Vitals faz. |
| GPU Watts | **Em %, sim; em watts, pela NVML** | `D3DKMTQueryAdapterInfo` (`KMTQAITYPE_ADAPTERPERFDATA`), campo `Power` (décimos de %) | O Windows dá % do limite (15,2% no teste). Watts saem da `nvml.dll` do driver NVIDIA (27,4 W no teste, limite 180 W), que bate com o %. |
| GPU Temperatura | **Sim** | `D3DKMTQueryAdapterInfo` (`KMTQAITYPE_ADAPTERPERFDATA`), campo `Temperature` (décimos de °C) | No teste: 47,4 °C, igual à NVML (diferença menor que 0,5 °C). Também dá rotação da fan e a temperatura máxima. Sem admin e sem DLL de fabricante. |
| VRAM % de uso | **Sim** | PDH `\GPU Adapter Memory(*)\Dedicated Usage` e o total por `IDXGIAdapter` (`DedicatedVideoMemory`) | Pode diferir 1 ou 2 pontos do NVML (que conta memória reservada pelo driver). |
| Rede Up | **Sim** | `GetIfTable2` (bytes enviados, por diferença) ou PDH `\Network Interface(*)\Bytes Sent/sec` | Escolha da placa de rede, ou soma das físicas ligadas. MB/s ou Mbps. |
| Rede Down | **Sim** | Igual, com bytes recebidos | Idem. |
| Disco % de atividade | **Sim** | PDH `\PhysicalDisk(*)\% Idle Time`, atividade = 100 menos ocioso | É o "Tempo ativo" do Gerenciador de Tarefas (leitura e escrita, não espaço). O `% Disk Time` passa de 100 em SSD NVMe, por isso não usamos. Por disco ou o mais ocupado. |

Extras baratos que o System Vitals tem e saem do Windows sem esforço (para depois, se quiser): ping em ms para
um host (o tile "GOOGLE 21ms", com `System.Net.NetworkInformation.Ping`), leitura e escrita do disco em MB/s,
espaço livre, uptime, frequência atual do CPU (`\Processor Information(_Total)\% Processor Performance`).

### Driver do fabricante (decisão do Victor)

O System Vitals mostra "VRAM, % Usage (NVIDIA), NVML" no print: ele lê pela `nvml.dll`, que **vem com o driver da
NVIDIA** (fica em `C:\Windows\System32`), não é programa à parte. AMD e Intel têm o equivalente no próprio driver
(ADLX e IGCL). Por ali saem GPU Watts e temperatura da GPU com certeza.

Não é "o SO que entrega", mas também não pede instalar nada. Proposta: a primeira versão usa só o Windows; se o
teste do D3DKMT não der temperatura da GPU, ou se o Victor quiser watts, entra uma fonte NVML opcional (carregada
só se a DLL existir, sem nada no pacote). Para o CPU não existe equivalente: temperatura e watts do processador
continuam fora.

### Por que não temperatura do CPU

O Windows não tem API para o sensor do processador. O WMI `MSAcpi_ThermalZoneTemperature` é a mesma zona ACPI
do contador acima, e ainda pede administrador. Todos os programas que mostram a temperatura certa (HWiNFO, Core
Temp, LibreHardwareMonitor) instalam um driver de kernel para ler o MSR; o WinRing0, o mais usado, hoje é
bloqueado pelo Defender como driver vulnerável. Fica fora por decisão de projeto.

## Resultado do teste no PC do Victor (2/11/2026)

Windows 11 26H2 (build 26300), Ryzen 7 5700X, RTX 5060 Ti 16 GB (driver 621.01). Programa de console, sem admin,
10 s parado, 16 s com os 16 threads em 100%, 10 s parado.

- **Funcionou:** CPU % (limitar a 100), CPU W pelo RAPL, RAM, GPU % (6% no 3D, igual à NVML), VRAM (3586 de
  16052 MiB = 22%), GPU °C, GPU Power %, fan, disco e rede.
- **Não funcionou:** temperatura do CPU (zona ACPI parada em 16,9 °C, WMI com acesso negado).
- **NVML:** existe e responde (47 °C, 27,4 W, 6%, 3813 MiB). Vem com o driver, não é instalação à parte.
- **Cuidado:** a RTX aparece como **dois adaptadores** no D3DKMT (por causa do SuperDisplay). Ficamos só com o que
  tem contadores `GPU Adapter Memory`. O `Win32_VideoController.AdapterRAM` para em 4 GB: não usar.
- GPU com carga não foi testada (não havia programa para isso sem instalar nada).

## Teste no PC antes de implementar (fase 0, feito)

Um programa de console pequeno (ou um teste manual no plugin) que lista, no PC do Victor:

1. As instâncias de `Thermal Zone Information`, `Energy Meter` e `Power Meter` e se o valor muda com carga.
2. `D3DKMT_ADAPTER_PERFDATA` da RTX 5060 Ti: `Temperature`, `Power`, `FanRPM` com valores reais ou zero.
3. Os contadores `GPU Engine` e `GPU Adapter Memory` batendo com o Gerenciador de Tarefas.

O resultado decide quais opções entram na lista da primeira versão. Os nomes dos contadores PDH mudam com o idioma
do Windows (o PC está em português), então o código usa sempre `PdhAddEnglishCounter`, nunca o nome traduzido.

## Como o widget funciona

### Opções (tela de configuração)

Levantadas do System Vitals instalado no PC do Victor (a tela de opções dele e os 11 botões salvos no perfil
"Game Profile"). Mantemos as mesmas opções, com campos normais no lugar de SVG.

- **Monitor**: a métrica, agrupada como no System Vitals: Uso % (CPU, GPU, RAM, VRAM), Temperatura (GPU), Energia
  (CPU W, GPU % e GPU W), Rede (MB/s, Mbps), Disco (% de atividade), Ping (Google 8.8.8.8, Cloudflare 1.1.1.1 ou
  IP próprio). Só aparecem as que o PC tem.
- **Dispositivo**, quando faz sentido: qual GPU (só os adaptadores com `GPU Adapter Memory`, porque a mesma placa
  pode aparecer duas vezes), qual placa de rede (vazio = a principal), qual disco.
- **Intervalo**: 0,5 s a 10 s (padrão 1 s). O System Vitals aceita de 0,1 s a 120 s; abaixo de 0,5 s não vale o
  custo na USB.
- **Mínimo e máximo do gráfico**: 0 a 100 fixo nas porcentagens; nas outras, máximo editável ou automático. Valores
  que o System Vitals põe ao escolher a métrica e que vamos copiar: temperatura 0 a 120, watts com o limite da placa
  quando a NVML dá (180 W na RTX 5060 Ti), ping 0 a 60 ms, rede automático.
- **Título**: preenchido pela métrica ("CPU", "GPU °C", "CPU Watt", "LAN MB/s", "GOOGLE") e editável. Tamanho, cor
  (padrão branco) e contorno.
- **Valor**: tamanho, cor (padrão branco), contorno e unidade (%, °, W, ms ou nenhuma), com opção de sobrescrito.
  O contorno, na cor do fundo, separa o número do gráfico.
- **Estilo do gráfico**, os seis do System Vitals:
  - Linha suave com preenchimento (padrão; o que o Victor usa): linha + degradê de 50% a 0% de opacidade.
  - Preenchimento: só o degradê (100% a 0%).
  - Linha.
  - Linha com pontos.
  - Hachurado: listras inclinadas (espessura, espaço, ângulo) recortadas pela área.
  - Matriz: pontos em grade dentro da área (densidade, tamanho, opacidade).
  - Ajustes: espessura da linha, opacidade em cima e embaixo, e "Base ↑" (o gráfico começa X% acima do rodapé,
    padrão 10%).
- **Cores**: fundo e gráfico, com a paleta de 32 cores do System Vitals mais seletor livre. Padrões e o que o Victor
  usa hoje:

  | Uso | Fundo | Gráfico |
  | --- | --- | --- |
  | CPU, RAM, rede (padrão do System Vitals) | `#12142D` (azul-marinho) | `#F700E7` (magenta) |
  | GPU, VRAM | `#ADCC00` (verde-limão) | `#F700E7` |
  | Ping | `#3C589A` (azul) | `#F700E7` |

  Paleta: D800FF 66FFFF B4FF00 0000FF CCCCFF 00FF00 FFFFFF FFFD01 CC221E 5D535E 19B8EB 40E0D0 8E2462 F700E7
  C6006C A1D6E2 FEA700 3C589A 8497B0 B18F6A DFCFBE 6B5B95 ADCC00 47DEEA 000000 F7D800 8B00F7 FF0000 00293C DFE166
  006C84 F5CAE6.
- **Imagem de fundo** (opcional): uma imagem sua atrás do gráfico, pelo `IWidgetFiles`.
- **Alerta** (nosso, o System Vitals não tem): acima de um valor, o fundo muda de cor.

O que não copiamos: o campo de unidade em SVG cru (`<tspan ...>`), o envio de telemetria que o System Vitals faz ao
abrir, o log em `C:\temp` e a leitura de disco por `IOCTL_DISK_PERFORMANCE` (pede admin; usamos o contador do
Windows).

### Toque

Toque: abre um programa, padrão o Gerenciador de Tarefas (`taskmgr.exe`), como no System Vitals. Long-press: alternar a escala ou zerar o gráfico.
A definir com o Victor; não é o que importa no widget.

### Tamanhos

`Small` (2x2, 198x143) como padrão, que é o equivalente do botão do Stream Deck com mais espaço; `Wide` (4x2) com
o gráfico mais longo (mais histórico); `Large` (4x4). `Mini` (1x1, 97x69) também, a pedido do Victor, mesmo o SDK
recomendando não pôr número e texto nesse tamanho: o desenho é proporcional à altura e o texto encolhe até caber.

### Desenho

Como o System Vitals: título em cima (a 20% da altura), valor grande no centro, gráfico ocupando o tile inteiro por
trás, com a curva suavizada (Bézier) e a base um pouco acima do rodapé. Fonte **Rajdhani** (a mesma dele; licença
OFL, pode ir dentro do plugin), em negrito, com contorno. Desenho com `System.Drawing` (antialiasing, degradê,
recorte para o hachurado) no `context.PixelSize`. O tile do WigiDash não é quadrado (Small 198x143), então os
tamanhos de fonte são relativos à altura, não os px do 512x512 dele.

Cada amostra nova desloca o gráfico: `PresentAsync` com o quadro inteiro e região suja só no tile, uma vez por
intervalo.

### Coleta

- Um **coletor compartilhado** para todas as instâncias (como o `AppMonitor` do Open Application): cada fonte
  (PDH, D3DKMT, `GetIfTable2`, memória) é lida uma vez por ciclo, mesmo com dez tiles na tela.
- O ciclo usa o menor intervalo pedido pelas instâncias acordadas e **para quando todas dormem** (tela apagada,
  página escondida), para não gastar CPU à toa.
- **Histórico por métrica** num buffer circular (uns 5 minutos), no coletor e não no tile: trocar de página e
  voltar mostra o gráfico já cheio.
- Instâncias de contador que mudam sozinhas (`GPU Engine` cria uma por processo) são reexpandidas a cada poucos
  segundos.
- As fontes ficam atrás de uma interface (`IMetricSource`), para testar o widget e o desenho com valores falsos na
  CI, que roda sem GPU.

### Arquivos previstos

| Arquivo | O quê |
| --- | --- |
| `Widgets/SystemInfoWidget.cs`, `Widgets/SystemInfoInstance.cs` | Widget e instância, no padrão dos outros dois |
| `Settings/SystemInfoSettings.razor` | Tela de opções |
| `Metrics/MetricCatalog.cs` | Lista de métricas, unidade, escala e de onde vem cada uma |
| `Metrics/MetricCollector.cs` | Coletor compartilhado, ciclo e histórico |
| `Metrics/Pdh.cs`, `Metrics/D3dkmt.cs`, `Metrics/NetStats.cs` | P/Invoke das fontes do Windows |
| `Rendering/SparklineRenderer.cs` | Tile com título, valor e gráfico |
| `tests/SystemPlugin.Tests/SystemInfoTests.cs` | Escala, formatação, histórico, desenho em todos os tamanhos |

Sem pacote NuGet novo: PDH, D3DKMT e IP Helper são P/Invoke direto nas DLLs do Windows.

## Ordem sugerida

1. Fase 0: o teste no PC (acima).
2. Primeira versão: CPU %, CPU W (onde existir RAPL), RAM %, GPU %, GPU °C, GPU Power %, VRAM %, Rede Up/Down,
   Disco %.
3. Depois, conforme a decisão sobre o driver do fabricante: GPU Watts pela NVML. E o que mais fizer falta da lista
   de extras.

## Decisões para o Victor

1. Aceitar ou não a `nvml.dll` do driver da NVIDIA como fonte opcional, só para GPU Watts (a temperatura da GPU
   já sai do Windows). Ela vem com o driver; não se embute no plugin.
2. Temperatura do CPU: o teste mostrou que não há fonte sem admin e driver de kernel. Proposta: fica fora.
3. Ação do toque e do long-press.

## Teste no PC (primeira versão)

1. `.\deploy.ps1 -RestartManager` e adicionar o System Info numa página (Small).
2. Monitor: as opções que o PC não tem aparecem como "não disponível" (temperatura do CPU nem está na lista).
3. Conferir com o Gerenciador de Tarefas: CPU %, RAM %, GPU % (3D), VRAM, GPU °C, disco e rede.
4. GPU W (NVML) e CPU W (RAPL) com um jogo ou carga: os valores se mexem.
5. Os seis estilos de gráfico, os temas prontos e o alerta; prévia na tela de opções acompanha.
6. Trocar de página e voltar: o gráfico continua cheio. Tela em repouso: o uso de CPU do Manager cai (coletor para).
7. Toque abre o Gerenciador de Tarefas.
