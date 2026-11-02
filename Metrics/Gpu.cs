using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>Uma placa de vídeo: nome e LUID pelo DXGI, que é como os contadores e o D3DKMT a chamam.</summary>
internal sealed record GpuAdapter(long Luid, string Name, ulong DedicatedMemory, uint VendorId)
{
    /// <summary>Como o LUID aparece nos nomes de instância dos contadores ("luid_0x00000000_0x00010702").</summary>
    public string CounterLuid => GpuCounters.FormatLuid(Luid);
}

/// <summary>Leitura dos nomes de instância dos contadores de GPU.</summary>
internal static partial class GpuCounters
{
    [GeneratedRegex(@"luid_0x([0-9a-fA-F]{8})_0x([0-9a-fA-F]{8})", RegexOptions.CultureInvariant)]
    private static partial Regex LuidPattern();

    [GeneratedRegex(@"engtype_(\w+)$", RegexOptions.CultureInvariant)]
    private static partial Regex EngineTypePattern();

    public static string FormatLuid(long luid) =>
        $"luid_0x{(uint)(luid >> 32):X8}_0x{(uint)(luid & 0xFFFFFFFF):X8}";

    /// <summary>LUID na instância ("pid_1_luid_0x00000000_0x00010702_phys_0_eng_0_engtype_3D").</summary>
    public static long? ParseLuid(string instance)
    {
        var m = LuidPattern().Match(instance);
        if (!m.Success)
            return null;
        var high = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var low = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((long)high << 32) | low;
    }

    /// <summary>
    /// Uso da GPU como no Gerenciador de Tarefas: soma os processos por tipo de engine (3D, Copy, VideoDecode...) e
    /// fica com o tipo mais ocupado. Por LUID.
    /// </summary>
    public static Dictionary<long, double> Utilization(IEnumerable<(string Instance, double Value)> engines)
    {
        var sums = new Dictionary<(long, string), double>();
        foreach (var (instance, value) in engines)
        {
            if (ParseLuid(instance) is not { } luid)
                continue;
            var type = EngineTypePattern().Match(instance) is { Success: true } m ? m.Groups[1].Value : "";
            sums[(luid, type)] = sums.GetValueOrDefault((luid, type)) + value;
        }
        var result = new Dictionary<long, double>();
        foreach (var ((luid, _), sum) in sums)
            result[luid] = Math.Min(100, Math.Max(result.GetValueOrDefault(luid), sum));
        return result;
    }

    /// <summary>Memória dedicada em uso por LUID ("luid_..._phys_0", em bytes).</summary>
    public static Dictionary<long, double> ByLuid(IEnumerable<(string Instance, double Value)> values)
    {
        var result = new Dictionary<long, double>();
        foreach (var (instance, value) in values)
        {
            if (ParseLuid(instance) is { } luid)
                result[luid] = result.GetValueOrDefault(luid) + value;
        }
        return result;
    }

    /// <summary>
    /// Tira as placas repetidas e as de software. A mesma GPU pode aparecer duas vezes (o SuperDisplay cria um
    /// adaptador virtual com os mesmos dados): fica a que tem contador de memória; sem contadores, a primeira de cada nome.
    /// </summary>
    public static List<GpuAdapter> Distinct(IEnumerable<GpuAdapter> adapters, IReadOnlySet<long> withCounters)
    {
        var list = adapters.Where(a => a.VendorId != 0x1414).ToList(); // Microsoft Basic Render Driver
        var result = new List<GpuAdapter>();
        foreach (var group in list.GroupBy(a => (a.Name, a.DedicatedMemory)))
        {
            var pick = group.FirstOrDefault(a => withCounters.Contains(a.Luid));
            if (pick is null && withCounters.Count == 0)
                pick = group.First();
            if (pick is not null)
                result.Add(pick);
        }
        return result;
    }
}

/// <summary>Lista as placas pelo DXGI.</summary>
internal static class Dxgi
{
    public static List<GpuAdapter> Adapters()
    {
        var result = new List<GpuAdapter>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var obj) != 0 || obj is not IDXGIFactory1 factory)
            return result;
        try
        {
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
            {
                try
                {
                    if (adapter.GetDesc1(out var desc) != 0 || (desc.Flags & 2) != 0) // DXGI_ADAPTER_FLAG_SOFTWARE
                        continue;
                    var luid = ((long)desc.LuidHigh << 32) | desc.LuidLow;
                    result.Add(new GpuAdapter(luid, desc.Description.TrimEnd('\0').Trim(), (ulong)desc.DedicatedVideoMemory, desc.VendorId));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return result;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IDXGIObject
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // IDXGIFactory
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();
        // IDXGIFactory1
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        [PreserveSig] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // IDXGIObject
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        // IDXGIAdapter
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();
        // IDXGIAdapter1
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}

/// <summary>
/// Dados de desempenho da placa pelo kernel gráfico (o que o Gerenciador de Tarefas mostra na aba GPU): temperatura
/// e potência em % do limite. Precisa de driver WDDM 2.9+ (NVIDIA, AMD e Intel recentes).
/// </summary>
internal static class D3dkmt
{
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    public readonly record struct PerfData(double TemperatureC, double PowerPercent, uint FanRpm);

    public static PerfData? Query(long luid)
    {
        var open = new D3DKMT_OPENADAPTERFROMLUID { LuidLow = (uint)(luid & 0xFFFFFFFF), LuidHigh = (int)(luid >> 32) };
        if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
            return null;
        try
        {
            var perf = new D3DKMT_ADAPTER_PERFDATA();
            var size = Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(perf, buffer, false);
                var query = new D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = open.hAdapter,
                    Type = KMTQAITYPE_ADAPTERPERFDATA,
                    pPrivateDriverData = buffer,
                    PrivateDriverDataSize = (uint)size,
                };
                if (D3DKMTQueryAdapterInfo(ref query) != 0)
                    return null;
                perf = Marshal.PtrToStructure<D3DKMT_ADAPTER_PERFDATA>(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            if (perf.Temperature == 0 && perf.Power == 0)
                return null; // driver sem suporte: tudo zero
            return new PerfData(perf.Temperature / 10.0, perf.Power / 10.0, perf.FanRPM);
        }
        finally
        {
            var close = new D3DKMT_CLOSEADAPTER { hAdapter = open.hAdapter };
            D3DKMTCloseAdapter(ref close);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID
    {
        public uint LuidLow;
        public int LuidHigh;
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID open);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER close);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO query);
}

/// <summary>
/// Watts da GPU NVIDIA pela <c>nvml.dll</c> que vem com o driver (em System32). Nada vai junto no plugin: sem a DLL,
/// a métrica some da lista.
/// </summary>
internal sealed class Nvml : IDisposable
{
    private readonly IntPtr _library;
    private readonly Dictionary<string, IntPtr> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly GetPowerUsage _power;

    private Nvml(IntPtr library, GetPowerUsage power)
    {
        _library = library;
        _power = power;
    }

    public static Nvml? TryLoad()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string[] candidates =
        [
            Path.Combine(system, "nvml.dll"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        ];
        foreach (var path in candidates)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
                continue;
            try
            {
                var init = Marshal.GetDelegateForFunctionPointer<Init>(NativeLibrary.GetExport(library, "nvmlInit_v2"));
                if (init() != 0)
                {
                    NativeLibrary.Free(library);
                    continue;
                }
                var nvml = new Nvml(library, Marshal.GetDelegateForFunctionPointer<GetPowerUsage>(NativeLibrary.GetExport(library, "nvmlDeviceGetPowerUsage")));
                nvml.LoadDevices();
                return nvml;
            }
            catch (EntryPointNotFoundException)
            {
                NativeLibrary.Free(library);
            }
        }
        return null;
    }

    private void LoadDevices()
    {
        var count = Marshal.GetDelegateForFunctionPointer<GetCount>(NativeLibrary.GetExport(_library, "nvmlDeviceGetCount_v2"));
        var byIndex = Marshal.GetDelegateForFunctionPointer<GetHandle>(NativeLibrary.GetExport(_library, "nvmlDeviceGetHandleByIndex_v2"));
        var name = Marshal.GetDelegateForFunctionPointer<GetName>(NativeLibrary.GetExport(_library, "nvmlDeviceGetName"));
        if (count(out var n) != 0)
            return;
        var buffer = new byte[96];
        for (uint i = 0; i < n; i++)
        {
            if (byIndex(i, out var device) != 0)
                continue;
            var text = name(device, buffer, (uint)buffer.Length) == 0
                ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0')
                : "";
            _devices.TryAdd(text, device);
        }
    }

    public bool HasDevices => _devices.Count > 0;

    /// <summary>Watts da placa com o nome dado pelo DXGI (ou da primeira, sem nome).</summary>
    public double? Watts(string? adapterName)
    {
        IntPtr device;
        if (adapterName is not null && _devices.TryGetValue(adapterName, out var match))
            device = match;
        else if (adapterName is not null && _devices.FirstOrDefault(d => adapterName.Contains(d.Key, StringComparison.OrdinalIgnoreCase)
                     || d.Key.Contains(adapterName, StringComparison.OrdinalIgnoreCase)) is { Value: var close } && close != IntPtr.Zero)
            device = close;
        else if (_devices.Count > 0)
            device = _devices.Values.First();
        else
            return null;
        return _power(device, out var milliwatts) == 0 ? milliwatts / 1000.0 : null;
    }

    public void Dispose()
    {
        try
        {
            Marshal.GetDelegateForFunctionPointer<Init>(NativeLibrary.GetExport(_library, "nvmlShutdown"))();
        }
        catch (EntryPointNotFoundException)
        {
        }
        NativeLibrary.Free(_library);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Init();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCount(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetHandle(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetName(IntPtr device, byte[] name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetPowerUsage(IntPtr device, out uint milliwatts);
}
