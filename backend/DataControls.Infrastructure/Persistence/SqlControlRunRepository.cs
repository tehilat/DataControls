using Dapper;
using DataControls.Application.Abstractions;
using DataControls.Domain.Controls;
using DataControls.Infrastructure.Sql;

namespace DataControls.Infrastructure.Persistence;

/// <summary>Run history in ctl.ControlRuns.</summary>
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
}
