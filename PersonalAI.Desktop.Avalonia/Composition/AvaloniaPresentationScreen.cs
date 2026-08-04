using Avalonia.Controls;

namespace PersonalAI.Desktop.Avalonia.Composition;

public sealed record AvaloniaPresentationScreen(
    string Route,
    string Label,
    object ViewModel,
    Func<Control> CreateContent,
    Action<Control>? Focus = null);
