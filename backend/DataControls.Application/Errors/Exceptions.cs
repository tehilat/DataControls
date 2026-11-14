namespace DataControls.Application.Errors;

/// <summary>One validation problem. <see cref="Path"/> points into the JSON, e.g. "condition.children[1].field".</summary>
public sealed record ValidationError(string Path, string Message);

/// <summary>The definition is invalid against the catalog. Messages are user-facing (Hebrew).</summary>
public sealed class ControlValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("הגדרת הבקרה אינה תקינה: " + string.Join(" | ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>A request parameter (paging, filter, depth, ...) is invalid. Messages are user-facing (Hebrew).</summary>
public sealed class InvalidRequestException(IReadOnlyList<ValidationError> errors)
    : Exception("הבקשה אינה תקינה: " + string.Join(" | ", errors.Select(e => e.Message)))
{
    public InvalidRequestException(string path, string message) : this([new ValidationError(path, message)]) { }

    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

/// <summary>Something that was asked for does not exist. Message is user-facing (Hebrew).</summary>
public class NotFoundException(string message) : Exception(message);

public sealed class ControlNotFoundException(int controlId)
    : NotFoundException($"הבקרה {controlId} לא נמצאה.")
{
    public int ControlId { get; } = controlId;
}

public sealed class RunNotFoundException(int runId)
    : NotFoundException($"ההרצה {runId} לא נמצאה.")
{
    public int RunId { get; } = runId;
}

public sealed class EntityNotFoundException(string entityKey)
    : NotFoundException($"הישות \"{(entityKey.Length > 50 ? entityKey[..50] : entityKey)}\" אינה קיימת בקטלוג.");

/// <summary>
/// The request conflicts with the current state (duplicate name, stale version, delete with history,
/// run of an inactive control). Message is user-facing (Hebrew); <see cref="Path"/> optionally names the field.
/// </summary>
public sealed class ConflictException(string message, string? path = null) : Exception(message)
{
    public string? Path { get; } = path;
}

/// <summary>Running the generated query failed (timeout, permission, ...). Message is user-facing (Hebrew).</summary>
public sealed class ControlExecutionException(string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The recorded run (ctl.ControlRuns) when the failure happened in a saved-control run.</summary>
    public int? RunId { get; init; }
}
