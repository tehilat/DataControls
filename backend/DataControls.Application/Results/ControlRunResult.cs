using DataControls.Domain.Metadata;

namespace DataControls.Application.Results;

public sealed record ResultColumn(string Key, string DisplayName, FieldDataType DataType, string? Unit);

/// <summary>One failing record: its id, display value, the root entity's visible fields, and the reason.</summary>
public sealed record ViolationRecord(
    object? Id,
    string? DisplayValue,
    IReadOnlyDictionary<string, object?> Values,
    string Reason);

public sealed record ControlRunResult(
    int? RunId,
    int? ControlId,
    string RootEntityKey,
    string RootEntityDisplayName,
    int TotalCount,
    bool Truncated,
    IReadOnlyList<ResultColumn> Columns,
    IReadOnlyList<ViolationRecord> Violations,
    long DurationMs);
