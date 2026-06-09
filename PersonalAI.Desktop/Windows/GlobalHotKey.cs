namespace PersonalAI.Desktop.Windows;

public sealed record GlobalHotKey(
    int Id,
    HotKeyModifiers Modifiers,
    uint VirtualKey);
