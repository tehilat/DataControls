using Dapper;
using DataControls.Application.Abstractions;
using DataControls.Application.Errors;
using DataControls.Application.Runs;
using DataControls.Domain.Controls;
using DataControls.Infrastructure.Sql;
using Microsoft.Data.SqlClient;

namespace DataControls.Infrastructure.Persistence;

/// <summary>ctl.Controls (the system's own table, not business data).</summary>
public sealed class SqlControlRepository(ISqlConnectionFactory db) : IControlRepository
{
    private const string SelectColumns = """
        SELECT c.ControlId, c.Name, c.Description, c.RootEntityKey, c.DefinitionJson, c.Severity, c.Source,
               c.IsActive, c.CreatedAt, c.UpdatedAt, c.RowVer
        FROM ctl.Controls c
        """;

    public async Task<Control?> GetByIdAsync(int controlId, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        var row = await connection.QuerySingleOrDefaultAsync<ControlRow>(
            new CommandDefinition(SelectColumns + " WHERE c.ControlId = @controlId;", new { controlId }, cancellationToken: ct));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Control>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = db.Create();
        var rows = await connection.QueryAsync<ControlRow>(
            new CommandDefinition(SelectColumns + " ORDER BY c.ControlId;", cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ControlListItem>> ListAsync(ControlListFilter filter, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.ControlId, c.Name, c.Description, c.RootEntityKey, c.DefinitionJson, c.Severity, c.Source,
                   c.IsActive, c.CreatedAt, c.UpdatedAt, c.RowVer,
                   lr.RunId AS LastRunId, lr.StartedAt AS LastRunStartedAt, lr.Status AS LastRunStatus,
                   lr.ViolationCount AS LastRunViolationCount
            FROM ctl.Controls c
            OUTER APPLY (SELECT TOP (1) r.RunId, r.StartedAt, r.Status, r.ViolationCount
                         FROM ctl.ControlRuns r WHERE r.ControlId = c.ControlId
                         ORDER BY r.RunId DESC) lr
            WHERE (@search IS NULL OR c.Name LIKE @search ESCAPE N'\' OR c.Description LIKE @search ESCAPE N'\')
              AND (@rootEntityKey IS NULL OR c.RootEntityKey = @rootEntityKey)
              AND (@isActive IS NULL OR c.IsActive = @isActive)
              AND (@source IS NULL OR c.Source = @source)
            ORDER BY c.Name;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("search", string.IsNullOrWhiteSpace(filter.Search) ? null : SqlText.Contains(filter.Search.Trim()),
            System.Data.DbType.String, size: 4000);
        parameters.Add("rootEntityKey", filter.RootEntityKey, System.Data.DbType.String, size: 50);
        parameters.Add("isActive", filter.IsActive, System.Data.DbType.Boolean);
        parameters.Add("source", filter.Source?.ToString(), System.Data.DbType.String, size: 10);

        await using var connection = db.Create();
        var rows = await connection.QueryAsync<ControlListRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.Select(r => new ControlListItem(r.ToDomain(),
            r.LastRunId is { } runId
                ? new RunSummary(runId, r.LastRunStartedAt!.Value, Enum.Parse<ControlRunStatus>(r.LastRunStatus!), r.LastRunViolationCount)
                : null)).ToList();
    }

    public async Task<int> CreateAsync(ControlDraft draft, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO ctl.Controls (Name, Description, RootEntityKey, DefinitionJson, Severity, Source, IsActive)
            OUTPUT INSERTED.ControlId
            VALUES (@Name, @Description, @RootEntityKey, @DefinitionJson, @Severity, @Source, @IsActive);
            """;
        await using var connection = db.Create();
        try
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, Parameters(draft), cancellationToken: ct));
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            throw DuplicateName(draft.Name);
        }
    }

    public async Task<UpdateOutcome> UpdateAsync(int controlId, ControlDraft draft, byte[] rowVersion, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE ctl.Controls
            SET Name = @Name, Description = @Description, RootEntityKey = @RootEntityKey, DefinitionJson = @DefinitionJson,
                Severity = @Severity, Source = @Source, IsActive = @IsActive, UpdatedAt = SYSDATETIME()
            WHERE ControlId = @ControlId AND RowVer = @RowVersion;
            DECLARE @updated INT = @@ROWCOUNT;
            SELECT @updated AS Updated, (SELECT COUNT(*) FROM ctl.Controls WHERE ControlId = @ControlId) AS Existing;
            """;
        var parameters = Parameters(draft);
        parameters.Add("ControlId", controlId);
        parameters.Add("RowVersion", rowVersion, System.Data.DbType.Binary, size: 8);

        await using var connection = db.Create();
        try
        {
            var (updated, existing) = await connection.QuerySingleAsync<(int, int)>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
            return updated > 0 ? UpdateOutcome.Updated : existing == 0 ? UpdateOutcome.NotFound : UpdateOutcome.Conflict;
        }
        catch (SqlException ex) when (IsUniqueViolation(ex))
        {
            throw DuplicateName(draft.Name);
        }
    }

    public async Task<bool> DeleteAsync(int controlId, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        try
        {
            return await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM ctl.Controls WHERE ControlId = @controlId;", new { controlId }, cancellationToken: ct)) > 0;
        }
        catch (SqlException ex) when (ex.Number == 547) // FK: a run was recorded meanwhile
        {
            throw new ConflictException("לא ניתן למחוק את הבקרה כי יש לה היסטוריית הרצות. ניתן להשבית אותה במקום.");
        }
    }

    public async Task<bool> SetActiveAsync(int controlId, bool isActive, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ctl.Controls SET IsActive = @isActive, UpdatedAt = SYSDATETIME() WHERE ControlId = @controlId;
            """, new { controlId, isActive }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeControlId, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("name", name, System.Data.DbType.String, size: 200);
        parameters.Add("excludeControlId", excludeControlId);

        await using var connection = db.Create();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM ctl.Controls
                WHERE Name = @name AND (@excludeControlId IS NULL OR ControlId <> @excludeControlId)) THEN 1 ELSE 0 END AS BIT);
            """, parameters, cancellationToken: ct));
    }

    public async Task<bool> HasRunsAsync(int controlId, CancellationToken ct = default)
    {
        await using var connection = db.Create();
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM ctl.ControlRuns WHERE ControlId = @controlId) THEN 1 ELSE 0 END AS BIT);
            """, new { controlId }, cancellationToken: ct));
    }

    private static DynamicParameters Parameters(ControlDraft draft)
    {
        var p = new DynamicParameters();
        p.Add("Name", draft.Name, System.Data.DbType.String, size: 200);
        p.Add("Description", draft.Description, System.Data.DbType.String, size: 1000);
        p.Add("RootEntityKey", draft.RootEntityKey, System.Data.DbType.String, size: 50);
        p.Add("DefinitionJson", draft.DefinitionJson, System.Data.DbType.String, size: -1);
        p.Add("Severity", draft.Severity.ToString(), System.Data.DbType.String, size: 10);
        p.Add("Source", draft.Source.ToString(), System.Data.DbType.String, size: 10);
        p.Add("IsActive", draft.IsActive);
        return p;
    }

    private static bool IsUniqueViolation(SqlException ex) => ex.Number is 2601 or 2627;

    private static ConflictException DuplicateName(string name) =>
        new($"כבר קיימת בקרה בשם \"{name}\".", "name");

    private class ControlRow
    {
        public int ControlId { get; init; }
        public string Name { get; init; } = "";
        public string? Description { get; init; }
        public string RootEntityKey { get; init; } = "";
        public string DefinitionJson { get; init; } = "";
        public string Severity { get; init; } = "";
        public string Source { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public byte[] RowVer { get; init; } = [];

        public Control ToDomain() => new()
        {
            ControlId = ControlId,
            Name = Name,
            Description = Description,
            RootEntityKey = RootEntityKey,
            DefinitionJson = DefinitionJson,
            Severity = Enum.Parse<ControlSeverity>(Severity, ignoreCase: true),
            Source = Enum.Parse<ControlSource>(Source, ignoreCase: true),
            IsActive = IsActive,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            RowVersion = RowVer
        };
    }

    private sealed class ControlListRow : ControlRow
    {
        public int? LastRunId { get; init; }
        public DateTime? LastRunStartedAt { get; init; }
        public string? LastRunStatus { get; init; }
        public int? LastRunViolationCount { get; init; }
    }
}
