using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class MarkdownCatalogLocation(string settingsDirectory, MarkdownCatalogFiles? catalogFiles = null) : ICatalogLocation
{
    private static readonly string Metadata = System.FormattableString.Invariant($"---\nformat_version: {CatalogFormat.CurrentVersion}\n---\n");
    private readonly string settingsPath = LocationPaths.Normalize(settingsDirectory);
    private readonly MarkdownCatalogFiles files = catalogFiles ?? new();

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

            var result = files.ReadAsync(root, cancellationToken).GetAwaiter().GetResult();
            if (result is OperationResult<CatalogSnapshot>.Failure failure) return Fail(failure.Code);
            var snapshot = ((OperationResult<CatalogSnapshot>.Success)result).Value;
            if (!snapshot.IsReadOnly)
            {
                var preparation = files.PrepareMigrationAsync(root, snapshot.Revision, null, cancellationToken).GetAwaiter().GetResult();
                if (preparation is OperationResult<PreparedCatalogMigration>.Failure preparationFailure)
                    return Fail(preparationFailure.Code);
            }
            var loaded = new MarkdownCatalogValidator().Load(root, snapshot.Documents, snapshot.Diagnostics);
            return new OperationResult<OpenedCatalog>.Success(new OpenedCatalog(root, loaded.Folders, loaded.Entries, loaded.Diagnostics.Distinct()));
        }
        catch (DecoderFallbackException) { return Fail(ErrorCode.MalformedContent); }
        catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
        {
            return Fail(LocationPaths.Classify(exception)!.Value);
        }
    }

    private static OperationResult<OpenedCatalog> Fail(ErrorCode code) => new OperationResult<OpenedCatalog>.Failure(code);
}
