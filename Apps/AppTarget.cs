using System.Text.RegularExpressions;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// O app escolhido, como fica salvo na instância. <see cref="Id"/> é o nome do item em <c>shell:AppsFolder</c>
/// (o mesmo da lista "Todos os apps" do Iniciar): o AUMID de um app da Store (<c>Familia_hash!App</c>), o AUMID de
/// um atalho (<c>com.squirrel.Discord.Discord</c>) ou um caminho com pasta conhecida
/// (<c>{6D809377-...}\Steam\steam.exe</c>).
/// </summary>
/// <remarks>
/// Não guardamos o caminho do exe. Apps Electron (Squirrel) instalam cada versão em <c>app-1.2.3\</c> e apagam a
/// anterior; quem guarda esse caminho fica com o ícone quebrado e o botão morto depois da atualização. O id do
/// Iniciar continua o mesmo, e o shell resolve o alvo atual toda vez que perguntamos.
/// </remarks>
public sealed record AppChoice(string Id, string Name);

/// <summary>
/// O que o shell disse sobre um <see cref="AppChoice"/> agora há pouco. Resolvido de novo ao carregar a instância,
/// ao acordar e de tempos em tempos, nunca salvo.
/// </summary>
/// <param name="Aumid">AppUserModelID (das janelas e da barra de tarefas), quando o app tem um.</param>
/// <param name="ExePath">Exe que o atalho abre hoje, quando é um app comum.</param>
/// <param name="ProcessName">Nome do processo que fica aberto (sem .exe). No Squirrel não é o Update.exe do atalho.</param>
/// <param name="InstallDirectory">
/// Pasta onde procurar o processo aberto, quando ele não é o exe do atalho: a do Update.exe no Squirrel, ou a do exe
/// num launcher que abre outro exe ao lado (EA app).
/// </param>
/// <param name="LinkArguments">
/// Argumentos que o atalho do Iniciar já passa ao <see cref="ExePath"/> (ex.: <c>--processStart Discord.exe</c>). Quem
/// abre o exe direto, para acrescentar argumentos, precisa mandar estes também.
/// </param>
public sealed record AppTarget(
    string Id,
    string Name,
    bool IsPackaged,
    string? Aumid,
    string? ExePath,
    string? ProcessName,
    string? InstallDirectory,
    string? LinkArguments = null)
{
    private static readonly Regex PackagedId = new(@"^[^\\!]+_[a-z0-9]{13}![^\\]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KnownFolderId = new(@"^(\{[0-9a-f\-]{36}\})\\(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SquirrelStart = new(@"--processStart(?:AndWait)?\s+(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsPackagedId(string id) => PackagedId.IsMatch(id);

    /// <summary>
    /// Monta o alvo a partir do que o shell respondeu. Sem acesso ao Windows: <paramref name="knownFolder"/> traduz o
    /// GUID de uma pasta conhecida no caminho dela (nos testes, um dicionário).
    /// </summary>
    public static AppTarget Build(string id, string name, string? aumid, string? linkTarget, string? linkArguments,
        Func<Guid, string?> knownFolder)
    {
        if (IsPackagedId(id))
            return new AppTarget(id, name, true, string.IsNullOrEmpty(aumid) ? id : aumid, null, null, null);

        var exe = IsExe(linkTarget) ? linkTarget : null;
        if (exe is null && KnownFolderId.Match(id) is { Success: true } known
            && Guid.TryParse(known.Groups[1].Value, out var folderId) && knownFolder(folderId) is { } folder)
        {
            var candidate = Path.Combine(folder, known.Groups[2].Value);
            exe = IsExe(candidate) ? candidate : null;
        }
        if (exe is null && Path.IsPathFullyQualified(id) && IsExe(id))
            exe = id;

        // Um id sem barra é um AUMID declarado no atalho (Electron, Chrome, Office).
        var appId = !string.IsNullOrEmpty(aumid) ? aumid : (id.Contains('\\') ? null : id);

        string? processName = null;
        string? directory = null;
        if (exe is not null)
        {
            processName = Path.GetFileNameWithoutExtension(exe);
            directory = Path.GetDirectoryName(exe);
            if (string.Equals(Path.GetFileName(exe), "Update.exe", StringComparison.OrdinalIgnoreCase)
                && linkArguments is not null && SquirrelStart.Match(linkArguments) is { Success: true } squirrel)
            {
                // Squirrel: o atalho abre ...\Discord\Update.exe --processStart Discord.exe, e o que roda é
                // ...\Discord\app-1.0.9035\Discord.exe.
                var started = squirrel.Groups[1].Success ? squirrel.Groups[1].Value : squirrel.Groups[2].Value;
                processName = Path.GetFileNameWithoutExtension(started);
            }
            if (!IsSpecificDirectory(directory))
                directory = null;
        }

        var shortcutArguments = exe is not null && IsExe(linkTarget) && !string.IsNullOrWhiteSpace(linkArguments)
            ? linkArguments.Trim()
            : null;
        return new AppTarget(id, name, false, appId, exe, processName, directory, shortcutArguments);
    }

    private static bool IsExe(string? path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(path);

    /// <summary>
    /// A pasta é de um app só? Procurar processos em <c>C:\Windows\System32</c> ou direto em <c>Program Files</c>
    /// marcaria como aberto qualquer coisa que roda ali.
    /// </summary>
    internal static bool IsSpecificDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
            return false;
        var full = directory.TrimEnd('\\');
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && (full.Equals(windows, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase)))
            return false;
        // C:\Program Files\App tem 3 partes; C:\Program Files sozinho tem 2.
        return full.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length >= 3;
    }
}
