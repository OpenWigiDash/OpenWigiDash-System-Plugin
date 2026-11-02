using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Metrics;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

[assembly: OpenWigiWidget(typeof(SystemInfoWidget))]

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>
/// System Info: uma métrica do PC (CPU, GPU, RAM, VRAM, rede, disco, ping) com o valor grande e um minigráfico em
/// tempo real, como o System Vitals do Stream Deck. Só com o que o Windows entrega, sem programa de terceiros.
/// </summary>
public sealed class SystemInfoWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("f2712413-c482-407b-9ae7-6acebea1a83e");

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "System Info",
        Version = typeof(SystemInfoWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = OpenApplicationWidget.Website,
        Description = "Uma métrica do PC (CPU, GPU, RAM, VRAM, rede, disco, ping) com minigráfico em tempo real.",
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small, WidgetSize.Wide, WidgetSize.Large],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        MetricCollector.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(SparklineRenderer.Render(pixelSize, PreviewVisual()));

    internal static SparkVisual PreviewVisual()
    {
        double?[] values = [22, 25, 31, 28, 26, 35, 48, 41, 33, 29, 27, 30, 38, 45, 40, 32, 28, 26, 24, 27, 31, 29, 26];
        return new SparkVisual(new SystemInfoConfig(), "CPU", "26", "%", values, 0, 100);
    }

    public async ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        var instance = new SystemInfoInstance(context, MetricCollector.Shared);
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }
}
