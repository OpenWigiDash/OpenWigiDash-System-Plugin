using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OpenWigiDash.SystemPlugin.Audio;

/// <summary>O que a instância pede ao Windows sobre áudio. Os testes trocam por um falso.</summary>
internal interface IAudioPlatform
{
    IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow);
    string? GetDefaultId(AudioFlow flow, AudioRoles roles);
    void SetDefault(string deviceId, AudioRoles roles);

    /// <summary>Algum dispositivo entrou, saiu ou o padrão mudou (inclusive por fora do widget).</summary>
    event Action? Changed;
}

/// <summary>
/// Core Audio para todas as instâncias: uma lista em cache por direção, refeita quando o Windows avisa que algo
/// mudou (<c>IMMNotificationClient</c>).
/// </summary>
internal sealed class AudioService : IAudioPlatform
{
    public static AudioService Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<AudioFlow, IReadOnlyList<AudioDevice>> _cache = [];
    private CoreAudio.IMMDeviceEnumerator? _enumerator;
    private ILogger? _logger;
    private Timer? _debounce;
    private AudioNotificationSink? _sink;

    public event Action? Changed;

    public void UseLogger(ILoggerFactory factory) => _logger ??= factory.CreateLogger("OpenWigiDash.System.Audio");

    private CoreAudio.IMMDeviceEnumerator Enumerator
    {
        get
        {
            lock (_gate)
            {
                if (_enumerator is null)
                {
                    _enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumerator();
                    _sink = new AudioNotificationSink(relevant =>
                    {
                        if (relevant)
                            Invalidate();
                    });
                    var hr = _enumerator.RegisterEndpointNotificationCallback(_sink);
                    if (hr != 0)
                        _logger?.LogWarning("Não deu para acompanhar as mudanças de áudio (0x{Hr:X8}).", hr);
                }
                return _enumerator;
            }
        }
    }

    public IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(flow, out var cached))
                return cached;
        }
        var list = Read(flow);
        lock (_gate)
            _cache[flow] = list;
        return list;
    }

    private IReadOnlyList<AudioDevice> Read(AudioFlow flow)
    {
        var devices = new List<AudioDevice>();
        if (Enumerator.EnumAudioEndpoints(ToDataFlow(flow), CoreAudio.DEVICE_STATE_ACTIVE | CoreAudio.DEVICE_STATE_UNPLUGGED, out var collection) != 0)
            return devices;
        try
        {
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0)
                    continue;
                try
                {
                    if (device.GetId(out var id) != 0 || device.GetState(out var state) != 0)
                        continue;
                    string? name = null, description = null, adapter = null;
                    if (device.OpenPropertyStore(0, out var store) == 0)
                    {
                        try
                        {
                            name = CoreAudio.GetString(store, CoreAudio.PKEY_Device_FriendlyName);
                            description = CoreAudio.GetString(store, CoreAudio.PKEY_Device_DeviceDesc);
                            adapter = CoreAudio.GetString(store, CoreAudio.PKEY_DeviceInterface_FriendlyName);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(store);
                        }
                    }
                    devices.Add(new AudioDevice(id, name ?? description ?? id, description ?? "", adapter ?? "",
                        (state & CoreAudio.DEVICE_STATE_ACTIVE) != 0));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
        return devices.OrderByDescending(d => d.Active).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string? GetDefaultId(AudioFlow flow, AudioRoles roles)
    {
        var role = roles == AudioRoles.Communications ? CoreAudio.ERole.Communications : CoreAudio.ERole.Console;
        if (Enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), role, out var device) != 0)
            return null;
        try
        {
            return device.GetId(out var id) == 0 ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public void SetDefault(string deviceId, AudioRoles roles)
    {
        var policy = (CoreAudio.IPolicyConfig)new CoreAudio.PolicyConfigClient();
        try
        {
            if (roles is AudioRoles.Default or AudioRoles.Both)
            {
                Check(policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Console));
                Check(policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Multimedia));
            }
            if (roles is AudioRoles.Communications or AudioRoles.Both)
                Check(policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Communications));
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
        Invalidate();

        static void Check(int hr)
        {
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
        }
    }

    private static CoreAudio.EDataFlow ToDataFlow(AudioFlow flow) =>
        flow == AudioFlow.Input ? CoreAudio.EDataFlow.Capture : CoreAudio.EDataFlow.Render;

    /// <summary>Joga o cache fora e avisa as instâncias, juntando avisos seguidos (o Windows manda vários por troca).</summary>
    private void Invalidate()
    {
        lock (_gate)
        {
            _cache.Clear();
            _debounce ??= new Timer(_ => Changed?.Invoke());
            _debounce.Change(150, Timeout.Infinite);
        }
    }
}
