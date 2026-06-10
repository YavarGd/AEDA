using CommunityToolkit.Mvvm.ComponentModel;
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

    public string RoleLabel => Role.ToString();

    [ObservableProperty]
    private string _content;
}
