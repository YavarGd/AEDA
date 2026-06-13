namespace PersonalAI.Core.Tasks;

public sealed record TaskEventBusOptions(int SubscriberBufferCapacity = 256)
{
    public static TaskEventBusOptions Default { get; } = new();
}
