using System.Globalization;
using DataControls.Application.Abstractions;
using DataControls.Application.Errors;
using DataControls.Application.Results;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;

namespace DataControls.Application.Runs;

/// <summary>Raw query-string input for the results of a run; validated by <see cref="RunService"/>.</summary>
public sealed record ViolationRequest(
    string? Search,
    IReadOnlyDictionary<string, string> Filters,
    string? SortBy,
    string? SortDir,
    int Page,
    int PageSize);

public interface IRunService
{
    Task<ControlRunInfo> GetAsync(int runId, CancellationToken ct = default);
    Task<PagedResult<ControlRunInfo>> ListAsync(int? controlId, ControlRunStatus? status, int page, int pageSize,
        CancellationToken ct = default);
    Task<PagedResult<StoredViolation>> GetViolationsAsync(int runId, ViolationRequest request, CancellationToken ct = default);
}

/// <summary>
/// Run history and stored results. Filter / sort keys are accepted only if they are columns of the run's
/// snapshot, and filter values are parsed by the column's data type here — so the repository receives typed values only.
/// </summary>
public sealed class RunService(IControlRunRepository runs) : IRunService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxSearchLength = 200;

    public async Task<ControlRunInfo> GetAsync(int runId, CancellationToken ct = default) =>
        await runs.GetAsync(runId, ct) ?? throw new RunNotFoundException(runId);

    public Task<PagedResult<ControlRunInfo>> ListAsync(int? controlId, ControlRunStatus? status, int page, int pageSize,
        CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        return runs.ListAsync(new RunListQuery(controlId, status, page, pageSize), ct);
    }

    public async Task<PagedResult<StoredViolation>> GetViolationsAsync(int runId, ViolationRequest request,
        CancellationToken ct = default)
    {
        ValidatePaging(request.Page, request.PageSize);
        var run = await GetAsync(runId, ct);

        var errors = new List<ValidationError>();
        var columns = run.Columns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is { Length: > MaxSearchLength })
            errors.Add(new ValidationError("search", $"טקסט החיפוש ארוך מדי (עד {MaxSearchLength} תווים)."));

        var filters = new List<ViolationFilter>();
        foreach (var (key, raw) in request.Filters)
        {
            var path = $"filter[{Clip(key)}]";
            if (!columns.TryGetValue(key, out var column))
            {
                errors.Add(new ValidationError(path, $"לא ניתן לסנן לפי \"{Clip(key)}\": זה אינו שדה בתוצאות ההרצה."));
                continue;
            }
            if (TryParse(raw, column, out var value, out var message))
                filters.Add(new ViolationFilter(column.Key, column.DataType, value!));
            else
                errors.Add(new ValidationError(path, message!));
        }

        var descending = IsDescending(request.SortDir, errors);
        ViolationSort? sort = null;
        if (!string.IsNullOrWhiteSpace(request.SortBy))
        {
            if (columns.TryGetValue(request.SortBy, out var column))
                sort = new ViolationSort(column.Key, column.DataType, descending);
            else
                errors.Add(new ValidationError("sortBy", $"לא ניתן למיין לפי \"{Clip(request.SortBy)}\": זה אינו שדה בתוצאות ההרצה."));
        }

        if (errors.Count > 0) throw new InvalidRequestException(errors);

        return await runs.GetViolationsAsync(runId,
            new ViolationQuery(search, filters, sort, request.Page, request.PageSize), ct);
    }

    private static bool IsDescending(string? sortDir, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(sortDir) || sortDir.Equals("asc", StringComparison.OrdinalIgnoreCase)) return false;
        if (sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase)) return true;
        errors.Add(new ValidationError("sortDir", "כיוון המיון חייב להיות asc או desc."));
        return false;
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        var errors = new List<ValidationError>();
        if (page < 1) errors.Add(new ValidationError("page", "מספר העמוד חייב להיות 1 או יותר."));
        if (pageSize is < 1 or > MaxPageSize)
            errors.Add(new ValidationError("pageSize", $"גודל העמוד חייב להיות בין 1 ל-{MaxPageSize}."));
        if (errors.Count > 0) throw new InvalidRequestException(errors);
    }

    /// <summary>Parses a filter value in the canonical (culture-independent) format of the column's type.</summary>
    public static bool TryParse(string? raw, ResultColumn column, out object? value, out string? error)
    {
        value = null;
        error = null;
        var text = raw?.Trim() ?? "";
        var name = column.DisplayName;

        switch (column.DataType)
        {
            case FieldDataType.Number:
                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                {
                    value = number;
                    return true;
                }
                error = $"ערך הסינון לשדה \"{name}\" חייב להיות מספר (למשל 12.5).";
                return false;

            case FieldDataType.Date:
                if (DateTime.TryParseExact(text, CanonicalValueJson.DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    value = date;
                    return true;
                }
                error = $"ערך הסינון לשדה \"{name}\" חייב להיות תאריך בפורמט yyyy-MM-dd.";
                return false;

            case FieldDataType.DateTime:
                if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dateTime))
                {
                    value = dateTime;
                    return true;
                }
                error = $"ערך הסינון לשדה \"{name}\" חייב להיות תאריך ושעה בפורמט yyyy-MM-ddTHH:mm:ss.";
                return false;

            case FieldDataType.Boolean:
                if (bool.TryParse(text, out var flag))
                {
                    value = flag ? "true" : "false"; // compared with JSON_VALUE, which returns "true" / "false"
                    return true;
                }
                error = $"ערך הסינון לשדה \"{name}\" חייב להיות true או false.";
                return false;

            default: // text / enum: exact match on the stored (raw) value
                if (text.Length is 0 or > 4000)
                {
                    error = $"ערך הסינון לשדה \"{name}\" חייב להכיל בין 1 ל-4000 תווים.";
                    return false;
                }
                value = text;
                return true;
        }
    }

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd"
    ];

    private static string Clip(string text) => text.Length > 50 ? text[..50] : text;
}
