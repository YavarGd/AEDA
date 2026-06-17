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

    public bool IsAssistantMessage => Role == ChatRole.Assistant;

    public bool IsUserMessage => Role == ChatRole.User;

    public HorizontalAlignment MessageHorizontalAlignment => Role switch
    {
        ChatRole.User => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    public double MessageMaxWidth => Role switch
    {
        ChatRole.Tool => 720,
        ChatRole.User => 680,
        _ => 840
    };

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

    public Thickness Padding => Role switch
    {
        ChatRole.User => new Thickness(14, 10, 14, 10),
        ChatRole.Tool => new Thickness(10, 7, 10, 7),
        _ => new Thickness(0, 4, 0, 4)
    };

    public CornerRadius CornerRadius => Role switch
    {
        ChatRole.User => new CornerRadius(10),
        ChatRole.Tool => new CornerRadius(8),
        _ => new CornerRadius(0)
    };

    [ObservableProperty]
    private string _content;
}
