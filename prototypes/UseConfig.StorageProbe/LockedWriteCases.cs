using System.ComponentModel;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace UseConfig.StorageProbe;

internal static class LockedWriteCases
{
    internal static void Run(Action<string, Action> test, string runRoot)
    {
        test("locked-identical-replacement-detected-by-file-id", () =>
        {
            var root = Fixture(runRoot);
            var expected = CatalogFiles.Read(root);
            var target = Path.Combine(root, Program.EntryRelative);
            var replacement = Beside(root, "external-identical.md");
            File.WriteAllBytes(replacement, expected.Files[Program.EntryRelative]);
            File.Replace(replacement, target, Beside(root, "external-previous.md"));
            Program.Require(CatalogFiles.Read(root).SameAs(expected));
            Program.Require(CatalogFiles.Read(root).Stamps![Program.EntryRelative].Id != expected.Stamps![Program.EntryRelative].Id);
            Program.Reject(() => LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage")), "external-conflict");
            Program.Require(CatalogFiles.Read(root).SameAs(expected));
        });

        foreach (var occupied in new[] { "previous.md", "intent.json", "complete" })
        {
            test("locked-evidence-collision-preserves-original-" + occupied, () =>
            {
                var root = Fixture(runRoot);
                var expected = CatalogFiles.Read(root);
                var stage = Beside(root, "stage");
                Directory.CreateDirectory(stage);
                var occupiedPath = Path.Combine(stage, occupied);
                var evidence = MarkdownCodec.Utf8.GetBytes("synthetic-existing-evidence");
                File.WriteAllBytes(occupiedPath, evidence);
                ExpectIOException(() => LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), stage));
                Program.Require(CatalogFiles.Read(root).SameAs(expected));
                Program.Require(File.ReadAllBytes(occupiedPath).AsSpan().SequenceEqual(evidence));
            });
        }

        test("locked-partial-write-error-rolls-back", () =>
        {
            var root = Fixture(runRoot);
            var expected = CatalogFiles.Read(root);
            var stage = Beside(root, "stage");
            var reachedPartial = false;
            ExpectIOException(() => LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), stage, phase =>
            {
                if (phase != "partial") return;
                reachedPartial = true;
                throw new IOException("synthetic-write-failure");
            }));
            Program.Require(reachedPartial && CatalogFiles.Read(root).SameAs(expected));
            Program.Require(File.ReadAllBytes(Path.Combine(stage, "previous.md")).AsSpan().SequenceEqual(expected.Files[Program.EntryRelative]));
            Program.Require(!File.Exists(Path.Combine(stage, "complete")));
        });

        test("locked-recovery-corrupt-backup-refused", () =>
        {
            var root = Fixture(runRoot);
            var stage = Beside(root, "stage");
            LockedFileWriter.Save(root, Program.EntryRelative, CatalogFiles.Read(root), Edited(), stage);
            var active = CatalogFiles.Read(root);
            File.AppendAllText(Path.Combine(stage, "previous.md"), "synthetic-corruption");
            var recovered = Beside(root, "recovered.md");
            Program.Reject(() => LockedFileWriter.RecoverToNewFile(stage, recovered), "recovery-hash");
            Program.Require(!File.Exists(recovered) && CatalogFiles.Read(root).SameAs(active));
        });

        test("locked-recovery-existing-destination-preserved", () =>
        {
            var root = Fixture(runRoot);
            var stage = Beside(root, "stage");
            LockedFileWriter.Save(root, Program.EntryRelative, CatalogFiles.Read(root), Edited(), stage);
            var active = CatalogFiles.Read(root);
            var recovered = Beside(root, "recovered.md");
            var external = MarkdownCodec.Utf8.GetBytes("synthetic-external-destination");
            File.WriteAllBytes(recovered, external);
            ExpectIOException(() => LockedFileWriter.RecoverToNewFile(stage, recovered));
            Program.Require(File.ReadAllBytes(recovered).AsSpan().SequenceEqual(external));
            Program.Require(CatalogFiles.Read(root).SameAs(active));
        });

        test("locked-preexisting-writer-refused", () =>
        {
            var root = Fixture(runRoot);
            var expected = CatalogFiles.Read(root);
            using (var writer = new FileStream(Path.Combine(root, Program.EntryRelative), FileMode.Open, FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete))
            {
                ExpectSharingViolation(() => LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage")));
            }
            Program.Require(CatalogFiles.Read(root).SameAs(expected));
            LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage-after-close"));
            Program.Require(File.ReadAllBytes(Path.Combine(root, Program.EntryRelative)).AsSpan().SequenceEqual(Edited()));
        });

        test("locked-preexisting-writable-mapping-refused", () =>
        {
            var root = Fixture(runRoot);
            var expected = CatalogFiles.Read(root);
            var target = Path.Combine(root, Program.EntryRelative);
            using (var writer = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            using (var mapping = MemoryMappedFile.CreateFromFile(writer, null, 0, MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None, leaveOpen: true))
            using (var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite))
            {
                // Isolate the surviving writable mapping from the original user file handle.
                writer.Dispose();
                ExpectSharingViolation(() => LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage")));
            }
            Program.Require(CatalogFiles.Read(root).SameAs(expected));
        });

        test("locked-hardlink-write-refused-then-retry", () =>
        {
            var root = Fixture(runRoot);
            var target = Path.Combine(root, Program.EntryRelative);
            var alias = Beside(root, "alias.md");
            if (!CreateHardLinkW(alias, target, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var expected = CatalogFiles.Read(root);
            var attempted = false;
            LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage"), phase =>
            {
                if (phase != "prepared") return;
                attempted = true;
                ExpectSharingViolation(() => File.WriteAllBytes(alias, expected.Files[Program.EntryRelative]));
            });
            Program.Require(attempted && File.ReadAllBytes(alias).AsSpan().SequenceEqual(Edited()));
            File.WriteAllBytes(alias, expected.Files[Program.EntryRelative]);
            Program.Require(File.ReadAllBytes(target).AsSpan().SequenceEqual(expected.Files[Program.EntryRelative]));
        });

        foreach (var metadata in new[] { "_catalog.md", "Тема/_topic.md" })
        {
            test("locked-metadata-protected-" + (metadata == "_catalog.md" ? "root" : "topic"), () =>
            {
                var root = Fixture(runRoot);
                var expected = CatalogFiles.Read(root);
                var attempted = false;
                LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage"), phase =>
                {
                    if (phase != "prepared") return;
                    attempted = true;
                    ExpectSharingViolation(() => File.WriteAllText(Path.Combine(root, metadata), "synthetic-external-metadata"));
                });
                Program.Require(attempted && File.ReadAllBytes(Path.Combine(root, metadata)).AsSpan().SequenceEqual(expected.Files[metadata]));
            });
        }

        foreach (var directory in new[] { "topic", "root", "parent" })
        {
            test("locked-directory-rename-protected-" + directory, () =>
            {
                var root = Fixture(runRoot);
                var expected = CatalogFiles.Read(root);
                var source = directory switch
                {
                    "topic" => Path.Combine(root, "Тема"),
                    "root" => root,
                    _ => Path.GetDirectoryName(root)!
                };
                var attempted = false;
                LockedFileWriter.Save(root, Program.EntryRelative, expected, Edited(), Beside(root, "stage"), phase =>
                {
                    if (phase != "prepared") return;
                    attempted = true;
                    ExpectDirectoryLock(() => Directory.Move(source, source + "-renamed"));
                });
                Program.Require(attempted && Directory.Exists(source) && !Directory.Exists(source + "-renamed"));
                Program.Require(File.ReadAllBytes(Path.Combine(root, Program.EntryRelative)).AsSpan().SequenceEqual(Edited()));
                Directory.Move(source, source + "-renamed");
                Program.Require(!Directory.Exists(source) && Directory.Exists(source + "-renamed"));
            });
        }
    }

    private static string Fixture(string runRoot)
    {
        var root = Path.Combine(runRoot, "locked-" + Guid.NewGuid().ToString("N"), "catalog");
        Directory.CreateDirectory(Path.Combine(root, "Тема"));
        File.WriteAllText(Path.Combine(root, "_catalog.md"), "---\nformat_version: 1\n---\n", MarkdownCodec.Utf8);
        File.WriteAllText(Path.Combine(root, "Тема", "_topic.md"), Program.Topic, MarkdownCodec.Utf8);
        File.WriteAllText(Path.Combine(root, Program.EntryRelative), Program.Original, MarkdownCodec.Utf8);
        return root;
    }

    private static string Beside(string root, string name) => Path.Combine(Path.GetDirectoryName(root)!, name);
    private static byte[] Edited() => MarkdownCodec.Utf8.GetBytes(Program.Original.Replace("Example", "Saved By Locked Writer", StringComparison.Ordinal));

    private static void ExpectIOException(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        throw new ProbeException("expected-io-error");
    }

    private static void ExpectSharingViolation(Action action)
    {
        try { action(); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33) { return; }
        throw new ProbeException("expected-sharing-violation");
    }

    private static void ExpectDirectoryLock(Action action)
    {
        try { action(); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 5 or 32) { return; }
        throw new ProbeException("expected-directory-lock");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
}
