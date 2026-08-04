using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PersonalAI.Desktop.Avalonia.Platform.Windows.Assist;

internal static class WindowsAssistTargetGuard
{
    public static bool IsHigherIntegrity(uint processId)
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            using var target = Process.GetProcessById((int)processId);
            return Integrity(target.Handle) > Integrity(current.Handle);
        }
        catch
        {
            return true;
        }
    }

    private static int Integrity(nint process)
    {
        if (!OpenProcessToken(process, 0x0008, out var token))
        {
            throw new Win32Exception();
        }

        try
        {
            _ = GetTokenInformation(token, 25, 0, 0, out var length);
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _))
                {
                    throw new Win32Exception();
                }

                var sid = Marshal.ReadIntPtr(buffer);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseHandle(token);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        nint token,
        int informationClass,
        nint information,
        int length,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
