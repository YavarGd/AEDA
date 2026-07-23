using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed record AssistContextPreviewModel(
    string ApplicationLabel,
    string ContextType,
    string ShortPreview,
    int TextLength,
    bool IsTruncated,
    bool IsBlocked,
    string? BlockedReason,
    bool IsClearable,
    string SourceIdentity)
{
    private const int PreviewLimit = 80;

    public static readonly AssistContextPreviewModel Empty = new(
        string.Empty,
        AssistContextKind.None.ToString(),
        string.Empty,
        0,
        false,
        false,
        null,
        false,
        string.Empty);

    public static AssistContextPreviewModel FromEnvelope(AssistContextEnvelope envelope)
    {
        if (envelope is null)
        {
            return Empty;
        }

        var preview = envelope.SelectedTextPreview;
        if (preview is not null && preview.Length > PreviewLimit)
        {
            preview = preview[..PreviewLimit] + "...";
        }

        return new AssistContextPreviewModel(
            envelope.ApplicationLabel,
            envelope.ContextKind.ToString(),
            preview ?? string.Empty,
            envelope.SelectedTextLength,
            envelope.IsTruncated,
            envelope.IsBlocked,
            envelope.BlockedReason,
            envelope.HasContext,
            envelope.ProcessIdentity);
    }

    public bool IsEmpty => string.IsNullOrEmpty(ApplicationLabel) && ContextType == AssistContextKind.None.ToString();
}
