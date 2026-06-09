using System.Drawing;
using System.Windows.Forms;

namespace PersonalAI.Desktop.Windows;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(
        Action openPersonalAi,
        Action hide,
        Action exit)
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PersonalAI",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add(
            "Open PersonalAI",
            null,
            (_, _) => openPersonalAi());
        _notifyIcon.ContextMenuStrip.Items.Add(
            "Hide",
            null,
            (_, _) => hide());
        _notifyIcon.ContextMenuStrip.Items.Add(
            "Exit",
            null,
            (_, _) => exit());
        _notifyIcon.DoubleClick += (_, _) => openPersonalAi();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
