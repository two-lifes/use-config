using System.Text;
using System.Text.RegularExpressions;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class MarkdownCatalogValidator : ICatalogValidator
{
    public IReadOnlyList<Diagnostic> Validate(IReadOnlyList<CatalogDocument> documents) =>
        Load("", documents, []).Diagnostics;

    internal OpenedCatalog Load(string rootPath, IReadOnlyList<CatalogDocument> documents, IEnumerable<Diagnostic> fileErrors)
    {
        var diagnostics = fileErrors.ToList();
        var parsed = new Dictionary<string, ParsedCatalogDocument>(StringComparer.OrdinalIgnoreCase);
        var duplicatePaths = documents.GroupBy(document => document.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() != 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            var path = document.RelativePath;
            var parts = path.Split('/');
            if (duplicatePaths.Contains(path) || parts.Length is < 1 or > 2 || parts.Any(part => !ValidComponent(part)))
            {
                diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, path));
                continue;
            }
            CatalogDocumentKind kind;
            if (parts.Length == 1 && parts[0].Equals("_catalog.md", StringComparison.OrdinalIgnoreCase))
                kind = CatalogDocumentKind.Catalog;
            else if (parts.Length == 2 && parts[0] == parts[0].Trim()
                && parts[1].Equals("_topic.md", StringComparison.OrdinalIgnoreCase)) kind = CatalogDocumentKind.Topic;
            else if (parts.Length == 2 && parts[0] == parts[0].Trim()
                && parts[1].EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && !parts[1].Equals("_catalog.md", StringComparison.OrdinalIgnoreCase)) kind = CatalogDocumentKind.Entry;
            else
            {
                diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, path));
                continue;
            }
            OperationResult<ParsedCatalogDocument> result;
            try { result = MarkdownCatalogCodec.Read(new UTF8Encoding(false, true).GetBytes(document.Text), kind); }
            catch (EncoderFallbackException) { result = new OperationResult<ParsedCatalogDocument>.Failure(ErrorCode.MalformedContent); }
            if (result is OperationResult<ParsedCatalogDocument>.Failure failure)
                diagnostics.Add(new Diagnostic(failure.Code, path));
            else parsed.Add(path, ((OperationResult<ParsedCatalogDocument>.Success)result).Value);
        }
        if (!parsed.ContainsKey("_catalog.md"))
        {
            if (!diagnostics.Any(item => string.Equals(item.RelativePath, "_catalog.md", StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, "_catalog.md"));
            return new OpenedCatalog(rootPath, [], [], diagnostics);
        }
        var duplicateIds = parsed.Where(pair => pair.Value.Kind != CatalogDocumentKind.Catalog)
            .GroupBy(pair => pair.Value.Fields["id"], StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(pair => pair.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in duplicateIds) diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, path, "id"));
        var folders = new Dictionary<string, CatalogFolder>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in parsed.Where(pair => pair.Value.Kind == CatalogDocumentKind.Topic && !duplicateIds.Contains(pair.Key)))
        {
            var name = pair.Key.Split('/')[0];
            folders.Add(name, new CatalogFolder(Guid.Parse(pair.Value.Fields["id"]), name));
        }
        var entries = new List<WebsiteEntry>();
        foreach (var pair in parsed.Where(pair => pair.Value.Kind == CatalogDocumentKind.Entry && !duplicateIds.Contains(pair.Key)))
        {
            var name = pair.Key.Split('/')[0];
            if (!folders.TryGetValue(name, out var folder))
            {
                var metadata = name + "/_topic.md";
                if (!diagnostics.Any(item => string.Equals(item.RelativePath, metadata, StringComparison.OrdinalIgnoreCase)))
                    diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, metadata));
                continue;
            }
            var fields = pair.Value.Fields;
            entries.Add(new WebsiteEntry(Guid.Parse(fields["id"]), folder.Id, fields["name"], fields["url"],
                pair.Value.Description, fields["username"], fields["password"], fields["additional_url"], fields["two_factor_note"]));
        }
        return new OpenedCatalog(rootPath, folders.Values, entries, diagnostics);
    }

    internal static bool ValidComponent(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) return false;
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !Regex.IsMatch(stem, "\\A(COM|LPT)[1-9¹²³]\\z");
    }
}
