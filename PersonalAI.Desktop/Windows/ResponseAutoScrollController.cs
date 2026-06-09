using System.Windows.Threading;

namespace PersonalAI.Desktop.Windows;

public sealed class ResponseAutoScrollController(
    System.Windows.Controls.TextBox textBox)
{
    public void ScrollToEndAsync()
    {
        _ = textBox.Dispatcher.BeginInvoke(
            () => textBox.ScrollToEnd(),
            DispatcherPriority.Background);
    }
}
