using System.Globalization;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>Métrica que o System Info mostra. Não renomeie: o nome fica salvo nas configurações.</summary>
public enum MetricKind
{
    CpuPercent,
    CpuWatts,
    RamPercent,
    GpuPercent,
    GpuTemperature,
    GpuPowerPercent,
    GpuWatts,
    VramPercent,
    NetworkDown,
    NetworkUp,
    DiskActivity,
    Ping,
}

/// <summary>Grupo do menu "Monitor", como no System Vitals.</summary>
public enum MetricGroup
{
    Usage,
    Temperature,
    Power,
    Network,
    Disk,
    Ping,
}

/// <summary>Como mostrar a velocidade da rede.</summary>
public enum NetworkUnit
{
    MegabytesPerSecond,
    MegabitsPerSecond,
}

/// <summary>Ficha de uma métrica: nome no menu, título e unidade padrão e escala do gráfico.</summary>
/// <param name="DefaultMax">Máximo do gráfico; <c>null</c> = automático (pelo maior valor visível).</param>
/// <param name="NeedsDevice">Tem escolha de dispositivo (GPU, disco, placa de rede, host do ping).</param>
public sealed record MetricInfo(
    MetricKind Kind,
    MetricGroup Group,
    string Label,
    string DefaultTitle,
    string Unit,
    double DefaultMin,
    double? DefaultMax,
    bool IsPercent,
    bool NeedsDevice);

public static class MetricCatalog
{
    public static IReadOnlyList<MetricInfo> All { get; } =
    [
        new(MetricKind.CpuPercent, MetricGroup.Usage, "CPU — % de uso", "CPU", "%", 0, 100, true, false),
        new(MetricKind.RamPercent, MetricGroup.Usage, "RAM — % de uso", "RAM", "%", 0, 100, true, false),
        new(MetricKind.GpuPercent, MetricGroup.Usage, "GPU — % de uso", "GPU", "%", 0, 100, true, true),
        new(MetricKind.VramPercent, MetricGroup.Usage, "VRAM — % de uso", "VRAM", "%", 0, 100, true, true),
        new(MetricKind.GpuTemperature, MetricGroup.Temperature, "GPU — temperatura", "GPU °C", "°", 0, 120, false, true),
        new(MetricKind.CpuWatts, MetricGroup.Power, "CPU — watts (RAPL)", "CPU Watt", "W", 0, null, false, false),
        new(MetricKind.GpuPowerPercent, MetricGroup.Power, "GPU — % do limite de energia", "GPU Power", "%", 0, 100, true, true),
        new(MetricKind.GpuWatts, MetricGroup.Power, "GPU — watts (NVIDIA)", "GPU Watt", "W", 0, null, false, true),
        new(MetricKind.NetworkDown, MetricGroup.Network, "Rede — download", "LAN ↓", "", 0, null, false, true),
        new(MetricKind.NetworkUp, MetricGroup.Network, "Rede — upload", "LAN ↑", "", 0, null, false, true),
        new(MetricKind.DiskActivity, MetricGroup.Disk, "Disco — % de atividade", "DISCO", "%", 0, 100, true, true),
        new(MetricKind.Ping, MetricGroup.Ping, "Ping", "PING", "ms", 0, 60, false, true),
    ];

    public static MetricInfo Get(MetricKind kind) => All.First(m => m.Kind == kind);

    public static string GroupName(MetricGroup group) => group switch
    {
        MetricGroup.Usage => "Uso %",
        MetricGroup.Temperature => "Temperatura",
        MetricGroup.Power => "Energia",
        MetricGroup.Network => "Rede",
        MetricGroup.Disk => "Disco",
        MetricGroup.Ping => "Ping",
        _ => group.ToString(),
    };

    /// <summary>Hosts prontos do ping, como no System Vitals.</summary>
    public static IReadOnlyList<MetricDevice> PingHosts { get; } =
    [
        new("8.8.8.8", "Google (8.8.8.8)", "GOOGLE"),
        new("1.1.1.1", "Cloudflare (1.1.1.1)", "CLOUDFLARE"),
    ];

    /// <summary>Título automático, com a unidade da rede quando for o caso.</summary>
    public static string DefaultTitle(MetricKind kind, NetworkUnit unit, string? device)
    {
        var info = Get(kind);
        return kind switch
        {
            MetricKind.NetworkDown or MetricKind.NetworkUp =>
                (kind == MetricKind.NetworkDown ? "LAN ↓ " : "LAN ↑ ") + (unit == NetworkUnit.MegabitsPerSecond ? "Mbps" : "MB/s"),
            MetricKind.Ping => PingHosts.FirstOrDefault(h => h.Id == device)?.ShortName ?? "PING",
            _ => info.DefaultTitle,
        };
    }

    /// <summary>Valor já convertido para a unidade mostrada (rede chega em bytes/s).</summary>
    public static double Display(MetricKind kind, double raw, NetworkUnit unit) => kind switch
    {
        MetricKind.NetworkDown or MetricKind.NetworkUp => unit == NetworkUnit.MegabitsPerSecond
            ? raw * 8 / 1_000_000
            : raw / (1024 * 1024),
        _ => raw,
    };

    /// <summary>Texto do número, sem a unidade: inteiro, ou uma casa abaixo de 10 em rede.</summary>
    public static string Format(MetricKind kind, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "–";
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        if (kind is MetricKind.NetworkDown or MetricKind.NetworkUp && value < 10)
            return value.ToString("0.0", culture);
        return Math.Round(value).ToString("0", culture);
    }
}

/// <summary>Algo que a métrica pode acompanhar: uma GPU, um disco, uma placa de rede, um host.</summary>
/// <param name="Id">O que fica salvo (LUID da GPU, nome da instância do contador, host).</param>
/// <param name="ShortName">Nome curto para o título automático; <c>null</c> = usa o da métrica.</param>
public sealed record MetricDevice(string Id, string Name, string? ShortName = null);

/// <summary>Uma série acompanhada: a métrica e o dispositivo (<c>null</c> = o padrão: todas as placas, o disco mais ocupado, a GPU principal).</summary>
public readonly record struct MetricKey(MetricKind Kind, string? Device);
