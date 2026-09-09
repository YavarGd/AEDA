using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using PersonalAI.Desktop.Avalonia.Themes;
using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Accessibility;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TextScaleUiCollection
{
    public const string Name = "Text scale UI";
}

[Collection(TextScaleUiCollection.Name)]
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

    [Fact]
    public void SameValueResponsiveWriteBecomesTheNewApplicationBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 20 };

        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);
        Assert.Equal(22, text.FontSize, 10);

        text.FontSize = 22;
        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);

        Assert.Equal(24.2, text.FontSize, 10);
    }

    [Fact]
    public void SameValueWideWriteBecomesTheNewApplicationBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 20 };

        manager.Apply(text, TextBlock.FontSizeProperty, 1.30);
        Assert.Equal(26, text.FontSize, 10);

        text.FontSize = 26;
        manager.Apply(text, TextBlock.FontSizeProperty, 1.30);

        Assert.Equal(33.8, text.FontSize, 10);
    }

    [Fact]
    public void FactorChangeAfterSameValueCollisionUsesTheNewBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 20 };

        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);
        text.FontSize = 22;
        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);
        manager.Apply(text, TextBlock.FontSizeProperty, 1.50);

        Assert.Equal(33, text.FontSize, 10);
    }

    [Fact]
    public void RepeatedPassesWithoutAnApplicationWriteStayIdempotent()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 20 };

        for (var pass = 0; pass < 5; pass++)
        {
            manager.Apply(text, TextBlock.FontSizeProperty, 1.10);
            Assert.Equal(22, text.FontSize, 10);
        }
    }

    [Fact]
    public void SameLocalWriteClearsCurrentValueOwnership()
    {
        var text = new TextBlock { FontSize = 20 };

        text.SetCurrentValue(TextBlock.FontSizeProperty, 22);
        Assert.Equal(22, text.GetValue(TextBlock.FontSizeProperty));
        Assert.Equal(22, text.GetBaseValue(TextBlock.FontSizeProperty).Value);
        Assert.True(text.GetDiagnostic(TextBlock.FontSizeProperty).IsOverriddenCurrentValue);

        text.FontSize = 22;

        Assert.Equal(22, text.GetValue(TextBlock.FontSizeProperty));
        Assert.Equal(22, text.GetBaseValue(TextBlock.FontSizeProperty).Value);
        Assert.False(text.GetDiagnostic(TextBlock.FontSizeProperty).IsOverriddenCurrentValue);
    }

    [Fact]
    public void CurrentValueKeepsBindingAttached()
    {
        var source = new FontSizeSource(20);
        var text = new TextBlock();
        text.Bind(
            TextBlock.FontSizeProperty,
            new Binding(nameof(FontSizeSource.Value)) { Source = source });
        var binding = BindingOperations.GetBindingExpressionBase(text, TextBlock.FontSizeProperty);

        Assert.NotNull(binding);
        text.SetCurrentValue(TextBlock.FontSizeProperty, 22);

        Assert.Same(
            binding,
            BindingOperations.GetBindingExpressionBase(text, TextBlock.FontSizeProperty));
    }

    [Fact]
    public void CurrentValueRetainsStylePriority()
    {
        var text = new TextBlock();
        using var style = text.SetValue(TextBlock.FontSizeProperty, 20, BindingPriority.Style);

        Assert.Equal(20, text.FontSize);
        Assert.Equal(BindingPriority.Style, text.GetDiagnostic(TextBlock.FontSizeProperty).Priority);
        text.SetCurrentValue(TextBlock.FontSizeProperty, 22);

        Assert.Equal(22, text.FontSize);
        Assert.Equal(BindingPriority.Style, text.GetDiagnostic(TextBlock.FontSizeProperty).Priority);
        Assert.True(text.GetDiagnostic(TextBlock.FontSizeProperty).IsOverriddenCurrentValue);
    }

    [Fact]
    public void ResponsiveStyleChangeBecomesTheNewManagerBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock();
        var compactStyle = text.SetValue(TextBlock.FontSizeProperty, 20, BindingPriority.Style);

        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);
        compactStyle?.Dispose();
        using var mediumStyle = text.SetValue(TextBlock.FontSizeProperty, 22, BindingPriority.Style);
        manager.Apply(text, TextBlock.FontSizeProperty, 1.10);

        Assert.Equal(24.2, text.FontSize, 10);
        Assert.Equal(BindingPriority.Style, text.GetDiagnostic(TextBlock.FontSizeProperty).Priority);
    }

    [Fact]
    public void UnsetDefaultHasNoBaseValue()
    {
        var text = new TextBlock();

        Assert.False(text.GetBaseValue(TextBlock.FontSizeProperty).HasValue);
        text.SetCurrentValue(TextBlock.FontSizeProperty, 22);

        Assert.Equal(22, text.FontSize);
        Assert.False(text.GetBaseValue(TextBlock.FontSizeProperty).HasValue);
    }

    [Fact]
    public void ForcedDefaultAndInheritedValuesDoNotDrift()
    {
        using var manager = CreateManager();
        var defaultText = new TextBlock();
        var inheritedText = new TextBlock();
        var parent = new ContentControl { FontSize = 20, Content = inheritedText };

        Assert.Equal(20, inheritedText.FontSize);

        for (var pass = 0; pass < 3; pass++)
        {
            manager.Apply(defaultText, TextBlock.FontSizeProperty, 1.5, force: true);
            manager.Apply(inheritedText, TextBlock.FontSizeProperty, 1.5, force: true);
            Assert.Equal(18, defaultText.FontSize);
            Assert.Equal(30, inheritedText.FontSize);
        }

        GC.KeepAlive(parent);
    }

    [Fact]
    public void ChatHeadingResponsiveBasesRemainCompactMediumAndWide()
    {
        Assert.Equal(20, ChatView.ResolveLayout(compact: true, medium: false).EmptyTitleSize);
        Assert.Equal(22, ChatView.ResolveLayout(compact: false, medium: true).EmptyTitleSize);
        Assert.Equal(26, ChatView.ResolveLayout(compact: false, medium: false).EmptyTitleSize);
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
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };

        manager.Apply(text, TextBlock.FontSizeProperty, factor);
        Assert.Equal(initialScaled, text.FontSize);

        text.FontSize = 24;
        manager.Apply(text, TextBlock.FontSizeProperty, factor);
        Assert.Equal(responsiveScaled, text.FontSize);
    }

    [Fact]
    public void ManagerWritesAndRepeatedLayoutsDoNotDrift()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };

        for (var pass = 0; pass < 5; pass++)
        {
            manager.Apply(text, TextBlock.FontSizeProperty, 1.5);
            Assert.Equal(48, text.FontSize);
        }
    }

    [Fact]
    public void FactorTransitionsUseTheLatestResponsiveBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };

        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(32, text.FontSize);
        text.FontSize = 24;
        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(24, text.FontSize);
        manager.Apply(text, TextBlock.FontSizeProperty, 1.5);
        Assert.Equal(36, text.FontSize);
        manager.Apply(text, TextBlock.FontSizeProperty, 2);
        Assert.Equal(48, text.FontSize);
        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(24, text.FontSize);
    }

    [Fact]
    public void ExternalWriteClearsStaleManagerOwnership()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };

        manager.Apply(text, TextBlock.FontSizeProperty, 1.5);
        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(32, text.FontSize);

        text.FontSize = 24;
        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(24, text.FontSize);
        text.FontSize = 32;
        manager.Apply(text, TextBlock.FontSizeProperty, 1);
        Assert.Equal(32, text.FontSize);
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
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };

        manager.Apply(text, TextBlock.FontSizeProperty, factor);
        Assert.Equal(wideScaled, text.FontSize);
        text.FontSize = 24;
        manager.Apply(text, TextBlock.FontSizeProperty, factor);
        Assert.Equal(compactScaled, text.FontSize);
        text.FontSize = 32;
        manager.Apply(text, TextBlock.FontSizeProperty, factor);
        Assert.Equal(wideScaled, text.FontSize);
    }

    [Fact]
    public void ControlsKeepIndependentResponsiveBases()
    {
        using var manager = CreateManager();
        var heading = new TextBlock { FontSize = 32 };
        var body = new TextBlock { FontSize = 14 };

        manager.Apply(heading, TextBlock.FontSizeProperty, 1.5);
        manager.Apply(body, TextBlock.FontSizeProperty, 1.5);
        Assert.Equal(48, heading.FontSize);
        Assert.Equal(21, body.FontSize);

        heading.FontSize = 24;
        manager.Apply(heading, TextBlock.FontSizeProperty, 1.5);
        manager.Apply(body, TextBlock.FontSizeProperty, 1.5);
        Assert.Equal(36, heading.FontSize);
        Assert.Equal(21, body.FontSize);
    }

    [Fact]
    public void NewlyDiscoveredControlReceivesCurrentScaleWithoutChangingExistingControl()
    {
        using var manager = CreateManager();
        var existing = new TextBlock { FontSize = 32 };
        manager.Apply(existing, TextBlock.FontSizeProperty, 1.5);

        var added = new TextBlock { FontSize = 18 };
        manager.Apply(added, TextBlock.FontSizeProperty, 1.5);
        manager.Apply(existing, TextBlock.FontSizeProperty, 1.5);

        Assert.Equal(27, added.FontSize);
        Assert.Equal(48, existing.FontSize);
    }

    [Fact]
    public void ManagerWriteCanReenterWithoutBecomingTheBase()
    {
        using var manager = CreateManager();
        var text = new TextBlock { FontSize = 32 };
        var writes = 0;
        text.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.FontSizeProperty)
            {
                writes++;
                manager.Apply(text, TextBlock.FontSizeProperty, 1.5);
            }
        };

        manager.Apply(text, TextBlock.FontSizeProperty, 1.5);

        Assert.Equal(1, writes);
        Assert.Equal(48, text.FontSize);
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

    private sealed class FontSizeSource(double value)
    {
        public double Value => value;
    }

    private static AvaloniaTextScaleManager CreateManager() =>
        new(new FakeSource(), action => action());

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
