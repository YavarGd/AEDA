namespace PersonalAI.Core.Chat.Rendering;

public sealed record RenderedChatContent(
    IReadOnlyList<ChatRenderBlock> Blocks,
    string PlainText);

public abstract record ChatRenderBlock;

public sealed record ChatParagraphBlock(IReadOnlyList<ChatInline> Inlines) : ChatRenderBlock;

public sealed record ChatHeadingBlock(int Level, IReadOnlyList<ChatInline> Inlines) : ChatRenderBlock;

public sealed record ChatListBlock(bool Ordered, IReadOnlyList<ChatListItem> Items) : ChatRenderBlock;

public sealed record ChatQuoteBlock(IReadOnlyList<ChatInline> Inlines) : ChatRenderBlock;

public sealed record ChatCodeBlock(string Language, string Code) : ChatRenderBlock;

public sealed record ChatHorizontalRuleBlock : ChatRenderBlock;

public sealed record ChatListItem(int Level, IReadOnlyList<ChatInline> Inlines);

public abstract record ChatInline(string Text);

public sealed record ChatTextInline(string Text) : ChatInline(Text);

public sealed record ChatEmphasisInline(string Text, bool Bold, bool Italic) : ChatInline(Text);

public sealed record ChatCodeInline(string Text) : ChatInline(Text);

public sealed record ChatLinkInline(string Text, string Uri, bool IsSafe) : ChatInline(Text);
