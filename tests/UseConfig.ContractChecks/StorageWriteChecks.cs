using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class StorageWriteChecks
{
    private const string Metadata = "---\nformat_version: 1\n---\n";
    private const string Topic = "---\nformat_version: 1\nid: \"22222222-2222-4222-8222-222222222222\"\n---\n";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string Base = Path.Combine(AppContext.BaseDirectory, "write-checks", Guid.NewGuid().ToString("N"));
    private static string NewRoot()
    {
        var root = Path.Combine(Base, Guid.NewGuid().ToString("N"), "Каталог 日本語");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "_catalog.md"), Metadata);
        return root;
    }
    private static T Value<T>(OperationResult<T> result)
    {
        if (result is OperationResult<T>.Success success) return success.Value;
        Console.WriteLine("Unexpected storage category: " + ((OperationResult<T>.Failure)result).Code);
        throw new InvalidOperationException("Unexpected storage result");
    }
    private static bool Failed<T>(OperationResult<T> result, ErrorCode code) => result is OperationResult<T>.Failure failure && failure.Code == code;
    private static FileChange Replace(string text = Metadata) => new FileChange.ReplaceDocument(new CatalogDocument("_catalog.md", text));

    [SupportedOSPlatform("windows")]
    internal static async Task RunAsync(Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file-operation checks require Windows.");
        var root = NewRoot();
        var files = new MarkdownCatalogFiles();
        var initial = Value(await files.ReadAsync(root, None));
        var created = Value(await files.ApplyAsync(root, initial.Revision,
            [new FileChange.CreateDirectory("Тема"), new FileChange.CreateDocument(new CatalogDocument("Тема/_topic.md", Topic)),
             new FileChange.CreateDocument(new CatalogDocument("Тема/example.md", MarkdownCodecChecks.Full))], None));
        check(created.Documents.Count == 3 && initial.Documents.Count == 1 && !ReferenceEquals(initial.Revision, created.Revision), "write creates complete Unicode topic and immutable snapshots");
        var replaced = Value(await files.ApplyAsync(root, created.Revision,
            [new FileChange.ReplaceDocument(new CatalogDocument("Тема/example.md", MarkdownCodecChecks.Full + "Changed\n"))], None));
        check(replaced.Documents.Single(item => item.RelativePath.EndsWith("example.md")).Text.EndsWith("Changed\n"), "write replaces through exclusive handle");
        check(Failed(await files.ApplyAsync(root, replaced.Revision, [new FileChange.Move("Тема/example.md", "root.md")], None), ErrorCode.MalformedContent), "entry cannot move outside a topic");
        var moved = Value(await files.ApplyAsync(root, replaced.Revision, [new FileChange.CreateDirectory("Other"),
            new FileChange.CreateDocument(new CatalogDocument("Other/_topic.md", Topic.Replace("22222222-2222-4222-8222-222222222222", "44444444-4444-4444-8444-444444444444"))),
            new FileChange.Move("Тема/example.md", "Other/entry.md")], None));
        check(File.Exists(Path.Combine(root, "Other", "entry.md")), "write moves file across topic boundary");
        moved = Value(await files.ApplyAsync(root, moved.Revision, [new FileChange.Move("Тема", "Renamed")], None));
        check(Directory.Exists(Path.Combine(root, "Renamed")), "write moves topic and metadata");
        moved = Value(await files.ApplyAsync(root, moved.Revision, [new FileChange.Move("Other/entry.md", "Renamed/entry.md")], None));
        moved = Value(await files.ApplyAsync(root, moved.Revision, [new FileChange.Move("Renamed", "Again")], None));
        check(File.Exists(Path.Combine(root, "Again", "entry.md")), "write moves populated topic");
        moved = Value(await files.ApplyAsync(root, moved.Revision, [new FileChange.Delete("Again/entry.md")], None));
        moved = Value(await files.ApplyAsync(root, moved.Revision, [new FileChange.Delete("Again"), new FileChange.Delete("Other")], None));
        check(moved.Documents.Count == 1 && !moved.IsReadOnly, "write deletes document and topic and cleans journals");
        check(!Directory.EnumerateDirectories(CatalogRecoveryStore.ContainerFor(root)).Any(), "successful operations remove service copies");
        check(Failed(await files.ApplyAsync(root, new CatalogRevision(), [Replace()], None), ErrorCode.Conflict), "unknown revision conflicts");
        check(Failed(await new MarkdownCatalogFiles().ApplyAsync(root, moved.Revision, [Replace()], None), ErrorCode.Conflict), "foreign adapter revision conflicts");
        check(Failed(await files.ApplyAsync(NewRoot(), moved.Revision, [Replace()], None), ErrorCode.Conflict), "revision is bound to root");
        check(Failed(await files.ApplyAsync(root, moved.Revision, [new FileChange.CreateDocument(new CatalogDocument("_catalog.md", Metadata))], None), ErrorCode.AlreadyExists), "create refuses overwrite");
        check(Failed(await files.ApplyAsync(root, moved.Revision, [new FileChange.CreateDirectory("Empty")], None), ErrorCode.MalformedContent), "topic without metadata is rejected before mutation");
        check(Failed(await files.ApplyAsync(root, moved.Revision, [Replace(), Replace()], None), ErrorCode.InvalidInput), "ambiguous overlapping changes rejected");
        check(Failed(await files.ApplyAsync(root, moved.Revision, [new FileChange.CreateDocument(new CatalogDocument("../outside.md", Metadata))], None), ErrorCode.InvalidInput), "escaping destination rejected");

        await Stale("bytes changed with restored timestamp", path => { var time = File.GetLastWriteTimeUtc(path); File.WriteAllText(path, Metadata + "\n"); File.SetLastWriteTimeUtc(path, time); });
        await Stale("identical bytes in replacement file", path => { File.Move(path, path + ".old"); File.WriteAllText(path, Metadata); File.Delete(path + ".old"); });
        await Stale("external rename", path => File.Move(path, path + ".renamed"));
        await Stale("external delete", File.Delete);
        await Stale("metadata attributes", path => File.SetAttributes(path, FileAttributes.Hidden));

        root = NewRoot();
        var forbidden = Path.Combine(root, "_catalog.md");
        var link = Path.Combine(Path.GetDirectoryName(root)!, "alias.md");
        if (!CreateHardLinkW(link, forbidden, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        files = new MarkdownCatalogFiles(point =>
        {
            if (point != "locked") return;
            using var child = Start("--storage-contend", root, link);
            if (!child.WaitForExit(10000) || child.ExitCode != 0) throw new InvalidOperationException("Child could mutate held objects");
        });
        initial = Value(await files.ReadAsync(root, None));
        check(!Value(await files.ApplyAsync(root, initial.Revision, [Replace()], None)).IsReadOnly,
            "separate process cannot write rename swap root or write through hard link during mutation");

        root = NewRoot();
        files = new MarkdownCatalogFiles(point => { if (point == "partial") throw new IOException("Synthetic write failure"); });
        initial = Value(await files.ReadAsync(root, None));
        var rollbackResult = await files.ApplyAsync(root, initial.Revision, [Replace(Metadata.Replace("\n", "\r\n"))], None);
        check(Failed(rollbackResult, ErrorCode.IoFailure)
            && File.ReadAllText(Path.Combine(root, "_catalog.md")) == Metadata, "partial write restores original through retained handle");
        check(!Value(await files.ReadAsync(root, None)).IsReadOnly, "rolled-back operation is completed and cleaned on next read");

        root = NewRoot();
        files = new MarkdownCatalogFiles(point => { if (point is "partial" or "rollback") throw new IOException("Synthetic rollback failure"); });
        initial = Value(await files.ReadAsync(root, None));
        check(Failed(await files.ApplyAsync(root, initial.Revision, [Replace(Metadata.Replace("\n", "\r\n"))], None), ErrorCode.RecoveryRequired), "failed rollback requires recovery");
        await Recover(root, files, "rollback failure");

        foreach (var point in new[] { "prepared", "partial", "written", "committed" })
        {
            root = NewRoot();
            using var child = Start("--storage-crash", root, point);
            var reached = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (reached != point) throw new InvalidOperationException("Crash child did not reach " + point);
            child.Kill();
            await child.WaitForExitAsync();
            files = new MarkdownCatalogFiles();
            // Process termination can signal before Windows finishes releasing file objects.
            var released = await files.ReadAsync(root, None);
            var releaseDeadline = Stopwatch.StartNew();
            while (Failed(released, ErrorCode.Conflict) && releaseDeadline.Elapsed < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(10);
                released = await files.ReadAsync(root, None);
            }
            Value(released);
            if (point == "committed") check(!Value(await files.ReadAsync(root, None)).IsReadOnly, "committed crash is cleaned at next read");
            else await Recover(root, files, "crash " + point);
        }

        root = NewRoot();
        files = new MarkdownCatalogFiles();
        initial = Value(await files.ReadAsync(root, None));
        var movedOutside = Path.Combine(Path.GetDirectoryName(root)!, "external.md");
        File.Move(Path.Combine(root, "_catalog.md"), movedOutside);
        check(Failed(await files.ApplyAsync(root, initial.Revision, [Replace()], None), ErrorCode.Conflict)
            && File.ReadAllText(movedOutside) == Metadata, "external cross-directory move conflicts without modifying moved object");

        root = NewRoot();
        files = new MarkdownCatalogFiles();
        initial = Value(await files.ReadAsync(root, None));
        File.WriteAllText(CatalogRecoveryStore.ContainerFor(root), "occupied service destination");
        check(await files.ApplyAsync(root, initial.Revision, [Replace()], None) is OperationResult<CatalogSnapshot>.Failure
            && File.ReadAllText(Path.Combine(root, "_catalog.md")) == Metadata, "preparation collision leaves original untouched");

        root = NewRoot();
        using (var lease = new RecoveryLease(root, true))
        using (var tree = new CatalogTree(root, null, None))
        using (var journal = new RecoveryJournal(lease, tree.Capture())) { }
        var malformedJournal = Directory.GetDirectories(CatalogRecoveryStore.ContainerFor(root)).Single();
        var manifestPath = Path.Combine(malformedJournal, "manifest.json");
        var validManifest = File.ReadAllText(manifestPath);
        foreach (var invalidManifest in new[] { "{", validManifest.Replace("_catalog.md", "../escape.md"), validManifest.Replace("\"Version\":1", "\"Version\":99") })
        {
            File.WriteAllText(manifestPath, invalidManifest);
            check(Failed(await files.FindAsync(root, None), ErrorCode.MalformedContent)
                && File.ReadAllText(Path.Combine(root, "_catalog.md")) == Metadata, "invalid journal syntax path or version is retained and rejected");
        }

        root = NewRoot();
        files = new MarkdownCatalogFiles();
        initial = Value(await files.ReadAsync(root, None));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try { await files.ApplyAsync(root, initial.Revision, [Replace()], canceled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { check(File.ReadAllText(Path.Combine(root, "_catalog.md")) == Metadata, "cancellation before mutation changes nothing"); }
        using var lateCancellation = new CancellationTokenSource();
        files = new MarkdownCatalogFiles(point => { if (point == "partial") lateCancellation.Cancel(); });
        initial = Value(await files.ReadAsync(root, None));
        check(await files.ApplyAsync(root, initial.Revision, [Replace()], lateCancellation.Token) is OperationResult<CatalogSnapshot>.Success,
            "cancellation after mutation returns committed outcome");

        root = NewRoot();
        using (var lease = new RecoveryLease(root, true))
        using (var tree = new CatalogTree(root, null, None))
        using (var journal = new RecoveryJournal(lease, tree.Capture())) { journal.Complete(); }
        var completedDirectory = Directory.GetDirectories(CatalogRecoveryStore.ContainerFor(root)).Single();
        File.SetAttributes(Path.Combine(completedDirectory, "backup-00000000"), FileAttributes.ReadOnly);
        files = new MarkdownCatalogFiles();
        var cleanupWarning = Value(await files.ReadAsync(root, None));
        check(!cleanupWarning.IsReadOnly && cleanupWarning.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryCleanupFailed), "cleanup failure is a separate nonblocking warning");
        completedDirectory = Directory.GetDirectories(CatalogRecoveryStore.ContainerFor(root)).Single();
        File.SetAttributes(Path.Combine(completedDirectory, "backup-00000000"), FileAttributes.Normal);
        File.Delete(Path.Combine(completedDirectory, "backup-00000000"));
        check(Value(await files.ReadAsync(root, None)).Diagnostics.Count == 0
            && !Directory.EnumerateDirectories(CatalogRecoveryStore.ContainerFor(root)).Any(), "partially completed cleanup resumes at next open");

        root = NewRoot();
        using (var lease = new RecoveryLease(root, true))
        using (var tree = new CatalogTree(root, null, None))
        using (var journal = new RecoveryJournal(lease, tree.Capture())) { }
        var recovery = Value(await files.FindAsync(root, None)).Single();
        var deniedDirectory = Directory.CreateDirectory(Path.Combine(Base, "denied-copy"));
        var descriptor = deniedDirectory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        try
        {
            var acl = deniedDirectory.GetAccessControl(AccessControlSections.Access);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateDirectories, AccessControlType.Deny));
            deniedDirectory.SetAccessControl(acl);
            check(Failed(await files.SaveCopyAsync(root, recovery, deniedDirectory.FullName, None), ErrorCode.AccessDenied), "recovery destination ACL denial preserves source and journal");
        }
        finally
        {
            var acl = new DirectorySecurity();
            acl.SetSecurityDescriptorSddlForm(descriptor, AccessControlSections.Access);
            deniedDirectory.SetAccessControl(acl);
        }
        check(File.ReadAllText(Path.Combine(root, "_catalog.md")) == Metadata
            && Value(await files.FindAsync(root, None)).Count == 1, "denied recovery preserves original and recoverable operation");

        root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "Topic"));
        File.WriteAllText(Path.Combine(root, "Topic", "_topic.md"), Topic);
        files = new MarkdownCatalogFiles(stage =>
        {
            if (stage == "locked") File.WriteAllText(Path.Combine(root, "Topic", "occupied.md"), MarkdownCodecChecks.Full);
        });
        initial = Value(await files.ReadAsync(root, None));
        check(await files.ApplyAsync(root, initial.Revision,
            [new FileChange.CreateDocument(new CatalogDocument("Topic/occupied.md", MarkdownCodecChecks.Full + "changed"))], None)
            is OperationResult<CatalogSnapshot>.Failure
            && File.ReadAllText(Path.Combine(root, "Topic", "occupied.md")) == MarkdownCodecChecks.Full,
            "destination occupied after validation is never overwritten");

        root = NewRoot();
        files = new MarkdownCatalogFiles();
        initial = Value(await files.ReadAsync(root, None));
        var relocatedRoot = root + "-original";
        Directory.Move(root, relocatedRoot);
        var junction = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        junction.ArgumentList.Add("/c");
        junction.ArgumentList.Add("mklink");
        junction.ArgumentList.Add("/J");
        junction.ArgumentList.Add(root);
        junction.ArgumentList.Add(relocatedRoot);
        using (var process = Process.Start(junction)!)
        {
            if (!process.WaitForExit(3000) || process.ExitCode != 0) throw new InvalidOperationException("Synthetic junction creation failed");
        }
        check(Failed(await files.ApplyAsync(root, initial.Revision, [Replace()], None), ErrorCode.InvalidInput)
            && File.ReadAllText(Path.Combine(relocatedRoot, "_catalog.md")) == Metadata, "reparse replacement of root is rejected without modifying its target");

        async Task Stale(string name, Action<string> alter)
        {
            var path = NewRoot();
            var adapter = new MarkdownCatalogFiles();
            var before = Value(await adapter.ReadAsync(path, None));
            alter(Path.Combine(path, "_catalog.md"));
            check(Failed(await adapter.ApplyAsync(path, before.Revision, [Replace()], None), ErrorCode.Conflict), "revision detects " + name);
        }

        async Task Recover(string path, MarkdownCatalogFiles adapter, string label)
        {
            var snapshot = Value(await adapter.ReadAsync(path, None));
            check(snapshot.IsReadOnly && snapshot.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired), label + " blocks writes");
            var pending = Value(await adapter.FindAsync(path, None));
            check(pending.Count == 1, label + " exposes opaque recovery identifier");
            File.WriteAllText(Path.Combine(path, "_catalog.md"), Metadata + "\n\n");
            var destination = Path.Combine(Base, "copies");
            Directory.CreateDirectory(destination);
            var saved = Value(await adapter.SaveCopyAsync(path, pending[0], destination, None));
            check(File.ReadAllText(Path.Combine(saved, "_catalog.md")) == Metadata
                && File.ReadAllText(Path.Combine(path, "_catalog.md")) == Metadata + "\n\n", label + " separate recovery preserves later edits");
            var second = Value(await adapter.SaveCopyAsync(path, pending[0], destination, None));
            check(second != saved && Directory.Exists(saved), label + " repeated recovery uses unique folders");
            check(Failed(await adapter.SaveCopyAsync(path, pending[0], path, None), ErrorCode.InvalidInput), label + " rejects recovery into active root");
            var journal = Directory.GetDirectories(CatalogRecoveryStore.ContainerFor(path)).Single();
            File.AppendAllText(Path.Combine(journal, "backup-00000000"), "tampered");
            check(Failed(await adapter.SaveCopyAsync(path, pending[0], destination, None), ErrorCode.MalformedContent), label + " rejects damaged backup checksum");
        }
    }

    private static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(StorageWriteChecks).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    internal static async Task<int> ChildAsync(string[] arguments)
    {
        var root = arguments[1];
        if (arguments[0] == "--storage-contend")
        {
            var blocked = 0;
            foreach (var action in new Action[] {
                () => File.WriteAllText(Path.Combine(root, "_catalog.md"), "external"),
                () => File.Move(Path.Combine(root, "_catalog.md"), Path.Combine(root, "renamed.md")),
                () => Directory.Move(root, root + "-moved"),
                () => File.WriteAllText(arguments[2], "external") })
            {
                try { action(); }
                catch (IOException) { blocked++; }
                catch (UnauthorizedAccessException) { blocked++; }
            }
            return blocked == 4 ? 0 : 1;
        }
        var files = new MarkdownCatalogFiles(point =>
        {
            if (point != arguments[2]) return;
            Console.WriteLine(point);
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        });
        var initial = Value(await files.ReadAsync(root, None));
        await files.ApplyAsync(root, initial.Revision, [Replace(Metadata.Replace("\n", "\r\n"))], None);
        return 1;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, IntPtr attributes);
}
