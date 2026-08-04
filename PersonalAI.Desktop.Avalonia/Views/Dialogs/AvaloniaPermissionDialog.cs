using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PersonalAI.Core.Permissions;
using PersonalAI.Infrastructure.Chat;
using FontWeight = global::Avalonia.Media.FontWeight;
using TextWrapping = global::Avalonia.Media.TextWrapping;

namespace PersonalAI.Desktop.Avalonia.Views.Dialogs;

public sealed class AvaloniaPermissionDialog : Window
{
    private readonly PermissionDialogSession _session;

    public AvaloniaPermissionDialog(
        PermissionRequest request,
        PermissionDialogSession session)
    {
        _session = session;
        var presentation = ToolPresentationMapper.ForPermission(request);
        Title = presentation.Title;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, presentation.Title);

        var panel = new StackPanel { Margin = new(22), Spacing = 14 };
        panel.Children.Add(new TextBlock
        {
            Text = presentation.Action,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = presentation.Explanation,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(LabelValue("Target scope", presentation.Scope));
        panel.Children.Add(new Border
        {
            Padding = new(12),
            BorderThickness = new(1),
            CornerRadius = new(8),
            Child = new TextBlock
            {
                Text = presentation.ReadOnlyExplanation,
                TextWrapping = TextWrapping.Wrap
            }
        });

        var permissions = request.Permissions.Count == 0
            ? "No declared permissions"
            : string.Join(", ", request.Permissions);
        panel.Children.Add(new Expander
        {
            Header = "Technical details",
            IsExpanded = false,
            Content = new TextBlock
            {
                Text = string.Join(
                    Environment.NewLine,
                    $"Permissions: {permissions}",
                    $"Scope: {presentation.TechnicalDetails}",
                    $"Risk: {request.RiskLevel}",
                    $"Access: {request.AccessMode}",
                    $"Leaves device: {request.LeavesMachine}",
                    $"Changes state: {request.ChangesState}"),
                TextWrapping = TextWrapping.Wrap
            }
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(Button("Allow once", PermissionDialogOutcome.AllowOnce));
        buttons.Children.Add(Button("Allow for this task", PermissionDialogOutcome.AllowForTask));
        buttons.Children.Add(Button("Deny", PermissionDialogOutcome.Deny));
        buttons.Children.Add(Button("Cancel task", PermissionDialogOutcome.CancelTask));
        panel.Children.Add(buttons);
        Content = panel;
    }

    public Task CloseFromCoordinatorAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (IsVisible)
            {
                Close();
            }

            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (IsVisible)
                {
                    Close();
                }

                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }

    private Button Button(string label, PermissionDialogOutcome outcome)
    {
        var button = new Button { Content = label };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            if (_session.TrySetOutcome(outcome))
            {
                Close(outcome);
            }
        };
        return button;
    }

    private static Control LabelValue(string label, string value)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }
}
