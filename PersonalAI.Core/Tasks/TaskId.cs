namespace PersonalAI.Core.Tasks;

public readonly record struct TaskId(Guid Value)
{
    public static TaskId NewId() => new(Guid.NewGuid());

    public static TaskId Parse(string value) => new(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}
