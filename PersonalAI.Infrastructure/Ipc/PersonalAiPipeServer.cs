using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PersonalAI.Core.Editor;

namespace PersonalAI.Infrastructure.Ipc;

public sealed class PersonalAiPipeServer : IDisposable, IAsyncDisposable
{
    public const string PipeName = "PersonalAI.EditorContext.v1";

    private readonly EditorContextMessageHandler _handler;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly object _gate = new();
    private Task? _serverTask;
    private Task? _disposeTask;
    private bool _disposed;

    public PersonalAiPipeServer(EditorContextMessageHandler handler)
        : this(handler, PipeName)
    {
    }

    internal PersonalAiPipeServer(EditorContextMessageHandler handler, string pipeName)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public EditorIpcConnectionState State { get; private set; } =
        EditorIpcConnectionState.Stopped;

    public string StatusMessage { get; private set; } = "Editor integration stopped.";

    public event EventHandler? StateChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_serverTask is not null || _disposed)
            {
                return;
            }

            SetState(
                EditorIpcConnectionState.Starting,
                "Starting editor integration.");
            _serverTask = Task.Run(RunAsync);
        }
    }

    public void Dispose() => _ = DisposeAsync();

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _cancellation.Cancel();
                _disposeTask = FinishDisposalAsync(_serverTask);
            }

            return new ValueTask(_disposeTask);
        }
    }

    private async Task FinishDisposalAsync(Task? serverTask)
    {
        try
        {
            if (serverTask is not null)
            {
                await serverTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                SetState(
                    EditorIpcConnectionState.Listening,
                    "VS Code integration connected.");

                await pipe.WaitForConnectionAsync(_cancellation.Token);
                await HandleConnectionAsync(pipe, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                SetState(
                    EditorIpcConnectionState.Stopped,
                    "Editor integration stopped.");
                return;
            }
            catch (IOException exception)
            {
                SetState(
                    EditorIpcConnectionState.Unavailable,
                    $"Editor integration already in use or unavailable. {exception.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2), _cancellation.Token);
            }
            catch (UnauthorizedAccessException exception)
            {
                SetState(
                    EditorIpcConnectionState.Unavailable,
                    $"Editor integration unavailable. {exception.Message}");
                return;
            }
        }

        SetState(
            EditorIpcConnectionState.Stopped,
            "Editor integration stopped.");
    }

    private void SetState(EditorIpcConnectionState state, string statusMessage)
    {
        State = state;
        StatusMessage = statusMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task HandleConnectionAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var line = await ReadLineLimitedAsync(
            stream,
            EditorContextProtocol.MaxMessageBytes,
            cancellationToken);

        if (line is null)
        {
            await WriteResponseAsync(stream, false, "empty", cancellationToken);
            return;
        }

        try
        {
            var envelope = EditorContextProtocol.Deserialize(
                Encoding.UTF8.GetBytes(line));
            var result = await _handler.HandleAsync(
                envelope,
                cancellationToken).ConfigureAwait(false);
            await WriteResponseAsync(
                stream,
                result.Ok,
                result.Message,
                cancellationToken);
        }
        catch (EditorContextProtocolException exception)
        {
            await WriteResponseAsync(
                stream,
                false,
                exception.Message,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await WriteResponseAsync(
                stream,
                false,
                "Request failed.",
                cancellationToken);
        }
    }

    private static async Task<string?> ReadLineLimitedAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var oneByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(oneByte, cancellationToken);

            if (read == 0)
            {
                break;
            }

            if (oneByte[0] == (byte)'\n')
            {
                break;
            }

            buffer.Add(oneByte[0]);

            if (buffer.Count > maxBytes)
            {
                throw new EditorContextProtocolException(
                    "Message exceeds the maximum size.");
            }
        }

        return buffer.Count == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        bool ok,
        string message,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Serialize(new PipeResponse(ok, message)) + "\n";
        var bytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed record PipeResponse(bool Ok, string Message);
}
