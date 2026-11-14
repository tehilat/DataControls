using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using DataControls.Application.Abstractions;
using DataControls.Application.Results;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Domain.Metadata;
using DataControls.Infrastructure.Sql;
using Microsoft.Data.SqlClient;

namespace DataControls.Infrastructure.Persistence;

/// <summary>Run history (ctl.ControlRuns) and stored results (ctl.ControlRunViolations).</summary>
public sealed class SqlControlRunRepository(ISqlConnectionFactory db) : IControlRunRepository
{
    public async Task<int> StartAsync(int controlId, string generatedSql, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO ctl.ControlRuns (ControlId, Status, GeneratedSql)
            OUTPUT INSERTED.RunId
            VALUES (@controlId, @status, @generatedSql);
            """,
            new { controlId, status = ControlRunStatus.Running.ToString(), generatedSql },
            cancellationToken: ct));
    }

    public async Task CompleteAsync(int runId, ControlRunStatus status, int? violationCount, long durationMs,
        string? errorMessage, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ctl.ControlRuns
            SET FinishedAt = SYSDATETIME(), Status = @status, ViolationCount = @violationCount,
                DurationMs = @durationMs, ErrorMessage = @errorMessage
            WHERE RunId = @runId;
            """,
            new
            {
                runId,
                status = status.ToString(),
                violationCount,
                durationMs = (int)Math.Min(durationMs, int.MaxValue),
                errorMessage
            },
            cancellationToken: ct));
    }

    public async Task SaveResultsAsync(int runId, IReadOnlyList<ResultColumn> columns, IReadOnlyList<StoredViolation> violations,
        CancellationToken ct = default)
    {
        await using var connection = db.Create();
        await connection.OpenAsync(ct);

        // No surrounding transaction on purpose: holding a lock on the ControlRuns row while the bulk insert
        // checks its FK against ControlRuns deadlocks concurrent runs. The bulk insert is atomic by itself
        // (internal transaction); the run is only marked Succeeded after both steps.
        if (violations.Count > 0)
        {
            using var table = new DataTable();
            table.Columns.Add("RunId", typeof(int));
            table.Columns.Add("RowNumber", typeof(int));
            table.Columns.Add("RecordId", typeof(string));
            table.Columns.Add("DisplayValue", typeof(string));
            table.Columns.Add("ValuesJson", typeof(string));
            table.Columns.Add("Reason", typeof(string));
            table.Columns.Add("SearchText", typeof(string));
            foreach (var v in violations)
                table.Rows.Add(runId, v.RowNumber, (object?)v.RecordId ?? DBNull.Value, (object?)v.DisplayValue ?? DBNull.Value,
                    v.ValuesJson, v.Reason, v.SearchText);

            using var bulk = new SqlBulkCopy(connection,
                SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.UseInternalTransaction, externalTransaction: null)
            {
                DestinationTableName = "ctl.ControlRunViolations"
            };
            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            // rows arrive in clustered-key order: no sort, so no large memory grant (matters on SQL Express)
            bulk.ColumnOrderHints.Add("RunId", SortOrder.Ascending);
            bulk.ColumnOrderHints.Add("RowNumber", SortOrder.Ascending);
            await bulk.WriteToServerAsync(table, ct);
        }

        var columnsParameter = new DynamicParameters();
        columnsParameter.Add("runId", runId);
        columnsParameter.Add("columnsJson", RunColumnsJson.Serialize(columns), DbType.String, size: -1);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ctl.ControlRuns SET ColumnsJson = @columnsJson WHERE RunId = @runId;",
            columnsParameter, cancellationToken: ct));
    }

    private const string RunColumns = """
        r.RunId, r.ControlId, c.Name AS ControlName, r.StartedAt, r.FinishedAt, r.Status,
        r.ViolationCount, r.DurationMs, r.ErrorMessage,
        (SELECT COUNT(*) FROM ctl.ControlRunViolations v WHERE v.RunId = r.RunId) AS StoredCount
        """;

    public async Task<ControlRunInfo?> GetAsync(int runId, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        var row = await connection.QuerySingleOrDefaultAsync<RunRow>(new CommandDefinition($"""
            SELECT {RunColumns}, r.ColumnsJson
            FROM ctl.ControlRuns r JOIN ctl.Controls c ON c.ControlId = r.ControlId
            WHERE r.RunId = @runId;
            """, new { runId }, cancellationToken: ct));
        return row?.ToInfo();
    }

    public async Task<PagedResult<ControlRunInfo>> ListAsync(RunListQuery query, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("controlId", query.ControlId);
        parameters.Add("status", query.Status?.ToString(), DbType.String, size: 20);
        parameters.Add("skip", (query.Page - 1) * query.PageSize);
        parameters.Add("take", query.PageSize);

        const string where = """
            WHERE (@controlId IS NULL OR r.ControlId = @controlId) AND (@status IS NULL OR r.Status = @status)
            """;

        await using var connection = db.Create();
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT(*) FROM ctl.ControlRuns r {where};
            SELECT {RunColumns}, CAST(NULL AS NVARCHAR(MAX)) AS ColumnsJson
            FROM ctl.ControlRuns r JOIN ctl.Controls c ON c.ControlId = r.ControlId
            {where}
            ORDER BY r.RunId DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, parameters, cancellationToken: ct));

        var total = await reader.ReadSingleAsync<int>();
        var items = (await reader.ReadAsync<RunRow>()).Select(r => r.ToInfo()).ToList();
        return new PagedResult<ControlRunInfo>(items, query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<StoredViolation>> GetViolationsAsync(int runId, ViolationQuery query, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("runId", runId);
        parameters.Add("skip", (query.Page - 1) * query.PageSize);
        parameters.Add("take", query.PageSize);

        var where = new StringBuilder("WHERE v.RunId = @runId");
        if (query.Search is { } search)
        {
            parameters.Add("search", SqlText.Contains(search), DbType.String, size: 4000);
            where.Append(" AND v.SearchText LIKE @search ESCAPE N'\\'");
        }

        for (var i = 0; i < query.Filters.Count; i++)
        {
            var filter = query.Filters[i];
            var pathParameter = $"@fp{i}";
            var valueParameter = $"@fv{i}";
            parameters.Add(pathParameter, TypedJsonValue.Path(filter.ColumnKey), DbType.String, size: 200);
            TypedJsonValue.AddValueParameter(parameters, valueParameter, filter.DataType, filter.Value);
            where.Append(" AND ").Append(TypedJsonValue.Expression(filter.DataType, pathParameter)).Append(" = ").Append(valueParameter);
        }

        var orderBy = "v.RowNumber";
        if (query.Sort is { } sort)
        {
            parameters.Add("@sp", TypedJsonValue.Path(sort.ColumnKey), DbType.String, size: 200);
            orderBy = TypedJsonValue.Expression(sort.DataType, "@sp") + (sort.Descending ? " DESC" : " ASC") + ", v.RowNumber";
        }

        await using var connection = db.Create();
        await using var reader = await connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT(*) FROM ctl.ControlRunViolations v {where};
            SELECT v.RowNumber, v.RecordId, v.DisplayValue, v.ValuesJson, v.Reason, CAST(N'' AS NVARCHAR(1)) AS SearchText
            FROM ctl.ControlRunViolations v
            {where}
            ORDER BY {orderBy}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, parameters, cancellationToken: ct));

        var total = await reader.ReadSingleAsync<int>();
        var items = (await reader.ReadAsync<ViolationRow>())
            .Select(r => new StoredViolation(r.RowNumber, r.RecordId, r.DisplayValue, r.ValuesJson, r.Reason, ""))
            .ToList();
        return new PagedResult<StoredViolation>(items, query.Page, query.PageSize, total);
    }

    private sealed class RunRow
    {
        public int RunId { get; init; }
        public int ControlId { get; init; }
        public string ControlName { get; init; } = "";
        public DateTime StartedAt { get; init; }
        public DateTime? FinishedAt { get; init; }
        public string Status { get; init; } = "";
        public int? ViolationCount { get; init; }
        public int? DurationMs { get; init; }
        public string? ErrorMessage { get; init; }
        public int StoredCount { get; init; }
        public string? ColumnsJson { get; init; }

        public ControlRunInfo ToInfo() => new(RunId, ControlId, ControlName, StartedAt, FinishedAt,
            Enum.Parse<ControlRunStatus>(Status, ignoreCase: true), ViolationCount, DurationMs, ErrorMessage,
            StoredCount, RunColumnsJson.Parse(ColumnsJson));
    }

    private sealed class ViolationRow
    {
        public int RowNumber { get; init; }
        public string? RecordId { get; init; }
        public string? DisplayValue { get; init; }
        public string ValuesJson { get; init; } = "{}";
        public string Reason { get; init; } = "";
    }
}

/// <summary>
/// Typed access to a stored value. The SQL expression is chosen from a FIXED set of constants by the
/// column's data type; the JSON path and the compared value are always parameters — no dynamic SQL from input.
/// </summary>
internal static class TypedJsonValue
{
    private const string NumberExpression = "TRY_CONVERT(decimal(38,10), JSON_VALUE(v.ValuesJson, {0}))";
    private const string DateExpression = "TRY_CONVERT(date, JSON_VALUE(v.ValuesJson, {0}), 23)";
    private const string DateTimeExpression = "TRY_CONVERT(datetime2(7), JSON_VALUE(v.ValuesJson, {0}), 126)";
    private const string TextExpression = "JSON_VALUE(v.ValuesJson, {0})";

    /// <param name="parameterName">A parameter name generated by the repository (never input).</param>
    public static string Expression(FieldDataType type, string parameterName)
    {
        var template = type switch
        {
            FieldDataType.Number => NumberExpression,
            FieldDataType.Date => DateExpression,
            FieldDataType.DateTime => DateTimeExpression,
            _ => TextExpression
        };
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, template, parameterName);
    }

    /// <summary>JSON path of a stored column. The key must be a plain identifier (defense in depth).</summary>
    public static string Path(string columnKey) =>
        SqlIdentifier.IsValid(columnKey)
            ? $"$.\"{columnKey}\""
            : throw new InvalidOperationException($"Unsafe result column key rejected: '{columnKey}'.");

    public static void AddValueParameter(DynamicParameters parameters, string name, FieldDataType type, object value)
    {
        switch (type)
        {
            case FieldDataType.Number:
                parameters.Add(name, value, DbType.Decimal, precision: 38, scale: 10);
                break;
            case FieldDataType.Date:
                parameters.Add(name, value, DbType.Date);
                break;
            case FieldDataType.DateTime:
                parameters.Add(name, value, DbType.DateTime2);
                break;
            default:
                parameters.Add(name, value, DbType.String, size: 4000);
                break;
        }
    }
}

/// <summary>The column snapshot of a run (ctl.ControlRuns.ColumnsJson). Data types use the meta keys.</summary>
internal static class RunColumnsJson
{
    private sealed record Column(string Key, string DisplayName, string DataType, string? Unit);

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<ResultColumn> columns) =>
        JsonSerializer.Serialize(columns.Select(c => new Column(c.Key, c.DisplayName, c.DataType.ToKey(), c.Unit)), Options);

    public static IReadOnlyList<ResultColumn> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return (JsonSerializer.Deserialize<List<Column>>(json, Options) ?? [])
            .Where(c => FieldDataTypes.TryParse(c.DataType, out _))
            .Select(c =>
            {
                FieldDataTypes.TryParse(c.DataType, out var type);
                return new ResultColumn(c.Key, c.DisplayName, type, c.Unit);
            })
            .ToList();
    }
}
