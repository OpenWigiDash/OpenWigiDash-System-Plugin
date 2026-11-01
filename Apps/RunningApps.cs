using System.Diagnostics;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>Se o app está aberto agora, com os processos e as janelas dele.</summary>
public sealed record AppState(bool IsRunning, IReadOnlyList<ProcessInfo> Processes, IReadOnlyList<WindowInfo> Windows)
{
    public static AppState Closed { get; } = new(false, [], []);
}

/// <summary>Fotografia dos processos e janelas do usuário, tirada de uma vez e comparada com vários apps.</summary>
public sealed class AppSnapshot(IReadOnlyList<ProcessInfo> processes, IReadOnlyList<WindowInfo> windows)
{
    public IReadOnlyList<ProcessInfo> Processes { get; } = processes;
    public IReadOnlyList<WindowInfo> Windows { get; } = windows;

    public AppState StateOf(AppTarget? target)
    {
        if (target is null)
            return AppState.Closed;
        var processes = Processes.Where(p => AppMatcher.IsMatch(target, p)).ToList();
        var ids = processes.Select(p => p.Id).ToHashSet();
        // Janela do app: declara o AUMID dele, ou é de um processo dele e não declara outro AUMID.
        var windows = Windows.Where(w => AppMatcher.IsMatch(target, w)
            || (ids.Contains(w.ProcessId) && (w.Aumid is null || target.Aumid is null))).ToList();
        return processes.Count == 0 && windows.Count == 0 ? AppState.Closed : new AppState(true, processes, windows);
    }
}

/// <summary>Lê processos e janelas do Windows. Rode numa thread STA (o AUMID da janela vem do shell).</summary>
internal static class RunningApps
{
    public static AppSnapshot Capture(bool includePackaged)
    {
        var processes = new List<ProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id is 0 or 4)
                    continue;
                var handle = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                if (handle == IntPtr.Zero)
                    continue; // processo de outro usuário ou protegido
                try
                {
                    var path = Native.GetProcessPath(handle);
                    var aumid = includePackaged ? Native.GetPackagedAumid(handle) : null;
                    processes.Add(new ProcessInfo(process.Id, process.ProcessName, path, aumid));
                }
                finally
                {
                    Native.CloseHandle(handle);
                }
            }
        }

        var windows = new List<WindowInfo>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd))
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                windows.Add(new WindowInfo(hwnd, pid, Native.GetWindowAumid(hwnd), Native.IsIconic(hwnd)));
            }
            return true;
        }, IntPtr.Zero);

        return new AppSnapshot(processes, windows);
    }

    /// <summary>Janela que aparece na barra de tarefas: visível, sem dono, com título, não é ferramenta nem escondida (cloaked).</summary>
    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero)
            return false;
        if ((Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOOLWINDOW) != 0)
            return false;
        return Native.GetWindowTextLength(hwnd) > 0 && !Native.IsCloaked(hwnd);
    }
}
