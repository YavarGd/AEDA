using PersonalAI.Core.Permissions;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Avalonia.Views.Research;
using PersonalAI.Desktop.Avalonia.Views.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Hosting;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed class AvaloniaAppComposition : IAsyncDisposable
{
    private AvaloniaAppComposition(AedaRuntime runtime, Action<Action> dispatch)
    {
        Runtime = runtime;
        Chat = new AvaloniaChatViewModel(runtime.ConversationSession, runtime.Settings, dispatch);
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
                content => ((ResearchView)content).FocusPrimaryAction())
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
