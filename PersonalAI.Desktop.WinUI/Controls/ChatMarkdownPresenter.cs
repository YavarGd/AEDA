using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PersonalAI.Core.Chat.Rendering;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Text;

namespace PersonalAI.Desktop.WinUI.Controls;

public sealed class ChatMarkdownPresenter : StackPanel
{
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(
            nameof(Content),
            typeof(RenderedChatContent),
            typeof(ChatMarkdownPresenter),
            new PropertyMetadata(null, OnContentChanged));

    public RenderedChatContent? Content
    {
        get => (RenderedChatContent?)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public ChatMarkdownPresenter()
    {
        Spacing = 8;
    }

    private static void OnContentChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        ((ChatMarkdownPresenter)dependencyObject).Render();
    }

    private void Render()
    {
        Children.Clear();

        if (Content is null)
        {
            return;
        }

        foreach (var block in Content.Blocks)
        {
            Children.Add(CreateBlock(block));
        }
    }

    private UIElement CreateBlock(ChatRenderBlock block) =>
        block switch
        {
            ChatHeadingBlock heading => CreateTextBlock(
                heading.Inlines,
                fontSize: heading.Level <= 2 ? 22 : 18,
                fontWeight: new FontWeight { Weight = 600 }),
            ChatParagraphBlock paragraph => CreateTextBlock(paragraph.Inlines),
            ChatQuoteBlock quote => CreateQuoteBlock(quote),
            ChatListBlock list => CreateListBlock(list),
            ChatCodeBlock code => CreateCodeBlock(code),
            ChatHorizontalRuleBlock => CreateRule(),
            _ => new TextBlock()
        };

    private TextBlock CreateTextBlock(
        IReadOnlyList<ChatInline> inlines,
        double fontSize = 14,
        FontWeight? fontWeight = null)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            FontSize = fontSize
        };

        if (fontWeight is not null)
        {
            textBlock.FontWeight = fontWeight.Value;
        }

        AddInlines(textBlock, inlines);
        return textBlock;
    }

    private UIElement CreateQuoteBlock(ChatQuoteBlock quote)
    {
        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["PersonalAiBorderBrush"],
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Child = CreateTextBlock(quote.Inlines)
        };
    }

    private UIElement CreateListBlock(ChatListBlock list)
    {
        var panel = new StackPanel { Spacing = 4 };
        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var row = new Grid
            {
                ColumnSpacing = 8,
                Margin = new Thickness(item.Level * 18, 0, 0, 0)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = list.Ordered ? $"{index + 1}." : "•",
                MinWidth = 22
            };
            var content = CreateTextBlock(item.Inlines);
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private UIElement CreateCodeBlock(ChatCodeBlock code)
    {
        var panel = new StackPanel();
        var header = new Grid
        {
            Background = (Brush)Application.Current.Resources["PersonalAiCodeHeaderBrush"],
            Padding = new Thickness(10, 6, 8, 6)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(code.Language) ? "text" : code.Language,
            Style = (Style)Application.Current.Resources["PersonalAiMetadataTextStyle"]
        });

        var copy = new Button
        {
            Content = "Copy",
            Padding = new Thickness(8, 2, 8, 2),
            MinWidth = 0
        };
        ToolTipService.SetToolTip(copy, "Copy code");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, "Copy code");
        copy.Click += (_, _) => CopyText(code.Code);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);

        var body = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
            Content = new TextBlock
            {
                Text = code.Code,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true,
                Padding = new Thickness(10)
            }
        };

        panel.Children.Add(header);
        panel.Children.Add(body);

        return new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["PersonalAiBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["PersonalAiCodeBackgroundBrush"],
            Child = panel
        };
    }

    private UIElement CreateRule()
    {
        return new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["PersonalAiBorderBrush"],
            Margin = new Thickness(0, 4, 0, 4)
        };
    }

    private static void AddInlines(TextBlock textBlock, IReadOnlyList<ChatInline> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case ChatCodeInline code:
                    textBlock.Inlines.Add(new Run
                    {
                        Text = code.Text,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas")
                    });
                    break;
                case ChatEmphasisInline { Bold: true } emphasis:
                    textBlock.Inlines.Add(new Bold { Inlines = { new Run { Text = emphasis.Text } } });
                    break;
                case ChatEmphasisInline { Italic: true } emphasis:
                    textBlock.Inlines.Add(new Italic { Inlines = { new Run { Text = emphasis.Text } } });
                    break;
                case ChatLinkInline link when link.IsSafe:
                    var hyperlink = new Hyperlink();
                    hyperlink.Inlines.Add(new Run { Text = link.Text });
                    hyperlink.Click += async (_, _) =>
                    {
                        if (Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri) &&
                            ChatMarkdownRenderer.IsSafeUri(link.Uri))
                        {
                            await Launcher.LaunchUriAsync(uri);
                        }
                    };
                    ToolTipService.SetToolTip(textBlock, link.Uri);
                    textBlock.Inlines.Add(hyperlink);
                    break;
                case ChatLinkInline link:
                    textBlock.Inlines.Add(new Run { Text = $"{link.Text} ({link.Uri})" });
                    break;
                default:
                    textBlock.Inlines.Add(new Run { Text = inline.Text });
                    break;
            }
        }
    }

    private static void CopyText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException ||
            exception is InvalidOperationException ||
            exception is System.Runtime.InteropServices.COMException)
        {
        }
    }
}
