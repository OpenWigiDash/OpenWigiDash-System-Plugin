using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Audio;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

[assembly: OpenWigiWidget(typeof(AudioDeviceWidget))]

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>
/// Placa de som: troca o dispositivo padrão do Windows (saída ou entrada). Cada toque passa para o próximo de uma
/// lista (carrossel; com dois é um toggle), long-press volta para o primeiro.
/// </summary>
public sealed class AudioDeviceWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("b3d7a9e2-4c61-4f0b-8e25-7a1c9d3f5e48");

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "Audio Device",
        Version = typeof(AudioDeviceWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = OpenApplicationWidget.Website,
        Description = "Troca a placa de som padrão do Windows (saída ou entrada), passando por uma lista de dispositivos.",
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        AudioService.Shared.UseLogger(host.LoggerFactory);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(TileRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new TileVisual(null, null, false) { Glyph = AudioGlyphs.ByKey("speaker")!.Glyph, DotCount = 2, DotIndex = 1 }));

    public async ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        var instance = new AudioDeviceInstance(context, AudioService.Shared);
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }
}
