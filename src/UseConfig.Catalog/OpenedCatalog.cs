namespace UseConfig.Catalog;

public sealed class OpenedCatalog(
    string rootPath, IEnumerable<CatalogFolder> folders,
    IEnumerable<WebsiteEntry> entries, IEnumerable<Diagnostic> diagnostics, CatalogSnapshot? snapshot = null)
{
    public string RootPath { get; } = rootPath;
    public CatalogSnapshot? Snapshot { get; } = snapshot;
    public IReadOnlyList<CatalogFolder> Folders { get; } = Array.AsReadOnly(folders.ToArray());
    public IReadOnlyList<WebsiteEntry> Entries { get; } = Array.AsReadOnly(entries.ToArray());
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = Array.AsReadOnly(diagnostics.ToArray());
    public bool IsReadOnly => Diagnostics.Any(diagnostic => diagnostic.Code != ErrorCode.RecoveryCleanupFailed);
}

public interface IDescriptionEditor
{
    Task<OperationResult<OpenedCatalog>> SaveAsync(
        OpenedCatalog catalog, Guid entryId, string description, CancellationToken cancellationToken);
}
