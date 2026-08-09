#if WINDOWS
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;
using System.Windows.Automation;

namespace PersonalAI.Infrastructure.Windows;

public sealed class WindowsUiaSelectedTextProvider :
    ISelectedTextContextProvider,
    IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(350);
    private readonly Func<ActiveWindowReference, int, string?> _readSelection;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private Task<string?>? _operation;
    private bool _disposed;

    public WindowsUiaSelectedTextProvider(
        Func<ActiveWindowReference, int, string?>? readSelection = null,
        TimeSpan? timeout = null)
    {
        _readSelection = readSelection ?? ReadFocusedSelection;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<SelectedTextContextResult> TryGetSelectedTextAsync(
        ActiveWindowReference foreground,
        PrivacySettings privacy,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maxCharacters <= 0 ||
            foreground.CapturedAtUtc < DateTimeOffset.UtcNow.AddSeconds(-5) ||
            PrivacyExclusionMatcher.IsSensitiveWindow(
                foreground.ProcessName,
                foreground.WindowTitle,
                privacy.ExcludedApplications))
        {
            return Unavailable(foreground, "Selection capture is blocked for this application.");
        }

        Task<string?> operation;
        lock (_gate)
        {
            if (_disposed)
            {
                return Unavailable(foreground, "Selection capture is unavailable.");
            }

            if (_operation is { IsCompleted: false })
            {
                return Unavailable(foreground, "Selection capture is already pending.");
            }

            operation = Task.Run(() =>
            {
                try
                {
                    return _readSelection(foreground, maxCharacters);
                }
                catch
                {
                    return null;
                }
            });
            _operation = operation;
        }

        string? selectedText;
        try
        {
            selectedText = await operation.WaitAsync(_timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Unavailable(foreground, "Selection capture timed out.");
        }

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

    public async ValueTask DisposeAsync()
    {
        Task? operation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            operation = _operation;
        }

        if (operation is null)
        {
            return;
        }

        try
        {
            await operation.WaitAsync(_timeout);
        }
        catch
        {
            // UI Automation has no managed cancellation for an in-flight COM call.
            // The single quarantined worker owns no mutable AEDA state.
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
#endif
