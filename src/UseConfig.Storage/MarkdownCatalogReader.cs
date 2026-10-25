using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

internal static class MarkdownCatalogReader
{
    internal static OpenedCatalog Read(string root, CancellationToken cancellationToken)
    {
        var documents = new List<CatalogDocument>();
        var errors = new List<Diagnostic>();
        // Root enumeration failures are handled by the location boundary, not an empty snapshot.
        foreach (var item in Directory.GetFileSystemEntries(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetFileName(item);
            try
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    errors.Add(new Diagnostic(ErrorCode.InvalidInput, relative));
                else if ((attributes & FileAttributes.Directory) == 0) AddFile(item, relative);
                else if (!MarkdownCatalogValidator.ValidComponent(relative) || relative != relative.Trim())
                    errors.Add(new Diagnostic(ErrorCode.MalformedContent, relative));
                else ReadTopic(item, relative);
            }
            catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
            {
                errors.Add(new Diagnostic(LocationPaths.Classify(exception)!.Value, relative));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new MarkdownCatalogValidator().Load(root, documents, errors);

        void ReadTopic(string topic, string name)
        {
            var hasMetadata = false;
            foreach (var item in Directory.GetFileSystemEntries(topic))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filename = Path.GetFileName(item);
                var relative = name + "/" + filename;
                if (filename.Equals("_topic.md", StringComparison.OrdinalIgnoreCase)) hasMetadata = true;
                try
                {
                    var attributes = File.GetAttributes(item);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                        errors.Add(new Diagnostic(ErrorCode.MalformedContent, relative));
                    else AddFile(item, relative);
                }
                catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
                {
                    errors.Add(new Diagnostic(LocationPaths.Classify(exception)!.Value, relative));
                }
            }
            if (!hasMetadata) errors.Add(new Diagnostic(ErrorCode.MalformedContent, name + "/_topic.md"));
        }

        void AddFile(string file, string relative)
        {
            // Unknown files need a location diagnostic, not reading their potentially unrelated contents.
            if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new Diagnostic(ErrorCode.MalformedContent, relative));
                return;
            }
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                documents.Add(new CatalogDocument(relative, new UTF8Encoding(false, true).GetString(buffer.ToArray())));
            }
            catch (DecoderFallbackException) { errors.Add(new Diagnostic(ErrorCode.MalformedContent, relative)); }
        }
    }
}
