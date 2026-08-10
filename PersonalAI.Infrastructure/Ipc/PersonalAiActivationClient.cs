using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PersonalAI.Core.Editor;

namespace PersonalAI.Infrastructure.Ipc;

public static class PersonalAiActivationClient
{
    internal static readonly TimeSpan DefaultHandoffTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultConnectAttemptTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);

    public static Task<bool> TryActivatePrimaryAsync(CancellationToken cancellationToken = default) =>
        TryActivatePrimaryAsync(
            PersonalAiPipeServer.PipeName,
            DefaultHandoffTimeout,
            DefaultConnectAttemptTimeout,
            DefaultRetryDelay,
            cancellationToken);

    internal static async Task<bool> TryActivatePrimaryAsync(
        string pipeName,
        TimeSpan handoffTimeout,
        TimeSpan connectAttemptTimeout,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(handoffTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(connectAttemptTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);

        using var handoffCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        handoffCancellation.CancelAfter(handoffTimeout);

        while (!handoffCancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    handoffCancellation.Token);
                connectCancellation.CancelAfter(connectAttemptTimeout);
                await pipe.ConnectAsync(connectCancellation.Token).ConfigureAwait(false);

                var envelope = new EditorContextEnvelope(
                    EditorContextProtocol.SupportedProtocolVersion,
                    Guid.NewGuid().ToString("N"),
                    ContextSource.Vscode,
                    EditorContextCommands.OpenPersonalAi,
                    UserPrompt: null,
                    Context: null);
                var payload = Encoding.UTF8.GetBytes(
                    EditorContextProtocol.Serialize(envelope) + "\n");
                await pipe.WriteAsync(payload, handoffCancellation.Token).ConfigureAwait(false);
                await pipe.FlushAsync(handoffCancellation.Token).ConfigureAwait(false);

                var response = await ReadResponseAsync(
                    pipe,
                    handoffCancellation.Token).ConfigureAwait(false);
                return response?.Ok == true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }

            if (handoffCancellation.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(retryDelay, handoffCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private static async Task<PipeResponse?> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        const int maxResponseBytes = 4 * 1024;
        var buffer = new List<byte>();
        var oneByte = new byte[1];
        while (buffer.Count <= maxResponseBytes)
        {
            var read = await stream.ReadAsync(oneByte, cancellationToken).ConfigureAwait(false);
            if (read == 0 || oneByte[0] == (byte)'\n')
            {
                break;
            }

            buffer.Add(oneByte[0]);
        }

        if (buffer.Count == 0 || buffer.Count > maxResponseBytes)
        {
            return null;
        }

        return JsonSerializer.Deserialize<PipeResponse>(buffer.ToArray());
    }

    private sealed record PipeResponse(bool Ok, string Message);
}
