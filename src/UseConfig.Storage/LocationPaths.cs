using UseConfig.Catalog;

namespace UseConfig.Storage;

internal static class LocationPaths
{
    public static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute path required.");
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var relative = normalized[Path.GetPathRoot(normalized)!.Length..];
        if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(component => component.EndsWith(' ') || component.EndsWith('.')))
            throw new ArgumentException("Ambiguous Windows path component.");
        return normalized;
    }

    public static bool Overlaps(string first, string second) =>
        Contains(first, second) || Contains(second, first);

    private static bool Contains(string parent, string child) =>
        parent.Equals(child, StringComparison.OrdinalIgnoreCase)
        || child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public static void RejectLinks(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Linked directories are not supported.");
        }
    }

    public static ErrorCode? Classify(Exception exception) => exception switch
    {
        CatalogFault fault => fault.Code,
        System.ComponentModel.Win32Exception native => native.NativeErrorCode switch
        {
            2 or 3 => ErrorCode.NotFound,
            5 => ErrorCode.AccessDenied,
            32 or 33 => ErrorCode.Conflict,
            80 or 183 => ErrorCode.AlreadyExists,
            39 or 112 => ErrorCode.InsufficientSpace,
            87 or 123 or 206 => ErrorCode.InvalidInput,
            _ => ErrorCode.IoFailure
        },
        UnauthorizedAccessException => ErrorCode.AccessDenied,
        FileNotFoundException or DirectoryNotFoundException => ErrorCode.NotFound,
        PathTooLongException or ArgumentException or NotSupportedException => ErrorCode.InvalidInput,
        IOException io when (io.HResult & 0xffff) is 80 or 183 => ErrorCode.AlreadyExists,
        IOException io when (io.HResult & 0xffff) is 39 or 112 => ErrorCode.InsufficientSpace,
        IOException => ErrorCode.IoFailure,
        _ => null
    };
}
