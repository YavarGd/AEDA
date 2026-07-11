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
    private Task? _generationTask;
    private PersonalAI.Core.Context.AttachedContextItem? _context;
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
    private string _statusText = "Ready";

    public bool IsEnabled => _settings.Enabled;

    public bool IsIdle => State == AssistPillState.IdlePill;

    public bool IsExpanded => State is not AssistPillState.Hidden and not AssistPillState.IdlePill;

    public bool IsFallbackInput => State == AssistPillState.SpotlightPrompt;

    public bool IsResponseSurface => State is AssistPillState.StreamingResponse or
        AssistPillState.Completed or AssistPillState.Cancelled or AssistPillState.Failed;

    public bool IsStreaming => State == AssistPillState.StreamingResponse;

    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);

    public bool CanSubmit => IsEnabled && IsFallbackInput &&
        !string.IsNullOrWhiteSpace(Prompt);

    public bool CanCancel => IsStreaming;

    public bool CanCopy => HasResponse;

    public bool CanShowResponseActions => HasResponse && !IsStreaming;

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
            StatusText = "Checking available context";
            try
            {
                _context = await _host.CaptureContextAsync(cancellationToken);
            }
            catch
            {
                _context = null;
            }

            if (AssistContextPolicy.IsMeaningful(_context, DateTimeOffset.UtcNow))
            {
                Prompt = AutomaticContextPrompt;
                _ = StartGenerationAsync(AutomaticContextPrompt);
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

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    public async Task SubmitAsync()
    {
        var prompt = Prompt.Trim();
        if (!CanSubmit)
        {
            return;
        }

        await StartGenerationAsync(prompt);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel() => _generationCancellation?.Cancel();

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
    public async Task OpenInAedaAsync()
    {
        await _host.OpenInAedaAsync();
        State = AssistPillState.Hidden;
    }

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
    }

    partial void OnStateChanged(AssistPillState value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsFallbackInput));
        OnPropertyChanged(nameof(IsResponseSurface));
        OnPropertyChanged(nameof(IsStreaming));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanShowResponseActions));
        SubmitCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private Task StartGenerationAsync(string prompt)
    {
        if (_generationTask is { IsCompleted: false })
        {
            return _generationTask;
        }

        _rawResponse.Clear();
        SetResponse(string.Empty);
        StatusText = "Generating";
        State = AssistPillState.StreamingResponse;
        var cancellation = new CancellationTokenSource();
        _generationCancellation = cancellation;
        _generationTask = RunGenerationAsync(prompt, cancellation);
        return _generationTask;
    }

    private async Task RunGenerationAsync(
        string prompt,
        CancellationTokenSource cancellation)
    {
        try
        {
            var result = await _host.GenerateAsync(
                prompt,
                _context,
                AppendResponseChunk,
                cancellation.Token);
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
            State = AssistPillState.Cancelled;
            StatusText = "Cancelled";
        }
        catch
        {
            Fail("The response could not be completed.");
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

    private void AppendResponseChunk(string chunk)
    {
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
}
