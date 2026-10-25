using UseConfig.Catalog;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS: {name}");
}

try
{
    var folderId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    var entryId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    var folder = new CatalogFolder(folderId, "Тема 日本語");
    var entry = new WebsiteEntry(entryId, folder.Id, "Пример 日本語", "HTTPS://example.com/%2f",
        description: "  # Описание\n日本語\n\n", username: "  demo\nuser  ",
        password: "  synthetic\npassword  ", additionalUrl: "https://example.org/%2F",
        twoFactorNote: "  Заметка\nsecond line  ");
    Check(folder.Id == folderId && entry.Id == entryId && entry.FolderId == folder.Id,
        "models retain explicit stable IDs and folder relationship");
    Check(CatalogFormat.CurrentVersion == 1 && folder.FormatVersion == 1 && entry.FormatVersion == 1,
        "models default to catalog format version 1");
    Check(folder.Name == "Тема 日本語" && entry.Name == "Пример 日本語"
        && entry.Url == "HTTPS://example.com/%2f" && entry.AdditionalUrl == "https://example.org/%2F",
        "models preserve Unicode names and original URL text");
    Check(entry.Description == "  # Описание\n日本語\n\n" && entry.TwoFactorNote == "  Заметка\nsecond line  "
        && entry.Username == "  demo\nuser  " && entry.Password == "  synthetic\npassword  ",
        "models preserve multiline fields and significant whitespace");
    var minimal = new WebsiteEntry(entryId, folderId, "Example", "https://example.com/");
    Check(minimal.Description == "" && minimal.Username == "" && minimal.Password == ""
        && minimal.AdditionalUrl == "" && minimal.TwoFactorNote == "",
        "optional entry fields default to empty strings");
    var renamedFolder = new CatalogFolder(folder.Id, "Renamed");
    var editedEntry = new WebsiteEntry(entry.Id, entry.FolderId, "Edited", entry.Url);
    Check(renamedFolder.Id == folder.Id && editedEntry.Id == entry.Id && editedEntry.FolderId == entry.FolderId,
        "recreating renamed models preserves supplied identities");
    Check(new CatalogFolder(folderId, "Future", formatVersion: 2).FormatVersion == 2
        && new WebsiteEntry(entryId, folderId, "Future", entry.Url, formatVersion: 2).FormatVersion == 2,
        "models retain explicit versions for later format validation");
    Check(typeof(CatalogFolder).GetProperties().All(property => property.SetMethod is null)
        && typeof(WebsiteEntry).GetProperties().All(property => property.SetMethod is null),
        "model properties are read-only");
    Check(folder.ToString() == typeof(CatalogFolder).FullName && entry.ToString() == typeof(WebsiteEntry).FullName,
        "default model string representations omit all user contents");

    var clock = new ManualDelay();
    IDelayScheduler scheduler = clock;
    var reveal = scheduler.DelayAsync(CredentialTiming.RevealDuration, CancellationToken.None);
    clock.Advance(TimeSpan.FromSeconds(29));
    Check(!reveal.IsCompleted, "reveal remains pending before deadline");
    clock.Advance(TimeSpan.FromSeconds(1));
    await reveal;
    Check(reveal.IsCompletedSuccessfully, "reveal expires at 30 seconds");

    using var previousCopy = new CancellationTokenSource();
    var oldExpiry = scheduler.DelayAsync(CredentialTiming.ClipboardDuration, previousCopy.Token);
    previousCopy.Cancel();
    var newExpiry = scheduler.DelayAsync(CredentialTiming.ClipboardDuration, CancellationToken.None);
    clock.Advance(TimeSpan.FromSeconds(59));
    Check(oldExpiry.IsCanceled && !newExpiry.IsCompleted, "repeated copy can cancel old expiry independently");
    clock.Advance(TimeSpan.FromSeconds(1));
    await newExpiry;
    Check(newExpiry.IsCompletedSuccessfully, "clipboard expiry is 60 seconds");

    IClipboardAccess clipboard = new MemoryClipboard();
    var first = (OperationResult<ClipboardOwnership>.Success)await clipboard.CopyAsync("synthetic", CancellationToken.None);
    var second = (OperationResult<ClipboardOwnership>.Success)await clipboard.CopyAsync("synthetic", CancellationToken.None);
    Check(await clipboard.ClearIfOwnedAsync(first.Value, CancellationToken.None)
        is OperationResult<ClipboardCleanup>.Success { Value: ClipboardCleanup.OwnershipLost },
        "same text with new ownership is preserved");
    Check(await clipboard.ClearIfOwnedAsync(second.Value, CancellationToken.None)
        is OperationResult<ClipboardCleanup>.Success { Value: ClipboardCleanup.Cleared }, "current ownership can clear");

    var files = new MemoryFiles();
    ICatalogFiles storage = files;
    var loaded = (OperationResult<CatalogSnapshot>.Success)await storage.ReadAsync("synthetic", CancellationToken.None);
    files.Revision = new CatalogRevision();
    Check(await storage.ApplyAsync("synthetic", loaded.Value.Revision, [], CancellationToken.None)
        is OperationResult<CatalogSnapshot>.Failure { Code: ErrorCode.Conflict }, "stale revision reports conflict");
    files.Diagnostics = [new Diagnostic(ErrorCode.MalformedContent, "topic/site.md", "name")];
    var invalid = (OperationResult<CatalogSnapshot>.Success)await storage.ReadAsync("synthetic", CancellationToken.None);
    Check(invalid.Value.IsReadOnly, "diagnostics force read-only snapshot");
    Check(await storage.ApplyAsync("synthetic", invalid.Value.Revision, [], CancellationToken.None)
        is OperationResult<CatalogSnapshot>.Failure { Code: ErrorCode.ReadOnly }, "read-only mutation reports failure");
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    try
    {
        await storage.ReadAsync("synthetic", canceled.Token);
        throw new InvalidOperationException("Cancellation was ignored");
    }
    catch (OperationCanceledException exception) when (exception.CancellationToken == canceled.Token)
    {
        Check(true, "cancellation remains distinguishable from storage errors");
    }
    Check(!new CatalogDocument("synthetic.md", "synthetic-secret").ToString()!.Contains("synthetic-secret"),
        "document default string representation omits contents");
    await LocationChecks.RunAsync(Check);
    Console.WriteLine($"Contract checks: {passed} passed, 0 failed. Includes empty-catalog and settings adapters.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Contract checks failed: {exception.GetType().Name}; {passed} passed.");
    return 1;
}

sealed class ManualDelay : IDelayScheduler
{
    private TimeSpan now;
    private readonly List<(TimeSpan Due, TaskCompletionSource Source)> pending = [];

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
        _ = source.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        pending.Add((now + delay, source));
        return source.Task;
    }

    public void Advance(TimeSpan elapsed)
    {
        now += elapsed;
        foreach (var item in pending.Where(item => item.Due <= now).ToArray())
        {
            item.Source.TrySetResult();
            pending.Remove(item);
        }
    }
}

sealed class MemoryClipboard : IClipboardAccess
{
    private ClipboardOwnership? current;

    public Task<OperationResult<ClipboardOwnership>> CopyAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        current = new ClipboardOwnership();
        return Task.FromResult<OperationResult<ClipboardOwnership>>(new OperationResult<ClipboardOwnership>.Success(current));
    }

    public Task<OperationResult<ClipboardCleanup>> ClearIfOwnedAsync(ClipboardOwnership ownership, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ReferenceEquals(current, ownership) ? ClipboardCleanup.Cleared : ClipboardCleanup.OwnershipLost;
        if (result == ClipboardCleanup.Cleared) current = null;
        return Task.FromResult<OperationResult<ClipboardCleanup>>(new OperationResult<ClipboardCleanup>.Success(result));
    }
}

sealed class MemoryFiles : ICatalogFiles
{
    public CatalogRevision Revision { get; set; } = new();
    public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = [];

    public Task<OperationResult<CatalogSnapshot>> ReadAsync(string rootPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<OperationResult<CatalogSnapshot>>(
            new OperationResult<CatalogSnapshot>.Success(new CatalogSnapshot(Revision, [], Diagnostics)));
    }

    public Task<OperationResult<CatalogSnapshot>> ApplyAsync(string rootPath, CatalogRevision expectedRevision,
        IReadOnlyList<FileChange> changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Diagnostics.Count != 0)
            return Task.FromResult<OperationResult<CatalogSnapshot>>(new OperationResult<CatalogSnapshot>.Failure(ErrorCode.ReadOnly));
        if (!ReferenceEquals(Revision, expectedRevision))
            return Task.FromResult<OperationResult<CatalogSnapshot>>(new OperationResult<CatalogSnapshot>.Failure(ErrorCode.Conflict));
        return ReadAsync(rootPath, cancellationToken);
    }
}
