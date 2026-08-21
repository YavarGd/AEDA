using Avalonia.Controls;
using Avalonia.Platform;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows;

public sealed class AvaloniaTrayIconService : IDisposable
{
    private readonly TrayIcon _trayIcon;

    public AvaloniaTrayIconService(
        Action openAeda,
        Action newChat,
        Action exit)
    {
        ArgumentNullException.ThrowIfNull(openAeda);
        ArgumentNullException.ThrowIfNull(newChat);
        ArgumentNullException.ThrowIfNull(exit);

        var openItem = Item("Open AEDA", openAeda);
        var newChatItem = Item("New chat", newChat);
        var exitItem = Item("Exit", exit);
        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(newChatItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        using var iconStream = AssetLoader.Open(
            new Uri("avares://AEDA/Assets/AedaAppIcon.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            Menu = menu,
            ToolTipText = "AEDA",
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => openAeda();
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }

    private static NativeMenuItem Item(string label, Action action)
    {
        var item = new NativeMenuItem(label);
        item.Click += (_, _) => action();
        return item;
    }
}
