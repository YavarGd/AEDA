using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Chat.Rendering;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AssistPillViewModel : ObservableObject
{
    private const int ContextLabelLimit = 120;
    private const int ContextPreviewLimit = 180;
    private readonly IAssistPillHost _host;
    private AttachedContextItem? _context;
    private AssistPillSettings _settings;
    private string _safeFullResponse = string.Empty;
    private int _isOpening;

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
    private string _contextLabel = "No context included";

    [ObservableProperty]
    private string _contextPreview = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready";

    public bool IsEnabled => _settings.Enabled;

    public bool IsIdle => State == AssistPillState.IdlePill;

    public bool IsExpanded => State is not AssistPillState.Hidden and not AssistPillState.IdlePill;

    public bool HasContext => _context is not null;

    public bool IsStreaming => State == AssistPillState.StreamingResponse;

    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);

    public bool CanSubmit => IsEnabled && IsExpanded && !IsStreaming &&
        !string.IsNullOrWhiteSpace(Prompt);

    public bool CanCancel => IsStreaming;

    public bool CanCopy => HasResponse;

    public bool CanRemoveContext => HasContext && !IsStreaming;

    public RenderedChatContent RenderedResponse =>
        ChatMarkdownRenderer.Shared.Render(Response);

    public void ApplySettings(AssistPillSettings settings)
    {
        _settings = ApplicationSettingsValidator.NormalizeAssistPill(settings);
        SetResponse(_safeFullResponse);
        OnPropertyChanged(nameof(IsEnabled));

        if (!_settings.Enabled)
        {
            if (IsStreaming)
            {
                _host.CancelGeneration();
            }

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
            StatusText = "Capturing available context";
            try
            {
                _context = await _host.CaptureContextAsync(cancellationToken);
            }
            catch
            {
                _context = null;
            }

            UpdateContextPresentation();
            State = _context is null
                ? AssistPillState.SpotlightPrompt
                : AssistPillState.ContextPrompt;
            StatusText = _context is null
                ? "No context included · Privacy protected"
                : "Context included";
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _isOpening, 0);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveContext))]
    public void RemoveContext()
    {
        _context = null;
        UpdateContextPresentation();
        if (State == AssistPillState.ContextPrompt)
        {
            State = AssistPillState.SpotlightPrompt;
        }

        StatusText = "Context removed";
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SubmitAsync()
    {
        var prompt = Prompt.Trim();
        if (!CanSubmit)
        {
            return;
        }

        if (_host.IsGenerating)
        {
            Fail("AEDA Chat is already generating. Open AEDA to continue there.");
            return;
        }

        SetResponse(string.Empty);
        StatusText = "Generating response";
        State = AssistPillState.StreamingResponse;

        try
        {
            var status = await _host.GenerateAsync(
                prompt,
                _context,
                SetResponse,
                CancellationToken.None);
            State = status switch
            {
                ChatStatus.Completed => AssistPillState.Completed,
                ChatStatus.Cancelled => AssistPillState.Cancelled,
                _ => AssistPillState.Failed
            };
            StatusText = status switch
            {
                ChatStatus.Completed => "Completed",
                ChatStatus.Cancelled => "Cancelled",
                _ => "The response could not be completed."
            };
        }
        catch (OperationCanceledException)
        {
            State = AssistPillState.Cancelled;
            StatusText = "Cancelled";
        }
        catch
        {
            Fail("The response could not be completed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        _host.CancelGeneration();
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
    }

    [RelayCommand]
    public void OpenInAeda()
    {
        _host.OpenInAeda();
        State = AssistPillState.Hidden;
    }

    public void OpenAeda() => _host.OpenInAeda();

    public void Collapse()
    {
        if (IsStreaming)
        {
            Cancel();
            return;
        }

        ShowIdle();
    }

    public void Hide()
    {
        if (IsStreaming)
        {
            Cancel();
            return;
        }

        State = AssistPillState.Hidden;
    }

    partial void OnPromptChanged(string value)
    {
        OnPropertyChanged(nameof(CanSubmit));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnResponseChanged(string value)
    {
        OnPropertyChanged(nameof(HasResponse));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(RenderedResponse));
        CopyResponseCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(AssistPillState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemoveContext));
        SubmitCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RemoveContextCommand.NotifyCanExecuteChanged();
    }

    private void UpdateContextPresentation()
    {
        ContextLabel = _context is null
            ? "No context included"
            : Bound($"{_context.SourceName} · {_context.DisplayTitle}", ContextLabelLimit);
        ContextPreview = _context is null
            ? string.Empty
            : Bound(_context.Preview, ContextPreviewLimit);
        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(CanRemoveContext));
        RemoveContextCommand.NotifyCanExecuteChanged();
    }

    private void SetResponse(string fullResponse)
    {
        _safeFullResponse = RemoveHiddenReasoning(fullResponse);
        Response = Bound(
            _safeFullResponse,
            _settings.ResponsePreviewCharacters);
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
}
