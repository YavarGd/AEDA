using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AssistPillViewModel : ObservableObject
{
    public const string AutomaticContextPrompt =
        "Explain the selected content clearly and concisely.";

    private readonly IAssistPillHost _host;
    private readonly StringBuilder _rawResponse = new();
    private AssistPillSettings _settings;
    private string _safeFullResponse = string.Empty;
    private CancellationTokenSource? _generationCancellation;
    private CancellationTokenSource? _screenCaptureCancellation;
    private Task? _generationTask;
    private PersonalAI.Core.Context.AttachedContextItem? _context;
    private int _isOpening;
    private string? _lastPrompt;
    private long _invocationId;
    private FocusRestorationRequest? _pendingFocusRestoration;

    public AssistPillViewModel(
        IAssistPillHost host,
        AssistPillSettings settings)
    {
        _host = host;
        _settings = ApplicationSettingsValidator.NormalizeAssistPill(settings);
        _state = _settings.Enabled
            ? AssistPillState.IdlePill
            : AssistPillState.Hidden;
    }

    [ObservableProperty]
    private AssistPillState _state;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _response = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private AssistContextPreviewModel _contextPreview = AssistContextPreviewModel.Empty;

    public bool IsEnabled => _settings.Enabled;

    public bool IsIdle => State == AssistPillState.IdlePill;

    public bool IsExpanded => State is not AssistPillState.Hidden and not AssistPillState.IdlePill;

    public bool IsFallbackInput => State == AssistPillState.SpotlightPrompt;

    public long InvocationId => Volatile.Read(ref _invocationId);

    public FocusRestorationRequest? PendingFocusRestoration => _pendingFocusRestoration;

    public bool IsDetectingContext => State == AssistPillState.DetectingContext;

    public bool IsResponseSurface => State is AssistPillState.DetectingContext or
        AssistPillState.StreamingResponse or
        AssistPillState.Completed or AssistPillState.Cancelled or AssistPillState.Failed;

    public bool IsStreaming => State == AssistPillState.StreamingResponse;

    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);

    public bool CanSubmit => IsEnabled && IsFallbackInput &&
        !string.IsNullOrWhiteSpace(Prompt);

    public bool CanCancel => IsStreaming;

    public bool CanCopy => HasResponse;

    public bool CanShowResponseActions => HasResponse && !IsStreaming;

    public bool HasContextPreview => !ContextPreview.IsEmpty && !ContextPreview.IsBlocked;

    public bool HasBlockedContext => ContextPreview.IsBlocked;

    public bool HasContextSurface => HasContextPreview || HasBlockedContext;

    public bool CanClearContext => ContextPreview.IsClearable && !IsStreaming;

    public bool CanHandoffToCode => CanShowResponseActions && IsCodeContext;

    public bool CanHandoffToResearch => CanShowResponseActions &&
        HasContextPreview &&
        !CanHandoffToCode;

    public bool CanHandoffToMemory => CanShowResponseActions && HasContextPreview;

    public string ContextTypeText => IsCodeContext
        ? "Selected code"
        : ContextPreview.ContextType switch
        {
            nameof(PersonalAI.Core.Context.AssistContextKind.ScreenText) => "Screen text",
            nameof(PersonalAI.Core.Context.AssistContextKind.Clipboard) => "Clipboard text",
            _ => "Selected text"
        };

    public string ContextCountText =>
        $"{ContextPreview.TextLength:N0} {(ContextPreview.TextLength == 1 ? "character" : "characters")}";

    public string ContextApplicationText => ContextPreview.ApplicationLabel.ToLowerInvariant() switch
    {
        "msedge" => "Microsoft Edge",
        "chrome" => "Google Chrome",
        "code" or "code - insiders" => "Visual Studio Code",
        _ => ContextPreview.ApplicationLabel
    };

    private bool IsCodeContext =>
        ContextPreview.ContextType == PersonalAI.Core.Context.AssistContextKind.VsCodeEditor.ToString() ||
        ContextPreview.ApplicationLabel.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
        ContextPreview.ApplicationLabel.StartsWith("Code - ", StringComparison.OrdinalIgnoreCase);

    public string ContextStatusMessage => ContextPreview.BlockedReason switch
    {
        "password-control" or "protected-control" => "Protected content was not captured.",
        "privacy-blocked" or "elevated-target" => "AEDA did not read this app.",
        _ => "Context could not be captured."
    };

    public bool CanRetry => State == AssistPillState.Failed &&
        !string.IsNullOrWhiteSpace(_lastPrompt);

    public void ApplySettings(AssistPillSettings settings)
    {
        _settings = ApplicationSettingsValidator.NormalizeAssistPill(settings);
        SetResponse(_safeFullResponse);
        OnPropertyChanged(nameof(IsEnabled));

        if (!_settings.Enabled)
        {
            Cancel();
            State = AssistPillState.Hidden;
        }
        else if (State == AssistPillState.Hidden)
        {
            ShowIdle();
        }
    }

    public void ShowIdle()
    {
        State = IsEnabled
            ? AssistPillState.IdlePill
            : AssistPillState.Hidden;
        StatusText = "Ready";
    }

    public async Task<bool> OpenPromptAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || Interlocked.CompareExchange(ref _isOpening, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            var invocationId = PrepareForInvocation();
            State = AssistPillState.DetectingContext;
            StatusText = "Reading selected text";
            try
            {
                _context = await _host.CaptureContextAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ShowIdle();
                return false;
            }
            catch
            {
                _context = null;
            }

            if (invocationId != InvocationId)
            {
                return false;
            }

            ContextPreview = _host.CurrentEnvelope is not null
                ? AssistContextPreviewModel.FromEnvelope(_host.CurrentEnvelope)
                : AssistContextPreviewModel.Empty;

            if (AssistContextPolicy.IsMeaningful(_context, DateTimeOffset.UtcNow))
            {
                Prompt = AutomaticContextPrompt;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "Context ready";
            }
            else
            {
                _context = null;
                Prompt = string.Empty;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "Ask AEDA";
            }

            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }

    [RelayCommand]
    public async Task SelectScreenTextAsync()
    {
        if (!IsFallbackInput ||
            Interlocked.CompareExchange(ref _isOpening, 1, 0) != 0)
        {
            return;
        }

        var invocationId = InvocationId;
        var cancellation = new CancellationTokenSource();
        _screenCaptureCancellation = cancellation;
        try
        {
            ContextPreview = AssistContextPreviewModel.Empty;
            State = AssistPillState.DetectingContext;
            StatusText = "Select text on screen";
            _context = await _host.CaptureScreenTextAsync(cancellation.Token);
            if (invocationId != InvocationId)
            {
                return;
            }

            ContextPreview = _host.CurrentEnvelope is not null
                ? AssistContextPreviewModel.FromEnvelope(_host.CurrentEnvelope)
                : AssistContextPreviewModel.Empty;

            if (AssistContextPolicy.IsMeaningful(_context, DateTimeOffset.UtcNow))
            {
                Prompt = AutomaticContextPrompt;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "Context ready";
            }
            else
            {
                _context = null;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "No text found — try another area";
            }
        }
        catch (OperationCanceledException)
        {
            if (invocationId == InvocationId)
            {
                _context = null;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "Ask AEDA";
            }
        }
        catch
        {
            if (invocationId == InvocationId)
            {
                _context = null;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "No text found — try another area";
            }
        }
        finally
        {
            if (ReferenceEquals(_screenCaptureCancellation, cancellation))
            {
                _screenCaptureCancellation = null;
            }

            cancellation.Dispose();
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }

    public void CancelContextCapture() => _screenCaptureCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SubmitAsync()
    {
        var prompt = Prompt.Trim();
        if (!CanSubmit)
        {
            return;
        }

        await StartGenerationAsync(prompt, InvocationId);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() => _generationCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRetry))]
    public async Task RetryAsync()
    {
        if (!CanRetry)
        {
            return;
        }

        if (_context is not null)
        {
            try
            {
                _context = await _host.CaptureContextAsync(CancellationToken.None);
                ContextPreview = _host.CurrentEnvelope is not null
                    ? AssistContextPreviewModel.FromEnvelope(_host.CurrentEnvelope)
                    : AssistContextPreviewModel.Empty;
            }
            catch
            {
                _context = null;
                _host.ClearContext();
                ContextPreview = AssistContextPreviewModel.Empty;
            }

            if (!AssistContextPolicy.IsMeaningful(_context, DateTimeOffset.UtcNow))
            {
                _context = null;
                _host.ClearContext();
                ContextPreview = AssistContextPreviewModel.Empty;
                Prompt = string.Empty;
                State = AssistPillState.SpotlightPrompt;
                StatusText = "Ask AEDA";
                return;
            }
        }

        await StartGenerationAsync(_lastPrompt!, InvocationId);
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    public async Task CopyResponseAsync()
    {
        if (!CanCopy)
        {
            return;
        }

        try
        {
            await _host.CopyTextAsync(_safeFullResponse, CancellationToken.None);
            StatusText = "Copied";
        }
        catch
        {
            StatusText = "Copy failed";
        }

        SetPendingFocusRestoration(FocusRestorationTrigger.CopyResponse);
    }

    [RelayCommand]
    public async Task OpenInAedaAsync()
    {
        SetPendingFocusRestoration(FocusRestorationTrigger.AppOpen);
        if (_lastPrompt is not null && HasResponse)
        {
            await _host.HandoffToModuleAsync(
                _lastPrompt,
                _safeFullResponse,
                AssistHandoffDestination.Chat);
        }
        else
        {
            await _host.OpenInAedaAsync();
        }

        State = AssistPillState.Hidden;
    }

    [RelayCommand(CanExecute = nameof(CanClearContext))]
    public void ClearContext()
    {
        _context = null;
        _host.ClearContext();
        ContextPreview = AssistContextPreviewModel.Empty;
        StatusText = "Context removed";
    }

    public async Task HandoffToModuleAsync(AssistHandoffDestination destination)
    {
        if (_lastPrompt is null || !HasResponse)
        {
            return;
        }

        SetPendingFocusRestoration(destination == AssistHandoffDestination.Chat
            ? FocusRestorationTrigger.AppOpen
            : FocusRestorationTrigger.ModuleOpen);
        await _host.HandoffToModuleAsync(_lastPrompt, _safeFullResponse, destination);
        State = AssistPillState.Hidden;
    }

    public void Collapse()
    {
        if (IsDetectingContext)
        {
            CancelContextCapture();
            return;
        }

        if (IsStreaming)
        {
            Cancel();
            return;
        }

        SetPendingFocusRestoration(FocusRestorationTrigger.Dismiss);

        ShowIdle();
    }

    public void Hide()
    {
        if (IsDetectingContext)
        {
            CancelContextCapture();
            return;
        }

        if (IsStreaming)
        {
            Cancel();
            return;
        }

        SetPendingFocusRestoration(FocusRestorationTrigger.Dismiss);

        State = AssistPillState.Hidden;
    }

    public Task WaitForGenerationAsync() => _generationTask ?? Task.CompletedTask;

    partial void OnPromptChanged(string value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnResponseChanged(string value)
    {
        OnPropertyChanged(nameof(HasResponse));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanShowResponseActions));
        CopyResponseCommand.NotifyCanExecuteChanged();
        NotifyContextActionsChanged();
    }

    partial void OnStateChanged(AssistPillState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsFallbackInput));
        OnPropertyChanged(nameof(IsDetectingContext));
        OnPropertyChanged(nameof(IsResponseSurface));
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanShowResponseActions));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanClearContext));
        SubmitCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        ClearContextCommand.NotifyCanExecuteChanged();
        NotifyContextActionsChanged();
    }

    partial void OnContextPreviewChanged(AssistContextPreviewModel value)
    {
        OnPropertyChanged(nameof(HasContextPreview));
        OnPropertyChanged(nameof(HasBlockedContext));
        OnPropertyChanged(nameof(HasContextSurface));
        OnPropertyChanged(nameof(CanClearContext));
        OnPropertyChanged(nameof(ContextTypeText));
        OnPropertyChanged(nameof(ContextCountText));
        OnPropertyChanged(nameof(ContextApplicationText));
        OnPropertyChanged(nameof(ContextStatusMessage));
        ClearContextCommand.NotifyCanExecuteChanged();
        NotifyContextActionsChanged();
    }

    private long PrepareForInvocation()
    {
        var invocationId = Interlocked.Increment(ref _invocationId);
        _rawResponse.Clear();
        SetResponse(string.Empty);
        Prompt = string.Empty;
        StatusText = string.Empty;
        ContextPreview = AssistContextPreviewModel.Empty;
        _context = null;
        _lastPrompt = null;
        _generationTask = null;
        _pendingFocusRestoration = null;
        OnPropertyChanged(nameof(InvocationId));
        return invocationId;
    }

    private Task StartGenerationAsync(string prompt, long invocationId)
    {
        if (invocationId != InvocationId)
        {
            return Task.CompletedTask;
        }

        if (_generationTask is { IsCompleted: false })
        {
            return _generationTask;
        }

        _rawResponse.Clear();
        SetResponse(string.Empty);
        _lastPrompt = prompt;
        StatusText = _context?.Metadata.GetValueOrDefault("captureSource") == "screenOcr"
            ? "Using text selected from screen"
            : _context is null ? "Generating" : "Using selected text";
        State = AssistPillState.StreamingResponse;
        var cancellation = new CancellationTokenSource();
        _generationCancellation = cancellation;
        _generationTask = RunGenerationAsync(prompt, invocationId, cancellation);
        return _generationTask;
    }

    private async Task RunGenerationAsync(
        string prompt,
        long invocationId,
        CancellationTokenSource cancellation)
    {
        try
        {
            var result = await _host.GenerateAsync(
                prompt,
                _context,
                chunk => AppendResponseChunk(invocationId, chunk),
                cancellation.Token);
            if (invocationId != InvocationId)
            {
                return;
            }

            if (result.Status == ChatStatus.Cancelled)
            {
                State = AssistPillState.Cancelled;
                StatusText = "Cancelled";
            }
            else if (result.Status == ChatStatus.Failed)
            {
                Fail(result.SafeErrorMessage ?? "The response could not be completed.");
            }
            else if (string.IsNullOrWhiteSpace(_safeFullResponse))
            {
                Fail("The provider returned no visible answer.");
            }
            else
            {
                State = AssistPillState.Completed;
                StatusText = "Completed";
            }
        }
        catch (OperationCanceledException)
        {
            if (invocationId == InvocationId)
            {
                State = AssistPillState.Cancelled;
                StatusText = "Cancelled";
            }
        }
        catch
        {
            if (invocationId == InvocationId)
            {
                Fail("The response could not be completed.");
            }
        }
        finally
        {
            if (ReferenceEquals(_generationCancellation, cancellation))
            {
                _generationCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void AppendResponseChunk(long invocationId, string chunk)
    {
        if (invocationId != InvocationId)
        {
            return;
        }

        _rawResponse.Append(chunk);
        SetResponse(RemoveHiddenReasoning(_rawResponse.ToString()));
    }

    private void SetResponse(string fullResponse)
    {
        _safeFullResponse = fullResponse;
        Response = Bound(fullResponse, _settings.ResponsePreviewCharacters);
    }

    private static string RemoveHiddenReasoning(string value) =>
        Regex.Replace(
            value,
            @"(?is)<(?:think|analysis)>.*?(?:</(?:think|analysis)>|$)",
            string.Empty).Trim();

    private static string Bound(string value, int limit)
    {
        var normalized = value.Trim();
        return normalized.Length <= limit
            ? normalized
            : $"{normalized[..(limit - 1)].TrimEnd()}…";
    }

    private void Fail(string message)
    {
        State = AssistPillState.Failed;
        StatusText = message;
    }

    private void SetPendingFocusRestoration(FocusRestorationTrigger trigger)
    {
        _pendingFocusRestoration = _host.RequestFocusRestoration(trigger);
        OnPropertyChanged(nameof(PendingFocusRestoration));
    }

    private void NotifyContextActionsChanged()
    {
        OnPropertyChanged(nameof(CanHandoffToCode));
        OnPropertyChanged(nameof(CanHandoffToResearch));
        OnPropertyChanged(nameof(CanHandoffToMemory));
    }
}
