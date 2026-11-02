# Plano: widget System Info

Status: **planejamento**, nada implementado. Implementação só depois do OK do Victor.

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
| CPU % de uso | **Sim** | PDH `\Processor Information(_Total)\% Processor Utility` | É o número do Gerenciador de Tarefas (o `% Processor Time` antigo fica abaixo dele com turbo). |
| CPU Watts | **Provável, em poucos PCs** | PDH `\Energy Meter(*)\Power` (mW) e `\Power Meter(*)\Power` | Só existe quando o firmware ou o driver de energia publica o RAPL pelo EMI do Windows. Comum em notebook Intel, raro em desktop, quase nunca em AMD. Se não existir, a opção some da lista. |
| CPU Temperatura | **Não, de forma confiável** | PDH `\Thermal Zone Information(*)\Temperature` (Kelvin, sem admin) | É a zona térmica ACPI da placa-mãe, não o sensor do processador. Em muitos desktops fica parada (ex.: 27,8 °C) ou anda devagar. A temperatura real do pacote do CPU só sai do MSR (Intel) ou do SMN (AMD), que pedem driver de kernel. Proposta: oferecer "Zona térmica (ACPI)" como opção experimental, com esse nome, e só se o teste no PC mostrar um valor que se mexe. |
| RAM % de uso | **Sim** | `GlobalMemoryStatusEx` (`dwMemoryLoad`, ou total menos disponível) | Trivial. |
| GPU % de uso | **Sim** | PDH `\GPU Engine(*)\Utilization Percentage` (WDDM 2.x, Windows 10 1709+) | É o que o Gerenciador de Tarefas usa: somamos por adaptador e tipo de engine e mostramos o maior (normalmente o 3D). Funciona com NVIDIA, AMD e Intel. Não precisa de terceiros, ao contrário do que o System Vitals faz. |
| GPU Watts | **Não em watts** | `D3DKMTQueryAdapterInfo` (`KMTQAITYPE_ADAPTERPERFDATA`), campo `Power` | O Windows dá a potência em **% do limite** da placa, não em watts, e só com driver WDDM 2.9+. Dá para mostrar "GPU Power %" se o teste confirmar. Watts de verdade só pela biblioteca do driver (ver "Driver do fabricante"). |
| GPU Temperatura | **Provável** | `D3DKMTQueryAdapterInfo` (`KMTQAITYPE_ADAPTERPERFDATA`), campo `Temperature` (décimos de °C) | É a mesma fonte da temperatura da GPU no Gerenciador de Tarefas (aba Desempenho > GPU). Se o Gerenciador mostra a temperatura no seu PC, a gente consegue ler. API pública do `gdi32.dll`, sem admin. |
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

## Teste no PC antes de implementar (fase 0)

Um programa de console pequeno (ou um teste manual no plugin) que lista, no PC do Victor:

1. As instâncias de `Thermal Zone Information`, `Energy Meter` e `Power Meter` e se o valor muda com carga.
2. `D3DKMT_ADAPTER_PERFDATA` da RTX 5060 Ti: `Temperature`, `Power`, `FanRPM` com valores reais ou zero.
3. Os contadores `GPU Engine` e `GPU Adapter Memory` batendo com o Gerenciador de Tarefas.

O resultado decide quais opções entram na lista da primeira versão. Os nomes dos contadores PDH mudam com o idioma
do Windows (o PC está em português), então o código usa sempre `PdhAddEnglishCounter`, nunca o nome traduzido.

## Como o widget funciona

### Opções (tela de configuração)

- **Monitor**: a métrica, agrupada por CPU, Memória, GPU, Rede e Disco. Só aparecem as que o PC tem (as
  "Provável" somem quando a leitura não existe).
- **Dispositivo**, quando faz sentido: qual GPU (pelo nome do DXGI), qual placa de rede (ou "todas"), qual disco
  (ou "o mais ocupado").
- **Intervalo**: 0,5 s a 5 s (padrão 1 s), como o "Polling" do System Vitals.
- **Título**: automático pela métrica ("CPU", "GPU °C", "LAN Mbps"), ou texto livre.
- **Unidade**, para rede: MB/s ou Mbps.
- **Escala do gráfico**: fixa 0 a 100 nas porcentagens; automática (pelo maior valor da janela) em rede e watts;
  temperatura de 20 a 100 °C, ajustável.
- **Cores**: fundo, linha e preenchimento, com alguns temas prontos (o roxo e o verde-limão dos prints).
- **Alerta** (opcional): acima de um valor, o fundo muda de cor (ex.: CPU acima de 90%).

O título e o número com unidade em sobrescrito ficam como no System Vitals; não vamos copiar o editor de SVG
("Unit Value" com `<tspan>`), que é complicado demais para o que entrega.

### Toque

Toque: nada por padrão, ou abrir o Gerenciador de Tarefas (opção). Long-press: alternar a escala ou zerar o gráfico.
A definir com o Victor; não é o que importa no widget.

### Tamanhos

`Small` (2x2, 198x143) como padrão, que é o equivalente do botão do Stream Deck com mais espaço; `Wide` (4x2) com
o gráfico mais longo (mais histórico); `Large` (4x4) dá para depois. Sem `Mini`: o SDK recomenda não pôr número
e texto em 97x69.

### Desenho

Título em cima, valor grande no meio, gráfico de área preenchido em degradê na faixa de baixo, como nos prints.
Desenho com `System.Drawing` no `context.PixelSize`, reaproveitando o texto que encolhe até caber do
`TileRenderer`. Cada amostra nova desloca o gráfico: `PresentAsync` com o quadro inteiro e região suja só no
tile, uma vez por intervalo (no máximo 2 quadros por segundo por tile).

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
2. Primeira versão: CPU %, RAM %, GPU %, VRAM %, Rede Up/Down, Disco %, mais GPU °C e GPU Power % se a fase 0
   confirmar.
3. Depois, conforme a decisão sobre o driver do fabricante: GPU Watts pela NVML. E o que mais fizer falta da lista
   de extras.

## Decisões para o Victor

1. Aceitar ou não a `nvml.dll` do driver da NVIDIA como fonte opcional (dá GPU Watts e garante GPU °C).
2. Oferecer a "Zona térmica (ACPI)" como temperatura do CPU, com esse nome, se o teste mostrar valor útil; ou
   deixar a temperatura do CPU fora.
3. Ação do toque e do long-press.
