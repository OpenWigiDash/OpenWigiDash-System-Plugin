using System.Runtime.InteropServices;
using OpenWigiDash.SystemPlugin.Apps;

namespace OpenWigiDash.SystemPlugin.Audio;

/// <summary>
/// Core Audio do Windows (MMDevice) para listar e acompanhar os dispositivos, e a <c>IPolicyConfig</c> (não
/// documentada, a mesma do EarTrumpet, SoundSwitch e dos plugins do Stream Deck) para trocar o padrão.
/// </summary>
internal static class CoreAudio
{
    public enum EDataFlow
    {
        Render = 0,
        Capture = 1,
    }

    public enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2,
    }

    public const uint DEVICE_STATE_ACTIVE = 0x1;
    public const uint DEVICE_STATE_UNPLUGGED = 0x8;
    public const uint DEVICE_STATE_DISABLED = 0x2;

    public static readonly Native.PropertyKey PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    public static readonly Native.PropertyKey PKEY_Device_DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    public static readonly Native.PropertyKey PKEY_DeviceInterface_FriendlyName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(OpenWigiDash.SystemPlugin.Audio.IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(OpenWigiDash.SystemPlugin.Audio.IMMNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(uint access, out Native.IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    public class PolicyConfigClient;

    /// <summary>Só <see cref="SetDefaultEndpoint"/> é usado; os outros estão aqui para a ordem da vtable.</summary>
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaultPeriod, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    public static string? GetString(Native.IPropertyStore store, Native.PropertyKey key)
    {
        if (store.GetValue(key, out var pv) != 0)
            return null;
        try
        {
            return pv.Vt == Native.VT_LPWSTR && pv.Value != IntPtr.Zero ? Marshal.PtrToStringUni(pv.Value) : null;
        }
        finally
        {
            Native.PropVariantClear(ref pv);
        }
    }
}

/// <summary>Chave de propriedade como chega no aviso do Windows.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct AudioPropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>
/// Avisos de mudança de dispositivo do Core Audio. Público de propósito: o Windows chama de volta um objeto .NET por
/// esta interface, e o COM só enxerga tipos públicos.
/// </summary>
[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, AudioPropertyKey key);
}

/// <summary>Repassa os avisos do Windows para o <see cref="AudioService"/>.</summary>
[ComVisible(true)]
public sealed class AudioNotificationSink : IMMNotificationClient
{
    private readonly Action<bool> _changed;

    internal AudioNotificationSink(Action<bool> changed) => _changed = changed;

    public void OnDeviceStateChanged(string deviceId, uint newState) => _changed(true);
    public void OnDeviceAdded(string deviceId) => _changed(true);
    public void OnDeviceRemoved(string deviceId) => _changed(true);
    public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId) => _changed(true);

    // Só o nome (renomeado no Painel de Som) interessa; as outras propriedades mudam o tempo todo.
    public void OnPropertyValueChanged(string deviceId, AudioPropertyKey key) =>
        _changed(key.FormatId == new Guid("a45c254e-df1c-4efd-8020-67d146a850e0") && key.PropertyId == 14);
}
