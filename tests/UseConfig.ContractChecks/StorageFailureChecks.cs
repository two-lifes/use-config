using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class StorageFailureChecks
{
    private const string Metadata = "---\nformat_version: 1\n---\n";
    private const string FirstId = "33333333-3333-4333-8333-333333333333";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly string Base = Path.Combine(AppContext.BaseDirectory, "failure-checks", Guid.NewGuid().ToString("N"));
    private static readonly string[] Topics = ["Тема", "Other", "Пустая", "Удалить"];
    private static string Id(int value) => $"{value}2222222-2222-4222-8222-222222222222";
    private static string Topic(int value) => $"---\nformat_version: 1\nid: \"{Id(value)}\"\n---\n";
    private static string Entry(int value) => MarkdownCodecChecks.Full.Replace(FirstId, Id(value));
    private static string Edited => Entry(6) + "Saved 日本語\n";

    private static Dictionary<string, byte[]> InitialFiles()
    {
        var result = new Dictionary<string, byte[]> { ["_catalog.md"] = Encoding.UTF8.GetBytes(Metadata) };
        for (var index = 0; index < Topics.Length; index++) result.Add(Topics[index] + "/_topic.md", Encoding.UTF8.GetBytes(Topic(index + 1)));
        result.Add("Тема/example.md", Encoding.UTF8.GetBytes(MarkdownCodecChecks.Full));
        result.Add("Other/edit.md", Encoding.UTF8.GetBytes(Entry(6)));
        result.Add("Other/discard.md", Encoding.UTF8.GetBytes(Entry(7)));
        return result;
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Base, Guid.NewGuid().ToString("N"), "Каталог 日本語");
        foreach (var topic in Topics) Directory.CreateDirectory(Path.Combine(root, topic));
        foreach (var pair in InitialFiles()) File.WriteAllBytes(Path.Combine(root, pair.Key), pair.Value);
        return root;
    }

    private sealed record Tree(Dictionary<string, byte[]> Files, HashSet<string> Directories);
    private static Tree Capture(string root) => new(
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Relative(root, path), File.ReadAllBytes),
        Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Select(path => Relative(root, path)).ToHashSet());
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool Same(Tree before, Tree after) => before.Directories.SetEquals(after.Directories)
        && before.Files.Count == after.Files.Count && before.Files.All(pair =>
            after.Files.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes));
    private static Tree Expected()
    {
        var files = InitialFiles();
        files.Add("Новая/_topic.md", Encoding.UTF8.GetBytes(Topic(5)));
        files.Add("Новая/new.md", Encoding.UTF8.GetBytes(Entry(8)));
        files["Other/edit.md"] = Encoding.UTF8.GetBytes(Edited);
        files.Add("Other/example.md", files["Тема/example.md"]);
        files.Remove("Тема/example.md");
        files.Remove("Other/discard.md");
        files.Remove("Удалить/_topic.md");
        return new(files, ["Тема", "Other", "Пустая", "Новая"]);
    }

    private static FileChange[] Changes() => [
        new FileChange.CreateDirectory("Новая"),
        new FileChange.CreateDocument(new("Новая/_topic.md", Topic(5))),
        new FileChange.CreateDocument(new("Новая/new.md", Entry(8))),
        new FileChange.ReplaceDocument(new("Other/edit.md", Edited)),
        new FileChange.Move("Тема/example.md", "Other/example.md"),
        new FileChange.Delete("Other/discard.md"), new FileChange.Delete("Удалить")];
    private static FileChange[] Replace() => [new FileChange.ReplaceDocument(new("Other/edit.md", Edited))];
    private static T Value<T>(OperationResult<T> result)
    {
        if (result is OperationResult<T>.Success success) return success.Value;
        Console.Error.WriteLine("Unexpected storage category: " + ((OperationResult<T>.Failure)result).Code);
        throw new InvalidOperationException("Unexpected storage result");
    }
    private static bool Failed<T>(OperationResult<T> result, ErrorCode code) => result is OperationResult<T>.Failure failure && failure.Code == code;
    private static IOException DiskFull(int code) => new("Synthetic disk-full failure", unchecked((int)(0x80070000u | (uint)code)));

    [SupportedOSPlatform("windows")]
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var root = NewRoot();
        var files = new MarkdownCatalogFiles();
        var initial = Value(await files.ReadAsync(root, None));
        Value(await files.ApplyAsync(root, initial.Revision, Changes(), None));
        check(Same(Expected(), Capture(root)), "mixed save preserves exact expected bytes and empty topics");
        Value(await new CatalogSettings(Path.Combine(Path.GetDirectoryName(root)!, "settings")).RememberAsync(root, None));
        using (var child = Start("--stage3-reopen", root))
        {
            try
            {
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                check(child.ExitCode == 0 && (await child.StandardOutput.ReadToEndAsync()).Trim() == "reopened",
                    "new process reopens remembered catalog with all fields and topic relationships");
            }
            finally
            {
                if (!child.HasExited) child.Kill();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        foreach (var (stage, code) in new[] { ("journal-backup-partial", 112), ("journal-manifest-partial", 39), ("partial", 112), ("partial", 39) })
        {
            root = NewRoot();
            var before = Capture(root);
            files = new MarkdownCatalogFiles(point => { if (point == stage) throw DiskFull(code); });
            initial = Value(await files.ReadAsync(root, None));
            check(Failed(await files.ApplyAsync(root, initial.Revision, Replace(), None), ErrorCode.InsufficientSpace)
                && Same(before, Capture(root)), stage + " disk-full preserves entire original tree");
            var reopened = Value(await new MarkdownCatalogFiles().ReadAsync(root, None));
            if (stage.StartsWith("journal-", StringComparison.Ordinal))
            {
                check(reopened.IsReadOnly && reopened.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired),
                    stage + " incomplete preparation is diagnosed without modifying source");
                check(Failed(await new MarkdownCatalogFiles().FindAsync(root, None), ErrorCode.MalformedContent),
                    stage + " incomplete journal is never offered as a recovery copy");
            }
            else check(!reopened.IsReadOnly && reopened.Diagnostics.Count == 0, "disk-full rollback reopens cleanly: " + code);
        }

        root = NewRoot();
        var original = Capture(root);
        files = new MarkdownCatalogFiles(point => { if (point is "partial" or "rollback-partial") throw DiskFull(112); });
        initial = Value(await files.ReadAsync(root, None));
        check(Failed(await files.ApplyAsync(root, initial.Revision, Replace(), None), ErrorCode.RecoveryRequired),
            "disk-full during rollback requires recovery instead of claiming save success");
        await Recover(root, original, check, "disk-full rollback");

        root = NewRoot();
        original = Capture(root);
        await Crash(root, "partial", 1);
        files = new MarkdownCatalogFiles(point => { if (point == "recovery-copy-partial") throw DiskFull(112); });
        await ReadAfterExit(files, root);
        var recovery = Value(await files.FindAsync(root, None)).Single();
        var beforeRecovery = Capture(root);
        var serviceBefore = Capture(CatalogRecoveryStore.ContainerFor(root));
        var destination = Directory.CreateDirectory(Path.Combine(Base, "copy-failure")).FullName;
        check(Failed(await files.SaveCopyAsync(root, recovery, destination, None), ErrorCode.InsufficientSpace)
            && Same(beforeRecovery, Capture(root)) && Same(serviceBefore, Capture(CatalogRecoveryStore.ContainerFor(root))),
            "partial recovery disk-full preserves source and service copy without returning success");
        check(Directory.GetDirectories(destination).Length == 1, "failed recovery retains its partial output for inspection");
        await Recover(root, original, check, "recovery retry after disk-full");

        foreach (var stage in new[] { "journal-backup-partial", "journal-manifest-partial" })
        {
            root = NewRoot();
            original = Capture(root);
            await Crash(root, stage, 1);
            files = new MarkdownCatalogFiles();
            var snapshot = await ReadAfterExit(files, root);
            check(Same(original, Capture(root)) && snapshot.IsReadOnly
                && snapshot.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired), stage + " process interruption preserves source");
            check(Failed(await files.FindAsync(root, None), ErrorCode.MalformedContent), stage + " killed preparation exposes no invalid backup");
        }
        foreach (var occurrence in new[] { 1, 3, 4, 5, 6 })
        {
            root = NewRoot();
            original = Capture(root);
            await Crash(root, "operation-applied", occurrence);
            await Recover(root, original, check, "mixed-operation interruption " + occurrence);
        }
        foreach (var occurrence in new[] { 1, 8, 12 })
        {
            root = NewRoot();
            await Crash(root, "cleanup-child-deleted", occurrence);
            files = new MarkdownCatalogFiles();
            var snapshot = await ReadAfterExit(files, root);
            check(Same(Expected(), Capture(root)) && !snapshot.IsReadOnly && snapshot.Diagnostics.Count == 0,
                "cleanup process interruption preserves committed result: " + occurrence);
            check(!Directory.EnumerateDirectories(CatalogRecoveryStore.ContainerFor(root)).Any(), "cleanup resumes after process exit: " + occurrence);
        }

        await AccessDenied(check);
        await RejectedTrees(check);
    }

    private static async Task Recover(string root, Tree original, Action<bool, string> check, string label)
    {
        var files = new MarkdownCatalogFiles();
        var snapshot = await ReadAfterExit(files, root);
        check(snapshot.IsReadOnly && snapshot.Diagnostics.Any(item => item.Code == ErrorCode.RecoveryRequired), label + " blocks writes");
        var before = Capture(root);
        var pending = Value(await files.FindAsync(root, None)).Single();
        var destination = Directory.CreateDirectory(Path.Combine(Base, "copies")).FullName;
        var copy = Value(await files.SaveCopyAsync(root, pending, destination, None));
        check(Same(original, Capture(copy)) && Same(before, Capture(root)), label + " restores complete previous tree separately");
        check(!Value(await new MarkdownCatalogFiles().ReadAsync(copy, None)).IsReadOnly, label + " recovery copy reopens validly");
    }

    [SupportedOSPlatform("windows")]
    private static async Task AccessDenied(Action<bool, string> check)
    {
        foreach (var preparation in new[] { false, true })
        {
            var root = NewRoot();
            var before = Capture(root);
            // Create only the service container/lease; no journal exists yet.
            if (preparation) { using var lease = new RecoveryLease(root, true); }
            var files = new MarkdownCatalogFiles();
            var initial = Value(await files.ReadAsync(root, None));
            FileSystemInfo target = preparation ? new DirectoryInfo(CatalogRecoveryStore.ContainerFor(root)) : new FileInfo(Path.Combine(root, "Other", "edit.md"));
            var saved = preparation ? ((DirectoryInfo)target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : ((FileInfo)target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            using var identity = WindowsIdentity.GetCurrent();
            FileSystemSecurity denied = preparation ? new DirectorySecurity() : new FileSecurity();
            denied.SetSecurityDescriptorSddlForm(saved, AccessControlSections.Access);
            denied.AddAccessRule(new FileSystemAccessRule(identity.User!, preparation ? FileSystemRights.CreateDirectories : FileSystemRights.WriteData, AccessControlType.Deny));
            try
            {
                SetAcl(target, denied);
                check(Failed(await files.ApplyAsync(root, initial.Revision, Replace(), None), ErrorCode.AccessDenied)
                    && Same(before, Capture(root)), "real write ACL denial preserves full catalog: " + preparation);
            }
            finally
            {
                FileSystemSecurity restored = preparation ? new DirectorySecurity() : new FileSecurity();
                restored.SetSecurityDescriptorSddlForm(saved, AccessControlSections.Access);
                SetAcl(target, restored);
            }
            var actual = preparation ? ((DirectoryInfo)target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access)
                : ((FileInfo)target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            check(actual == saved && Same(before, Capture(root)), "write ACL is restored exactly: " + preparation);
            files = new MarkdownCatalogFiles();
            initial = Value(await files.ReadAsync(root, None));
            check(!initial.IsReadOnly && await files.ApplyAsync(root, initial.Revision, Replace(), None) is OperationResult<CatalogSnapshot>.Success,
                "write succeeds after ACL restoration: " + preparation);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetAcl(FileSystemInfo target, FileSystemSecurity security)
    {
        if (target is DirectoryInfo directory) directory.SetAccessControl((DirectorySecurity)security);
        else ((FileInfo)target).SetAccessControl((FileSecurity)security);
    }

    private static async Task RejectedTrees(Action<bool, string> check)
    {
        foreach (var invalid in new[] { "malformed", "unsupported", "unknown", "duplicate" })
        {
            var root = NewRoot();
            if (invalid == "malformed") File.AppendAllText(Path.Combine(root, "_catalog.md"), "unexpected");
            if (invalid == "unsupported") File.WriteAllText(Path.Combine(root, "_catalog.md"), Metadata.Replace(": 1", ": 999"));
            if (invalid == "unknown") File.WriteAllText(Path.Combine(root, "Other", "unknown.txt"), "synthetic unknown data");
            if (invalid == "duplicate") File.WriteAllText(Path.Combine(root, "Other", "duplicate.md"), MarkdownCodecChecks.Full);
            var before = Capture(root);
            var files = new MarkdownCatalogFiles();
            var initial = Value(await files.ReadAsync(root, None));
            var result = await files.ApplyAsync(root, initial.Revision, Replace(), None);
            check(initial.IsReadOnly && Failed(result, ErrorCode.ReadOnly) && Same(before, Capture(root)),
                invalid + " rejection preserves every source path and byte");
        }
        foreach (var change in new[] { "edit", "rename", "move", "delete" })
        {
            var root = NewRoot();
            var files = new MarkdownCatalogFiles();
            var initial = Value(await files.ReadAsync(root, None));
            var entry = Path.Combine(root, "Other", "edit.md");
            if (change == "edit") File.AppendAllText(entry, "External 日本語\n");
            if (change == "rename") File.Move(entry, Path.Combine(root, "Other", "renamed.md"));
            if (change == "move") File.Move(entry, Path.Combine(root, "Пустая", "moved.md"));
            if (change == "delete") File.Delete(entry);
            var before = Capture(root);
            check(Failed(await files.ApplyAsync(root, initial.Revision, Replace(), None), ErrorCode.Conflict) && Same(before, Capture(root)),
                "external " + change + " preserves entire changed tree and never recreates stale entries");
        }
    }

    private static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(StorageFailureChecks).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static async Task Crash(string root, string point, int occurrence)
    {
        using var child = Start("--stage3-crash", root, point, occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var reached = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (reached != point) throw new InvalidOperationException("Child failed to reach checkpoint " + point);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<CatalogSnapshot> ReadAfterExit(MarkdownCatalogFiles files, string root)
    {
        // Windows may signal process exit before releasing its file objects; persistent conflicts still fail.
        var timer = Stopwatch.StartNew();
        while (true)
        {
            var result = await files.ReadAsync(root, None);
            if (!Failed(result, ErrorCode.Conflict) || timer.Elapsed > TimeSpan.FromSeconds(2)) return Value(result);
            await Task.Delay(10);
        }
    }

    internal static async Task<int> ChildAsync(string[] args)
    {
        if (args[0] == "--stage3-fixture") { Console.WriteLine(NewRoot()); return 0; }
        var root = args[1];
        if (args[0] == "--stage3-reopen")
        {
            var settings = new CatalogSettings(Path.Combine(Path.GetDirectoryName(root)!, "settings"));
            var session = new CatalogSession(new MarkdownCatalogLocation(Path.Combine(Path.GetDirectoryName(root)!, "settings")), settings);
            Value(await session.RestoreAsync(None));
            if (session.CurrentPath != root || session.Current is not { IsReadOnly: false } loaded || !Same(Expected(), Capture(root))) return 2;
            if (loaded.Folders.Count != 4 || loaded.Entries.Count != 3 || !loaded.Folders.Any(folder => folder.Name == "Пустая")) return 3;
            foreach (var entry in loaded.Entries)
            {
                var edited = entry.Id == Guid.Parse(Id(6));
                var expectedFolder = entry.Id == Guid.Parse(Id(8)) ? Id(5) : Id(2);
                if (entry.FolderId != Guid.Parse(expectedFolder) || entry.FormatVersion != 1
                    || entry.Name != "Пример 日本語 😀" || entry.Url != "https://example.com/" || entry.Username != "demo'user"
                    || entry.Password != "  demo:\"quote\"\\path#123  " || entry.AdditionalUrl != "http://example.org/mail"
                    || entry.TwoFactorNote != "First line\nВторая строка" || entry.Description != "# Описание\n\n日本語 😀\n\n" + (edited ? "Saved 日本語\n" : "")) return 4;
            }
            Console.WriteLine("reopened");
            return 0;
        }
        var hits = 0;
        var files = new MarkdownCatalogFiles(point =>
        {
            if (point != args[2] || ++hits != int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture)) return;
            Console.WriteLine(point);
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        });
        var initial = Value(await files.ReadAsync(root, None));
        Value(await files.ApplyAsync(root, initial.Revision, Changes(), None));
        return 1;
    }
}
