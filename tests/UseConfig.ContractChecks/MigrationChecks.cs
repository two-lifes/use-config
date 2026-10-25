using System.Text;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class MigrationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var token = CancellationToken.None;
        var fixture = CreateFixture();
        var original = Bytes(fixture.Root);
        var files = new MarkdownCatalogFiles();
        var snapshot = await Read(files, fixture.Root);
        var noOp = await files.PrepareMigrationAsync(fixture.Root, snapshot.Revision, null, token);
        check(noOp is OperationResult<PreparedCatalogMigration>.Success { Value.RequiresMigration: false }
            && Equal(original, Bytes(fixture.Root)) && !Directory.EnumerateFileSystemEntries(fixture.Backups).Any(),
            "migration current v1 is validated without backup or source writes");
        check(CatalogFormat.SupportedVersions is not HashSet<int>
            && CatalogFormat.SupportedVersions is ICollection<int> { IsReadOnly: true },
            "supported format collection cannot be mutated");

        foreach (var version in new[] { "0", "2", "999", "999999999999999999999999999" })
        {
            var unsupported = CreateFixture();
            File.WriteAllText(Path.Combine(unsupported.Root, "_catalog.md"), Metadata(version));
            var before = Bytes(unsupported.Root);
            var adapter = new MarkdownCatalogFiles();
            var loaded = await Read(adapter, unsupported.Root);
            var result = await adapter.PrepareMigrationAsync(unsupported.Root, loaded.Revision, unsupported.Backups, token);
            var opened = await new MarkdownCatalogLocation(unsupported.Settings, adapter).OpenAsync(unsupported.Root, false, token);
            check(result is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.UnsupportedFormat }
                && opened is OperationResult<OpenedCatalog>.Success { Value.IsReadOnly: true }
                && Equal(before, Bytes(unsupported.Root)) && !Directory.EnumerateFileSystemEntries(unsupported.Backups).Any(),
                "migration rejects unknown version " + version + " without backup or source writes");
        }

        var events = new List<string>();
        var migration = new FixtureMigration(documents =>
        {
            events.Add("migration");
            return Converted(documents);
        });
        var registry = Registry(migration);
        var adapterWithMigration = new MarkdownCatalogFiles(registry, point =>
        {
            if (point == "migration-backup-verified") events.Add("backup");
        });
        var success = await Prepare(adapterWithMigration, fixture);
        check(success is OperationResult<PreparedCatalogMigration>.Success { Value.RequiresMigration: true }
            && events.SequenceEqual(["backup", "migration"]), "verified filesystem backup precedes transformation");
        var prepared = ((OperationResult<PreparedCatalogMigration>.Success)success).Value;
        check(Equal(original, Bytes(prepared.BackupPath!)) && Equal(original, Bytes(fixture.Root)),
            "migration backup preserves BOM CRLF Unicode multiline fields and empty topics byte for byte");
        check(registry.Validate(prepared.Documents) == 2 && prepared.SourceRevision is not null
            && !prepared.ToString()!.Contains("synthetic"), "prepared migration validates target and omits contents in diagnostics");

        var disconnected = new MarkdownCatalogFiles(Registry());
        var missingTransition = await Prepare(disconnected, fixture);
        check(missingTransition is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.UnsupportedFormat },
            "missing registered transition does not claim success");

        foreach (var fault in new[] { ErrorCode.AccessDenied, ErrorCode.InsufficientSpace, ErrorCode.IoFailure })
        {
            var failed = CreateFixture();
            var source = Bytes(failed.Root);
            var calls = 0;
            var failing = new MarkdownCatalogFiles(Registry(new FixtureMigration(documents =>
            {
                calls++;
                return Converted(documents);
            })), point =>
            {
                if (point == "migration-backup-partial") throw new CatalogFault(fault);
            });
            var result = await Prepare(failing, failed);
            check(result is OperationResult<PreparedCatalogMigration>.Failure failure && failure.Code == fault
                && calls == 0 && Equal(source, Bytes(failed.Root)), "partial backup " + fault + " blocks transformation and preserves source");
        }

        foreach (var mode in new[] { "returned-error", "exception", "wrong-version", "invalid-schema" })
        {
            var failed = CreateFixture();
            var source = Bytes(failed.Root);
            var failing = new MarkdownCatalogFiles(Registry(new FixtureMigration(documents => mode switch
            {
                "returned-error" => new OperationResult<IReadOnlyList<CatalogDocument>>.Failure(ErrorCode.IoFailure),
                "exception" => throw new IOException("synthetic-secret"),
                "wrong-version" => new OperationResult<IReadOnlyList<CatalogDocument>>.Success(documents),
                _ => new OperationResult<IReadOnlyList<CatalogDocument>>.Success([new CatalogDocument("_catalog.md", Metadata("2") + "invalid")])
            })));
            var result = await Prepare(failing, failed);
            var backup = Directory.GetDirectories(failed.Backups).Single();
            check(result is OperationResult<PreparedCatalogMigration>.Failure
                && !result.ToString()!.Contains("synthetic-secret")
                && Equal(source, Bytes(failed.Root)) && Equal(source, Bytes(backup)),
                "migration " + mode + " retains original and verified backup with categorized diagnostics");
        }

        var stale = await Read(adapterWithMigration, fixture.Root);
        File.AppendAllText(Path.Combine(fixture.Root, "Тема", "site.md"), "external");
        check(await adapterWithMigration.PrepareMigrationAsync(fixture.Root, stale.Revision, fixture.Backups, token)
            is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.Conflict },
            "migration refuses externally changed revision");
        check(await files.PrepareMigrationAsync(fixture.Root, new CatalogRevision(), fixture.Backups, token)
            is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.Conflict },
            "migration refuses unknown revision");

        var blocked = CreateFixture();
        using (var handle = new FileStream(Path.Combine(blocked.Root, "_catalog.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var blockedSnapshot = await Read(files, blocked.Root);
            check(await files.PrepareMigrationAsync(blocked.Root, blockedSnapshot.Revision, blocked.Backups, token)
                is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.ReadOnly },
                "migration refuses inaccessible source instead of backing up partial catalog");
        }

        var corrupt = CreateFixture();
        File.AppendAllText(Path.Combine(corrupt.Root, "_catalog.md"), "invalid");
        check(await Prepare(adapterWithMigration, corrupt) is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.ReadOnly },
            "migration rejects malformed source before backup");

        var overlap = CreateFixture();
        var overlapSnapshot = await Read(adapterWithMigration, overlap.Root);
        check(await adapterWithMigration.PrepareMigrationAsync(overlap.Root, overlapSnapshot.Revision, overlap.Root, token)
            is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.InvalidInput },
            "migration forbids backup overlapping active catalog");
        check(await adapterWithMigration.PrepareMigrationAsync(overlap.Root, overlapSnapshot.Revision, Path.Combine(overlap.Backups, "missing"), token)
            is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.NotFound },
            "missing backup destination blocks migration");

        var canceled = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        var canceledAdapter = new MarkdownCatalogFiles(registry, point =>
        {
            if (point == "migration-backup-verified") cancellation.Cancel();
        });
        var canceledSnapshot = await Read(canceledAdapter, canceled.Root);
        try
        {
            await canceledAdapter.PrepareMigrationAsync(canceled.Root, canceledSnapshot.Revision, canceled.Backups, cancellation.Token);
            throw new InvalidOperationException("Cancellation ignored");
        }
        catch (OperationCanceledException)
        {
            check(Equal(Bytes(canceled.Root), Bytes(Directory.GetDirectories(canceled.Backups).Single())),
                "cancellation after backup retains original and complete copy");
        }

        var concurrent = CreateFixture();
        var concurrentOriginal = Bytes(concurrent.Root);
        var concurrentAdapter = new MarkdownCatalogFiles(registry, point =>
        {
            if (point == "migration-backup-verified") File.WriteAllText(Path.Combine(concurrent.Root, "external.md"), "external");
        });
        check(await Prepare(concurrentAdapter, concurrent) is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.Conflict }
            && File.ReadAllText(Path.Combine(concurrent.Root, "external.md")) == "external"
            && Equal(concurrentOriginal, Bytes(Directory.GetDirectories(concurrent.Backups).Single())),
            "new file during preparation conflicts and retains external edit and original backup");

        var damagedBackup = CreateFixture();
        var damagedAdapter = new MarkdownCatalogFiles(registry, point =>
        {
            if (point == "migration-backup-verified")
                File.WriteAllText(Path.Combine(Directory.GetDirectories(damagedBackup.Backups).Single(), "unexpected.md"), "external");
        });
        check(await Prepare(damagedAdapter, damagedBackup) is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.Conflict },
            "backup is reverified after transformation and unexpected files block success");

        var recovering = CreateFixture();
        var failingWriter = new MarkdownCatalogFiles(point =>
        {
            if (point is "partial" or "rollback") throw new IOException("synthetic");
        });
        var beforeWrite = await Read(failingWriter, recovering.Root);
        await failingWriter.ApplyAsync(recovering.Root, beforeWrite.Revision,
            [new FileChange.ReplaceDocument(new CatalogDocument("_catalog.md", Metadata("1").Replace("\n", "\r\n")))], token);
        var recoverySnapshot = await Read(files, recovering.Root);
        check(await files.PrepareMigrationAsync(recovering.Root, recoverySnapshot.Revision, recovering.Backups, token)
            is OperationResult<PreparedCatalogMigration>.Failure { Code: ErrorCode.RecoveryRequired },
            "pending safe-write recovery blocks migration preparation");
    }

    // Version 2 exists only in this isolated test registry, never in the application registry.
    private static CatalogMigrationRegistry Registry(params ICatalogMigration[] migrations) => new(2,
        new Dictionary<int, ICatalogValidator> { [1] = new MarkdownCatalogValidator(), [2] = new FixtureV2Validator() }, migrations);

    private static OperationResult<IReadOnlyList<CatalogDocument>> Converted(IReadOnlyList<CatalogDocument> documents) =>
        new OperationResult<IReadOnlyList<CatalogDocument>>.Success(documents.Select(document =>
            new CatalogDocument(document.RelativePath, document.Text.Replace("format_version: 1", "format_version: 2"))).ToArray());

    private sealed class FixtureV2Validator : ICatalogValidator
    {
        public IReadOnlyList<Diagnostic> Validate(IReadOnlyList<CatalogDocument> documents) =>
            new MarkdownCatalogValidator().Validate(documents.Select(document =>
                new CatalogDocument(document.RelativePath, document.Text.Replace("format_version: 2", "format_version: 1"))).ToArray());
    }

    private sealed class FixtureMigration(Func<IReadOnlyList<CatalogDocument>, OperationResult<IReadOnlyList<CatalogDocument>>> transform) : ICatalogMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;
        public OperationResult<IReadOnlyList<CatalogDocument>> Apply(IReadOnlyList<CatalogDocument> documents) => transform(documents);
    }

    private static string Metadata(string version) => "---\nformat_version: " + version + "\n---\n";

    private static (string Root, string Backups, string Settings) CreateFixture()
    {
        var parent = Path.Combine(AppContext.BaseDirectory, "migration-checks", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "catalog");
        var backups = Path.Combine(parent, "backups");
        Directory.CreateDirectory(Path.Combine(root, "Тема"));
        Directory.CreateDirectory(Path.Combine(root, "Empty"));
        Directory.CreateDirectory(backups);
        File.WriteAllText(Path.Combine(root, "_catalog.md"), Metadata("1"), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root, "Тема", "_topic.md"), "---\r\nformat_version: 1\r\nid: \"22222222-2222-4222-8222-222222222222\"\r\n---\r\n");
        File.WriteAllText(Path.Combine(root, "Empty", "_topic.md"), "---\nformat_version: 1\nid: \"44444444-4444-4444-8444-444444444444\"\n---\n");
        File.WriteAllText(Path.Combine(root, "Тема", "site.md"), MarkdownCodecChecks.Full);
        return (root, backups, Path.Combine(parent, "settings"));
    }

    private static Dictionary<string, byte[]> Bytes(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static bool Equal(Dictionary<string, byte[]> first, Dictionary<string, byte[]> second) =>
        first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var bytes) && pair.Value.SequenceEqual(bytes));

    private static async Task<CatalogSnapshot> Read(MarkdownCatalogFiles files, string root) =>
        ((OperationResult<CatalogSnapshot>.Success)await files.ReadAsync(root, CancellationToken.None)).Value;

    private static async Task<OperationResult<PreparedCatalogMigration>> Prepare(MarkdownCatalogFiles files,
        (string Root, string Backups, string Settings) fixture)
    {
        var snapshot = await Read(files, fixture.Root);
        return await files.PrepareMigrationAsync(fixture.Root, snapshot.Revision, fixture.Backups, CancellationToken.None);
    }
}
