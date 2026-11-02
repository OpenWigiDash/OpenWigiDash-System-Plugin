using System.Drawing;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Apps;
using OpenWigiDash.SystemPlugin.Execute;
using OpenWigiDash.SystemPlugin.Icons;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Settings;

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>Um botão Execute na tela.</summary>
public sealed class ExecuteInstance : IWidgetInstance
{
    internal const string ActionKey = "action";
    internal const string TitleKey = "title";
    internal const string GlyphKey = "glyph";
    internal const string IconFitKey = "iconFit";
    internal const string BackgroundKey = "background";
    internal const string CustomIconKey = "customIcon";

    /// <summary>Ícone quando não há programa com ícone próprio.</summary>
    internal const string DefaultGlyph = "terminal";

    /// <summary>Quanto tempo o verde fica no tile depois de dar certo.</summary>
    internal static TimeSpan SuccessFeedback { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>O vermelho fica mais tempo: é o que a pessoa precisa notar.</summary>
    internal static TimeSpan FailureFeedback { get; set; } = TimeSpan.FromSeconds(5);

    private static readonly string[] ScriptExtensions = [".bat", ".cmd", ".ps1"];

    private readonly IWidgetContext _context;
    private readonly IExecuteRunner _runner;
    private readonly Func<string, Task<Bitmap?>> _loadFileIcon;
    private readonly object _gate = new();
    private CancellationTokenSource? _running;
    private CancellationTokenSource? _feedback;
    private int _iconVersion;

    internal ExecuteInstance(IWidgetContext context, IExecuteRunner runner, Func<string, Task<Bitmap?>>? loadFileIcon = null)
    {
        _context = context;
        _runner = runner;
        _loadFileIcon = loadFileIcon ?? (path => AppIcons.LoadFileIconAsync(path));
    }

    public ExecuteAction Action { get; private set; } = new();
    public string Title { get; private set; } = "";

    /// <summary>Chave do ícone da <see cref="IconLibrary"/>, ou <c>null</c> para automático (ícone do programa).</summary>
    public string? Glyph { get; private set; }
    public IconFit IconFit { get; private set; } = IconFit.Padded;

    /// <summary>Cor de fundo ("#RRGGBB"), ou <c>null</c> para preto.</summary>
    public string? Background { get; private set; }

    /// <summary>Id do arquivo do ícone próprio (escolhido do PC), ou <c>null</c>.</summary>
    public string? CustomIconFileId { get; private set; }
    public Bitmap? CustomIcon { get; private set; }
    public string? CustomIconDataUrl { get; private set; }

    /// <summary>Ícone que o Windows mostra para o arquivo escolhido (o automático).</summary>
    public Bitmap? FileIcon { get; private set; }

    /// <summary>O que o tile mostra agora: rodando, verde, vermelho ou nada.</summary>
    public TileStatus Status { get; private set; }

    /// <summary>A última execução, para a tela de configurações.</summary>
    public ExecuteResult? LastResult { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running is not null;
        }
    }

    /// <summary>Algo que a tela de configurações mostra mudou.</summary>
    public event System.Action? Changed;

    public Type? SettingsComponent => typeof(ExecuteSettings);

    internal async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = _context.Settings;
        Action = await settings.GetJsonAsync<ExecuteAction?>(ActionKey, null, cancellationToken).ConfigureAwait(false) ?? new();
        Title = await settings.GetAsync(TitleKey, cancellationToken).ConfigureAwait(false) ?? "";
        Glyph = IconLibrary.ByKey(await settings.GetAsync(GlyphKey, cancellationToken).ConfigureAwait(false))?.Key;
        if (Enum.TryParse<IconFit>(await settings.GetAsync(IconFitKey, cancellationToken).ConfigureAwait(false), out var fit))
            IconFit = fit;
        Background = TileColor.Normalize(await settings.GetAsync(BackgroundKey, cancellationToken).ConfigureAwait(false));
        CustomIconFileId = await settings.GetAsync(CustomIconKey, cancellationToken).ConfigureAwait(false);
        await LoadCustomIconAsync(cancellationToken).ConfigureAwait(false);
        ReloadFileIcon();
    }

    // ---- Configurações ----

    /// <summary>Muda a ação. O ícone automático acompanha o caminho novo.</summary>
    public async Task SetActionAsync(ExecuteAction action)
    {
        var pathChanged = action.Mode != Action.Mode || !string.Equals(action.Path, Action.Path, StringComparison.Ordinal);
        Action = action;
        await _context.Settings.SetJsonAsync(ActionKey, action).ConfigureAwait(false);
        if (pathChanged)
            ReloadFileIcon();
        Changed?.Invoke();
        _context.RequestRender();
    }

    public Task UpdateActionAsync(Func<ExecuteAction, ExecuteAction> change) => SetActionAsync(change(Action));

    public async Task SetTitleAsync(string title)
    {
        Title = title ?? "";
        await _context.Settings.SetAsync(TitleKey, Title).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    /// <summary>Ícone da biblioteca; <c>null</c> = automático.</summary>
    public async Task SetGlyphAsync(string? key)
    {
        Glyph = IconLibrary.ByKey(key)?.Key;
        if (Glyph is null)
            await _context.Settings.RemoveAsync(GlyphKey).ConfigureAwait(false);
        else
            await _context.Settings.SetAsync(GlyphKey, Glyph).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    public async Task SetIconFitAsync(IconFit fit)
    {
        IconFit = fit;
        await _context.Settings.SetAsync(IconFitKey, fit.ToString()).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

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

    /// <summary>
    /// Usa um ícone escolhido pela pessoa (imagem, .ico, .exe ou .dll). Vira PNG e é copiado para os arquivos da
    /// instância, como no Open Application.
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
                _context.Logger.LogWarning("O ícone próprio do Execute sumiu; usando o automático.");
                return;
            }
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            using var loaded = new Bitmap(stream);
            CustomIcon = new Bitmap(loaded);
            CustomIconDataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _context.Logger.LogWarning(ex, "O ícone próprio do Execute não pôde ser lido.");
        }
    }

    /// <summary>
    /// Ícone automático: o do arquivo (o que o Explorer mostra) para programas e atalhos. Scripts (.bat, .cmd, .ps1) e
    /// comandos ficam com o glifo de terminal, que lê melhor no tile que a engrenagem genérica do Windows.
    /// </summary>
    private void ReloadFileIcon()
    {
        var version = Interlocked.Increment(ref _iconVersion);
        FileIcon = null;
        if (Action.Mode != ExecuteMode.Program || Action.IsEmpty)
            return;
        var path = Action.ResolvedPath;
        if (!Path.IsPathFullyQualified(path) || ScriptExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return;
        _ = LoadAsync();

        async Task LoadAsync()
        {
            try
            {
                var icon = await _loadFileIcon(path).ConfigureAwait(false);
                if (Volatile.Read(ref _iconVersion) != version)
                    return;
                FileIcon = icon;
                Changed?.Invoke();
                _context.RequestRender();
            }
            catch (Exception ex)
            {
                _context.Logger.LogDebug(ex, "Falha ao ler o ícone de {Path}.", path);
            }
        }
    }

    /// <summary>Glifo automático quando não há ícone de arquivo.</summary>
    public IconOption AutomaticGlyph =>
        IconLibrary.ByKey(Action.Mode == ExecuteMode.Command && Action.Shell != CommandShell.Cmd ? "code" : DefaultGlyph)!;

    // ---- Tile ----

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TileRenderer.Render(_context.PixelSize, _context.Theme, CurrentVisual()));

    internal TileVisual CurrentVisual()
    {
        var chosen = IconLibrary.ByKey(Glyph);
        var icon = CustomIcon ?? (chosen is null ? FileIcon : null);
        var glyph = icon is null ? chosen ?? AutomaticGlyph : null;
        var compact = _context.PixelSize.Height < 100;
        var detail = Action.IsEmpty && !compact ? "Configure" : null;
        return new TileVisual(icon, NullIfEmpty(Title), Running: false, Dimmed: Action.IsEmpty, Detail: detail)
        {
            Fit = IconFit,
            Background = TileColor.Parse(Background),
            Glyph = glyph?.Glyph,
            GlyphAccent = glyph?.Accent,
            Status = Status,
        };
    }

    public ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        switch (touch.Kind)
        {
            case TouchKind.Tap:
                // Não espera: o toque volta na hora e o tile mostra o andamento.
                _ = RunAsync();
                break;
            case TouchKind.LongPress:
                Stop();
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Roda a ação e mostra o resultado no tile. Um toque enquanto ainda roda é ignorado (não abre duas vezes).
    /// Devolve <c>null</c> quando não rodou por isso.
    /// </summary>
    public async Task<ExecuteResult?> RunAsync()
    {
        var action = Action;
        if (action.IsEmpty)
            return null;
        CancellationTokenSource running;
        lock (_gate)
        {
            if (_running is not null)
                return null;
            running = _running = CancellationTokenSource.CreateLinkedTokenSource(_context.Stopping);
            _feedback?.Cancel();
            _feedback = null;
        }

        if (action.WaitForExit)
            ShowStatus(TileStatus.Running);

        ExecuteResult result;
        try
        {
            result = await _runner.RunAsync(action, running.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ExecuteResult.Fail(ex.Message);
        }
        finally
        {
            lock (_gate)
                _running = null;
            running.Dispose();
        }

        LastResult = result;
        LastRunAt = DateTimeOffset.Now;
        if (result.Success)
            _context.Logger.LogInformation("Execute: {Target} rodou em {Duration}.", Describe(action), result.Duration);
        else
            _context.Logger.LogWarning("Execute: {Target} falhou: {Error}", Describe(action), result.Error);

        ShowStatus(result.Success ? TileStatus.Success : TileStatus.Failure);
        ScheduleClear(result.Success ? SuccessFeedback : FailureFeedback);
        return result;
    }

    /// <summary>Encerra o programa que está sendo esperado (long-press).</summary>
    public void Stop()
    {
        lock (_gate)
            _running?.Cancel();
    }

    private void ShowStatus(TileStatus status)
    {
        Status = status;
        Changed?.Invoke();
        _context.RequestRender();
    }

    private void ScheduleClear(TimeSpan after)
    {
        CancellationTokenSource feedback;
        lock (_gate)
        {
            _feedback?.Cancel();
            feedback = _feedback = CancellationTokenSource.CreateLinkedTokenSource(_context.Stopping);
        }
        _ = ClearAsync();

        async Task ClearAsync()
        {
            try
            {
                await Task.Delay(after, _context.Host.Time, feedback.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            lock (_gate)
            {
                if (!ReferenceEquals(_feedback, feedback))
                    return;
                _feedback = null;
            }
            feedback.Dispose();
            ShowStatus(TileStatus.None);
        }
    }

    private static string Describe(ExecuteAction action) =>
        action.Mode == ExecuteMode.Program ? action.ResolvedPath : action.Shell + " (comando)";

    public ValueTask OnSleepAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        // O programa pode ter sido atualizado (ícone novo) enquanto a página estava fora de vista.
        ReloadFileIcon();
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnRemovedAsync(CancellationToken cancellationToken)
    {
        if (CustomIconFileId is { } fileId)
            await _context.Files.DeleteAsync(fileId, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _feedback?.Cancel();
            _feedback = null;
        }
        return ValueTask.CompletedTask;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
