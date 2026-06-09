using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PersonalAI.Core.Editor;

namespace PersonalAI.Desktop.Ipc;

public sealed class PersonalAiPipeServer : IDisposable
{
    public const string PipeName = "PersonalAI.EditorContext.v1";

    private readonly EditorContextMessageHandler _handler;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    public PersonalAiPipeServer(EditorContextMessageHandler handler)
    {
        _handler = handler;
    }

    public void Start()
    {
        _serverTask = Task.Run(RunAsync);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(_cancellation.Token);
                await HandleConnectionAsync(pipe, _cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
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
            _handler.Handle(envelope);
            await WriteResponseAsync(stream, true, "ok", cancellationToken);
        }
        catch (EditorContextProtocolException exception)
        {
            await WriteResponseAsync(
                stream,
                false,
                exception.Message,
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
