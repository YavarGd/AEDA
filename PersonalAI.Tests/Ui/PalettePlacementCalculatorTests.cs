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
}
