using System.Collections.Concurrent;
using System.Drawing;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Audio;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Settings;

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>Um botão de placa de som na tela: cada toque passa para o próximo dispositivo da lista.</summary>
public sealed class AudioDeviceInstance : IWidgetInstance
{
    internal const string TitleKey = "title";
    internal const string FlowKey = "flow";
    internal const string RolesKey = "roles";
    internal const string MatchingKey = "matching";
    internal const string DevicesKey = "devices";
    internal const string BackgroundKey = "background";
    internal const string FlowBadgeKey = "flowBadge";

    /// <summary>Cores do selo de direção: azul = saída, laranja = entrada.</summary>
    internal static readonly Color OutputBadgeColor = Color.FromArgb(0x3B, 0x82, 0xF6);
    internal static readonly Color InputBadgeColor = Color.FromArgb(0xF0, 0x93, 0x2B);

    private readonly IWidgetContext _context;
    private readonly IAudioPlatform _audio;
    private readonly ConcurrentDictionary<string, Bitmap> _images = new();
    private List<AudioDeviceEntry> _entries = [];
    private DateTime _failedAt = DateTime.MinValue;
    private static readonly TimeSpan FailureShown = TimeSpan.FromSeconds(3);

    internal AudioDeviceInstance(IWidgetContext context, IAudioPlatform audio)
    {
        _context = context;
        _audio = audio;
        _audio.Changed += OnAudioChanged;
    }

    public string Title { get; private set; } = "";
    public AudioFlow Flow { get; private set; } = AudioFlow.Output;
    public AudioRoles Roles { get; private set; } = AudioRoles.Default;
    public DeviceMatching Matching { get; private set; } = DeviceMatching.Fuzzy;
    public IReadOnlyList<AudioDeviceEntry> Entries => _entries;

    /// <summary>Dispositivos da direção escolhida, ligados primeiro.</summary>
    public IReadOnlyList<AudioDevice> Devices => _audio.GetDevices(Flow);

    public string? DefaultId => _audio.GetDefaultId(Flow, Roles);

    public AudioDevice? Resolve(AudioDeviceEntry entry) => DeviceMatcher.Find(entry, Devices, Matching);

    public Bitmap? ImageFor(AudioDeviceEntry entry) =>
        entry.ImageFileId is { } id && _images.TryGetValue(id, out var image) ? image : null;

    public event Action? Changed;

    public Type? SettingsComponent => typeof(AudioDeviceSettings);

    internal async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = _context.Settings;
        Title = await settings.GetAsync(TitleKey, cancellationToken).ConfigureAwait(false) ?? "";
        if (Enum.TryParse<AudioFlow>(await settings.GetAsync(FlowKey, cancellationToken).ConfigureAwait(false), out var flow))
            Flow = flow;
        if (Enum.TryParse<AudioRoles>(await settings.GetAsync(RolesKey, cancellationToken).ConfigureAwait(false), out var roles))
            Roles = roles;
        if (Enum.TryParse<DeviceMatching>(await settings.GetAsync(MatchingKey, cancellationToken).ConfigureAwait(false), out var matching))
            Matching = matching;
        Background = TileColor.Normalize(await settings.GetAsync(BackgroundKey, cancellationToken).ConfigureAwait(false));
        if (bool.TryParse(await settings.GetAsync(FlowBadgeKey, cancellationToken).ConfigureAwait(false), out var badge))
            ShowFlowBadge = badge;
        _entries = await settings.GetJsonAsync(DevicesKey, new List<AudioDeviceEntry>(), cancellationToken).ConfigureAwait(false);
        foreach (var entry in _entries)
            await LoadImageAsync(entry.ImageFileId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cor de fundo ("#RRGGBB"), ou <c>null</c> para o preto padrão.</summary>
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

    /// <summary>Selo no canto dizendo se o tile troca a saída (alto-falante azul) ou a entrada (microfone laranja).</summary>
    public bool ShowFlowBadge { get; private set; }

    public Task SetShowFlowBadgeAsync(bool value) => SaveAsync(FlowBadgeKey, (ShowFlowBadge = value).ToString());

    internal TileBadge? FlowBadge => !ShowFlowBadge ? null
        : Flow == AudioFlow.Input ? new TileBadge(AudioGlyphs.ByKey("microphone")!.Glyph, InputBadgeColor)
        : new TileBadge(AudioGlyphs.ByKey("volume")!.Glyph, OutputBadgeColor);

    public Task SetTitleAsync(string title) => SaveAsync(TitleKey, Title = title ?? "");

    public async Task SetFlowAsync(AudioFlow flow)
    {
        if (flow == Flow)
            return;
        Flow = flow;
        // Os dispositivos de saída não servem como entrada: a lista recomeça.
        foreach (var entry in _entries)
            await DeleteImageAsync(entry.ImageFileId).ConfigureAwait(false);
        _entries = [];
        await _context.Settings.SetJsonAsync(DevicesKey, _entries).ConfigureAwait(false);
        await SaveAsync(FlowKey, flow.ToString()).ConfigureAwait(false);
    }

    public Task SetRolesAsync(AudioRoles roles) => SaveAsync(RolesKey, (Roles = roles).ToString());

    public Task SetMatchingAsync(DeviceMatching matching) => SaveAsync(MatchingKey, (Matching = matching).ToString());

    public Task AddDeviceAsync(AudioDevice device)
    {
        if (_entries.Any(e => string.Equals(e.Id, device.Id, StringComparison.OrdinalIgnoreCase)))
            return Task.CompletedTask;
        _entries = [.. _entries, AudioDeviceEntry.From(device)];
        return SaveEntriesAsync();
    }

    public async Task RemoveAsync(int index)
    {
        if (index < 0 || index >= _entries.Count)
            return;
        await DeleteImageAsync(_entries[index].ImageFileId).ConfigureAwait(false);
        _entries = _entries.Where((_, i) => i != index).ToList();
        await SaveEntriesAsync().ConfigureAwait(false);
    }

    public Task MoveAsync(int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= _entries.Count || target < 0 || target >= _entries.Count)
            return Task.CompletedTask;
        var list = _entries.ToList();
        (list[index], list[target]) = (list[target], list[index]);
        _entries = list;
        return SaveEntriesAsync();
    }

    public Task SetGlyphAsync(int index, string? glyph)
    {
        if (index < 0 || index >= _entries.Count)
            return Task.CompletedTask;
        var list = _entries.ToList();
        list[index] = list[index] with { Glyph = glyph };
        _entries = list;
        return SaveEntriesAsync();
    }

    /// <summary>Usa uma imagem própria (copiada para os arquivos da instância) no lugar do ícone.</summary>
    public async Task SetImageAsync(int index, string sourcePath)
    {
        if (index < 0 || index >= _entries.Count)
            return;
        await DeleteImageAsync(_entries[index].ImageFileId).ConfigureAwait(false);
        var fileId = "device-" + Guid.NewGuid().ToString("N");
        await _context.Files.ImportAsync(fileId, sourcePath).ConfigureAwait(false);
        await LoadImageAsync(fileId, default).ConfigureAwait(false);
        var list = _entries.ToList();
        list[index] = list[index] with { ImageFileId = fileId };
        _entries = list;
        await SaveEntriesAsync().ConfigureAwait(false);
    }

    public async Task ClearImageAsync(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].ImageFileId is null)
            return;
        await DeleteImageAsync(_entries[index].ImageFileId).ConfigureAwait(false);
        var list = _entries.ToList();
        list[index] = list[index] with { ImageFileId = null };
        _entries = list;
        await SaveEntriesAsync().ConfigureAwait(false);
    }

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TileRenderer.Render(_context.PixelSize, _context.Theme, CurrentVisual()));

    internal TileVisual CurrentVisual() => BuildVisual() with { Background = TileColor.Parse(Background), Badge = FlowBadge };

    private TileVisual BuildVisual()
    {
        var compact = _context.PixelSize.Height < 100;
        var title = string.IsNullOrWhiteSpace(Title) ? null : Title;
        IReadOnlyList<AudioDevice> devices;
        string? defaultId;
        try
        {
            devices = Devices;
            defaultId = DefaultId;
        }
        catch (Exception ex)
        {
            _context.Logger.LogWarning(ex, "Falha ao ler os dispositivos de áudio.");
            return new TileVisual(null, title, false, Dimmed: true, Detail: compact ? null : "Áudio indisponível")
                { Glyph = AudioGlyphs.For(null, null, Flow).Glyph };
        }

        if (_entries.Count == 0)
        {
            return new TileVisual(null, title, false, Dimmed: true, Detail: compact ? null : "Escolha os dispositivos")
                { Glyph = AudioGlyphs.For(null, null, Flow).Glyph };
        }

        if (DateTime.UtcNow - _failedAt < FailureShown)
        {
            return new TileVisual(null, compact ? "Recusado" : title, false, Dimmed: true,
                Detail: compact ? null : "O Windows recusou")
                { Glyph = AudioGlyphs.For(null, null, Flow).Glyph };
        }

        var index = DeviceMatcher.CurrentIndex(_entries, defaultId, devices, Matching);
        var current = devices.FirstOrDefault(d => string.Equals(d.Id, defaultId, StringComparison.OrdinalIgnoreCase));
        var entry = index >= 0 ? _entries[index] : null;
        var name = current is null ? null : ShortName(current);
        var glyph = AudioGlyphs.For(entry?.Glyph, entry?.Name ?? current?.Name, Flow);
        return new TileVisual(entry is null ? null : ImageFor(entry), title ?? (compact ? null : name), false)
        {
            Glyph = glyph.Glyph,
            GlyphAccent = glyph.Accent,
            DotCount = _entries.Count,
            DotIndex = index,
        };
    }

    private static string ShortName(AudioDevice device) =>
        string.IsNullOrWhiteSpace(device.Description) ? device.Name : device.Description;

    public ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        try
        {
            switch (touch.Kind)
            {
                case TouchKind.Tap:
                    if (DeviceMatcher.NextIndex(_entries, DefaultId, Devices, Matching) is { } next)
                        Switch(next);
                    break;
                case TouchKind.LongPress:
                    if (DeviceMatcher.FirstAvailable(_entries, Devices, Matching) is { } first)
                        Switch(first);
                    break;
            }
        }
        catch (Exception ex)
        {
            _context.Logger.LogWarning(ex, "Falha ao trocar o dispositivo de áudio.");
            // Sem isso o toque parecia não fazer nada (ex.: o "Chat" de headsets JBL não aceita virar saída padrão).
            _failedAt = DateTime.UtcNow;
            _context.RequestRender();
            _ = Task.Delay(FailureShown + TimeSpan.FromMilliseconds(100)).ContinueWith(_ => _context.RequestRender(), TaskScheduler.Default);
        }
        return ValueTask.CompletedTask;
    }

    private void Switch(int index)
    {
        if (Resolve(_entries[index]) is { } device)
        {
            _audio.SetDefault(device.Id, Roles);
            _context.RequestRender();
        }
    }

    public async ValueTask OnRemovedAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in _entries)
            await DeleteImageAsync(entry.ImageFileId).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _audio.Changed -= OnAudioChanged;
        return ValueTask.CompletedTask;
    }

    private void OnAudioChanged()
    {
        Changed?.Invoke();
        _context.RequestRender();
    }

    private async Task SaveAsync(string key, string value)
    {
        await _context.Settings.SetAsync(key, value).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    private async Task SaveEntriesAsync()
    {
        await _context.Settings.SetJsonAsync(DevicesKey, _entries).ConfigureAwait(false);
        Changed?.Invoke();
        _context.RequestRender();
    }

    private async Task LoadImageAsync(string? fileId, CancellationToken cancellationToken)
    {
        if (fileId is null)
            return;
        try
        {
            if (await _context.Files.GetPathAsync(fileId, cancellationToken).ConfigureAwait(false) is not { } path || !File.Exists(path))
                return;
            using var stream = new MemoryStream(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            using var loaded = new Bitmap(stream);
            _images[fileId] = new Bitmap(loaded);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _context.Logger.LogWarning(ex, "Imagem do dispositivo não pôde ser lida.");
        }
    }

    private async Task DeleteImageAsync(string? fileId)
    {
        if (fileId is null)
            return;
        _images.TryRemove(fileId, out _);
        await _context.Files.DeleteAsync(fileId).ConfigureAwait(false);
    }
}
