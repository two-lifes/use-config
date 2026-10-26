using UseConfig.Catalog;

namespace UseConfig.App;

internal enum EditChoice { Save, Discard, Cancel }

internal sealed class CatalogShell(CatalogSession session, IDescriptionEditor editor)
{
    private OpenedCatalog? catalog;
    public event Action? Changed;
    public Guid? FolderId { get; private set; }
    public Guid? EntryId { get; private set; }
    public string Query { get; private set; } = "";
    public string Description { get; private set; } = "";
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "";
    public WebsiteEntry? Entry => catalog?.Entries.FirstOrDefault(entry => entry.Id == EntryId);
    public bool CanEdit => Entry is not null && catalog is { IsReadOnly: false, Snapshot.IsReadOnly: false };
    public bool IsDirty => Entry is not null && Description != Entry.Description;
    public IReadOnlyList<CatalogFolder> Folders => catalog?.Folders.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    public IReadOnlyList<WebsiteEntry> Entries => catalog?.Entries.Where(entry => Query.Length == 0
        ? entry.FolderId == FolderId
        : new[] { entry.Name, entry.Url, entry.AdditionalUrl, entry.Description, entry.Username }
            .Any(value => value.Contains(Query, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    public void Synchronize()
    {
        if (ReferenceEquals(catalog, session.Current)) return;
        var sameRoot = catalog?.RootPath == session.Current?.RootPath;
        catalog = session.Current;
        FolderId = sameRoot && Folders.Any(folder => folder.Id == FolderId) ? FolderId : Folders.FirstOrDefault()?.Id;
        EntryId = null;
        Description = "";
        Query = "";
        Status = "";
    }

    public void Search(string query)
    {
        if (IsBusy) return;
        Query = query;
        Changed?.Invoke();
    }

    public void Edit(string text)
    {
        if (IsBusy || !CanEdit) return;
        Description = text;
        Status = "";
        Changed?.Invoke();
    }

    public void Discard()
    {
        if (IsBusy) return;
        DiscardCore();
        Changed?.Invoke();
    }

    private void DiscardCore()
    {
        Description = Entry?.Description ?? "";
        Status = "";
    }

    public Task<bool> SelectFolderAsync(Guid id, Func<EditChoice> prompt) => TransitionAsync(prompt, () =>
    {
        FolderId = id;
        EntryId = null;
        Description = "";
        Query = "";
        return Task.CompletedTask;
    }, FolderId != id && Folders.Any(folder => folder.Id == id));

    public Task<bool> SelectEntryAsync(Guid id, Func<EditChoice> prompt) => TransitionAsync(prompt, () =>
    {
        EntryId = id;
        Description = Entry!.Description;
        return Task.CompletedTask;
    }, EntryId != id && catalog?.Entries.Any(entry => entry.Id == id) == true);

    public async Task<bool> TransitionAsync(Func<EditChoice> prompt, Func<Task> action, bool needed = true)
    {
        if (IsBusy || !needed) return false;
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            if (IsDirty)
            {
                var choice = prompt();
                if (choice == EditChoice.Cancel) return false;
                if (choice == EditChoice.Save && !await SaveCoreAsync()) return false;
                if (choice == EditChoice.Discard) DiscardCore();
            }
            await action();
            return true;
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public Task<bool> SaveAsync() => TransitionAsync(() => EditChoice.Save, () => Task.CompletedTask);

    private async Task<bool> SaveCoreAsync()
    {
        if (!CanEdit || catalog is null || EntryId is not { } id) return false;
        Status = "Saving description…";
        Changed?.Invoke();
        var result = await editor.SaveAsync(catalog, id, Description, CancellationToken.None);
        if (result is OperationResult<OpenedCatalog>.Failure failure)
        {
            Status = failure.Code == ErrorCode.Conflict
                ? "The catalog changed outside the app. Your input is retained. Discard it explicitly before Retry."
                : $"Description was not saved ({failure.Code}). Your input is retained. Check the catalog and try again.";
            return false;
        }
        var updated = ((OperationResult<OpenedCatalog>.Success)result).Value;
        if (!session.Replace(catalog, updated))
        {
            Status = "The active catalog changed. Your input is retained.";
            return false;
        }
        catalog = updated;
        Description = Entry?.Description ?? "";
        Status = "Description saved.";
        return true;
    }
}
