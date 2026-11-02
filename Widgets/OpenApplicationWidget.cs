using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Apps;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

[assembly: OpenWigiWidget(typeof(OpenApplicationWidget))]

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>
/// Open Application: abre um app com o ícone dele. Toque abre (ou traz para a frente, se já está aberto);
/// long-press fecha ou força o fechamento; uma bolinha verde mostra que o app está aberto.
/// </summary>
public sealed class OpenApplicationWidget : IWidget
{
    internal const string Website = "https://github.com/OpenWigiDash/OpenWigiDash-System-Plugin";

    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("6f1e2c0a-5b7d-4f3e-9a1c-2d8b4e6f0a17");

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "Open Application",
        Version = typeof(OpenApplicationWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = Website,
        Description = "Abre um aplicativo com o ícone dele e mostra quando ele está aberto.",
        // Botão de ícone: cabe no Mini (decisão do SDK 3.0: Mini só para botão de ícone).
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        AppMonitor.Shared.UseLogger(host.LoggerFactory);
        // Prepara a lista de apps e os ícones dela para a tela de configurações abrir sem espera.
        _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => AppCatalog.WarmAsync(), TaskScheduler.Default);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(TileRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new TileVisual(null, null, Running: false)));

    public async ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        var instance = new OpenApplicationInstance(context, AppMonitor.Shared.Watch(), new WindowsAppPlatform());
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }
}
