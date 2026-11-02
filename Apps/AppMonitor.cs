using Microsoft.Extensions.Logging;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// O app que uma instância acompanha: o alvo resolvido agora há pouco e se ele está aberto. Criado pelo
/// <see cref="AppMonitor"/>, que atualiza todos numa thread só.
/// </summary>
internal sealed class AppWatch : IDisposable
{
    private readonly AppMonitor _monitor;
    private AppChoice? _app;

    internal AppWatch(AppMonitor monitor) => _monitor = monitor;

    /// <summary>App escolhido. Trocar pede uma resolução nova na próxima volta do monitor.</summary>
    public AppChoice? App
    {
        get => Volatile.Read(ref _app);
        set
        {
            Volatile.Write(ref _app, value);
            Target = null;
            State = AppState.Closed;
            ResolveRequested = true;
            _monitor.Wake();
        }
    }

    public AppTarget? Target { get; internal set; }
    public AppState State { get; internal set; } = AppState.Closed;

    /// <summary>Instância dormindo (página fora de vista): o monitor pula esta.</summary>
    public bool Active
    {
        get => Volatile.Read(ref _active);
        set
        {
            Volatile.Write(ref _active, value);
            if (value)
            {
                ResolveRequested = true;
                _monitor.Wake();
            }
        }
    }

    private bool _active = true;

    internal bool ResolveRequested { get; set; } = true;
    internal DateTime ResolvedAt { get; set; }

    /// <summary>
    /// Mudou o alvo ou o estado (abriu, fechou). <c>opened</c> = acabou de abrir: bom momento para recarregar o
    /// ícone, porque uma atualização do app costuma terminar assim.
    /// </summary>
    public event Action<bool>? Changed;

    internal void RaiseChanged(bool opened) => Changed?.Invoke(opened);

    /// <summary>Atualiza agora (antes de decidir entre abrir e trazer para a frente).</summary>
    public Task RefreshAsync() => _monitor.RefreshAsync();

    public void Dispose() => _monitor.Remove(this);
}

/// <summary>
/// Uma thread STA para todos os Open Application: a cada 1,5 s fotografa processos e janelas uma vez e compara com
/// todos os apps; a cada minuto pergunta de novo ao shell para onde cada atalho aponta. Sem instâncias acordadas, para.
/// </summary>
internal sealed class AppMonitor
{
    public static AppMonitor Shared { get; } = new();

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ResolveEvery = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly List<AppWatch> _watches = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly List<TaskCompletionSource> _pendingRefresh = [];
    private Thread? _thread;
    private ILogger? _logger;

    public void UseLogger(ILoggerFactory factory) => _logger ??= factory.CreateLogger("OpenWigiDash.System");

    public AppWatch Watch()
    {
        var watch = new AppWatch(this);
        lock (_gate)
        {
            _watches.Add(watch);
            if (_thread is null)
            {
                _thread = new Thread(Loop) { IsBackground = true, Name = "OpenWigiDash.System monitor" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }
        _wake.Set();
        return watch;
    }

    internal void Remove(AppWatch watch)
    {
        lock (_gate)
            _watches.Remove(watch);
    }

    internal void Wake() => _wake.Set();

    internal Task RefreshAsync()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _pendingRefresh.Add(tcs);
        _wake.Set();
        return tcs.Task;
    }

    private void Loop()
    {
        while (true)
        {
            List<AppWatch> watches;
            List<TaskCompletionSource> waiting;
            lock (_gate)
            {
                watches = _watches.Where(w => w.Active && w.App is not null).ToList();
                waiting = [.. _pendingRefresh];
                _pendingRefresh.Clear();
            }

            try
            {
                if (watches.Count > 0)
                    Poll(watches);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falha ao verificar os apps abertos.");
            }
            foreach (var tcs in waiting)
                tcs.TrySetResult();

            _wake.WaitOne(watches.Count > 0 ? Interval : Timeout.InfiniteTimeSpan);
        }
    }

    private void Poll(List<AppWatch> watches)
    {
        var now = DateTime.UtcNow;
        foreach (var watch in watches)
        {
            if (!watch.ResolveRequested && now - watch.ResolvedAt < ResolveEvery)
                continue;
            var app = watch.App;
            if (app is null)
                continue;
            AppTarget? target = null;
            try
            {
                target = AppCatalog.Resolve(app);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Falha ao resolver o app {App}.", app.Id);
            }
            watch.ResolveRequested = false;
            watch.ResolvedAt = now;
            if (target != watch.Target && ReferenceEquals(app, watch.App))
            {
                _logger?.LogInformation("App {App}: aumid={Aumid}, exe={Exe}, processo={Process}, pasta={Directory}, store={Packaged}.",
                    app.Id, target?.Aumid, target?.ExePath, target?.ProcessName, target?.InstallDirectory, target?.IsPackaged);
                watch.Target = target;
                watch.RaiseChanged(false);
            }
        }

        var snapshot = RunningApps.Capture(includePackaged: watches.Any(w => w.Target?.IsPackaged == true));
        foreach (var watch in watches)
        {
            var before = watch.State;
            var after = snapshot.StateOf(watch.Target);
            watch.State = after;
            if (before.IsRunning != after.IsRunning)
            {
                if (after.IsRunning)
                    watch.ResolveRequested = true; // abriu: pode ter sido atualizado, confere o alvo de novo
                watch.RaiseChanged(after.IsRunning);
            }
        }
    }
}
