using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Dashboard;

namespace PersonalAI.Tests.Avalonia.Dashboard;

public sealed class AvaloniaDashboardViewTests
{
    [Fact]
    public void DashboardCopyIsNoviceFriendlyAndDoesNotDescribeModulesAsDeferred()
    {
        var source = ReadDashboardMarkup();

        // Code, Memory, Research, Task Center and Assist are migrated and reachable from
        // the navigation list, so the Dashboard must not describe them as still pending.
        Assert.DoesNotContain("arrive in later steps", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("later step", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not available yet", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Coming later", source, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Text=\"What would you like to work on?\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Text=\"Choose a place to start. You can return Home at any time.\"",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardContainsSixCapabilityCardsInApprovedOrder()
    {
        var document = XDocument.Parse(ReadDashboardMarkup());
        XNamespace ns = "https://github.com/avaloniaui";
        var cards = document.Descendants(ns + "Button")
            .Where(element => Classes(element).Contains("capabilityCard"))
            .ToArray();

        Assert.Equal(6, cards.Length);
        Assert.Equal(
        [
            "Open Chat — Ask questions and get help",
            "Open Code — Review and improve code safely",
            "Open Memory — View what AEDA remembers",
            "Open Research — Find and verify information",
            "Open Task Center — Track work and approvals",
            "Open Assist — Get help in another application"
        ], cards.Select(card => Attribute(card, "AutomationProperties.Name")));
        Assert.Equal(6, cards.Select(card => Attribute(card, "AutomationProperties.Name")).Distinct().Count());
    }

    [Fact]
    public void PrimaryActionAndCardsRemainReadableAndKeyboardReachable()
    {
        var document = XDocument.Parse(ReadDashboardMarkup());
        XNamespace ns = "https://github.com/avaloniaui";
        var buttons = document.Descendants(ns + "Button").ToArray();
        var cards = buttons.Where(element => Classes(element).Contains("capabilityCard")).ToArray();

        Assert.Equal("StartConversationButton", Attribute(buttons[0], "Name"));
        Assert.All(cards, card =>
        {
            Assert.DoesNotContain("IsEnabled", card.Attributes().Select(attribute => attribute.Name.LocalName));
            Assert.All(card.Descendants(ns + "TextBlock"), text =>
            {
                Assert.False(string.IsNullOrWhiteSpace(Attribute(text, "FontSize")));
                Assert.Equal("Wrap", Attribute(text, "TextWrapping"));
            });
        });
        Assert.All(document.Descendants(ns + "Path"), path =>
            Assert.Equal("False", Attribute(path, "IsHitTestVisible")));
    }

    [Fact]
    public void DashboardUsesOnlyLiveChatDataAndSemanticResources()
    {
        var markup = ReadDashboardMarkup();
        var code = ReadSource("Views", "Dashboard", "DashboardView.axaml.cs");
        var combined = markup + code;
        var resources = Regex.Matches(markup, @"DynamicResource ([A-Za-z]+)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        var approvedResources = new[]
        {
            "CardSurfaceBrush", "BorderBrush", "AccentBrush", "ElevatedSurfaceBrush",
            "SubtleSurfaceBrush", "SecondaryTextBrush", "AccentSoftBrush"
        };

        Assert.Contains("x:DataType=\"vm:AvaloniaChatViewModel\"", markup);
        Assert.Contains("{Binding Conversations.Count}", markup);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", markup);
        Assert.Contains("ChatPresentation.DescribeStatus", code);
        Assert.Single(Regex.Matches(markup, "<ScrollViewer").Cast<Match>());
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", combined);
        Assert.DoesNotContain("Running locally", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recent", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Continue where", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("133", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("1,248", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AedaModuleDashboardViewModel", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AedaShellNavigationState", combined, StringComparison.Ordinal);
        Assert.All(resources, resource => Assert.Contains(resource, approvedResources));
    }

    [Fact]
    public void DashboardCardsUseExistingChatAndBoundedModuleIntents()
    {
        var document = XDocument.Parse(ReadDashboardMarkup());
        XNamespace ns = "https://github.com/avaloniaui";
        var cards = document.Descendants(ns + "Button")
            .Where(element => Classes(element).Contains("capabilityCard"))
            .ToArray();
        var code = ReadSource("Views", "Dashboard", "DashboardView.axaml.cs");

        Assert.Equal("OnOpenChatClick", Attribute(cards[0], "Click"));
        Assert.Equal(
        [
            "aeda-code", "aeda-memory", "aeda-research", "aeda-task-center", "aeda-assist"
        ], cards.Skip(1).Select(card => Attribute(card, "Tag")));
        Assert.All(cards.Skip(1), card => Assert.Equal("OnOpenModuleClick", Attribute(card, "Click")));
        Assert.Contains("OpenChatRequested?.Invoke", code);
        Assert.Contains("OpenModuleRequested?.Invoke", code);
        Assert.Contains("IsSupportedModuleRoute(route)", code);
    }

    [Theory]
    [InlineData("aeda-code", true)]
    [InlineData("aeda-memory", true)]
    [InlineData("aeda-research", true)]
    [InlineData("aeda-task-center", true)]
    [InlineData("aeda-assist", true)]
    [InlineData("settings", false)]
    [InlineData("arbitrary-route", false)]
    public void ModuleRouteValidationIsClosedAndDeterministic(string route, bool expected) =>
        Assert.Equal(expected, DashboardView.IsSupportedModuleRoute(route));

    [Fact]
    public void ResponsiveLayoutsMatchTheApprovedThreeTwoOneGeometry()
    {
        var wide = DashboardView.ResolveLayout(compact: false, medium: false);
        var medium = DashboardView.ResolveLayout(compact: false, medium: true);
        var compact = DashboardView.ResolveLayout(compact: true, medium: false);

        Assert.Equal((1180d, 32d, 32d, 20d, 132d, 64d, 28d, 3), wide);
        Assert.Equal((900d, 24d, 26d, 16d, 124d, 60d, 24d, 2), medium);
        Assert.Equal((640d, 16d, 20d, 12d, 104d, 56d, 21d, 1), compact);
    }

    [Fact]
    public void MainWindowBridgesDashboardModuleIntentToExistingPresentationRouting()
    {
        var source = ReadSource("MainWindow.axaml.cs");

        Assert.Contains("DashboardRoute.OpenModuleRequested +=", source);
        Assert.Contains("NavigateToPresentation(e.Route, focusContent: true)", source);
        Assert.Contains("DashboardRoute.ApplyResponsiveMode(compact, medium)", source);
        Assert.DoesNotContain("NavigationService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardFocusAndIconsReuseTheBoundedExistingSeams()
    {
        var dashboard = ReadSource("Views", "Dashboard", "DashboardView.axaml.cs");
        var window = ReadSource("MainWindow.axaml.cs");
        var iconCatalog = ReadSource("RouteIconCatalog.cs");

        Assert.Contains("FocusPrimaryAction() => StartConversationButton.Focus()", dashboard);
        Assert.Contains("RouteIconCatalog.Get", dashboard);
        Assert.Contains("RouteIconCatalog.Get(route)", window);
        Assert.Contains("[\"chat\"]", iconCatalog);
        Assert.Contains("Icons.GetValueOrDefault(route, Icons[\"module\"])", iconCatalog);
    }

    private static string ReadDashboardMarkup() =>
        ReadSource("Views", "Dashboard", "DashboardView.axaml");

    private static string[] Classes(XElement element) =>
        (Attribute(element, "Classes") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == localName ||
            $"{attribute.Name.NamespaceName}:{attribute.Name.LocalName}" == localName)?.Value ?? string.Empty;

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
        // <repo>/PersonalAI.Tests/Avalonia/Dashboard/<this file>
        var dashboardDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(dashboardDirectory, "..", "..", ".."));
    }
}
