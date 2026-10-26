using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class MarkdownDescriptionEditor(ICatalogFiles files) : IDescriptionEditor
{
    public async Task<OperationResult<OpenedCatalog>> SaveAsync(
        OpenedCatalog catalog, Guid entryId, string description, CancellationToken cancellationToken)
    {
        if (catalog.IsReadOnly || catalog.Snapshot is not { IsReadOnly: false } snapshot)
            return new OperationResult<OpenedCatalog>.Failure(ErrorCode.ReadOnly);
        if (!catalog.Entries.Any(entry => entry.Id == entryId))
            return new OperationResult<OpenedCatalog>.Failure(ErrorCode.NotFound);
        foreach (var document in snapshot.Documents)
        {
            if (!document.RelativePath.Contains('/') || document.RelativePath.EndsWith("/_topic.md", StringComparison.OrdinalIgnoreCase)) continue;
            var parsed = MarkdownCatalogCodec.Read(Encoding.UTF8.GetBytes(document.Text), CatalogDocumentKind.Entry);
            if (parsed is not OperationResult<ParsedCatalogDocument>.Success source
                || !Guid.TryParse(source.Value.Fields["id"], out var id) || id != entryId) continue;
            var encoded = MarkdownCatalogCodec.Write(new ParsedCatalogDocument(CatalogDocumentKind.Entry,
                source.Value.Fields.ToDictionary(pair => pair.Key, pair => pair.Value), description));
            if (encoded is OperationResult<byte[]>.Failure invalid)
                return new OperationResult<OpenedCatalog>.Failure(invalid.Code);
            var text = Encoding.UTF8.GetString(((OperationResult<byte[]>.Success)encoded).Value);
            var result = await files.ApplyAsync(catalog.RootPath, snapshot.Revision,
                [new FileChange.ReplaceDocument(new CatalogDocument(document.RelativePath, text))], cancellationToken);
            if (result is OperationResult<CatalogSnapshot>.Failure failure)
                return new OperationResult<OpenedCatalog>.Failure(failure.Code);
            var updated = ((OperationResult<CatalogSnapshot>.Success)result).Value;
            var loaded = new MarkdownCatalogValidator().Load(catalog.RootPath, updated.Documents, updated.Diagnostics);
            return new OperationResult<OpenedCatalog>.Success(new OpenedCatalog(catalog.RootPath,
                loaded.Folders, loaded.Entries, loaded.Diagnostics, updated));
        }
        return new OperationResult<OpenedCatalog>.Failure(ErrorCode.NotFound);
    }
}
