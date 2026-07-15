using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using System.Windows.Automation;

namespace PersonalAI.Desktop.WinUI.Services;

public interface ISelectedTextContextProvider
{
    Task<SelectedTextContextResult> TryGetSelectedTextAsync(
        ActiveWindowReference foreground,
        PrivacySettings privacy,
        int maxCharacters,
        CancellationToken cancellationToken);
}

public sealed record SelectedTextContextResult(
    bool IsAvailable,
    string? Text,
    string SourceType,
    string? ApplicationIdentity,
    DateTimeOffset CapturedAtUtc,
    string? SafeFailureReason,
    bool IsTrustedForImmediateSubmission);

public sealed class WindowsUiaSelectedTextProvider(
    Func<ActiveWindowReference, int, string?>? readSelection = null) :
    ISelectedTextContextProvider
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(350);

    public async Task<SelectedTextContextResult> TryGetSelectedTextAsync(
        ActiveWindowReference foreground,
        PrivacySettings privacy,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        if (maxCharacters <= 0 ||
            foreground.CapturedAtUtc < DateTimeOffset.UtcNow.AddSeconds(-5) ||
            PrivacyExclusionMatcher.IsSensitiveWindow(
                foreground.ProcessName,
                foreground.WindowTitle,
                privacy.ExcludedApplications))
        {
            return Unavailable(foreground, "Selection capture is blocked for this application.");
        }

        try
        {
            var selectedText = await Task.Run(
                    () => (readSelection ?? ReadFocusedSelection)(foreground, maxCharacters),
                    cancellationToken)
                .WaitAsync(Timeout, cancellationToken);
            selectedText = selectedText?.Trim();
            if (string.IsNullOrWhiteSpace(selectedText))
            {
                return Unavailable(foreground, "The focused control has no selected text.");
            }

            selectedText = selectedText.Length > maxCharacters
                ? selectedText[..maxCharacters]
                : selectedText;
            return new SelectedTextContextResult(
                true,
                selectedText,
                "Windows UI Automation TextPattern",
                foreground.ProcessName,
                DateTimeOffset.UtcNow,
                null,
                true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(foreground, "Selection capture timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Unavailable(foreground, "Selection capture timed out.");
        }
        catch
        {
            return Unavailable(foreground, "The focused control does not expose a readable selection.");
        }
    }

    private static string? ReadFocusedSelection(
        ActiveWindowReference foreground,
        int maxCharacters)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null || focused.Current.ProcessId != foreground.ProcessId)
        {
            return null;
        }

        if (focused.Current.IsPassword)
        {
            return null;
        }

        if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) ||
            pattern is not TextPattern textPattern)
        {
            return null;
        }

        var ranges = textPattern.GetSelection();
        var remaining = maxCharacters;
        var parts = new List<string>();
        foreach (var range in ranges)
        {
            if (remaining <= 0)
            {
                break;
            }

            var text = range.GetText(remaining + 1);
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Length > remaining ? text[..remaining] : text;
                parts.Add(text);
                remaining -= text.Length;
            }
        }

        var selectedText = string.Join(Environment.NewLine, parts)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Take(2_000);
        return string.Join(Environment.NewLine, selectedText).Trim();
    }

    private static SelectedTextContextResult Unavailable(
        ActiveWindowReference foreground,
        string reason) => new(
            false,
            null,
            "Windows UI Automation TextPattern",
            foreground.ProcessName,
            DateTimeOffset.UtcNow,
            reason,
            false);
}
