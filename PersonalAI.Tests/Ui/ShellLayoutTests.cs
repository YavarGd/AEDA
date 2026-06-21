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
    [InlineData("Module dashboard", "OpenDashboardCommand", "\uE80F")]
    [InlineData("Settings", "OpenSettingsCommand", "\uE713")]
    [InlineData("Send", "SendMessageCommand", "\uE74A")]
    [InlineData("Add context", null, "\uE898")]
    public void IconOnlyPrimaryButtons_KeepAccessibleLabelsAndCommands(
        string accessibleName,
        string? commandName,
        string glyph)
    {
        var button = FindButtonByAccessibleName(accessibleName);

        Assert.Equal(accessibleName, AttributeValue(button, "ToolTipService.ToolTip"));
        if (commandName is not null)
        {
            Assert.Equal($"{{Binding {commandName}}}", AttributeValue(button, "Command"));
        }

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
            "PersonalAiCodeBackgroundBrush",
            "PersonalAiCodeHeaderBrush",
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
    public void ChatSurface_RemovesVisibleStopButtonAndKeepsJumpToLatestAccessible()
    {
        var document = LoadShellXaml();

        Assert.DoesNotContain(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            button => AttributeValue(button, "AutomationProperties.Name") == "Stop");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            button => AttributeValue(button, "AutomationProperties.Name") == "Jump to latest");
    }

    [Fact]
    public void Composer_UsesAttachmentMenuAndNoPermanentEmptyContextText()
    {
        var document = LoadShellXaml();
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();
        var menuItems = document.Descendants()
            .Where(element => element.Name.LocalName == "MenuFlyoutItem")
            .ToArray();

        Assert.DoesNotContain(
            document.Descendants(),
            element => AttributeValue(element, "Text") == "No context attached");
        Assert.Contains(
            buttons,
            button => AttributeValue(button, "AutomationProperties.Name") == "Add context");
        Assert.DoesNotContain(
            buttons,
            button => AttributeValue(button, "AutomationProperties.Name") == "Capture active app context");
        Assert.DoesNotContain(
            buttons,
            button => AttributeValue(button, "AutomationProperties.Name") == "Attach clipboard text");
        Assert.DoesNotContain(
            buttons,
            button => AttributeValue(button, "AutomationProperties.Name") == "Capture screenshot");
        Assert.Contains(
            menuItems,
            item => AttributeValue(item, "Command") == "{Binding CaptureApplicationContextCommand}");
        Assert.Contains(
            menuItems,
            item => AttributeValue(item, "Command") == "{Binding AttachClipboardContextCommand}");
        Assert.Contains(
            menuItems,
            item => AttributeValue(item, "Command") == "{Binding CaptureScreenshotContextCommand}");
    }

    [Fact]
    public void ConversationRows_ShowTitleAndPreviewOnly()
    {
        var document = LoadShellXaml();
        var row = document.Descendants()
            .Where(element => element.Name.LocalName == "StackPanel")
            .Single(element =>
                element.Descendants().Any(child =>
                    child.Name.LocalName == "TextBlock" &&
                    AttributeValue(child, "Text") == "{Binding Title}") &&
                element.Descendants().Any(child =>
                    child.Name.LocalName == "TextBlock" &&
                    AttributeValue(child, "Text") == "{Binding Preview}" &&
                    AttributeValue(child, "TextWrapping") == "NoWrap"));
        var preview = row.Descendants().Single(element =>
            element.Name.LocalName == "TextBlock" &&
            AttributeValue(element, "Text") == "{Binding Preview}" &&
            AttributeValue(element, "TextWrapping") == "NoWrap");

        Assert.Equal("1", AttributeValue(preview, "MaxLines"));
        Assert.DoesNotContain(
            row.Descendants(),
            element => AttributeValue(element, "Text") == "{Binding Model}");
        Assert.DoesNotContain(
            row.Descendants(),
            element => AttributeValue(element, "Text") == "{Binding Status}");
    }

    [Fact]
    public void ChatTimeline_BindsRolePresentationWithoutCreatingWinUiObjects()
    {
        var document = LoadShellXaml();
        var timeline = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ListView" &&
                AttributeValue(element, "Name") == "TimelineList");
        var messageBorder = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "Border" &&
                AttributeValue(element, "MaxWidth") == "{Binding MessageMaxWidth}");
        var contentText = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "TextBlock" &&
                AttributeValue(element, "Text") == "{Binding Content}");

        Assert.Equal("Top", AttributeValue(timeline, "VerticalContentAlignment"));
        Assert.Equal("{Binding BorderThickness}", AttributeValue(messageBorder, "BorderThickness"));
        Assert.Equal("{Binding MessageHorizontalAlignment}", AttributeValue(messageBorder, "HorizontalAlignment"));
        Assert.Equal("{Binding ContentMaxLines}", AttributeValue(contentText, "MaxLines"));
        Assert.Equal("{Binding ContentTextWrapping}", AttributeValue(contentText, "TextWrapping"));
    }

    [Fact]
    public void AttachmentCards_AreCompactAndHideTechnicalDetails()
    {
        var document = LoadShellXaml();
        var attachmentCard = document.Descendants()
            .Where(element => element.Name.LocalName == "Border")
            .Single(element =>
                AttributeValue(element, "ToolTipService.ToolTip") == "{Binding Preview}" &&
                AttributeValue(element, "Width") == "184");

        Assert.Contains(
            attachmentCard.Descendants(),
            element => element.Name.LocalName == "FontIcon" &&
                AttributeValue(element, "Glyph") == "\uE722");
        Assert.DoesNotContain(
            attachmentCard.Descendants(),
            element => element.Name.LocalName == "Image");
        Assert.DoesNotContain(
            attachmentCard.Descendants(),
            element => AttributeValue(element, "Text") == "{Binding Preview}");
        Assert.DoesNotContain(
            attachmentCard.Descendants(),
            element => AttributeValue(element, "Text") == "{Binding Type}");
    }

    [Fact]
    public void VisionSelector_ListsOnlyVisionModels()
    {
        var document = LoadShellXaml();
        var visionSelector = document.Descendants()
            .Where(element => element.Name.LocalName == "ComboBox")
            .Single(element => AttributeValue(element, "Header") == "Vision");

        Assert.Equal("{Binding VisionModels}", AttributeValue(visionSelector, "ItemsSource"));
        Assert.Equal("False", AttributeValue(visionSelector, "IsEditable"));
    }

    [Fact]
    public void Header_ShowsConversationModelOverrideSeparatelyFromRoutingStatus()
    {
        var document = LoadShellXaml();

        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Border"),
            border =>
                AttributeValue(border, "Visibility")?.Contains(
                    "HasConversationModelOverride",
                    StringComparison.Ordinal) == true &&
                AttributeValue(border, "ToolTipService.ToolTip") ==
                    "{Binding ConversationModelOverrideLabel}");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "TextBlock"),
            text => AttributeValue(text, "Text") == "{Binding RoutingStatusMessage}");
    }

    [Fact]
    public void ShellNavigationSurfaces_BindChatDashboardCodeAndSettings()
    {
        var document = LoadShellXaml();

        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Grid"),
            grid => AttributeValue(grid, "Visibility")?.Contains("IsChatVisible", StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Grid"),
            grid => AttributeValue(grid, "Visibility")?.Contains("IsDashboardVisible", StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Grid"),
            grid => AttributeValue(grid, "Visibility")?.Contains("IsCodeVisible", StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Grid"),
            grid => AttributeValue(grid, "Visibility")?.Contains("IsResearchVisible", StringComparison.Ordinal) == true);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Button"),
            button => AttributeValue(button, "Command") == "{Binding OpenChatCommand}");
    }

    [Fact]
    public void ModuleSuggestionCard_IsExplicitAndDismissible()
    {
        var document = LoadShellXaml();
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "Border"),
            border => AttributeValue(border, "Visibility")?.Contains("HasModuleSuggestion", StringComparison.Ordinal) == true);
        Assert.Contains(
            buttons,
            button => AttributeValue(button, "Content") == "{Binding ModuleSuggestionOpenLabel}" &&
                AttributeValue(button, "Command") == "{Binding OpenSuggestedModuleCommand}");
        Assert.Contains(
            buttons,
            button => AttributeValue(button, "AutomationProperties.Name") == "Dismiss module suggestion" &&
                AttributeValue(button, "Command") == "{Binding DismissModuleSuggestionCommand}");
    }

    [Fact]
    public void BlockedVisionCard_OffersAutomaticRoutingAndSettingsActions()
    {
        var document = LoadShellXaml();
        var buttons = document.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .ToArray();

        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "Content") == "Use automatic routing" &&
                AttributeValue(button, "Command") ==
                    "{Binding DataContext.UseAutomaticRoutingCommand, ElementName=Root}" &&
                AttributeValue(button, "Visibility")?.Contains(
                    "IsCapabilityBlocked",
                    StringComparison.Ordinal) == true);
        Assert.Contains(
            buttons,
            button =>
                AttributeValue(button, "Content") == "Choose vision model" &&
                AttributeValue(button, "Command") ==
                    "{Binding DataContext.ChooseVisionModelCommand, ElementName=Root}" &&
                AttributeValue(button, "Visibility")?.Contains(
                    "IsCapabilityBlocked",
                    StringComparison.Ordinal) == true);
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
