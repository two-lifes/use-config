using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

internal sealed class CatalogImage
{
    internal string Root { get; }
    internal Dictionary<string, byte[]> Files { get; }
    internal Dictionary<string, NativeStamp> Stamps { get; }
    internal List<Diagnostic> Diagnostics { get; }
    internal IReadOnlyList<CatalogDocument> Documents { get; }

    internal CatalogImage(string root, Dictionary<string, byte[]> files, Dictionary<string, NativeStamp> stamps,
        IEnumerable<Diagnostic> diagnostics)
    {
        Root = root;
        Files = files;
        Stamps = stamps;
        Diagnostics = diagnostics.ToList();
        var documents = new List<CatalogDocument>();
        foreach (var pair in files)
        {
            try { documents.Add(new CatalogDocument(pair.Key, new UTF8Encoding(false, true).GetString(pair.Value))); }
            catch (DecoderFallbackException) { Diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, pair.Key)); }
        }
        Documents = Array.AsReadOnly(documents.ToArray());
        Diagnostics = new MarkdownCatalogValidator().Load(root, Documents, Diagnostics).Diagnostics.ToList();
    }

    internal bool SameAs(CatalogImage other) => Root.Equals(other.Root, StringComparison.OrdinalIgnoreCase)
        && Stamps.Count == other.Stamps.Count && Stamps.All(pair => other.Stamps.TryGetValue(pair.Key, out var stamp) && stamp == pair.Value)
        && SameFiles(Files, other.Files);

    internal static bool SameFiles(IReadOnlyDictionary<string, byte[]> first, IReadOnlyDictionary<string, byte[]> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes));
}

internal sealed class CatalogTree : IDisposable
{
    private readonly DirectoryPins ancestors = new();
    internal string Root { get; }
    internal Dictionary<string, NativeCatalogFile> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, NativeCatalogFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Diagnostic> diagnostics = [];

    internal CatalogTree(string root, ChangePlan? plan, CancellationToken cancellationToken)
    {
        Root = root;
        try
        {
            var parent = Directory.GetParent(root);
            if (parent is not null) ancestors.Pin(parent.FullName);
            Directories.Add("", NativeCatalogFile.Open(root, true));
            foreach (var item in Directory.GetFileSystemEntries(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(item);
                TryRead(item, name, true);
            }
        }
        catch { Dispose(); throw; }

        void TryRead(string fullPath, string relative, bool rootLevel)
        {
            try
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new CatalogFault(ErrorCode.InvalidInput);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!rootLevel || !MarkdownCatalogValidator.ValidComponent(relative) || relative != relative.Trim())
                        throw new CatalogFault(ErrorCode.MalformedContent);
                    Directories.Add(relative, NativeCatalogFile.Open(fullPath, true, delete: plan?.ChangedDirectories.Contains(relative) == true));
                    foreach (var child in Directory.GetFileSystemEntries(fullPath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryRead(child, relative + "/" + Path.GetFileName(child), false);
                    }
                    if (!Files.ContainsKey(relative + "/_topic.md"))
                        diagnostics.Add(new Diagnostic(ErrorCode.MalformedContent, relative + "/_topic.md"));
                }
                else
                {
                    if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) throw new CatalogFault(ErrorCode.MalformedContent);
                    Files.Add(relative, NativeCatalogFile.Open(fullPath, false,
                        write: plan?.Replacements.Contains(relative) == true,
                        delete: plan?.Relocations.Contains(relative) == true));
                }
            }
            catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
            {
                if (plan is not null && LocationPaths.Classify(exception) is ErrorCode.MalformedContent or ErrorCode.InvalidInput or ErrorCode.NotFound)
                    throw new CatalogFault(ErrorCode.Conflict);
                if (plan is not null) throw;
                var code = LocationPaths.Classify(exception)!.Value;
                diagnostics.Add(new Diagnostic(code == ErrorCode.Conflict ? ErrorCode.IoFailure : code, relative));
            }
        }
    }

    internal CatalogImage Capture()
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var stamps = new Dictionary<string, NativeStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Directories) stamps.Add(pair.Key, pair.Value.Stamp());
        foreach (var pair in Files)
        {
            try { bytes.Add(pair.Key, pair.Value.Read()); stamps.Add(pair.Key, pair.Value.Stamp()); }
            catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
            {
                diagnostics.Add(new Diagnostic(LocationPaths.Classify(exception)!.Value, pair.Key));
            }
        }
        var known = Files.Keys.Concat(Directories.Keys.Where(path => path.Length != 0)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directories.Keys)
        {
            foreach (var child in Directory.GetFileSystemEntries(FullPath(directory)))
            {
                var relative = directory.Length == 0 ? Path.GetFileName(child) : directory + "/" + Path.GetFileName(child);
                if (!known.Remove(relative) && !diagnostics.Any(item => item.RelativePath == relative))
                    diagnostics.Add(new Diagnostic(ErrorCode.Conflict, relative));
            }
        }
        foreach (var missing in known) diagnostics.Add(new Diagnostic(ErrorCode.Conflict, missing));
        return new CatalogImage(Root, bytes, stamps, diagnostics);
    }

    internal string FullPath(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
    internal NativeCatalogFile Parent(string relative) => Directories[ParentPath(relative)];
    internal static string ParentPath(string relative) => relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
    internal static string Leaf(string relative) => relative[(relative.LastIndexOf('/') + 1)..];

    public void Dispose()
    {
        foreach (var file in Files.Values) file.Dispose();
        foreach (var directory in Directories.Values.Reverse()) directory.Dispose();
        ancestors.Dispose();
    }
}

internal sealed class ChangePlan
{
    internal Dictionary<string, byte[]> ResultFiles { get; }
    internal HashSet<string> ResultDirectories { get; }
    internal HashSet<string> Replacements { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> Relocations { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ChangedDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<FileChange> Changes { get; }

    internal ChangePlan(CatalogImage original, IReadOnlyList<FileChange> changes)
    {
        Changes = changes.ToArray();
        if (Changes.Count == 0) throw new CatalogFault(ErrorCode.InvalidInput);
        ResultFiles = new Dictionary<string, byte[]>(original.Files, StringComparer.OrdinalIgnoreCase);
        ResultDirectories = original.Stamps.Keys.Where(path => !original.Files.ContainsKey(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();
        foreach (var change in Changes)
        {
            var source = change switch
            {
                FileChange.ReplaceDocument replace => replace.Document.RelativePath,
                FileChange.Move move => move.Source,
                FileChange.Delete delete => delete.RelativePath,
                _ => null
            };
            if (source is null) continue;
            ValidatePath(source);
            if (sources.Any(other => Overlaps(source, other))) throw new CatalogFault(ErrorCode.InvalidInput);
            sources.Add(source);
        }
        foreach (var create in Changes.OfType<FileChange.CreateDirectory>())
        {
            var path = create.RelativePath;
            ValidatePath(path);
            if (path.Contains('/') || path != path.Trim()) throw new CatalogFault(ErrorCode.InvalidInput);
            Claim(path);
            ResultDirectories.Add(path);
        }
        foreach (var change in Changes.Where(change => change is not FileChange.CreateDirectory))
        {
            switch (change)
            {
                case FileChange.CreateDocument create:
                    ValidatePath(create.Document.RelativePath);
                    Claim(create.Document.RelativePath);
                    RequireParent(create.Document.RelativePath);
                    ResultFiles.Add(create.Document.RelativePath, Encode(create.Document.Text));
                    break;
                case FileChange.ReplaceDocument replace:
                    if (!original.Files.ContainsKey(replace.Document.RelativePath)) throw new CatalogFault(ErrorCode.Conflict);
                    Replacements.Add(replace.Document.RelativePath);
                    ResultFiles[replace.Document.RelativePath] = Encode(replace.Document.Text);
                    break;
                case FileChange.Move move:
                    ValidatePath(move.Destination);
                    if (move.Source == move.Destination) throw new CatalogFault(ErrorCode.InvalidInput);
                    if (!move.Source.Equals(move.Destination, StringComparison.OrdinalIgnoreCase)) Claim(move.Destination);
                    RequireParent(move.Destination);
                    if (original.Files.ContainsKey(move.Source))
                    {
                        Relocations.Add(move.Source);
                        var bytes = ResultFiles[move.Source];
                        ResultFiles.Remove(move.Source);
                        ResultFiles.Add(move.Destination, bytes);
                    }
                    else if (original.Stamps.ContainsKey(move.Source))
                    {
                        if (move.Destination.Contains('/') || move.Destination != move.Destination.Trim()) throw new CatalogFault(ErrorCode.InvalidInput);
                        ChangedDirectories.Add(move.Source);
                        ResultDirectories.Remove(move.Source);
                        ResultDirectories.Add(move.Destination);
                        foreach (var path in original.Files.Keys.Where(path => path.StartsWith(move.Source + "/", StringComparison.OrdinalIgnoreCase)))
                        {
                            Relocations.Add(path);
                            var bytes = ResultFiles[path];
                            ResultFiles.Remove(path);
                            ResultFiles.Add(move.Destination + path[move.Source.Length..], bytes);
                        }
                    }
                    else throw new CatalogFault(ErrorCode.Conflict);
                    break;
                case FileChange.Delete delete:
                    if (original.Files.ContainsKey(delete.RelativePath))
                    {
                        Relocations.Add(delete.RelativePath);
                        ResultFiles.Remove(delete.RelativePath);
                    }
                    else if (original.Stamps.ContainsKey(delete.RelativePath))
                    {
                        ChangedDirectories.Add(delete.RelativePath);
                        ResultDirectories.Remove(delete.RelativePath);
                        foreach (var path in original.Files.Keys.Where(path => path.StartsWith(delete.RelativePath + "/", StringComparison.OrdinalIgnoreCase)))
                        {
                            Relocations.Add(path);
                            ResultFiles.Remove(path);
                        }
                    }
                    else throw new CatalogFault(ErrorCode.Conflict);
                    break;
                default: throw new CatalogFault(ErrorCode.InvalidInput);
            }
        }
        foreach (var source in sources.Where(ChangedDirectories.Contains))
            if (claimed.Any(path => path.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase))) throw new CatalogFault(ErrorCode.InvalidInput);
        foreach (var path in ResultFiles.Keys) RequireParent(path);
        foreach (var directory in ResultDirectories.Where(path => path.Length != 0))
            if (!ResultFiles.ContainsKey(directory + "/_topic.md")) throw new CatalogFault(ErrorCode.MalformedContent);
        var documents = ResultFiles.Select(pair => new CatalogDocument(pair.Key, new UTF8Encoding(false, true).GetString(pair.Value))).ToArray();
        if (new MarkdownCatalogValidator().Validate(documents).Count != 0) throw new CatalogFault(ErrorCode.MalformedContent);

        void Claim(string path)
        {
            if (original.Stamps.ContainsKey(path) || !claimed.Add(path)) throw new CatalogFault(ErrorCode.AlreadyExists);
        }
        void RequireParent(string path)
        {
            if (!ResultDirectories.Contains(CatalogTree.ParentPath(path))) throw new CatalogFault(ErrorCode.InvalidInput);
        }
    }

    private static byte[] Encode(string text)
    {
        try { return new UTF8Encoding(false, true).GetBytes(text); }
        catch (EncoderFallbackException) { throw new CatalogFault(ErrorCode.MalformedContent); }
    }

    internal static void ValidatePath(string path)
    {
        var parts = path.Split('/');
        if (parts.Length is < 1 or > 2 || parts.Any(part => !MarkdownCatalogValidator.ValidComponent(part)))
            throw new CatalogFault(ErrorCode.InvalidInput);
    }
    private static bool Overlaps(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase)
        || first.StartsWith(second + "/", StringComparison.OrdinalIgnoreCase) || second.StartsWith(first + "/", StringComparison.OrdinalIgnoreCase);
}
