using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using PersonalAI.Desktop.Avalonia.Platform.Windows;
using System.Runtime.CompilerServices;

namespace PersonalAI.Desktop.Avalonia.Themes;

public sealed class AvaloniaTextScaleManager : IDisposable
{
    private readonly IWindowsTextScaleSource _source;
    private readonly Action<Action> _dispatch;
    private readonly List<Action<double>> _targets = [];
    private readonly Dictionary<Window, IDisposable> _windows = [];
    private readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, FontSizeState>> _states = new();
    private bool _disposed;

    public AvaloniaTextScaleManager(
        IWindowsTextScaleSource source,
        Action<Action> dispatch)
    {
        _source = source;
        _dispatch = dispatch;
        CurrentFactor = ReadFactor(source);
        source.Changed += OnSourceChanged;
    }

    public double CurrentFactor { get; private set; }

    public IDisposable AttachTarget(Action<double> apply)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _targets.Add(apply);
        apply(CurrentFactor);
        return new Subscription(() => _targets.Remove(apply));
    }

    public void Attach(Window window)
    {
        if (_disposed || _windows.ContainsKey(window))
        {
            return;
        }

        var target = AttachTarget(factor => Apply(window, factor));
        EventHandler layoutUpdated = (_, _) => Apply(window, CurrentFactor);
        EventHandler closed = null!;
        closed = (_, _) => Detach(window);
        window.LayoutUpdated += layoutUpdated;
        window.Closed += closed;
        _windows.Add(window, new Subscription(() =>
        {
            window.LayoutUpdated -= layoutUpdated;
            window.Closed -= closed;
            target.Dispose();
        }));
    }

    public static double ScaleFontSize(double baseline, double factor) =>
        baseline * Normalize(factor);

    public static double EffectiveFactorForWindow(double factor, Size clientSize) =>
        clientSize.Width <= 64 && clientSize.Height <= 64
            ? Math.Min(Normalize(factor), 1.25)
            : Normalize(factor);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Changed -= OnSourceChanged;
        _source.Dispose();
        foreach (var window in _windows.Values.ToArray())
        {
            window.Dispose();
        }

        _windows.Clear();
        _targets.Clear();
    }

    private void Apply(Window window, double factor)
    {
        // The idle Assist surface is intentionally icon-like and fixed at 52x52.
        var effectiveFactor = EffectiveFactorForWindow(factor, window.ClientSize);
        Apply(window, TemplatedControl.FontSizeProperty, effectiveFactor, force: true);

        foreach (var element in window.GetLogicalDescendants().OfType<AvaloniaObject>())
        {
            switch (element)
            {
                case TextBlock text when text.IsSet(TextBlock.FontSizeProperty):
                    Apply(text, TextBlock.FontSizeProperty, effectiveFactor);
                    break;
                case TemplatedControl control when control.IsSet(TemplatedControl.FontSizeProperty):
                    Apply(control, TemplatedControl.FontSizeProperty, effectiveFactor);
                    break;
                case TextElement text when text.IsSet(TextElement.FontSizeProperty):
                    Apply(text, TextElement.FontSizeProperty, effectiveFactor);
                    break;
            }
        }
    }

    private void Apply(
        AvaloniaObject target,
        StyledProperty<double> property,
        double factor,
        bool force = false)
    {
        if (!force && !target.IsSet(property))
        {
            return;
        }

        var observed = target.GetValue(property);
        var properties = _states.GetOrCreateValue(target);
        if (!properties.TryGetValue(property, out var state))
        {
            state = new FontSizeState(observed);
            properties.Add(property, state);
        }

        state.Apply(observed, factor, scaled => target.SetCurrentValue(property, scaled));
    }

    internal sealed class FontSizeState(double baseline)
    {
        private double _baseline = baseline;
        private double? _lastManagerValue;

        internal void Apply(double observed, double factor, Action<double> write)
        {
            if (_lastManagerValue is not double last || Different(observed, last))
            {
                _baseline = observed;
                _lastManagerValue = null;
            }

            var scaled = ScaleFontSize(_baseline, factor);
            if (!Different(observed, scaled))
            {
                return;
            }

            _lastManagerValue = scaled;
            write(scaled);
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e) =>
        _dispatch(() => UpdateFactor(ReadFactor(_source)));

    private void UpdateFactor(double factor)
    {
        if (_disposed || Math.Abs(CurrentFactor - factor) < 0.001)
        {
            return;
        }

        CurrentFactor = factor;
        foreach (var target in _targets.ToArray())
        {
            target(factor);
        }
    }

    private void Detach(Window window)
    {
        if (_windows.Remove(window, out var subscription))
        {
            subscription.Dispose();
        }
    }

    private static double ReadFactor(IWindowsTextScaleSource source)
    {
        try
        {
            return Normalize(source.Factor);
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException or
            PlatformNotSupportedException)
        {
            return 1;
        }
    }

    private static double Normalize(double factor) =>
        double.IsFinite(factor) && factor > 0 ? factor : 1;

    private static bool Different(double left, double right) =>
        Math.Abs(left - right) > 0.01;

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
