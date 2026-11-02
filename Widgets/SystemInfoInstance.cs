using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Metrics;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Settings;

namespace OpenWigiDash.SystemPlugin.Widgets;

/// <summary>Um tile do System Info na tela: acompanha uma série do coletor e redesenha a cada valor novo.</summary>
public sealed class SystemInfoInstance : IWidgetInstance
{
    internal const string ConfigKey = "config";

    private readonly IWidgetContext _context;
    private readonly MetricCollector _collector;
    private readonly object _gate = new();
    private MetricSeries? _series;
    private bool _awake = true;

    internal SystemInfoInstance(IWidgetContext context, MetricCollector collector)
    {
        _context = context;
        _collector = collector;
    }

    public SystemInfoConfig Config { get; private set; } = new();

    /// <summary>Algo que a tela de configurações mostra mudou (ex.: valor novo).</summary>
    public event Action? Changed;

    public Type? SettingsComponent => typeof(SystemInfoSettings);

    internal MetricSeries? Series => _series;

    internal IMetricSource Source => _collector.Source;

    internal async ValueTask LoadSettingsAsync(CancellationToken cancellationToken)
    {
        Config = await _context.Settings.GetJsonAsync(ConfigKey, new SystemInfoConfig(), cancellationToken).ConfigureAwait(false)
            ?? new SystemInfoConfig();
        Follow();
    }

    public async Task SetConfigAsync(SystemInfoConfig config)
    {
        var old = Config;
        Config = config;
        await _context.Settings.SetJsonAsync(ConfigKey, config).ConfigureAwait(false);
        if (old.Key != config.Key || old.Interval != config.Interval)
            Follow();
        Changed?.Invoke();
        _context.RequestRender();
    }

    /// <summary>Troca de métrica: o tema e a escala voltam ao sugerido para ela, como no System Vitals.</summary>
    public Task SetMetricAsync(MetricKind kind)
    {
        var theme = SystemInfoThemes.For(kind);
        return SetConfigAsync(Config with
        {
            Metric = kind,
            Device = kind == MetricKind.Ping ? MetricCatalog.PingHosts[0].Id : null,
            Title = null,
            Max = null,
            AlertAbove = null,
            Background = theme.Background,
            ChartColor = theme.Chart,
        });
    }

    /// <summary>Solta a série antiga e pega a da configuração atual.</summary>
    private void Follow()
    {
        lock (_gate)
        {
            if (_series is not null)
            {
                _series.Updated -= OnUpdated;
                _collector.Release(_series, _awake);
            }
            _series = _collector.Acquire(Config.Key, Config.Interval);
            if (!_awake)
                _collector.SetAwake(_series, false);
            _series.Updated += OnUpdated;
        }
    }

    private void OnUpdated()
    {
        if (!_awake)
            return;
        _context.RequestRender();
        Changed?.Invoke();
    }

    public ValueTask<WidgetFrame> RenderAsync(RenderReason reason, CancellationToken cancellationToken) =>
        ValueTask.FromResult(SparklineRenderer.Render(_context.PixelSize, CurrentVisual()));

    internal SparkVisual CurrentVisual() => BuildVisual(Config, _series?.Snapshot() ?? []);

    /// <summary>O que desenhar para a configuração e o histórico (bruto, na unidade do Windows).</summary>
    internal static SparkVisual BuildVisual(SystemInfoConfig config, IReadOnlyList<double?> raw)
    {
        var info = MetricCatalog.Get(config.Metric);
        var values = raw.Select(v => v is { } x ? MetricCatalog.Display(config.Metric, x, config.NetworkUnit) : (double?)null).ToList();
        var last = values.Count > 0 ? values[^1] : null;
        var title = config.Title ?? MetricCatalog.DefaultTitle(config.Metric, config.NetworkUnit, config.Device);
        var text = last is { } v ? MetricCatalog.Format(config.Metric, v) : "–";
        var unit = last is null ? "" : info.Unit;
        var max = config.Max is { } m && m > info.DefaultMin ? m : info.DefaultMax;
        var alert = config.AlertAbove is { } limit && last is { } current && current > limit;
        return new SparkVisual(config, title, text, unit, values, info.DefaultMin, max, alert);
    }

    public ValueTask OnTouchAsync(TouchEvent touch, CancellationToken cancellationToken)
    {
        if (touch.Kind != TouchKind.Tap)
            return ValueTask.CompletedTask;
        var target = Config.Tap switch
        {
            TapAction.TaskManager => "taskmgr.exe",
            TapAction.Custom => Config.TapTarget,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(target))
            return ValueTask.CompletedTask;
        try
        {
            Process.Start(new ProcessStartInfo(target.Trim()) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _context.Logger.LogWarning(ex, "Não deu para abrir {Target}.", target);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnSleepAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_awake && _series is not null)
                _collector.SetAwake(_series, false);
            _awake = false;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask OnWakeAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_awake && _series is not null)
                _collector.SetAwake(_series, true);
            _awake = true;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_series is not null)
            {
                _series.Updated -= OnUpdated;
                _collector.Release(_series, _awake);
                _series = null;
            }
        }
        return ValueTask.CompletedTask;
    }
}
