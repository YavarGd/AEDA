using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace PersonalAI.Core.Tasks;

public sealed class TaskEventBus : ITaskEventBus
{
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = [];
    private readonly TaskEventBusOptions _options;

    public TaskEventBus()
        : this(TaskEventBusOptions.Default)
    {
    }

    public TaskEventBus(TaskEventBusOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.SubscriberBufferCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Subscriber buffer capacity must be positive.");
        }

        _options = options;
    }

    public ValueTask PublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(taskEvent);

        Subscription[] subscriptions;
        lock (_gate)
        {
            subscriptions = [.. _subscriptions];
        }

        foreach (var subscription in subscriptions)
        {
            if (subscription.Accepts(taskEvent))
            {
                subscription.Channel.Writer.TryWrite(taskEvent);
            }
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        CancellationToken cancellationToken = default) =>
        SubscribeCoreAsync(taskId: null, cancellationToken);

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        SubscribeCoreAsync(taskId, cancellationToken);

    private async IAsyncEnumerable<TaskEvent> SubscribeCoreAsync(
        TaskId? taskId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TaskEvent>(
            new BoundedChannelOptions(_options.SubscriberBufferCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });
        var subscription = new Subscription(taskId, channel);

        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }

        try
        {
            await foreach (var taskEvent in channel.Reader.ReadAllAsync(
                               cancellationToken))
            {
                yield return taskEvent;
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscriptions.Remove(subscription);
            }

            channel.Writer.TryComplete();
        }
    }

    private sealed record Subscription(
        TaskId? TaskId,
        Channel<TaskEvent> Channel)
    {
        public bool Accepts(TaskEvent taskEvent) =>
            TaskId is null || taskEvent.TaskId.Equals(TaskId.Value);
    }
}
