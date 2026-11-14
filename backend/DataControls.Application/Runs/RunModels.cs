using DataControls.Application.Results;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Runs;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>A recorded run (ctl.ControlRuns). The generated SQL is deliberately not part of it.</summary>
public sealed record ControlRunInfo(
    int RunId,
    int ControlId,
    string ControlName,
    DateTime StartedAt,
    DateTime? FinishedAt,
    ControlRunStatus Status,
    int? ViolationCount,
    int? DurationMs,
    string? ErrorMessage,
    int StoredCount,
    IReadOnlyList<ResultColumn> Columns)
{
    /// <summary>More violations exist than were stored (row limit).</summary>
    public bool Truncated => ViolationCount is { } total && total > StoredCount;
}

/// <summary>Last run of a control, shown in the controls list.</summary>
public sealed record RunSummary(int RunId, DateTime StartedAt, ControlRunStatus Status, int? ViolationCount);

/// <summary>One failing record as stored in ctl.ControlRunViolations.</summary>
public sealed record StoredViolation(
    int RowNumber,
    string? RecordId,
    string? DisplayValue,
    string ValuesJson,
    string Reason,
    string SearchText);

public sealed record RunListQuery(int? ControlId, ControlRunStatus? Status, int Page, int PageSize);

/// <summary>A typed filter on one stored column. <see cref="Value"/> is already of the column's CLR type.</summary>
public sealed record ViolationFilter(string ColumnKey, FieldDataType DataType, object Value);

public sealed record ViolationSort(string ColumnKey, FieldDataType DataType, bool Descending);

/// <summary>A validated query over the stored results of a run (keys come from the run's column snapshot).</summary>
public sealed record ViolationQuery(
    string? Search,
    IReadOnlyList<ViolationFilter> Filters,
    ViolationSort? Sort,
    int Page,
    int PageSize);
