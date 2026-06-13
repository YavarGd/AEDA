namespace PersonalAI.Core.Tools;

public readonly record struct ToolId
{
    public ToolId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
