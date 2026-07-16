param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\PersonalAI.Desktop.WinUI\bin\Release\net10.0-windows10.0.19041.0\PersonalAI.Desktop.WinUI.exe'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Programs')) 'AEDA.lnk')
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$workingDirectory = Split-Path -Parent $executable
$iconPath = Join-Path $workingDirectory 'Assets\AedaAppIcon.ico'

if (-not (Test-Path -LiteralPath $iconPath)) {
    throw "AEDA icon not found beside the executable: $iconPath"
}

$shortcutDirectory = Split-Path -Parent $ShortcutPath
[IO.Directory]::CreateDirectory($shortcutDirectory) | Out-Null
Remove-Item -LiteralPath $ShortcutPath -Force -ErrorAction SilentlyContinue

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $workingDirectory
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = 'AEDA local intelligence workspace'
$shortcut.Save()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class AedaShortcutIdentity
{
    private const uint ReadWrite = 0x00000002;
    private const ushort UnicodeString = 31;

    public static void SetAppUserModelId(string shortcutPath, string appUserModelId)
    {
        var interfaceId = typeof(IPropertyStore).GUID;
        IPropertyStore store;
        SHGetPropertyStoreFromParsingName(
            shortcutPath,
            IntPtr.Zero,
            ReadWrite,
            ref interfaceId,
            out store);
        var key = new PropertyKey(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            5);
        var value = PropVariant.FromString(appUserModelId);
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
            Marshal.ReleaseComObject(store);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                Type = UnicodeString,
                Pointer = Marshal.StringToCoTaskMemUni(value)
            };
        }
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHGetPropertyStoreFromParsingName(
        string path,
        IntPtr bindContext,
        uint flags,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
'@

[AedaShortcutIdentity]::SetAppUserModelId($ShortcutPath, 'AEDA.LocalIntelligence')
Get-Item -LiteralPath $ShortcutPath
