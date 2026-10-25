using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace UseConfig.StorageProbe;

internal static class Program
{
    internal const string EntryRelative = "Тема/example.md";
    internal const string Original = "---\nformat_version: 1\nid: \"33333333-3333-4333-8333-333333333333\"\nname: \"Example\"\nurl: \"https://example.com/\"\n---\n";
    internal const string Topic = "---\nformat_version: 1\nid: \"22222222-2222-4222-8222-222222222222\"\n---\n";
    private static int passed;
    private static int failed;
    private static string runRoot = "";

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("UNSUPPORTED: Windows is required.");
            return 1;
        }
        if (args.Length > 0 && args[0] == "worker") return Worker(args);
        // Keep all fixtures beneath the ignored build tree; never accept a user catalog path.
        runRoot = Path.Combine(AppContext.BaseDirectory, "probe-runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runRoot);
        Console.WriteLine($"ENV Windows={Environment.OSVersion.Version} runtime={Environment.Version} x64={Environment.Is64BitProcess} fs={new DriveInfo(Path.GetPathRoot(runRoot)!).DriveFormat}");
        CodecCases.Run(Test);
        CatalogCases();
        FileCases();
        LockedWriteCases.Run(Test, runRoot);
        ArchiveCases();
        Console.WriteLine($"RESULT passed={passed} failed={failed}");
        return failed != 0 ? 1 : 0;
    }

    internal static void Require(bool condition)
    {
        if (!condition) throw new ProbeException("assertion");
    }

    internal static void Reject(Action action, string? expected = null)
    {
        try { action(); }
        catch (ProbeException ex) when (expected == null || ex.Message == expected) { return; }
        throw new ProbeException("expected-rejection");
    }

    private static void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            failed++;
            // Never print exception messages, field values, or user path names.
            var code = ex is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : ex.HResult & 0xFFFF;
            Console.WriteLine("FAIL " + name + " [" + ex.GetType().Name + "; code=" + code + "]");
        }
    }

    private static string Fixture()
    {
        var root = Path.Combine(runRoot, Guid.NewGuid().ToString("N"), "catalog");
        Directory.CreateDirectory(Path.Combine(root, "Тема"));
        File.WriteAllText(Path.Combine(root, "_catalog.md"), "---\nformat_version: 1\n---\n", MarkdownCodec.Utf8);
        File.WriteAllText(Path.Combine(root, "Тема", "_topic.md"), Topic, MarkdownCodec.Utf8);
        File.WriteAllText(Path.Combine(root, EntryRelative), Original, MarkdownCodec.Utf8);
        return root;
    }

    private static string Beside(string root, string name) => Path.Combine(Path.GetDirectoryName(root)!, name);
    private static byte[] Edited(string name) => MarkdownCodec.Utf8.GetBytes(Original.Replace("Example", name, StringComparison.Ordinal));

    private static void CatalogCases()
    {
        Test("catalog-empty", () =>
        {
            var root = Path.Combine(runRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "_catalog.md"), "---\nformat_version: 1\n---\n");
            Require(CatalogFiles.Read(root).Files.Count == 1);
        });
        var mutations = new (string Name, Action<string> Change)[]
        {
            ("duplicate-id", r => File.Copy(Path.Combine(r, EntryRelative), Path.Combine(r, "Тема", "copy.md"))),
            ("missing-metadata", r => File.Move(Path.Combine(r, "Тема", "_topic.md"), Beside(r, "metadata.md"))),
            ("extra-file", r => File.WriteAllText(Path.Combine(r, "Тема", "notes.txt"), "synthetic")),
            ("nested-topic", r => Directory.CreateDirectory(Path.Combine(r, "Тема", "nested"))),
            ("unsupported-version", r => File.WriteAllText(Path.Combine(r, EntryRelative), Original.Replace("version: 1", "version: 2"))),
            ("root-entry", r => File.Copy(Path.Combine(r, EntryRelative), Path.Combine(r, "extra.md")))
        };
        foreach (var (name, change) in mutations)
        {
            Test("catalog-reject-" + name, () =>
            {
                var root = Fixture();
                change(root);
                var before = RawFiles(root);
                Reject(() => CatalogFiles.Read(root));
                Require(before.SameAs(RawFiles(root)));
                Reject(() => CatalogFiles.Export(root, Beside(root, "invalid.zip")));
                Require(!File.Exists(Beside(root, "invalid.zip")));
            });
        }
    }

    private static CatalogSnapshot RawFiles(string root) => new(new SortedDictionary<string, byte[]>(
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p), File.ReadAllBytes), StringComparer.Ordinal));

    private static void FileCases()
    {
        Test("locked-save-success-and-backup", () =>
        {
            var root = Fixture();
            var snapshot = CatalogFiles.Read(root);
            var stage = Beside(root, "stage");
            LockedFileWriter.Save(root, EntryRelative, snapshot, Edited("Saved"), stage);
            Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(Edited("Saved")));
            Require(File.ReadAllBytes(Path.Combine(stage, "previous.md")).AsSpan().SequenceEqual(snapshot.Files[EntryRelative]));
        });
        foreach (var mode in new[] { "write", "rename", "move", "delete", "metadata" })
        {
            Test("external-before-save-" + mode, () =>
            {
                var root = Fixture();
                var snapshot = CatalogFiles.Read(root);
                var path = Path.Combine(root, EntryRelative);
                switch (mode)
                {
                    case "write":
                        var stamp = File.GetLastWriteTimeUtc(path);
                        File.WriteAllBytes(path, Edited("Changed"));
                        File.SetLastWriteTimeUtc(path, stamp);
                        break;
                    case "rename": File.Move(path, Path.Combine(root, "Тема", "renamed.md")); break;
                    case "move":
                        AddEmptyTopic(root);
                        snapshot = CatalogFiles.Read(root);
                        File.Move(path, Path.Combine(root, "Empty", "moved.md"));
                        break;
                    case "delete": File.Move(path, Beside(root, "removed.md")); break;
                    case "metadata": File.WriteAllText(Path.Combine(root, "Тема", "_topic.md"), Topic.Replace("22222222", "55555555")); break;
                }
                var external = RawFiles(root);
                Reject(() => LockedFileWriter.Save(root, EntryRelative, snapshot, Edited("Saved"), Beside(root, "stage")), "external-conflict");
                Require(external.SameAs(RawFiles(root)));
            });
        }
        Test("sharing-violation-preserves-original", () =>
        {
            var root = Fixture();
            var snapshot = CatalogFiles.Read(root);
            using (var hold = new FileStream(Path.Combine(root, EntryRelative), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    LockedFileWriter.Save(root, EntryRelative, snapshot, Edited("Saved"), Beside(root, "stage"));
                    throw new ProbeException("expected-io-error");
                }
                catch (IOException) { Require(snapshot.SameAs(CatalogFiles.Read(root))); }
            }
        });
        Test("read-only-write-refusal", () =>
        {
            var root = Fixture();
            var path = Path.Combine(root, EntryRelative);
            var snapshot = CatalogFiles.Read(root);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                try
                {
                    LockedFileWriter.Save(root, EntryRelative, snapshot, Edited("Saved"), Beside(root, "stage"));
                    throw new ProbeException("expected-write-refusal");
                }
                catch (UnauthorizedAccessException) { Require(snapshot.SameAs(CatalogFiles.Read(root))); }
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });
        foreach (var mode in new[] { "write", "editor-replace" })
        {
            Test("unsafe-negative-control-" + mode, () =>
            {
                var root = Fixture();
                var snapshot = CatalogFiles.Read(root);
                var stage = Beside(root, "stage");
                Reject(() => CatalogFiles.ReplaceCandidate(root, EntryRelative, snapshot, Edited("Saved"), stage,
                    () => RunWorker(mode, root)), "conflict-after-replace");
                Require(File.ReadAllBytes(Path.Combine(stage, "previous.md")).AsSpan().SequenceEqual(Edited("External")));
                Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(Edited("Saved")));
            });
        }
        foreach (var mode in new[] { "write", "editor-replace", "rename", "delete", "topic-rename", "root-rename", "metadata-write", "rootmeta-write" })
        {
            Test("locked-cross-process-" + mode, () =>
            {
                var root = Fixture();
                var before = CatalogFiles.Read(root);
                LockedFileWriter.Save(root, EntryRelative, before, Edited("Saved"), Beside(root, "stage"), phase =>
                {
                    if (phase == "locked") RunWorker("blocked-" + mode, root, "BLOCKED");
                });
                Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(Edited("Saved")));
                if (mode is "write" or "editor-replace")
                {
                    RunWorker(mode, root);
                    var external = CatalogFiles.Read(root);
                    Require(external.Files[EntryRelative].AsSpan().SequenceEqual(Edited("External")));
                    Reject(() => LockedFileWriter.Save(root, EntryRelative, before, Edited("Stale"), Beside(root, "stale-stage")), "external-conflict");
                    Require(external.SameAs(CatalogFiles.Read(root)));
                }
                if (mode is "topic-rename" or "root-rename")
                {
                    RunWorker("after-unlock-" + mode, root);
                    var movedEntry = mode == "topic-rename" ? Path.Combine(root, "Renamed", "example.md")
                        : Path.Combine(Beside(root, "moved-catalog"), EntryRelative);
                    Require(File.ReadAllBytes(movedEntry).AsSpan().SequenceEqual(Edited("Saved")));
                }
            });
        }
        foreach (var mode in new[] { "before", "after" })
        {
            Test("process-interruption-" + mode, () =>
            {
                var root = Fixture();
                using var child = StartWorker("crash-" + mode, root);
                Signal(child, "READY");
                child.StandardInput.WriteLine("GO");
                Signal(child, "PAUSED");
                child.Kill();
                Require(child.WaitForExit(10000));
                var expected = mode == "before" ? Edited("Example") : Edited("Saved");
                Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(expected));
                _ = CatalogFiles.Read(root);
                if (mode == "after") Require(File.ReadAllBytes(Beside(root, "worker-backup.md")).AsSpan().SequenceEqual(Edited("Example")));
            });
        }
        foreach (var phase in new[] { "prepared", "partial", "written" })
        {
            Test("locked-process-interruption-" + phase, () =>
            {
                var root = Fixture();
                using var child = StartWorker("locked-crash-" + phase, root);
                Signal(child, "READY");
                child.StandardInput.WriteLine("GO");
                Signal(child, "PAUSED");
                child.Kill();
                Require(child.WaitForExit(10000));
                var bytesAtCrash = File.ReadAllBytes(Path.Combine(root, EntryRelative));
                if (phase == "prepared") Require(bytesAtCrash.AsSpan().SequenceEqual(Edited("Example")));
                if (phase == "partial") Require(!bytesAtCrash.AsSpan().SequenceEqual(Edited("Example"))
                    && !bytesAtCrash.AsSpan().SequenceEqual(Edited("Saved")));
                if (phase == "written") Require(bytesAtCrash.AsSpan().SequenceEqual(Edited("Saved")));
                var stage = Beside(root, "locked-stage");
                var recovered = Beside(root, "recovered.md");
                LockedFileWriter.RecoverToNewFile(stage, recovered);
                Require(File.ReadAllBytes(recovered).AsSpan().SequenceEqual(Edited("Example")));
                // A later editor save must survive recovery, even after a partial write.
                RunWorker("write", root);
                LockedFileWriter.RecoverToNewFile(stage, Beside(root, "recovered-again.md"));
                Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(Edited("External")));
            });
        }
    }

    private static void AddEmptyTopic(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "Empty"));
        File.WriteAllText(Path.Combine(root, "Empty", "_topic.md"), Topic.Replace("22222222", "66666666"));
    }

    private static void ArchiveCases()
    {
        Test("zip-roundtrip-unicode-empty-topic-all-fields", () =>
        {
            var root = Fixture();
            AddEmptyTopic(root);
            File.WriteAllBytes(Path.Combine(root, EntryRelative), MarkdownCodec.Utf8.GetBytes(CodecCases.Full));
            var original = CatalogFiles.Read(root);
            var zip = Beside(root, "backup.zip");
            CatalogFiles.Export(root, zip);
            var restored = CatalogFiles.Extract(zip, Beside(root, "restored"));
            Require(original.SameAs(restored));
        });
        Test("zip-source-change-refused", () =>
        {
            var root = Fixture();
            var zip = Beside(root, "backup.zip");
            Reject(() => CatalogFiles.Export(root, zip, () => RunWorker("write", root)), "export-conflict");
            Require(!File.Exists(zip));
            Require(File.ReadAllBytes(Path.Combine(root, EntryRelative)).AsSpan().SequenceEqual(Edited("External")));
        });
        foreach (var path in new[] { "../escaped.md", "/absolute.md", "C:/escape.md", "topic/../../escape.md", "topic\\escape.md" })
        {
            Test("zip-unsafe-" + Array.IndexOf(new[] { "../escaped.md", "/absolute.md", "C:/escape.md", "topic/../../escape.md", "topic\\escape.md" }, path), () =>
            {
                var root = Fixture();
                var zip = Beside(root, "unsafe.zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntry(path);
                var destination = Beside(root, "restored");
                Reject(() => CatalogFiles.Extract(zip, destination), "unsafe-archive-path");
                Require(!Directory.Exists(destination));
            });
        }
        Test("zip-truncated-refused", () =>
        {
            var root = Fixture();
            var zip = Beside(root, "broken.zip");
            CatalogFiles.Export(root, zip);
            var bytes = File.ReadAllBytes(zip);
            File.WriteAllBytes(zip, bytes[..(bytes.Length / 2)]);
            try
            {
                CatalogFiles.Extract(zip, Beside(root, "restored"));
                throw new ProbeException("expected-invalid-archive");
            }
            catch (InvalidDataException) { Require(!Directory.Exists(Beside(root, "restored"))); }
        });
        Test("zip-invalid-catalog-refused", () =>
        {
            var root = Fixture();
            var zip = Beside(root, "invalid.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("_catalog.md").Open()))
                writer.Write("---\nformat_version: 2\n---\n");
            var before = CatalogFiles.Read(root);
            Reject(() => CatalogFiles.Extract(zip, Beside(root, "restored")), "unsupported-version");
            Require(before.SameAs(CatalogFiles.Read(root)));
        });
    }

    private static Process StartWorker(string mode, string root)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in new[] { "worker", mode, root }) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new ProbeException("worker-start");
    }

    private static void Signal(Process child, string expected)
    {
        var line = child.StandardOutput.ReadLineAsync();
        if (!line.Wait(TimeSpan.FromSeconds(10)) || line.Result != expected)
        {
            if (!child.HasExited) child.Kill();
            throw new ProbeException("worker-protocol");
        }
    }

    private static void RunWorker(string mode, string root, string response = "DONE")
    {
        using var child = StartWorker(mode, root);
        Signal(child, "READY");
        child.StandardInput.WriteLine("GO");
        Signal(child, response);
        if (!child.WaitForExit(10000)) { child.Kill(); throw new ProbeException("worker-timeout"); }
        Require(child.ExitCode == 0);
    }

    private static int Worker(string[] args)
    {
        try
        {
            if (args.Length != 3) return 1;
            var root = Path.GetFullPath(args[2]);
            var allowed = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "probe-runs")) + Path.DirectorySeparatorChar;
            if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) return 1;
            var mode = args[1];
            var path = Path.Combine(root, EntryRelative);
            Console.WriteLine("READY");
            if (Console.ReadLine() != "GO") return 1;
            if (mode is "after-unlock-topic-rename" or "after-unlock-root-rename")
            {
                if (mode == "after-unlock-topic-rename")
                    Directory.Move(Path.Combine(root, "Тема"), Path.Combine(root, "Renamed"));
                else Directory.Move(root, Beside(root, "moved-catalog"));
                Console.WriteLine("DONE");
                return 0;
            }
            if (mode.StartsWith("locked-crash-", StringComparison.Ordinal))
            {
                LockedFileWriter.Save(root, EntryRelative, CatalogFiles.Read(root), Edited("Saved"), Beside(root, "locked-stage"), phase =>
                {
                    if (phase == mode[13..])
                    {
                        Console.WriteLine("PAUSED");
                        _ = Console.ReadLine();
                        throw new ProbeException("expected-process-termination");
                    }
                });
                return 1;
            }
            if (mode.StartsWith("blocked-", StringComparison.Ordinal))
            {
                try
                {
                    switch (mode[8..])
                    {
                        case "write": File.WriteAllBytes(path, Edited("External")); break;
                        case "editor-replace":
                            var prepared = Beside(root, "blocked-prepared.md");
                            File.WriteAllBytes(prepared, Edited("External"));
                            File.Replace(prepared, path, null);
                            break;
                        case "rename": File.Move(path, Path.Combine(root, "Тема", "renamed.md")); break;
                        case "delete": File.Delete(path); break;
                        case "topic-rename": Directory.Move(Path.Combine(root, "Тема"), Path.Combine(root, "Renamed")); break;
                        case "root-rename": Directory.Move(root, Beside(root, "moved-catalog")); break;
                        case "metadata-write": File.WriteAllText(Path.Combine(root, "Тема", "_topic.md"), Topic.Replace("22222222", "55555555")); break;
                        case "rootmeta-write": File.WriteAllText(Path.Combine(root, "_catalog.md"), "---\nformat_version: 2\n---\n"); break;
                        default: return 1;
                    }
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) == 32
                    || ((ex.HResult & 0xFFFF) == 5 && (mode == "blocked-topic-rename" || mode == "blocked-root-rename")))
                {
                    Console.WriteLine("BLOCKED");
                    return 0;
                }
                Console.WriteLine("UNEXPECTED-WRITE");
                return 1;
            }
            if (mode == "write") File.WriteAllBytes(path, Edited("External"));
            else
            {
                var temp = Beside(root, "worker-prepared.md");
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(Edited(mode == "editor-replace" ? "External" : "Saved"));
                    file.Flush(true);
                }
                if (mode != "crash-before") File.Replace(temp, path, Beside(root, "worker-backup.md"));
                if (mode.StartsWith("crash-", StringComparison.Ordinal))
                {
                    Console.WriteLine("PAUSED");
                    _ = Console.ReadLine();
                    return 1;
                }
            }
            Console.WriteLine("DONE");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR " + (ex.HResult & 0xFFFF));
            return 1;
        }
    }
}
