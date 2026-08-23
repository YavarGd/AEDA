using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Chat;

public sealed class AvaloniaGeneralChatDesignTests
{
    [Fact]
    public void ConversationRailKeepsTheLiveSearchNewChatAndSelectionSeams()
    {
        var markup = ReadChatMarkup();

        Assert.Contains("Text=\"{Binding SearchText, Mode=TwoWay}\"", markup);
        Assert.Contains("Command=\"{Binding NewChatCommand}\"", markup);
        Assert.Contains("ItemsSource=\"{Binding Conversations}\"", markup);
        Assert.Contains("SelectionChanged=\"OnConversationSelectionChanged\"", markup);
        Assert.Contains("Value=\"{Binding Title}\"", markup);
    }

    [Fact]
    public void EmptyConversationUsesOnlyTheApprovedCopy()
    {
        var document = XDocument.Parse(ReadChatMarkup());
        XNamespace ns = "https://github.com/avaloniaui";
        var emptyState = Named(document, "EmptyConversationState");
        var copy = emptyState.Descendants(ns + "TextBlock")
            .Select(element => Attribute(element, "Text"))
            .ToArray();

        Assert.Equal(
        [
            "How can AEDA help?",
            "Type a message below to start a conversation."
        ], copy);
        Assert.DoesNotContain("starter", emptyState.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(emptyState.Descendants(ns + "Button"));
    }

    [Fact]
    public void ComposerOwnsSendAndStopAsOneTrailingActionGroup()
    {
        var document = XDocument.Parse(ReadChatMarkup());
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        XNamespace ns = "https://github.com/avaloniaui";
        var composer = Named(document, "ComposerSurface");
        var actions = Named(document, "ComposerActions");
        var send = Named(document, "SendButton");
        var stop = Named(document, "StopButton");

        Assert.Contains(actions, composer.Descendants());
        Assert.Contains(send, actions.Descendants());
        Assert.Contains(stop, actions.Descendants());
        Assert.Equal("{Binding IsGenerating}", Attribute(stop, "IsVisible"));
        Assert.Equal(string.Empty, Attribute(send, "IsVisible"));
        Assert.Contains("composerAction", Attribute(send, "Classes"));
        Assert.Contains("composerAction", Attribute(stop, "Classes"));
        Assert.Contains("<Setter Property=\"Width\" Value=\"44\"", document.ToString());
        Assert.Contains("<Setter Property=\"Height\" Value=\"44\"", document.ToString());
        Assert.Equal("44", Attribute(Named(document, "Composer"), "MinHeight"));
        Assert.Contains("Math.Max(0, (layout.ComposerMinHeight - 46) / 2)", code);
    }

    [Fact]
    public void SendUsesOnlyTheApprovedPlainArrow()
    {
        var document = XDocument.Parse(ReadChatMarkup());
        XNamespace ns = "https://github.com/avaloniaui";
        var send = Named(document, "SendButton");
        var path = Assert.Single(send.Descendants(ns + "Path"));

        Assert.Equal("M5,12 L18,12 M13,6 L19,12 L13,18", Attribute(path, "Data"));
        Assert.DoesNotContain("AedaEye", send.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MessageSurfaceKeepsLiveRolesAndAvailableActions()
    {
        var markup = ReadChatMarkup();

        Assert.Contains("ChatRoleConverters.IsUser", markup);
        Assert.Contains("ChatRoleConverters.IsAssistant", markup);
        Assert.Contains("ChatRoleConverters.IsTool", markup);
        Assert.Contains("ChatRoleConverters.IsSystem", markup);
        Assert.Contains("DataContext.RetryCommand", markup);
        Assert.Contains("DataContext.RegenerateCommand", markup);
        Assert.Contains("ChatMarkdownPresenter", markup);
    }

    [Fact]
    public void FailedAssistantRendersExistingContentOnceAndKeepsRetry()
    {
        var document = XDocument.Parse(ReadChatMarkup());
        var markup = document.ToString();
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        var viewModel = ReadSource("ViewModels", "Chat", "AvaloniaChatViewModel.cs");
        var combined = markup + code;
        XNamespace ns = "https://github.com/avaloniaui";
        var presenter = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "ChatMarkdownPresenter");
        var failure = Assert.Single(document.Descendants(ns + "Border"), element =>
            Attribute(element, "IsVisible").Contains("ChatRoleConverters.IsFailed"));
        var failureText = Assert.Single(failure.Descendants(ns + "TextBlock"));

        Assert.Contains("ChatPresentation.DescribeStatus(", code);
        Assert.Equal(
            "{Binding Status, Converter={x:Static views:ChatRoleConverters.IsNotFailed}}",
            Attribute(presenter, "IsVisible"));
        Assert.Equal("{Binding Content}", Attribute(failureText, "Text"));
        Assert.Equal("{Binding Content}", Attribute(failure, "AutomationProperties.Name"));
        Assert.DoesNotContain("Something went wrong. Please try again.", markup);
        Assert.Contains("Something went wrong. Please try again.", viewModel);
        Assert.Contains("DataContext.RetryCommand", markup);
        Assert.Contains("IsVisible=\"{Binding CanRetry}\"", markup);
        Assert.DoesNotContain("provider unavailable", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("local model unavailable", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("context unavailable", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StatusMessage}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactModeDefaultsToListAndOnlyApprovedActionsEnterChat()
    {
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        var focus = Slice(code, "public void FocusComposer()", "public void ApplyResponsiveMode");
        var selection = Slice(code, "private void OnConversationSelectionChanged", "private void TrackLoad");
        var activeSelection = Slice(
            selection,
            "if (_viewModel.ActiveConversation?.Id == conversation.Id)",
            "var viewModel = _viewModel");
        var newChat = Slice(code, "private void OnNewChatClick", "private void OnSendClick");

        Assert.Contains("_compactChatActive = false", code);
        Assert.Contains("ShowCompactChat();", focus);
        Assert.Contains("ShowCompactChat();", selection);
        Assert.Contains("ShowCompactChat();", activeSelection);
        Assert.Contains("ShowCompactChat();", newChat);
        Assert.DoesNotContain("ClearConversationSelection", code);
        Assert.Contains("BackToConversationsButton.IsVisible = showCompactChat", code);
        Assert.Contains("ConversationRail.IsVisible = !_compact || !showCompactChat", code);
    }

    [Fact]
    public void CompactBackPreservesAndFocusesTheSelectedActiveConversation()
    {
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        var responsive = Slice(code, "public void ApplyResponsiveMode", "internal static (");
        var back = Slice(code, "private void OnBackToConversationsClick", "private void FocusSelectedConversation");
        var focus = Slice(code, "private void FocusSelectedConversation", "private void ShowCompactChat");

        Assert.DoesNotContain("SelectedItem =", responsive);
        Assert.DoesNotContain("SelectedItem =", back);
        Assert.DoesNotContain("ActiveConversation", back);
        Assert.Contains("FocusSelectedConversation();", back);
        Assert.Contains("ContainerFromItem(selected)", focus);
        Assert.Contains("ConversationList.Focus()", focus);
        Assert.Contains("SearchBox.Focus()", focus);
        Assert.DoesNotContain("OpenConversationAsync", responsive + back + focus);
    }

    [Fact]
    public void GenerationDisablesBackAndKeepsCompactChatActive()
    {
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        var responsive = Slice(code, "public void ApplyResponsiveMode", "internal static (");
        var availability = Slice(code, "private void UpdateConversationListAvailability", "private void UpdateStatusText");
        var back = Slice(code, "private void OnBackToConversationsClick", "private void FocusSelectedConversation");

        Assert.Contains("_compactChatActive = _viewModel?.IsGenerating == true", responsive);
        Assert.Contains("ConversationList.IsEnabled = available", availability);
        Assert.Contains("BackToConversationsButton.IsEnabled = available", availability);
        Assert.Contains("_viewModel?.IsGenerating == true", back);
    }

    [Fact]
    public void ActiveConversationPaneReentryNeverReloadsTheConversation()
    {
        var code = ReadSource("Views", "Chat", "ChatView.axaml.cs");
        var selection = Slice(code, "private void OnConversationSelectionChanged", "private void OnConversationListKeyDown");
        var reentry = Slice(code, "private bool ShowSelectedActiveConversation", "private void TrackLoad");

        Assert.Contains("ConversationList.Tapped", code);
        Assert.Contains("ConversationList.KeyDown", code);
        Assert.Contains("ShowCompactChat();", reentry);
        Assert.DoesNotContain("OpenConversationAsync", reentry);
        Assert.Single(Regex.Matches(selection, "OpenConversationAsync").Cast<Match>());
    }

    [Fact]
    public void MainWindowBridgesExistingResponsiveStateWithoutChangingRoutes()
    {
        var window = ReadSource("MainWindow.axaml.cs");

        Assert.Contains("ChatRoute.ApplyResponsiveMode(compact, medium)", window);
        Assert.Contains("DashboardRoute.ApplyResponsiveMode(compact, medium)", window);
        Assert.Contains("NavigateToPresentation", window);
    }

    [Fact]
    public void ChatUsesSemanticThemeResourcesWithoutFixedColors()
    {
        var markup = ReadChatMarkup();
        var presenter = ReadSource("Views", "Chat", "ChatMarkdownPresenter.cs");
        var combined = markup + presenter;

        Assert.DoesNotMatch("#[0-9A-Fa-f]{6}", combined);
        Assert.DoesNotContain("AedaBorderBrush", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AedaCodeBackgroundBrush", combined, StringComparison.Ordinal);
        Assert.Contains("DynamicResource AccentBrush", markup);
        Assert.Contains(
            "ListBoxItem:selected /template/ ContentPresenter#PART_ContentPresenter",
            markup);
        Assert.Contains("DynamicResource SurfaceAltBrush", markup);
        Assert.Contains("DynamicResource BorderBrush", markup);
    }

    [Theory]
    [InlineData(false, false, 300, 60, 28, 720, 52, 26)]
    [InlineData(false, true, 240, 56, 22, 580, 48, 22)]
    [InlineData(true, false, 0, 52, 16, double.PositiveInfinity, 44, 20)]
    public void ResponsiveLayoutsMatchTheApprovedWideMediumCompactGeometry(
        bool compact,
        bool medium,
        double railWidth,
        double headerHeight,
        double padding,
        double messageMaxWidth,
        double composerMinHeight,
        double emptyTitleSize) =>
        Assert.Equal(
            (railWidth, headerHeight, padding, messageMaxWidth, composerMinHeight, emptyTitleSize),
            ChatView.ResolveLayout(compact, medium));

    private static string ReadChatMarkup() =>
        ReadSource("Views", "Chat", "ChatView.axaml");

    private static XElement Named(XDocument document, string name) =>
        Assert.Single(document.Descendants(), element => Attribute(element, "Name") == name);

    private static string Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value
        ?? string.Empty;

    private static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return source[startIndex..endIndex];
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = Path.Combine([GetRepositoryRoot(), "PersonalAI.Desktop.Avalonia", .. relativePath]);
        Assert.True(File.Exists(path), $"Avalonia source not found at {path}");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot([CallerFilePath] string testFilePath = "")
    {
        var chatDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(Path.Combine(chatDirectory, "..", "..", ".."));
    }
}
