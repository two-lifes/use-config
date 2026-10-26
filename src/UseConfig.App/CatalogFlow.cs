using UseConfig.Catalog;

namespace UseConfig.App;

internal sealed class CatalogFlow(CatalogSession session, ICatalogRecovery recovery)
{
    private sealed record Request(string? Path, bool Create, bool Remember);
    private Request retry = new(null, false, false);
    public event Action? Changed;
    public CatalogSession Session => session;
    public bool IsChoosing { get; private set; } = true;
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Select an existing catalog or create one in an empty folder.";
    public string? AttemptedPath { get; private set; }
    public IReadOnlyList<RecoveryOperation> RecoveryOperations { get; private set; } = [];

    public void Choose()
    {
        if (IsBusy) return;
        IsChoosing = true;
        Status = "Choose another catalog, or return to the current catalog.";
        Changed?.Invoke();
    }

    public void Back()
    {
        if (IsBusy || session.Current is null) return;
        IsChoosing = false;
        Status = OpenStatus();
        Changed?.Invoke();
    }

    public void PickerFailed()
    {
        if (IsBusy) return;
        Status = "The folder picker is unavailable. Please try again.";
        Changed?.Invoke();
    }

    public Task RestoreAsync() => OpenAsync(new(null, false, false));
    public Task SelectAsync(string? path, bool create) => path is null ? Task.CompletedTask : OpenAsync(new(path, create, true));
    public Task RetryAsync() => OpenAsync(retry);

    private async Task OpenAsync(Request request)
    {
        if (IsBusy) return;
        retry = request;
        AttemptedPath = request.Path;
        IsBusy = true;
        Status = request.Create ? "Creating catalog…" : "Opening catalog…";
        Changed?.Invoke();
        try
        {
            var result = request.Path is null
                ? await session.RestoreAsync(CancellationToken.None)
                : await session.OpenAsync(request.Path, request.Create, request.Remember, CancellationToken.None);
            AttemptedPath ??= session.RememberedPath;
            if (result is OperationResult<Completion>.Failure failure)
            {
                IsChoosing = true;
                Status = Describe(failure.Code);
                return;
            }

            IsChoosing = session.Current is null;
            Status = OpenStatus();
            RecoveryOperations = [];
            if (session.Current is { } current)
            {
                // A successful create must never be repeated as creation by Retry.
                retry = new(current.RootPath, false, true);
                if (current.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired))
                {
                    var pending = await recovery.FindAsync(current.RootPath, CancellationToken.None);
                    if (pending is OperationResult<IReadOnlyList<RecoveryOperation>>.Success found && found.Value.Count > 0)
                        RecoveryOperations = found.Value;
                    else
                        Status = "Recovery data could not be verified. Preserve the catalog and service copies for inspection.";
                }
            }
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    public async Task SaveRecoveryAsync(RecoveryOperation operation, string destination)
    {
        if (IsBusy || session.CurrentPath is not { } root || !RecoveryOperations.Contains(operation)) return;
        IsBusy = true;
        Status = "Saving recovery copy…";
        Changed?.Invoke();
        try
        {
            var result = await recovery.SaveCopyAsync(root, operation, destination, CancellationToken.None);
            Status = result is OperationResult<string>.Success success
                ? $"Recovery copy saved to {success.Value}. The current catalog is unchanged. Choose the copy explicitly to open it."
                : Describe(((OperationResult<string>.Failure)result).Code);
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    private string OpenStatus() => session.Current?.IsReadOnly == true
        ? "Catalog is read-only. Repair the listed files and retry. This selection was not saved."
        : session.PersistenceError is not null
            ? "Catalog is open, but its path could not be remembered. Retry to save the path."
            : session.Current is { } current
                ? $"Catalog is open. {current.Folders.Count} folders, {current.Entries.Count} websites."
                : "Select an existing catalog or create one in an empty folder.";

    public static string Describe(ErrorCode code) => code switch
    {
        ErrorCode.NotFound => "The catalog directory is missing. Retry or choose another location.",
        ErrorCode.AccessDenied => "Access was denied. Check permissions and retry, or choose another location.",
        ErrorCode.AlreadyExists => "The selected folder is not empty. No existing files were replaced.",
        ErrorCode.MalformedContent => "Catalog metadata or settings are malformed. Repair them or explicitly choose another catalog.",
        ErrorCode.UnsupportedFormat => "The catalog or settings version is unsupported. Choose a supported catalog; existing files are preserved.",
        ErrorCode.InvalidInput => "Choose a normal directory outside the application settings directory. Linked or overlapping paths are not supported.",
        ErrorCode.Conflict => "The directory changed during creation. Check it and try again.",
        ErrorCode.InsufficientSpace => "There is not enough disk space. Free space and retry.",
        ErrorCode.RecoveryRequired => "An interrupted operation needs recovery. Save a separate recovery copy below; the current catalog will not be changed.",
        ErrorCode.RecoveryCleanupFailed => "A service recovery copy could not be removed. Preserve it for inspection; the catalog remains available.",
        _ => "The operation could not finish. Check the directory and permissions, then retry."
    };
}
