using System.Collections.Specialized;
using System.ComponentModel;
using PersonalAI.Core.Context;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Services;

public interface IAssistPillHost
{
    bool IsGenerating { get; }

    Task<AttachedContextItem?> CaptureContextAsync(CancellationToken cancellationToken);

    Task<ChatStatus> GenerateAsync(
        string prompt,
        AttachedContextItem? context,
        Action<string> reportResponse,
        CancellationToken cancellationToken);

    void CancelGeneration();

    Task CopyTextAsync(string text, CancellationToken cancellationToken);

    void OpenInAeda();
}

public sealed class AssistPillHost(
    MainViewModel mainViewModel,
    ActiveWindowContextService contextService,
    IClipboardWriter clipboardWriter,
    Action showMainWindow) : IAssistPillHost
{
    public bool IsGenerating => mainViewModel.IsGenerating;

    public Task<AttachedContextItem?> CaptureContextAsync(
        CancellationToken cancellationToken) =>
        contextService.CaptureAsync(cancellationToken);

    public async Task<ChatStatus> GenerateAsync(
        string prompt,
        AttachedContextItem? context,
        Action<string> reportResponse,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await mainViewModel.NewChatAsync();

        if (context is not null && !mainViewModel.TryAttachContext(context))
        {
            throw new InvalidOperationException("assist_context_attach_failed");
        }

        ChatMessageViewModel? assistant = null;
        PropertyChangedEventHandler messageChanged = (_, args) =>
        {
            if (args.PropertyName == nameof(ChatMessageViewModel.Content) &&
                assistant is not null)
            {
                reportResponse(assistant.Content);
            }
        };
        NotifyCollectionChangedEventHandler messagesChanged = (_, args) =>
        {
            var next = args.NewItems?
                .OfType<ChatMessageViewModel>()
                .LastOrDefault(message => message.IsAssistantMessage);
            if (next is null)
            {
                return;
            }

            if (assistant is not null)
            {
                assistant.PropertyChanged -= messageChanged;
            }

            assistant = next;
            assistant.PropertyChanged += messageChanged;
            reportResponse(assistant.Content);
        };

        mainViewModel.Messages.CollectionChanged += messagesChanged;
        try
        {
            mainViewModel.Prompt = prompt;
            await mainViewModel.SendMessageAsync();
            if (assistant is not null)
            {
                reportResponse(assistant.Content);
            }

            return mainViewModel.Status;
        }
        finally
        {
            mainViewModel.Messages.CollectionChanged -= messagesChanged;
            if (assistant is not null)
            {
                assistant.PropertyChanged -= messageChanged;
            }
        }
    }

    public void CancelGeneration() => mainViewModel.CancelGeneration();

    public Task CopyTextAsync(string text, CancellationToken cancellationToken) =>
        clipboardWriter.CopyTextAsync(text, cancellationToken);

    public void OpenInAeda()
    {
        mainViewModel.OpenChat();
        showMainWindow();
    }
}
