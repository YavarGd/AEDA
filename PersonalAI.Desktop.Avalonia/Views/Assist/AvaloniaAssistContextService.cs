using PersonalAI.Core.Context;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Desktop.Avalonia.Views.Assist;

/// <summary>
/// Cross-application selection capture (foreground-window tracking, UI Automation text
/// pattern reads, native focus inspection) is intentionally not wired up for the
/// Avalonia Assist surface in this task. This adapter always reports "nothing
/// captured", which drives <see cref="AssistPillViewModel"/> into its fallback-input
/// mode: the spotlight prompt is shown instead of an automatic context-derived prompt,
/// exactly as it does on WinUI when the WinUI service itself finds no meaningful
/// context. Wiring real capture is left to a follow-up task.
/// </summary>
public sealed class AvaloniaAssistContextService : IActiveWindowContextService
{
    public SelectedTextCaptureResult? LastCaptureResult => null;

    public Task<AttachedContextItem?> CaptureAsync(
        AttachedContextItem? explicitContext = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<AttachedContextItem?>(null);
}
