using System.Text;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class LocationChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        // Retain synthetic fixtures under ignored build output for inspecting failed checks.
        var root = Path.Combine(AppContext.BaseDirectory, "location-checks", Guid.NewGuid().ToString("N"));
        var settingsDirectory = Path.Combine(root, "settings");
        var catalog = Path.Combine(root, "Каталог 日本語");
        Directory.CreateDirectory(catalog);
        var location = new MarkdownCatalogLocation(settingsDirectory);
        var settings = new CatalogSettings(settingsDirectory);
        var session = new CatalogSession(location, settings);
        var token = CancellationToken.None;
        check(await session.RestoreAsync(token) is OperationResult<Completion>.Success && session.CurrentPath is null,
            "missing settings require selection");
        check(await session.OpenAsync(catalog, true, true, token) is OperationResult<Completion>.Success
            && session.CurrentPath == catalog && session.PersistenceError is null, "create and remember Unicode catalog");
        var metadataPath = Path.Combine(catalog, "_catalog.md");
        var original = File.ReadAllBytes(metadataPath);
        check(Encoding.UTF8.GetString(original) == "---\nformat_version: 1\n---\n", "creation emits canonical metadata");
        var reopened = new CatalogSession(location, settings);
        check(await reopened.RestoreAsync(token) is OperationResult<Completion>.Success && reopened.CurrentPath == catalog,
            "new session reopens remembered catalog");
        check(await location.OpenAsync(catalog, true, token) is OperationResult<OpenedCatalog>.Failure { Code: ErrorCode.AlreadyExists }
            && original.SequenceEqual(File.ReadAllBytes(metadataPath)), "creation never overwrites metadata");
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var originalSettings = File.ReadAllBytes(settingsPath);
        check(await session.OpenAsync(Path.Combine(root, "missing"), false, true, token)
            is OperationResult<Completion>.Failure { Code: ErrorCode.NotFound }
            && originalSettings.SequenceEqual(File.ReadAllBytes(settingsPath)) && session.CurrentPath == catalog,
            "failed selection retains active catalog and remembered setting");
        foreach (var overlap in new[] { root, settingsDirectory, Path.Combine(settingsDirectory, "child") })
            check(await location.OpenAsync(overlap, true, token) is OperationResult<OpenedCatalog>.Failure { Code: ErrorCode.InvalidInput },
                "settings ancestor, directory and descendant are excluded");
        File.WriteAllText(Path.Combine(catalog, "other.md"), "synthetic");
        check(HasDiagnostic(await location.OpenAsync(catalog, false, token), ErrorCode.MalformedContent),
            "unexpected root file makes catalog read-only");
        var empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        check(HasDiagnostic(await location.OpenAsync(empty, false, token), ErrorCode.MalformedContent)
            && !Directory.EnumerateFileSystemEntries(empty).Any(), "opening does not initialize missing metadata");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await location.OpenAsync(empty, true, canceled.Token);
            check(false, "canceled creation must throw");
        }
        catch (OperationCanceledException)
        {
            check(!Directory.EnumerateFileSystemEntries(empty).Any(), "canceled creation leaves directory unchanged");
        }
        check(await location.OpenAsync(empty, true, token) is OperationResult<OpenedCatalog>.Success, "second empty catalog created");
        var emptyMetadata = Path.Combine(empty, "_catalog.md");
        foreach (var valid in new[] { "---\nformat_version: 1\n---", "\uFEFF---\r\nformat_version: 1\r\n---\r\n" })
        {
            File.WriteAllText(emptyMetadata, valid, new UTF8Encoding(false));
            check(await location.OpenAsync(empty, false, token) is OperationResult<OpenedCatalog>.Success { Value.IsReadOnly: false }, "supported metadata envelope accepted");
        }
        foreach (var invalid in new[] { "---\nformat_version: 1\n---\nbody", "---\nformat_version: 1\nformat_version: 1\n---", "---\rformat_version: 1\r---" })
        {
            File.WriteAllText(emptyMetadata, invalid);
            check(HasDiagnostic(await location.OpenAsync(empty, false, token), ErrorCode.MalformedContent)
                && File.ReadAllText(emptyMetadata) == invalid, "invalid metadata preserved");
        }
        File.WriteAllBytes(emptyMetadata, [0xff, 0xfe]);
        check(HasDiagnostic(await location.OpenAsync(empty, false, token), ErrorCode.MalformedContent),
            "invalid UTF8 rejected");
        File.WriteAllText(emptyMetadata, "---\nformat_version: 2\n---\n");
        check(HasDiagnostic(await location.OpenAsync(empty, false, token), ErrorCode.UnsupportedFormat),
            "future catalog version rejected");
        File.WriteAllBytes(emptyMetadata, original);
        using (var lockedMetadata = new FileStream(emptyMetadata, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(HasDiagnostic(await location.OpenAsync(empty, false, token), ErrorCode.IoFailure),
                "inaccessible metadata reports an IO failure without replacement");
        foreach (var badSettings in new[] { "not JSON", "{\"settings_version\":2,\"catalog_path\":null}" })
        {
            File.WriteAllText(settingsPath, badSettings);
            check(await settings.ReadAsync(token) is OperationResult<string?>.Failure
                && File.ReadAllText(settingsPath) == badSettings, "bad settings preserved on read");
            check(await settings.RememberAsync(empty, token) is OperationResult<Completion>.Success
                && Directory.GetFiles(settingsDirectory, "*.bak").Any(file => File.ReadAllText(file) == badSettings),
                "explicit replacement backs up malformed or future settings");
        }
        var beforeFailure = File.ReadAllBytes(settingsPath);
        using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            check(await settings.RememberAsync(catalog, token) is OperationResult<Completion>.Failure,
                "locked settings report persistence failure");
            check(beforeFailure.SequenceEqual(File.ReadAllBytes(settingsPath)), "failed replacement retains previous settings");
            var failedPersistence = new CatalogSession(location, settings);
            check(await failedPersistence.OpenAsync(empty, false, true, token) is OperationResult<Completion>.Success
                && failedPersistence.CurrentPath == empty && failedPersistence.PersistenceError is not null,
                "settings failure leaves catalog open with separate diagnostic");
        }
        var missing = Path.Combine(root, "no-longer-available");
        await settings.RememberAsync(missing, token);
        var missingSession = new CatalogSession(location, settings);
        check(await missingSession.RestoreAsync(token) is OperationResult<Completion>.Failure { Code: ErrorCode.NotFound }
            && missingSession.RememberedPath == missing && !Directory.Exists(missing),
            "missing remembered location is retained without creating a substitute");
    }

    private static bool HasDiagnostic(OperationResult<OpenedCatalog> result, ErrorCode code) =>
        result is OperationResult<OpenedCatalog>.Success success && success.Value.IsReadOnly
        && success.Value.Diagnostics.Any(diagnostic => diagnostic.Code == code);
}
