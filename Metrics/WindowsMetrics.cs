using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>
/// As métricas pelo que o Windows já entrega, sem admin e sem programa de terceiros: PDH, DXGI, D3DKMT e a memória.
/// Watts da GPU pela <c>nvml.dll</c> do driver NVIDIA, se ela existir. Ver docs/SYSTEM-INFO.md.
/// </summary>
internal sealed class WindowsMetrics : IMetricSource, IDisposable
{
    internal const string CpuUtility = @"\Processor Information(_Total)\% Processor Utility";
    internal const string EnergyMeter = @"\Energy Meter(*)\Power";
    internal const string GpuEngine = @"\GPU Engine(*)\Utilization Percentage";
    internal const string GpuMemory = @"\GPU Adapter Memory(*)\Dedicated Usage";
    internal const string DiskIdle = @"\PhysicalDisk(*)\% Idle Time";
    internal const string NetReceived = @"\Network Interface(*)\Bytes Received/sec";
    internal const string NetSent = @"\Network Interface(*)\Bytes Sent/sec";

    /// <summary>Os contadores com curinga só veem processos novos (GPU Engine) quando a consulta é refeita.</summary>
    private static readonly TimeSpan RebuildEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AdaptersEvery = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private PdhQuery? _pdh;
    private DateTime _builtAt;
    private List<GpuAdapter> _adapters = [];
    private DateTime _adaptersAt;
    private Lazy<Nvml?> _nvml = new(Nvml.TryLoad);

    private double? _cpu;
    private double? _cpuWatts;
    private Dictionary<long, double> _gpuUse = [];
    private Dictionary<long, double> _gpuMemory = [];
    private readonly Dictionary<long, D3dkmt.PerfData?> _gpuPerf = [];
    private IReadOnlyList<(string Instance, double Value)> _diskIdle = [];
    private IReadOnlyList<(string Instance, double Value)> _received = [];
    private IReadOnlyList<(string Instance, double Value)> _sent = [];
    private readonly HashSet<long> _perfRead = [];

    public bool IsAvailable(MetricKind kind)
    {
        lock (_gate)
        {
            EnsureQuery();
            return kind switch
            {
                MetricKind.CpuWatts => _pdh!.Has(EnergyMeter) && HasPackage(),
                MetricKind.GpuPercent => _pdh!.Has(GpuEngine) && Adapters().Count > 0,
                MetricKind.VramPercent => _pdh!.Has(GpuMemory) && Adapters().Count > 0,
                MetricKind.GpuTemperature or MetricKind.GpuPowerPercent =>
                    Adapters().Any(a => D3dkmt.Query(a.Luid) is not null),
                MetricKind.GpuWatts => _nvml.Value?.HasDevices == true,
                MetricKind.DiskActivity => _pdh!.Has(DiskIdle),
                MetricKind.NetworkDown or MetricKind.NetworkUp => _pdh!.Has(NetReceived),
                _ => true,
            };
        }
    }

    public IReadOnlyList<MetricDevice> GetDevices(MetricKind kind)
    {
        lock (_gate)
        {
            EnsureQuery();
            switch (kind)
            {
                case MetricKind.GpuPercent or MetricKind.VramPercent or MetricKind.GpuTemperature
                    or MetricKind.GpuPowerPercent or MetricKind.GpuWatts:
                    return Adapters().Select(a => new MetricDevice(a.CounterLuid, a.Name)).ToList();
                case MetricKind.DiskActivity:
                    _pdh!.Collect();
                    return _pdh.Values(DiskIdle).Select(v => v.Instance).Where(i => i != "_Total").Order()
                        .Select(i => new MetricDevice(i, "Disco " + i)).ToList();
                case MetricKind.NetworkDown or MetricKind.NetworkUp:
                    _pdh!.Collect();
                    return _pdh.Values(NetReceived).Select(v => v.Instance).Distinct().Order()
                        .Select(i => new MetricDevice(i, i)).ToList();
                case MetricKind.Ping:
                    return MetricCatalog.PingHosts;
                default:
                    return [];
            }
        }
    }

    public void Collect()
    {
        lock (_gate)
        {
            // Consulta refeita agora: as taxas ainda não têm duas leituras. Fica com os valores do ciclo anterior.
            if (EnsureQuery())
                return;
            var pdh = _pdh!;
            pdh.Collect();
            _cpu = pdh.Value(CpuUtility) is { } cpu ? Math.Clamp(cpu, 0, 100) : null; // passa de 100 com turbo
            var energy = pdh.Values(EnergyMeter).Where(v => IsPackage(v.Instance)).ToList();
            _cpuWatts = energy.Count > 0 ? energy.Sum(v => v.Value) / 1000.0 : null; // mW
            _gpuUse = GpuCounters.Utilization(pdh.Values(GpuEngine));
            _gpuMemory = GpuCounters.ByLuid(pdh.Values(GpuMemory));
            _diskIdle = pdh.Values(DiskIdle);
            _received = pdh.Values(NetReceived);
            _sent = pdh.Values(NetSent);
            _gpuPerf.Clear();
            _perfRead.Clear();
        }
    }

    public double? Read(MetricKey key)
    {
        lock (_gate)
        {
            switch (key.Kind)
            {
                case MetricKind.CpuPercent:
                    return _cpu;
                case MetricKind.CpuWatts:
                    return _cpuWatts;
                case MetricKind.RamPercent:
                    return MemoryLoad();
                case MetricKind.GpuPercent:
                    return Adapter(key.Device) is { } gpu ? _gpuUse.GetValueOrDefault(gpu.Luid) : null;
                case MetricKind.VramPercent:
                    return Adapter(key.Device) is { DedicatedMemory: > 0 } vram && _gpuMemory.TryGetValue(vram.Luid, out var used)
                        ? Math.Clamp(used * 100.0 / vram.DedicatedMemory, 0, 100)
                        : null;
                case MetricKind.GpuTemperature:
                    return Adapter(key.Device) is { } t ? Perf(t.Luid)?.TemperatureC : null;
                case MetricKind.GpuPowerPercent:
                    return Adapter(key.Device) is { } p ? Perf(p.Luid)?.PowerPercent : null;
                case MetricKind.GpuWatts:
                    return _nvml.Value?.Watts(Adapter(key.Device)?.Name);
                case MetricKind.DiskActivity:
                    return DiskActivity(_diskIdle, key.Device);
                case MetricKind.NetworkDown:
                    return Sum(_received, key.Device);
                case MetricKind.NetworkUp:
                    return Sum(_sent, key.Device);
                default:
                    return null;
            }
        }
    }

    public async Task<double?> PingAsync(string host, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(1), cancellationToken: cancellationToken).ConfigureAwait(false);
        return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
    }

    /// <summary>Atividade = 100 menos o tempo ocioso. Sem disco escolhido, o mais ocupado.</summary>
    internal static double? DiskActivity(IReadOnlyList<(string Instance, double Value)> idle, string? device)
    {
        var disks = idle.Where(v => v.Instance != "_Total").ToList();
        if (device is not null)
            disks = disks.Where(v => v.Instance == device).ToList();
        if (disks.Count == 0)
            return null;
        return Math.Clamp(100 - disks.Min(v => v.Value), 0, 100);
    }

    /// <summary>Uma placa de rede, ou a soma de todas.</summary>
    internal static double? Sum(IReadOnlyList<(string Instance, double Value)> values, string? device)
    {
        if (values.Count == 0)
            return null;
        if (device is null)
            return values.Sum(v => v.Value);
        var match = values.Where(v => v.Instance == device).ToList();
        return match.Count == 0 ? null : match.Sum(v => v.Value);
    }

    /// <summary>Energia é taxa: precisa de duas leituras para a instância vir com valor.</summary>
    private bool HasPackage()
    {
        _pdh!.Collect();
        return _pdh.Values(EnergyMeter).Any(v => IsPackage(v.Instance));
    }

    /// <summary>Só o pacote ("RAPL_Package0_PKG"); os núcleos já estão dentro dele e o _Total vem zerado.</summary>
    internal static bool IsPackage(string instance) => instance.EndsWith("_PKG", StringComparison.OrdinalIgnoreCase);

    private D3dkmt.PerfData? Perf(long luid)
    {
        if (_perfRead.Add(luid))
            _gpuPerf[luid] = D3dkmt.Query(luid);
        return _gpuPerf.GetValueOrDefault(luid);
    }

    private GpuAdapter? Adapter(string? device)
    {
        var list = Adapters();
        return list.FirstOrDefault(a => a.CounterLuid == device)
            ?? list.OrderByDescending(a => a.DedicatedMemory).FirstOrDefault();
    }

    private List<GpuAdapter> Adapters()
    {
        if (DateTime.UtcNow - _adaptersAt < AdaptersEvery)
            return _adapters;
        _adaptersAt = DateTime.UtcNow;
        try
        {
            _pdh!.Collect();
            var withCounters = GpuCounters.ByLuid(_pdh.Values(GpuMemory)).Keys.ToHashSet();
            _adapters = GpuCounters.Distinct(Dxgi.Adapters(), withCounters);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            _adapters = [];
        }
        return _adapters;
    }

    /// <summary>Monta (ou refaz) a consulta. <c>true</c> = acabou de montar.</summary>
    private bool EnsureQuery()
    {
        if (_pdh is not null && DateTime.UtcNow - _builtAt < RebuildEvery)
            return false;
        _pdh?.Dispose();
        var pdh = new PdhQuery();
        foreach (var path in new[] { CpuUtility, EnergyMeter, GpuEngine, GpuMemory, DiskIdle, NetReceived, NetSent })
            pdh.Add(path);
        // Contadores de taxa precisam de duas leituras: a primeira só arma.
        pdh.Collect();
        _pdh = pdh;
        _builtAt = DateTime.UtcNow;
        return true;
    }

    private static double? MemoryLoad()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return null;
        return (status.ullTotalPhys - status.ullAvailPhys) * 100.0 / status.ullTotalPhys;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _pdh?.Dispose();
            _pdh = null;
            if (_nvml.IsValueCreated)
                _nvml.Value?.Dispose();
            _nvml = new(Nvml.TryLoad);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
