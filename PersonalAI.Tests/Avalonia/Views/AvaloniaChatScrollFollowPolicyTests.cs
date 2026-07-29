using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaChatScrollFollowPolicyTests
{
    [Fact]
    public void ContentShorterThanViewport_IsAtEnd()
    {
        Assert.True(ChatScrollFollowPolicy.IsAtEnd(
            offsetY: 0, extentHeight: 100, viewportHeight: 400));
    }

    [Fact]
    public void ScrolledToBottom_IsAtEnd()
    {
        Assert.True(ChatScrollFollowPolicy.IsAtEnd(
            offsetY: 600, extentHeight: 1000, viewportHeight: 400));
    }

    [Fact]
    public void ScrolledAwayFromBottom_IsNotAtEnd()
    {
        Assert.False(ChatScrollFollowPolicy.IsAtEnd(
            offsetY: 200, extentHeight: 1000, viewportHeight: 400));
    }

    [Fact]
    public void NearBottomWithinTolerance_StillFollows()
    {
        // A few pixels of slack keeps streaming from dropping follow mode.
        Assert.True(ChatScrollFollowPolicy.IsAtEnd(
            offsetY: 590, extentHeight: 1000, viewportHeight: 400));
    }

    [Fact]
    public void JustOutsideTolerance_StopsFollowing()
    {
        Assert.False(ChatScrollFollowPolicy.IsAtEnd(
            offsetY: 560, extentHeight: 1000, viewportHeight: 400, tolerance: 24));
    }

    [Fact]
    public void UserScrollDuringStreaming_StopsFollowingEvenThoughContentGrew()
    {
        // The reader moved away from the end in the same change that added content;
        // streaming must not pull the timeline back.
        var following = ChatScrollFollowPolicy.ShouldFollowAfterScrollChange(
            wasFollowing: true,
            offsetMoved: true,
            contentGrew: true,
            offsetY: 100,
            extentHeight: 1000,
            viewportHeight: 400);

        Assert.False(following);
    }

    [Fact]
    public void ContentGrowthAlone_KeepsPreviousFollowDecision()
    {
        // Extent grew without the reader moving, so the prior decision stands even though
        // the new extent puts the current offset far from the end.
        Assert.True(ChatScrollFollowPolicy.ShouldFollowAfterScrollChange(
            wasFollowing: true,
            offsetMoved: false,
            contentGrew: true,
            offsetY: 100,
            extentHeight: 1000,
            viewportHeight: 400));

        Assert.False(ChatScrollFollowPolicy.ShouldFollowAfterScrollChange(
            wasFollowing: false,
            offsetMoved: false,
            contentGrew: true,
            offsetY: 600,
            extentHeight: 1000,
            viewportHeight: 400));
    }

    [Fact]
    public void UserScrollBackToEnd_ResumesFollowing()
    {
        var following = ChatScrollFollowPolicy.ShouldFollowAfterScrollChange(
            wasFollowing: false,
            offsetMoved: true,
            contentGrew: false,
            offsetY: 600,
            extentHeight: 1000,
            viewportHeight: 400);

        Assert.True(following);
    }
}
