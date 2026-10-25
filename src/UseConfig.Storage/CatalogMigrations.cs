using System.Text;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class PreparedCatalogMigration
{
    public CatalogRevision SourceRevision { get; }
    public IReadOnlyList<CatalogDocument> Documents { get; }
    public string? BackupPath { get; }
    public bool RequiresMigration => BackupPath is not null;

    internal PreparedCatalogMigration(CatalogRevision revision, IEnumerable<CatalogDocument> documents, string? backupPath)
    {
        SourceRevision = revision;
        Documents = Array.AsReadOnly(documents.ToArray());
        BackupPath = backupPath;
    }
}

internal interface ICatalogMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    OperationResult<IReadOnlyList<CatalogDocument>> Apply(IReadOnlyList<CatalogDocument> documents);
}

internal sealed class CatalogMigrationRegistry
{
    internal static CatalogMigrationRegistry Production { get; } = new(CatalogFormat.CurrentVersion,
        new Dictionary<int, ICatalogValidator> { [1] = new MarkdownCatalogValidator() }, []);

    internal int CurrentVersion { get; }
    private readonly Dictionary<int, ICatalogValidator> validators;
    private readonly Dictionary<int, ICatalogMigration> migrations;

    internal CatalogMigrationRegistry(int currentVersion, IReadOnlyDictionary<int, ICatalogValidator> validators,
        IEnumerable<ICatalogMigration> migrations)
    {
        this.validators = new Dictionary<int, ICatalogValidator>(validators);
        this.migrations = migrations.ToDictionary(item => item.FromVersion);
        CurrentVersion = currentVersion;
        if (!this.validators.ContainsKey(currentVersion) || this.validators.Keys.Any(version => version <= 0)
            || this.migrations.Values.Any(item => item.FromVersion >= item.ToVersion || item.ToVersion != currentVersion
                || !this.validators.ContainsKey(item.FromVersion)))
            throw new ArgumentException("Invalid format registry.");
    }

    internal int Validate(IReadOnlyList<CatalogDocument> documents)
    {
        var roots = documents.Where(document => document.RelativePath.Equals("_catalog.md", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roots.Length != 1) throw new CatalogFault(ErrorCode.MalformedContent);
        var version = Version(roots[0]);
        if (!validators.TryGetValue(version, out var validator)) throw new CatalogFault(ErrorCode.UnsupportedFormat);
        if (documents.Any(document => Version(document) != version)) throw new CatalogFault(ErrorCode.MalformedContent);
        var diagnostics = validator.Validate(documents);
        if (diagnostics.Count != 0) throw new CatalogFault(diagnostics[0].Code);
        return version;
    }

    internal ICatalogMigration? Resolve(IReadOnlyList<CatalogDocument> documents)
    {
        var version = Validate(documents);
        if (version == CurrentVersion) return null;
        if (version > CurrentVersion || !migrations.TryGetValue(version, out var migration))
            throw new CatalogFault(ErrorCode.UnsupportedFormat);
        return migration;
    }

    private static int Version(CatalogDocument document)
    {
        var result = MarkdownCatalogCodec.ReadVersion(new UTF8Encoding(false, true).GetBytes(document.Text));
        return result is OperationResult<int>.Success success ? success.Value
            : throw new CatalogFault(((OperationResult<int>.Failure)result).Code);
    }
}

internal sealed class CatalogMigrationRunner(CatalogMigrationRegistry registry, Action<string>? checkpoint)
{
    internal PreparedCatalogMigration Prepare(CatalogTree tree, CatalogImage source, CatalogRevision revision,
        string? backupParent, CancellationToken cancellationToken)
    {
        var migration = registry.Resolve(source.Documents);
        if (migration is null) return new PreparedCatalogMigration(revision, source.Documents, null);
        if (backupParent is null) throw new CatalogFault(ErrorCode.InvalidInput);
        using var backup = new CatalogMigrationBackup(source, LocationPaths.Normalize(backupParent), checkpoint, cancellationToken);
        checkpoint?.Invoke("migration-backup-verified");
        cancellationToken.ThrowIfCancellationRequested();
        var result = migration.Apply(source.Documents);
        if (result is OperationResult<IReadOnlyList<CatalogDocument>>.Failure failure) throw new CatalogFault(failure.Code);
        var documents = Array.AsReadOnly(((OperationResult<IReadOnlyList<CatalogDocument>>.Success)result).Value.ToArray());
        if (registry.Validate(documents) != registry.CurrentVersion) throw new CatalogFault(ErrorCode.UnsupportedFormat);
        var after = tree.Capture();
        if (!source.SameAs(after) || after.Diagnostics.Any(item => item.Code != ErrorCode.UnsupportedFormat))
            throw new CatalogFault(ErrorCode.Conflict);
        backup.Verify();
        cancellationToken.ThrowIfCancellationRequested();
        // A prepared result is not permission to overwrite the active catalog. Future formats
        // must supply compatible safe-write/recovery support before registering a transition.
        return new PreparedCatalogMigration(revision, documents, backup.Path);
    }
}

internal sealed class CatalogMigrationBackup : IDisposable
{
    private readonly DirectoryPins pins = new();
    private readonly Dictionary<string, NativeCatalogFile> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativeCatalogFile> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly CatalogImage source;
    internal string Path { get; }

    internal CatalogMigrationBackup(CatalogImage source, string parent, Action<string>? checkpoint, CancellationToken cancellationToken)
    {
        this.source = source;
        if (LocationPaths.Overlaps(source.Root, parent)) throw new CatalogFault(ErrorCode.InvalidInput);
        var name = "Migration backup " + Guid.NewGuid().ToString("N");
        Path = System.IO.Path.Combine(parent, name);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentHandle = pins.Pin(parent);
            directories.Add("", NativeCatalogFile.Create(parentHandle, name, true));
            foreach (var directory in source.Stamps.Keys.Except(source.Files.Keys, StringComparer.OrdinalIgnoreCase).Where(path => path.Length != 0))
                directories.Add(directory, NativeCatalogFile.Create(directories[""], directory, true));
            foreach (var pair in source.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = NativeCatalogFile.Create(directories[CatalogTree.ParentPath(pair.Key)], CatalogTree.Leaf(pair.Key), false);
                files.Add(pair.Key, file);
                file.Write(pair.Value, () => checkpoint?.Invoke("migration-backup-partial"));
            }
            Verify();
        }
        catch { Dispose(); throw; }
    }

    internal void Verify()
    {
        foreach (var pair in source.Files)
            if (!files[pair.Key].Read().AsSpan().SequenceEqual(pair.Value)) throw new CatalogFault(ErrorCode.IoFailure);
        var expected = files.Keys.Concat(directories.Keys.Where(path => path.Length != 0)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories.Keys)
            foreach (var child in Directory.GetFileSystemEntries(System.IO.Path.Combine(Path, directory)))
            {
                var relative = directory.Length == 0 ? System.IO.Path.GetFileName(child) : directory + "/" + System.IO.Path.GetFileName(child);
                if (!expected.Remove(relative)) throw new CatalogFault(ErrorCode.Conflict);
            }
        if (expected.Count != 0) throw new CatalogFault(ErrorCode.Conflict);
    }

    public void Dispose()
    {
        foreach (var file in files.Values) file.Dispose();
        foreach (var directory in directories.Values.Reverse()) directory.Dispose();
        pins.Dispose();
    }
}
