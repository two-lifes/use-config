namespace UseConfig.Catalog;

public interface ICatalogLocation
{
    Task<OperationResult<OpenedCatalog>> OpenAsync(string path, bool create, CancellationToken cancellationToken);
}

public interface ICatalogSettings
{
    Task<OperationResult<string?>> ReadAsync(CancellationToken cancellationToken);
    Task<OperationResult<Completion>> RememberAsync(string path, CancellationToken cancellationToken);
}

public sealed class CatalogSession(ICatalogLocation location, ICatalogSettings settings)
{
    public OpenedCatalog? Current { get; private set; }
    public string? CurrentPath => Current?.RootPath;
    public string? RememberedPath { get; private set; }
    public ErrorCode? PersistenceError { get; private set; }

    public bool Replace(OpenedCatalog expected, OpenedCatalog updated)
    {
        if (!ReferenceEquals(Current, expected) || expected.RootPath != updated.RootPath) return false;
        Current = updated;
        return true;
    }

    public async Task<OperationResult<Completion>> RestoreAsync(CancellationToken cancellationToken)
    {
        var read = await settings.ReadAsync(cancellationToken);
        if (read is OperationResult<string?>.Failure failure)
            return new OperationResult<Completion>.Failure(failure.Code);
        RememberedPath = ((OperationResult<string?>.Success)read).Value;
        if (RememberedPath is null) return new OperationResult<Completion>.Success(Completion.Done);
        return await OpenAsync(RememberedPath, false, false, cancellationToken);
    }

    public async Task<OperationResult<Completion>> OpenAsync(
        string path, bool create, bool remember, CancellationToken cancellationToken)
    {
        var opened = await location.OpenAsync(path, create, cancellationToken);
        if (opened is OperationResult<OpenedCatalog>.Failure failure)
            return new OperationResult<Completion>.Failure(failure.Code);
        Current = ((OperationResult<OpenedCatalog>.Success)opened).Value;
        PersistenceError = null;
        if (remember && !Current.IsReadOnly)
        {
            // Opening/creation has committed. Persistence must report its actual outcome.
            var saved = await settings.RememberAsync(Current.RootPath, CancellationToken.None);
            if (saved is OperationResult<Completion>.Failure saveFailure)
                PersistenceError = saveFailure.Code;
            else
                RememberedPath = CurrentPath;
        }
        return new OperationResult<Completion>.Success(Completion.Done);
    }
}
