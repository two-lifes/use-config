using System.Text;
using System.Text.RegularExpressions;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class EmptyCatalogLocation(string settingsDirectory) : ICatalogLocation
{
    private const string Metadata = "---\nformat_version: 1\n---\n";
    private readonly string settingsPath = LocationPaths.Normalize(settingsDirectory);

    public Task<OperationResult<string>> OpenAsync(string path, bool create, CancellationToken cancellationToken) =>
        Task.Run(() => Open(path, create, cancellationToken), cancellationToken);

    private OperationResult<string> Open(string path, bool create, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = LocationPaths.Normalize(path);
            if (LocationPaths.Overlaps(root, settingsPath)) return Fail(ErrorCode.InvalidInput);
            LocationPaths.RejectLinks(root);
            var items = Directory.GetFileSystemEntries(root);
            var metadataPath = Path.Combine(root, "_catalog.md");
            if (create)
            {
                if (items.Length != 0) return Fail(ErrorCode.AlreadyExists);
                var parent = Directory.GetParent(root);
                if (parent is null) return Fail(ErrorCode.InvalidInput);
                // Stage outside the catalog so an interrupted write cannot leave partial metadata.
                var staging = Path.Combine(parent.FullName, $".useconfig-{Guid.NewGuid():N}.tmp");
                cancellationToken.ThrowIfCancellationRequested();
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(Encoding.UTF8.GetBytes(Metadata));
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                LocationPaths.RejectLinks(root);
                if (Directory.EnumerateFileSystemEntries(root).Any()) return Fail(ErrorCode.Conflict);
                File.Move(staging, metadataPath, false);
                // Do not report cancellation after the metadata has been committed.
                return Open(root, false, CancellationToken.None);
            }

            foreach (var item in items)
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    return Fail(ErrorCode.InvalidInput);
            if (items.Any(item => !Path.GetFileName(item).Equals("_catalog.md", StringComparison.OrdinalIgnoreCase)))
                return Fail(ErrorCode.CatalogContentsNotYetSupported);
            if (items.Length != 1 || (File.GetAttributes(metadataPath) & FileAttributes.Directory) != 0)
                return Fail(ErrorCode.MalformedContent);
            var bytes = File.ReadAllBytes(metadataPath);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.StartsWith('\uFEFF')) text = text[1..];
            text = text.Replace("\r\n", "\n");
            var match = Regex.Match(text, "\\A---\\nformat_version: ([0-9]+)\\n---\\n?\\z", RegexOptions.CultureInvariant);
            if (!match.Success) return Fail(ErrorCode.MalformedContent);
            if (match.Groups[1].Value != "1") return Fail(ErrorCode.UnsupportedFormat);
            cancellationToken.ThrowIfCancellationRequested();
            return new OperationResult<string>.Success(root);
        }
        catch (DecoderFallbackException) { return Fail(ErrorCode.MalformedContent); }
        catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
        {
            return Fail(LocationPaths.Classify(exception)!.Value);
        }
    }

    private static OperationResult<string> Fail(ErrorCode code) => new OperationResult<string>.Failure(code);
}
