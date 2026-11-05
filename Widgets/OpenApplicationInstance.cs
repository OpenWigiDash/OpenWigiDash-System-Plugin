using System.Drawing;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Apps;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Settings;

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>Um botão Open Application na tela.</summary>
public sealed class OpenApplicationInstance : IWidgetInstance
{
    internal const string AppKey = "app";
    internal const string TitleKey = "title";
    internal const string LongPressKey = "longPress";
    internal const string BringToFrontKey = "bringToFront";
    internal const string IconFitKey = "iconFit";
    internal const string BackgroundKey = "background";
    internal const string CustomIconKey = "customIcon";
    internal const string ArgumentsKey = "arguments";

    /// <summary>Quanto o toque espera o monitor conferir se o app está aberto antes de decidir.</summary>
    private static readonly TimeSpan RefreshWait = TimeSpan.FromMilliseconds(400);

    private readonly IWidgetContext _context;
    private readonly AppWatch _watch;
    private readonly IAppPlatform _platform;

    internal OpenApplicationInstance(IWidgetContext context, AppWatch watch, IAppPlatform platform)
    {
        _context = context;
        _watch = watch;
        _platform = platform;
        _watch.Changed += OnWatchChanged;
    }

    public AppChoice? App { get; private set; }
    public string Title { get; private set; } = "";
    public LongPressAction LongPress { get; private set; } = LongPressAction.Quit;
    public bool BringToFront { get; private set; } = true;
    public IconFit IconFit { get; private set; } = IconFit.Fill;

    /// <summary>Argumentos para o app (ex.: <c>/v:MEU-PC</c> no RDP). Vazio = abre pelo Iniciar, como antes.</summary>
    public string Arguments { get; private set; } = "";

    /// <summary>Para a tela de configurações: para onde o app aponta hoje e se está aberto.</summary>
    public AppTarget? Target => _watch.Target;
    public AppState State => _watch.State;

    /// <summary>O shell já respondeu e o app não está mais no Iniciar.</summary>
    public bool IsMissing => App is not null && _watch.Target is null && _watch.ResolvedAt != default && !_watch.ResolveRequested;

    /// <summary>Algo que a tela de configurações mostra mudou.</summary>
    public event Action? Changed;

    public Type? SettingsComponent => typeof(OpenApplicationSettings);

    internal async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = _context.Settings;
        App = await settings.GetJsonAsync<AppChoice?>(AppKey, null, cancellationToken).ConfigureAwait(false);
        Title = await settings.GetAsync(TitleKey, cancellationToken).ConfigureAwait(false) ?? "";
        Arguments = await settings.GetAsync(ArgumentsKey, cancellationToken).ConfigureAwait(false) ?? "";
        if (Enum.TryParse<LongPressAction>(await settings.GetAsync(LongPressKey, cancellationToken).ConfigureAwait(false), out var action))
            LongPress = action;
        if (bool.TryParse(await settings.GetAsync(BringToFrontKey, cancellationToken).ConfigureAwait(false), out var front))
            BringToFront = front;
        if (Enum.TryParse<IconFit>(await settings.GetAsync(IconFitKey, cancellationToken).ConfigureAwait(false), out var fit))
            IconFit = fit;
        Background = TileColor.Normalize(await settings.GetAsync(BackgroundKey, cancellationToken).ConfigureAwait(false));
        CustomIconFileId = await settings.GetAsync(CustomIconKey, cancellationToken).ConfigureAwait(false);
        await LoadCustomIconAsync(cancellationToken).ConfigureAwait(false);

        if (App is not null)
        {
            _watch.App = App;
            ReloadIcon(refresh: false);
        }
    }

    public async Task SetAppAsync(AppChoice? app)
    {
        App = app;
        _watch.App = app;
        if (app is null)
            await _context.Settings.RemoveAsync(AppKey).ConfigureAwait(false);
        else
            await _context.Settings.SetJsonAsync(AppKey, app).ConfigureAwait(false);
        if (app is not null)
            ReloadIcon(refresh: true);
        Changed?.Invoke();
        _context.RequestRender();
    }

    public async Task SetTitleAsync(string title)
    {
        Title = title ?? "";
        await _context.Settings.SetAsync(TitleKey, Title).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    public async Task SetArgumentsAsync(string arguments)
    {
        Arguments = arguments?.Trim() ?? "";
        if (Arguments.Length == 0)
            await _context.Settings.RemoveAsync(ArgumentsKey).ConfigureAwait(false);
        else
            await _context.Settings.SetAsync(ArgumentsKey, Arguments).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetLongPressAsync(LongPressAction action)
    {
        LongPress = action;
        await _context.Settings.SetAsync(LongPressKey, action.ToString()).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetBringToFrontAsync(bool value)
    {
        BringToFront = value;
        await _context.Settings.SetAsync(BringToFrontKey, value.ToString()).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task SetIconFitAsync(IconFit fit)
    {
        IconFit = fit;
        await _context.Settings.SetAsync(IconFitKey, fit.ToString()).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    /// <summary>Cor de fundo ("#RRGGBB"), ou <c>null</c> para o padrão (preto, ou a cor da placa do ícone).</summary>
    public string? Background { get; private set; }

    public async Task SetBackgroundAsync(string? hex)
    {
        Background = TileColor.Normalize(hex);
        if (Background is null)
            await _context.Settings.RemoveAsync(BackgroundKey).ConfigureAwait(false);
        else
            await _context.Settings.SetAsync(BackgroundKey, Background).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    /// <summary>Id do arquivo do ícone próprio, ou <c>null</c> para usar o ícone do app.</summary>
    public string? CustomIconFileId { get; private set; }

    /// <summary>Ícone próprio carregado (o PNG guardado nos arquivos da instância).</summary>
    public Bitmap? CustomIcon { get; private set; }

    /// <summary>O ícone próprio em PNG data URL, para a tela de configurações.</summary>
    public string? CustomIconDataUrl { get; private set; }

    /// <summary>
    /// Usa um ícone escolhido pela pessoa (imagem, .ico, .exe ou .dll) no lugar do ícone do app. O arquivo é convertido
    /// para PNG e copiado para os arquivos da instância, então pode ser apagado ou mudar de lugar depois.
    /// </summary>
    public async Task SetCustomIconAsync(string sourcePath)
    {
        var png = Path.Combine(Path.GetTempPath(), "openwigidash-icon-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await Task.Run(() => CustomIconFile.SaveAsPng(sourcePath, png)).ConfigureAwait(false);
            var previous = CustomIconFileId;
            var fileId = "icon-" + Guid.NewGuid().ToString("N");
            await _context.Files.ImportAsync(fileId, png).ConfigureAwait(false);
            CustomIconFileId = fileId;
            await LoadCustomIconAsync(default).ConfigureAwait(false);
            await _context.Settings.SetAsync(CustomIconKey, fileId).ConfigureAwait(false);
            if (previous is not null)
                await _context.Files.DeleteAsync(previous).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(png); } catch (IOException) { }
        }
        Changed?.Invoke();
        _context.RequestRender();
    }

    /// <summary>Volta a usar o ícone do app.</summary>
    public async Task ClearCustomIconAsync()
    {
        if (CustomIconFileId is not { } fileId)
            return;
        CustomIconFileId = null;
        CustomIcon = null;
        CustomIconDataUrl = null;
        await _context.Settings.RemoveAsync(CustomIconKey).ConfigureAwait(false);
        await _context.Files.DeleteAsync(fileId).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    private async Task LoadCustomIconAsync(CancellationToken cancellationToken)
    {
        if (CustomIconFileId is not { } fileId)
            return;
        try
        {
            if (await _context.Files.GetPathAsync(fileId, cancellationToken).ConfigureAwait(false) is not { } path || !File.Exists(path))
            {
                _context.Logger.LogWarning("O ícone próprio do Open Application sumiu; usando o ícone do app.");
                return;
            }
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            using var loaded = new Bitmap(stream);
            // O antigo não é descartado: um quadro em andamento pode estar desenhando com ele.
            CustomIcon = new Bitmap(loaded);
            CustomIconDataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _context.Logger.LogWarning(ex, "O ícone próprio do Open Application não pôde ser lido.");
        }
    }

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TileRenderer.Render(_context.PixelSize, _context.Theme, CurrentVisual()));

    internal TileVisual CurrentVisual()
    {
        if (App is null)
        {
            // No Mini não cabe aviso; o desenho padrão já diz que falta escolher.
            var compact = _context.PixelSize.Height < 100;
            return new TileVisual(CustomIcon, NullIfEmpty(Title), false, Detail: compact ? null : "Escolha um app")
                { Fit = IconFit, Background = TileColor.Parse(Background) };
        }
        var missing = IsMissing;
        return new TileVisual(CustomIcon ?? _platform.PeekIcon(App.Id), NullIfEmpty(Title), _watch.State.IsRunning, missing,
            missing ? "Não encontrado" : null) { Fit = IconFit, Background = TileColor.Parse(Background) };
    }

    public async ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (App is not { } app)
            return;
        try
        {
            switch (touch.Kind)
            {
                case TouchKind.Tap:
                    await RefreshStateAsync().ConfigureAwait(false);
                    var state = _watch.State;
                    if (BringToFront && state.IsRunning && _platform.BringToFront(state))
                        break;
                    _platform.Launch(app, _watch.Target, NullIfEmpty(Arguments));
                    break;

                case TouchKind.LongPress when LongPress != LongPressAction.DoNothing:
                    await RefreshStateAsync().ConfigureAwait(false);
                    if (_watch.State.IsRunning)
                        _platform.Quit(_watch.State, LongPress);
                    _ = _watch.RefreshAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            _context.Logger.LogWarning(ex, "Falha ao abrir ou fechar {App}.", app.Name);
        }
    }

    public ValueTask OnSleepAsync(CancellationToken cancellationToken)
    {
        _watch.Active = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        _watch.Active = true;
        // O app pode ter sido atualizado enquanto a página estava fora de vista.
        if (App is not null)
            ReloadIcon(refresh: true);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnRemovedAsync(CancellationToken cancellationToken)
    {
        if (CustomIconFileId is { } fileId)
            await _context.Files.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _watch.Changed -= OnWatchChanged;
        _watch.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task RefreshStateAsync()
    {
        var refresh = _watch.RefreshAsync();
        await Task.WhenAny(refresh, Task.Delay(RefreshWait)).ConfigureAwait(false);
    }

    private void OnWatchChanged(bool opened)
    {
        // Abriu (talvez depois de atualizar) ou o alvo mudou: o ícone do exe novo pode ser outro.
        if (App is not null)
            ReloadIcon(refresh: true);
        Changed?.Invoke();
        _context.RequestRender();
    }

    private void ReloadIcon(bool refresh)
    {
        if (App is not { } app)
            return;
        _ = LoadIconAsync();

        async Task LoadIconAsync()
        {
            try
            {
                var before = _platform.PeekIcon(app.Id);
                var icon = await _platform.LoadIconAsync(app.Id, refresh).ConfigureAwait(false);
                if (!ReferenceEquals(before, icon))
                {
                    Changed?.Invoke();
                    _context.RequestRender();
                }
            }
            catch (Exception ex)
            {
                _context.Logger.LogDebug(ex, "Falha ao carregar o ícone de {App}.", app.Name);
            }
        }
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
