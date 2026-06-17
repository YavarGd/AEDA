using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.WinUI.Models;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessageViewModel(ChatRole role, string content)
    {
        Role = role;
        _content = content;
    }

    public ChatRole Role { get; }

    public string RoleLabel => Role == ChatRole.Tool
        ? "Tool activity"
        : Role.ToString();

    public bool IsToolActivity => Role == ChatRole.Tool;

    public Thickness BorderThickness => IsToolActivity
        ? new Thickness(1)
        : new Thickness(0);

    public Thickness Padding => IsToolActivity
        ? new Thickness(10)
        : new Thickness(0);

    [ObservableProperty]
    private string _content;
}
