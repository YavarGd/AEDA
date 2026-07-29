namespace PersonalAI.Infrastructure.Chat;

public enum ChatStatus
{
    Ready,
    Connecting,
    Generating,
    Completed,
    Cancelled,
    Failed
}
