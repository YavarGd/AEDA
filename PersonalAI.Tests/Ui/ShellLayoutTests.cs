using System.Xml.Linq;
using PersonalAI.Core.Chat;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Ui;

public sealed class ShellLayoutTests
{
    [Fact]
    public void WindowBounds_KeepShellAboveCrushPoint()
    {
        Assert.Equal(1080, WinUiWindowPlacementService.DefaultWindowWidth);
        Assert.Equal(760, WinUiWindowPlacementService.DefaultWindowHeight);
        Assert.Equal(1080, WinUiWindowPlacementService.MinimumWindowWidth);
        Assert.Equal(760, WinUiWindowPlacementService.MinimumWindowHeight);
    }

    [Theory]
    [InlineData("New chat", "NewChatCommand", "\uE710")]
    [InlineData("Settings", "OpenSettingsCommand", "\uE713")]
    [InlineData("Send", "SendMessageCommand", "\uE74A")]
    [InlineData("Attach clipboard text", "AttachClipboardContextCommand", "\uE8C8")]
    [InlineData("Capture screenshot", "CaptureScreenshotContextCommand", "\uE722")]
    [InlineData("Capture active app context", "CaptureApplicationContextCommand", "\uE8B7")]
    public void IconOnlyPrimaryButtons_KeepAccessibleLabelsAndCommands(
        string accessibleName,
        string commandName,
        string glyph)
    {
        var button = FindButtonByAccessibleName(accessibleName);

        Assert.Equal(accessibleName, AttributeValue(button, "ToolTipService.ToolTip"));
        Assert.Equal($"{{Binding {commandName}}}", AttributeValue(button, "Command"));
        Assert.Null(AttributeValue(button, "Content"));
        Assert.Contains(
            button.Descendants().Where(element => element.Name.LocalName == "FontIcon"),
            icon => AttributeValue(icon, "Glyph") == glyph);
    }

    [Fact]
    public void DeveloperWorkspaceTools_AreCollapsedBehindDebugSettingsExpander()
    {
        var document = LoadShellXaml();
        var developerExpander = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Expander" &&
                AttributeValue(element, "Header") == "Developer");

        Assert.Equal("False", AttributeValue(developerExpander, "IsExpanded"));
        Assert.Contains(
            "IsDeveloperDiagnosticsVisible",
            AttributeValue(developerExpander, "Visibility"));
    }

    [Fact]
    public void ShellRegions_HaveBoundedSidebarAndComposerInput()
    {
        var document = LoadShellXaml();
        var root = document.Descendants().Single(element =>
            element.Name.LocalName == "Grid" &&
            AttributeValue(element, "Name") == "Root");
        var prompt = document.Descendants().Single(element =>
            element.Name.LocalName == "TextBox" &&
            AttributeValue(element, "Name") == "PromptTextBox");

        Assert.Equal("1080", AttributeValue(root, "MinWidth"));
        Assert.Equal("760", AttributeValue(root, "MinHeight"));
        Assert.Equal("360", AttributeValue(prompt, "MinWidth"));
        Assert.Equal("84", AttributeValue(prompt, "MinHeight"));
    }

    [Fact]
    public void ThemeResources_DefineSharedVisualSystemTokens()
    {
        var document = LoadThemeResources();
        var requiredKeys = new[]
        {
            "PersonalAiPageBackgroundBrush",
            "PersonalAiSidebarBackgroundBrush",
            "PersonalAiElevatedSurfaceBrush",
            "PersonalAiSubtleSurfaceBrush",
            "PersonalAiMessageSurfaceBrush",
            "PersonalAiToolActivitySurfaceBrush",
            "PersonalAiBorderBrush",
            "PersonalAiMutedTextBrush",
            "PersonalAiSecondaryTextBrush",
            "PersonalAiSuccessBrush",
            "PersonalAiWarningBrush",
            "PersonalAiErrorBrush",
            "PersonalAiActiveBrush",
            "CardCornerRadius",
            "PillCornerRadius",
            "ComposerCornerRadius",
            "IconButtonSize",
            "PersonalAiIconButtonStyle",
            "PersonalAiPageTitleTextStyle",
            "PersonalAiSettingsExpanderStyle"
        };

        foreach (var key in requiredKeys)
        {
            Assert.Contains(
                document.Descendants(),
                element => AttributeValue(element, "Key") == key);
        }
    }

    [Fact]
    public void SettingsSurface_UsesClearGroupsAndKeepsDeveloperCollapsed()
    {
        var document = LoadShellXaml();
        var headers = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Expander")
            .Select(element => AttributeValue(element, "Header"))
            .ToArray();

        Assert.Contains("General", headers);
        Assert.Contains("Workspaces", headers);
        Assert.Contains("Models", headers);
        Assert.Contains("Appearance", headers);
        Assert.Contains("Integrations", headers);
        Assert.Contains("Privacy", headers);
        Assert.Contains("Developer", headers);
        Assert.DoesNotContain("Advanced model routing", headers);
        Assert.DoesNotContain("Context and privacy", headers);
    }

    [Fact]
    public void ChatMessageViewModel_RolesHaveDistinctPresentation()
    {
        var user = new ChatMessageViewModel(ChatRole.User, "Hello");
        var assistant = new ChatMessageViewModel(ChatRole.Assistant, "Hi");
        var tool = new ChatMessageViewModel(ChatRole.Tool, "Search completed.");

        Assert.Equal(Microsoft.UI.Xaml.HorizontalAlignment.Right, user.MessageHorizontalAlignment);
        Assert.Equal(Microsoft.UI.Xaml.HorizontalAlignment.Left, assistant.MessageHorizontalAlignment);
        Assert.True(user.MessageMaxWidth < assistant.MessageMaxWidth);
        Assert.True(tool.ContentMaxLines > assistant.ContentMaxLines);
        Assert.True(tool.BorderThickness.Left > assistant.BorderThickness.Left);
    }

    private static XElement FindButtonByAccessibleName(string accessibleName)
    {
        var document = LoadShellXaml();
        return document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Button" &&
                AttributeValue(element, "AutomationProperties.Name") == accessibleName);
    }

    private static XDocument LoadShellXaml()
    {
        return LoadProjectXaml("Views", "MainWindow.xaml");
    }

    private static XDocument LoadThemeResources()
    {
        return LoadProjectXaml("Themes", "ThemeResources.xaml");
    }

    private static XDocument LoadProjectXaml(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                [directory.FullName, "PersonalAI.Desktop.WinUI", .. segments]);

            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {Path.Combine(segments)}.");
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }
}
