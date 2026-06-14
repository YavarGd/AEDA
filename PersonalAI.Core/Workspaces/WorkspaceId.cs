namespace PersonalAI.Core.Workspaces;

public readonly record struct WorkspaceId
{
    public WorkspaceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public static WorkspaceId NewId() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
