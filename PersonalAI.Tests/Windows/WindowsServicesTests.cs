using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Windows;

public sealed class WindowsServicesTests
{
    [Fact]
    public void HotkeyMapper_MapsModifiersAndFunctionKeys()
    {
        var mapped = WindowsHotkeyMapper.TryMap(
            new HotkeySettings(true, true, true, true, "f24"),
            out var hotkey,
            out var error);

        Assert.True(mapped, error);
        Assert.Equal(0x0Fu, hotkey.Modifiers);
        Assert.Equal(0x87u, hotkey.VirtualKey);
    }

    [Fact]
    public void SingleInstance_UsesWinUiMutexName()
    {
        Assert.Equal(
            "Local\\PersonalAI.WinUI.SingleInstance",
            WindowsSingleInstanceService.MutexName);
    }
}
