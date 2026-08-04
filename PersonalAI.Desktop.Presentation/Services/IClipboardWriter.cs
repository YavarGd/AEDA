namespace PersonalAI.Desktop.Presentation.Services;

public interface IClipboardWriter
{
    Task CopyTextAsync(string text, CancellationToken cancellationToken = default);
}
