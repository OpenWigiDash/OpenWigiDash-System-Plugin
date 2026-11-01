namespace OpenWigiDash.SystemPlugin.Apps;

/// <summary>
/// Roda código do shell numa thread STA própria. As interfaces do shell (pasta de apps, ícones) esperam STA, e o
/// host chama o widget de threads do pool (MTA).
/// </summary>
internal static class Sta
{
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "OpenWigiDash.System STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
