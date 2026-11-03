using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Execute;
using OpenWigiDash.SystemPlugin.Icons;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

[assembly: OpenWigiWidget(typeof(ExecuteWidget))]

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>
/// Execute: um botão que roda um programa (.exe, .bat, .cmd, .ps1, atalho) com argumentos e pasta de execução, ou um
/// comando inline no cmd/PowerShell. O tile mostra se deu certo (verde) ou falhou (vermelho).
/// </summary>
public sealed class ExecuteWidget : IWidget
{
    /// <summary>Não mude: é por ele que os layouts salvos acham o widget.</summary>
    public static readonly Guid WidgetId = new("0c4e8b51-7d2a-4f96-b3e1-5a9f2c7d8e63");

    private IWidgetHost? _host;

    public WidgetManifest Manifest { get; } = new()
    {
        Id = WidgetId,
        Name = "Execute",
        Version = typeof(ExecuteWidget).Assembly.GetName().Version ?? new Version(1, 0, 0),
        Author = "Victor Perez",
        Website = OpenApplicationWidget.Website,
        Description = "Roda um programa, um .bat ou um comando, com argumentos e pasta de execução, e mostra se deu certo.",
        // Botão de ícone, como o Open Application.
        SupportedSizes = [WidgetSize.Mini, WidgetSize.Small],
    };

    public ValueTask LoadAsync(IWidgetHost host, CancellationToken cancellationToken)
    {
        _host = host;
        return ValueTask.CompletedTask;
    }

    public ValueTask<WidgetFrame?> GetPreviewAsync(WidgetSize size, PixelSize pixelSize, CancellationToken cancellationToken) =>
        ValueTask.FromResult<WidgetFrame?>(TileRenderer.Render(pixelSize, _host?.Theme ?? new WidgetTheme(),
            new TileVisual(null, null, Running: false) { Glyph = IconLibrary.ByKey(ExecuteInstance.DefaultGlyph)!.Glyph }));

    public async ValueTask<IWidgetInstance> CreateInstanceAsync(IWidgetContext context, CancellationToken cancellationToken)
    {
        var instance = new ExecuteInstance(context, new ProcessExecuteRunner());
        await instance.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return instance;
    }
}
