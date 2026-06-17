using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Chat.Rendering;

namespace PersonalAI.Desktop.WinUI.Models;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessageViewModel(
        ChatRole role,
        string content,
        string? activityKey = null,
        ChatMessageDisplayStatus status = ChatMessageDisplayStatus.Completed,
        DateTimeOffset? createdAtUtc = null,
        string? modelName = null,
        string? routingSourceLabel = null)
    {
        Id = Guid.NewGuid();
        Role = role;
        ActivityKey = activityKey;
        Status = status;
        CreatedAtUtc = createdAtUtc ?? DateTimeOffset.UtcNow;
        ModelName = modelName;
        RoutingSourceLabel = routingSourceLabel;
        _content = content;
        _renderedContent = ChatMarkdownRenderer.Shared.Render(content);
    }

    public Guid Id { get; }

    public ChatRole Role { get; }

    public string? ActivityKey { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public ChatMessageDisplayStatus Status { get; set; }

    public string? ModelName { get; private set; }

    public string? RoutingSourceLabel { get; private set; }

    public string RoleLabel => Role == ChatRole.Tool
        ? "Activity"
        : Role.ToString();

    public bool IsToolActivity => Role == ChatRole.Tool;

    public bool IsAssistantMessage => Role == ChatRole.Assistant;

    public bool IsUserMessage => Role == ChatRole.User;

    public bool UsesMarkdownPresenter => IsAssistantMessage && !IsStreaming;

    public bool UsesPlainTextPresenter => !UsesMarkdownPresenter;

    public bool CanRegenerate { get; set; }

    public bool CanRetry => IsAssistantMessage &&
        Status is ChatMessageDisplayStatus.Failed or ChatMessageDisplayStatus.Cancelled;

    public bool IsCapabilityBlocked => Status == ChatMessageDisplayStatus.Blocked;

    public string MetadataText
    {
        get
        {
            var status = Status switch
            {
                ChatMessageDisplayStatus.Streaming => "Generating",
                ChatMessageDisplayStatus.Failed => "Failed",
                ChatMessageDisplayStatus.Blocked => "Action needed",
                ChatMessageDisplayStatus.Cancelled => "Cancelled",
                ChatMessageDisplayStatus.Regenerated => "Regenerated",
                _ => RoleLabel
            };

            var model = string.IsNullOrWhiteSpace(ModelName)
                ? string.Empty
                : $" · {RoutingSourceLabel ?? "Model"}: {ModelName}";

            return $"{status} · {CreatedAtUtc.ToLocalTime():g}{model}";
        }
    }

    public HorizontalAlignment MessageHorizontalAlignment => Role switch
    {
        ChatRole.User => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    public double MessageMaxWidth => Role switch
    {
        ChatRole.Tool => 720,
        ChatRole.User => 680,
        ChatRole.Assistant when Status is ChatMessageDisplayStatus.Failed
            or ChatMessageDisplayStatus.Cancelled
            or ChatMessageDisplayStatus.Blocked => 520,
        _ => 840
    };

    public TextWrapping ContentTextWrapping => IsToolActivity
        ? TextWrapping.NoWrap
        : TextWrapping.Wrap;

    public TextTrimming ContentTextTrimming => IsToolActivity
        ? TextTrimming.CharacterEllipsis
        : TextTrimming.None;

    public int ContentMaxLines => IsToolActivity ? 1 : 0;

    public Thickness BorderThickness =>
        IsToolActivity ||
        Status is ChatMessageDisplayStatus.Failed
            or ChatMessageDisplayStatus.Cancelled
            or ChatMessageDisplayStatus.Blocked
            ? new Thickness(1)
            : new Thickness(0);

    public Thickness Padding => Role switch
    {
        ChatRole.User => new Thickness(14, 10, 14, 10),
        ChatRole.Tool => new Thickness(10, 7, 10, 7),
        ChatRole.Assistant when Status is ChatMessageDisplayStatus.Failed
            or ChatMessageDisplayStatus.Cancelled
            or ChatMessageDisplayStatus.Blocked => new Thickness(12),
        _ => new Thickness(0, 4, 0, 4)
    };

    public CornerRadius CornerRadius => Role switch
    {
        ChatRole.User => new CornerRadius(10),
        ChatRole.Tool => new CornerRadius(8),
        ChatRole.Assistant when Status is ChatMessageDisplayStatus.Failed
            or ChatMessageDisplayStatus.Cancelled
            or ChatMessageDisplayStatus.Blocked => new CornerRadius(8),
        _ => new CornerRadius(0)
    };

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private RenderedChatContent _renderedContent;

    partial void OnContentChanged(string value)
    {
        if (!IsStreaming && IsAssistantMessage)
        {
            RenderedContent = ChatMarkdownRenderer.Shared.Render(value);
        }
    }

    partial void OnIsStreamingChanged(bool value)
    {
        OnPropertyChanged(nameof(UsesMarkdownPresenter));
        OnPropertyChanged(nameof(UsesPlainTextPresenter));
    }

    public void CompleteRendering(ChatMessageDisplayStatus status = ChatMessageDisplayStatus.Completed)
    {
        Status = status;
        IsStreaming = false;
        RenderedContent = ChatMarkdownRenderer.Shared.Render(Content);
        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsCapabilityBlocked));
        OnPropertyChanged(nameof(MessageMaxWidth));
        OnPropertyChanged(nameof(BorderThickness));
        OnPropertyChanged(nameof(Padding));
        OnPropertyChanged(nameof(CornerRadius));
    }

    public void StartStreaming()
    {
        Status = ChatMessageDisplayStatus.Streaming;
        IsStreaming = true;
        OnPropertyChanged(nameof(MetadataText));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(IsCapabilityBlocked));
    }

    public void SetCanRegenerate(bool value)
    {
        if (CanRegenerate == value)
        {
            return;
        }

        CanRegenerate = value;
        OnPropertyChanged(nameof(CanRegenerate));
    }

    public void SetModelMetadata(string modelName, string routingSourceLabel)
    {
        ModelName = modelName;
        RoutingSourceLabel = routingSourceLabel;
        OnPropertyChanged(nameof(MetadataText));
    }
}

public enum ChatMessageDisplayStatus
{
    Completed,
    Streaming,
    Cancelled,
    Failed,
    Blocked,
    Regenerated
}
