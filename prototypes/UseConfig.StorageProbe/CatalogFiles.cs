using System.IO.Compression;
using System.Security.Cryptography;

namespace UseConfig.StorageProbe;

internal sealed record CatalogSnapshot(SortedDictionary<string, byte[]> Files, Dictionary<string, FileStamp>? Stamps = null)
{
    internal bool SameAs(CatalogSnapshot other) => Files.Count == other.Files.Count
        && Files.All(pair => other.Files.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes));
}

internal static class CatalogFiles
{
    internal static CatalogSnapshot Read(string root, IReadOnlyDictionary<string, FileStream>? held = null)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var stamps = new Dictionary<string, FileStamp>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CheckPath(root);
        var rootFiles = Directory.GetFiles(root);
        if (rootFiles.Length != 1 || Path.GetFileName(rootFiles[0]) != "_catalog.md") throw new ProbeException("root-layout");
        Add(rootFiles[0], DocumentKind.Catalog);
        var topicNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var topic in Directory.GetDirectories(root))
        {
            CheckPath(topic);
            var name = Path.GetFileName(topic);
            if (name != name.Trim() || !topicNames.Add(name) || !ValidComponent(name)) throw new ProbeException("topic-name");
            if (Directory.GetDirectories(topic).Length != 0) throw new ProbeException("nested-topic");
            var metadata = Path.Combine(topic, "_topic.md");
            if (!File.Exists(metadata)) throw new ProbeException("missing-topic-metadata");
            Add(metadata, DocumentKind.Topic);
            foreach (var entry in Directory.GetFiles(topic))
            {
                if (entry == metadata) continue;
                var filename = Path.GetFileName(entry);
                if (!ValidComponent(filename) || !Path.GetExtension(entry).Equals(".md", StringComparison.OrdinalIgnoreCase)
                    || filename.Equals("_catalog.md", StringComparison.OrdinalIgnoreCase)
                    || filename.Equals("_topic.md", StringComparison.OrdinalIgnoreCase)) throw new ProbeException("extra-file");
                Add(entry, DocumentKind.Entry);
            }
        }
        return new CatalogSnapshot(files, stamps);

        void Add(string path, DocumentKind kind)
        {
            CheckPath(path);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var isHeld = held != null && held.TryGetValue(relative, out _);
            var input = isHeld ? held![relative] : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] bytes;
            try
            {
                bytes = ReadBytes(input);
                stamps.Add(relative, WindowsFileGuard.Stamp(input.SafeFileHandle));
            }
            finally { if (!isHeld) input.Dispose(); }
            var doc = MarkdownCodec.Read(bytes, kind);
            if (doc.Fields.TryGetValue("id", out var id) && !ids.Add(id)) throw new ProbeException("duplicate-id");
            files.Add(relative, bytes);
        }
    }

    internal static byte[] ReadBytes(FileStream file)
    {
        file.Position = 0;
        using var buffer = new MemoryStream();
        file.CopyTo(buffer);
        return buffer.ToArray();
    }

    internal static bool ValidComponent(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')
            || name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))) return false;
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is not ("CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$")
            && !System.Text.RegularExpressions.Regex.IsMatch(stem, "^(COM|LPT)[1-9¹²³]$");
    }

    private static void CheckPath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ProbeException("reparse-point");
    }

    internal static string Fingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // Deliberately a candidate algorithm: the suite tests its check/replace race.
    internal static void ReplaceCandidate(string root, string relative, CatalogSnapshot expected, byte[] replacement,
        string staging, Action? beforeReplace = null)
    {
        _ = MarkdownCodec.Read(replacement, DocumentKind.Entry);
        if (!Read(root).SameAs(expected)) throw new ProbeException("external-conflict");
        Directory.CreateDirectory(staging);
        var prepared = Path.Combine(staging, "prepared.md");
        var backup = Path.Combine(staging, "previous.md");
        using (var output = new FileStream(prepared, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            output.Write(replacement);
            output.Flush(true);
        }
        if (!Read(root).SameAs(expected)) throw new ProbeException("external-conflict");
        beforeReplace?.Invoke();
        File.Replace(prepared, Path.Combine(root, relative), backup);
        if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(expected.Files[relative]))
            throw new ProbeException("conflict-after-replace");
    }

    internal static void Export(string root, string archive, Action? duringExport = null)
    {
        var before = Read(root);
        var partial = archive + ".partial";
        using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            foreach (var (relative, bytes) in before.Files)
            {
                var entry = zip.CreateEntry(relative);
                using var output = entry.Open();
                output.Write(bytes);
            }
        }
        duringExport?.Invoke();
        if (!Read(root).SameAs(before)) throw new ProbeException("export-conflict");
        File.Move(partial, archive, false);
    }

    internal static CatalogSnapshot Extract(string archive, string destination)
    {
        // The probe only extracts into a new synthetic directory, never over saved data.
        if (Directory.Exists(destination)) throw new ProbeException("destination-exists");
        var content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var zip = ZipFile.OpenRead(archive))
        {
            foreach (var entry in zip.Entries)
            {
                var relative = entry.FullName;
                var parts = relative.Split('/');
                if (parts.Length is < 1 or > 2 || parts.Any(p => !ValidComponent(p))
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                    || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new ProbeException("unsafe-archive-path");
                using var input = entry.Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                if (!content.TryAdd(relative, buffer.ToArray())) throw new ProbeException("duplicate-archive-path");
            }
        }
        Directory.CreateDirectory(destination);
        foreach (var (relative, bytes) in content)
        {
            var path = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
        return Read(destination);
    }
}
