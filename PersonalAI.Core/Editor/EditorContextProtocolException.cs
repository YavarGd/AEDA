namespace PersonalAI.Core.Editor;

public sealed class EditorContextProtocolException : Exception
{
    public EditorContextProtocolException(string message)
        : base(message)
    {
    }

    public EditorContextProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
