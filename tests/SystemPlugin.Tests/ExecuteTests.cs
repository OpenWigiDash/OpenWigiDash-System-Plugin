using System.Text;
using OpenWigi.Sdk;
using OpenWigiDash.SystemPlugin.Execute;
using OpenWigiDash.SystemPlugin.Rendering;
using OpenWigiDash.SystemPlugin.Widgets;

namespace SystemPlugin.Tests;

public class ExecuteTests
{
    /// <summary>Runner falso: devolve o resultado escolhido, ou fica rodando até o teste soltar.</summary>
    private sealed class FakeRunner : IExecuteRunner
    {
        public List<ExecuteAction> Calls { get; } = [];
        public ExecuteResult Result { get; set; } = new(true, null, null, null, TimeSpan.Zero);
        public TaskCompletionSource? Gate { get; set; }
        public bool WasCancelled { get; private set; }

        public async Task<ExecuteResult> RunAsync(ExecuteAction action, CancellationToken cancellationToken)
        {
            Calls.Add(action);
            if (Gate is { } gate)
            {
                try
                {
                    await gate.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    WasCancelled = true;
                    return new ExecuteResult(false, null, "Parado.", null, TimeSpan.Zero);
                }
            }
            return Result;
        }
    }

    private static (ExecuteInstance Instance, FakeRunner Runner, FakeContext Context) Create()
    {
        var context = new FakeContext(WidgetSize.Small);
        var runner = new FakeRunner();
        return (new ExecuteInstance(context, runner, _ => Task.FromResult<System.Drawing.Bitmap?>(null)), runner, context);
    }

    private static TouchEvent Tap => new(TouchKind.Tap, 10, 10, TimeSpan.FromMilliseconds(80));
    private static TouchEvent Hold => new(TouchKind.LongPress, 10, 10, TimeSpan.FromMilliseconds(600), 1);

    private static readonly ExecuteAction Notepad = new() { Path = @"C:\Windows\notepad.exe" };

    [Fact]
    public async Task EmptyActionDoesNothing()
    {
        var (instance, runner, _) = Create();

        Assert.Null(await instance.RunAsync());

        Assert.Empty(runner.Calls);
        Assert.Equal(TileStatus.None, instance.Status);
    }

    [Fact]
    public async Task SuccessTurnsTileGreen()
    {
        var (instance, runner, _) = Create();
        await instance.SetActionAsync(Notepad);

        var result = await instance.RunAsync();

        Assert.True(result!.Success);
        Assert.Single(runner.Calls);
        Assert.Equal(TileStatus.Success, instance.Status);
        Assert.Equal(TileStatus.Success, instance.CurrentVisual().Status);
    }

    [Fact]
    public async Task FailureTurnsTileRedAndKeepsTheError()
    {
        var (instance, runner, _) = Create();
        await instance.SetActionAsync(Notepad);
        runner.Result = new ExecuteResult(false, 3, "Terminou com o código 3.", "erro", TimeSpan.Zero);

        await instance.RunAsync();

        Assert.Equal(TileStatus.Failure, instance.Status);
        Assert.Equal(3, instance.LastResult!.ExitCode);
        Assert.NotNull(instance.LastRunAt);
    }

    [Fact]
    public async Task FeedbackGoesAwayAfterAWhile()
    {
        var previous = ExecuteInstance.SuccessFeedback;
        ExecuteInstance.SuccessFeedback = TimeSpan.FromMilliseconds(50);
        try
        {
            var (instance, _, _) = Create();
            await instance.SetActionAsync(Notepad);
            await instance.RunAsync();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (instance.Status != TileStatus.None && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(TileStatus.None, instance.Status);
        }
        finally
        {
            ExecuteInstance.SuccessFeedback = previous;
        }
    }

    [Fact]
    public async Task WaitingShowsRunningAndIgnoresSecondTap()
    {
        var (instance, runner, _) = Create();
        await instance.SetActionAsync(Notepad with { WaitForExit = true });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = instance.RunAsync();
        Assert.Equal(TileStatus.Running, instance.Status);
        Assert.True(instance.IsRunning);

        Assert.Null(await instance.RunAsync());

        runner.Gate.SetResult();
        await first;
        Assert.Single(runner.Calls);
        Assert.Equal(TileStatus.Success, instance.Status);
        Assert.False(instance.IsRunning);
    }

    [Fact]
    public async Task LongPressStopsWhatIsRunning()
    {
        var (instance, runner, _) = Create();
        await instance.SetActionAsync(Notepad with { WaitForExit = true });
        runner.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = instance.RunAsync();
        await instance.OnTouchAsync(Hold, default);
        await run;

        Assert.True(runner.WasCancelled);
        Assert.Equal(TileStatus.Failure, instance.Status);
    }

    [Fact]
    public async Task TapRuns()
    {
        var (instance, runner, _) = Create();
        await instance.SetActionAsync(Notepad);

        await instance.OnTouchAsync(Tap, default);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (runner.Calls.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task SettingsSurviveReload()
    {
        var (instance, _, context) = Create();
        var action = new ExecuteAction
        {
            Mode = ExecuteMode.Command,
            Command = "echo oi",
            Shell = CommandShell.PowerShell,
            WorkingDirectory = @"C:\Temp",
            Window = ExecuteWindow.Hidden,
            WaitForExit = true,
        };
        await instance.SetActionAsync(action);
        await instance.SetTitleAsync("Build");
        await instance.SetGlyphAsync("lightning");
        await instance.SetBackgroundAsync("#23a55a");

        var reloaded = new ExecuteInstance(context, new FakeRunner(), _ => Task.FromResult<System.Drawing.Bitmap?>(null));
        await reloaded.LoadSettingsAsync(default);

        Assert.Equal(action, reloaded.Action);
        Assert.Equal("Build", reloaded.Title);
        Assert.Equal("lightning", reloaded.Glyph);
        Assert.Equal("#23A55A", reloaded.Background);
    }

    [Fact]
    public async Task RendersEveryStatusAtEverySize()
    {
        foreach (var size in new[] { WidgetSize.Mini, WidgetSize.Small })
        {
            var context = new FakeContext(size);
            var instance = new ExecuteInstance(context, new FakeRunner(), _ => Task.FromResult<System.Drawing.Bitmap?>(null));
            await instance.SetActionAsync(Notepad);
            foreach (var status in Enum.GetValues<TileStatus>())
            {
                var frame = TileRenderer.Render(context.PixelSize, context.Theme, instance.CurrentVisual() with { Status = status });
                Assert.Equal(context.PixelSize, frame.Size);
            }
        }
    }

    // ---- Linha de comando ----

    [Fact]
    public void ProgramUsesItsFolderByDefault()
    {
        var info = ExecuteCommandLine.Build(new ExecuteAction { Path = "\"C:\\Tools\\build.bat\"", Arguments = "--fast" });

        Assert.Equal(@"C:\Tools\build.bat", info.FileName);
        Assert.Equal("--fast", info.Arguments);
        Assert.Equal(@"C:\Tools", info.WorkingDirectory);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void ExplicitWorkingDirectoryWins()
    {
        var info = ExecuteCommandLine.Build(Notepad with { WorkingDirectory = @"D:\Projetos" });

        Assert.Equal(@"D:\Projetos", info.WorkingDirectory);
    }

    [Fact]
    public void ShortcutsAndDocumentsGoThroughTheShell()
    {
        Assert.True(ExecuteCommandLine.Build(new ExecuteAction { Path = @"C:\Users\x\Desktop\Jogo.lnk" }).UseShellExecute);
        Assert.True(ExecuteCommandLine.Build(new ExecuteAction { Path = "https://example.com" }).UseShellExecute);
        Assert.False(ExecuteCommandLine.Build(new ExecuteAction { Path = "notepad" }).UseShellExecute);
    }

    [Fact]
    public void AdminUsesRunAs()
    {
        var info = ExecuteCommandLine.Build(Notepad with { RunAsAdmin = true });

        Assert.True(info.UseShellExecute);
        Assert.Equal("runas", info.Verb);
    }

    [Fact]
    public void HiddenAndWaitingCapturesOutput()
    {
        var action = Notepad with { Window = ExecuteWindow.Hidden, WaitForExit = true };

        var info = ExecuteCommandLine.Build(action, captureOutput: true);

        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.False(ExecuteCommandLine.Build(action with { RunAsAdmin = true }, captureOutput: true).RedirectStandardOutput);
        Assert.False(ExecuteCommandLine.Build(action with { Window = ExecuteWindow.Normal }, captureOutput: true).RedirectStandardOutput);
    }

    [Fact]
    public void CmdJoinsLines()
    {
        var info = ExecuteCommandLine.Build(new ExecuteAction
        {
            Mode = ExecuteMode.Command,
            Command = "cd /d C:\\src\r\n\r\ngit pull",
        });

        Assert.EndsWith("cmd.exe", info.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/d /s /c \"cd /d C:\\src & git pull\"", info.Arguments);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), info.WorkingDirectory);
    }

    [Fact]
    public void CmdKeepOpenUsesK()
    {
        var info = ExecuteCommandLine.Build(new ExecuteAction { Mode = ExecuteMode.Command, Command = "dir", KeepOpen = true });

        Assert.StartsWith("/d /s /k ", info.Arguments);
    }

    [Fact]
    public void PowerShellGetsTheScriptEncoded()
    {
        const string script = "Write-Host \"olá\"\n$x = 1";
        var info = ExecuteCommandLine.Build(new ExecuteAction { Mode = ExecuteMode.Command, Shell = CommandShell.PowerShell, Command = script });

        Assert.Equal("powershell.exe", info.FileName);
        var encoded = info.Arguments.Split(' ').Last();
        Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
        Assert.Contains("-ExecutionPolicy Bypass", info.Arguments);
    }

    [Fact]
    public void Ps1FileRunsInPowerShell()
    {
        var info = ExecuteCommandLine.Build(new ExecuteAction { Path = @"C:\Scripts\limpar.ps1", Arguments = "-Force" });

        Assert.Equal("powershell.exe", info.FileName);
        Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\Scripts\\limpar.ps1\" -Force", info.Arguments);
        Assert.Equal(@"C:\Scripts", info.WorkingDirectory);
    }

    [Fact]
    public void ValidationExplainsMissingFiles()
    {
        Assert.Equal("Escolha o programa.", ProcessExecuteRunner.Validate(new ExecuteAction()));
        Assert.StartsWith("Arquivo não encontrado", ProcessExecuteRunner.Validate(new ExecuteAction { Path = @"C:\nao\existe.exe" }));
        Assert.StartsWith("Pasta de execução", ProcessExecuteRunner.Validate(new ExecuteAction { Path = "notepad", WorkingDirectory = @"C:\nao\existe" }));
        Assert.Null(ProcessExecuteRunner.Validate(new ExecuteAction { Path = "notepad" }));
    }

    [Fact]
    public async Task RealCommandReportsExitCodeAndOutput()
    {
        var runner = new ProcessExecuteRunner();
        var ok = await runner.RunAsync(new ExecuteAction
        {
            Mode = ExecuteMode.Command,
            Command = "echo ola",
            Window = ExecuteWindow.Hidden,
            WaitForExit = true,
        }, default);
        var fail = await runner.RunAsync(new ExecuteAction
        {
            Mode = ExecuteMode.Command,
            Command = "exit /b 7",
            Window = ExecuteWindow.Hidden,
            WaitForExit = true,
        }, default);

        Assert.True(ok.Success);
        Assert.Equal(0, ok.ExitCode);
        Assert.Equal("ola", ok.Output);
        Assert.False(fail.Success);
        Assert.Equal(7, fail.ExitCode);
    }
}
