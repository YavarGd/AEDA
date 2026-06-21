using System.Diagnostics;
using PersonalAI.Core.Coding;

namespace PersonalAI.Infrastructure.Coding;

public sealed class ControlledProcessRunner : IControlledProcessRunner
{
    public async Task<ControlledProcessResult> RunAsync(
        ControlledProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return Failed(startedAt, startFailed: true);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var safeOut = ValidationOutputSanitizer.Sanitize(stdout, request.MaxOutputCharacters);
            var safeErr = ValidationOutputSanitizer.Sanitize(stderr, request.MaxOutputCharacters);
            return new ControlledProcessResult(
                process.ExitCode,
                TimedOut: false,
                Cancelled: false,
                StartFailed: false,
                safeOut.Text,
                safeErr.Text,
                safeOut.Truncated,
                safeErr.Truncated,
                DateTimeOffset.UtcNow - startedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return Failed(startedAt, cancelled: true);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return Failed(startedAt, timedOut: true);
        }
        catch
        {
            return Failed(startedAt, startFailed: true);
        }
    }

    private static ControlledProcessResult Failed(
        DateTimeOffset startedAt,
        bool timedOut = false,
        bool cancelled = false,
        bool startFailed = false) =>
        new(
            null,
            timedOut,
            cancelled,
            startFailed,
            string.Empty,
            string.Empty,
            StdoutTruncated: false,
            StderrTruncated: false,
            DateTimeOffset.UtcNow - startedAt);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
