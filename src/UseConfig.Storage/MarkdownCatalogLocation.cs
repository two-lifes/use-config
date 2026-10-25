using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class MarkdownCatalogLocation(string settingsDirectory) : ICatalogLocation
{
    private const string Metadata = "---\nformat_version: 1\n---\n";
    private readonly string settingsPath = LocationPaths.Normalize(settingsDirectory);

    public Task<OperationResult<OpenedCatalog>> OpenAsync(string path, bool create, CancellationToken cancellationToken) =>
        Task.Run(() => Open(path, create, cancellationToken), cancellationToken);

    private OperationResult<OpenedCatalog> Open(string path, bool create, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = LocationPaths.Normalize(path);
            if (LocationPaths.Overlaps(root, settingsPath)) return Fail(ErrorCode.InvalidInput);
            LocationPaths.RejectLinks(root);
            var metadataPath = Path.Combine(root, "_catalog.md");
            if (create)
            {
                if (Directory.EnumerateFileSystemEntries(root).Any()) return Fail(ErrorCode.AlreadyExists);
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

            return new OperationResult<OpenedCatalog>.Success(MarkdownCatalogReader.Read(root, cancellationToken));
        }
        catch (DecoderFallbackException) { return Fail(ErrorCode.MalformedContent); }
        catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
        {
            return Fail(LocationPaths.Classify(exception)!.Value);
        }
    }

    private static OperationResult<OpenedCatalog> Fail(ErrorCode code) => new OperationResult<OpenedCatalog>.Failure(code);
}
