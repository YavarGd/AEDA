using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia;
using PersonalAI.Desktop.Avalonia.Composition;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaShellRestoreTests
{
    private const string ProbeVariable = "AEDA_D11_RESTORE_PROBE";

    [Fact]
    public Task D11_FirstShowStartsOnHome() => RunScenarioAsync("first-show");

    [Fact]
    public Task D11_HideShowPreservesTaskCenter() => RunScenarioAsync("hide-show");

    [Fact]
    public Task D11_ActivationRestorePreservesTaskCenter() => RunScenarioAsync("activation");

    [Fact]
    public Task D11_MultipleRestoresPreserveMemory() => RunScenarioAsync("multiple");

    [Fact]
    public Task D11_NavigationAfterRestoreWorksSettings() => RunScenarioAsync("navigate-after");

    [Fact]
    public Task D11_ExplicitNewChatKeepsItsExistingBehavior() => RunScenarioAsync("new-chat");

    [Fact]
    public Task D11_HiddenStartupInitializesHomeOnFirstActivation() => RunScenarioAsync("hidden-start");

    private static async Task RunScenarioAsync(
        string scenario,
        [CallerMemberName] string testName = "")
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == scenario)
        {
            RunScenario(scenario);
            return;
        }

        await RunProbeInIsolatedProcessAsync(testName, scenario);
    }

    private static void RunScenario(string scenario)
    {
        StartApplication();
        var (window, chat) = CreateWindow();

        try
        {
            switch (scenario)
            {
                case "first-show":
                    window.Show();
                    Pump();
                    AssertHome(window);
                    break;
                case "hide-show":
                    ShowAndNavigate(window, "aeda-task-center");
                    window.Hide();
                    Pump();
                    window.Show();
                    Pump();
                    AssertPresentation(window, "aeda-task-center", "Task Center");
                    break;
                case "activation":
                    ShowAndNavigate(window, "aeda-task-center");
                    var activation = new AvaloniaWindowActivationService(window);
                    activation.Hide();
                    Pump();
                    activation.ShowRestoreAndActivate();
                    Pump();
                    AssertPresentation(window, "aeda-task-center", "Task Center");
                    break;
                case "multiple":
                    ShowAndNavigate(window, "aeda-memory");
                    for (var index = 0; index < 3; index++)
                    {
                        window.Hide();
                        Pump();
                        window.Show();
                        Pump();
                        AssertPresentation(window, "aeda-memory", "Memory");
                    }
                    break;
                case "navigate-after":
                    ShowAndNavigate(window, "aeda-task-center");
                    window.Hide();
                    Pump();
                    window.Show();
                    Pump();
                    Assert.True(window.NavigateToPresentation("settings"));
                    AssertPresentation(window, "settings", "Settings");
                    break;
                case "new-chat":
                    ShowAndNavigate(window, "settings");
                    chat.Draft = "pending";
                    window.OpenChat(newChat: true);
                    Assert.Equal(string.Empty, chat.Draft);
                    AssertChat(window);
                    break;
                case "hidden-start":
                    Assert.False(window.IsVisible);
                    var hiddenActivation = new AvaloniaWindowActivationService(window);
                    hiddenActivation.ShowRestoreAndActivate();
                    Pump();
                    AssertHome(window);
                    Assert.True(window.NavigateToPresentation("aeda-memory"));
                    hiddenActivation.Hide();
                    Pump();
                    hiddenActivation.ShowRestoreAndActivate();
                    Pump();
                    AssertPresentation(window, "aeda-memory", "Memory");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void ShowAndNavigate(MainWindow window, string route)
    {
        window.Show();
        Pump();
        Assert.True(window.NavigateToPresentation(route));
        Pump();
    }

    private static void AssertHome(MainWindow window)
    {
        Assert.Equal(ShellRoute.Dashboard, window.CurrentRoute);
        Assert.Equal("Home", window.FindControl<TextBlock>("PageTitle")!.Text);
        Assert.Equal(
            "Home",
            AutomationProperties.GetName(
                Assert.IsType<ListBoxItem>(window.FindControl<ListBox>("NavigationList")!.SelectedItem)));
        Assert.True(window.FindControl<Control>("DashboardRoute")!.IsVisible);
        Assert.False(window.FindControl<Control>("PresentationRoute")!.IsVisible);
    }

    private static void AssertChat(MainWindow window)
    {
        Assert.Equal(ShellRoute.Chat, window.CurrentRoute);
        Assert.Equal("General Chat", window.FindControl<TextBlock>("PageTitle")!.Text);
        Assert.Equal(
            "General chat",
            AutomationProperties.GetName(
                Assert.IsType<ListBoxItem>(window.FindControl<ListBox>("NavigationList")!.SelectedItem)));
        Assert.True(window.FindControl<Control>("ChatRoute")!.IsVisible);
        Assert.False(window.FindControl<Control>("PresentationRoute")!.IsVisible);
    }

    private static void AssertPresentation(MainWindow window, string route, string label)
    {
        Assert.Equal(ShellRoute.Presentation, window.CurrentRoute);
        Assert.Equal(label, window.FindControl<TextBlock>("PageTitle")!.Text);
        Assert.Equal(
            label,
            AutomationProperties.GetName(
                Assert.IsType<ListBoxItem>(window.FindControl<ListBox>("NavigationList")!.SelectedItem)));
        var host = window.FindControl<ContentControl>("PresentationRoute")!;
        Assert.True(host.IsVisible);
        Assert.Equal(route, Assert.IsType<Border>(host.Content).Tag);
    }

    private static (MainWindow Window, AvaloniaChatViewModel Chat) CreateWindow()
    {
        var chat = new AvaloniaChatViewModel(
            new ConversationSessionService(
                new EmptyConversationRepository(),
                new ChatSessionService(new EmptyChatProvider())),
            new TestSettings(),
            action => action());
        var window = new MainWindow
        {
            Width = 1200,
            Height = 900,
            ShowInTaskbar = false
        };
        window.AttachComposition(
            chat,
            [
                Screen("aeda-memory", "Memory"),
                Screen("aeda-task-center", "Task Center"),
                Screen("settings", "Settings")
            ]);
        return (window, chat);
    }

    private static AvaloniaPresentationScreen Screen(string route, string label) =>
        new(route, label, new object(), () => new Border { Tag = route });

    private static void StartApplication() =>
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static async Task RunProbeInIsolatedProcessAsync(string testName, string scenario)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(AvaloniaShellRestoreTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaShellRestoreTests).FullName + "." + testName);
        startInfo.Environment[ProbeVariable] = scenario;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private sealed class EmptyChatProvider : IChatProvider
    {
        public string ProviderName => "test";

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyConversationRepository : IConversationRepository
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);

        public Task<Conversation?> GetConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Conversation?>(null);

        public Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredChatMessage>>([]);

        public Task<Conversation> CreateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(conversation);

        public Task UpdateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<StoredChatMessage> AddMessageAsync(
            StoredChatMessage message,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(message);
    }

    private sealed class TestSettings : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; } = ApplicationSettings.CreateDefault();

        public string SettingsPath => string.Empty;

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
