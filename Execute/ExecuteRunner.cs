using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OpenWigiDash.SystemPlugin.Execute;

/// <summary>Como terminou uma execução.</summary>
/// <param name="Success">Abriu (sem esperar) ou saiu com código 0 (esperando).</param>
/// <param name="ExitCode">Código de saída, quando esperou terminar.</param>
/// <param name="Error">Por que falhou, em português, para a tela de configurações.</param>
/// <param name="Output">Fim do que o programa escreveu (só sem janela e esperando).</param>
public sealed record ExecuteResult(bool Success, int? ExitCode, string? Error, string? Output, TimeSpan Duration)
{
    public static ExecuteResult Fail(string error) => new(false, null, error, null, TimeSpan.Zero);
}

/// <summary>Roda um <see cref="ExecuteAction"/>. Cancelar o token encerra o programa (long-press).</summary>
public interface IExecuteRunner
{
    Task<ExecuteResult> RunAsync(ExecuteAction action, CancellationToken cancellationToken);
}

/// <summary>Roda de verdade, com <see cref="Process"/>.</summary>
internal sealed class ProcessExecuteRunner : IExecuteRunner
{
    /// <summary>Quanto da saída guardar para mostrar (o fim, que é onde costuma estar o erro).</summary>
    internal const int MaxOutput = 4000;

    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: a pessoa disse "Não" no UAC.

    public async Task<ExecuteResult> RunAsync(ExecuteAction action, CancellationToken cancellationToken)
    {
        if (Validate(action) is { } problem)
            return ExecuteResult.Fail(problem);

        var info = ExecuteCommandLine.Build(action, captureOutput: true);
        var capture = info.RedirectStandardOutput;
        var output = new StringBuilder();
        var watch = Stopwatch.StartNew();
        Process? process;
        try
        {
            process = new Process { StartInfo = info };
            if (capture)
            {
                process.OutputDataReceived += (_, e) => Append(output, e.Data);
                process.ErrorDataReceived += (_, e) => Append(output, e.Data);
            }
            if (!process.Start())
            {
                // O shell entregou para um programa que já estava aberto (documento, link): conta como abriu.
                process.Dispose();
                return new ExecuteResult(true, null, null, null, watch.Elapsed);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ExecuteResult.Fail("Cancelado no aviso de administrador (UAC).");
        }
        catch (Win32Exception ex)
        {
            return ExecuteResult.Fail(ex.NativeErrorCode == 2 ? "Arquivo não encontrado: " + info.FileName : ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return ExecuteResult.Fail(ex.Message);
        }

        using (process)
        {
            if (capture)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            if (!action.WaitForExit)
                return new ExecuteResult(true, null, null, null, watch.Elapsed);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Já terminou, ou é elevado e não dá para encerrar daqui.
                }
                return new ExecuteResult(false, null, "Parado pelo long-press.", Text(output), watch.Elapsed);
            }

            var code = process.ExitCode;
            return new ExecuteResult(code == 0, code, code == 0 ? null : $"Terminou com o código {code}.", Text(output), watch.Elapsed);
        }
    }

    /// <summary>Erros que dá para explicar antes de tentar (arquivo ou pasta que não existem).</summary>
    internal static string? Validate(ExecuteAction action)
    {
        if (action.IsEmpty)
            return action.Mode == ExecuteMode.Program ? "Escolha o programa." : "Escreva o comando.";
        if (action.Mode == ExecuteMode.Program)
        {
            var file = action.ResolvedPath;
            if (Path.IsPathFullyQualified(file) && !File.Exists(file) && !Directory.Exists(file))
                return "Arquivo não encontrado: " + file;
        }
        var folder = ExecuteAction.Clean(action.WorkingDirectory);
        if (folder.Length > 0 && !Directory.Exists(folder))
            return "Pasta de execução não encontrada: " + folder;
        return null;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
            return;
        lock (output)
        {
            output.AppendLine(line);
            if (output.Length > MaxOutput * 2)
                output.Remove(0, output.Length - MaxOutput);
        }
    }

    private static string? Text(StringBuilder output)
    {
        lock (output)
        {
            var text = output.ToString().TrimEnd();
            if (text.Length > MaxOutput)
                text = "…" + text[^MaxOutput..];
            return text.Length == 0 ? null : text;
        }
    }
}
