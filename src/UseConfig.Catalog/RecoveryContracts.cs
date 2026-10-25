namespace UseConfig.Catalog;

// Issued by one recovery adapter; never a caller-supplied filesystem path.
public sealed class RecoveryOperation;

public interface ICatalogRecovery
{
    Task<OperationResult<IReadOnlyList<RecoveryOperation>>> FindAsync(string rootPath, CancellationToken cancellationToken);
    Task<OperationResult<string>> SaveCopyAsync(
        string rootPath, RecoveryOperation operation, string destinationParent, CancellationToken cancellationToken);
}
