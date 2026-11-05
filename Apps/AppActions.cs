using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>O que o long-press faz com o app aberto.</summary>
public enum LongPressAction
{
    DoNothing,
    /// <summary>Pede para fechar (como clicar no X): o app pode perguntar se quer salvar.</summary>
    Quit,
    /// <summary>Encerra os processos do app na hora, sem perguntar.</summary>
    ForceQuit,
}

/// <summary>Abrir, trazer para a frente e fechar um app.</summary>
internal static class AppActions
{
    /// <summary>
    /// Abre pelo Iniciar (<c>shell:AppsFolder\id</c>): o Windows resolve o exe atual, os argumentos do atalho e a
    /// ativação de app da Store, do mesmo jeito que um clique no menu Iniciar.
    /// </summary>
    public static void Launch(AppChoice app) =>
        Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{app.Id}") { UseShellExecute = true })?.Dispose();

    /// <summary>
    /// Abre com argumentos (<see cref="AppLaunch.Plan"/>). Sem argumentos é o mesmo que <see cref="Launch(AppChoice)"/>.
    /// <paramref name="target"/> é o alvo que o monitor já resolveu; se ainda não resolveu, pergunta ao shell aqui.
    /// </summary>
    public static void Launch(AppChoice app, AppTarget? target, string? arguments)
    {
        if (AppLaunch.Normalize(arguments).Length == 0)
        {
            Launch(app);
            return;
        }
        target ??= Sta.RunAsync(() => AppCatalog.Resolve(app)).GetAwaiter().GetResult();
        var plan = AppLaunch.Plan(app, target, arguments);
        switch (plan.Kind)
        {
            case LaunchKind.Packaged:
                Sta.RunAsync(() =>
                {
                    var manager = (Native.IApplicationActivationManager)new Native.ApplicationActivationManager();
                    try
                    {
                        Marshal.ThrowExceptionForHR(manager.ActivateApplication(plan.File, plan.Arguments, 0, out _));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(manager);
                    }
                    return true;
                }).GetAwaiter().GetResult();
                break;

            default:
                // Pelo shell: um exe que pede administrador mostra o UAC em vez de falhar.
                var info = new ProcessStartInfo(plan.File) { Arguments = plan.Arguments, UseShellExecute = true };
                if (plan.Kind == LaunchKind.Exe && Path.GetDirectoryName(plan.File) is { Length: > 0 } folder)
                    info.WorkingDirectory = folder;
                Process.Start(info)?.Dispose();
                break;
        }
    }

    /// <summary>
    /// Traz a janela do app para a frente. <c>false</c> se não há janela (app só na bandeja): aí quem chamou abre
    /// de novo, e os apps de instância única (Steam, Discord) mostram a janela sozinhos.
    /// </summary>
    public static bool BringToFront(AppState state)
    {
        var window = state.Windows.FirstOrDefault(w => !w.Minimized) ?? state.Windows.FirstOrDefault();
        if (window is null || !Native.IsWindow(window.Handle))
            return false;

        if (Native.IsIconic(window.Handle))
            Native.ShowWindow(window.Handle, Native.SW_RESTORE);
        else
            Native.ShowWindow(window.Handle, Native.SW_SHOW);

        // O Windows só deixa trocar o primeiro plano quem recebeu a última entrada. Um toque no WigiDash não conta
        // como entrada do PC; apertar e soltar o Alt libera o SetForegroundWindow (truque usado por launchers).
        if (Native.GetForegroundWindow() != window.Handle)
        {
            Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.SetForegroundWindow(window.Handle);
        }
        return true;
    }

    /// <summary>Fecha o app: <see cref="LongPressAction.Quit"/> manda WM_CLOSE às janelas; ForceQuit mata os processos.</summary>
    public static void Quit(AppState state, LongPressAction action)
    {
        switch (action)
        {
            case LongPressAction.Quit:
                foreach (var window in state.Windows)
                    Native.PostMessage(window.Handle, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;

            case LongPressAction.ForceQuit:
                foreach (var info in state.Processes)
                {
                    try
                    {
                        using var process = Process.GetProcessById(info.Id);
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Já fechou, ou é de outro usuário/elevado.
                    }
                }
                break;
        }
    }
}
