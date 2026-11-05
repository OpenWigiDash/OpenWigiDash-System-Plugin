namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>Por onde um app é aberto.</summary>
public enum LaunchKind
{
    /// <summary>Pelo Iniciar (<c>shell:AppsFolder\id</c>), como um clique no menu.</summary>
    StartMenu,
    /// <summary>Direto no exe para onde o atalho aponta, com os argumentos do atalho mais os da instância.</summary>
    Exe,
    /// <summary>App da Store ativado pelo AUMID com argumentos (IApplicationActivationManager).</summary>
    Packaged,
}

/// <summary>O que abrir e com que argumentos. <see cref="File"/> é o caminho, o AUMID ou o <c>shell:AppsFolder\id</c>.</summary>
public sealed record LaunchPlan(LaunchKind Kind, string File, string Arguments);

/// <summary>
/// Decide como abrir um app com argumentos. Sem argumentos vai sempre pelo Iniciar, que resolve tudo sozinho. Com
/// argumentos o Iniciar não serve: o shell ignora os parâmetros de um item de <c>shell:AppsFolder</c>. Então o app comum
/// abre direto pelo exe do atalho e o da Store é ativado pelo AUMID, que aceita argumentos.
/// </summary>
public static class AppLaunch
{
    public static LaunchPlan Plan(AppChoice app, AppTarget? target, string? arguments)
    {
        var startMenu = $@"shell:AppsFolder\{app.Id}";
        var extra = Normalize(arguments);
        if (extra.Length == 0)
            return new LaunchPlan(LaunchKind.StartMenu, startMenu, "");
        if (target is { IsPackaged: true, Aumid: { } aumid })
            return new LaunchPlan(LaunchKind.Packaged, aumid, extra);
        if (target?.ExePath is { } exe)
            return new LaunchPlan(LaunchKind.Exe, exe, Combine(exe, target.LinkArguments, extra));
        // Sem exe conhecido (o shell não respondeu): tenta pelo Iniciar mesmo, que pode ignorar os argumentos.
        return new LaunchPlan(LaunchKind.StartMenu, startMenu, extra);
    }

    /// <summary>Argumentos como a pessoa escreveu, com as variáveis (<c>%USERPROFILE%</c>) trocadas.</summary>
    public static string Normalize(string? arguments) =>
        string.IsNullOrWhiteSpace(arguments) ? "" : Environment.ExpandEnvironmentVariables(arguments.Trim());

    /// <summary>
    /// Junta os argumentos do atalho com os da instância. No Squirrel (Discord, Slack) o atalho abre o
    /// <c>Update.exe --processStart App.exe</c>, e o que vai para o app tem que ir em <c>--process-start-args</c>.
    /// </summary>
    internal static string Combine(string exe, string? linkArguments, string extra)
    {
        var link = linkArguments?.Trim() ?? "";
        if (string.Equals(Path.GetFileName(exe), "Update.exe", StringComparison.OrdinalIgnoreCase)
            && link.Contains("--processStart", StringComparison.OrdinalIgnoreCase)
            && !link.Contains("--process-start-args", StringComparison.OrdinalIgnoreCase))
        {
            return $"{link} --process-start-args \"{extra.Replace("\"", "\\\"")}\"";
        }
        return link.Length == 0 ? extra : link + " " + extra;
    }
}
