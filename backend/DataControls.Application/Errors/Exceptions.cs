namespace DataControls.Application.Errors;

/// <summary>One validation problem. <see cref="Path"/> points into the JSON, e.g. "condition.children[1].field".</summary>
public sealed record ValidationError(string Path, string Message);

/// <summary>The definition is invalid against the catalog. Messages are user-facing (Hebrew).</summary>
public sealed class ControlValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("הגדרת הבקרה אינה תקינה: " + string.Join(" | ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}

public sealed class ControlNotFoundException(int controlId)
    : Exception($"הבקרה {controlId} לא נמצאה.")
{
    public int ControlId { get; } = controlId;
}

/// <summary>Running the generated query failed (timeout, permission, ...). Message is user-facing (Hebrew).</summary>
public sealed class ControlExecutionException(string message, Exception? inner = null)
    : Exception(message, inner);
