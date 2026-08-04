using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PersonalAI.Desktop.Avalonia.Views.Dialogs;

public sealed class WorkspaceDialog : Window
{
    private readonly TextBox? _textBox;

    private WorkspaceDialog(
        string title,
        string message,
        string confirmLabel,
        string? text = null)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, title);

        var panel = new StackPanel
        {
            Margin = new(20),
            Spacing = 14
        };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
        });

        if (text is not null)
        {
            _textBox = new TextBox { Text = text };
            AutomationProperties.SetName(_textBox, "Workspace name");
            panel.Children.Add(_textBox);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Cancel" };
        var confirm = new Button { Content = confirmLabel };
        AutomationProperties.SetName(cancel, "Cancel");
        AutomationProperties.SetName(confirm, confirmLabel);
        cancel.Click += (_, _) => Close(text is null ? false : null);
        confirm.Click += (_, _) =>
            Close(text is null ? true : _textBox?.Text?.Trim());
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        Content = panel;

        Opened += (_, _) => (_textBox as Control ?? cancel).Focus();
    }

    public static Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmLabel) =>
        new WorkspaceDialog(title, message, confirmLabel)
            .ShowDialog<bool>(owner);

    public static Task<string?> RenameAsync(
        Window owner,
        string currentName) =>
        new WorkspaceDialog(
                "Rename workspace",
                "Enter a new display name. The folder path will not change.",
                "Rename",
                currentName)
            .ShowDialog<string?>(owner);
}
