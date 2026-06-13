namespace PersonalAI.Core.Tasks;

public sealed record SafeTaskError(
    string Code,
    string UserMessage)
{
    public static SafeTaskError FromException(
        string code,
        string userMessage,
        Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(exception);

        return new SafeTaskError(code, userMessage);
    }
}
