using System.Runtime.CompilerServices;

namespace PersonalAI.Tests.Avalonia.Memory;

public sealed class AvaloniaAedaMemoryViewTests
{
    [Fact]
    public void ViewPresentsSearchCreateRetrievalAndDetailStatesWithSafeMessaging()
    {
        var source = ReadSource("Views", "Memory", "MemoryView.axaml");

        // Search state
        Assert.Contains("Text=\"{Binding SearchText}\"", source);
        Assert.Contains("Command=\"{Binding SearchMemoriesCommand}\"", source);
        Assert.Contains("ItemsSource=\"{Binding SearchResults}\"", source);
        Assert.Contains("No memories matched your search.", source);

        // Detail state
        Assert.Contains("IsVisible=\"{Binding HasSelectedMemory}\"", source);
        Assert.Contains("IsVisible=\"{Binding HasNoSelectedMemory}\"", source);
        Assert.Contains("Text=\"{Binding SelectedMemory.Text}\"", source);

        // Create state
        Assert.Contains("Text=\"{Binding NewMemoryText}\"", source);
        Assert.Contains("Text=\"{Binding NewMemorySourceReason}\"", source);
        Assert.Contains("Command=\"{Binding CreateExplicitMemoryCommand}\"", source);

        // Retrieval state
        Assert.Contains("Text=\"{Binding RetrievalQuery}\"", source);
        Assert.Contains("Command=\"{Binding PreviewRetrievalCommand}\"", source);
        Assert.Contains("ItemsSource=\"{Binding RetrievalPreview}\"", source);
        Assert.Contains("No retrieval preview results yet.", source);

        // Empty states
        Assert.Contains("IsVisible=\"{Binding HasNoRecentMemories}\"", source);
        Assert.Contains("IsVisible=\"{Binding HasNoTaskOutcomes}\"", source);
        Assert.Contains("IsVisible=\"{Binding HasNoDocuments}\"", source);

        // Local-only / privacy messaging
        Assert.Contains("Text=\"{Binding PrivacyStatusText}\"", source);
        Assert.Contains("Text=\"{Binding SafeStatusMessage}\"", source);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", source);

        // Accessible lists
        Assert.Contains("AutomationProperties.Name=\"Search results\"", source);
        Assert.Contains("AutomationProperties.Name=\"Recent memories\"", source);
        Assert.Contains("AutomationProperties.Name=\"Task outcomes\"", source);
        Assert.Contains("AutomationProperties.Name=\"Indexed documents\"", source);
        Assert.Contains("AutomationProperties.Name=\"Retrieval preview results\"", source);
        Assert.Contains("AutomationProperties.Name=\"Memory\"", source);
    }

    [Fact]
    public void ViewSupportsPerRecordArchiveAndDeleteActions()
    {
        var source = ReadSource("Views", "Memory", "MemoryView.axaml");

        Assert.Contains("Click=\"OnOpenMemoryClick\"", source);
        Assert.Contains("Click=\"OnArchiveMemoryClick\"", source);
        Assert.Contains("Click=\"OnDeleteMemoryClick\"", source);
    }

    [Fact]
    public void CodeBehindWiresRecordActionsToViewModelCommands()
    {
        var source = ReadSource("Views", "Memory", "MemoryView.axaml.cs");

        Assert.Contains("viewModel.OpenMemoryDetailAsync(summary)", source);
        Assert.Contains("viewModel.ArchiveMemoryAsync(summary)", source);
        Assert.Contains("viewModel.DeleteMemoryAsync(summary)", source);
        Assert.Contains("viewModel.InitializeAsync()", source);
    }

    [Fact]
    public void CompositionRegistersAccessibleMemoryRoute()
    {
        var source = ReadSource("Composition", "AvaloniaAppComposition.cs");

        Assert.Contains("\"aeda-memory\"", source);
        Assert.Contains("\"Memory\"", source);
        Assert.Contains("new AedaMemoryModuleViewModel(runtime.MemoryModule, runtime.ModuleRegistry)", source);
        Assert.Contains("() => new MemoryView()", source);
    }

    private static string ReadSource(
        params string[] relativePath)
    {
        var repositoryRoot = GetRepositoryRoot();
        var path = Path.Combine(
            [repositoryRoot, "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot(
        [CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Memory/<this file>
        var memoryDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(memoryDirectory, "..", "..", ".."));
    }
}
