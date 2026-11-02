using System.Runtime.InteropServices;

namespace OpenWigiDash.SystemPlugin.Metrics;

/// <summary>
/// Contadores de desempenho do Windows (PDH). Sempre pelo nome em inglês (<c>PdhAddEnglishCounterW</c>): o nome
/// traduzido muda com o idioma do Windows.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x0;
    private const uint PDH_CSTATUS_NEW_DATA = 0x1;

    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = [];

    public PdhQuery()
    {
        Check(PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
    }

    /// <summary>Adiciona o contador; <c>false</c> se ele não existe neste PC (ex.: Energy Meter sem RAPL).</summary>
    public bool Add(string path)
    {
        if (_counters.ContainsKey(path))
            return true;
        if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) != 0)
            return false;
        _counters[path] = counter;
        return true;
    }

    public bool Has(string path) => _counters.ContainsKey(path);

    public void Collect() => PdhCollectQueryData(_query);

    /// <summary>Valor de um contador sem curinga.</summary>
    public double? Value(string path)
    {
        if (!_counters.TryGetValue(path, out var counter))
            return null;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) != 0)
            return null;
        return value.CStatus is PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA ? value.Value : null;
    }

    /// <summary>Valores de um contador com curinga (<c>(*)</c>), por instância.</summary>
    public IReadOnlyList<(string Instance, double Value)> Values(string path)
    {
        if (!_counters.TryGetValue(path, out var counter))
            return [];
        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0)
            return [];
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buffer) != 0)
                return [];
            var list = new List<(string, double)>((int)count);
            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.Value.CStatus is not (PDH_CSTATUS_VALID_DATA or PDH_CSTATUS_NEW_DATA))
                    continue;
                list.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value));
            }
            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private static void Check(uint status, string call)
    {
        if (status != 0)
            throw new InvalidOperationException($"{call} falhou: 0x{status:X8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE
    {
        public uint CStatus;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr Name;
        public PDH_FMT_COUNTERVALUE Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr items);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
