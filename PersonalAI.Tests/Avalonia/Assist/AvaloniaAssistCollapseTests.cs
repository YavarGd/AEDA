using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.Views.Assist;
using PersonalAI.Desktop.Presentation.Services;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Tests.Avalonia.Assist;

public sealed class AvaloniaAssistCollapseTests
{
    private const string ProbeVariable = "AEDA_G03_ASSIST_COLLAPSE_PROBE";

    [Fact]
    public async Task FloatingAssistCanRepeatedlyCollapseToItsAccessiblePill()
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == "run")
        {
            await RunProbeAsync();
            return;
        }

        await RunProbeInIsolatedProcessAsync();
    }

    private static async Task RunProbeAsync()
    {
        StartApplication();
        var viewModel = new AssistPillViewModel(new FakeHost(), AssistPillSettings.Default);
        var floating = new AssistView
        {
            HostMode = AssistViewHostMode.CompactWindow,
            DataContext = viewModel
        };
        var module = new AssistView { DataContext = viewModel };
        var window = new Window
        {
            Width = 900,
            Height = 500,
            ShowInTaskbar = false,
            Content = new StackPanel { Children = { floating, module } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var pill = floating.FindControl<Button>("CompactPill")!;
        var collapse = floating.FindControl<Button>("CollapseButton")!;
        Assert.True(viewModel.IsIdle);
        Assert.True(pill.IsEffectivelyVisible);
        Assert.False(collapse.IsVisible);

        Assert.True(await viewModel.OpenPromptAsync());
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsExpanded);
        Assert.False(pill.IsVisible);
        Assert.True(collapse.IsEffectivelyVisible);
        Assert.True(collapse.Focusable);
        Assert.Equal("Collapse Assist", AutomationProperties.GetName(collapse));
        Assert.True(collapse.Focus());
        Assert.Same(collapse, window.FocusManager!.GetFocusedElement());

        viewModel.Prompt = "preserved draft";
        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsIdle);
        Assert.True(pill.IsEffectivelyVisible);
        Assert.Equal("preserved draft", viewModel.Prompt);
        Assert.True(module.FindControl<Control>("FullSurface")!.IsEffectivelyVisible);
        Assert.False(module.FindControl<Control>("CompactPill")!.IsVisible);

        Assert.True(await viewModel.OpenPromptAsync());
        Dispatcher.UIThread.RunJobs();
        var escape = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape
        };
        floating.RaiseEvent(escape);
        Dispatcher.UIThread.RunJobs();
        Assert.True(escape.Handled);
        Assert.True(viewModel.IsIdle);
        Assert.True(pill.IsEffectivelyVisible);

        Assert.True(await viewModel.OpenPromptAsync());
        Dispatcher.UIThread.RunJobs();
        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.IsIdle);
        Assert.True(pill.IsEffectivelyVisible);

        window.Close();
        await viewModel.DisposeAsync();
    }

    private static void StartApplication() =>
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

    private static async Task RunProbeInIsolatedProcessAsync()
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
        startInfo.ArgumentList.Add(typeof(AvaloniaAssistCollapseTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaAssistCollapseTests).FullName + "." +
            nameof(FloatingAssistCanRepeatedlyCollapseToItsAccessiblePill));
        startInfo.Environment[ProbeVariable] = "run";

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private sealed class FakeHost : IAssistPillHost
    {
        public string? LastCaptureFailureMessage => null;

        public Task<AttachedContextItem?> CaptureContextAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AttachedContextItem?>(null);

        public Task<AttachedContextItem?> CaptureScreenTextAsync(CancellationToken cancellationToken) =>
            Task.FromResult<AttachedContextItem?>(null);

        public Task<AssistGenerationResult> GenerateAsync(
            string prompt,
            AttachedContextItem? context,
            Action<string> reportChunk,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AssistGenerationResult(ChatStatus.Completed));

        public Task CopyTextAsync(string text, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task OpenInAedaAsync() => Task.CompletedTask;
    }
}
