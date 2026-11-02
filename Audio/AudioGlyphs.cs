using OpenWigiDash.SystemPlugin.Icons;

namespace OpenWigiDash.SystemPlugin.Audio;

/// <summary>Ícone de cada dispositivo de áudio: o escolhido na <see cref="IconLibrary"/>, ou um palpite pelo nome.</summary>
public static class AudioGlyphs
{
    public static IReadOnlyList<IconOption> All => IconLibrary.All;

    public static IconOption? ByKey(string? key) => IconLibrary.ByKey(key);

    /// <summary>Ícone escolhido, ou um palpite pelo nome do dispositivo.</summary>
    public static IconOption For(string? key, string? deviceName, AudioFlow flow)
    {
        if (ByKey(key) is { } chosen)
            return chosen;
        return ByKey(Guess(deviceName, flow))!;
    }

    public static string Guess(string? deviceName, AudioFlow flow)
    {
        var name = (deviceName ?? "").ToLowerInvariant();
        if (ContainsAny(name, "webcam", "camera", "câmera", "c920", "c922", "brio", "cam "))
            return flow == AudioFlow.Input ? "webcam-mic" : "camera";
        if (ContainsAny(name, "headset", "quantum", "arctis", "hyperx", "cloud", "kraken", "astro"))
            return "headset";
        // "Microfone" contém "fone": o microfone vem antes do fone de ouvido.
        if (flow == AudioFlow.Input || ContainsAny(name, "microfone", "microphone", "mic "))
            return "microphone";
        if (ContainsAny(name, "fone", "headphone", "earphone", "airpods", "buds"))
            return "headphones";
        if (ContainsAny(name, "hdmi", "displayport", "nvidia", "monitor", " tv", "ac10", "lg ", "samsung"))
            return "monitor";
        return "speaker";
    }

    private static bool ContainsAny(string text, params string[] parts) => parts.Any(text.Contains);
}
