using System.Xml.Linq;
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
        Assert.Equal("680", AttributeValue(root, "MinHeight"));
        Assert.Equal("360", AttributeValue(prompt, "MinWidth"));
        Assert.Equal("88", AttributeValue(prompt, "MinHeight"));
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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PersonalAI.Desktop.WinUI",
                "Views",
                "MainWindow.xaml");

            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MainWindow.xaml.");
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }
}
