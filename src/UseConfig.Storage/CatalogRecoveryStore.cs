using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UseConfig.Catalog;

namespace UseConfig.Storage;

internal sealed record RecoveryFile(string Path, string Hash, uint Volume, ulong Id);
internal sealed record RecoveryDirectory(string Path, uint Volume, ulong Id);
internal sealed record RecoveryManifest(int Version, string Root, uint Volume, ulong RootId,
    RecoveryFile[] Files, RecoveryDirectory[] Directories);
internal sealed record RecoveryData(string Folder, string ManifestHash, RecoveryManifest Manifest,
    Dictionary<string, byte[]> Files, bool Complete);

internal sealed class RecoveryLease : IDisposable
{
    internal DirectoryPins Pins { get; } = new();
    internal string Container { get; }
    internal NativeCatalogFile? Lock { get; private set; }
    internal bool Exists { get; private set; }

    internal RecoveryLease(string root, bool create)
    {
        Container = CatalogRecoveryStore.ContainerFor(root);
        try
        {
            var parent = Path.GetDirectoryName(Container)!;
            var parentHandle = Pins.Pin(parent);
            if (!Directory.Exists(Container))
            {
                if (!create) return;
                try { using var created = NativeCatalogFile.Create(parentHandle, Path.GetFileName(Container), true); }
                catch (Exception exception) when (LocationPaths.Classify(exception) == ErrorCode.AlreadyExists) { Pins.Pin(Container); }
            }
            var containerHandle = Pins.Pin(Container);
            var lockPath = Path.Combine(Container, "writer.lock");
            try { Lock = NativeCatalogFile.Open(lockPath, false, write: true); }
            catch (Exception exception) when (LocationPaths.Classify(exception) == ErrorCode.NotFound)
            {
                if (!create && Directory.EnumerateFileSystemEntries(Container).Any()) throw new CatalogFault(ErrorCode.RecoveryRequired);
                Lock = NativeCatalogFile.Create(containerHandle, "writer.lock", false);
            }
            Exists = true;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { Lock?.Dispose(); Pins.Dispose(); }
}

internal sealed class RecoveryJournal : IDisposable
{
    internal string Folder { get; }
    internal NativeCatalogFile Directory { get; }
    internal RecoveryManifest Manifest { get; }
    private readonly List<NativeCatalogFile> owned = [];
    private readonly byte[] manifestBytes;

    internal RecoveryJournal(RecoveryLease lease, CatalogImage image)
    {
        var name = Guid.NewGuid().ToString("N");
        Folder = Path.Combine(lease.Container, name);
        Directory = NativeCatalogFile.Create(lease.Pins.Pin(lease.Container), name, true);
        try
        {
            var files = image.Files.Select(pair => new RecoveryFile(pair.Key, CatalogRecoveryStore.Hash(pair.Value),
                image.Stamps[pair.Key].Volume, image.Stamps[pair.Key].Id)).ToArray();
            var directories = image.Stamps.Where(pair => !image.Files.ContainsKey(pair.Key)).Select(pair =>
                new RecoveryDirectory(pair.Key, pair.Value.Volume, pair.Value.Id)).ToArray();
            Manifest = new RecoveryManifest(1, image.Root, image.Stamps[""].Volume, image.Stamps[""].Id, files, directories);
            for (var index = 0; index < files.Length; index++) Create($"backup-{index:D8}", image.Files[files[index].Path]);
            manifestBytes = JsonSerializer.SerializeToUtf8Bytes(Manifest);
            Create("manifest.json", manifestBytes);
        }
        catch { Dispose(); throw; }
    }

    private void Create(string name, byte[] bytes)
    {
        var file = NativeCatalogFile.Create(Directory, name, false);
        owned.Add(file);
        file.Write(bytes);
    }
    internal string HeldFile(string path) => $"held-{Array.FindIndex(Manifest.Files, item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)):D8}";
    internal string HeldDirectory(string path) => $"held-dir-{Array.FindIndex(Manifest.Directories, item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase)):D8}";
    internal void Complete() => Create("complete", Encoding.ASCII.GetBytes(CatalogRecoveryStore.Hash(manifestBytes)));
    public void Dispose()
    {
        foreach (var file in owned) file.Dispose();
        Directory.Dispose();
    }
}

internal static class CatalogRecoveryStore
{
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string ContainerFor(string root)
    {
        var parent = Directory.GetParent(root) ?? throw new CatalogFault(ErrorCode.InvalidInput);
        return Path.Combine(parent.FullName, ".useconfig-recovery-" + Hash(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
    }

    internal static IReadOnlyList<RecoveryData> Scan(RecoveryLease lease, string root, List<Diagnostic> diagnostics)
    {
        var pending = new List<RecoveryData>();
        if (!lease.Exists) return pending;
        foreach (var path in Directory.GetFileSystemEntries(lease.Container))
        {
            if (Path.GetFileName(path) == "writer.lock") continue;
            var name = Path.GetFileName(path);
            var completed = name.EndsWith(".complete", StringComparison.Ordinal);
            if (!Guid.TryParseExact(completed ? name[..^9] : name, "N", out _))
            {
                diagnostics.Add(new Diagnostic(ErrorCode.RecoveryRequired));
                continue;
            }
            try
            {
                if (completed)
                {
                    try { CleanupCompleted(path, root); }
                    catch (Exception exception) when (LocationPaths.Classify(exception) is not null || exception is JsonException)
                    { diagnostics.Add(new Diagnostic(ErrorCode.RecoveryCleanupFailed)); }
                    continue;
                }
                var data = Read(path, root);
                if (!data.Complete) pending.Add(data);
                else
                {
                    try
                    {
                        using (var directory = NativeCatalogFile.Open(path, true, delete: true))
                            directory.Rename(lease.Pins.Pin(lease.Container), name + ".complete");
                        CleanupCompleted(path + ".complete", root);
                    }
                    catch (Exception exception) when (LocationPaths.Classify(exception) is not null)
                    {
                        diagnostics.Add(new Diagnostic(ErrorCode.RecoveryCleanupFailed));
                    }
                }
            }
            catch (Exception exception) when (LocationPaths.Classify(exception) is not null || exception is JsonException)
            {
                diagnostics.Add(new Diagnostic(ErrorCode.RecoveryRequired));
            }
        }
        if (pending.Count != 0) diagnostics.Add(new Diagnostic(ErrorCode.RecoveryRequired));
        return pending;
    }

    private static void CleanupCompleted(string folder, string root)
    {
        // The terminal directory name survives partial cleanup. Keep the manifest until all data is gone.
        if (!File.Exists(Path.Combine(folder, "manifest.json")))
        {
            using var empty = NativeCatalogFile.Open(folder, true, delete: true);
            if (Directory.EnumerateFileSystemEntries(folder).Any()) throw new CatalogFault(ErrorCode.MalformedContent);
            empty.Delete();
            return;
        }
        byte[] bytes;
        using (var file = NativeCatalogFile.Open(Path.Combine(folder, "manifest.json"), false)) bytes = file.Read();
        var manifest = JsonSerializer.Deserialize<RecoveryManifest>(bytes) ?? throw new CatalogFault(ErrorCode.MalformedContent);
        if (manifest.Version != 1 || manifest.Root != root || manifest.Files is null || manifest.Directories is null)
            throw new CatalogFault(ErrorCode.MalformedContent);
        Cleanup(new RecoveryData(folder, Hash(bytes), manifest, [], true));
    }

    internal static RecoveryData Read(string folder, string root)
    {
        using var directory = NativeCatalogFile.Open(folder, true);
        var held = new List<NativeCatalogFile>();
        try
        {
            var manifestFile = NativeCatalogFile.Open(Path.Combine(folder, "manifest.json"), false);
            held.Add(manifestFile);
            var manifestBytes = manifestFile.Read();
            var manifest = JsonSerializer.Deserialize<RecoveryManifest>(manifestBytes) ?? throw new CatalogFault(ErrorCode.MalformedContent);
            if (manifest.Version != 1 || manifest.Root != root || manifest.Files is null || manifest.Directories is null)
                throw new CatalogFault(ErrorCode.MalformedContent);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json", "complete" };
            for (var index = 0; index < manifest.Files.Length; index++)
            {
                var item = manifest.Files[index];
                if (item is null || item.Path is null || item.Hash is null) throw new CatalogFault(ErrorCode.MalformedContent);
                ChangePlan.ValidatePath(item.Path);
                var name = $"backup-{index:D8}";
                allowed.Add(name);
                allowed.Add($"held-{index:D8}");
                var backup = NativeCatalogFile.Open(Path.Combine(folder, name), false);
                held.Add(backup);
                var bytes = backup.Read();
                if (Hash(bytes) != item.Hash || !files.TryAdd(item.Path, bytes)) throw new CatalogFault(ErrorCode.MalformedContent);
            }
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < manifest.Directories.Length; index++)
            {
                var item = manifest.Directories[index];
                if (item is null || item.Path is null || !directories.Add(item.Path)) throw new CatalogFault(ErrorCode.MalformedContent);
                if (item.Path.Length != 0)
                {
                    ChangePlan.ValidatePath(item.Path);
                    if (item.Path.Contains('/') || item.Path != item.Path.Trim() || !files.ContainsKey(item.Path + "/_topic.md"))
                        throw new CatalogFault(ErrorCode.MalformedContent);
                }
                else if (item.Volume != manifest.Volume || item.Id != manifest.RootId) throw new CatalogFault(ErrorCode.MalformedContent);
                allowed.Add($"held-dir-{index:D8}");
            }
            if (!directories.Contains("") || files.Keys.Any(path => !directories.Contains(CatalogTree.ParentPath(path))))
                throw new CatalogFault(ErrorCode.MalformedContent);
            var stamps = directories.ToDictionary(path => path, _ => default(NativeStamp), StringComparer.OrdinalIgnoreCase);
            foreach (var path in files.Keys) if (!stamps.TryAdd(path, default)) throw new CatalogFault(ErrorCode.MalformedContent);
            if (new CatalogImage(root, files, stamps, []).Diagnostics.Count != 0) throw new CatalogFault(ErrorCode.MalformedContent);
            var entries = Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).ToArray();
            if (entries.Any(name => name is null || !allowed.Contains(name))) throw new CatalogFault(ErrorCode.MalformedContent);
            var complete = entries.Contains("complete", StringComparer.OrdinalIgnoreCase);
            if (complete)
            {
                var marker = NativeCatalogFile.Open(Path.Combine(folder, "complete"), false);
                held.Add(marker);
                if (!marker.Read().AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(Hash(manifestBytes))))
                    throw new CatalogFault(ErrorCode.MalformedContent);
            }
            return new RecoveryData(folder, Hash(manifestBytes), manifest, files, complete);
        }
        finally { foreach (var file in held) file.Dispose(); }
    }

    internal static void Cleanup(RecoveryData data)
    {
        // No recursive path deletion: every permitted child is checked and deleted through its own handle.
        using var directory = NativeCatalogFile.Open(data.Folder, true, delete: true);
        var children = new List<NativeCatalogFile>();
        try
        {
            foreach (var path in Directory.GetFileSystemEntries(data.Folder).OrderBy(path => Path.GetFileName(path) == "manifest.json" ? 2 : Path.GetFileName(path) == "complete" ? 1 : 0))
            {
                var name = Path.GetFileName(path);
                var isDirectory = name.StartsWith("held-dir-", StringComparison.Ordinal);
                var child = NativeCatalogFile.Open(path, isDirectory, delete: true);
                children.Add(child);
                if (isDirectory)
                {
                    var index = Index(name, "held-dir-", data.Manifest.Directories.Length);
                    var original = data.Manifest.Directories[index];
                    var stamp = child.Stamp();
                    if (stamp.Volume != original.Volume || stamp.Id != original.Id || Directory.EnumerateFileSystemEntries(path).Any())
                        throw new CatalogFault(ErrorCode.Conflict);
                }
                else if (name == "manifest.json")
                {
                    if (Hash(child.Read()) != data.ManifestHash) throw new CatalogFault(ErrorCode.Conflict);
                }
                else if (name == "complete")
                {
                    if (!child.Read().AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(data.ManifestHash))) throw new CatalogFault(ErrorCode.Conflict);
                }
                else
                {
                    var originalObject = name.StartsWith("held-", StringComparison.Ordinal);
                    var index = Index(name, originalObject ? "held-" : "backup-", data.Manifest.Files.Length);
                    var original = data.Manifest.Files[index];
                    if (Hash(child.Read()) != original.Hash) throw new CatalogFault(ErrorCode.Conflict);
                    var stamp = child.Stamp();
                    if (originalObject && (stamp.Volume != original.Volume || stamp.Id != original.Id)) throw new CatalogFault(ErrorCode.Conflict);
                }
            }
            // Verify every child before starting cleanup, leaving unknown files untouched.
            foreach (var child in children) { child.Delete(); child.Dispose(); }
            children.Clear();
            directory.Delete();
        }
        finally { foreach (var child in children) child.Dispose(); }
    }

    private static int Index(string value, string prefix, int count)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 8
            || !int.TryParse(value.AsSpan(prefix.Length), out var index) || index < 0 || index >= count)
            throw new CatalogFault(ErrorCode.MalformedContent);
        return index;
    }

    internal static string SaveCopy(RecoveryData data, string parent, string root, CancellationToken cancellationToken)
    {
        if (LocationPaths.Overlaps(parent, root) || LocationPaths.Overlaps(parent, Path.GetDirectoryName(data.Folder)!))
            throw new CatalogFault(ErrorCode.InvalidInput);
        using var pins = new DirectoryPins();
        var parentHandle = pins.Pin(parent);
        var name = "Recovered catalog " + Guid.NewGuid().ToString("N");
        var stagingName = name;
        var staging = Path.Combine(parent, stagingName);
        using var copy = NativeCatalogFile.Create(parentHandle, stagingName, true);
        var directories = new Dictionary<string, NativeCatalogFile>(StringComparer.OrdinalIgnoreCase) { [""] = copy };
        var files = new List<NativeCatalogFile>();
        try
        {
            foreach (var topic in data.Manifest.Directories.Where(item => item.Path.Length != 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                directories.Add(topic.Path, NativeCatalogFile.Create(copy, topic.Path, true));
            }
            foreach (var pair in data.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = NativeCatalogFile.Create(directories[CatalogTree.ParentPath(pair.Key)], CatalogTree.Leaf(pair.Key), false);
                files.Add(file);
                file.Write(pair.Value);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Validate through retained handles: reopening would conflict with our exclusive creation handles.
            if (files.Count != data.Files.Count || files.Where((file, index) =>
                !file.Read().AsSpan().SequenceEqual(data.Files.ElementAt(index).Value)).Any())
                throw new CatalogFault(ErrorCode.IoFailure);
            var expected = data.Files.Keys.Concat(directories.Keys.Where(path => path.Length != 0)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories.Keys)
                foreach (var child in System.IO.Directory.GetFileSystemEntries(Path.Combine(staging, directory)))
                    if (!expected.Remove(directory.Length == 0 ? Path.GetFileName(child) : directory + "/" + Path.GetFileName(child)))
                        throw new CatalogFault(ErrorCode.Conflict);
            if (expected.Count != 0) throw new CatalogFault(ErrorCode.Conflict);
            return Path.Combine(parent, name);
        }
        finally
        {
            foreach (var file in files) file.Dispose();
            foreach (var directory in directories.Where(pair => pair.Key.Length != 0)) directory.Value.Dispose();
        }
    }
}
