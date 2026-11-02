using System.Text.Json.Serialization;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>Estilo do gráfico, os seis do System Vitals. Não renomeie: fica salvo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChartStyle>))]
public enum ChartStyle
{
    /// <summary>Linha com preenchimento em degradê suave (padrão).</summary>
    SoftFill,
    /// <summary>Só o preenchimento em degradê.</summary>
    Fill,
    Line,
    LinePoints,
    /// <summary>Listras inclinadas recortadas pela área do gráfico.</summary>
    Hatch,
    /// <summary>Pontos em grade dentro da área do gráfico.</summary>
    Matrix,
}

/// <summary>O que o toque faz.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TapAction>))]
public enum TapAction
{
    None,
    TaskManager,
    Custom,
}

/// <summary>Tudo o que a tela de opções do System Info salva, num JSON só.</summary>
public sealed record SystemInfoConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter<MetricKind>))]
    public MetricKind Metric { get; init; } = MetricKind.CpuPercent;

    /// <summary>GPU (LUID), disco, placa de rede ou host do ping; <c>null</c> = o padrão da métrica.</summary>
    public string? Device { get; init; }

    public int IntervalMs { get; init; } = 1000;

    [JsonConverter(typeof(JsonStringEnumConverter<NetworkUnit>))]
    public NetworkUnit NetworkUnit { get; init; } = NetworkUnit.MegabytesPerSecond;

    /// <summary>Título; <c>null</c> = automático pela métrica. Vazio = sem título.</summary>
    public string? Title { get; init; }

    /// <summary>Máximo do gráfico; <c>null</c> = o padrão da métrica (ou automático).</summary>
    public double? Max { get; init; }

    public string Background { get; init; } = SystemInfoThemes.Navy.Background;
    public string ChartColor { get; init; } = SystemInfoThemes.Navy.Chart;
    public ChartStyle Style { get; init; } = ChartStyle.SoftFill;

    /// <summary>Espessura da linha, em % da altura do tile x 10 (15 = 1,5%), como a escala de 512 px do System Vitals.</summary>
    public int LineWidth { get; init; } = 15;

    /// <summary>Opacidade do degradê em cima e embaixo, 0 a 100.</summary>
    public int OpacityTop { get; init; } = 50;
    public int OpacityBottom { get; init; }

    /// <summary>"Base ↑": o gráfico começa esta % acima do rodapé.</summary>
    public int Baseline { get; init; } = 10;

    public string TitleColor { get; init; } = "#FFFFFF";

    /// <summary>Tamanho do título em % do padrão.</summary>
    public int TitleSize { get; init; } = 100;
    public bool TitleOutline { get; init; } = true;

    public string ValueColor { get; init; } = "#FFFFFF";
    public int ValueSize { get; init; } = 100;
    public bool ValueOutline { get; init; } = true;

    /// <summary>Unidade em sobrescrito (%, W, ms); o "°" fica sempre normal.</summary>
    public bool UnitSuperscript { get; init; } = true;
    public bool ShowUnit { get; init; } = true;

    /// <summary>Acima deste valor (na unidade mostrada) o fundo vira <see cref="AlertColor"/>. <c>null</c> = sem alerta.</summary>
    public double? AlertAbove { get; init; }
    public string AlertColor { get; init; } = "#CC221E";

    public TapAction Tap { get; init; } = TapAction.TaskManager;

    /// <summary>Programa, arquivo ou endereço aberto no toque (com <see cref="TapAction.Custom"/>).</summary>
    public string? TapTarget { get; init; }

    public TimeSpan Interval => TimeSpan.FromMilliseconds(Math.Clamp(IntervalMs, 500, 10_000));

    public MetricKey Key => new(Metric, string.IsNullOrWhiteSpace(Device) ? null : Device);
}

/// <summary>Cores prontas: as do System Vitals que o Victor usa (gráfico magenta em três fundos) e a paleta dele.</summary>
public static class SystemInfoThemes
{
    public sealed record Theme(string Name, string Background, string Chart);

    public static readonly Theme Navy = new("Azul-marinho", "#12142D", "#F700E7");
    public static readonly Theme Lime = new("Verde-limão", "#ADCC00", "#F700E7");
    public static readonly Theme Blue = new("Azul", "#3C589A", "#F700E7");

    public static IReadOnlyList<Theme> All { get; } =
    [
        Navy,
        Lime,
        Blue,
        new("Preto e ciano", "#000000", "#47DEEA"),
        new("Grafite e laranja", "#2B2D31", "#FEA700"),
        new("Petróleo e verde", "#00293C", "#B4FF00"),
    ];

    /// <summary>Tema sugerido ao escolher a métrica: GPU no verde-limão, ping no azul, o resto no azul-marinho.</summary>
    public static Theme For(MetricKind kind) => kind switch
    {
        MetricKind.GpuPercent or MetricKind.GpuTemperature or MetricKind.GpuPowerPercent or MetricKind.GpuWatts
            or MetricKind.VramPercent => Lime,
        MetricKind.Ping => Blue,
        _ => Navy,
    };

    /// <summary>As 32 cores do seletor do System Vitals.</summary>
    public static IReadOnlyList<string> Palette { get; } =
    [
        "#D800FF", "#66FFFF", "#B4FF00", "#0000FF", "#CCCCFF", "#00FF00", "#FFFFFF", "#FFFD01",
        "#CC221E", "#5D535E", "#19B8EB", "#40E0D0", "#8E2462", "#F700E7", "#C6006C", "#A1D6E2",
        "#FEA700", "#3C589A", "#8497B0", "#B18F6A", "#DFCFBE", "#6B5B95", "#ADCC00", "#47DEEA",
        "#000000", "#F7D800", "#8B00F7", "#FF0000", "#00293C", "#DFE166", "#006C84", "#F5CAE6",
    ];
}
