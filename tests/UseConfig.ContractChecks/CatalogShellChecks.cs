using UseConfig.App;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class CatalogShellChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "shell-checks", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "catalog");
        var settingsPath = Path.Combine(root, "settings");
        Directory.CreateDirectory(path);
        var files = new MarkdownCatalogFiles();
        var location = new MarkdownCatalogLocation(settingsPath, files);
        var session = new CatalogSession(location, new CatalogSettings(settingsPath));
        var token = CancellationToken.None;
        await session.OpenAsync(path, true, false, token);
        var topic = Path.Combine(path, "Alpha");
        Directory.CreateDirectory(topic);
        File.WriteAllText(Path.Combine(topic, "_topic.md"), "---\nformat_version: 1\nid: \"22222222-2222-4222-8222-222222222222\"\n---\n");
        var documentPath = Path.Combine(topic, "manual.MD");
        File.WriteAllText(documentPath, MarkdownCodecChecks.Full);
        var otherTopic = Path.Combine(path, "Zulu");
        Directory.CreateDirectory(otherTopic);
        File.WriteAllText(Path.Combine(otherTopic, "_topic.md"), "---\nformat_version: 1\nid: \"55555555-5555-4555-8555-555555555555\"\n---\n");
        File.WriteAllText(Path.Combine(otherTopic, "other.md"), MarkdownCodecChecks.Full.Replace(
            "33333333-3333-4333-8333-333333333333", "44444444-4444-4444-8444-444444444444"));
        await session.OpenAsync(path, false, false, token);
        var editor = new ControlledEditor(new MarkdownDescriptionEditor(files));
        var shell = new CatalogShell(session, editor);
        shell.Synchronize();
        var original = session.Current!.Entries.First(entry => entry.FolderId == shell.FolderId);
        check(shell.Folders[0].Name == "Alpha" && shell.Entry is null && shell.Entries.Count == 1, "shell starts with first sorted folder and no details");
        await shell.SelectEntryAsync(original.Id, () => throw new InvalidOperationException());
        check(shell.CanEdit && !shell.IsDirty && shell.Description == original.Description, "shell selection loads saved description");
        foreach (var query in new[] { original.Name, original.Url, original.AdditionalUrl, original.Description, original.Username })
        {
            shell.Search(query.ToUpperInvariant());
            check(shell.Entries.Count == 2, "search matches a supported field across folders ignoring case");
        }
        foreach (var query in new[] { original.Password, original.TwoFactorNote, "no-match-synthetic" })
        {
            shell.Search(query);
            check(shell.Entries.Count == 0 && shell.EntryId == original.Id, "search excludes secrets and retains selected editor with no results");
        }
        shell.Search("");
        check(shell.Entries.Count == 1, "empty query restores selected folder");
        shell.Edit("new searchable Описание 日本語\n\n");
        check(shell.IsDirty, "description changes mark editor dirty");
        shell.Search("new searchable");
        check(shell.Entries.Count == 0, "search ignores unsaved input");
        var transitioned = false;
        check(!await shell.TransitionAsync(() => EditChoice.Cancel, () => { transitioned = true; return Task.CompletedTask; })
            && !transitioned && shell.IsDirty, "cancel preserves input and prevents pending action");
        var otherId = session.Current.Entries.Single(entry => entry.Id != original.Id).Id;
        check(!await shell.SelectEntryAsync(otherId, () => EditChoice.Cancel) && shell.EntryId == original.Id, "cancel retains entry selection");
        check(!await shell.SelectFolderAsync(shell.Folders[1].Id, () => EditChoice.Cancel) && shell.FolderId == original.FolderId, "cancel retains folder selection");
        check(await shell.SaveAsync() && !shell.IsDirty && shell.Entries.Count == 1, "save refreshes saved search results and clears dirty state");
        var saved = shell.Entry!;
        check(saved.Description == "new searchable Описание 日本語\n\n" && saved.Id == original.Id && saved.FolderId == original.FolderId
            && saved.Name == original.Name && saved.Url == original.Url && saved.Username == original.Username && saved.Password == original.Password
            && saved.AdditionalUrl == original.AdditionalUrl && saved.TwoFactorNote == original.TwoFactorNote,
            "description save preserves all other fields and stable identities");
        check(File.Exists(documentPath), "description save preserves manually named path");
        var reopened = (OperationResult<OpenedCatalog>.Success)await location.OpenAsync(path, false, token);
        check(reopened.Value.Entries.Single(entry => entry.Id == original.Id).Description == saved.Description, "description survives catalog reopening");
        shell.Edit("discard this");
        check(await shell.SelectEntryAsync(otherId, () => EditChoice.Discard) && shell.EntryId == otherId && !shell.IsDirty,
            "discard permits entry change without saving input");
        await shell.SelectEntryAsync(original.Id, () => EditChoice.Cancel);
        check(shell.Description == saved.Description, "discard retains previously saved description");
        shell.Edit("");
        check(await shell.SaveAsync() && shell.Entry!.Description == "", "empty description saves");
        foreach (var code in new[] { ErrorCode.AccessDenied, ErrorCode.InsufficientSpace, ErrorCode.IoFailure, ErrorCode.Conflict })
        {
            shell.Edit("retain on failure");
            editor.Error = code;
            transitioned = false;
            check(!await shell.TransitionAsync(() => EditChoice.Save, () => { transitioned = true; return Task.CompletedTask; })
                && !transitioned && shell.Description == "retain on failure" && shell.IsDirty && shell.Status.Length > 0,
                "save failure retains editor and blocks pending action: " + code);
        }
        editor.Error = null;
        editor.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = shell.SaveAsync();
        check(shell.IsBusy, "save exposes busy state");
        var calls = editor.Calls;
        shell.Edit("competing");
        shell.Search("competing");
        shell.Discard();
        check(!await shell.SelectEntryAsync(otherId, () => EditChoice.Discard) && !await shell.SaveAsync()
            && editor.Calls == calls && shell.Description == "retain on failure", "busy save blocks competing edits and transitions");
        editor.Gate.SetResult();
        check(await saving && !shell.IsBusy, "save completes and releases busy state");
        editor.Gate = null;
        shell.Edit("before folder transition");
        check(await shell.SelectFolderAsync(shell.Folders[1].Id, () => EditChoice.Save) && shell.Entry is null && shell.Query == "",
            "successful save allows folder transition and clears editor");

        foreach (var mutation in new[] { "edit", "rename", "delete" })
        {
            await session.OpenAsync(path, false, false, token);
            shell.Synchronize();
            await shell.SelectEntryAsync(original.Id, () => EditChoice.Cancel);
            shell.Edit("must remain unsaved");
            var bytes = File.ReadAllBytes(documentPath);
            var moved = Path.Combine(topic, "renamed.md");
            if (mutation == "edit") File.AppendAllText(documentPath, "external change");
            else File.Move(documentPath, mutation == "rename" ? moved : Path.Combine(root, "removed.md"));
            var external = File.Exists(documentPath) ? File.ReadAllBytes(documentPath) : null;
            check(!await shell.SaveAsync() && shell.IsDirty && shell.Description == "must remain unsaved", "external " + mutation + " blocks stale save and preserves input");
            check(external is null ? !File.Exists(documentPath) : external.SequenceEqual(File.ReadAllBytes(documentPath)),
                "external " + mutation + " is neither overwritten nor recreated");
            if (mutation == "rename") File.Move(moved, documentPath);
            else if (mutation == "delete") File.Move(Path.Combine(root, "removed.md"), documentPath);
            else File.WriteAllBytes(documentPath, bytes);
            shell.Discard();
        }
        File.WriteAllText(Path.Combine(topic, "bad.md"), "invalid");
        await session.OpenAsync(path, false, false, token);
        shell.Synchronize();
        await shell.SelectEntryAsync(original.Id, () => EditChoice.Cancel);
        var before = shell.Description;
        shell.Edit("blocked");
        check(!shell.CanEdit && shell.Description == before, "read-only catalog blocks description editing");
        check(await new MarkdownDescriptionEditor(files).SaveAsync(session.Current!, original.Id, "blocked", token)
            is OperationResult<OpenedCatalog>.Failure { Code: ErrorCode.ReadOnly }, "storage rejects read-only saves");
        var noSnapshot = new OpenedCatalog(path, session.Current!.Folders, session.Current.Entries, []);
        check(await new MarkdownDescriptionEditor(files).SaveAsync(noSnapshot, original.Id, "blocked", token)
            is OperationResult<OpenedCatalog>.Failure { Code: ErrorCode.ReadOnly }, "storage rejects unversioned saves");
        check(!session.Replace(noSnapshot, noSnapshot), "session rejects updates for a replaced catalog");
    }

    private sealed class ControlledEditor(IDescriptionEditor inner) : IDescriptionEditor
    {
        public ErrorCode? Error { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public int Calls { get; private set; }
        public async Task<OperationResult<OpenedCatalog>> SaveAsync(OpenedCatalog catalog, Guid entryId, string description, CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate is not null) await Gate.Task;
            return Error is { } code ? new OperationResult<OpenedCatalog>.Failure(code)
                : await inner.SaveAsync(catalog, entryId, description, cancellationToken);
        }
    }
}
