using Avalonia.Controls;
using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Assist;
using PersonalAI.Desktop.Avalonia.Views.Capture;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Avalonia.Views.Research;
using PersonalAI.Desktop.Avalonia.Views.Tasks;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Hosting;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaAppComposition : IAsyncDisposable
{
    private AvaloniaAppComposition(AedaRuntime runtime, Action<Action> dispatch)
    {
        Runtime = runtime;
        Chat = new AvaloniaChatViewModel(runtime.ConversationSession, runtime.Settings, dispatch);

        // The view is created lazily by the screen factory, which the shell invokes on the
        // UI thread. Avalonia controls must not be constructed here, because composition
        // runs before the dispatcher hand-off.
        AssistView? assistView = null;
        var assistPillHost = new AssistPillHost(
            runtime.ConversationSession,
            runtime.Settings,
            new PersonalAI.Core.Chat.DeterministicChatModelRouter(),
            runtime.CheckCurrentProviderAsync,
            runtime.ListCurrentModelsAsync,
            new AvaloniaAssistContextService(),
            new AvaloniaScreenTextCaptureService(
                () => TopLevel.GetTopLevel(assistView)?.Screens?.All ?? []),
            () => null,
            new AvaloniaClipboardWriter(() => assistView),
            conversationId => conversationId is { } id
                ? Chat.OpenConversationAsync(id)
                : Task.CompletedTask);
        var assistPillViewModel = new AssistPillViewModel(
            assistPillHost,
            runtime.Settings.Current.AssistPill);

        Screens =
        [
            new AvaloniaPresentationScreen(
                "aeda-task-center",
                "Task Center",
                new AedaTaskCenterViewModel(runtime.TaskCenter),
                () => new TaskCenterView(),
                content => ((TaskCenterView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-memory",
                "Memory",
                new AedaMemoryModuleViewModel(runtime.MemoryModule, runtime.ModuleRegistry),
                () => new MemoryView(),
                content => ((MemoryView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-research",
                "Research",
                new AedaResearchModuleViewModel(runtime.ResearchModule, runtime.ModuleRegistry),
                () => new ResearchView(),
                content => ((ResearchView)content).FocusPrimaryAction()),
            new AvaloniaPresentationScreen(
                "aeda-assist",
                "Assist",
                assistPillViewModel,
                () => assistView ??= new AssistView(),
                content => ((AssistView)content).FocusPrimaryAction())
        ];
    }

    public AedaRuntime Runtime { get; }

    public AvaloniaChatViewModel Chat { get; }

    public IReadOnlyList<AvaloniaPresentationScreen> Screens { get; }

    public static async Task<AvaloniaAppComposition> CreateAsync(Action<Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var runtime = await AedaRuntime.CreateAsync(new DenyingPermissionBroker());
        return new AvaloniaAppComposition(runtime, dispatch);
    }

    public async ValueTask DisposeAsync()
    {
        Chat.Dispose();
        await Runtime.DisposeAsync();
    }
}
