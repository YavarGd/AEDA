using PersonalAI.Core.Ui;

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
