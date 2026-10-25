namespace UseConfig.Catalog;

// Tokens are opaque, adapter-issued and scoped to one loaded catalog/archive.
public sealed class CatalogRevision;
public sealed class ValidatedArchive;

public sealed class CatalogDocument(string relativePath, string text)
{
    public string RelativePath { get; } = relativePath;
    public string Text { get; } = text;
}

public sealed record CatalogSnapshot(
    CatalogRevision Revision,
    IReadOnlyList<CatalogDocument> Documents,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool IsReadOnly => Diagnostics.Count != 0;
}

public abstract record FileChange
{
    private FileChange() { }

    public sealed record CreateDirectory(string RelativePath) : FileChange;
    public sealed record CreateDocument(CatalogDocument Document) : FileChange;
    public sealed record ReplaceDocument(CatalogDocument Document) : FileChange;
    public sealed record Move(string Source, string Destination) : FileChange;
    public sealed record Delete(string RelativePath) : FileChange;
}

public interface ICatalogFiles
{
    Task<OperationResult<CatalogSnapshot>> ReadAsync(string rootPath, CancellationToken cancellationToken);
    Task<OperationResult<CatalogSnapshot>> ApplyAsync(
        string rootPath, CatalogRevision expectedRevision,
        IReadOnlyList<FileChange> changes, CancellationToken cancellationToken);
}

public interface ICatalogValidator
{
    IReadOnlyList<Diagnostic> Validate(IReadOnlyList<CatalogDocument> documents);
}

public interface ICatalogArchives
{
    Task<OperationResult<Completion>> ExportAsync(
        string rootPath, CatalogRevision expectedRevision, string archivePath, CancellationToken cancellationToken);
    Task<OperationResult<ValidatedArchive>> ValidateAsync(string archivePath, CancellationToken cancellationToken);
    Task<OperationResult<CatalogSnapshot>> RestoreAsync(
        string rootPath, CatalogRevision expectedRevision, ValidatedArchive archive,
        string priorBackupPath, CancellationToken cancellationToken);
}
