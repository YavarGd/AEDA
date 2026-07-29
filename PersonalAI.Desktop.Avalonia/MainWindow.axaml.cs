using Avalonia.Controls;

namespace PersonalAI.Desktop.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ReadyButton.Focus();
    }
}
