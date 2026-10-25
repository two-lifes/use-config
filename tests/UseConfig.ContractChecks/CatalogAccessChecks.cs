using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using UseConfig.Catalog;
using UseConfig.Storage;

internal static class CatalogAccessChecks
{
    [SupportedOSPlatform("windows")]
    public static async Task RunAsync(Action<bool, string> check)
    {
        var token = CancellationToken.None;
        var location = new ControlledLocation();
        var settings = new RecordingSettings { Path = "remembered" };
        var startup = new CatalogSession(location, settings);
        check(await startup.RestoreAsync(token) is OperationResult<Completion>.Failure { Code: ErrorCode.AccessDenied }
            && startup.Current is null && startup.RememberedPath == "remembered"
            && settings.Writes == 0 && location.CreationRequests == 0,
            "access denied at startup retains remembered path without writing settings or creating a substitute");
        location.Deny = false;
        await startup.OpenAsync("active", false, true, token);
        var active = startup.Current;
        settings.Writes = 0;
        location.Deny = true;
        check(await startup.OpenAsync("denied", false, true, token)
            is OperationResult<Completion>.Failure { Code: ErrorCode.AccessDenied }
            && ReferenceEquals(startup.Current, active) && startup.RememberedPath == "active"
            && settings.Path == "active" && settings.Writes == 0 && location.CreationRequests == 0,
            "denied selection preserves active snapshot and settings without writes");
        location.Deny = false;
        check(await startup.OpenAsync("denied", false, true, token) is OperationResult<Completion>.Success
            && startup.CurrentPath == "denied" && settings.Path == "denied" && settings.Writes == 1,
            "retry after restored access can open and remember the requested catalog");

        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows ACL verification is required.");
        var root = Path.Combine(AppContext.BaseDirectory, "access-checks", Guid.NewGuid().ToString("N"));
        var catalog = Path.Combine(root, "catalog");
        var directory = Directory.CreateDirectory(catalog);
        var settingsDirectory = Path.Combine(root, "settings");
        var realLocation = new MarkdownCatalogLocation(settingsDirectory);
        var realSettings = new CatalogSettings(settingsDirectory);
        var session = new CatalogSession(realLocation, realSettings);
        check(await session.OpenAsync(catalog, true, true, token) is OperationResult<Completion>.Success,
            "synthetic ACL catalog is initialized");
        var before = File.ReadAllBytes(Path.Combine(catalog, "_catalog.md"));
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        var savedSettings = File.ReadAllBytes(settingsPath);
        var descriptor = directory.GetAccessControl(AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent();
        var denied = directory.GetAccessControl(AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            var blocked = new CatalogSession(realLocation, realSettings);
            check(await blocked.RestoreAsync(token) is OperationResult<Completion>.Failure { Code: ErrorCode.AccessDenied }
                && blocked.Current is null && blocked.RememberedPath == catalog
                && savedSettings.SequenceEqual(File.ReadAllBytes(settingsPath)),
                "real Windows directory ACL denies reopening without replacing settings");
            var previous = session.Current;
            check(await session.OpenAsync(catalog, false, true, token)
                is OperationResult<Completion>.Failure { Code: ErrorCode.AccessDenied }
                && ReferenceEquals(session.Current, previous)
                && savedSettings.SequenceEqual(File.ReadAllBytes(settingsPath)),
                "real denied selection retains the active catalog and saved settings");
        }
        finally
        {
            // Setting the descriptor explicitly marks the DACL for persistence during restoration.
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorSddlForm(descriptor, AccessControlSections.Access);
            directory.SetAccessControl(restored);
        }
        var restoredAccess = directory.GetAccessControl(AccessControlSections.Access);
        using var restoredIdentity = WindowsIdentity.GetCurrent();
        var hasListDeny = restoredAccess.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Deny
                && (rule.FileSystemRights & FileSystemRights.ListDirectory) != 0
                && rule.IdentityReference.Equals(restoredIdentity.User));
        check(!hasListDeny
            && before.SequenceEqual(File.ReadAllBytes(Path.Combine(catalog, "_catalog.md")))
            && savedSettings.SequenceEqual(File.ReadAllBytes(settingsPath)),
            "original directory ACL and catalog/settings bytes are preserved");
        check(await session.OpenAsync(catalog, false, true, token) is OperationResult<Completion>.Success,
            "real catalog reopens after ACL restoration");
    }

    private sealed class ControlledLocation : ICatalogLocation
    {
        public bool Deny { get; set; } = true;
        public int CreationRequests { get; private set; }
        public Task<OperationResult<OpenedCatalog>> OpenAsync(string path, bool create, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (create) CreationRequests++;
            return Task.FromResult<OperationResult<OpenedCatalog>>(Deny
                ? new OperationResult<OpenedCatalog>.Failure(ErrorCode.AccessDenied)
                : new OperationResult<OpenedCatalog>.Success(new OpenedCatalog(path, [], [], [])));
        }
    }

    private sealed class RecordingSettings : ICatalogSettings
    {
        public string? Path { get; set; }
        public int Writes { get; set; }
        public Task<OperationResult<string?>> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<OperationResult<string?>>(new OperationResult<string?>.Success(Path));
        }
        public Task<OperationResult<Completion>> RememberAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Path = path;
            return Task.FromResult<OperationResult<Completion>>(new OperationResult<Completion>.Success(Completion.Done));
        }
    }
}
