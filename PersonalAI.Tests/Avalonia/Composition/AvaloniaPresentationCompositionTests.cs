using System.Runtime.CompilerServices;
using Avalonia.Controls;
using PersonalAI.Desktop.Avalonia.Composition;

namespace PersonalAI.Tests.Avalonia.Composition;

public sealed class AvaloniaPresentationCompositionTests
{
    [Fact]
    public void RegistrationCarriesRouteViewModelAndLazyContentFactory()
    {
        var viewModel = new object();
        var screen = new AvaloniaPresentationScreen(
            "sample",
            "Sample",
            viewModel,
            () => new ContentControl());

        Assert.Equal("sample", screen.Route);
        Assert.Equal("Sample", screen.Label);
        Assert.Same(viewModel, screen.ViewModel);
        Assert.NotNull(screen.CreateContent);
    }

    [Fact]
    public void AppPassesGenericScreenCompositionWithoutFeatureReferences()
    {
        var app = ReadSource("App.axaml.cs");
        var window = ReadSource("MainWindow.axaml.cs");
        var markup = ReadSource("MainWindow.axaml");

        Assert.Contains(
            "window.AttachComposition(_composition.Chat, _composition.Screens)",
            app);
        Assert.DoesNotContain("TaskCenter", app);
        Assert.Contains("foreach (var screen in screens)", window);
        Assert.Contains("public bool NavigateToPresentation", window);
        Assert.Contains("_screenByRoute.TryGetValue(route", window);
        Assert.Contains("x:Name=\"PresentationRoute\"", markup);
        Assert.Equal(1, Count(markup, "<ContentControl"));
    }

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string ReadSource(
        string relativePath,
        [CallerFilePath] string testFilePath = "")
    {
        // <repo>/PersonalAI.Tests/Avalonia/Composition/<this file>
        var directory = Path.GetDirectoryName(testFilePath)!;
        var repositoryRoot = Path.GetFullPath(
            Path.Combine(directory, "..", "..", ".."));
        var path = Path.Combine(
            repositoryRoot,
            "PersonalAI.Desktop.Avalonia",
            relativePath);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }
}
