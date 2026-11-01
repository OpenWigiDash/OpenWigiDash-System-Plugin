using System.Text.RegularExpressions;

namespace OpenWigiDash.SystemPlugin.Audio;

/// <summary>Saída (alto-falantes, fones) ou entrada (microfones).</summary>
public enum AudioFlow
{
    Output,
    Input,
}

/// <summary>Qual padrão do Windows trocar.</summary>
public enum AudioRoles
{
    /// <summary>O "dispositivo padrão" (jogos, música, sistema).</summary>
    Default,
    /// <summary>O "dispositivo de comunicação padrão" (Discord, Teams, chamadas).</summary>
    Communications,
    /// <summary>Os dois de uma vez.</summary>
    Both,
}

/// <summary>Como reconhecer um dispositivo salvo na lista.</summary>
public enum DeviceMatching
{
    /// <summary>Só pelo id do Windows.</summary>
    Exact,
    /// <summary>Pelo id e, se ele mudou, pelo nome sem o prefixo "2- " que o Windows põe quando o dongle troca de porta.</summary>
    Fuzzy,
}

/// <summary>Um dispositivo de áudio como o Windows mostra agora.</summary>
/// <param name="Name">Nome completo, ex.: "Microfone do Headset (2- JBL Quantum 800)".</param>
/// <param name="Description">Nome do dispositivo sem o adaptador, ex.: "Microfone do Headset".</param>
/// <param name="Interface">Adaptador, ex.: "2- JBL Quantum 800".</param>
/// <param name="Active">Ligado e disponível (não desconectado nem desativado).</param>
public sealed record AudioDevice(string Id, string Name, string Description, string Interface, bool Active);

/// <summary>Um item da lista do carrossel, como fica salvo na instância.</summary>
/// <param name="Glyph">Ícone escolhido (<see cref="AudioGlyphs"/>); <c>null</c> = automático pelo nome.</param>
/// <param name="ImageFileId">Imagem própria importada pelo <c>IWidgetFiles</c>, no lugar do ícone.</param>
public sealed record AudioDeviceEntry(string Id, string Name, string Description, string Interface,
    string? Glyph = null, string? ImageFileId = null)
{
    public static AudioDeviceEntry From(AudioDevice device) =>
        new(device.Id, device.Name, device.Description, device.Interface);
}

/// <summary>Acha os dispositivos salvos entre os atuais e decide o próximo do carrossel. Sem Windows: os testes chamam direto.</summary>
public static class DeviceMatcher
{
    // "2- JBL Quantum 800", "(2- JBL Quantum 800)": o número que o Windows põe quando cria outro endpoint igual.
    private static readonly Regex Counter = new(@"(^|\()\s*\d+\s*-\s*", RegexOptions.CultureInvariant);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var text = Counter.Replace(name.Trim(), "$1");
        return Spaces.Replace(text, " ").Trim().ToLowerInvariant();
    }

    /// <summary>O dispositivo atual que corresponde ao item, ou <c>null</c> se ele não está ligado agora.</summary>
    public static AudioDevice? Find(AudioDeviceEntry entry, IReadOnlyList<AudioDevice> devices, DeviceMatching matching)
    {
        var byId = devices.FirstOrDefault(d => string.Equals(d.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (byId is { Active: true })
            return byId;
        if (matching == DeviceMatching.Exact)
            return null;

        var name = Normalize(entry.Name);
        var adapter = Normalize(entry.Interface);
        var active = devices.Where(d => d.Active).ToList();

        var sameName = active.Where(d => Normalize(d.Name) == name).ToList();
        if (sameName.Count > 0)
            return sameName.FirstOrDefault(d => Normalize(d.Interface) == adapter) ?? sameName[0];

        // O nome completo mudou mais que o prefixo: mesmo tipo de dispositivo no mesmo adaptador.
        var description = Normalize(entry.Description);
        return active.FirstOrDefault(d => Normalize(d.Interface) == adapter && adapter.Length > 0
            && Normalize(d.Description) == description && description.Length > 0);
    }

    /// <summary>Posição na lista do dispositivo padrão de agora, ou -1 se ele não está na lista.</summary>
    public static int CurrentIndex(IReadOnlyList<AudioDeviceEntry> entries, string? defaultId,
        IReadOnlyList<AudioDevice> devices, DeviceMatching matching)
    {
        if (defaultId is null)
            return -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(Find(entries[i], devices, matching)?.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Próximo item ligado depois do atual, dando a volta (carrossel). Pula o que não está ligado e o que é o próprio
    /// padrão de agora. <c>null</c> se não há outro para ir.
    /// </summary>
    public static int? NextIndex(IReadOnlyList<AudioDeviceEntry> entries, string? defaultId,
        IReadOnlyList<AudioDevice> devices, DeviceMatching matching)
    {
        if (entries.Count == 0)
            return null;
        var current = CurrentIndex(entries, defaultId, devices, matching);
        for (var step = 1; step <= entries.Count; step++)
        {
            var i = ((current < 0 ? -1 : current) + step) % entries.Count;
            var device = Find(entries[i], devices, matching);
            if (device is not null && !string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return null;
    }

    /// <summary>Primeiro item ligado (long-press volta para ele).</summary>
    public static int? FirstAvailable(IReadOnlyList<AudioDeviceEntry> entries, IReadOnlyList<AudioDevice> devices, DeviceMatching matching)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (Find(entries[i], devices, matching) is not null)
                return i;
        }
        return null;
    }
}
