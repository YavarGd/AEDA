using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using PersonalAI.Core.Editor;
using PersonalAI.Infrastructure.Ipc;

namespace PersonalAI.Tests.Editor;

public sealed class PersonalAiActivationClientTests
{
    [Fact]
    public async Task OpenRequestUsesExistingContractAndCarriesNoContext()
    {
        var pipeName = UniquePipeName();
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var receive = ReceiveAndAcknowledgeAsync(pipe);

        var handedOff = await ActivateAsync(pipeName, TimeSpan.FromSeconds(2));
        var envelope = await receive;

        Assert.True(handedOff);
        Assert.Equal("PersonalAI.EditorContext.v1", PersonalAiPipeServer.PipeName);
        Assert.Equal(EditorContextProtocol.SupportedProtocolVersion, envelope.ProtocolVersion);
        Assert.False(string.IsNullOrWhiteSpace(envelope.RequestId));
        Assert.Equal(ContextSource.Vscode, envelope.Source);
        Assert.Equal(EditorContextCommands.OpenPersonalAi, envelope.Command);
        Assert.Null(envelope.UserPrompt);
        Assert.Null(envelope.Context);
    }

    [Fact]
    public async Task ExistingPipeOpenCommandActivatesPrimaryAndAcknowledgesHandoff()
    {
        var pipeName = UniquePipeName();
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attachedContextCount = 0;
        await using var server = new PersonalAiPipeServer(
            new EditorContextMessageHandler(
                _ => Interlocked.Increment(ref attachedContextCount),
                () => activated.TrySetResult()),
            pipeName);
        server.Start();

        var handedOff = await ActivateAsync(pipeName, TimeSpan.FromSeconds(2));

        Assert.True(handedOff);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(0, Volatile.Read(ref attachedContextCount));
    }

    [Fact]
    public async Task PrimaryStartupRaceRetriesUntilExistingPipeIsReady()
    {
        var pipeName = UniquePipeName();
        var activationCount = 0;
        await using var server = new PersonalAiPipeServer(
            new EditorContextMessageHandler(
                _ => { },
                () => Interlocked.Increment(ref activationCount)),
            pipeName);

        var handoff = ActivateAsync(pipeName, TimeSpan.FromSeconds(2));
        await Task.Delay(150);
        server.Start();

        Assert.True(await handoff);
        Assert.Equal(1, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task UnreachablePrimaryFailsSafelyWithinBound()
    {
        var stopwatch = Stopwatch.StartNew();

        var handedOff = await ActivateAsync(
            UniquePipeName(),
            TimeSpan.FromMilliseconds(250));

        Assert.False(handedOff);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RepeatedSecondaryLaunchesOnlyRequestPrimaryActivation()
    {
        var pipeName = UniquePipeName();
        var activationCount = 0;
        await using var server = new PersonalAiPipeServer(
            new EditorContextMessageHandler(
                _ => { },
                () => Interlocked.Increment(ref activationCount)),
            pipeName);
        server.Start();

        for (var launch = 0; launch < 3; launch++)
        {
            Assert.True(await ActivateAsync(pipeName, TimeSpan.FromSeconds(2)));
        }

        Assert.Equal(3, Volatile.Read(ref activationCount));
    }

    [Fact]
    public async Task ServerDisposalWaitsForItsBackgroundListenerToStop()
    {
        var pipeName = UniquePipeName();
        var server = new PersonalAiPipeServer(
            new EditorContextMessageHandler(_ => { }, () => { }),
            pipeName);
        server.Start();
        using var idleClient = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await idleClient.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(1));

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(EditorIpcConnectionState.Stopped, server.State);
    }

    private static Task<bool> ActivateAsync(string pipeName, TimeSpan timeout) =>
        PersonalAiActivationClient.TryActivatePrimaryAsync(
            pipeName,
            timeout,
            TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(20));

    private static async Task<EditorContextEnvelope> ReceiveAndAcknowledgeAsync(
        NamedPipeServerStream pipe)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var line = await reader.ReadLineAsync(timeout.Token);
        Assert.NotNull(line);
        var envelope = EditorContextProtocol.Deserialize(Encoding.UTF8.GetBytes(line));
        var response = Encoding.UTF8.GetBytes("{\"Ok\":true,\"Message\":\"opened\"}\n");
        await pipe.WriteAsync(response, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        return envelope;
    }

    private static string UniquePipeName() => $"AEDA.Tests.Activation.{Guid.NewGuid():N}";
}
