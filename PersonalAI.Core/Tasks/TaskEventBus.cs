using System.Threading.Channels;

namespace PersonalAI.Core.Tasks;

public sealed class TaskEventBus : ITaskEventBus
{
    private readonly object _gate = new();
    private readonly object _publishGate = new();
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

    internal int ActiveSubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscriptions.Count;
            }
        }
    }

    public ValueTask PublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(taskEvent);

        lock (_publishGate)
        {
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
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        CancellationToken cancellationToken = default) =>
        new SubscriptionEnumerable(this, taskId: null, cancellationToken);

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        new SubscriptionEnumerable(this, taskId, cancellationToken);

    private Subscription CreateSubscription(TaskId? taskId)
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

        return subscription;
    }

    private void RemoveSubscription(Subscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }

        subscription.Channel.Writer.TryComplete();
    }

    private sealed class SubscriptionEnumerable(
        TaskEventBus owner,
        TaskId? taskId,
        CancellationToken cancellationToken) : IAsyncEnumerable<TaskEvent>
    {
        public IAsyncEnumerator<TaskEvent> GetAsyncEnumerator(
            CancellationToken enumeratorCancellationToken = default)
        {
            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                enumeratorCancellationToken);
            return new SubscriptionEnumerator(
                owner,
                owner.CreateSubscription(taskId),
                linkedCancellation);
        }
    }

    private sealed class SubscriptionEnumerator(
        TaskEventBus owner,
        Subscription subscription,
        CancellationTokenSource cancellation) : IAsyncEnumerator<TaskEvent>
    {
        private bool _disposed;

        public TaskEvent Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                Current = await subscription.Channel.Reader.ReadAsync(
                    cancellation.Token);
                return true;
            }
            catch (ChannelClosedException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                await DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            cancellation.Cancel();
            owner.RemoveSubscription(subscription);
            cancellation.Dispose();
            return ValueTask.CompletedTask;
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
