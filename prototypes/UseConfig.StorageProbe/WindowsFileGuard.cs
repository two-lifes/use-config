using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UseConfig.StorageProbe;

internal readonly record struct FileStamp(uint Volume, ulong Id, ulong LastWrite);

internal sealed class WindowsFileGuard : IDisposable
{
    private readonly List<SafeFileHandle> directories = [];

    internal void PinAncestors(string directory)
    {
        var ancestors = new Stack<string>();
        for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
            ancestors.Push(current.FullName);
        foreach (var path in ancestors)
        {
            // Denying DELETE pins each path component, not the directory's children.
            var handle = CreateFileW(path, 0x80, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error);
            }
            directories.Add(handle);
            _ = Stamp(handle);
        }
    }

    internal static FileStamp Stamp(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new ProbeException("reparse-point");
        return new FileStamp(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            ((ulong)info.WriteHigh << 32) | info.WriteLow);
    }

    public void Dispose()
    {
        for (var i = directories.Count - 1; i >= 0; i--) directories[i].Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
}
