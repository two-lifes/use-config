using System.Text.Json;

namespace UseConfig.StorageProbe;

internal sealed record RecoveryIntent(string RelativePath, string PreviousHash, string ProposedHash);

internal static class LockedFileWriter
{
    internal static void Save(string root, string relative, CatalogSnapshot expected, byte[] replacement,
        string staging, Action<string>? checkpoint = null)
    {
        _ = MarkdownCodec.Read(replacement, DocumentKind.Entry);
        var fullRoot = Path.GetFullPath(root);
        var target = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var fullStage = Path.GetFullPath(staging);
        if (!target.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullStage.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || fullStage.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || Path.GetPathRoot(fullRoot) != Path.GetPathRoot(fullStage)) throw new ProbeException("write-path");
        if (!expected.Files.ContainsKey(relative) || expected.Stamps == null) throw new ProbeException("missing-snapshot");
        using var pins = new WindowsFileGuard();
        pins.PinAncestors(Path.GetDirectoryName(target)!);
        pins.PinAncestors(Path.GetDirectoryName(fullStage)!);
        // Reject all existing readers/writers that cannot coexist; never wait and overwrite later.
        using var file = OpenTarget(target);
        using var rootMetadata = new FileStream(Path.Combine(fullRoot, "_catalog.md"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var topicRelative = Path.GetDirectoryName(relative)!.Replace('\\', '/') + "/_topic.md";
        using var topicMetadata = new FileStream(Path.Combine(fullRoot, topicRelative), FileMode.Open, FileAccess.Read, FileShare.Read);
        var held = new Dictionary<string, FileStream>
        {
            [relative] = file,
            ["_catalog.md"] = rootMetadata,
            [topicRelative] = topicMetadata
        };
        var actual = CatalogFiles.Read(fullRoot, held);
        if (!actual.SameAs(expected) || held.Keys.Any(key => actual.Stamps![key] != expected.Stamps[key]))
            throw new ProbeException("external-conflict");
        checkpoint?.Invoke("locked");
        // CreateNew avoids overwriting another operation's recovery evidence.
        Directory.CreateDirectory(fullStage);
        pins.PinAncestors(fullStage);
        using var backup = CreateDurable(Path.Combine(fullStage, "previous.md"), expected.Files[relative]);
        var intent = new RecoveryIntent(relative, CatalogFiles.Fingerprint(expected.Files[relative]), CatalogFiles.Fingerprint(replacement));
        using var manifest = CreateDurable(Path.Combine(fullStage, "intent.json"), JsonSerializer.SerializeToUtf8Bytes(intent));
        checkpoint?.Invoke("prepared");
        try
        {
            file.Position = 0;
            file.SetLength(0);
            var half = replacement.Length / 2;
            file.Write(replacement.AsSpan(0, half));
            file.Flush(true);
            checkpoint?.Invoke("partial");
            file.Write(replacement.AsSpan(half));
            file.SetLength(replacement.Length);
            file.Flush(true);
            checkpoint?.Invoke("written");
            if (!CatalogFiles.ReadBytes(file).AsSpan().SequenceEqual(replacement)) throw new ProbeException("write-verification");
            using var complete = CreateDurable(Path.Combine(fullStage, "complete"), MarkdownCodec.Utf8.GetBytes(intent.ProposedHash));
        }
        catch
        {
            // The same exclusive handle is still held; rollback cannot erase an external edit.
            file.Position = 0;
            file.Write(expected.Files[relative]);
            file.SetLength(expected.Files[relative].Length);
            file.Flush(true);
            throw;
        }
    }

    private static FileStream OpenTarget(string target)
    {
        try { return new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
        catch (FileNotFoundException) { throw new ProbeException("external-conflict"); }
    }

    private static FileStream CreateDurable(string path, byte[] bytes)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            stream.Write(bytes);
            stream.Flush(true);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static void RecoverToNewFile(string staging, string destination)
    {
        // Recovery materializes an independently validated old version. It never repairs the
        // live path automatically, since an editor may have saved there after our process died.
        using var pins = new WindowsFileGuard();
        pins.PinAncestors(staging);
        using var manifest = new FileStream(Path.Combine(staging, "intent.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        using var backup = new FileStream(Path.Combine(staging, "previous.md"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var intent = JsonSerializer.Deserialize<RecoveryIntent>(CatalogFiles.ReadBytes(manifest)) ?? throw new ProbeException("recovery-manifest");
        var bytes = CatalogFiles.ReadBytes(backup);
        if (CatalogFiles.Fingerprint(bytes) != intent.PreviousHash) throw new ProbeException("recovery-hash");
        _ = MarkdownCodec.Read(bytes, DocumentKind.Entry);
        using var recovered = CreateDurable(destination, bytes);
    }
}
