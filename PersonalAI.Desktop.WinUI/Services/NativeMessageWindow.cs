using InfrastructureNativeMessageWindow = PersonalAI.Infrastructure.Windows.NativeMessageWindow;

namespace PersonalAI.Desktop.WinUI.Services;

public class NativeMessageWindow : InfrastructureNativeMessageWindow
{
    protected NativeMessageWindow(string className)
        : base(className)
    {
    }
}
