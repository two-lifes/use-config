using System.Runtime.CompilerServices;
using System.Text.Json;
using UseConfig.Catalog;

namespace UseConfig.Storage;

public sealed class MarkdownCatalogFiles : ICatalogFiles, ICatalogRecovery
{
    private readonly ConditionalWeakTable<CatalogRevision, CatalogImage> revisions = new();
    private readonly ConditionalWeakTable<RecoveryOperation, RecoveryData> recoveries = new();
    private readonly SemaphoreSlim gate = new(1);
    private readonly Action<string>? checkpoint;

    public MarkdownCatalogFiles() { }
    internal MarkdownCatalogFiles(Action<string> checkpoint) => this.checkpoint = checkpoint;

    public Task<OperationResult<CatalogSnapshot>> ReadAsync(string rootPath, CancellationToken cancellationToken) =>
        Run(() => ReadCore(LocationPaths.Normalize(rootPath), cancellationToken), cancellationToken);

    public Task<OperationResult<CatalogSnapshot>> ApplyAsync(string rootPath, CatalogRevision revision,
        IReadOnlyList<FileChange> changes, CancellationToken cancellationToken) =>
        Run(() => ApplyCore(LocationPaths.Normalize(rootPath), revision, changes, cancellationToken), cancellationToken);

    public Task<OperationResult<IReadOnlyList<RecoveryOperation>>> FindAsync(string rootPath, CancellationToken cancellationToken) => Run(() =>
    {
        var root = LocationPaths.Normalize(rootPath);
        using var lease = new RecoveryLease(root, false);
        var diagnostics = new List<Diagnostic>();
        var pending = CatalogRecoveryStore.Scan(lease, root, diagnostics);
        if (pending.Count == 0 && diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired))
            throw new CatalogFault(ErrorCode.MalformedContent);
        return (IReadOnlyList<RecoveryOperation>)Array.AsReadOnly(pending.Select(data =>
        {
            var operation = new RecoveryOperation();
            recoveries.Add(operation, data);
            return operation;
        }).ToArray());
    }, cancellationToken);

    public Task<OperationResult<string>> SaveCopyAsync(string rootPath, RecoveryOperation operation, string destinationParent,
        CancellationToken cancellationToken) => Run(() =>
    {
        var root = LocationPaths.Normalize(rootPath);
        if (!recoveries.TryGetValue(operation, out var receipt) || receipt.Manifest.Root != root)
            throw new CatalogFault(ErrorCode.Conflict);
        using var lease = new RecoveryLease(root, false);
        var data = CatalogRecoveryStore.Read(receipt.Folder, root);
        if (data.Complete || data.ManifestHash != receipt.ManifestHash) throw new CatalogFault(ErrorCode.Conflict);
        cancellationToken.ThrowIfCancellationRequested();
        return CatalogRecoveryStore.SaveCopy(data, LocationPaths.Normalize(destinationParent), root, cancellationToken);
    }, cancellationToken);

    private async Task<OperationResult<T>> Run<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run<OperationResult<T>>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { return new OperationResult<T>.Success(operation()); }
                catch (JsonException) { return new OperationResult<T>.Failure(ErrorCode.MalformedContent); }
                catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
                { return new OperationResult<T>.Failure(LocationPaths.Classify(exception)!.Value); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private CatalogSnapshot ReadCore(string root, CancellationToken cancellationToken)
    {
        using var tree = new CatalogTree(root, null, cancellationToken);
        var image = tree.Capture();
        using var lease = new RecoveryLease(root, false);
        CatalogRecoveryStore.Scan(lease, root, image.Diagnostics);
        var revision = new CatalogRevision();
        revisions.Add(revision, image);
        return new CatalogSnapshot(revision, image.Documents, Array.AsReadOnly(image.Diagnostics.ToArray()));
    }

    private CatalogSnapshot ApplyCore(string root, CatalogRevision revision, IReadOnlyList<FileChange> changes, CancellationToken cancellationToken)
    {
        if (!revisions.TryGetValue(revision, out var original) || !root.Equals(original.Root, StringComparison.OrdinalIgnoreCase))
            throw new CatalogFault(ErrorCode.Conflict);
        if (original.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired)) throw new CatalogFault(ErrorCode.RecoveryRequired);
        if (original.Diagnostics.Any(item => item.Code != ErrorCode.RecoveryCleanupFailed)) throw new CatalogFault(ErrorCode.ReadOnly);
        var plan = new ChangePlan(original, changes);
        using (var lease = new RecoveryLease(root, true))
        {
            var diagnostics = new List<Diagnostic>();
            CatalogRecoveryStore.Scan(lease, root, diagnostics);
            if (diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired)) throw new CatalogFault(ErrorCode.RecoveryRequired);
            using var tree = new CatalogTree(root, plan, cancellationToken);
            var current = tree.Capture();
            if (!current.SameAs(original)) throw new CatalogFault(ErrorCode.Conflict);
            if (current.Diagnostics.Count != 0) throw new CatalogFault(ErrorCode.ReadOnly);
            checkpoint?.Invoke("locked");
            cancellationToken.ThrowIfCancellationRequested();
            using var journal = new RecoveryJournal(lease, current);
            var undo = new Stack<Action>();
            var detached = new HashSet<NativeCatalogFile>();
            var started = false;
            var committed = false;
            try
            {
                checkpoint?.Invoke("prepared");
                cancellationToken.ThrowIfCancellationRequested();
                revisions.Remove(revision);
                started = true;
                foreach (var create in plan.Changes.OfType<FileChange.CreateDirectory>())
                {
                    var path = create.RelativePath;
                    var directory = NativeCatalogFile.Create(tree.Directories[""], path, true);
                    tree.Directories.Add(path, directory);
                    undo.Push(() => { directory.Delete(); directory.Dispose(); tree.Directories.Remove(path); });
                }
                foreach (var change in plan.Changes.Where(change => change is not FileChange.CreateDirectory))
                {
                    switch (change)
                    {
                        case FileChange.CreateDocument create:
                            {
                                var path = create.Document.RelativePath;
                                var file = NativeCatalogFile.Create(tree.Parent(path), CatalogTree.Leaf(path), false);
                                tree.Files.Add(path, file);
                                undo.Push(() => { file.Delete(); file.Dispose(); tree.Files.Remove(path); });
                                file.Write(plan.ResultFiles[path], () => checkpoint?.Invoke("partial"));
                                break;
                            }
                        case FileChange.ReplaceDocument replace:
                            {
                                var path = replace.Document.RelativePath;
                                var file = tree.Files[path];
                                undo.Push(() => file.Write(original.Files[path]));
                                file.Write(plan.ResultFiles[path], () => checkpoint?.Invoke("partial"));
                                break;
                            }
                        case FileChange.Move move: Relocate(move.Source, move.Destination); break;
                        case FileChange.Delete delete: Relocate(delete.RelativePath, null); break;
                    }
                }
                checkpoint?.Invoke("written");
                var result = tree.Capture();
                if (result.Diagnostics.Count != 0 || !CatalogImage.SameFiles(plan.ResultFiles, result.Files)
                    || !plan.ResultDirectories.SetEquals(tree.Directories.Keys)) throw new CatalogFault(ErrorCode.Conflict);
                journal.Complete();
                committed = true;
                checkpoint?.Invoke("committed");
            }
            catch (Exception exception) when (LocationPaths.Classify(exception) is not null || exception is OperationCanceledException)
            {
                checkpoint?.Invoke("failure-" + LocationPaths.Classify(exception));
                if (committed) throw new CatalogFault(ErrorCode.RecoveryCleanupFailed);
                try
                {
                    checkpoint?.Invoke("rollback");
                    while (undo.TryPop(out var action)) action();
                    var restored = tree.Capture();
                    if (restored.Diagnostics.Count != 0 || !CatalogImage.SameFiles(original.Files, restored.Files)
                        || !tree.Directories.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(original.Stamps.Keys.Except(original.Files.Keys, StringComparer.OrdinalIgnoreCase)))
                        throw new CatalogFault(ErrorCode.RecoveryRequired);
                    journal.Complete();
                }
                catch (Exception rollbackException) when (LocationPaths.Classify(rollbackException) is not null)
                { checkpoint?.Invoke("rollback-failure-" + LocationPaths.Classify(rollbackException)); throw new CatalogFault(ErrorCode.RecoveryRequired); }
                if (started && exception is OperationCanceledException) throw new CatalogFault(ErrorCode.IoFailure);
                throw;
            }
            finally { foreach (var file in detached) file.Dispose(); }

            void MoveFile(string source, string? destination, NativeCatalogFile parent, string name)
            {
                var file = tree.Files[source];
                var oldParent = tree.Parent(source);
                file.Rename(parent, name);
                tree.Files.Remove(source);
                if (destination is not null) tree.Files.Add(destination, file); else detached.Add(file);
                undo.Push(() =>
                {
                    file.Rename(oldParent, CatalogTree.Leaf(source));
                    if (destination is not null) tree.Files.Remove(destination); else detached.Remove(file);
                    tree.Files.Add(source, file);
                });
            }

            void Relocate(string source, string? destination)
            {
                if (tree.Files.ContainsKey(source))
                {
                    MoveFile(source, destination, destination is null ? journal.Directory : tree.Parent(destination),
                        destination is null ? journal.HeldFile(source) : CatalogTree.Leaf(destination));
                    return;
                }
                // Empty the topic under held handles before moving it, including on Windows versions
                // that reject directory rename while descendants are open.
                var children = tree.Files.Keys.Where(path => path.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var child in children) MoveFile(child, null, journal.Directory, journal.HeldFile(child));
                var directory = tree.Directories[source];
                directory.Rename(destination is null ? journal.Directory : tree.Directories[""], destination ?? journal.HeldDirectory(source));
                tree.Directories.Remove(source);
                if (destination is not null) tree.Directories.Add(destination, directory); else detached.Add(directory);
                undo.Push(() =>
                {
                    directory.Rename(tree.Directories[""], source);
                    if (destination is not null) tree.Directories.Remove(destination); else detached.Remove(directory);
                    tree.Directories.Add(source, directory);
                });
                if (destination is null) return;
                foreach (var child in children)
                {
                    var file = detached.Single(item => item.Stamp().Id == original.Stamps[child].Id && item.Stamp().Volume == original.Stamps[child].Volume);
                    var target = destination + child[source.Length..];
                    file.Rename(directory, CatalogTree.Leaf(child));
                    detached.Remove(file);
                    tree.Files.Add(target, file);
                    undo.Push(() => { file.Rename(journal.Directory, journal.HeldFile(child)); tree.Files.Remove(target); detached.Add(file); });
                }
            }
        }
        // Write times may be finalized by Windows when the write handle closes. Issue a fresh revision afterwards.
        return ReadCore(root, CancellationToken.None);
    }
}
