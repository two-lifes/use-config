using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using UseConfig.Catalog;

namespace UseConfig.Storage;

internal sealed class CatalogFault(ErrorCode code) : Exception
{
    internal ErrorCode Code { get; } = code;
}

internal readonly record struct NativeStamp(uint Volume, ulong Id, ulong Written, ulong Length, uint Attributes);

internal sealed class NativeCatalogFile : IDisposable
{
    internal SafeFileHandle Handle { get; }
    internal bool IsDirectory { get; }
    private readonly FileStream? stream;

    private NativeCatalogFile(SafeFileHandle handle, bool directory, bool write)
    {
        Handle = handle;
        IsDirectory = directory;
        try
        {
            _ = Stamp();
            if (!directory) stream = new FileStream(handle, write ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static NativeCatalogFile Open(string path, bool directory, bool write = false, bool delete = false)
    {
        var access = directory ? 0x80u : 0x80000000u;
        if (write) access |= 0x40000000;
        if (delete) access |= 0x10000;
        var share = directory ? 3u : write || delete ? 0u : 1u;
        // Relative NtCreateFile creates long paths too; reopening must use the same extended-length namespace.
        var nativePath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        var handle = CreateFileW(nativePath, access, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return new NativeCatalogFile(handle, directory, write);
    }

    internal static NativeCatalogFile Create(NativeCatalogFile parent, string name, bool directory)
    {
        if (!MarkdownCatalogValidator.ValidComponent(name)) throw new CatalogFault(ErrorCode.InvalidInput);
        var text = Marshal.StringToHGlobalUni(name);
        var unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeName>());
        try
        {
            var length = checked((ushort)(name.Length * 2));
            Marshal.StructureToPtr(new UnicodeName { Length = length, MaximumLength = length, Buffer = text }, unicodePointer, false);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                Root = parent.Handle.DangerousGetHandle(),
                Name = unicodePointer,
                Attributes = 0x40
            };
            // FILE_CREATE never adopts an existing object. Return its handle without a path-reopen window.
            var access = directory ? 0x001100a1u : 0xc0110000u;
            var status = NtCreateFile(out var handle, access, ref attributes, out _, IntPtr.Zero,
                directory ? 0x10u : 0x80u, directory ? 3u : 0u, 2, 0x00200020u | (directory ? 1u : 0x40u), IntPtr.Zero, 0);
            GC.KeepAlive(parent);
            if (status < 0)
            {
                handle?.Dispose();
                throw new Win32Exception((int)RtlNtStatusToDosError(status));
            }
            return new NativeCatalogFile(handle!, directory, !directory);
        }
        finally
        {
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    internal NativeStamp Stamp()
    {
        if (!GetFileInformationByHandle(Handle, out var information)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((information.Attributes & 0x400) != 0 || ((information.Attributes & 0x10) != 0) != IsDirectory)
            throw new CatalogFault(ErrorCode.InvalidInput);
        return new NativeStamp(information.Volume, ((ulong)information.IndexHigh << 32) | information.IndexLow,
            ((ulong)information.WriteHigh << 32) | information.WriteLow, ((ulong)information.SizeHigh << 32) | information.SizeLow, information.Attributes);
    }

    internal byte[] Read()
    {
        if (stream is null) throw new InvalidOperationException();
        stream.Position = 0;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal void Write(byte[] bytes, Action? partial = null)
    {
        if (stream is null) throw new InvalidOperationException();
        stream.Position = 0;
        stream.SetLength(0);
        var half = bytes.Length / 2;
        stream.Write(bytes.AsSpan(0, half));
        stream.Flush(true);
        partial?.Invoke();
        stream.Write(bytes.AsSpan(half));
        stream.SetLength(bytes.Length);
        stream.Flush(true);
        if (!Read().AsSpan().SequenceEqual(bytes)) throw new CatalogFault(ErrorCode.IoFailure);
    }

    internal void Rename(NativeCatalogFile parent, string name)
    {
        if (!MarkdownCatalogValidator.ValidComponent(name)) throw new CatalogFault(ErrorCode.InvalidInput);
        var bytes = Encoding.Unicode.GetBytes(name);
        var nameOffset = IntPtr.Size == 8 ? 20 : 12;
        var buffer = Marshal.AllocHGlobal(nameOffset + bytes.Length + 2);
        try
        {
            Marshal.Copy(new byte[nameOffset + bytes.Length + 2], 0, buffer, nameOffset + bytes.Length + 2);
            Marshal.WriteIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4, parent.Handle.DangerousGetHandle());
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 16 : 8, bytes.Length);
            Marshal.Copy(bytes, 0, buffer + nameOffset, bytes.Length);
            var status = NtSetInformationFile(Handle, out _, buffer, (uint)(nameOffset + bytes.Length + 2), 10);
            if (status < 0) throw new Win32Exception((int)RtlNtStatusToDosError(status));
            GC.KeepAlive(parent);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal void Delete()
    {
        var buffer = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(buffer, 1);
            if (!SetFileInformationByHandle(Handle, 4, buffer, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        stream?.Dispose();
        Handle.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeName { internal ushort Length, MaximumLength; internal IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr Root, Name;
        internal uint Attributes;
        internal IntPtr Security, Quality;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { internal IntPtr Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, IntPtr information, uint length);
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes,
        out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatus status, IntPtr information, uint length, int informationClass);
}

internal sealed class DirectoryPins : IDisposable
{
    private readonly Dictionary<string, NativeCatalogFile> handles = new(StringComparer.OrdinalIgnoreCase);
    internal NativeCatalogFile Pin(string path)
    {
        if (handles.TryGetValue(path, out var existing)) return existing;
        var parent = Directory.GetParent(path);
        if (parent is not null) Pin(parent.FullName);
        var handle = NativeCatalogFile.Open(path, true);
        handles.Add(path, handle);
        return handle;
    }
    public void Dispose()
    {
        foreach (var handle in handles.Values.Reverse()) handle.Dispose();
    }
}
