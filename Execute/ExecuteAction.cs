using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenWigiDash.SystemPlugin.Execute;

/// <summary>O que o Execute roda: um programa/arquivo ou um comando escrito na hora.</summary>
public enum ExecuteMode
{
    /// <summary>Um .exe, .bat, .cmd, .ps1 ou qualquer arquivo/atalho (abre com o programa padrão).</summary>
    Program,
    /// <summary>Um comando inline, rodado pelo cmd ou pelo PowerShell.</summary>
    Command,
}

/// <summary>Quem roda o comando inline.</summary>
public enum CommandShell
{
    Cmd,
    /// <summary>Windows PowerShell (powershell.exe), que vem com o Windows.</summary>
    PowerShell,
    /// <summary>PowerShell 7 (pwsh.exe), se estiver instalado.</summary>
    Pwsh,
}

/// <summary>Como a janela do programa abre.</summary>
public enum ExecuteWindow
{
    Normal,
    Minimized,
    Maximized,
    /// <summary>Sem janela (bom para scripts e comandos).</summary>
    Hidden,
}

/// <summary>
/// A configuração de um botão Execute. Salva inteira como JSON numa chave só: os campos dependem uns dos outros
/// (o modo decide quais valem).
/// </summary>
public sealed record ExecuteAction
{
    public ExecuteMode Mode { get; init; } = ExecuteMode.Program;

    /// <summary>Modo programa: caminho do arquivo. Aceita variáveis (%USERPROFILE%) e aspas em volta.</summary>
    public string Path { get; init; } = "";

    /// <summary>Modo programa: argumentos, do jeito que iriam na linha de comando.</summary>
    public string Arguments { get; init; } = "";

    /// <summary>Pasta de execução. Vazia = a pasta do arquivo (programa) ou a pasta do usuário (comando).</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>Modo comando: o texto. Várias linhas rodam em sequência.</summary>
    public string Command { get; init; } = "";

    public CommandShell Shell { get; init; } = CommandShell.Cmd;

    public ExecuteWindow Window { get; init; } = ExecuteWindow.Normal;

    /// <summary>Modo comando com janela: não fecha a janela no fim (cmd /k, PowerShell -NoExit).</summary>
    public bool KeepOpen { get; init; }

    /// <summary>Roda elevado. O Windows mostra o UAC no PC.</summary>
    public bool RunAsAdmin { get; init; }

    /// <summary>Espera terminar e usa o código de saída (0 = deu certo). Desligado: deu certo = abriu.</summary>
    public bool WaitForExit { get; init; }

    [JsonIgnore]
    public bool IsEmpty => Mode == ExecuteMode.Program ? string.IsNullOrWhiteSpace(Path) : string.IsNullOrWhiteSpace(Command);

    /// <summary>Caminho do programa com as variáveis expandidas e sem aspas.</summary>
    [JsonIgnore]
    public string ResolvedPath => Clean(Path);

    /// <summary>Pasta de execução que vai ser usada (já com o padrão quando vazia), ou <c>null</c> se não há.</summary>
    [JsonIgnore]
    public string? ResolvedWorkingDirectory
    {
        get
        {
            var folder = Clean(WorkingDirectory);
            if (folder.Length > 0)
                return folder;
            if (Mode == ExecuteMode.Command)
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var file = ResolvedPath;
            return System.IO.Path.IsPathFullyQualified(file) ? System.IO.Path.GetDirectoryName(file) : null;
        }
    }

    /// <summary>
    /// Comando de PowerShell escolhido para rodar no cmd (cmdlets como Start-Sleep, Add-Type, [Tipo]::Metodo): o cmd roda
    /// só as linhas que entende e o resto falha. A tela de configurações avisa.
    /// </summary>
    [JsonIgnore]
    public bool LooksLikePowerShellInCmd =>
        Mode == ExecuteMode.Command && Shell == CommandShell.Cmd && PowerShellHint.IsMatch(Command);

    private static readonly Regex PowerShellHint = new(
        @"\b(Get|Set|Start|Stop|New|Remove|Add|Invoke|Write|Out|Select|Where|ForEach|Test|Import)-[A-Za-z]+\b|\[[A-Za-z.]+\]::|\$[A-Za-z_]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Clean(string? text) =>
        Environment.ExpandEnvironmentVariables((text ?? "").Trim().Trim('"').Trim());
}

/// <summary>Como o <see cref="ExecuteAction"/> vira um <see cref="ProcessStartInfo"/>. Separado para dar para testar.</summary>
public static class ExecuteCommandLine
{
    /// <summary>Executáveis que o CreateProcess roda direto (o .bat/.cmd passa pelo cmd sozinho).</summary>
    private static readonly string[] Executables = [".exe", ".com", ".bat", ".cmd"];

    /// <summary>
    /// Monta o processo. <paramref name="captureOutput"/> pede a saída do programa (só funciona sem janela e sem admin;
    /// <see cref="CanCapture"/> diz quando).
    /// </summary>
    public static ProcessStartInfo Build(ExecuteAction action, bool captureOutput = false)
    {
        var (file, arguments) = action.Mode == ExecuteMode.Command ? ForCommand(action) : ForProgram(action);

        // Admin, pastas, links e arquivos que não são executáveis (.lnk, .url, documentos) passam pelo shell. O resto vai direto, o que
        // permite esconder a janela de console e ler a saída.
        var shell = action.RunAsAdmin || !IsExecutable(file);
        var info = new ProcessStartInfo(file)
        {
            Arguments = arguments,
            UseShellExecute = shell,
            WindowStyle = action.Window switch
            {
                ExecuteWindow.Minimized => ProcessWindowStyle.Minimized,
                ExecuteWindow.Maximized => ProcessWindowStyle.Maximized,
                ExecuteWindow.Hidden => ProcessWindowStyle.Hidden,
                _ => ProcessWindowStyle.Normal,
            },
        };
        if (action.RunAsAdmin)
            info.Verb = "runas";
        if (action.ResolvedWorkingDirectory is { } folder)
            info.WorkingDirectory = folder;
        if (!shell && action.Window == ExecuteWindow.Hidden)
            info.CreateNoWindow = true;
        if (captureOutput && CanCapture(action, info))
        {
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            // O cmd e o Windows PowerShell escrevem na página de código OEM (850 no Brasil); o pwsh, em UTF-8.
            var encoding = action.Mode == ExecuteMode.Command && action.Shell == CommandShell.Pwsh ? Encoding.UTF8 : OemEncoding();
            info.StandardOutputEncoding = encoding;
            info.StandardErrorEncoding = encoding;
        }
        return info;
    }

    /// <summary>Dá para ler o que o programa escreveu: sem janela, sem shell (admin/documento) e esperando terminar.</summary>
    public static bool CanCapture(ExecuteAction action, ProcessStartInfo info) =>
        action.WaitForExit && action.Window == ExecuteWindow.Hidden && !info.UseShellExecute;

    private static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    public static bool IsExecutable(string file)
    {
        if (file.Contains("://", StringComparison.Ordinal) || Directory.Exists(file))
            return false;
        var extension = System.IO.Path.GetExtension(file);
        // Sem extensão ("notepad", "ping"): o Windows procura o .exe no PATH.
        return extension.Length == 0 || Executables.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static (string File, string Arguments) ForProgram(ExecuteAction action)
    {
        var file = action.ResolvedPath;
        var arguments = action.Arguments.Trim();
        // .ps1 abriria no Bloco de Notas: roda no PowerShell.
        if (string.Equals(System.IO.Path.GetExtension(file), ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var script = $"-NoProfile -ExecutionPolicy Bypass {(action.KeepOpen ? "-NoExit " : "")}-File \"{file}\"";
            return ("powershell.exe", arguments.Length == 0 ? script : script + " " + arguments);
        }
        return (file, arguments);
    }

    private static (string File, string Arguments) ForCommand(ExecuteAction action)
    {
        var lines = action.Command.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        switch (action.Shell)
        {
            case CommandShell.PowerShell:
            case CommandShell.Pwsh:
                // -EncodedCommand: várias linhas, aspas e acentos chegam inteiros, sem escapar nada.
                var script = string.Join("\n", lines);
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var exe = action.Shell == CommandShell.Pwsh ? "pwsh.exe" : "powershell.exe";
                return (exe, $"-NoProfile -ExecutionPolicy Bypass {(action.KeepOpen ? "-NoExit " : "")}-EncodedCommand {encoded}");

            default:
                // Várias linhas viram comandos em sequência ("&"), como num .bat sem parar no erro. O /s com aspas em
                // volta deixa o cmd usar o resto da linha do jeito que foi escrito.
                var command = string.Join(" & ", lines);
                var comspec = Environment.GetEnvironmentVariable("ComSpec");
                var cmd = string.IsNullOrWhiteSpace(comspec) ? "cmd.exe" : comspec;
                return (cmd, $"/d /s {(action.KeepOpen ? "/k" : "/c")} \"{command}\"");
        }
    }
}
