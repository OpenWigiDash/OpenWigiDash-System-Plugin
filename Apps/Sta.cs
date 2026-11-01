using System.Collections.Concurrent;

namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// Roda código do shell numa thread STA. As interfaces do shell (pasta de apps, ícones) esperam STA, e o host chama o
/// widget de threads do pool (MTA). Uma thread só, de vida longa, com fila: criar uma thread por ícone (centenas ao
/// abrir a lista de apps) deixava a lista lenta.
/// </summary>
internal static class Sta
{
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly Lazy<Thread> Worker = new(StartWorker);

    public static Task<T> RunAsync<T>(Func<T> work)
    {
        _ = Worker.Value;
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var job in Queue.GetConsumingEnumerable())
                job();
        })
        {
            IsBackground = true,
            Name = "OpenWigiDash.System STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }
}
