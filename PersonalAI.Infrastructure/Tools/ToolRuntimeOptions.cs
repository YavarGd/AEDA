namespace PersonalAI.Infrastructure.Tools;

public sealed record ToolRuntimeOptions(
    TimeSpan DefaultTimeout,
    bool UsePerTaskPermissionCache)
{
    public static ToolRuntimeOptions Default { get; } =
        new(TimeSpan.FromSeconds(30), UsePerTaskPermissionCache: true);
}
