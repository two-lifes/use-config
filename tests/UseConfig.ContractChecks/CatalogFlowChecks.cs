using UseConfig.App;
using UseConfig.Catalog;

internal static class CatalogFlowChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var location = new Location();
        var settings = new Settings();
        var recovery = new Recovery();
        var session = new CatalogSession(location, settings);
        var flow = new CatalogFlow(session, recovery);
        await flow.RestoreAsync();
        check(flow.IsChoosing && !flow.IsBusy && session.Current is null, "flow first launch shows selection");
        await flow.SelectAsync(null, true);
        check(location.Calls == 0 && settings.Writes == 0, "picker cancellation neither creates nor remembers");
        await flow.SelectAsync("first", true);
        check(!flow.IsChoosing && session.CurrentPath == "first" && settings.Path == "first", "create enters catalog and remembers path");
        await flow.RetryAsync();
        check(!location.LastCreate, "retry after successful create opens rather than recreates");
        flow.Choose();
        check(flow.IsChoosing && session.CurrentPath == "first", "change keeps active catalog");
        await flow.SelectAsync(null, false);
        check(flow.IsChoosing && session.CurrentPath == "first", "canceled change retains active catalog and selection view");
        flow.Back();
        check(!flow.IsChoosing, "back returns to catalog");
        flow.Choose();
        location.Error = ErrorCode.NotFound;
        await flow.SelectAsync("missing", false);
        check(flow.IsChoosing && session.CurrentPath == "first" && settings.Path == "first", "failed change preserves active and remembered catalogs");
        await flow.RetryAsync();
        check(location.LastPath == "missing" && flow.Status.Contains("missing"), "retry uses failed target instead of active catalog");
        location.Error = null;
        await flow.RetryAsync();
        check(!flow.IsChoosing && session.CurrentPath == "missing", "retry success enters recovered location");

        location.Diagnostics = [new(ErrorCode.UnsupportedFormat)];
        await flow.SelectAsync("readonly", false);
        check(!flow.IsChoosing && session.Current!.IsReadOnly && settings.Path == "missing", "invalid catalog enters read-only without persisting selection");
        check(flow.Status.Contains("read-only"), "read-only guidance is visible");
        location.Diagnostics = [];
        settings.FailWrite = true;
        await flow.SelectAsync("unsaved-path", false);
        check(!flow.IsChoosing && flow.Status.Contains("could not be remembered"), "persistence failure retains open catalog and explains retry");
        settings.FailWrite = false;
        await flow.RetryAsync();
        check(settings.Path == "unsaved-path" && session.PersistenceError is null, "retry saves failed path persistence");

        var restored = new CatalogFlow(new CatalogSession(location, settings), recovery);
        await restored.RestoreAsync();
        check(!restored.IsChoosing && restored.Session.CurrentPath == settings.Path, "remembered catalog automatically opens");
        settings.FailRead = true;
        var failedRestore = new CatalogFlow(new CatalogSession(location, settings), recovery);
        await failedRestore.RestoreAsync();
        var reads = settings.Reads;
        settings.FailRead = false;
        await failedRestore.RetryAsync();
        check(settings.Reads == reads + 1 && !failedRestore.IsChoosing, "settings error retry rereads settings");

        location.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = flow.SelectAsync("delayed", false);
        check(flow.IsBusy && flow.Status == "Opening catalog…", "delayed opening exposes local loading state");
        var calls = location.Calls;
        await flow.SelectAsync("competing", true);
        await flow.RetryAsync();
        flow.Choose();
        check(location.Calls == calls && !flow.IsChoosing, "busy flow blocks competing operations and navigation");
        location.Gate.SetResult();
        await opening;
        location.Gate = null;
        check(!flow.IsBusy && session.CurrentPath == "delayed", "opening releases busy state");

        location.Diagnostics = [new(ErrorCode.RecoveryRequired)];
        await flow.SelectAsync("recovery", false);
        check(flow.RecoveryOperations.Count == 1, "recovery choices loaded for interrupted operation");
        recovery.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = flow.SaveRecoveryAsync(recovery.Operation, "destination");
        check(flow.IsBusy && flow.Status == "Saving recovery copy…", "recovery exposes its own loading message");
        calls = location.Calls;
        await flow.SelectAsync("competing", false);
        await flow.SaveRecoveryAsync(recovery.Operation, "other");
        check(location.Calls == calls && recovery.Saves == 1, "recovery blocks catalog switching and duplicate saves");
        recovery.Gate.SetResult();
        await saving;
        check(!flow.IsBusy && session.CurrentPath == "recovery" && flow.Status.Contains("unchanged"), "recovery completion preserves current catalog");
        recovery.Gate = null;
        recovery.FailSave = true;
        await flow.SaveRecoveryAsync(recovery.Operation, "destination");
        check(!flow.IsBusy && flow.Status.Contains("disk space"), "recovery failure releases controls and explains cause");
        recovery.FailFind = true;
        await flow.RetryAsync();
        check(flow.RecoveryOperations.Count == 0 && flow.Status.Contains("could not be verified"), "unverified recovery disables save choices");
        flow.PickerFailed();
        check(flow.Status.Contains("picker is unavailable"), "picker error has actionable text");
    }

    private sealed class Location : ICatalogLocation
    {
        public int Calls;
        public string? LastPath;
        public bool LastCreate;
        public ErrorCode? Error;
        public Diagnostic[] Diagnostics = [];
        public TaskCompletionSource? Gate;
        public async Task<OperationResult<OpenedCatalog>> OpenAsync(string path, bool create, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = path;
            LastCreate = create;
            if (Gate is not null) await Gate.Task;
            return Error is { } error ? new OperationResult<OpenedCatalog>.Failure(error)
                : new OperationResult<OpenedCatalog>.Success(new(path, [], [], Diagnostics));
        }
    }

    private sealed class Settings : ICatalogSettings
    {
        public string? Path;
        public bool FailRead;
        public bool FailWrite;
        public int Reads;
        public int Writes;
        public Task<OperationResult<string?>> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<OperationResult<string?>>(FailRead ? new OperationResult<string?>.Failure(ErrorCode.MalformedContent)
                : new OperationResult<string?>.Success(Path));
        }
        public Task<OperationResult<Completion>> RememberAsync(string path, CancellationToken cancellationToken)
        {
            Writes++;
            if (!FailWrite) Path = path;
            return Task.FromResult<OperationResult<Completion>>(FailWrite ? new OperationResult<Completion>.Failure(ErrorCode.AccessDenied)
                : new OperationResult<Completion>.Success(Completion.Done));
        }
    }

    private sealed class Recovery : ICatalogRecovery
    {
        public RecoveryOperation Operation = new();
        public int Saves;
        public TaskCompletionSource? Gate;
        public bool FailSave;
        public bool FailFind;
        public Task<OperationResult<IReadOnlyList<RecoveryOperation>>> FindAsync(string rootPath, CancellationToken cancellationToken) =>
            Task.FromResult<OperationResult<IReadOnlyList<RecoveryOperation>>>(FailFind
                ? new OperationResult<IReadOnlyList<RecoveryOperation>>.Failure(ErrorCode.IoFailure)
                : new OperationResult<IReadOnlyList<RecoveryOperation>>.Success([Operation]));
        public async Task<OperationResult<string>> SaveCopyAsync(string rootPath, RecoveryOperation operation, string destinationParent, CancellationToken cancellationToken)
        {
            Saves++;
            if (Gate is not null) await Gate.Task;
            return FailSave ? new OperationResult<string>.Failure(ErrorCode.InsufficientSpace)
                : new OperationResult<string>.Success(destinationParent);
        }
    }
}
