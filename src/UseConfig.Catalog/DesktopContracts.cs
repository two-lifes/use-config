namespace UseConfig.Catalog;

public enum UrlHandoff { Accepted }
public enum ClipboardCleanup { Cleared, OwnershipLost }

// An adapter-issued identity, not the copied text or a content hash.
public sealed class ClipboardOwnership;

public interface IUrlLauncher
{
    Task<OperationResult<UrlHandoff>> LaunchAsync(Uri address, CancellationToken cancellationToken);
}

public interface IClipboardAccess
{
    Task<OperationResult<ClipboardOwnership>> CopyAsync(string text, CancellationToken cancellationToken);
    Task<OperationResult<ClipboardCleanup>> ClearIfOwnedAsync(ClipboardOwnership ownership, CancellationToken cancellationToken);
}

public interface IDelayScheduler
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public static class CredentialTiming
{
    public static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ClipboardDuration = TimeSpan.FromSeconds(60);
}
