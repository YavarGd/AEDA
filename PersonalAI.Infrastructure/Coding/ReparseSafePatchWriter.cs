using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PersonalAI.Infrastructure.Coding;

internal static class ReparseSafePatchWriter
{
    private const uint FileListDirectory = 0x0001;
    private const uint FileAddFile = 0x0002;
    private const uint FileAddSubdirectory = 0x0004;
    private const uint FileReadAttributes = 0x0080;
    private const uint DeleteChild = 0x0040;
    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeTemporary = 0x00000100;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint FileOpen = 1;
    private const uint FileCreate = 2;
    private const uint FileOpenIf = 3;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjectCaseInsensitive = 0x00000040;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfoClass = 9;

    public static void Write(
        string workspaceRoot,
        string relativePath,
        string content,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PatchApplyValidator.RejectUnsafeRelativePath(relativePath);

        if (!OperatingSystem.IsWindows())
        {
            WritePortable(workspaceRoot, relativePath, content, replaceExisting);
            return;
        }

        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            throw new InvalidOperationException("path_outside_workspace");
        }

        using var directories = OpenDestinationParent(workspaceRoot, parts[..^1]);
        var parent = directories.Parent;
        var destinationName = parts[^1];
        using (var existing = TryOpenRelative(
            parent,
            destinationName,
            FileReadAttributes | Synchronize,
            ShareRead | ShareWrite | ShareDelete,
            FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeNormal))
        {
            if (existing is not null)
            {
                RejectReparsePoint(existing);
                EnsureHandleInside(workspaceRoot, existing);
                if (!replaceExisting)
                {
                    throw new InvalidOperationException("write_failed");
                }
            }
            else if (replaceExisting)
            {
                throw new InvalidOperationException("path_outside_workspace");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stagingName = $".aeda-patch-{Guid.NewGuid():N}.tmp";
        using var staging = OpenRelative(
            parent,
            stagingName,
            GenericRead | GenericWrite | Delete | Synchronize,
            ShareRead | ShareWrite | ShareDelete,
            FileCreate,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeNormal | FileAttributeTemporary);

        var renamed = false;
        try
        {
            WriteAndVerify(staging, content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RenameRelative(staging, parent, destinationName, replaceExisting);
            renamed = true;
        }
        finally
        {
            if (!renamed)
            {
                TryDelete(staging);
            }
        }
    }

    private static DirectoryHandleChain OpenDestinationParent(
        string workspaceRoot,
        IReadOnlyList<string> parts)
    {
        var current = CreateFileW(
            workspaceRoot,
            FileListDirectory | FileAddFile | FileAddSubdirectory | FileReadAttributes | DeleteChild | Synchronize,
            ShareRead | ShareWrite,
            IntPtr.Zero,
            OpenExisting,
            BackupSemantics | OpenReparsePoint,
            IntPtr.Zero);
        if (current.IsInvalid)
        {
            current.Dispose();
            throw new InvalidOperationException("path_outside_workspace");
        }

        var handles = new List<SafeFileHandle> { current };
        try
        {
            RejectReparsePoint(current);
            EnsureHandleInside(workspaceRoot, current);
            foreach (var part in parts)
            {
                var next = OpenDirectoryRelative(
                    current,
                    part,
                    FileListDirectory | FileAddFile | FileAddSubdirectory | FileReadAttributes | DeleteChild | Synchronize);
                ValidateDestinationDirectory(workspaceRoot, next);
                handles.Add(next);
                current = next;
            }

            return new DirectoryHandleChain(handles);
        }
        catch
        {
            foreach (var handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    private static void ValidateDestinationDirectory(string workspaceRoot, SafeFileHandle handle)
    {
        try
        {
            RejectReparsePoint(handle);
            EnsureHandleInside(workspaceRoot, handle);
        }
        catch
        {
            handle.Dispose();
            throw new InvalidOperationException("path_outside_workspace");
        }
    }

    private static SafeFileHandle OpenDirectoryRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess)
    {
        var handle = TryOpenRelative(
            parent,
            name,
            desiredAccess,
            ShareRead | ShareWrite,
            FileOpenIf,
            FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeDirectory);
        return handle ?? throw new InvalidOperationException("path_outside_workspace");
    }

    private static SafeFileHandle OpenRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint createOptions,
        uint attributes)
    {
        var handle = TryOpenRelative(
            parent,
            name,
            desiredAccess,
            shareAccess,
            disposition,
            createOptions,
            attributes,
            out _);
        if (handle is not null)
        {
            return handle;
        }

        throw new InvalidOperationException("write_failed");
    }

    private static SafeFileHandle? TryOpenRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint createOptions,
        uint attributes) =>
        TryOpenRelative(
            parent,
            name,
            desiredAccess,
            shareAccess,
            disposition,
            createOptions,
            attributes,
            out _);

    private static SafeFileHandle? TryOpenRelative(
        SafeFileHandle parent,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint disposition,
        uint createOptions,
        uint attributes,
        out int status)
    {
        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            var nameLength = checked((ushort)(name.Length * sizeof(char)));
            Marshal.StructureToPtr(
                new UnicodeString(
                    nameLength,
                    checked((ushort)(nameLength + sizeof(char))),
                    nameBuffer),
                unicodePointer,
                fDeleteOld: false);
            var objectAttributes = new ObjectAttributes(
                Marshal.SizeOf<ObjectAttributes>(),
                parent.DangerousGetHandle(),
                unicodePointer,
                ObjectCaseInsensitive,
                IntPtr.Zero,
                IntPtr.Zero);
            status = NtCreateFile(
                out var handle,
                desiredAccess,
                ref objectAttributes,
                out _,
                IntPtr.Zero,
                attributes,
                shareAccess,
                disposition,
                createOptions,
                IntPtr.Zero,
                0);
            if (status >= 0 && !handle.IsInvalid)
            {
                return handle;
            }

            handle.Dispose();
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static void WriteAndVerify(
        SafeFileHandle handle,
        string content,
        CancellationToken cancellationToken)
    {
        var expected = Encoding.UTF8.GetBytes(content);
        RandomAccess.SetLength(handle, 0);
        RandomAccess.Write(handle, expected, 0);
        RandomAccess.FlushToDisk(handle);
        cancellationToken.ThrowIfCancellationRequested();
        var actualBytes = new byte[expected.Length];
        var offset = 0;
        while (offset < actualBytes.Length)
        {
            var read = RandomAccess.Read(handle, actualBytes.AsSpan(offset), offset);
            if (read == 0)
            {
                throw new InvalidOperationException("hash_mismatch");
            }

            offset += read;
        }

        var actual = Encoding.UTF8.GetString(actualBytes);
        if (CodeContextService.ComputeHash(actual) != CodeContextService.ComputeHash(content))
        {
            throw new InvalidOperationException("hash_mismatch");
        }
    }

    private static void RenameRelative(
        SafeFileHandle source,
        SafeFileHandle parent,
        string destinationName,
        bool replaceExisting)
    {
        var nameBytes = Encoding.Unicode.GetBytes(destinationName);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(int);
        var bufferSize = checked(nameOffset + nameBytes.Length + sizeof(char));
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var index = 0; index < nameOffset; index++)
            {
                Marshal.WriteByte(buffer, index, 0);
            }

            Marshal.WriteByte(buffer, 0, replaceExisting ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(buffer, rootOffset, parent.DangerousGetHandle());
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
            Marshal.WriteInt16(buffer, nameOffset + nameBytes.Length, 0);
            var status = NtSetInformationFile(
                source,
                out _,
                buffer,
                (uint)bufferSize,
                FileRenameInformation);
            if (status < 0)
            {
                throw new InvalidOperationException("write_failed");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void TryDelete(SafeFileHandle handle)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(byte));
        try
        {
            Marshal.WriteByte(buffer, 1);
            _ = SetFileInformationByHandle(handle, FileDispositionInfo, buffer, sizeof(byte));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void RejectReparsePoint(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(
                handle,
                FileAttributeTagInfoClass,
                out var info,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            throw new InvalidOperationException("write_failed");
        }

        if ((info.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("path_outside_workspace");
        }
    }

    private static void EnsureHandleInside(string workspaceRoot, SafeFileHandle handle)
    {
        var actual = GetFinalPath(handle);
        var expectedRoot = Path.GetFullPath(workspaceRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedActual = Path.GetFullPath(actual).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.Equals(expectedRoot, normalizedActual, StringComparison.OrdinalIgnoreCase) &&
            !normalizedActual.StartsWith(
                expectedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("path_outside_workspace");
        }
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0)
        {
            throw new InvalidOperationException("write_failed");
        }

        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                throw new InvalidOperationException("write_failed");
            }
        }

        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[8..];
        }

        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
            ? path[4..]
            : path;
    }

    private static void WritePortable(
        string workspaceRoot,
        string relativePath,
        string content,
        bool replaceExisting)
    {
        var fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath));
        var root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("path_outside_workspace");
        }

        var directory = Path.GetDirectoryName(fullPath) ?? workspaceRoot;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".aeda-patch-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, fullPath, replaceExisting);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private sealed class DirectoryHandleChain(IReadOnlyList<SafeFileHandle> handles) : IDisposable
    {
        public SafeFileHandle Parent => handles[^1];

        public void Dispose()
        {
            for (var index = handles.Count - 1; index >= 0; index--)
            {
                handles[index].Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UnicodeString(
        ushort Length,
        ushort MaximumLength,
        IntPtr Buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ObjectAttributes(
        int Length,
        IntPtr RootDirectory,
        IntPtr ObjectName,
        uint Attributes,
        IntPtr SecurityDescriptor,
        IntPtr SecurityQualityOfService);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct IoStatusBlock(
        IntPtr Status,
        UIntPtr Information);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FileAttributeTagInfo(
        uint FileAttributes,
        uint ReparseTag);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out SafeFileHandle fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        out FileAttributeTagInfo fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);
}
