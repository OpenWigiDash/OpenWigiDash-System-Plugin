using System.Drawing;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Metrics;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

namespace SystemPlugin.Tests;

public class GpuCounterTests
{
    [Fact]
    public void ParsesLuidFromEngineInstance()
    {
        Assert.Equal(0x10702L, GpuCounters.ParseLuid("pid_1234_luid_0x00000000_0x00010702_phys_0_eng_0_engtype_3D"));
        Assert.Equal((1L << 32) | 0x92744, GpuCounters.ParseLuid("luid_0x00000001_0x00092744_phys_0"));
        Assert.Null(GpuCounters.ParseLuid("_Total"));
        Assert.Equal("luid_0x00000000_0x00010702", GpuCounters.FormatLuid(0x10702));
    }

    [Fact]
    public void UtilizationSumsProcessesAndTakesBusiestEngine()
    {
        var engines = new[]
        {
            ("pid_1_luid_0x00000000_0x00010702_phys_0_eng_0_engtype_3D", 20.0),
            ("pid_2_luid_0x00000000_0x00010702_phys_0_eng_0_engtype_3D", 15.0),
            ("pid_2_luid_0x00000000_0x00010702_phys_0_eng_3_engtype_VideoDecode", 30.0),
            ("pid_3_luid_0x00000000_0x00010702_phys_0_eng_5_engtype_Copy", 90.0),
            ("pid_3_luid_0x00000000_0x00010702_phys_0_eng_6_engtype_Copy", 40.0),
            ("pid_1_luid_0x00000000_0x00099999_phys_0_eng_0_engtype_3D", 5.0),
        };

        var use = GpuCounters.Utilization(engines);

        Assert.Equal(100, use[0x10702]); // Copy somou 130: limita em 100
        Assert.Equal(5, use[0x99999]);
    }

    [Fact]
    public void DuplicatedAdapterKeepsTheOneWithCounters()
    {
        // A RTX aparece duas vezes por causa do SuperDisplay; o Basic Render Driver (0x1414) sai.
        var adapters = new[]
        {
            new GpuAdapter(0x92744, "NVIDIA GeForce RTX 5060 Ti", 16_000_000_000, 0x10DE),
            new GpuAdapter(0x10702, "NVIDIA GeForce RTX 5060 Ti", 16_000_000_000, 0x10DE),
            new GpuAdapter(0x5555, "Microsoft Basic Render Driver", 0, 0x1414),
        };

        var result = GpuCounters.Distinct(adapters, new HashSet<long> { 0x10702 });

        Assert.Equal([0x10702L], result.Select(a => a.Luid));
        Assert.Single(GpuCounters.Distinct(adapters, new HashSet<long>()));
    }

    [Fact]
    public void DiskIsBusiestAndNetworkSums()
    {
        (string, double)[] idle = [("0 C:", 95), ("1 G:", 40), ("_Total", 67)];
        Assert.Equal(60, WindowsMetrics.DiskActivity(idle, null));
        Assert.Equal(5, WindowsMetrics.DiskActivity(idle, "0 C:"));
        Assert.Null(WindowsMetrics.DiskActivity(idle, "9 Z:"));

        (string, double)[] net = [("Intel Wi-Fi 6 AX200", 1000), ("Realtek GbE", 500)];
        Assert.Equal(1500, WindowsMetrics.Sum(net, null));
        Assert.Equal(500, WindowsMetrics.Sum(net, "Realtek GbE"));
        Assert.True(WindowsMetrics.IsPackage("RAPL_Package0_PKG"));
        Assert.False(WindowsMetrics.IsPackage("RAPL_Package0_Core3_CORE"));
    }
}

public class MetricCatalogTests
{
    [Fact]
    public void EveryKindHasAnEntry()
    {
        foreach (var kind in Enum.GetValues<MetricKind>())
            Assert.Equal(kind, MetricCatalog.Get(kind).Kind);
    }

    [Theory]
    [InlineData(MetricKind.CpuPercent, 26.4, "26")]
    [InlineData(MetricKind.GpuTemperature, 47.6, "48")]
    [InlineData(MetricKind.NetworkDown, 0.04, "0,0")]
    [InlineData(MetricKind.NetworkDown, 3.25, "3,3")]
    [InlineData(MetricKind.NetworkDown, 12.6, "13")]
    public void Formats(MetricKind kind, double value, string expected) =>
        Assert.Equal(expected, MetricCatalog.Format(kind, value));

    [Fact]
    public void NetworkConvertsBytes()
    {
        Assert.Equal(1, MetricCatalog.Display(MetricKind.NetworkUp, 1024 * 1024, NetworkUnit.MegabytesPerSecond));
        Assert.Equal(8, MetricCatalog.Display(MetricKind.NetworkUp, 1_000_000, NetworkUnit.MegabitsPerSecond));
        Assert.Equal("LAN ↓ Mbps", MetricCatalog.DefaultTitle(MetricKind.NetworkDown, NetworkUnit.MegabitsPerSecond, null));
        Assert.Equal("GOOGLE", MetricCatalog.DefaultTitle(MetricKind.Ping, NetworkUnit.MegabytesPerSecond, "8.8.8.8"));
    }

    [Fact]
    public void AutoScaleUsesPeakWithHeadroom()
    {
        Assert.Equal(100, SparklineRenderer.ScaleMax(0, 100, [5.0, 20.0]));
        Assert.Equal(22, SparklineRenderer.ScaleMax(0, null, [5.0, 20.0, null]), 6);
        Assert.Equal(1, SparklineRenderer.ScaleMax(0, null, [null]));
    }

    [Fact]
    public void SegmentsBreakOnGapsAndEndAtRightEdge()
    {
        var size = new PixelSize(198, 143);
        var segments = SparklineRenderer.Segments(size, [10.0, 20.0, null, 50.0, 100.0], 0, 100, 10);

        Assert.Equal(2, segments.Count);
        Assert.Equal(198f, segments[1][^1].X, 3);
        Assert.True(segments[1][^1].Y < segments[1][0].Y); // 100 fica acima de 50
        Assert.True(segments[0][0].Y <= 143 * 0.9f + 0.01f); // começa na base (10% acima do rodapé)
    }
}

internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeMetrics : IMetricSource
{
    public int Collects { get; private set; }
    public Dictionary<MetricKind, double?> Values { get; } = [];

    public bool IsAvailable(MetricKind kind) => true;
    public IReadOnlyList<MetricDevice> GetDevices(MetricKind kind) => [];
    public void Collect() => Collects++;
    public double? Read(MetricKey key) => Values.GetValueOrDefault(key.Kind);
    public Task<double?> PingAsync(string host, CancellationToken cancellationToken) => Task.FromResult<double?>(21);
}

public class MetricCollectorTests
{
    private static (MetricCollector Collector, FakeMetrics Source, ManualTime Time) Create()
    {
        var source = new FakeMetrics();
        var time = new ManualTime();
        return (new MetricCollector(source, time, autoStart: false), source, time);
    }

    [Fact]
    public void ReadsCountersOncePerCycleForManyTiles()
    {
        var (collector, source, _) = Create();
        source.Values[MetricKind.CpuPercent] = 26;
        source.Values[MetricKind.RamPercent] = 61;
        var a = collector.Acquire(new MetricKey(MetricKind.CpuPercent, null), TimeSpan.FromSeconds(1));
        var b = collector.Acquire(new MetricKey(MetricKind.CpuPercent, null), TimeSpan.FromSeconds(1));
        var c = collector.Acquire(new MetricKey(MetricKind.RamPercent, null), TimeSpan.FromSeconds(1));

        collector.Tick();

        Assert.Same(a, b); // mesma métrica e intervalo: a mesma série
        Assert.Equal(1, source.Collects);
        Assert.Equal(26, a.Last);
        Assert.Equal(61, c.Last);
    }

    [Fact]
    public void WaitsForTheIntervalAndStopsWhenAllSleep()
    {
        var (collector, source, time) = Create();
        source.Values[MetricKind.CpuPercent] = 10;
        var series = collector.Acquire(new MetricKey(MetricKind.CpuPercent, null), TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(2), collector.Tick());
        time.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(TimeSpan.FromSeconds(1), collector.Tick());
        Assert.Single(series.Snapshot());

        time.Now += TimeSpan.FromSeconds(1);
        collector.Tick();
        Assert.Equal(2, series.Snapshot().Count);

        collector.SetAwake(series, false);
        time.Now += TimeSpan.FromSeconds(5);
        Assert.Null(collector.Tick()); // nada acordado: o ciclo para
        Assert.Equal(2, source.Collects);
    }

    [Fact]
    public void HistoryIsKeptWhileWatchedAndCapped()
    {
        var (collector, source, time) = Create();
        var key = new MetricKey(MetricKind.CpuPercent, null);
        var series = collector.Acquire(key, TimeSpan.FromSeconds(1));
        for (var i = 0; i < MetricSeries.Capacity + 20; i++)
        {
            source.Values[MetricKind.CpuPercent] = i;
            collector.Tick();
            time.Now += TimeSpan.FromSeconds(1);
        }
        Assert.Equal(MetricSeries.Capacity, series.Snapshot().Count);

        // Outro tile da mesma métrica já começa com o gráfico cheio.
        var again = collector.Acquire(key, TimeSpan.FromSeconds(1));
        Assert.Same(series, again);

        collector.Release(series, wasAwake: true);
        collector.Release(again, wasAwake: true);
        Assert.Empty(collector.Acquire(key, TimeSpan.FromSeconds(1)).Snapshot());
    }

    [Fact]
    public void IntervalIsClamped()
    {
        Assert.Equal(MetricCollector.MinInterval, MetricCollector.Clamp(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(MetricCollector.MaxInterval, MetricCollector.Clamp(TimeSpan.FromMinutes(2)));
    }
}

public class SystemInfoInstanceTests
{
    private static (SystemInfoInstance Instance, FakeMetrics Source, MetricCollector Collector, FakeContext Context) Create()
    {
        var source = new FakeMetrics();
        var collector = new MetricCollector(source, new ManualTime(), autoStart: false);
        var context = new FakeContext(WidgetSize.Small);
        return (new SystemInfoInstance(context, collector), source, collector, context);
    }

    [Fact]
    public async Task NewValueAsksForAFrame()
    {
        var (instance, source, collector, context) = Create();
        await instance.LoadSettingsAsync(default);
        source.Values[MetricKind.CpuPercent] = 26.4;

        collector.Tick();

        Assert.Equal(1, context.Renders);
        var visual = instance.CurrentVisual();
        Assert.Equal("CPU", visual.Title);
        Assert.Equal("26", visual.Value);
        Assert.Equal("%", visual.Unit);
    }

    [Fact]
    public async Task SleepingTileStopsTheCollector()
    {
        var (instance, source, collector, context) = Create();
        await instance.LoadSettingsAsync(default);
        await instance.OnSleepAsync(default);

        Assert.Null(collector.Tick());
        Assert.Equal(0, source.Collects);
        Assert.Equal(0, context.Renders);

        await instance.OnWakeAsync(default);
        collector.Tick();
        Assert.Equal(1, source.Collects);
    }

    [Fact]
    public async Task MetricChangeAppliesThemeAndSaves()
    {
        var (instance, _, _, context) = Create();
        await instance.LoadSettingsAsync(default);

        await instance.SetMetricAsync(MetricKind.GpuTemperature);

        Assert.Equal(SystemInfoThemes.Lime.Background, instance.Config.Background);
        var saved = await context.Settings.GetJsonAsync<SystemInfoConfig?>(SystemInfoInstance.ConfigKey, null);
        Assert.Equal(MetricKind.GpuTemperature, saved!.Metric);
        Assert.Equal(MetricKind.GpuTemperature, instance.Series!.Key.Kind);
    }

    [Fact]
    public void VisualShowsDashWithoutReadingAndAlertAboveLimit()
    {
        var config = new SystemInfoConfig { Metric = MetricKind.CpuPercent, AlertAbove = 90 };

        var empty = SystemInfoInstance.BuildVisual(config, [null]);
        Assert.Equal("–", empty.Value);
        Assert.Equal("", empty.Unit);

        Assert.True(SystemInfoInstance.BuildVisual(config, [95.0]).Alert);
        Assert.False(SystemInfoInstance.BuildVisual(config, [50.0]).Alert);

        var net = SystemInfoInstance.BuildVisual(new SystemInfoConfig { Metric = MetricKind.NetworkDown }, [2.5 * 1024 * 1024]);
        Assert.Equal("2,5", net.Value);
        Assert.Null(net.Max);
    }

    [Fact]
    public void ConfigRoundTripsAsJson()
    {
        var config = new SystemInfoConfig { Metric = MetricKind.Ping, Device = "1.1.1.1", Style = ChartStyle.Hatch, Max = 80 };
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        Assert.Contains("\"Ping\"", json);
        Assert.Equal(config, System.Text.Json.JsonSerializer.Deserialize<SystemInfoConfig>(json));
    }
}

/// <summary>Desenho: só roda no Windows (System.Drawing).</summary>
public class SystemInfoRenderTests
{
    public static TheoryData<int, int> Sizes()
    {
        var data = new TheoryData<int, int>();
        foreach (var size in new[] { WidgetSize.Small, WidgetSize.Wide, WidgetSize.Large })
        {
            foreach (var inset in TileFrame.InsetOptions)
            {
                var cell = size.CellSpan;
                data.Add(cell.Width - 2 * inset, cell.Height - 2 * inset);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RendersEveryStyleInEverySize(int width, int height)
    {
        var size = new PixelSize(width, height);
        double?[] values = [10, 40, null, 35, 80, 100, 0, 55];
        foreach (var style in Enum.GetValues<ChartStyle>())
        {
            var visual = new SparkVisual(new SystemInfoConfig { Style = style }, "CPU Watt", "63", "W", values, 0, null);
            var frame = SparklineRenderer.Render(size, visual);
            Assert.Equal(size, frame.Size);
        }
    }

    [Fact]
    public void BackgroundAndChartUseConfiguredColors()
    {
        var size = new PixelSize(198, 143);
        var visual = new SparkVisual(new SystemInfoConfig { Style = ChartStyle.Fill, OpacityTop = 100, OpacityBottom = 100, Title = "" },
            "", "", "", Enumerable.Repeat<double?>(100, 60).ToList(), 0, 100);
        using var bitmap = SparklineRenderer.Draw(size, visual);

        Assert.Equal(ColorTranslator.FromHtml("#F700E7").ToArgb(), bitmap.GetPixel(100, 130).ToArgb()); // gráfico cheio
        var empty = new SparkVisual(new SystemInfoConfig(), "", "", "", [], 0, 100);
        using var blank = SparklineRenderer.Draw(size, empty);
        Assert.Equal(ColorTranslator.FromHtml("#12142D").ToArgb(), blank.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void PreviewRenders()
    {
        var frame = SparklineRenderer.Render(new PixelSize(198, 143), SystemInfoWidget.PreviewVisual());
        Assert.Equal(198, frame.Size.Width);
    }
}
