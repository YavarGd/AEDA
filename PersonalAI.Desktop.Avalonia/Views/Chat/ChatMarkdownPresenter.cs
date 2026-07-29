using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using System.Runtime.InteropServices;
using Avalonia.Layout;
using Avalonia.Media;
using PersonalAI.Core.Chat.Rendering;

namespace PersonalAI.Desktop.Avalonia.Views.Chat;

/// <summary>
/// Renders assistant markdown with native Avalonia controls and inlines only.
/// Parsing is reused from <see cref="ChatMarkdownRenderer"/> in Core, so this type only
/// maps already-parsed blocks onto controls.
/// </summary>
public sealed class ChatMarkdownPresenter : StackPanel
{
    private const double CodeBlockMaxHeight = 320;

    private static readonly FontFamily MonospaceFont =
        new("Cascadia Mono, Consolas, Menlo, monospace");

    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<ChatMarkdownPresenter, string?>(nameof(Markdown));

    public ChatMarkdownPresenter()
    {
        Spacing = 8;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty)
        {
            Render();
        }
    }

    private void Render()
    {
        Children.Clear();

        var markdown = Markdown;
        if (string.IsNullOrEmpty(markdown))
        {
            AutomationProperties.SetName(this, string.Empty);
            return;
        }

        var content = ChatMarkdownRenderer.Shared.Render(markdown);

        foreach (var block in content.Blocks)
        {
            Children.Add(CreateBlock(block));
        }

        AutomationProperties.SetName(this, ChatPresentation.ToAccessibleText(content));
    }

    private Control CreateBlock(ChatRenderBlock block) => block switch
    {
        ChatHeadingBlock heading => CreateText(
            heading.Inlines,
            heading.Level <= 2 ? 20 : 16,
            FontWeight.SemiBold),
        ChatParagraphBlock paragraph => CreateText(paragraph.Inlines),
        ChatQuoteBlock quote => CreateQuote(quote),
        ChatListBlock list => CreateList(list),
        ChatCodeBlock code => CreateCode(code),
        ChatHorizontalRuleBlock => CreateRule(),
        _ => CreateText([])
    };

    private static SelectableTextBlock CreateText(
        IReadOnlyList<ChatInline> inlines,
        double fontSize = 14,
        FontWeight fontWeight = FontWeight.Normal)
    {
        var textBlock = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Inlines = []
        };

        AddInlines(textBlock.Inlines, inlines);
        return textBlock;
    }

    private static Control CreateQuote(ChatQuoteBlock quote)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = CreateText(quote.Inlines)
        };
        border.Bind(
            Border.BorderBrushProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                "AedaBorderBrush"));
        return border;
    }

    private static Control CreateList(ChatListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };

        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Margin = new Thickness(item.Level * 18, 0, 0, 0)
            };

            var marker = new TextBlock
            {
                Text = list.Ordered ? $"{index + 1}." : "•",
                MinWidth = 22,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            var content = CreateText(item.Inlines);
            Grid.SetColumn(marker, 0);
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private Control CreateCode(ChatCodeBlock code)
    {
        var language = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(code.Language) ? "text" : code.Language,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        var copyButton = new Button
        {
            Content = "Copy",
            Padding = new Thickness(10, 2),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AutomationProperties.SetName(copyButton, "Copy code block");
        copyButton.Click += async (_, _) =>
        {
            // async void handler: nothing may escape to the dispatcher.
            try
            {
                await CopyAsync(code.Code);
            }
            catch
            {
                // Copying is best-effort convenience only.
            }
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(language, 0);
        Grid.SetColumn(copyButton, 1);
        header.Children.Add(language);
        header.Children.Add(copyButton);

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = CodeBlockMaxHeight,
            Content = new SelectableTextBlock
            {
                Text = code.Code,
                FontFamily = MonospaceFont,
                TextWrapping = TextWrapping.NoWrap
            }
        };

        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(header);
        panel.Children.Add(body);

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Child = panel
        };
        border.Bind(
            Border.BorderBrushProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                "AedaBorderBrush"));
        border.Bind(
            Border.BackgroundProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                "AedaCodeBackgroundBrush"));

        return border;
    }

    private static Control CreateRule()
    {
        var rule = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4)
        };
        rule.Bind(
            Border.BackgroundProperty,
            new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                "AedaBorderBrush"));
        return rule;
    }

    private async Task CopyAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is TimeoutException ||
            exception is OperationCanceledException ||
            // ExternalException covers COMException, which Windows raises when another
            // process is holding the clipboard open.
            exception is ExternalException)
        {
        }
    }

    private static void AddInlines(
        InlineCollection target,
        IReadOnlyList<ChatInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case ChatCodeInline code:
                    target.Add(new Run(code.Text) { FontFamily = MonospaceFont });
                    break;
                case ChatEmphasisInline { Bold: true } bold:
                    target.Add(new Run(bold.Text) { FontWeight = FontWeight.Bold });
                    break;
                case ChatEmphasisInline { Italic: true } italic:
                    target.Add(new Run(italic.Text) { FontStyle = FontStyle.Italic });
                    break;
                case ChatLinkInline { IsSafe: true } link:
                    target.Add(new Run(link.Text)
                    {
                        TextDecorations = TextDecorations.Underline
                    });
                    target.Add(new Run($" ({link.Uri})") { FontSize = 12 });
                    break;
                case ChatLinkInline link:
                    target.Add(new Run(ChatPresentation.DescribeUnsafeLink(link)));
                    break;
                default:
                    target.Add(new Run(inline.Text));
                    break;
            }
        }
    }
}
