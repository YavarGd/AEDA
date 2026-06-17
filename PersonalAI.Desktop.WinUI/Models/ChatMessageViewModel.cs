using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.WinUI.Models;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessageViewModel(
        ChatRole role,
        string content,
        string? activityKey = null)
    {
        Role = role;
        ActivityKey = activityKey;
        _content = content;
    }

    public ChatRole Role { get; }

    public string? ActivityKey { get; }

    public string RoleLabel => Role == ChatRole.Tool
        ? "Activity"
        : Role.ToString();

    public bool IsToolActivity => Role == ChatRole.Tool;

    public TextWrapping ContentTextWrapping => IsToolActivity
        ? TextWrapping.NoWrap
        : TextWrapping.Wrap;

    public TextTrimming ContentTextTrimming => IsToolActivity
        ? TextTrimming.CharacterEllipsis
        : TextTrimming.None;

    public int ContentMaxLines => IsToolActivity ? 1 : 0;

    public Thickness BorderThickness => IsToolActivity
        ? new Thickness(1)
        : new Thickness(0);

    public Thickness Padding => IsToolActivity
        ? new Thickness(8, 6, 8, 6)
        : new Thickness(0);

    [ObservableProperty]
    private string _content;
}
