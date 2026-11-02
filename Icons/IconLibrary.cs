namespace OpenWigiDash.SystemPlugin.Icons;

/// <summary>Um ícone da biblioteca: glifo da fonte de ícones do Windows e, se combinado, um segundo glifo no canto.</summary>
/// <param name="Accent">Segundo glifo, pequeno no canto inferior direito (ex.: microfone sobre a webcam).</param>
public sealed record IconOption(string Key, string Name, string Category, string Glyph, string? Accent = null);

/// <summary>
/// Biblioteca de ícones do plugin, por categoria, para qualquer widget. Glifos da Segoe Fluent Icons (Segoe MDL2 Assets no
/// Windows 10). Vai para o SDK (coleção do host, com a janela de escolha no Manager); até lá, é esta.
/// </summary>
public static class IconLibrary
{
    public static class Categories
    {
        public const string Audio = "Áudio";
        public const string Video = "Vídeo";
        public const string Devices = "Dispositivos";
        public const string Communication = "Comunicação";
        public const string System = "Sistema";
    }

    public static IReadOnlyList<IconOption> All { get; } =
    [
        // Áudio
        new("speaker", "Alto-falante", Categories.Audio, "\uE7F5"),
        new("headphones", "Fone de ouvido", Categories.Audio, "\uE7F6"),
        new("headset", "Headset", Categories.Audio, "\uE95B"),
        new("microphone", "Microfone", Categories.Audio, "\uE720"),
        new("volume", "Volume", Categories.Audio, "\uE767"),
        new("volume-high", "Volume alto", Categories.Audio, "\uE995"),
        new("volume-low", "Volume baixo", Categories.Audio, "\uE993"),
        new("mute", "Mudo", Categories.Audio, "\uE74F"),
        new("music", "Música", Categories.Audio, "\uE8D6"),
        // Vídeo
        new("camera", "Webcam", Categories.Video, "\uE714"),
        new("webcam-mic", "Webcam + microfone", Categories.Video, "\uE714", "\uE720"),
        new("monitor", "Monitor / TV", Categories.Video, "\uE7F4"),
        new("play", "Tocar", Categories.Video, "\uE768"),
        new("pause", "Pausar", Categories.Video, "\uE769"),
        // Dispositivos
        new("bluetooth", "Bluetooth", Categories.Devices, "\uE702"),
        new("game", "Controle", Categories.Devices, "\uE7FC"),
        new("laptop", "Notebook", Categories.Devices, "\uE7F8"),
        new("phone", "Telefone", Categories.Devices, "\uE717"),
        new("keyboard", "Teclado", Categories.Devices, "\uE765"),
        new("mouse", "Mouse", Categories.Devices, "\uE962"),
        new("printer", "Impressora", Categories.Devices, "\uE749"),
        // Comunicação
        new("chat", "Chat", Categories.Communication, "\uE8BD"),
        new("mail", "E-mail", Categories.Communication, "\uE715"),
        new("people", "Pessoas", Categories.Communication, "\uE716"),
        // Sistema
        new("settings", "Configurações", Categories.System, "\uE713"),
        new("power", "Desligar", Categories.System, "\uE7E8"),
        new("lock", "Bloquear", Categories.System, "\uE72E"),
        new("home", "Início", Categories.System, "\uE80F"),
        new("wifi", "Wi-Fi", Categories.System, "\uE701"),
    ];

    /// <summary>Categorias na ordem da lista.</summary>
    public static IReadOnlyList<string> CategoryNames { get; } = All.Select(i => i.Category).Distinct().ToList();

    public static IconOption? ByKey(string? key) =>
        key is null ? null : All.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Filtra por categoria (<c>null</c> = todas) e por texto no nome ou na chave.</summary>
    public static IEnumerable<IconOption> Search(string? category, string? text) =>
        All.Where(i => (category is null || i.Category == category)
            && (string.IsNullOrWhiteSpace(text)
                || i.Name.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || i.Key.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)));
}
