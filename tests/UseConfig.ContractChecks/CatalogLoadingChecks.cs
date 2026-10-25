using UseConfig.Catalog;
using UseConfig.Storage;

internal static class CatalogLoadingChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "catalog-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = Path.Combine(root, "catalog");
        Directory.CreateDirectory(catalog);
        var settings = new CatalogSettings(Path.Combine(root, "settings"));
        var location = new MarkdownCatalogLocation(Path.Combine(root, "settings"));
        var token = CancellationToken.None;
        await location.OpenAsync(catalog, true, token);
        var topic = Path.Combine(catalog, "Тема 日本語");
        Directory.CreateDirectory(topic);
        const string topicId = "22222222-2222-4222-8222-222222222222";
        var topicText = "---\nformat_version: 1\nid: \"" + topicId + "\"\n---\n";
        File.WriteAllText(Path.Combine(topic, "_topic.md"), topicText);
        File.WriteAllText(Path.Combine(topic, "manual.MD"), MarkdownCodecChecks.Full);
        var loaded = await Load();
        check(!loaded.IsReadOnly && loaded.Folders.Count == 1 && loaded.Entries.Count == 1,
            "populated Unicode catalog loads models");
        check(loaded.Entries[0].FolderId == loaded.Folders[0].Id
            && loaded.Entries[0].Password == "  demo:\"quote\"\\path#123  "
            && loaded.Entries[0].Description.EndsWith("\n\n", StringComparison.Ordinal), "loaded model preserves parent and multiline credentials");
        var session = new CatalogSession(location, settings);
        await session.OpenAsync(catalog, false, true, token);
        var reopened = new CatalogSession(location, settings);
        await reopened.RestoreAsync(token);
        check(reopened.Current is { IsReadOnly: false, Entries.Count: 1 }, "populated catalog is remembered and reopened");
        var settingsBefore = File.ReadAllBytes(Path.Combine(root, "settings", "settings.json"));
        File.WriteAllText(Path.Combine(topic, "broken.md"), "synthetic-invalid-content");
        await session.OpenAsync(catalog, false, true, token);
        check(session.Current is { IsReadOnly: true, Entries.Count: 1 }
            && settingsBefore.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "settings", "settings.json"))),
            "malformed catalog exposes valid records without saving selection");
        check(session.Current!.Diagnostics.All(diagnostic => !diagnostic.ToString().Contains("synthetic-invalid-content")),
            "diagnostics omit source values");
        File.WriteAllText(Path.Combine(topic, "broken.md"), MarkdownCodecChecks.Full);
        loaded = await Load();
        check(loaded.IsReadOnly && loaded.Entries.Count == 0
            && loaded.Diagnostics.Count(diagnostic => diagnostic.Field == "id") == 2,
            "all duplicate entry ID participants are excluded");
        File.WriteAllText(Path.Combine(topic, "broken.md"), MarkdownCodecChecks.Full.Replace(
            "33333333-3333-4333-8333-333333333333", topicId));
        loaded = await Load();
        check(loaded.IsReadOnly && loaded.Folders.Count == 0 && loaded.Entries.Count == 0,
            "topic-entry ID collision invalidates topic and prevents orphan entries");
        File.WriteAllText(Path.Combine(topic, "broken.md"), MarkdownCodecChecks.Full.Replace(
            "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"));
        loaded = await Load();
        check(!loaded.IsReadOnly && loaded.Entries.Count == 2, "external repair reloads cleanly");
        var absentTopic = Path.Combine(catalog, "No metadata");
        Directory.CreateDirectory(absentTopic);
        loaded = await Load();
        check(loaded.IsReadOnly && loaded.Diagnostics.Any(diagnostic => diagnostic.RelativePath == "No metadata/_topic.md"),
            "empty topic without metadata is diagnosed");
        File.WriteAllText(Path.Combine(absentTopic, "_topic.md"), topicText.Replace(topicId, "55555555-5555-4555-8555-555555555555"));
        Directory.CreateDirectory(Path.Combine(topic, "nested"));
        File.WriteAllText(Path.Combine(topic, "notes.txt"), "synthetic-unrelated-content");
        loaded = await Load();
        check(loaded.IsReadOnly && loaded.Entries.Count == 2
            && loaded.Diagnostics.Any(diagnostic => diagnostic.RelativePath == "Тема 日本語/nested")
            && loaded.Diagnostics.Any(diagnostic => diagnostic.RelativePath == "Тема 日本語/notes.txt"),
            "nested directories and unrelated files are diagnosed without hiding valid records");
        var metadataPath = Path.Combine(catalog, "_catalog.md");
        File.WriteAllText(metadataPath, "---\nformat_version: 2\n---\n");
        loaded = await Load();
        check(loaded.IsReadOnly && loaded.Folders.Count == 0 && loaded.Entries.Count == 0
            && loaded.Diagnostics.Any(diagnostic => diagnostic.Code == ErrorCode.UnsupportedFormat),
            "unsupported root prevents interpreting children");
        check(File.ReadAllText(metadataPath) == "---\nformat_version: 2\n---\n", "unsupported source remains unchanged");
        var validator = new MarkdownCatalogValidator();
        check(validator.Validate([new CatalogDocument("../outside.md", "synthetic")]).Count != 0,
            "document validation rejects escaping paths");
        check(validator.Validate([new CatalogDocument("_catalog.md", "---\nformat_version: 1\n---\n"),
            new CatalogDocument("CON/_topic.md", topicText)]).Count != 0, "reserved Windows topic names rejected");
        check(loaded.ToString() == typeof(OpenedCatalog).FullName, "loaded catalog string representation omits contents");

        async Task<OpenedCatalog> Load() => ((OperationResult<OpenedCatalog>.Success)await location.OpenAsync(catalog, false, token)).Value;
    }
}
