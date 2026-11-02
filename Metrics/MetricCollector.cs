using Microsoft.Extensions.Logging;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>De onde vêm os números. O Windows de verdade é <see cref="WindowsMetrics"/>; os testes usam um falso.</summary>
internal interface IMetricSource
{
    /// <summary>A métrica existe neste PC (ex.: RAPL, NVML).</summary>
    bool IsAvailable(MetricKind kind);

    /// <summary>O que dá para escolher na métrica (GPUs, discos, placas de rede). Vazio = sem escolha.</summary>
    IReadOnlyList<MetricDevice> GetDevices(MetricKind kind);

    /// <summary>Lê os contadores uma vez por ciclo, para todas as métricas.</summary>
    void Collect();

    /// <summary>Valor da métrica na última coleta; <c>null</c> = sem leitura agora.</summary>
    double? Read(MetricKey key);

    /// <summary>Ping é lento (rede); fica fora do ciclo e é feito à parte.</summary>
    Task<double?> PingAsync(string host, CancellationToken cancellationToken);
}

/// <summary>Uma série que uma ou mais instâncias mostram: os últimos valores, na ordem em que chegaram.</summary>
internal sealed class MetricSeries
{
    public const int Capacity = 300;

    private readonly object _gate = new();
    private readonly Queue<double?> _values = new();

    public MetricSeries(MetricKey key, TimeSpan interval)
    {
        Key = key;
        Interval = interval;
    }

    public MetricKey Key { get; }
    public TimeSpan Interval { get; }
    internal DateTime DueAt { get; set; }
    internal int Watchers { get; set; }
    internal int Awake { get; set; }

    /// <summary>Chegou um valor novo.</summary>
    public event Action? Updated;

    public double? Last
    {
        get
        {
            lock (_gate)
                return _values.Count == 0 ? null : _values.Last();
        }
    }

    public IReadOnlyList<double?> Snapshot()
    {
        lock (_gate)
            return _values.ToArray();
    }

    internal void Push(double? value)
    {
        lock (_gate)
        {
            _values.Enqueue(value);
            while (_values.Count > Capacity)
                _values.Dequeue();
        }
        Updated?.Invoke();
    }
}

/// <summary>
/// Um coletor para todas as instâncias: cada contador é lido uma vez por ciclo, mesmo com vários tiles. O ciclo
/// segue o menor intervalo das séries acordadas e para quando todas dormem. O histórico fica aqui, não no tile:
/// trocar de página e voltar mostra o gráfico já cheio.
/// </summary>
internal sealed class MetricCollector : IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(10);

    private static readonly Lazy<MetricCollector> LazyShared = new(() => new MetricCollector(new WindowsMetrics()));
    public static MetricCollector Shared => LazyShared.Value;

    private readonly object _gate = new();
    private readonly Dictionary<(MetricKey, TimeSpan), MetricSeries> _series = [];
    private readonly IMetricSource _source;
    private readonly TimeProvider _time;
    private readonly bool _autoStart;
    private readonly SemaphoreSlim _wake = new(0);
    private CancellationTokenSource? _loop;
    private ILogger? _logger;

    internal MetricCollector(IMetricSource source, TimeProvider? time = null, bool autoStart = true)
    {
        _source = source;
        _time = time ?? TimeProvider.System;
        _autoStart = autoStart;
    }

    public IMetricSource Source => _source;

    public void UseLogger(ILoggerFactory factory) => _logger ??= factory.CreateLogger("OpenWigiDash.System.Metrics");

    /// <summary>Começa a acompanhar a série (acordada). Devolva com <see cref="Release"/>.</summary>
    public MetricSeries Acquire(MetricKey key, TimeSpan interval)
    {
        interval = Clamp(interval);
        MetricSeries series;
        lock (_gate)
        {
            if (!_series.TryGetValue((key, interval), out series!))
                _series[(key, interval)] = series = new MetricSeries(key, interval);
            series.Watchers++;
            series.Awake++;
            series.DueAt = DateTime.MinValue;
        }
        Kick();
        return series;
    }

    public void Release(MetricSeries series, bool wasAwake)
    {
        lock (_gate)
        {
            series.Watchers--;
            if (wasAwake)
                series.Awake--;
            // Sem ninguém olhando, o histórico vai embora (uma série nova começa vazia).
            if (series.Watchers <= 0)
                _series.Remove((series.Key, series.Interval));
        }
    }

    public void SetAwake(MetricSeries series, bool awake)
    {
        lock (_gate)
        {
            series.Awake += awake ? 1 : -1;
            if (awake)
                series.DueAt = DateTime.MinValue;
        }
        if (awake)
            Kick();
    }

    public static TimeSpan Clamp(TimeSpan interval) =>
        interval < MinInterval ? MinInterval : interval > MaxInterval ? MaxInterval : interval;

    /// <summary>Um ciclo: lê o que estiver vencido e diz quanto esperar até o próximo. <c>null</c> = nada acordado.</summary>
    internal TimeSpan? Tick()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        List<MetricSeries> due;
        DateTime? next = null;
        lock (_gate)
        {
            due = _series.Values.Where(s => s.Awake > 0 && s.DueAt <= now).ToList();
            foreach (var s in due)
                s.DueAt = now + s.Interval;
            foreach (var s in _series.Values.Where(s => s.Awake > 0))
                next = next is null || s.DueAt < next ? s.DueAt : next;
        }

        if (due.Count > 0)
        {
            var sampled = new Dictionary<MetricKey, double?>();
            if (due.Any(s => s.Key.Kind != MetricKind.Ping))
            {
                try
                {
                    _source.Collect();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Falha ao ler os contadores do Windows.");
                }
            }
            foreach (var s in due)
            {
                if (s.Key.Kind == MetricKind.Ping)
                {
                    StartPing(s);
                    continue;
                }
                if (!sampled.TryGetValue(s.Key, out var value))
                {
                    try
                    {
                        value = _source.Read(s.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Falha ao ler {Metric}.", s.Key.Kind);
                        value = null;
                    }
                    sampled[s.Key] = value;
                }
                s.Push(value);
            }
        }

        if (next is null)
            return null;
        var wait = next.Value - _time.GetUtcNow().UtcDateTime;
        return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
    }

    private void StartPing(MetricSeries series)
    {
        var host = string.IsNullOrWhiteSpace(series.Key.Device) ? MetricCatalog.PingHosts[0].Id : series.Key.Device!;
        _ = Task.Run(async () =>
        {
            double? value;
            try
            {
                value = await _source.PingAsync(host, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Ping para {Host} falhou.", host);
                value = null;
            }
            series.Push(value);
        });
    }

    private void Kick()
    {
        if (!_autoStart)
            return;
        lock (_gate)
        {
            if (_loop is null)
            {
                _loop = new CancellationTokenSource();
                var token = _loop.Token;
                _ = Task.Run(() => RunAsync(token));
                return;
            }
        }
        _wake.Release();
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan? wait;
            try
            {
                wait = Tick();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falha no ciclo do coletor.");
                wait = TimeSpan.FromSeconds(1);
            }
            try
            {
                // Sem nada acordado, fica parado até alguém acordar (Kick).
                if (wait is null)
                    await _wake.WaitAsync(token).ConfigureAwait(false);
                else
                    await _wake.WaitAsync(wait.Value, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _loop?.Cancel();
        (_source as IDisposable)?.Dispose();
    }
}
