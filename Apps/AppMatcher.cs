namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>Um processo aberto, como o <see cref="RunningApps"/> enxerga.</summary>
/// <param name="PackagedAumid">AUMID do pacote, só em app da Store/MSIX.</param>
public sealed record ProcessInfo(int Id, string Name, string? Path, string? PackagedAumid);

/// <summary>Uma janela de primeiro nível visível, com o AUMID que ela declara (se declara).</summary>
public sealed record WindowInfo(IntPtr Handle, int ProcessId, string? Aumid, bool Minimized);

/// <summary>Decide se um processo ou uma janela é do app. Sem Windows: os testes chamam direto.</summary>
public static class AppMatcher
{
    public static bool IsMatch(AppTarget target, ProcessInfo process)
    {
        if (target.IsPackaged)
            return process.PackagedAumid is not null && string.Equals(process.PackagedAumid, target.Aumid, StringComparison.OrdinalIgnoreCase);

        if (process.Path is null)
            return false;
        if (target.ExePath is not null && string.Equals(process.Path, target.ExePath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (target.InstallDirectory is null)
            return false;

        var exeName = target.ExePath is null ? null : Path.GetFileNameWithoutExtension(target.ExePath);
        var launcherStartsOther = target.ProcessName is not null && !string.Equals(target.ProcessName, exeName, StringComparison.OrdinalIgnoreCase);
        if (launcherStartsOther)
        {
            // Squirrel: Discord.exe em qualquer app-x.y.z dentro da pasta do Update.exe.
            return string.Equals(process.Name, target.ProcessName, StringComparison.OrdinalIgnoreCase)
                && process.Path.StartsWith(target.InstallDirectory + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Launcher que abre outro exe na mesma pasta (EA app: EALauncher.exe abre EADesktop.exe).
        return string.Equals(Path.GetDirectoryName(process.Path), target.InstallDirectory, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMatch(AppTarget target, WindowInfo window) =>
        target.Aumid is not null && window.Aumid is not null && string.Equals(window.Aumid, target.Aumid, StringComparison.OrdinalIgnoreCase);
}
