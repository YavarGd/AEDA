namespace PersonalAI.Desktop.WinUI.Services;

public interface IClipboardWriter
{
    Task CopyTextAsync(string text, CancellationToken cancellationToken = default);
}
