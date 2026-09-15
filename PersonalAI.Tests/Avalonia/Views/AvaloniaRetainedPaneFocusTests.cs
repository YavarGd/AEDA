using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PersonalAI.Desktop.Avalonia.Views.Memory;
using PersonalAI.Desktop.Avalonia.Views.Tasks;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaRetainedPaneFocusTests
{
    private const string ProbeVariable = "AEDA_D09_FOCUS_PROBE";

    [Fact]
    public async Task TaskCenterReentryFocusesTheRetainedVisiblePaneWithoutResizeFocusSteal()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "tasks")
        {
            RunTaskCenterProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync(
            nameof(TaskCenterReentryFocusesTheRetainedVisiblePaneWithoutResizeFocusSteal),
            "tasks");
    }

    [Fact]
    public async Task MemoryReentryPreservesEveryRetainedPresentationWithoutResizeFocusSteal()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "memory")
        {
            RunMemoryProbe();
            return;
        }

        await RunProbeInIsolatedProcessAsync(
            nameof(MemoryReentryPreservesEveryRetainedPresentationWithoutResizeFocusSteal),
            "memory");
    }

    private static void RunTaskCenterProbe()
    {
        StartApplication();
        var view = new TaskCenterView();
        var sentinel = new Button { Content = "sentinel" };
        var window = Show(view, sentinel);

        AssertResponsiveDoesNotMoveFocus(
            sentinel,
            () => view.ApplyResponsiveMode(compact: true, medium: false),
            () => view.ApplyResponsiveMode(compact: false, medium: false));

        AssertTaskCenterFocus(view, compact: false, medium: false, "Overview", "RefreshButton");
        AssertTaskCenterFocus(view, compact: false, medium: true, "Overview", "RefreshButton");
        AssertTaskCenterFocus(view, compact: true, medium: false, "Overview", "CompactOverview");
        AssertTaskCenterFocus(view, compact: true, medium: false, "Approvals", "ApprovalSurface");
        AssertTaskCenterFocus(view, compact: true, medium: false, "Active", "ActiveSurface");
        AssertTaskCenterFocus(view, compact: true, medium: false, "Recent", "RecentSurface");
        AssertTaskCenterFocus(view, compact: true, medium: false, "Failed", "FailedSurface");
        AssertTaskCenterFocus(view, compact: true, medium: false, "SelectedTask", "SelectedTaskHeading");
        window.Close();
    }

    private static void RunMemoryProbe()
    {
        StartApplication();
        var view = new MemoryView();
        var sentinel = new Button { Content = "sentinel" };
        var window = Show(view, sentinel);

        AssertResponsiveDoesNotMoveFocus(
            sentinel,
            () => view.ApplyResponsiveMode(compact: true, medium: false),
            () => view.ApplyResponsiveMode(compact: false, medium: false));

        AssertMemoryFocus(view, false, false, "Recent", "Overview", "MemorySearchTextBox");
        AssertMemoryFocus(view, false, true, "TaskOutcomes", "Overview", "MemorySearchTextBox");
        AssertMemoryFocus(view, false, false, "IndexedKnowledge", "Overview", "IndexedKnowledgeHeading");
        AssertMemoryFocus(view, false, true, "IndexedKnowledge", "Overview", "IndexedKnowledgeHeading");
        AssertMemoryFocus(view, true, false, "Recent", "Overview", "CompactOverview");
        AssertMemoryFocus(view, true, false, "Recent", "MemoryList", "MemorySearchTextBox");
        AssertMemoryFocus(view, true, false, "TaskOutcomes", "MemoryList", "MemorySearchTextBox");
        AssertMemoryFocus(view, true, false, "Recent", "SelectedDetail", "SelectedMemoryDetailHeading");
        AssertMemoryFocus(view, true, false, "IndexedKnowledge", "IndexedKnowledge", "IndexedKnowledgeHeading");
        AssertMemoryFocus(view, true, false, "Recent", "AddMemory", "AddMemoryTextBox");
        AssertMemoryFocus(view, true, false, "Recent", "Retrieval", "RetrievalQueryTextBox");
        window.Close();
    }

    private static void AssertTaskCenterFocus(
        TaskCenterView view,
        bool compact,
        bool medium,
        string pane,
        string expectedTarget)
    {
        view.ApplyResponsiveMode(compact, medium);
        SetPrivateEnum(view, "_compactPane", pane);
        view.ApplyResponsiveMode(compact, medium);

        var target = view.FindControl<Control>(expectedTarget)!;
        var candidates = view.PrimaryFocusTargets();
        Assert.Same(target, candidates[0]);
        Assert.True(target.IsEffectivelyVisible);

        view.FocusPrimaryAction();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(pane, GetPrivateEnum(view, "_compactPane"));
        var focused = Assert.IsAssignableFrom<Control>(
            TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement());
        Assert.Contains(focused, candidates);
        Assert.True(focused.IsEffectivelyVisible);
        if (compact && pane != "Overview")
        {
            Assert.False(view.FindControl<Control>("RefreshButton")!.IsEffectivelyVisible);
        }
    }

    private static void AssertMemoryFocus(
        MemoryView view,
        bool compact,
        bool medium,
        string source,
        string pane,
        string expectedTarget)
    {
        view.ApplyResponsiveMode(compact, medium);
        SetPrivateEnum(view, "_source", source);
        SetPrivateEnum(view, "_compactPane", pane);
        view.ApplyResponsiveMode(compact, medium);

        var target = view.FindControl<Control>(expectedTarget)!;
        Assert.Same(target, view.PrimaryFocusTargets()[0]);
        Assert.True(target.IsEffectivelyVisible);

        view.FocusPrimaryAction();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(source, GetPrivateEnum(view, "_source"));
        Assert.Equal(pane, GetPrivateEnum(view, "_compactPane"));
        Assert.Same(target, TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement());
    }

    private static void AssertResponsiveDoesNotMoveFocus(
        Button sentinel,
        params Action[] transitions)
    {
        Assert.True(sentinel.Focus());
        foreach (var transition in transitions)
        {
            transition();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(
                sentinel,
                TopLevel.GetTopLevel(sentinel)!.FocusManager!.GetFocusedElement());
        }
    }

    private static Window Show(Control view, Control sentinel)
    {
        var content = new Grid();
        content.Children.Add(view);
        content.Children.Add(sentinel);
        var window = new Window
        {
            Width = 1200,
            Height = 900,
            ShowInTaskbar = false,
            Content = content
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void StartApplication() =>
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

    private static void SetPrivateEnum(object target, string fieldName, string value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static string GetPrivateEnum(object target, string fieldName) =>
        target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!.ToString()!;

    private static async Task RunProbeInIsolatedProcessAsync(string testName, string probe)
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
        startInfo.ArgumentList.Add(typeof(AvaloniaRetainedPaneFocusTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaRetainedPaneFocusTests).FullName + "." + testName);
        startInfo.Environment[ProbeVariable] = probe;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }
}
