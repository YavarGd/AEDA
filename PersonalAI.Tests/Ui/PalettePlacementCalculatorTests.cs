using PersonalAI.Core.Ui;
using PersonalAI.Desktop.WinUI.Views;

namespace PersonalAI.Tests.Ui;

public sealed class PalettePlacementCalculatorTests
{
    [Fact]
    public void CenterInBounds_CentersPaletteInAvailableArea()
    {
        var position = PalettePlacementCalculator.CenterInBounds(
            new RectBounds(100, 50, 1000, 700),
            width: 500,
            height: 300);

        Assert.Equal(350, position.X);
        Assert.Equal(250, position.Y);
    }

    [Fact]
    public void CenterInBounds_DoesNotPlacePaletteBeforeAvailableArea()
    {
        var position = PalettePlacementCalculator.CenterInBounds(
            new RectBounds(100, 50, 300, 200),
            width: 500,
            height: 300);

        Assert.Equal(100, position.X);
        Assert.Equal(50, position.Y);
    }

    [Fact]
    public void NearCursor_ClampsPaletteInsideAvailableArea()
    {
        var position = PalettePlacementCalculator.NearCursor(
            new RectBounds(0, 0, 800, 600),
            new PointPosition(790, 590),
            width: 300,
            height: 220);

        Assert.Equal(500, position.X);
        Assert.Equal(380, position.Y);
    }

    [Fact]
    public void BottomRightInBounds_UsesWorkAreaMargin()
    {
        var position = PalettePlacementCalculator.BottomRightInBounds(
            new RectBounds(100, 50, 1000, 700),
            width: 56,
            height: 56,
            margin: 20);

        Assert.Equal(1024, position.X);
        Assert.Equal(674, position.Y);
    }

    [Fact]
    public void BottomRightInBounds_ClampsOversizedSurfaceSafely()
    {
        var position = PalettePlacementCalculator.BottomRightInBounds(
            new RectBounds(100, 50, 40, 40),
            width: 56,
            height: 56,
            margin: 20);

        Assert.Equal(100, position.X);
        Assert.Equal(50, position.Y);
    }

    [Fact]
    public void AssistResponseSizing_EmptyResponseUsesCompactMinimum()
    {
        var layout = AssistResponseSizingPolicy.Calculate(
            0,
            new RectBounds(0, 0, 1920, 1040),
            1);

        Assert.Equal(560, layout.Width);
        Assert.Equal(180, layout.Height);
        Assert.False(layout.RequiresScrolling);
    }

    [Fact]
    public void AssistResponseSizing_GrowsAndCapsLongResponses()
    {
        var area = new RectBounds(0, 0, 1920, 1040);
        var shortResponse = AssistResponseSizingPolicy.Calculate(100, area, 1);
        var longerResponse = AssistResponseSizingPolicy.Calculate(1_000, area, 1);
        var maximum = AssistResponseSizingPolicy.Calculate(100_000, area, 1);

        Assert.True(shortResponse.Height > 180);
        Assert.True(longerResponse.Height > shortResponse.Height);
        Assert.Equal(480, maximum.Height);
        Assert.True(maximum.RequiresScrolling);
    }

    [Fact]
    public void AssistResponseSizing_RespectsWorkAreaAndDisplayScale()
    {
        var scaled = AssistResponseSizingPolicy.Calculate(
            0,
            new RectBounds(0, 0, 2560, 1440),
            2);
        var constrained = AssistResponseSizingPolicy.Calculate(
            100_000,
            new RectBounds(0, 0, 400, 300),
            1);

        Assert.Equal(1_120, scaled.Width);
        Assert.Equal(360, scaled.Height);
        Assert.True(constrained.Width <= 360);
        Assert.True(constrained.Height <= 260);
    }

    [Fact]
    public void AssistResponseSizing_IsBatchedInsteadOfPerToken()
    {
        Assert.Equal(150, AssistPillWindow.ResponseResizeIntervalMilliseconds);
    }

    [Fact]
    public void IsVisibleWithinAnyWorkingArea_RejectsOffscreenPosition()
    {
        var isVisible = WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
            new WindowPosition(2000, 2000),
            width: 500,
            height: 300,
            [new RectBounds(0, 0, 1280, 720)]);

        Assert.False(isVisible);
    }

    [Fact]
    public void IsVisibleWithinAnyWorkingArea_AcceptsPartiallyVisiblePosition()
    {
        var isVisible = WindowPositionValidator.IsVisibleWithinAnyWorkingArea(
            new WindowPosition(1200, 650),
            width: 500,
            height: 300,
            [new RectBounds(0, 0, 1280, 720)]);

        Assert.True(isVisible);
    }

    [Fact]
    public void ClampToVisibleWorkingArea_MovesPartiallyVisibleWindowFullyOnscreen()
    {
        var position = WindowPositionValidator.ClampToVisibleWorkingArea(
            new WindowPosition(1200, 650),
            width: 500,
            height: 300,
            [new RectBounds(0, 0, 1280, 720)]);

        Assert.Equal(new WindowPosition(780, 420), position);
    }

    [Fact]
    public void ClampToVisibleWorkingArea_RejectsDisconnectedDisplayPosition()
    {
        var position = WindowPositionValidator.ClampToVisibleWorkingArea(
            new WindowPosition(2200, 100),
            width: 250,
            height: 58,
            [new RectBounds(0, 0, 1920, 1040)]);

        Assert.Null(position);
    }

    [Fact]
    public void SavedPosition_RoundTripsBeforeBoundsCorrection()
    {
        var expected = new WindowPosition(-1200, 240);

        var loaded = WindowPositionJson.Deserialize(
            WindowPositionJson.Serialize(expected));

        Assert.Equal(expected, loaded);
        Assert.Null(WindowPositionJson.Deserialize("not-json"));
    }
}
