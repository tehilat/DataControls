using System.Globalization;
using System.Text.Json;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;

namespace DataControls.Api.Contracts;

/// <summary>A result column (a field of the root entity).</summary>
/// <param name="Key">The key of the value in each record's "values".</param>
/// <param name="DataType">number | text | date | datetime | boolean | enum</param>
public sealed record ColumnDto(string Key, string DisplayName, string DataType, string? Unit)
{
    public static ColumnDto From(ResultColumn c) => new(c.Key, c.DisplayName, c.DataType.ToKey(), c.Unit);
}

/// <summary>A recorded run. The generated SQL is intentionally not exposed.</summary>
/// <param name="ViolationCount">The real number of violations (also when only part of them was stored).</param>
/// <param name="StoredCount">How many violations were stored (limited by the row limit).</param>
public sealed record RunDto(
    int RunId,
    int ControlId,
    string ControlName,
    DateTime StartedAt,
    DateTime? FinishedAt,
    ControlRunStatus Status,
    int? ViolationCount,
    int StoredCount,
    bool Truncated,
    int? DurationMs,
    string? ErrorMessage,
    IReadOnlyList<ColumnDto> Columns)
{
    public static RunDto From(ControlRunInfo r) => new(r.RunId, r.ControlId, r.ControlName, r.StartedAt, r.FinishedAt,
        r.Status, r.ViolationCount, r.StoredCount, r.Truncated, r.DurationMs, r.ErrorMessage,
        r.Columns.Select(ColumnDto.From).ToList());
}

/// <summary>A run in the history list.</summary>
public sealed record RunListItemDto(
    int RunId,
    int ControlId,
    string ControlName,
    DateTime StartedAt,
    DateTime? FinishedAt,
    ControlRunStatus Status,
    int? ViolationCount,
    bool Truncated,
    int? DurationMs,
    string? ErrorMessage)
{
    public static RunListItemDto From(ControlRunInfo r) => new(r.RunId, r.ControlId, r.ControlName, r.StartedAt,
        r.FinishedAt, r.Status, r.ViolationCount, r.Truncated, r.DurationMs, r.ErrorMessage);
}

/// <summary>A failing record with the reason in Hebrew.</summary>
/// <param name="Values">Column key → value in canonical form: numbers as JSON numbers,
/// dates "yyyy-MM-dd", date-times "yyyy-MM-ddTHH:mm:ss.fffffff", booleans true/false, list fields by their raw value.</param>
public sealed record ViolationDto(int RowNumber, string? RecordId, string? DisplayValue, JsonElement Values, string Reason)
{
    public static ViolationDto From(StoredViolation v)
    {
        using var document = JsonDocument.Parse(v.ValuesJson);
        return new(v.RowNumber, v.RecordId, v.DisplayValue, document.RootElement.Clone(), v.Reason);
    }

    public static ViolationDto From(int rowNumber, ViolationRecord v, IReadOnlyList<ResultColumn> columns)
    {
        using var document = JsonDocument.Parse(CanonicalValueJson.Serialize(v.Values, columns));
        return new(rowNumber, Convert.ToString(v.Id, CultureInfo.InvariantCulture), v.DisplayValue,
            document.RootElement.Clone(), v.Reason);
    }
}

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages)
{
    public static PageDto<T> From<TSource>(PagedResult<TSource> page, Func<TSource, T> map) =>
        new(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);
}

/// <summary>The result of running an unsaved definition. Nothing is recorded.</summary>
public sealed record PreviewDto(
    string RootEntity,
    string RootEntityDisplayNamePlural,
    int TotalCount,
    bool Truncated,
    long DurationMs,
    IReadOnlyList<ColumnDto> Columns,
    IReadOnlyList<ViolationDto> Items)
{
    public static PreviewDto From(ControlRunResult r) => new(r.RootEntityKey, r.RootEntityDisplayName, r.TotalCount,
        r.Truncated, r.DurationMs, r.Columns.Select(ColumnDto.From).ToList(),
        r.Violations.Select((v, i) => ViolationDto.From(i + 1, v, r.Columns)).ToList());
}
