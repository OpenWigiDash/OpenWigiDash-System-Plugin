namespace OpenWigiDash.SystemPlugin.Audio;

/// <summary>
/// Ícones para os dispositivos, da fonte de ícones do Windows (Segoe Fluent Icons; Segoe MDL2 Assets no Windows 10).
/// Enquanto o SDK não tem uma coleção de ícones do host, é esta lista.
/// </summary>
public static class AudioGlyphs
{
    public sealed record GlyphOption(string Key, string Name, string Glyph);

    public static IReadOnlyList<GlyphOption> All { get; } =
    [
        new("speaker", "Alto-falante", ""),
        new("headphones", "Fone de ouvido", ""),
        new("headset", "Headset", ""),
        new("microphone", "Microfone", ""),
        new("camera", "Webcam", ""),
        new("monitor", "Monitor / TV", ""),
        new("volume", "Volume", ""),
        new("bluetooth", "Bluetooth", ""),
        new("game", "Controle", ""),
        new("music", "Música", ""),
        new("phone", "Telefone", ""),
        new("laptop", "Notebook", ""),
        new("volume-high", "Volume alto", ""),
        new("volume-low", "Volume baixo", ""),
        new("mute", "Mudo", ""),
    ];

    public static GlyphOption? ByKey(string? key) =>
        key is null ? null : All.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ícone escolhido, ou um palpite pelo nome do dispositivo.</summary>
    public static GlyphOption For(string? key, string? deviceName, AudioFlow flow)
    {
        if (ByKey(key) is { } chosen)
            return chosen;
        return ByKey(Guess(deviceName, flow))!;
    }

    public static string Guess(string? deviceName, AudioFlow flow)
    {
        var name = (deviceName ?? "").ToLowerInvariant();
        if (ContainsAny(name, "webcam", "camera", "câmera", "c920", "c922", "brio", "cam "))
            return "camera";
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
