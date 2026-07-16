using PersonalAI.Core.Ui;

namespace PersonalAI.Tests.Ui;

public sealed class AssistMotionPolicyTests
{
    [Fact]
    public void Entrance_OnlyPointerWithAnimationsUsesSpatialMotion()
    {
        var response = AssistMotionPolicy.Entrance(AssistInvocationKind.Pointer, false, true);
        var spotlight = AssistMotionPolicy.Entrance(AssistInvocationKind.Pointer, true, true);
        var keyboard = AssistMotionPolicy.Entrance(AssistInvocationKind.Keyboard, true, true);
        var reduced = AssistMotionPolicy.Entrance(AssistInvocationKind.Pointer, false, false);

        Assert.Equal(new AssistEntranceMotion(true, 0.97, 160), response);
        Assert.Equal(new AssistEntranceMotion(true, 0.98, 140), spotlight);
        Assert.Equal(new AssistEntranceMotion(false, 1, 0), keyboard);
        Assert.Equal(new AssistEntranceMotion(false, 1, 0), reduced);
        Assert.InRange(AssistMotionPolicy.FirstContentFadeMilliseconds, 100, 140);
    }

    [Fact]
    public void HeightInterpolation_IsMonotonicRetargetableAndBounded()
    {
        var motion = new AssistHeightInterpolator();
        motion.Retarget(180, 320, 480, true, true, 7, TimeSpan.Zero);

        Assert.True(motion.IsActive);
        Assert.True(motion.TrySample(TimeSpan.FromMilliseconds(90), 7, out var middle));
        Assert.InRange(middle, 181, 319);

        motion.Retarget(middle, 420, 480, true, true, 7, TimeSpan.FromMilliseconds(90));
        Assert.True(motion.TrySample(TimeSpan.FromMilliseconds(270), 7, out var completed));
        Assert.Equal(420, completed);
        Assert.False(motion.IsActive);
    }

    [Fact]
    public void HeightInterpolation_StreamingDoesNotShrinkAndCaps()
    {
        var motion = new AssistHeightInterpolator();

        Assert.Equal(300, motion.Retarget(300, 240, 480, true, true, 1, TimeSpan.Zero));
        Assert.False(motion.IsActive);
        motion.Retarget(300, 900, 480, true, true, 1, TimeSpan.Zero);
        Assert.Equal(480, motion.TargetHeight);
        Assert.True(motion.TrySample(TimeSpan.FromMilliseconds(180), 1, out var capped));
        Assert.Equal(480, capped);
        Assert.Equal(480, motion.Retarget(480, 900, 480, true, true, 1, TimeSpan.FromSeconds(1)));
        Assert.False(motion.IsActive);
    }

    [Fact]
    public void HeightInterpolation_CancellationInvocationAndReducedMotionAreImmediate()
    {
        var motion = new AssistHeightInterpolator();
        motion.Retarget(180, 360, 480, true, true, 2, TimeSpan.Zero);

        Assert.False(motion.TrySample(TimeSpan.FromMilliseconds(16), 3, out _));
        motion.Cancel();
        Assert.False(motion.IsActive);
        Assert.Equal(360, motion.Retarget(180, 360, 480, true, false, 4, TimeSpan.Zero));
        Assert.False(motion.IsActive);
    }
}
