namespace UseConfig.Catalog;

public enum ErrorCode
{
    InvalidInput, UnsupportedFormat, MalformedContent, NotFound, AccessDenied,
    Conflict, ReadOnly, AlreadyExists, InsufficientSpace, IoFailure,
    UnsafeArchive, ClipboardUnavailable, LaunchFailed
}

// Locations are for UI diagnostics only, never for technical logging.
public sealed record Diagnostic(ErrorCode Code, string? RelativePath = null, string? Field = null);

public abstract record OperationResult<T>
{
    private OperationResult() { }

    public sealed record Success(T Value) : OperationResult<T>;
    public sealed record Failure(ErrorCode Code) : OperationResult<T>;
}

public enum Completion { Done }
