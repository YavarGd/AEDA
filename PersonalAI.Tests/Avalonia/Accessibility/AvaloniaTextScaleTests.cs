using System.Runtime.InteropServices;
using Avalonia;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.Themes;

namespace PersonalAI.Tests.Avalonia.Accessibility;

public sealed class AvaloniaTextScaleTests
{
    [Theory]
    [InlineData(1, 12, 14, 20, 26)]
    [InlineData(1.5, 18, 21, 30, 39)]
    [InlineData(2, 24, 28, 40, 52)]
    public void FactorScalesBodyControlNavigationAndHeadingTypography(
        double factor,
        double body,
        double control,
        double navigation,
        double heading)
    {
        Assert.Equal(body, AvaloniaTextScaleManager.ScaleFontSize(12, factor));
        Assert.Equal(control, AvaloniaTextScaleManager.ScaleFontSize(14, factor));
        Assert.Equal(navigation, AvaloniaTextScaleManager.ScaleFontSize(20, factor));
        Assert.Equal(heading, AvaloniaTextScaleManager.ScaleFontSize(26, factor));
    }

    [Fact]
    public void CompactAssistPillCapsTypographyWithoutLimitingExpandedAssist()
    {
        Assert.Equal(1.25, AvaloniaTextScaleManager.EffectiveFactorForWindow(2, new Size(52, 52)));
        Assert.Equal(2, AvaloniaTextScaleManager.EffectiveFactorForWindow(2, new Size(440, 180)));
    }

    [Fact]
    public void AppAttachesEveryOpenedWindowToTheSingleTextScaleManager()
    {
        var app = ReadAvaloniaSource("App.axaml.cs");
        var manager = ReadAvaloniaSource("Themes", "AvaloniaTextScaleManager.cs");
        var source = ReadAvaloniaSource("Platform", "Windows", "WindowsTextScaleSource.cs");

        Assert.Contains("Window.WindowOpenedEvent.AddClassHandler<Window>", app, StringComparison.Ordinal);
        Assert.Contains("_textScaleManager?.Attach(window)", app, StringComparison.Ordinal);
        Assert.Contains("TextScaleFactorChanged", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post", app, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderTransform", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonApplicationSettings", app + manager + source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 32, 24)]
    [InlineData(1.5, 48, 36)]
    [InlineData(2, 64, 48)]
    public void ResponsiveWritesReplaceTheApplicationBase(
        double factor,
        double initialScaled,
        double responsiveScaled)
    {
        var size = 32d;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        size = Apply(state, size, factor);
        Assert.Equal(initialScaled, size);

        size = 24;
        size = Apply(state, size, factor);
        Assert.Equal(responsiveScaled, size);
    }

    [Fact]
    public void ManagerWritesAndRepeatedLayoutsDoNotDrift()
    {
        var size = 32d;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        for (var pass = 0; pass < 5; pass++)
        {
            size = Apply(state, size, 1.5);
            Assert.Equal(48, size);
        }
    }

    [Fact]
    public void FactorTransitionsUseTheLatestResponsiveBase()
    {
        var size = 32d;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        size = Apply(state, size, 1);
        Assert.Equal(32, size);
        size = 24;
        size = Apply(state, size, 1);
        Assert.Equal(24, size);
        size = Apply(state, size, 1.5);
        Assert.Equal(36, size);
        size = Apply(state, size, 2);
        Assert.Equal(48, size);
        size = Apply(state, size, 1);
        Assert.Equal(24, size);
    }

    [Fact]
    public void ExternalWriteClearsStaleManagerOwnership()
    {
        var size = 32d;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        size = Apply(state, size, 1.5);
        size = Apply(state, size, 1);
        Assert.Equal(32, size);

        size = Apply(state, 24, 1);
        Assert.Equal(24, size);
        size = Apply(state, 32, 1);
        Assert.Equal(32, size);
    }

    [Theory]
    [InlineData(1, 32, 24)]
    [InlineData(1.5, 48, 36)]
    [InlineData(2, 64, 48)]
    public void WideCompactWideUsesEachLatestBase(
        double factor,
        double wideScaled,
        double compactScaled)
    {
        var size = 32d;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        size = Apply(state, size, factor);
        Assert.Equal(wideScaled, size);
        size = Apply(state, 24, factor);
        Assert.Equal(compactScaled, size);
        size = Apply(state, 32, factor);
        Assert.Equal(wideScaled, size);
    }

    [Fact]
    public void ControlsKeepIndependentResponsiveBases()
    {
        var heading = 32d;
        var body = 14d;
        var headingState = new AvaloniaTextScaleManager.FontSizeState(heading);
        var bodyState = new AvaloniaTextScaleManager.FontSizeState(body);

        heading = Apply(headingState, heading, 1.5);
        body = Apply(bodyState, body, 1.5);
        Assert.Equal(48, heading);
        Assert.Equal(21, body);

        heading = Apply(headingState, 24, 1.5);
        body = Apply(bodyState, body, 1.5);
        Assert.Equal(36, heading);
        Assert.Equal(21, body);
    }

    [Fact]
    public void NewlyDiscoveredControlReceivesCurrentScaleWithoutChangingExistingControl()
    {
        var existing = 32d;
        var existingState = new AvaloniaTextScaleManager.FontSizeState(existing);
        existing = Apply(existingState, existing, 1.5);

        var added = 18d;
        var addedState = new AvaloniaTextScaleManager.FontSizeState(added);
        added = Apply(addedState, added, 1.5);
        existing = Apply(existingState, existing, 1.5);

        Assert.Equal(27, added);
        Assert.Equal(48, existing);
    }

    [Fact]
    public void ManagerWriteCanReenterWithoutBecomingTheBase()
    {
        var size = 32d;
        var writes = 0;
        var state = new AvaloniaTextScaleManager.FontSizeState(size);

        state.Apply(size, 1.5, scaled =>
        {
            writes++;
            size = scaled;
            state.Apply(size, 1.5, nested =>
            {
                writes++;
                size = nested;
            });
        });

        Assert.Equal(1, writes);
        Assert.Equal(48, size);
    }

    [Fact]
    public void LiveFactorsAreIdempotentAndNeverCumulative()
    {
        var source = new FakeSource();
        using var manager = new AvaloniaTextScaleManager(source, action => action());
        var sizes = new List<double>();
        using var target = manager.AttachTarget(
            factor => sizes.Add(AvaloniaTextScaleManager.ScaleFontSize(14, factor)));

        source.Set(1.5);
        source.Set(2);
        source.Set(2);
        source.Set(1);

        Assert.Equal([14, 21, 28, 14], sizes);
    }

    [Fact]
    public void LiveChangeIsMarshalledBeforeExistingTargetsUpdate()
    {
        Action? queued = null;
        var source = new FakeSource();
        using var manager = new AvaloniaTextScaleManager(source, action => queued = action);
        var factor = 0d;
        using var target = manager.AttachTarget(value => factor = value);

        source.Set(1.5);

        Assert.Equal(1, factor);
        Assert.NotNull(queued);
        queued();
        Assert.Equal(1.5, factor);
    }

    [Fact]
    public void TargetAddedAfterChangeReceivesCurrentFactor()
    {
        var source = new FakeSource();
        using var manager = new AvaloniaTextScaleManager(source, action => action());
        source.Set(2);
        var main = 0d;
        var assist = 0d;

        using var mainTarget = manager.AttachTarget(value => main = value);
        using var assistTarget = manager.AttachTarget(value => assist = value);

        Assert.Equal(2, main);
        Assert.Equal(main, assist);
    }

    [Fact]
    public void ExistingMainAndAssistTargetsUpdateFromTheSameLiveSource()
    {
        var source = new FakeSource();
        using var manager = new AvaloniaTextScaleManager(source, action => action());
        var main = 0d;
        var assist = 0d;
        using var mainTarget = manager.AttachTarget(value => main = value);
        using var assistTarget = manager.AttachTarget(value => assist = value);

        source.Set(1.5);

        Assert.Equal(1.5, main);
        Assert.Equal(main, assist);
    }

    [Fact]
    public void DisposalUnsubscribesFromWindowsSource()
    {
        var source = new FakeSource();
        var manager = new AvaloniaTextScaleManager(source, action => action());
        Assert.Equal(1, source.SubscriberCount);

        manager.Dispose();

        Assert.Equal(0, source.SubscriberCount);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void ExpectedPlatformFailureFallsBackToBaseline()
    {
        using var manager = new AvaloniaTextScaleManager(
            new ThrowingSource(),
            action => action());

        Assert.Equal(1, manager.CurrentFactor);
    }

    private sealed class FakeSource : IWindowsTextScaleSource
    {
        private EventHandler? _changed;

        public double Factor { get; private set; } = 1;

        public int SubscriberCount { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler? Changed
        {
            add
            {
                _changed += value;
                SubscriberCount++;
            }
            remove
            {
                _changed -= value;
                SubscriberCount--;
            }
        }

        public void Set(double factor)
        {
            Factor = factor;
            _changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingSource : IWindowsTextScaleSource
    {
        public double Factor => throw new COMException();

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    private static double Apply(
        AvaloniaTextScaleManager.FontSizeState state,
        double observed,
        double factor)
    {
        var result = observed;
        state.Apply(observed, factor, scaled => result = scaled);
        return result;
    }

    private static string ReadAvaloniaSource(params string[] path) =>
        File.ReadAllText(FindRepositoryFile("PersonalAI.Desktop.Avalonia", path));

    private static string FindRepositoryFile(string project, params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, project, .. path]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(path)}.");
    }
}
